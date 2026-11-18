using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A page background has to reach the package and come back.
///
/// `theme.background` set a model field that nothing wrote: measured, the run reported
/// `theme.background` as applied while the exported model returned background=None and the page
/// XML contained no PageBackground cell. The renderer did use a background — but only from its own
/// command-line argument, never from the file, so a caller who set the colour in the spec saw a
/// white preview and a white document.
///
/// The cell lives in `pages.xml` under the page's PageSheet, not in `pageN.xml`. That matters
/// because the bridges that patch page XML never touch that part.
/// </summary>
public sealed class BackgroundTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string OneNodeIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture","nodes":[{"id":"a","label":"A"}]}
    """;

    private static (ExecutionResult Result, Diagram Diagram, string Path) Run(string? specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(OneNodeIr, Json)!;
        var spec = specJson is null ? null : JsonSerializer.Deserialize<DesignSpec>(specJson, Json);

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-bg-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);
        return (result, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void ABackgroundFromTheSpecSurvivesTheRoundTrip()
    {
        var (_, diagram, path) = Run("""{"specVersion":"1.0","theme":{"background":"#FAFAFA"}}""");
        try
        {
            Assert.Equal("#FAFAFA", diagram.Pages[0].Background);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ThePackageActuallyContainsThePageBackgroundCell()
    {
        // Guard against a fix that only shuttles the value through the in-memory model.
        var (_, _, path) = Run("""{"specVersion":"1.0","theme":{"background":"#FAFAFA"}}""");
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = zip.GetEntry("visio/pages/pages.xml")!;
            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("PageBackground", xml);
            Assert.Contains("#FAFAFA", xml);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ADiagramWithoutABackgroundWritesNone()
    {
        // Absence must stay absence: writing a default colour would change every existing diagram.
        var (_, diagram, path) = Run(null);
        try
        {
            Assert.True(string.IsNullOrWhiteSpace(diagram.Pages[0].Background));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BackgroundAppliedIsOnlyClaimedWhenItIsWritten()
    {
        // The original bug reported `theme.background` while writing nothing.
        var (result, diagram, path) = Run("""{"specVersion":"1.0","theme":{"background":"#121212"}}""");
        try
        {
            bool claimed = result.AppliedDesignNotes.Any(n => n.Contains("background", StringComparison.OrdinalIgnoreCase));

            if (claimed)
            {
                Assert.Equal("#121212", diagram.Pages[0].Background);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnExplicitPageBackgroundIsNotOverwritten()
    {
        // A per-page colour is more specific than the theme.
        var ir = JsonSerializer.Deserialize<DiagramIr>(OneNodeIr, Json)!;
        var diagram = DiagramIrConverter.ToDiagram(ir);
        diagram.Pages[0].Background = "#000000";

        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"background":"#FAFAFA"}}""", Json)!;
        DesignSpecApplier.Apply(diagram, spec);

        Assert.Equal("#000000", diagram.Pages[0].Background);
    }
}
