using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `nodeUtilisation` answers "how much of the page does the drawing itself occupy" without changing
/// `pageUtilisation`, which counts decorations too.
///
/// `known-issues.md` §2 records the problem: `checkout-sequence` reports 84% while its nodes span 23% of
/// the page, because a note at y=6.35 and a title at y=4.35 stretch the content box to full height. The
/// consequence is that `page.underused` misses two of six reference diagrams.
///
/// Redefining the existing figure would move every score in the project. Adding a second measurement does
/// not: nothing that reads `pageUtilisation` changes, and a caller who wants to know whether the drawing
/// fills the page can ask directly.
/// </summary>
public sealed class NodeUtilisationTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static Diagram TwoNodesAndAStrayNote() => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 13, Height = 7 },
                Nodes =
                {
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 1, 1.4, 0.6) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(10, 1, 1.4, 0.6) }
                },
                Edges = { new Edge { Id = "e", From = "a", To = "b" } },
                TextBlocks =
                {
                    new TextBlock { Id = "note", Text = "注", Role = "note", Bounds = new Bounds(6.5, 6.4, 6, 0.4) }
                }
            }
        }
    };

    [Fact]
    public void TheDrawingOnlyFigureIgnoresTheNote()
    {
        var report = LayoutAnalyzer.Analyze(TwoNodesAndAStrayNote());

        double drawing = report.NodeUtilisation["p"];
        double whole = report.PageUtilisation["p"];

        // The nodes span 9.4 x 0.6 of a 13 x 7 page — about 6%.
        Assert.InRange(drawing, 0.02, 0.15);
        Assert.True(whole > drawing * 2, $"both figures agree ({whole:P0} vs {drawing:P0}) — the fixture stopped separating them");
    }

    [Fact]
    public void ADiagramWithNoNodesReportsNoDrawingFigureAtAll()
    {
        // A page of only text is legal, and it takes an early-return path that never reaches the
        // utilisation block — so neither figure exists for it. Asserting absence rather than zero, because
        // zero would also be a claim.
        var diagram = new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 8, Height = 6 },
                    TextBlocks = { new TextBlock { Id = "t", Text = "标题", Role = "title", Bounds = new Bounds(4, 5, 4, 0.5) } }
                }
            }
        };

        var report = LayoutAnalyzer.Analyze(diagram);

        Assert.False(report.NodeUtilisation.ContainsKey("p"));
        Assert.False(report.PageUtilisation.ContainsKey("p"));
    }

    [Fact]
    public void AFilledPageReadsHighOnBoth()
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 6, Height = 4 } };

        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 2; j++)
            page.Nodes.Add(new Node
            {
                Id = $"n{i}{j}", Text = "X", Bounds = new Bounds(1.2 + i * 1.8, 1.2 + j * 1.6, 1.4, 1.2)
            });

        var report = LayoutAnalyzer.Analyze(new Diagram { Id = "t", Pages = { page } });

        Assert.True(report.NodeUtilisation["p"] > 0.5);
    }

    [Theory]
    [InlineData("user-data-flow", """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""")]
    [InlineData("checkout-sequence", """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""")]
    public void TheTwoDiagramsThatMissUnderusedNowShowWhy(string scenario, string specJson)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", $"{scenario}.ir.json")), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-nu-{Guid.NewGuid():N}.vsdx");

        try
        {
            new ProtocolRunner().Execute(ir, spec, path);
            var report = LayoutAnalyzer.Analyze(new VsdxEngine().Read(path).Diagram);

            double drawing = report.NodeUtilisation.Values.Single();
            double whole = report.PageUtilisation.Values.Single();

            Assert.True(drawing < 0.30, $"{scenario}: drawing occupies {drawing:P0}, no longer sparse");
            Assert.True(whole > 0.30, $"{scenario}: whole page reads {whole:P0}, no longer inflated");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
