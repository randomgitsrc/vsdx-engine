using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A protocol warning must say how severe the problem was, or a caller cannot tell a clipping shape from
/// a slightly loose margin.
///
/// `ExecutionWarning` carries only `Code`, `Message` and `Target`, and `FromCompatibility` copies every
/// compatibility issue into it regardless of level. Measured: a node pushed off a 2x2" page produces
/// `layout.node.out-of-page` — an **error** that sets `hardConstraintsPassed = false` — delivered through
/// the `warnings` array with no severity at all, while `errors` stayed empty.
///
/// The caller sees "one warning" and "hard constraints failed" in the same result and has no way to
/// connect them.
/// </summary>
public sealed class WarningSeverityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}],"edges":[]}
    """;

    private static ExecutionResult Run(string specJson)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(Ir, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-sev-{Guid.NewGuid():N}.vsdx");

        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AnErrorLevelProblemIsMarkedInTheWarningItArrivesAs()
    {
        var result = Run("""{"specVersion":"1.0","canvas":{"mode":"fixed","width":2,"height":2,"shrinkToFit":false}}""");

        Assert.False(result.Quality.HardConstraintsPassed);

        var warning = result.Warnings.FirstOrDefault(w => w.Code.Contains("out-of-page", StringComparison.Ordinal));

        Assert.NotNull(warning);
        Assert.Equal("error", warning!.Severity);
    }

    [Fact]
    public void AnInformationalProblemIsMarkedAsInfo()
    {
        // Severity is carried per issue, not one level for the whole array. `page.margins` is info;
        // it must not arrive claiming to be a warning or an error.
        var result = Run("""{"specVersion":"1.0","canvas":{"mode":"fixed","width":2,"height":2,"shrinkToFit":false}}""");

        var info = result.Warnings.FirstOrDefault(w => w.Code.Contains("margins", StringComparison.Ordinal));

        Assert.NotNull(info);
        Assert.Equal("info", info!.Severity);
    }

    [Fact]
    public void EveryWarningHasASeverity()
    {
        var result = Run("""{"specVersion":"1.0","canvas":{"mode":"fixed","width":2,"height":2,"shrinkToFit":false}}""");

        Assert.All(result.Warnings, w => Assert.False(string.IsNullOrWhiteSpace(w.Severity),
            $"'{w.Code}' has no severity"));
    }

    [Fact]
    public void HardFailuresAreReportedByTheQualityBlockEvenWhenDeliveredAsWarnings()
    {
        // Whatever the warnings say, the gate has to agree with them.
        var result = Run("""{"specVersion":"1.0","canvas":{"mode":"fixed","width":2,"height":2,"shrinkToFit":false}}""");

        bool anyErrorWarning = result.Warnings.Any(w => w.Severity == "error");

        Assert.Equal(anyErrorWarning, !result.Quality.HardConstraintsPassed);
    }
}
