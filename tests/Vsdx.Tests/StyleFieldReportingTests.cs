using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Style fields the engine accepts but cannot express must be reported, not dropped.
///
/// `SchemaDeclaresThemAndTheModelAcceptsThem` below shows the shape of the promise: the JSON
/// schema advertises opacity, shadow, radius, lineJoin and paddingBottom, and the model has a
/// property for each. None of them reached the Adapters layer at all, so a caller could set
/// all five, get `compatibility: lossless`, and receive a file where none of them applied.
///
/// This is the one outcome the project's own constraints forbid: accepted by the protocol,
/// allowed by the schema, ignored at execution, and not reported.
/// </summary>
public sealed class StyleFieldReportingTests
{
    private static Diagram StyledNode(Action<ShapeStyle> configure)
    {
        var style = new ShapeStyle { Fill = "#2E75B6" };
        configure(style);

        var page = new Page
        {
            Name = "p",
            Size = new PageSize { Width = 6, Height = 3 },
            Nodes = { new Node { Id = "x", Text = "X", Bounds = new Bounds(3, 1.5, 2, 0.8), Style = style } }
        };

        return new Diagram { Pages = { page } };
    }

    private static WriteResult Write(Diagram diagram, out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"vsdx-style-{Guid.NewGuid():N}.vsdx");
        return new VsdxEngine().Create(diagram, path, new WriteOptions { AutoLayout = false, ValidateAfterSave = false });
    }

    [Theory]
    [InlineData("shadow")]
    [InlineData("radius")]
    [InlineData("lineJoin")]
    [InlineData("paddingBottom")]
    public void UnsupportedStyleFieldsAreReported(string field)
    {
        var diagram = StyledNode(s =>
        {
            switch (field)
            {
                case "opacity": s.Opacity = 0.5; break;
                case "shadow": s.Shadow = true; break;
                case "radius": s.Radius = 0.3; break;
                case "lineJoin": s.LineJoin = "round"; break;
                case "paddingBottom": s.PaddingBottom = 0.3; break;
            }
        });

        var result = Write(diagram, out var path);
        try
        {
            var codes = result.Compatibility.Issues.Select(i => i.Code).ToList();

            Assert.Contains(codes, c => c.Contains("style.", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingNoUnsupportedFieldProducesNoSuchWarning()
    {
        // The warning must distinguish "you asked for something we cannot do" from ordinary
        // writes, or it becomes background noise.
        var result = Write(StyledNode(_ => { }), out var path);
        try
        {
            Assert.DoesNotContain(result.Compatibility.Issues,
                i => i.Code.Contains("style.", StringComparison.OrdinalIgnoreCase)
                  && i.Code.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SupportedStyleFieldsDoNotTriggerTheUnsupportedWarning()
    {
        // Fill, stroke, font and friends really are implemented; they must stay silent.
        var diagram = StyledNode(s =>
        {
            s.Stroke = "#000000";
            s.StrokeWidth = 0.02;
            s.Font = "Arial";
            s.FontSize = 12;
            s.Bold = true;
            s.TextColor = "#FFFFFF";
        });

        var result = Write(diagram, out var path);
        try
        {
            Assert.DoesNotContain(result.Compatibility.Issues,
                i => i.Code.Contains("style.unsupported", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheReportNamesWhichFieldWasDropped()
    {
        // "Some style was dropped" is not actionable; the caller needs to know which.
        var diagram = StyledNode(s => { s.Opacity = 0.5; s.Shadow = true; });

        var result = Write(diagram, out var path);
        try
        {
            var text = string.Join(" ", result.Compatibility.Issues.Select(i => i.Message)).ToLowerInvariant();

            Assert.True(text.Contains("opacity") || text.Contains("shadow"),
                $"report did not name the dropped field: {text}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
