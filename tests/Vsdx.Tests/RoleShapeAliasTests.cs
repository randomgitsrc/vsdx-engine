using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `role` should carry a shape when the caller means one.
///
/// `ShapeForRole` maps thirteen *semantic* roles (gateway, database, queue, …) to Visio masters and falls back
/// to `Rectangle` for everything else. `MasterCatalog.Aliases` separately knows 66 shape names — including
/// `diamond`, `hexagon`, `pentagon`, `trapezoid`, `parallelogram`, `circle`.
///
/// The two do not overlap, so an IR author who writes `"role": "diamond"` meaning the shape gets a rectangle.
/// Measured: `role: diamond` through `execute` produces `NameU="Rectangle"`.
///
/// The independent agent that drew the order-system diagram reported the IR path as unable to specify shapes at
/// all. It is not that narrow — it is that the fallback drops the information instead of asking the catalogue.
/// </summary>
public sealed class RoleShapeAliasTests
{
    [Theory]
    [InlineData("diamond", "Diamond")]
    [InlineData("hexagon", "Hexagon")]
    [InlineData("parallelogram", "Parallelogram")]
    [InlineData("pentagon", "Pentagon")]
    [InlineData("trapezoid", "Trapezoid")]
    [InlineData("circle", "Circle")]
    [InlineData("square", "Square")]
    public void AShapeNameAsARoleProducesThatShape(string role, string expected)
    {
        Assert.Equal(expected, DiagramIrConverter.ShapeForRole(role));
    }

    [Theory]
    [InlineData("gateway", "Hexagon")]
    [InlineData("database", "Process")]
    [InlineData("queue", "Parallelogram")]
    [InlineData("client", "Rectangle")]
    [InlineData("decision", "Diamond")]
    public void SemanticRolesKeepTheirMappings(string role, string expected)
    {
        // The semantic table must still win: `database` means the process master, not a shape called database.
        Assert.Equal(expected, DiagramIrConverter.ShapeForRole(role));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-that-is-not-a-shape")]
    public void UnknownROlesStillFallBackToRectangle(string? role)
    {
        Assert.Equal("Rectangle", DiagramIrConverter.ShapeForRole(role));
    }

    [Fact]
    public void AShapeSetAsRoleSurvivesTheWholeIRPath()
    {
        // End to end: the mapping is only useful if the writer uses it.
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            """{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A","role":"diamond"}],"edges":[]}""",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var diagram = DiagramIrConverter.ToDiagram(ir);

        Assert.Equal("Diamond", diagram.Pages[0].Nodes[0].Shape);
    }
}
