using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// CandidateTests: generating several layout variants, rejecting the undeliverable ones and
/// ranking the rest. Brief §4.8 asks for candidates produced in isolation so a failed attempt
/// cannot damage the current good version.
/// </summary>
public sealed class CandidateTests
{
    private static Diagram Sample()
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 16, Height = 10 },
            Groups =
            {
                new Group { Id = "g1", Name = "Upper", Members = { "a", "b" } },
                new Group { Id = "g2", Name = "Lower", Members = { "c" } }
            },
            Nodes =
            {
                new Node { Id = "a", Text = "Alpha", Bounds = new Bounds(3, 8, 2, 0.9), GroupId = "g1" },
                new Node { Id = "b", Text = "Beta", Bounds = new Bounds(6, 8, 2, 0.9), GroupId = "g1" },
                new Node { Id = "c", Text = "Gamma", Bounds = new Bounds(4, 3, 2, 0.9), GroupId = "g2" }
            },
            Edges = { new Edge { Id = "e1", From = "a", To = "c", Kind = "straight" } }
        };

        return new Diagram { Type = "architecture", PageSize = page.Size, Pages = { page } };
    }

    [Fact]
    public void Compare_ProducesSeveralCandidates()
    {
        var set = CandidateComparison.Generate(Sample());

        Assert.True(set.Candidates.Count >= 3, "comparison should offer more than one variant");
    }

    [Fact]
    public void Compare_NeverMutatesTheSource()
    {
        var source = Sample();
        var before = DiagramJson.Serialize(source);

        CandidateComparison.Generate(source);

        Assert.Equal(before, DiagramJson.Serialize(source));
    }

    [Fact]
    public void Compare_RanksDeliverableFirst()
    {
        var set = CandidateComparison.Generate(Sample());
        var ranked = set.Deliverable;

        // Ordered best-first and all of them actually pass.
        for (int i = 0; i < ranked.Count; i++)
        {
            Assert.True(ranked[i].Deliverable);
            if (i > 0) Assert.True(ranked[i - 1].Score >= ranked[i].Score);
        }
    }

    [Fact]
    public void Compare_RejectsUndeliverableCandidates()
    {
        var set = CandidateComparison.Generate(Sample());

        Assert.All(set.Rejected, c => Assert.False(c.Deliverable));
        Assert.All(set.Deliverable, c => Assert.True(c.Deliverable));
        // Every candidate appears in exactly one bucket.
        Assert.Equal(set.Candidates.Count, set.Deliverable.Count + set.Rejected.Count);
    }

    [Fact]
    public void Compare_FindsAtLeastOneDeliverable()
    {
        var set = CandidateComparison.Generate(Sample());
        Assert.NotNull(set.Best);
    }

    [Fact]
    public void Compare_SpacingVariantsActuallyDiffer()
    {
        var set = CandidateComparison.Generate(Sample());

        var tight = set.Candidates.FirstOrDefault(c => Math.Abs(c.Recipe.NodeGapX - 0.40) < 0.01);
        var loose = set.Candidates.FirstOrDefault(c => Math.Abs(c.Recipe.NodeGapX - 0.80) < 0.01);

        if (tight is null || loose is null) return;   // recipe set changed; nothing to compare

        double tightSpan = ContentWidth(tight.Model);
        double looseSpan = ContentWidth(loose.Model);
        Assert.True(looseSpan > tightSpan, "wider spacing must produce a wider drawing");
    }

    [Fact]
    public void Compare_TransposedCandidateIsStillDeliverable()
    {
        // Transposition is offered as a *variant*, not as a guaranteed improvement: the judge is
        // the quality report, not the axis swap. What must hold is that the variant is a valid
        // diagram in its own right.
        var wide = Sample();
        // Sized so the transposed variant still fits. Containers reserve heading clearance, so the
        // short side has to be comfortably larger than two stacked containers — the assertion is
        // about the variant being deliverable, not about how tightly a sheet can be packed.
        wide.Pages[0].Size = new PageSize { Width = 24, Height = 12 };

        var recipes = new List<CandidateRecipe>
        {
            new() { Strategy = LayoutStrategy.Grid, Transpose = true }
        };

        var set = CandidateComparison.Generate(wide, recipes);

        Assert.Single(set.Candidates);
        Assert.True(set.Candidates[0].Deliverable,
            string.Join("; ", set.Candidates[0].Report.Errors.Select(e => e.Code)));
    }

    [Fact]
    public void Compare_BestIsNeverWorseThanAnyDeliverable()
    {
        var set = CandidateComparison.Generate(Sample());
        var best = set.Best;
        Assert.NotNull(best);
        Assert.All(set.Deliverable, c => Assert.True(c.Score <= best!.Score));
    }

    [Fact]
    public void Compare_HasClearWinnerBehavesSensibly()
    {
        var set = CandidateComparison.Generate(Sample());

        // With one deliverable candidate it is trivially the winner.
        if (set.Deliverable.Count <= 1)
        {
            Assert.True(set.HasClearWinner());
            return;
        }

        // Otherwise the predicate must agree with the actual margin.
        int margin = set.Deliverable[0].Score - set.Deliverable[1].Score;
        Assert.Equal(margin >= 3, set.HasClearWinner());
    }

    [Fact]
    public void Compare_DescribesEveryCandidate()
    {
        var set = CandidateComparison.Generate(Sample());
        var lines = set.Describe().ToList();

        Assert.Equal(set.Candidates.Count, lines.Count);
        Assert.All(lines, l => Assert.Contains("score=", l));
    }

    [Fact]
    public void Compare_CustomRecipesAreHonoured()
    {
        var recipes = new[] { new CandidateRecipe { Strategy = LayoutStrategy.Layered, NodeGapX = 0.9 } };

        var set = CandidateComparison.Generate(Sample(), recipes);

        Assert.Single(set.Candidates);
        Assert.Equal(0.9, set.Candidates[0].Recipe.NodeGapX, 3);
    }

    [Fact]
    public void Compare_BestCandidateWritesAndValidates()
    {
        var dir = TestData.NewTempDir("candidates");
        var set = CandidateComparison.Generate(Sample());
        var best = set.Best;
        Assert.NotNull(best);

        var path = Path.Combine(dir, "best.vsdx");
        var engine = new VsdxEngine();
        engine.Create(best!.Model, path);

        var validation = engine.Validate(path);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Code)));
    }

    [Fact]
    public void Compare_DefaultRecipesOfferAnAlternativeStrategy()
    {
        var recipes = CandidateComparison.DefaultRecipes(Sample());

        Assert.Contains(recipes, r => r.Strategy == DiagramLayoutEngine.SuggestStrategy(Sample()));
        Assert.True(recipes.Select(r => r.Strategy).Distinct().Count() >= 2,
            "comparison is pointless if every recipe uses the same strategy");
    }

    private static double ContentWidth(Diagram d)
    {
        var nodes = d.Pages[0].Nodes;
        return nodes.Max(n => n.Bounds.X + n.Bounds.W / 2) - nodes.Min(n => n.Bounds.X - n.Bounds.W / 2);
    }
}
