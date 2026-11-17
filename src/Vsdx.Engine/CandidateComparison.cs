using Vsdx.Model;

namespace Vsdx.Engine;

/// <summary>
/// One generated variant of a diagram: a different layout strategy, direction, spacing or theme,
/// rendered and measured so variants can be compared rather than guessed between.
/// </summary>
public sealed class LayoutCandidate
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required Diagram Model { get; init; }
    public required LayoutReport Report { get; init; }

    /// <summary>How this variant was produced, for the comparison table.</summary>
    public required CandidateRecipe Recipe { get; init; }

    public bool Deliverable => Report.HardConstraintsPassed;
    public int Score => Report.Score;
    public int HardViolations => Report.Errors.Count();
    public int SoftViolations => Report.Warnings.Count();
}

/// <summary>The knobs a candidate varies. Kept explicit so a comparison is reproducible.</summary>
public sealed record CandidateRecipe
{
    public LayoutStrategy Strategy { get; init; } = LayoutStrategy.Layered;
    public LayoutDirection Direction { get; init; } = LayoutDirection.LeftToRight;
    public double NodeGapX { get; init; } = 0.55;
    public double BandGapY { get; init; } = 0.85;
    public bool FitToText { get; init; } = true;
    public bool Transpose { get; init; }

    public string Describe() =>
        $"{Strategy}/{Direction}" +
        (Math.Abs(NodeGapX - 0.55) > 0.01 ? $" gap={NodeGapX:F2}" : "") +
        (Math.Abs(BandGapY - 0.85) > 0.01 ? $" band={BandGapY:F2}" : "") +
        (Transpose ? " transposed" : "");
}

/// <summary>
/// Generates several candidate layouts for the same model, filters them by hard constraints and
/// ranks the survivors by soft quality.
///
/// Brief §4.8 asks for candidates produced in isolation so a failed attempt cannot damage the
/// current good version. Every candidate here is built from a deep copy of the input.
/// </summary>
public static class CandidateComparison
{
    /// <summary>
    /// Builds the default candidate set: the semantic strategy, an alternative strategy, two
    /// spacing variants, and a transposed attempt for wide content.
    /// </summary>
    public static IReadOnlyList<CandidateRecipe> DefaultRecipes(Diagram diagram)
    {
        var suggested = DiagramLayoutEngine.SuggestStrategy(diagram);
        var recipes = new List<CandidateRecipe>
        {
            new() { Strategy = suggested },
            new() { Strategy = suggested, NodeGapX = 0.40, BandGapY = 0.65 },
            new() { Strategy = suggested, NodeGapX = 0.80, BandGapY = 1.10 }
        };

        // An alternative strategy is only interesting when it is genuinely different.
        foreach (var alternative in new[] { LayoutStrategy.Flow, LayoutStrategy.Layered, LayoutStrategy.Grid })
        {
            if (alternative == suggested) continue;
            recipes.Add(new CandidateRecipe { Strategy = alternative });
            break;
        }

        // Wide, shallow content often reads better rotated.
        var page = diagram.Pages.FirstOrDefault();
        if (page?.Size is { Width: > 0 } size && size.Width / Math.Max(size.Height, 0.1) > 1.7)
        {
            recipes.Add(new CandidateRecipe
            {
                Strategy = LayoutStrategy.Grid,
                Transpose = true,
                NodeGapX = 0.45
            });
        }

        return recipes;
    }

    /// <summary>
    /// Produces and measures every candidate. Each is generated from a copy, so the caller's
    /// model is never touched.
    /// </summary>
    public static CandidateSet Generate(
        Diagram source, IEnumerable<CandidateRecipe>? recipes = null)
    {
        recipes ??= DefaultRecipes(source);
        var set = new CandidateSet();
        int index = 1;

        foreach (var recipe in recipes)
        {
            var candidateModel = Clone(source);

            if (recipe.FitToText) DiagramSizer.Apply(candidateModel);

            DiagramLayoutEngine.Apply(
                candidateModel,
                new LayoutOptions
                {
                    NodeGapX = recipe.NodeGapX,
                    BandGapY = recipe.BandGapY,
                    Direction = recipe.Direction
                },
                recipe.Strategy);

            if (recipe.Transpose) Transpose(candidateModel);

            DiagramLayout.Apply(candidateModel);
            OrthogonalRouter.Route(candidateModel);

            foreach (var page in candidateModel.Pages)
            {
                DiagramSizer.Recenter(page);
                DiagramSizer.FitPage(page, shrink: true);
            }

            var report = LayoutAnalyzer.Analyze(candidateModel);
            set.Candidates.Add(new LayoutCandidate
            {
                Id = $"c{index++}",
                Description = recipe.Describe(),
                Model = candidateModel,
                Report = report,
                Recipe = recipe
            });
        }

        return set;
    }

    /// <summary>Swaps X and Y so a wide layout becomes tall.</summary>
    private static void Transpose(Diagram diagram)
    {
        foreach (var page in diagram.Pages)
        {
            foreach (var node in page.Nodes)
            {
                (node.Bounds.X, node.Bounds.Y) = (node.Bounds.Y, node.Bounds.X);
                (node.Bounds.W, node.Bounds.H) = (node.Bounds.H, node.Bounds.W);
            }

            foreach (var group in page.Groups.Where(g => g.Bounds is not null))
            {
                (group.Bounds!.X, group.Bounds.Y) = (group.Bounds.Y, group.Bounds.X);
                (group.Bounds.W, group.Bounds.H) = (group.Bounds.H, group.Bounds.W);
            }

            // Existing waypoints describe the untransposed geometry, so drop them and re-route.
            foreach (var edge in page.Edges) edge.Waypoints = null;

            if (page.Size is not null)
                (page.Size.Width, page.Size.Height) = (page.Size.Height, page.Size.Width);
        }
    }

    private static Diagram Clone(Diagram source) =>
        DiagramJson.Deserialize(DiagramJson.Serialize(source));
}

/// <summary>The candidate set with hard filtering and soft ranking applied.</summary>
public sealed class CandidateSet
{
    public List<LayoutCandidate> Candidates { get; } = new();

    /// <summary>Candidates that pass every hard constraint, best first.</summary>
    public IReadOnlyList<LayoutCandidate> Deliverable =>
        Candidates.Where(c => c.Deliverable).OrderByDescending(c => c.Score).ToList();

    /// <summary>Candidates rejected for a hard violation, kept for the report.</summary>
    public IReadOnlyList<LayoutCandidate> Rejected =>
        Candidates.Where(c => !c.Deliverable).ToList();

    public LayoutCandidate? Best => Deliverable.FirstOrDefault();

    /// <summary>
    /// True when the best candidate is meaningfully better than the runner-up. Used to decide
    /// whether comparison was worth it, not to reject anything automatically.
    /// </summary>
    public bool HasClearWinner(int margin = 3)
    {
        var ranked = Deliverable;
        if (ranked.Count < 2) return ranked.Count == 1;
        return ranked[0].Score - ranked[1].Score >= margin;
    }

    /// <summary>Ranking table for a report; one line per candidate.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var c in Candidates.OrderByDescending(x => x.Deliverable).ThenByDescending(x => x.Score))
        {
            string verdict = c.Deliverable ? "ok" : $"rejected ({c.Report.Errors.First().Code})";
            yield return $"{c.Id,-3} {c.Description,-34} score={c.Score,3} " +
                         $"hard={c.HardViolations} soft={c.SoftViolations} {verdict}";
        }
    }
}
