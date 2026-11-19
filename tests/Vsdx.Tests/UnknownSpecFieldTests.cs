using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A DesignSpec field the engine does not know must be reported, not silently discarded.
///
/// `System.Text.Json` ignores unmapped properties by default, so `{"canvas": {"profile": "a4-landscape"}}` or a
/// plain typo deserialises without complaint and the request evaporates. The run reports `succeeded`.
///
/// I hit this writing the output-profile research: I passed `canvas.profile` expecting it to pin the sheet,
/// and the diagram came back on whatever paper the engine chose, with a clean status. Nothing said the field
/// had been dropped.
///
/// Goal §5.3: "Engine 不能静默忽略无法满足的要求". An unknown field is a request the engine cannot honour, which
/// makes silence here the same class of defect as the dropped `metadata.source` and the skipped free path.
/// </summary>
public sealed class UnknownSpecFieldTests
{
    private static (ExecutionResult Result, string File) Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(
            """{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}],"edges":[]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var spec = JsonSerializer.Deserialize<DesignSpec>(
            specJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-unk-{Guid.NewGuid():N}.vsdx");

        try
        {
            return (new ProtocolRunner().Execute(ir, spec, path), path);
        }
        finally { }
    }

    [Fact]
    public void AnUnknownTopLevelFieldIsReported()
    {
        var (result, file) = Run("""{"specVersion":"1.0","nonexistentField":123}""");

        try
        {
            Assert.True(
                result.UnmetConstraints.Any(u => u.Code.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                || result.Warnings.Any(w => w.Code.Contains("unknown", StringComparison.OrdinalIgnoreCase)),
                "an unknown design-spec field was accepted and discarded without a report");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void AnUnknownNestedFieldIsReported()
    {
        var (result, file) = Run("""{"specVersion":"1.0","canvas":{"mode":"paper","profile":"a4-landscape"}}""");

        try
        {
            bool reported =
                result.UnmetConstraints.Any(u => u.Code.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                || result.Warnings.Any(w => w.Code.Contains("unknown", StringComparison.OrdinalIgnoreCase));

            Assert.True(reported, "canvas.profile was silently ignored");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void AValidSpecProducesNoSuchComplaint()
    {
        // The negative case: a clean spec must not gain warnings.
        var (result, file) = Run("""{"specVersion":"1.0","canvas":{"mode":"paper"}}""");

        try
        {
            Assert.DoesNotContain(result.Warnings, w => w.Code.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
