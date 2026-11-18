using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A text block's role must survive the round trip, whatever its id happens to be.
///
/// The reader infers the role from the shape's `Name` prefix — `title…` becomes "title", `note…` becomes
/// "note", anything else falls through to "caption". It never reads the role that was stored. So the same
/// block declared `{"id":"tb","role":"note"}` comes back as `caption`, while renaming the id to
/// `title-x` silently changes the role to "title" with the IR unchanged.
///
/// That makes the id a functional parameter: two diagrams with identical content and different block ids
/// produce different typography, and nothing reports the change.
/// </summary>
public sealed class TextBlockRoleFidelityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static Diagram RoundTrip(string id, string role)
    {
        var irJson = "{"
            + "\"irVersion\":\"1.0\",\"id\":\"t\",\"kind\":\"architecture\","
            + "\"nodes\":[{\"id\":\"a\",\"label\":\"A\"}],\"edges\":[],"
            + "\"textBlocks\":[{\"id\":\"" + id + "\",\"text\":\"文本\",\"role\":\"" + role + "\","
            + "\"box\":{\"x\":3,\"y\":5,\"width\":4,\"height\":0.6}}]}";

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>("""{"specVersion":"1.0"}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-role-{Guid.NewGuid():N}.vsdx");

        try
        {
            new ProtocolRunner().Execute(ir, spec, path);
            return new VsdxEngine().Read(path).Diagram;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("title")]
    [InlineData("subtitle")]
    [InlineData("legend")]
    [InlineData("note")]
    [InlineData("annotation")]
    [InlineData("caption")]
    public void TheRoleSurvivesWhateverTheIdIs(string role)
    {
        // A neutral id: no prefix the reader could infer from, so only the stored role can survive.
        var written = RoundTrip("tb1", role);

        var block = written.Pages[0].TextBlocks.Single();

        Assert.Equal(role, block.Role);
    }

    [Fact]
    public void TheIdDoesNotDecideTheRole()
    {
        // Two runs, identical except for the id. If the reader infers from the name, these differ.
        var asPlain = RoundTrip("tb1", "note");
        var asTitleish = RoundTrip("title-1", "note");

        Assert.Equal("note", asPlain.Pages[0].TextBlocks.Single().Role);
        Assert.Equal("note", asTitleish.Pages[0].TextBlocks.Single().Role);
    }
}
