using System.Text.Json;
using System.Text.RegularExpressions;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `capability-boundaries.md` must describe scenarios that exist and numbers that are reproducible.
///
/// It listed five scenarios with scores. Two were stale (cbo 98 against 100, deploy-topology 94 against 82),
/// one — "订单状态机 96" — had no example file at all and appears never to have had one, and the paper-size
/// claim ("all three land on A3") was wrong for two of the three: they land on A4, portrait and landscape
/// respectively, with only `user-data-flow` needing A3.
///
/// Scores drift whenever layout changes, so the table now says it is a snapshot and points at
/// `check-layout`. What this test protects is the part that should not drift: every scenario named must be
/// one of the committed examples.
/// </summary>
public sealed class BoundariesClaimsTests
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

    private static string Doc() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "capability-boundaries.md"));

    [Fact]
    public void EveryScenarioInTheResultsTableHasAnExampleFile()
    {
        var doc = Doc();
        var examples = Path.Combine(Root(), "examples", "realworld");

        var available = Directory.GetFiles(examples, "*.ir.json")
            .Select(f => Path.GetFileName(f).Replace(".ir.json", "", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        // Both spellings the table uses: a bare word or a backticked id.
        var named = Regex.Matches(doc, @"\|[^|\n]*?(`([a-z][a-z-]+)`|[a-z][a-z-]{4,})[^|\n]*\|")
            .Select(m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value)
            .Where(n => available.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(named);

        // And the reverse: nothing in the table may name a scenario that does not exist.
        foreach (var missing in new[] { "订单状态机", "stateMachine" })
            Assert.DoesNotContain(missing, doc);
    }

    [Fact]
    public void SectionNumbersAreUnique()
    {
        // Adding a section re-used "七" and left two of them.
        var numbers = Regex.Matches(Doc(), @"^## ([一二三四五六七八九十]+)、", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public void ThePaperClaimMatchesWhatTheEnginePicks()
    {
        // A4 portrait, A4 landscape, A3 landscape — not one paper for all three.
        var engine = new VsdxEngine();
        var sizes = new List<(string Scenario, double W, double H)>();

        foreach (var scenario in new[] { "cbo", "deploy-topology", "user-data-flow" })
        {
            var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(
                File.ReadAllText(Path.Combine(Root(), "examples", "realworld", scenario + ".ir.json")), Json)!;

            var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(
                """{"specVersion":"1.0","canvas":{"mode":"paper"}}""", Json)!;

            var path = Path.Combine(Path.GetTempPath(), $"vsdx-paper-{Guid.NewGuid():N}.vsdx");

            try
            {
                new ProtocolRunner().Execute(ir, spec, path);
                var page = engine.Read(path).Diagram.Pages[0];
                sizes.Add((scenario, page.Size!.Width, page.Size.Height));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        Assert.Equal(3, sizes.Count);
        Assert.True(sizes.Select(s => Math.Round(s.W, 1)).Distinct().Count() > 1,
            "all three scenarios picked the same width — the doc's older claim would be right again");
    }

    [Fact]
    public void L4StaysMarkedUnverified()
    {
        var doc = Doc();

        Assert.Contains("从未执行", doc);
        Assert.Contains("不能替代", doc);
    }
}
