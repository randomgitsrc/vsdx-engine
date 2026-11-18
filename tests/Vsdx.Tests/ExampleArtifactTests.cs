using System.Security.Cryptography;
using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The committed example images must match what the engine produces today.
///
/// Several rounds of layout fixes changed the drawings — heading clearance, content centring, paper
/// orientation — while the PNGs beside them stayed as they were rendered before those fixes. A reader
/// comparing the documented score with the picture would be looking at two different drawings.
///
/// Every render is byte-identical across runs (only `docProps/custom.xml` timestamps vary, and that part
/// is not rendered), so the PNG can be compared exactly.
/// </summary>
public sealed class ExampleArtifactTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Render(string scenario, string specJson)
    {
        var examples = Path.Combine(Root(), "examples", "realworld");

        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
            File.ReadAllText(Path.Combine(examples, $"{scenario}.ir.json")), Json)!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var vsdx = Path.Combine(Path.GetTempPath(), $"vsdx-art-{Guid.NewGuid():N}.vsdx");
        var png = Path.ChangeExtension(vsdx, ".png");

        try
        {
            new ProtocolRunner().Execute(ir, spec, vsdx);
            new VsdxEngine().Render(new RenderRequest
            {
                InputPath = vsdx,
                OutputPath = png,
                Format = "png"
            });

            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(png)));
        }
        finally
        {
            if (File.Exists(vsdx)) File.Delete(vsdx);
            if (File.Exists(png)) File.Delete(png);
        }
    }

    private static string Committed(string scenario)
    {
        var path = Path.Combine(Root(), "examples", "realworld", $"{scenario}.png");

        Assert.True(File.Exists(path), $"{scenario}.png is missing");

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData("cbo")]
    [InlineData("deploy-topology")]
    [InlineData("user-data-flow")]
    [InlineData("checkout-sequence")]
    public void TheCommittedImageMatchesTheEngine(string scenario)
    {
        var actual = Render(scenario,
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""");

        Assert.Equal(Committed(scenario), actual);
    }

    [Fact]
    public void TheSwimlaneImageMatches()
    {
        var actual = Render("order-swimlane",
            """{"specVersion":"1.0","theme":{"palette":"enterprise"},"layout":"swimlane","direction":"topToBottom"}""");

        Assert.Equal(Committed("order-swimlane"), actual);
    }

    [Fact]
    public void TheTreeImageMatches()
    {
        var actual = Render("org-tree",
            """{"specVersion":"1.0","theme":{"palette":"enterprise"},"layout":"tree","direction":"topToBottom"}""");

        Assert.Equal(Committed("org-tree"), actual);
    }

    [Fact]
    public void EveryScenarioHasAnImage()
    {
        var examples = Path.Combine(Root(), "examples", "realworld");

        foreach (var ir in Directory.GetFiles(examples, "*.ir.json"))
        {
            var scenario = Path.GetFileName(ir).Replace(".ir.json", "", StringComparison.Ordinal);
            var png = Path.Combine(examples, $"{scenario}.png");

            Assert.True(File.Exists(png), $"{scenario}.ir.json has no committed image");
        }
    }
}
