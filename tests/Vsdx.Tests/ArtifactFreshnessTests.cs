using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every committed artefact must be newer than the IR it came from, and must match what the engine produces now.
///
/// A third visual review caught this before any test did: `netflix-active-active.png` and `.vsdx` were twelve
/// minutes older than the `.ir.json`, because I edited the graph and regenerated nothing. The committed image still
/// showed the mis-ordered region that the accompanying document claimed to have fixed — artefact, document and
/// conclusion contradicting each other, with the checker reporting a clean score the whole time.
///
/// `ExampleArtifactTests` already compares images against a fresh render for four scenarios, but
/// `netflix-active-active` was not among them, so nothing failed. This file checks the whole set rather than a
/// hand-maintained list: a scenario added tomorrow is covered without anyone remembering to add it.
/// </summary>
public sealed class ArtifactFreshnessTests
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

    private static string Examples() => Path.Combine(Root(), "examples", "realworld");

    private static IEnumerable<string> Scenarios() =>
        Directory.GetFiles(Examples(), "*.ir.json")
            .Select(f => Path.GetFileName(f).Replace(".ir.json", "", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

    [Fact]
    public void EveryScenarioHasACommittedImageAndPackage()
    {
        var missing = new List<string>();

        foreach (var scenario in Scenarios())
        {
            foreach (var extension in new[] { ".vsdx", ".png" })
            {
                var path = Path.Combine(Examples(), scenario + extension);
                if (!File.Exists(path)) missing.Add(scenario + extension);
            }
        }

        Assert.True(missing.Count == 0,
            "scenarios without committed artefacts: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoArtefactPredatesItsSource()
    {
        // The check that would have caught the netflix staleness. Modification time is a weak signal — a checkout
        // resets every timestamp — so this is a backstop, not the primary guard. The render comparison below is
        // what actually proves the image is current.
        var stale = new List<string>();

        foreach (var scenario in Scenarios())
        {
            var source = new FileInfo(Path.Combine(Examples(), scenario + ".ir.json"));

            foreach (var extension in new[] { ".vsdx", ".png" })
            {
                var artefact = new FileInfo(Path.Combine(Examples(), scenario + extension));
                if (!artefact.Exists) continue;

                if (artefact.LastWriteTimeUtc < source.LastWriteTimeUtc)
                    stale.Add($"{scenario}{extension} ({artefact.LastWriteTimeUtc:u} < {source.LastWriteTimeUtc:u})");
            }
        }

        Assert.True(stale.Count == 0,
            "artefacts older than their IR: " + string.Join("; ", stale));
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void ThePackageReflectsTheCurrentEngine(string scenario)
    {
        // The real guard: build the diagram from the IR now and compare the page geometry with what was committed.
        var fresh = Path.Combine(Path.GetTempPath(), $"fresh-{scenario}-{Guid.NewGuid():N}.vsdx");

        try
        {
            var ir = JsonSerializer.Deserialize<DiagramIr>(
                File.ReadAllText(Path.Combine(Examples(), scenario + ".ir.json")), Json)!;

            var spec = JsonSerializer.Deserialize<DesignSpec>(SpecFor(scenario), Json)!;
            new ProtocolRunner().Execute(ir, spec, fresh);

            var committed = new VsdxEngine()
                .Read(Path.Combine(Examples(), scenario + ".vsdx")).Diagram;

            var produced = new VsdxEngine().Read(fresh).Diagram;

            Assert.Equal(Describe(committed), Describe(produced));
        }
        finally { if (File.Exists(fresh)) File.Delete(fresh); }
    }

    public static TheoryData<string> AllScenarios()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in Scenarios()) data.Add(scenario);
        return data;
    }

    /// <summary>
    /// The spec each example is generated with. Two scenarios are not plain `layered`, and using the wrong one
    /// would make this test fail for a reason that has nothing to do with staleness.
    /// </summary>
    private static string SpecFor(string scenario) => scenario switch
    {
        "order-swimlane" =>
            """{"specVersion":"1.0","theme":{"palette":"enterprise"},"layout":"swimlane","direction":"topToBottom"}""",
        "org-tree" =>
            """{"specVersion":"1.0","theme":{"palette":"enterprise"},"layout":"tree","direction":"topToBottom"}""",
        "netflix-active-active" =>
            """{"specVersion":"1.0","layout":"layered","direction":"topToBottom","theme":{"palette":"enterprise"}}""",
        _ => """{"specVersion":"1.0","theme":{"palette":"enterprise"}}"""
    };

    /// <summary>Node positions and sizes, rounded — enough to tell a stale drawing from a current one.</summary>
    private static string Describe(Vsdx.Model.Diagram diagram)
    {
        var page = diagram.Pages[0];

        var nodes = page.Nodes
            .OrderBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => $"{n.Id}:{n.Bounds.X:F2},{n.Bounds.Y:F2},{n.Bounds.W:F2},{n.Bounds.H:F2}");

        var groups = page.Groups
            .OrderBy(g => g.Id, StringComparer.Ordinal)
            .Select(g => $"{g.Id}:{g.Bounds?.X:F2},{g.Bounds?.Y:F2},{g.Bounds?.W:F2},{g.Bounds?.H:F2}");

        return string.Join("|", nodes.Concat(groups));
    }
}
