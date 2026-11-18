using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A node's `attributes` must survive the round trip with names and values untouched.
///
/// The IR calls the field `attributes`; the model calls it `ShapeData`. They are the same data, and the
/// converter maps between them — but exporting the model writes `shapeData`, so an IR round trip through
/// `export-model` does not show the key the caller supplied. My first check read `node.attributes` on the
/// model and found nothing, which looked like total loss for the third time in three rounds.
///
/// The data is intact: six properties including a Chinese name, a kebab-case name and an `UPPER` key whose
/// value is `lower`, all unchanged. Case is preserved on both sides.
/// </summary>
public sealed class NodeAttributesFidelityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string Ir = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "nodes":[{"id":"a","label":"A",
       "attributes":{"Instance":"10.0.1.11","Owner":"平台组","Tier":"1",
                     "中文键":"中文值","kebab-case":"v","UPPER":"lower"}}],
     "edges":[]}
    """;

    private static (Diagram Written, string File) RoundTrip(string? irJson = null)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(irJson ?? Ir, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-attr-{Guid.NewGuid():N}.vsdx");

        new ProtocolRunner().Execute(ir, spec, path);
        return (new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void EveryAttributeSurvives()
    {
        var (written, file) = RoundTrip();

        try
        {
            var data = written.Pages[0].Nodes.Single().ShapeData;

            Assert.NotNull(data);
            Assert.Equal(6, data!.Count);
            Assert.Equal("10.0.1.11", data["Instance"]);
            Assert.Equal("平台组", data["Owner"]);
            Assert.Equal("中文值", data["中文键"]);
            Assert.Equal("v", data["kebab-case"]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void KeyCaseIsNotNormalised()
    {
        // A model that lower-cased keys would silently merge `UPPER` and `Upper`.
        var (written, file) = RoundTrip();

        try
        {
            var data = written.Pages[0].Nodes.Single().ShapeData!;

            Assert.Contains("UPPER", data.Keys);
            Assert.DoesNotContain("upper", data.Keys);
            Assert.Equal("lower", data["UPPER"]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheAttributeReachesThePackageAsPropertyRows()
    {
        // Storage form: a `Prop` section with one row per attribute.
        var (_, file) = RoundTrip();

        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(file);
            using var stream = zip.Entries.First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                                                   && e.FullName != "visio/pages/pages.xml").Open();
            using var reader = new StreamReader(stream);
            var xml = reader.ReadToEnd();

            Assert.Contains("Section N=\"Prop\"", xml);
            Assert.Contains("10.0.1.11", xml);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void AModelWithoutAttributesGainsNone()
    {
        var (written, file) = RoundTrip("""
        {"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}],"edges":[]}
        """);

        try
        {
            Assert.True(written.Pages[0].Nodes.Single().ShapeData is null or { Count: 0 });
        }
        finally { File.Delete(file); }
    }
}
