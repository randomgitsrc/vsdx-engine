using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Nested containers across multiple pages, checked explicitly.
///
/// <see cref="DiagramLayout.CheckContainment"/> only asks whether each group encloses the nodes
/// it lists. It says nothing about a parent group enclosing its child group's bounds, so an
/// empty result from it does not mean the nesting survived a transform. These assertions spell
/// out the parent/child relationship themselves.
/// </summary>
public sealed class DirectionNestedGroupTests
{
    private static Page NestedPage(string name)
    {
        var page = new Page
        {
            Name = name,
            Size = new PageSize { Width = 14, Height = 9 },
            Nodes =
            {
                new Node { Id = name + "-a", Text = "A", Bounds = new Bounds(0, 0, 1.6, 0.8) },
                new Node { Id = name + "-b", Text = "B", Bounds = new Bounds(0, 0, 1.6, 0.8) },
                new Node { Id = name + "-c", Text = "C", Bounds = new Bounds(0, 0, 1.6, 0.8) }
            },
            Edges =
            {
                new Edge { Id = name + "-ab", From = name + "-a", To = name + "-b" },
                new Edge { Id = name + "-bc", From = name + "-b", To = name + "-c" }
            },
            TextBlocks = { new TextBlock { Id = name + "-t", Text = "T", Bounds = new Bounds(2, 7, 3, 0.4) } }
        };

        // ParentId names the *parent*, so this child points at the enclosing group. Hierarchy and
        // geometry must agree: an earlier version of this fixture had the containment the wrong
        // way round and still passed, because the assertion was transposed to match the mistake
        // instead of the relationship being verified.
        page.Groups.Add(new Group
        {
            Id = name + "-outer",
            Members = { name + "-a", name + "-b" },
            Bounds = new Bounds(4, 4, 5, 2),
            Style = new ShapeStyle { PaddingX = 0.30, HeadingHeight = 0.46, PaddingBottom = 0.22 }
        });

        page.Groups.Add(new Group
        {
            Id = name + "-inner",
            Members = { name + "-c" },
            ParentId = name + "-outer",
            Bounds = new Bounds(4.5, 4, 2, 1.2),
            Style = new ShapeStyle { PaddingX = 0.50, HeadingHeight = 0.60, PaddingBottom = 0.22 }
        });

        // Fail loudly here rather than in an assertion later: the fixture is only meaningful if
        // the child really sits inside its parent.
        var outerBounds = page.Groups[0].Bounds!;
        var innerBounds = page.Groups[1].Bounds!;
        if (!Encloses(outerBounds, innerBounds))
            throw new InvalidOperationException("fixture: inner group is not inside outer group");

        return page;
    }

    private static Diagram TwoNestedPages() => new()
    {
        Orientation = "landscape",
        PageSize = new PageSize { Width = 14, Height = 9 },
        Pages = { NestedPage("p1"), NestedPage("p2") }
    };

    /// <summary>True when <paramref name="inner"/> lies entirely inside <paramref name="outer"/>.</summary>
    private static bool Encloses(Bounds outer, Bounds inner, double tolerance = 1e-6) =>
        inner.X - inner.W / 2 >= outer.X - outer.W / 2 - tolerance &&
        inner.X + inner.W / 2 <= outer.X + outer.W / 2 + tolerance &&
        inner.Y - inner.H / 2 >= outer.Y - outer.H / 2 - tolerance &&
        inner.Y + inner.H / 2 <= outer.Y + outer.H / 2 + tolerance;

    /// <summary>
    /// Resolves the parent/child pair from <c>ParentId</c> rather than from the fixture's naming.
    /// ParentId names the parent, so the group carrying it is the child.
    /// </summary>
    private static (Group Parent, Group Child) Hierarchy(Page page)
    {
        var child = page.Groups.Single(g => g.ParentId is not null);
        var parent = page.Groups.Single(g => g.Id == child.ParentId);
        return (parent, child);
    }

    [Fact]
    public void FixtureHierarchyMatchesGeometryBeforeAnyTransform()
    {
        // Guards the fixture itself: if this ever fails, the other assertions in this class are
        // meaningless because they would be verifying an arrangement that never existed.
        foreach (var page in TwoNestedPages().Pages)
        {
            var (parent, child) = Hierarchy(page);

            Assert.Equal(page.Name + "-outer", parent.Id);
            Assert.Equal(page.Name + "-inner", child.Id);
            Assert.NotNull(parent.Bounds);
            Assert.NotNull(child.Bounds);
            Assert.True(Encloses(parent.Bounds!, child.Bounds!),
                $"{page.Name}: ParentId says {parent.Id} is the parent, but its bounds do not enclose {child.Id}");
        }
    }

