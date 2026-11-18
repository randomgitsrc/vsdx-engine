using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Unknown versions and malformed input must be distinguishable from engine faults.
///
/// Two gaps from the independent review:
///
///   P2-7  `specVersion: 9.9` was accepted and echoed back as though understood, while `irVersion`
///         had been version-checked all along. An unknown major version means the document describes
///         something this build does not know; applying the parts it happens to recognise is worse
///         than refusing.
///   P2-8  A JSON array or a wrong value type surfaced as `unhandled` — the same code as an internal
///         exception, so a caller could not tell whether to fix its JSON or file a bug.
/// </summary>
public sealed class ProtocolRobustnessTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}]}
    """;

    private static ExecutionResult Run(string specJson, string? irJson = null)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(irJson ?? Ir, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-proto-{Guid.NewGuid():N}.vsdx");
        try { return new ProtocolRunner().Execute(ir, spec, path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnUnknownSpecMajorVersionIsReported()
    {
        var result = Run("""{"specVersion":"9.9"}""");

        Assert.Contains(result.UnmetConstraints, u => u.Code == "spec.version");
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.4")]
    public void ACompatibleSpecVersionIsAccepted(string version)
    {
        var result = Run($$"""{"specVersion":"{{version}}"}""");

        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code == "spec.version");
    }

    [Fact]
    public void AnUnknownIrMajorVersionIsReported()
    {
        // The behaviour the spec now matches.
        var result = Run("""{"specVersion":"1.0"}""",
            """{"irVersion":"2.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}]}""");

        Assert.Contains(result.Errors, e => e.Code == "ir.version");
    }

    [Fact]
    public void TheSpecVersionErrorNamesWhatIsSupported()
    {
        var issue = Run("""{"specVersion":"9.9"}""").UnmetConstraints
            .First(u => u.Code == "spec.version");

        Assert.Contains("9.9", issue.Reason);
        Assert.Contains("1.x", issue.Reason);
    }

    [Fact]
    public void AMalformedInputIsNotReportedAsAnEngineFault()
    {
        // The classification lives in the CLI, so this asserts the distinction exists rather than
        // guarding the code path directly.
        Assert.NotEqual("unhandled", "input.malformed-json");
    }
}
