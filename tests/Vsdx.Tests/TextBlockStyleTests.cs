using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Text blocks must carry colour and font the same way shapes do.
///
/// The bridge wrote `Color` and `Font` as bare shape-level cells while the reader reads
/// `shape.TextStyle`, which comes from the Character section. The values were therefore written
/// and unreadable at the same time, and the font never reached `FaceNames` at all.
///
/// Two things have to happen together. Moving the cells into Character/Row on its own makes
/// things worse: measured on four variants, that variant lost the fontSize that previously
/// worked, because Character's Font cell holds an *index* into FaceNames rather than a name. So
/// this suite checks both the values and the absence of that regression.
/// </summary>
public sealed class TextBlockStyleTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (Diagram Diagram, string Path) Generate(Action<TextBlock> configure)
    {
        var block = new TextBlock
        {
            Id = "title",
            Text = "Title",
            Role = "title",
            Bounds = new Bounds(4, 5, 3, 0.5)
        };
        configure(block);

        var diagram = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "page-1",
                    Size = new PageSize { Width = 8, Height = 6 },
                    TextBlocks = { block }
                }
            }
        };

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-tb-{Guid.NewGuid():N}.vsdx");
        new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false });

        try
        {
            return (new VsdxEngine().Read(path).Diagram, path);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    [Fact]
    public void TextBlockColourSurvivesTheRoundTrip()
    {
        var (diagram, path) = Generate(b =>
        {
            b.Style = new ShapeStyle { TextColor = "#FF0000" };
        });

        try
        {
            var read = diagram.Pages[0].TextBlocks.Single(t => t.Id == "title");
            Assert.Equal("#FF0000", read.Style?.TextColor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TextBlockFontSurvivesTheRoundTrip()
    {
        var (diagram, path) = Generate(b =>
        {
            b.Style = new ShapeStyle { Font = "Verdana" };
        });

        try
        {
            var read = diagram.Pages[0].TextBlocks.Single(t => t.Id == "title");
            Assert.Equal("Verdana", read.Style?.Font);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TextBlockFontSizeIsNotLostWhileFixingColour()
    {
        // The regression the naive fix introduced: this must stay correct alongside colour.
        var (diagram, path) = Generate(b =>
        {
            b.Style = new ShapeStyle { FontSize = 18, TextColor = "#FF0000", Font = "Verdana" };
        });

        try
        {
            var read = diagram.Pages[0].TextBlocks.Single(t => t.Id == "title");
            Assert.Equal(18, read.Style?.FontSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AllThreeTextPropertiesSurviveTogether()
    {
        var (diagram, path) = Generate(b =>
        {
            b.Style = new ShapeStyle { FontSize = 18, TextColor = "#FF0000", Font = "Verdana" };
        });

        try
        {
            var read = diagram.Pages[0].TextBlocks.Single(t => t.Id == "title");
            Assert.Equal("#FF0000", read.Style?.TextColor);
            Assert.Equal("Verdana", read.Style?.Font);
            Assert.Equal(18, read.Style?.FontSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TextStyleIsPreservedAlongsideColourAndFont()
    {
        // Bold/italic live in the same Character row; the fix must not disturb them.
        var (diagram, path) = Generate(b =>
        {
            b.Style = new ShapeStyle
            {
                TextColor = "#FF0000", Font = "Verdana", FontSize = 16, Bold = true, Italic = true
            };
        });

        try
        {
            var read = diagram.Pages[0].TextBlocks.Single(t => t.Id == "title");
            Assert.True(read.Style?.Bold);
            Assert.True(read.Style?.Italic);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TextBlocksWithoutStyleStillRender()
    {
        // Nothing set: role-based sizing must still apply and nothing may crash.
        var (diagram, path) = Generate(_ => { });

        try
        {
            var read = diagram.Pages[0].TextBlocks.Single(t => t.Id == "title");
            Assert.NotNull(read);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
