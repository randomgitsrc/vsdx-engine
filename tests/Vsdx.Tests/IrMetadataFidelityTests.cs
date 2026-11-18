using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every field the IR accepts must reach the model or be reported as unmet.
///
/// `IrMetadata` has four fields. `Description` and `Tags` are carried through and persisted; `Source` and
/// `Created` are read by nothing — the converter never touches them, so a caller who supplies them gets
/// silence. Worse for `Created`: the written package reports the moment it was generated, so the caller's
/// timestamp is not merely dropped but replaced by a different value that looks authoritative.
///
/// The charter's rule is that a request is either honoured or listed. Neither field is listed.
/// </summary>
public sealed class IrMetadataFidelityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (ExecutionResult Result, Diagram Written) Run(string irJson)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-meta-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new ProtocolRunner().Execute(ir, spec, path);
            return (result, new VsdxEngine().Read(path).Diagram);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private const string WithAllMetadata = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "metadata":{
       "description":"描述文本",
       "source":"https://example.com/source",
       "created":"2026-01-15T10:00:00+00:00",
       "tags":{"team":"platform"}
     },
     "nodes":[{"id":"a","label":"A"}],"edges":[]}
    """;

    [Fact]
    public void DescriptionReachesTheModel()
    {
        var (_, written) = Run(WithAllMetadata);

        Assert.Equal("描述文本", written.Metadata.Description);
    }

    [Fact]
    public void TagsReachTheModel()
    {
        var (_, written) = Run(WithAllMetadata);

        Assert.Equal("platform", written.Metadata.Tags?["team"]);
    }

    [Fact]
    public void SourceIsEitherCarriedOrReported()
    {
        var (result, written) = Run(WithAllMetadata);

        bool carried = written.Metadata.Source == "https://example.com/source";
        bool reported = result.UnmetConstraints.Any(u => u.Code.Contains("metadata", StringComparison.OrdinalIgnoreCase));

        Assert.True(carried || reported,
            "source was neither carried through nor reported as unmet");
    }

    [Fact]
    public void CreatedIsEitherHonouredOrReported()
    {
        var (result, written) = Run(WithAllMetadata);

        bool honoured = written.Metadata.Created == DateTimeOffset.Parse("2026-01-15T10:00:00+00:00");
        bool reported = result.UnmetConstraints.Any(u => u.Code.Contains("metadata", StringComparison.OrdinalIgnoreCase));

        Assert.True(honoured || reported,
            "the caller's created timestamp was replaced and not reported");
    }

    [Fact]
    public void AbsentMetadataIsNotReported()
    {
        // Only a request that was made and dropped deserves a line.
        var (result, _) = Run("""
        {"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}],"edges":[]}
        """);

        Assert.DoesNotContain(result.UnmetConstraints, u => u.Code.Contains("metadata", StringComparison.OrdinalIgnoreCase));
    }
}
