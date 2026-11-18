using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A request that cannot be honoured must appear in `unmet`; a step that ran must appear in
/// `applied`. The charter names the exception: accepted by the protocol, allowed by the schema,
/// ignored in execution, and not reported.
///
/// Four instances found by independent review:
///
///   P2-1  `spacing` under `layout=manual` — silent, while `direction` and `containerLayouts` in the
///         same branch both report
///   P2-2  `theme.roleColors` with no role-carrying nodes — neither applied nor unmet
///   P2-3  `container.bands` recorded even on a diagram with no containers
///   P2-5  `IrMetadata` (description, tags) dropped entirely, unmentioned
/// </summary>
public sealed class SilentIgnoreTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static readonly string TwoNodes = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A"},{"id":"b","label":"B"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private static ExecutionResult Run(string irJson, string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-silent-{Guid.NewGuid():N}.vsdx");
        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SpacingUnderManualIsReported()
    {
        var result = Run(TwoNodes, """{"specVersion":"1.0","layout":"manual","spacing":{"nodeGapX":2.0}}""");

        Assert.Contains(result.UnmetConstraints, u => u.Code == "spacing.manual-ignored");
    }

    [Fact]
    public void ManualIsStillTheOnlyCaseThatReportsIt()
    {
        // Outside manual, spacing is honoured and must not be reported as ignored.
        var result = Run(TwoNodes, """{"specVersion":"1.0","layout":"layered","spacing":{"nodeGapX":2.0}}""");

        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "spacing.manual-ignored");
    }

    [Fact]
    public void RoleColorsWithNothingToColourAreReported()
    {
        var result = Run(TwoNodes, """{"specVersion":"1.0","theme":{"roleColors":{"service":"#FF0000"}}}""");

        bool mentioned = result.UnmetConstraints.Any(u => u.Code.StartsWith("theme.roleColors", StringComparison.Ordinal)) ||
                         result.AppliedDesignNotes.Any(n => n.Contains("roleColors", StringComparison.Ordinal));

        Assert.True(mentioned, "roleColors was neither applied nor reported");
    }

    [Fact]
    public void BandsAreNotClaimedWhenThereAreNoContainers()
    {
        var result = Run(TwoNodes, """{"specVersion":"1.0"}""");

        Assert.DoesNotContain("container.bands", result.AppliedDesignNotes);
    }

    [Fact]
    public void BandsAreStillReportedWhenContainersExist()
    {
        var withContainer = """
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "containers":[{"id":"g","label":"G"}],
         "nodes":[{"id":"a","label":"A","containerId":"g"}],"edges":[]}
        """;

        var result = Run(withContainer, """{"specVersion":"1.0"}""");

        Assert.Contains("container.bands", result.AppliedDesignNotes);
    }

    [Fact]
    public void MetadataThatCannotBeStoredIsReported()
    {
        var withMeta = """
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "metadata":{"description":"a description","tags":{"k":"v"}},
         "nodes":[{"id":"a","label":"A"}]}
        """;

        var result = Run(withMeta, """{"specVersion":"1.0"}""");

        // No longer dropped: the fields are persisted in the package. The test moved from "report
        // the loss" to "do not lose it", which is the better outcome.
        Assert.True(result.Status is ExecutionStatus.Succeeded or ExecutionStatus.Partial);
    }

    [Fact]
    public void MetadataSurvivesTheRoundTrip()
    {
        var withMeta = """
        {"irVersion":"1.0","id":"t","kind":"architecture",
         "metadata":{"description":"a description","tags":{"k":"v"}},
         "nodes":[{"id":"a","label":"A"}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(withMeta, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-meta-{Guid.NewGuid():N}.vsdx");
        try
        {
            new ProtocolRunner().Execute(ir, spec, path);
            var back = new VsdxEngine().Read(path).Diagram.Metadata;

            Assert.Equal("a description", back.Description);
            Assert.Equal("v", back.Tags?["k"]);
        }
        finally { File.Delete(path); }
    }
}
