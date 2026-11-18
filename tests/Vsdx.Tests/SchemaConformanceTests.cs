using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The schema must accept every value the engine accepts, and reject what it rejects.
///
/// `canvas.mode=paper` and `layout=swimlane` both worked in the engine while the schema's enums
/// omitted them — so a caller validating against the published schema would reject working features,
/// and a schema-validating tool could not express them at all.
///
/// The same review found `theme.palette` documented as "declared, no reader" when it was fully
/// implemented; that is checked here too, since an enum that drifts either way misleads.
/// </summary>
public sealed class SchemaConformanceTests
{
    private static JsonElement Schema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        var path = Path.Combine(dir!.FullName, "schemas", "phase7", "design-spec.schema.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    private static string[] EnumOf(params string[] path)
    {
        var node = Schema().GetProperty("properties");
        foreach (var step in path) node = node.GetProperty(step);

        return node.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("auto")]
    [InlineData("fixed")]
    public void EveryCanvasModeIsInTheSchema(string mode)
    {
        // `paper` was implemented before it was declared.
        Assert.Contains(mode, EnumOf("canvas", "properties", "mode"));
    }

    [Theory]
    [InlineData("swimlane")]
    [InlineData("layered")]
    [InlineData("flow")]
    public void EveryLayoutStrategyIsInTheSchema(string layout)
    {
        Assert.Contains(layout, EnumOf("layout"));
    }

    [Fact]
    public void EveryPaletteNameIsInTheSchema()
    {
        var declared = EnumOf("theme", "properties", "palette");

        foreach (var name in Palettes.Names)
            Assert.Contains(name, declared);
    }

    [Fact]
    public void TheSchemaDeclaresNoPaletteTheEngineLacks()
    {
        // The other direction: a name in the schema that does not exist misleads just as much.
        foreach (var name in EnumOf("theme", "properties", "palette"))
            Assert.True(Palettes.TryGet(name, out _), $"schema declares palette '{name}', engine has no such palette");
    }

    [Fact]
    public void DocumentedPaletteNamesExist()
    {
        // `office` appeared in a source comment and does not exist.
        Assert.DoesNotContain("office", Palettes.Names);
        Assert.Equal(4, Palettes.Names.Count);
    }
}
