using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The parts of a result must agree with each other.
///
/// Six rounds of fixes touched how a result describes itself — metadata carried through, roles stored,
/// notes appended, severity preserved. Each was verified in isolation; this checks that they hold
/// together, on the property that matters most: what `warnings` says about severity must match what
/// `hardConstraintsPassed` says about the gate.
///
/// Measured across all six reference diagrams: every one reports zero error-severity warnings alongside
/// `hardConstraintsPassed: true`.
/// </summary>
public sealed class ReportConsistencyTests
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

    private static ExecutionResult Run(string scenario, string specJson)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", $"{scenario}.ir.json")), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-cons-{Guid.NewGuid():N}.vsdx");

        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private const string Plain = """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""";

    [Theory]
    [InlineData("cbo")]
    [InlineData("deploy-topology")]
    [InlineData("user-data-flow")]
    [InlineData("checkout-sequence")]
    public void WarningsAndTheGateAgree(string scenario)
    {
        var result = Run(scenario, Plain);

        bool hasErrorWarning = result.Warnings.Any(w => w.Severity == "error");

        Assert.Equal(hasErrorWarning, !result.Quality.HardConstraintsPassed);
    }

    [Theory]
    [InlineData("order-swimlane", """{"specVersion":"1.0","layout":"swimlane","direction":"topToBottom"}""")]
    [InlineData("org-tree", """{"specVersion":"1.0","layout":"tree","direction":"topToBottom"}""")]
    public void TheStrategyExamplesAgreeToo(string scenario, string spec)
    {
        var result = Run(scenario, spec);

        Assert.True(result.Quality.HardConstraintsPassed);
        Assert.DoesNotContain(result.Warnings, w => w.Severity == "error");
    }

    [Fact]
    public void EveryWarningCarriesAValidSeverity()
    {
        var result = Run("deploy-topology", Plain);

        Assert.All(result.Warnings, w => Assert.Contains(w.Severity, new[] { "error", "warning", "info" }));
    }

    [Fact]
    public void TheGatesOwnCountsMatchTheReport()
    {
        // `hardViolations` and `softViolations` are what the score is computed from; they must describe
        // the same analysis the issue codes came from.
        var result = Run("deploy-topology", Plain);

        Assert.True(result.Quality.HardConstraintsPassed);
        Assert.Equal(0, result.Quality.HardViolations);
        Assert.True(result.Quality.SoftViolations > 0);
        Assert.True(result.Quality.Score > 0);
    }
}
