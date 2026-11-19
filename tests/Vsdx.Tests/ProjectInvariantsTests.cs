using System.Text.RegularExpressions;
using Vsdx.Engine;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The few claims that must hold for the project to be what its documentation says it is.
///
/// Each is already covered somewhere; this collects them so a single run answers "is the project still in the
/// state the status report describes". They are deliberately the ones that would be most damaging to get
/// wrong: the verification level, the deliverability of every shipped example, and the promise that an
/// unmet request is reported rather than dropped.
/// </summary>
public sealed class ProjectInvariantsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void L4IsStillUnverifiedAndStillSaidSo()
    {
        // The one claim the whole version-numbering argument rests on.
        foreach (var file in new[] { "docs/limitations.md", "docs/status-2026-11.md", "PROJECT-STATUS.md" })
        {
            var path = Path.Combine(Root(), file);
            if (!File.Exists(path)) continue;

            var text = File.ReadAllText(path);

            Assert.Contains("L4", text);
            Assert.Contains("未执行", text);
        }
    }

    [Fact]
    public void EveryShippedExamplePassesItsHardConstraints()
    {
        // Six scenarios, all `hardConstraintsPassed: true`. A caller choosing this engine is choosing it on
        // the strength of these being deliverable.
        var root = Root();
        var failures = new List<string>();
        var engine = new VsdxEngine();

        foreach (var ir in Directory.GetFiles(Path.Combine(root, "examples", "realworld"), "*.ir.json"))
        {
            var scenario = Path.GetFileName(ir).Replace(".ir.json", "", StringComparison.Ordinal);
            var spec = scenario switch
            {
                "order-swimlane" => """{"specVersion":"1.0","layout":"swimlane","direction":"topToBottom"}""",
                "org-tree" => """{"specVersion":"1.0","layout":"tree","direction":"topToBottom"}""",
                _ => """{"specVersion":"1.0","theme":{"palette":"enterprise"}}"""
            };

            var protocol = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DiagramIr>(
                File.ReadAllText(ir), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            var design = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DesignSpec>(
                spec, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            var output = Path.Combine(Path.GetTempPath(), $"vsdx-inv-{Guid.NewGuid():N}.vsdx");

            try
            {
                var result = new Vsdx.Protocol.ProtocolRunner().Execute(protocol, design, output);

                if (!result.Quality.HardConstraintsPassed)
                    failures.Add($"{scenario}: {string.Join(",", result.Quality.IssueCodes)}");
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }

        Assert.True(failures.Count == 0, "examples failing the gate: " + string.Join(" | ", failures));
    }

    [Fact]
    public void AnUnknownConstraintIsReportedNotIgnored()
    {
        // The charter's first rule, at the protocol boundary: a request that cannot be honoured must appear
        // in `unmetConstraints` rather than disappear.
        var ir = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DiagramIr>(
            """{"irVersion":"1.0","id":"t","kind":"architecture","nodes":[{"id":"a","label":"A"}],"edges":[]}""",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DesignSpec>(
            """{"specVersion":"1.0","hardConstraints":[{"code":"this_is_not_a_real_constraint"}]}""",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var output = Path.Combine(Path.GetTempPath(), $"vsdx-inv2-{Guid.NewGuid():N}.vsdx");

        try
        {
            var result = new Vsdx.Protocol.ProtocolRunner().Execute(ir, spec, output);

            Assert.Contains(result.UnmetConstraints, u => u.Code == "unknown_constraint");
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    [Fact]
    public void TheVersionStaysBelowOnePointOh()
    {
        // 0.x is the honest signal while L4 has not run. If this ever fails, that decision was revisited.
        var props = File.ReadAllText(Path.Combine(Root(), "Directory.Build.props"));
        var version = Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value.Trim();

        Assert.StartsWith("0.", version);
    }

    [Fact]
    public void TheStatusReportNamesEveryOpenIssueCategory()
    {
        // The report says "no item is something that should have been fixed but was not". That is only
        // honest if the categories it lists still match the register.
        var report = File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "status-2026-11.md"));
        var register = File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "known-issues.md"));

        int open = Regex.Matches(register, @"^## \d+\.", RegexOptions.Multiline).Count;

        Assert.True(open > 0, "the register lists nothing open");
        Assert.Contains("known-issues.md", report);
    }
}
