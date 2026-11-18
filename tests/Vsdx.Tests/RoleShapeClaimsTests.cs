using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Two capability-matrix entries claimed less than the engine does.
///
/// "语义样式角色" was marked partial with the note "形状不随角色变化". `MasterCatalog` maps roles to
/// Visio masters and the mapping is live: measured `database`→`Process`, `decision`→`Diamond`,
/// `process`→`Rectangle`.
///
/// "多套视觉风格" was marked experimental with "未验证能否产出明显不同的风格". Two palettes render to
/// different images — that is the verification it said was missing.
/// </summary>
public sealed class RoleShapeClaimsTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static (string NameU, string File) Write(string role, string specJson)
    {
        var irJson = "{\"irVersion\":\"1.0\",\"id\":\"t\",\"kind\":\"architecture\","
            + "\"nodes\":[{\"id\":\"n\",\"label\":\"N\",\"role\":\"" + role + "\"}],\"edges\":[]}";

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-role-{Guid.NewGuid():N}.vsdx");

        new ProtocolRunner().Execute(ir, spec, path);

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        using var stream = zip.Entries
            .First(e => e.FullName.StartsWith("visio/pages/page", StringComparison.Ordinal)
                     && e.FullName != "visio/pages/pages.xml").Open();
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();

        var names = System.Text.RegularExpressions.Regex.Matches(xml, "NameU=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(n => n is not "TextBlock" and not "FreePath")
            .Distinct()
            .ToList();

        return (string.Join(",", names), path);
    }

    [Theory]
    [InlineData("database", "Process")]
    [InlineData("decision", "Diamond")]
    [InlineData("process", "Rectangle")]
    public void TheRoleSelectsTheMaster(string role, string expected)
    {
        var (nameU, file) = Write(role, """{"specVersion":"1.0"}""");

        try { Assert.Contains(expected, nameU); }
        finally { File.Delete(file); }
    }

    private static (string NameU, string File) WriteExample(string scenario, string specJson)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VsdxEngine.sln")))
            root = root.Parent;

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(root!.FullName, "examples", "realworld", scenario + ".ir.json")), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-pal-{Guid.NewGuid():N}.vsdx");

        new ProtocolRunner().Execute(ir, spec, path);
        return ("", path);
    }

    [Fact]
    public void DifferentRolesProduceDifferentShapes()
    {
        // The claim under test: the shape does not follow the role. It does.
        var (database, a) = Write("database", """{"specVersion":"1.0"}""");
        var (decision, b) = Write("decision", """{"specVersion":"1.0"}""");

        try { Assert.NotEqual(database, decision); }
        finally { File.Delete(a); File.Delete(b); }
    }

    [Fact]
    public void TwoPalettesRenderDifferentlyOnADiagramWithRoles()
    {
        // The verification the matrix said was missing. Run on a real diagram rather than a single bare
        // node: a one-node page with no container renders white-on-black under every palette, because
        // there is no role colour to apply. That is a property of the fixture, not of the palette.
        var (_, entered) = WriteExample("cbo", """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");
        var (_, midnight) = WriteExample("cbo", """{"specVersion":"1.0","theme":{"palette":"midnight"}}""");

        try
        {
            var engine = new VsdxEngine();

            var a = Path.ChangeExtension(entered, ".png");
            var b = Path.ChangeExtension(midnight, ".png");

            engine.Render(new RenderRequest { InputPath = entered, OutputPath = a, Format = "png" });
            engine.Render(new RenderRequest { InputPath = midnight, OutputPath = b, Format = "png" });

            Assert.NotEqual(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a))),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(b))));

            File.Delete(a);
            File.Delete(b);
        }
        finally { File.Delete(entered); File.Delete(midnight); }
    }
}
