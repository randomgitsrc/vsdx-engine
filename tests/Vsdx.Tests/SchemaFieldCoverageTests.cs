using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The published schema must accept every field the model has, because it declares
/// `additionalProperties: false` on each object.
///
/// It did not. `page` allowed twelve fields and omitted `textBlocks` and `paths` — the two that carry every
/// title, note, legend and hand-drawn outline. `group` omitted `order`, and `node` omitted `sourceMaster`.
///
/// A caller validating a model against this schema would have been told a diagram with a title block was
/// invalid, while the engine produced it without complaint. That is the same class of defect as the enums
/// that omitted `paper` and `swimlane` — closed by ignoring the checker, or by not writing titles.
/// </summary>
public sealed class SchemaFieldCoverageTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static JsonElement Schema() =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Root(), "schemas", "diagram.schema.json"))).RootElement;

    private static HashSet<string> SchemaFields(string definition) =>
        (Schema().GetProperty("$defs").GetProperty(definition).GetProperty("properties")
            .EnumerateObject().Select(p => p.Name))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Property names of a model class, camelCased.</summary>
    private static HashSet<string> ModelFields(string className)
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Model", "DiagramModel.cs"));

        int start = source.IndexOf($"public sealed class {className}\n", StringComparison.Ordinal);
        Assert.True(start > 0, $"{className} not found in the model");

        int next = source.IndexOf("public sealed class ", start + 10, StringComparison.Ordinal);
        var body = source[start..(next > 0 ? next : source.Length)];

        return Regex.Matches(body, @"public [\w<>,\[\]?\s]+ (\w+) \{ get")
            .Select(m => char.ToLowerInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value[1..])
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Page", "page")]
    [InlineData("Node", "node")]
    [InlineData("Edge", "edge")]
    [InlineData("Group", "group")]
    public void TheSchemaAcceptsEveryModelField(string className, string definition)
    {
        var allowed = SchemaFields(definition);

        // `additionalProperties: false` makes an omission a rejection, not a silent pass.
        var readOnly = new HashSet<string>(StringComparer.Ordinal) { "visioId", "visioShapeId" };

        var missing = ModelFields(className)
            .Where(f => !allowed.Contains(f) && !readOnly.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{definition} rejects fields the model has: " + string.Join(", ", missing));
    }

    [Fact]
    public void EachObjectStillRejectsUnknownFields()
    {
        // The check above is only meaningful while `additionalProperties` is false.
        foreach (var definition in new[] { "page", "node", "edge", "group" })
        {
            var value = Schema().GetProperty("$defs").GetProperty(definition)
                .GetProperty("additionalProperties");

            Assert.False(value.GetBoolean(), $"{definition} now accepts unknown fields");
        }
    }

    [Fact]
    public void TextBlocksAndPathsAreDescribed()
    {
        // The two that were missing carry every title, note and custom outline.
        var page = Schema().GetProperty("$defs").GetProperty("page").GetProperty("properties");

        Assert.True(page.TryGetProperty("textBlocks", out _), "the schema does not describe textBlocks");
        Assert.True(page.TryGetProperty("paths", out _), "the schema does not describe paths");
    }
}
