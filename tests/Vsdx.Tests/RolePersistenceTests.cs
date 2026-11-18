using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A node's semantic role must survive a round-trip, because everything downstream depends on it.
///
/// `Node.Type` was written by nothing and read by nothing, so it disappeared on the first export.
/// The damage is not cosmetic: roleColors and palette lookup both key off the role, so a model read
/// back from a generated file could never be re-styled. The fill happened to survive as a literal
/// colour, which is exactly what makes the failure easy to miss — the picture looks right while the
/// meaning behind it is gone.
///
/// This matters for the project's core promise: hand the user a file they (or an agent) can keep
/// editing. Losing the role turns a semantic model into a bag of hard-coded colours.
/// </summary>
public sealed class RolePersistenceTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string RoleIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "nodes":[
       {"id":"db","label":"DB","role":"database"},
       {"id":"svc","label":"Svc","role":"service"},
       {"id":"gw","label":"GW","role":"gateway"}],
     "edges":[{"id":"e","from":"svc","to":"db"}]}
    """;

    private static Diagram Generate(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(RoleIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-role-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, spec, path);

        try
        {
            return new VsdxEngine().Read(path).Diagram;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RolesSurviveTheFirstRoundTrip()
    {
        var diagram = Generate("""{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");

        var roles = diagram.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Type, StringComparer.Ordinal);

        Assert.Equal("database", roles["db"]);
        Assert.Equal("service", roles["svc"]);
        Assert.Equal("gateway", roles["gw"]);
    }

    [Fact]
    public void RolesSurviveASecondSave()
    {
        // The realistic editing loop: generate, read, edit, save again.
        var ir = JsonSerializer.Deserialize<DiagramIr>(RoleIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var first = Path.Combine(Path.GetTempPath(), $"vsdx-role1-{Guid.NewGuid():N}.vsdx");
        var second = Path.Combine(Path.GetTempPath(), $"vsdx-role2-{Guid.NewGuid():N}.vsdx");

        var engine = new VsdxEngine();
        new ProtocolRunner(engine).Execute(ir, spec, first);

        try
        {
            var model = engine.Read(first).Diagram;
            engine.Update(first, model, second);

            var reread = engine.Read(second).Diagram;
            var roles = reread.Pages[0].Nodes.ToDictionary(n => n.Id, n => n.Type, StringComparer.Ordinal);

            Assert.Equal("database", roles["db"]);
            Assert.Equal("service", roles["svc"]);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void ARecolouredModelCanStillBeRestyledByRole()
    {
        // The practical consequence: roleColors must still work on a model read back from disk.
        var ir = JsonSerializer.Deserialize<DiagramIr>(RoleIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-role3-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, spec, path);

        try
        {
            var engine = new VsdxEngine();
            var model = engine.Read(path).Diagram;

            var restyle = JsonSerializer.Deserialize<DesignSpec>(
                """{"specVersion":"1.0","theme":{"roleColors":{"database":"#FF0000"}}}""", Json)!;

            DesignSpecApplier.Apply(model, restyle);

            var db = model.Pages[0].Nodes.Single(n => n.Id == "db");
            Assert.Equal("#FF0000", db.Style?.Fill);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NodesWithoutARoleStillRoundTripCleanly()
    {
        // No role is a legitimate state; it must not turn into an empty string or a stray cell.
        const string plainIr = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}],"edges":[]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(plainIr, Json)!;
        var path = Path.Combine(Path.GetTempPath(), $"vsdx-role4-{Guid.NewGuid():N}.vsdx");
        new ProtocolRunner().Execute(ir, null, path);

        try
        {
            var node = new VsdxEngine().Read(path).Diagram.Pages[0].Nodes.Single(n => n.Id == "a");
            Assert.True(node.Type is null || node.Type.Length == 0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
