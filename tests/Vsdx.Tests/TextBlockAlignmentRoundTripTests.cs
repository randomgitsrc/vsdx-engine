using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A text block's alignment must survive a round trip.
///
/// Two fields carried it — `TextBlock.Align` set by an author in the IR, `TextBlock.Style.HAlign` set
/// by the reader when parsing a package — and the writer read only the first. So every round trip
/// silently reset alignment to left: measured, a centred title came back left with `changes` empty
/// and `diff` reporting identical.
///
/// Found by independent review, not by the suite, which is exactly why the second test exists.
/// </summary>
public sealed class TextBlockAlignmentRoundTripTests
{
    private static (Diagram Written, string Path) RoundTrip(TextBlock block)
    {
        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 10, Height = 8 },
            Nodes = { new Node { Id = "n", Text = "N", Bounds = new Bounds(5, 4, 1.4, 0.7) } },
            TextBlocks = { block }
        };

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-align-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(new Diagram { Id = "t", Pages = { page } }, path,
            new WriteOptions { AutoLayout = false });

        return (new VsdxEngine().Read(path).Diagram, path);
    }

    [Theory]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("left")]
    public void AuthorAlignmentSurvivesTheWrite(string align)
    {
        var block = new TextBlock
        {
            Id = "title", Text = "Title", Role = "title", Align = align,
            Bounds = new Bounds(5, 6, 4, 0.6)
        };

        var (written, path) = RoundTrip(block);
        try
        {
            var back = written.Pages[0].TextBlocks.Single();
            string? effective = back.Align ?? back.Style?.HAlign;

            Assert.Equal(align, effective, ignoreCase: true);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadingThenWritingDoesNotChangeAlignment()
    {
        // The failure mode: parse a package, write it back untouched, alignment resets.
        var block = new TextBlock
        {
            Id = "title", Text = "Title", Role = "title",
            Style = new ShapeStyle { HAlign = "center" },
            Bounds = new Bounds(5, 6, 4, 0.6)
        };

        var (written, path) = RoundTrip(block);
        try
        {
            var back = written.Pages[0].TextBlocks.Single();
            Assert.Equal("center", (back.Align ?? back.Style?.HAlign)?.ToLowerInvariant());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DiffNoticesAnAlignmentChange()
    {
        // Without this, `diff` declares two packages identical while their titles sit differently.
        var before = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 10, Height = 8 },
                    TextBlocks = { new TextBlock { Id = "title", Text = "T", Align = "center", Bounds = new Bounds(5, 6, 4, 0.6) } }
                }
            }
        };

        var after = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 10, Height = 8 },
                    TextBlocks = { new TextBlock { Id = "title", Text = "T", Align = "left", Bounds = new Bounds(5, 6, 4, 0.6) } }
                }
            }
        };

        var diff = DiagramDiff.Compute(before, after, "a", "b");

        Assert.False(diff.Identical);
        Assert.Contains(diff.Changes, c => c.Property == "hAlign");
    }
}
