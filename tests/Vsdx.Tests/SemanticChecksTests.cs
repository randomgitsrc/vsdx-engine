using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The checker sees geometry only. A diagram where the client writes straight to the database — bypassing every
/// service — scores 90 and is told about a connector crossing a band heading.
///
/// E15 measured that. This is the first semantic rule: a `client` or `actor` connecting directly to a `database` or
/// `storage` is reported, because there is no reading of that edge under which an end user's device speaks SQL to the
/// datastore, and in a deployment diagram it means the application tier has been bypassed.
///
/// **The rule is deliberately narrow.** My first attempt was "edges spanning two or more tiers", which fired on six
/// edges across the shipped cases — `client → service`, `service → external`, `database → gateway`,
/// `database → external` are all ordinary. Tier distance is not a proxy for wrongness; the role pair is.
/// </summary>
public sealed class SemanticChecksTests
{
    private static Diagram WithEdge(string fromRole, string toRole)
    {
        var page = new Page { Name = "p", Size = new PageSize { Width = 12, Height = 9 } };

        page.Nodes.Add(new Node { Id = "a", Text = "A", Type = fromRole, Bounds = new Bounds(5, 7, 1.4, 0.6) });
        page.Nodes.Add(new Node { Id = "b", Text = "B", Type = toRole, Bounds = new Bounds(5, 2, 1.4, 0.6) });

        page.Edges.Add(new Edge { Id = "e", From = "a", To = "b" });

        return new Diagram { Id = "t", Type = "architecture", Pages = { page } };
    }

    [Theory]
    [InlineData("client", "database")]
    [InlineData("client", "storage")]
    [InlineData("actor", "database")]
    public void AClientReachingADatastoreDirectlyIsReported(string from, string to)
    {
        var report = LayoutAnalyzer.Analyze(WithEdge(from, to));

        Assert.Contains(report.Issues, i => i.Code == "edge.bypasses-service-tier");
    }

    [Theory]
    [InlineData("client", "service")]
    [InlineData("client", "gateway")]
    [InlineData("service", "database")]
    [InlineData("service", "external")]
    [InlineData("database", "gateway")]
    [InlineData("database", "external")]
    [InlineData("gateway", "service")]
    public void OrdinaryEdgesAreNotReported(string from, string to)
    {
        var report = LayoutAnalyzer.Analyze(WithEdge(from, to));

        Assert.DoesNotContain(report.Issues, i => i.Code == "edge.bypasses-service-tier");
    }

    [Fact]
    public void TheRuleStaysSilentOnEveryShippedCase()
    {
        // The shipped diagrams are the reference for "ordinary". If the rule fires on one of them it is wrong.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln"))) dir = dir.Parent;

        foreach (var file in Directory.GetFiles(Path.Combine(dir!.FullName, "examples", "realworld"), "*.ir.json"))
        {
            var ir = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DiagramIr>(
                File.ReadAllText(file),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            var report = LayoutAnalyzer.Analyze(Vsdx.Protocol.DiagramIrConverter.ToDiagram(ir));

            Assert.DoesNotContain(report.Issues, i => i.Code == "edge.bypasses-service-tier");
        }
    }
}