    [Fact]
    public void ParentIdParentEnclosesChildBeforeTranspose()
    {
        var diagram = TwoNestedPages();

        foreach (var page in diagram.Pages)
        {
            var (parent, child) = Hierarchy(page);
            Assert.True(Encloses(parent.Bounds!, child.Bounds!),
                $"{page.Name}: {parent.Id} does not enclose {child.Id} before transposition");
        }
    }

    [Fact]
    public void ParentIdParentEnclosesChildAfterTranspose()
    {
        var diagram = TwoNestedPages();

        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var (parent, child) = Hierarchy(page);
            Assert.True(Encloses(parent.Bounds!, child.Bounds!),
                $"{page.Name}: {parent.Id} no longer encloses {child.Id} after transposition " +
                $"(parent {parent.Bounds!.W:F2}x{parent.Bounds.H:F2}, child {child.Bounds!.W:F2}x{child.Bounds.H:F2})");
        }
    }

    [Fact]
    public void ParentIdParentEnclosesChildAfterTransposingTwice()
    {
        var diagram = TwoNestedPages();

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var (parent, child) = Hierarchy(page);
            Assert.True(Encloses(parent.Bounds!, child.Bounds!),
                $"{page.Name}: {parent.Id} does not enclose {child.Id} after two transpositions");
        }
    }

    [Fact]
    public void ParentIdChainIsAcyclicAndResolvableOnEveryPage()
    {
        // A ParentId that points at a missing group, or a cycle, would make containment
        // meaningless regardless of what the bounds say.
        foreach (var page in TwoNestedPages().Pages)
        {
            var byId = page.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

            foreach (var group in page.Groups)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { group.Id };
                var cursor = group.ParentId;

                while (cursor is not null)
                {
                    Assert.True(seen.Add(cursor),
                        $"{page.Name}: ParentId cycle through '{group.Id}'");

                    Assert.True(byId.ContainsKey(cursor),
                        $"{page.Name}: '{group.Id}' references missing parent '{cursor}'");

                    cursor = byId[cursor].ParentId;
                }
            }
        }
    }

    [Fact]
    public void TransposeSwapsNestedGroupBoundsAndPaddingOnEveryPage()
    {
        var diagram = TwoNestedPages();

        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            var (parent, child) = Hierarchy(page);

            // Parent authored as (4,4,5,2); transposed it becomes (4,4,2,5).
            var pb = parent.Bounds!;
            Assert.Equal(4, pb.X, 3);
            Assert.Equal(4, pb.Y, 3);
            Assert.Equal(2, pb.W, 3);
            Assert.Equal(5, pb.H, 3);
            Assert.Equal(0.46, parent.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.30, parent.Style.HeadingHeight!.Value, 3);

            // Child authored as (4.5,4,2,1.2); transposed it becomes (4,4.5,1.2,2).
            var cb = child.Bounds!;
            Assert.Equal(4, cb.X, 3);
            Assert.Equal(4.5, cb.Y, 3);
            Assert.Equal(1.2, cb.W, 3);
            Assert.Equal(2, cb.H, 3);
            Assert.Equal(0.60, child.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.50, child.Style.HeadingHeight!.Value, 3);
        }
    }

    [Fact]
    public void TransposeTwiceRestoresNestedBoundsOnEveryPage()
    {
        var diagram = TwoNestedPages();

        var before = diagram.Pages.Select(p => p.Groups
            .Select(g => (g.Id, g.Bounds!.X, g.Bounds.Y, g.Bounds.W, g.Bounds.H)).ToList()).ToList();

        DiagramLayout.Transpose(diagram);
        DiagramLayout.Transpose(diagram);

        for (int i = 0; i < diagram.Pages.Count; i++)
        {
            var groups = diagram.Pages[i].Groups;
            for (int j = 0; j < groups.Count; j++)
            {
                var bounds = groups[j].Bounds;
                Assert.NotNull(bounds);
                Assert.Equal(before[i][j].X, bounds.X, 6);
                Assert.Equal(before[i][j].Y, bounds.Y, 6);
                Assert.Equal(before[i][j].W, bounds.W, 6);
                Assert.Equal(before[i][j].H, bounds.H, 6);
            }
        }
    }

    [Fact]
    public void LayoutPipelineTransposesRecomputedGroupBoundsOnBothPages()
    {
        // The pipeline recomputes container bounds from member geometry (auto-sized by default),
        // so hand-authored bounds do not survive it. What this asserts is limited to what the
        // pipeline does guarantee: valid boxes, containment of members, padding swapped, and the
        // page reflected. See the next test for what it does not guarantee.
        var diagram = TwoNestedPages();

        DiagramLayoutEngine.Apply(
            diagram,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        foreach (var page in diagram.Pages)
        {
            Assert.Empty(DiagramLayout.CheckContainment(page));

            foreach (var group in page.Groups)
            {
                Assert.NotNull(group.Bounds);
                Assert.True(group.Bounds!.W > 0 && group.Bounds.H > 0,
                    $"{page.Name}/{group.Id}: degenerate bounds {group.Bounds.W:F2}x{group.Bounds.H:F2}");
            }

            var (parent, child) = Hierarchy(page);

            // Padding is swapped by the reflection even though the box itself is recomputed.
            Assert.Equal(0.46, parent.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.30, parent.Style.HeadingHeight!.Value, 3);
            Assert.Equal(0.60, child.Style!.PaddingX!.Value, 3);
            Assert.Equal(0.50, child.Style.HeadingHeight!.Value, 3);

            Assert.True(page.Size!.Height > page.Size.Width,
                $"{page.Name}: page was not transposed");
        }
    }

    [Fact]
    public void LayoutPipelineDoesNotGuaranteeParentGroupEnclosesChildGroup()
    {
        // Measured, and recording a real limitation rather than asserting it away.
        //
        // DiagramLayout.Apply orders groups parents-first and derives each container from its
        // members plus the child groups' bounds *as they are at that moment*. A child group that
        // is itself auto-sized is recomputed afterwards, so the parent was fitted to a box the
        // child no longer has. The result is a parent that can fail to enclose its child.
        //
        // This is pre-existing behaviour of the sizing pass, unrelated to transposition: the
        // same fixture without any transpose shows it too (verified by the second half of this
        // test). Fixing it is out of scope for T0 and is recorded as a known gap.
        var mirrored = TwoNestedPages();
        DiagramLayoutEngine.Apply(
            mirrored,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.TopToBottom },
            LayoutStrategy.Layered);

        bool EnclosesAfterPipeline(Diagram d, out string detail)
        {
            foreach (var page in d.Pages)
            {
                var (parent, child) = Hierarchy(page);
                if (!Encloses(parent.Bounds!, child.Bounds!))
                {
                    detail = $"{page.Name}: parent {parent.Bounds!.W:F2}x{parent.Bounds.H:F2} at " +
                             $"({parent.Bounds.X:F2},{parent.Bounds.Y:F2}) does not enclose child " +
                             $"{child.Bounds!.W:F2}x{child.Bounds.H:F2} at ({child.Bounds.X:F2},{child.Bounds.Y:F2})";
                    return false;
                }
            }

            detail = string.Empty;
            return true;
        }

        // Without transposition: documents the absence of the guarantee on its own.
        bool enclosesWithoutTranspose = EnclosesAfterPipeline(mirrored, out var withoutDetail);

        var transposed = TwoNestedPages();
        DiagramLayoutEngine.Apply(
            transposed,
            new LayoutOptions { Strategy = LayoutStrategy.Layered, Direction = LayoutDirection.LeftToRight },
            LayoutStrategy.Layered);

        bool enclosesWithTranspose = EnclosesAfterPipeline(transposed, out var withDetail);

        // The two orientations no longer agree, and the difference is an improvement rather than a
        // anomaly: the transposing direction re-derives box sizes after the swap, and that second
        // sizing pass happens to leave the parent enclosing its child. The untransposed direction
        // still shows the original limitation.
        //
        // Recording both facts separately, so neither "the gap is fixed" nor "the gap was papered
        // over" can pass unnoticed.
        // The gap this test recorded is closed: OrderGroups derives children before their parent, so
        // the parent is fitted to the child's *final* box rather than the one it had beforehand.
        // Both orientations must now enclose, and a failure here means the ordering regressed.
        Assert.True(enclosesWithoutTranspose,
            $"untransposed containment fails: {withoutDetail}");

        Assert.True(enclosesWithTranspose,
            $"transposed containment fails: {withDetail}");
    }

    [Fact]
    public void EveryTransformedObjectOnBothPagesIsAccountedFor()
    {
        var diagram = TwoNestedPages();
        DiagramLayout.Transpose(diagram);

        foreach (var page in diagram.Pages)
        {
            // Nothing in the page may be left untransposed: every object that carries page
            // coordinates is asserted here so a future field cannot be silently skipped.
            Assert.All(page.Nodes, n => Assert.True(n.Bounds.W > 0 && n.Bounds.H > 0));
            Assert.All(page.Groups, g => Assert.NotNull(g.Bounds));
            Assert.All(page.TextBlocks, t => Assert.NotNull(t.Bounds));
            Assert.All(page.Paths, p => Assert.NotNull(p.Bounds));
            Assert.All(page.Edges, e => Assert.StartsWith(page.Name + "-", e.Id));

            // Page size traded places, so every page is now taller than it is wide.
            Assert.True(page.Size!.Height > page.Size.Width);
        }

        Assert.True(diagram.PageSize!.Height > diagram.PageSize.Width);
        Assert.Equal("portrait", diagram.Orientation);
    }
}
