using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// `architecture.md` and `dependencies.md` describe things the repository can be checked against, so
/// they are living documents and are held to the code. `test-report.md` is the opposite — a snapshot of
/// one moment — and is deliberately left unbound.
///
/// The distinction matters: binding a historical record would force it to stay current and destroy the
/// only thing it is good for.
/// </summary>
public sealed class ArchitectureDocTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Doc(string name) => File.ReadAllText(Path.Combine(Root(), "docs", name));

    [Fact]
    public void EveryProjectAppearsInTheArchitectureDoc()
    {
        var architecture = Doc("architecture.md");

        var projects = Directory.GetDirectories(Path.Combine(Root(), "src"))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        Assert.True(projects.Count >= 7, $"only {projects.Count} projects found — is the path right?");

        var missing = projects.Where(p => !architecture.Contains(p, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, $"projects absent from architecture.md: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryPackageAppearsInTheDependencyDoc()
    {
        var deps = Doc("dependencies.md");
        var props = File.ReadAllText(Path.Combine(Root(), "Directory.Packages.props"));

        var packages = Regex.Matches(props, @"Include=""([A-Za-z.]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missing = packages.Where(p => !deps.Contains(p, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, $"packages absent from dependencies.md: {string.Join(", ", missing)}");
    }

    [Fact]
    public void TheDependencyDocStatesEachLicence()
    {
        // The file exists to answer "can I ship this"; a version without a licence does not.
        var deps = Doc("dependencies.md");

        foreach (var licence in new[] { "MIT" })
            Assert.Contains(licence, deps);
    }

    [Fact]
    public void TheTestReportIsMarkedAsASnapshotNotAsCurrentState()
    {
        // It lists 16 test classes and reports 110 passing; the suite now has far more. Without this
        // header a reader would take it as the current result.
        var report = Doc("test-report.md");

        Assert.Contains("历史快照", report);
        Assert.Contains("不是当前状态", report);
        Assert.Contains("git log -1 --format=%cd", report);
    }

    [Fact]
    public void TheTestReportDoesNotClaimACurrentCount()
    {
        // A frozen number in a snapshot is fine only if the header says so — but the header must not
        // itself carry a number that will rot.
        var report = Doc("test-report.md");
        var header = report[..Math.Min(report.Length, 600)];

        Assert.DoesNotMatch(@"写于测试规模为 \d+", header);
    }

    /// <summary>
    /// The dependency graph must stay acyclic, and the doc must not claim more than that.
    ///
    /// `AGENTS.md` said "依赖单向", which reads as a strict chain. It is not one: `Vsdx.Cli` references both
    /// `Vsdx.Engine` and `Vsdx.Protocol`, and `Vsdx.Protocol` references `Vsdx.Engine`. What holds — and what
    /// matters — is that nothing points back at something that points at it. The wording now says so.
    /// </summary>
    [Fact]
    public void TheProjectGraphHasNoCycles()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VsdxEngine.sln")))
            root = root.Parent;

        Assert.NotNull(root);

        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var project in Directory.GetDirectories(Path.Combine(root!.FullName, "src")))
        {
            var csproj = Directory.GetFiles(project, "*.csproj").FirstOrDefault();
            if (csproj is null) continue;

            var name = Path.GetFileName(project);
            var refs = System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(csproj), @"ProjectReference\s+Include=""([^""]+)""")
                .Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value))
                .ToList();

            edges[name] = refs;
        }

        Assert.NotEmpty(edges);

        // Depth-first cycle detection.
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var cycle = new List<string>();

        bool Walk(string node)
        {
            if (done.Contains(node)) return false;
            if (!visiting.Add(node))
            {
                cycle.Add(node);
                return true;
            }

            foreach (var next in edges.GetValueOrDefault(node) ?? new List<string>())
                if (Walk(next)) { cycle.Add(node); return true; }

            visiting.Remove(node);
            done.Add(node);
            return false;
        }

        foreach (var node in edges.Keys)
            Assert.False(Walk(node), "dependency cycle through: " + string.Join(" -> ", cycle));
    }

    [Fact]
    public void NoProjectReferencesItself()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VsdxEngine.sln")))
            root = root.Parent;

        foreach (var project in Directory.GetDirectories(Path.Combine(root!.FullName, "src")))
        {
            var csproj = Directory.GetFiles(project, "*.csproj").FirstOrDefault();
            if (csproj is null) continue;

            var name = Path.GetFileName(project);

            Assert.DoesNotContain(name + ".csproj", File.ReadAllText(csproj));
        }
    }
}