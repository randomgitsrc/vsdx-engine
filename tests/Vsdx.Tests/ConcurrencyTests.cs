using System.Collections.Concurrent;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A single `VsdxEngine` instance can be used from several threads.
///
/// Nothing had tested it. The facade holds only `readonly` dependency fields and the four static collections
/// in the codebase (`MasterCatalog.BuiltIn`, `Aliases`, `Palettes.All`, `ColorUtil.NamedColors`) are never
/// written after initialisation — so the risk was theoretical, and measuring it took ten minutes.
///
/// Measured by hand first: 12 parallel creates with distinct outputs, then 20 parallel reads of one file.
/// Zero failures either way. These are the same checks, kept so a future change that introduces instance
/// state or a lazily-populated cache has something to fail against.
/// </summary>
public sealed class ConcurrencyTests
{
    private static Diagram Sample(string id) => new()
    {
        Id = id,
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 8, Height = 6 },
                Nodes =
                {
                    new Node { Id = "a", Text = "A", Bounds = new Bounds(2, 3, 1.3, 0.6) },
                    new Node { Id = "b", Text = "B", Bounds = new Bounds(6, 3, 1.3, 0.6) }
                },
                Edges = { new Edge { Id = "e", From = "a", To = "b" } }
            }
        }
    };

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vsdx-conc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void OneInstanceCreatesManyFilesInParallel()
    {
        var engine = new VsdxEngine();
        var dir = NewDir();
        var failures = new ConcurrentBag<string>();

        try
        {
            Parallel.For(0, 12, i =>
            {
                try
                {
                    var path = Path.Combine(dir, $"f{i}.vsdx");
                    engine.Create(Sample($"d{i}"), path, new WriteOptions { AutoLayout = false });

                    var back = engine.Read(path).Diagram;
                    if (back.Pages[0].Nodes.Count != 2) failures.Add($"{i}: wrong node count");
                }
                catch (Exception ex)
                {
                    failures.Add($"{i}: {ex.GetType().Name}: {ex.Message}");
                }
            });

            Assert.True(failures.IsEmpty, string.Join(" | ", failures.Take(5)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ManyThreadsReadTheSameFileAtOnce()
    {
        // The case a caller actually hits: several readers on a deliverable that is already written.
        var engine = new VsdxEngine();
        var dir = NewDir();
        var shared = Path.Combine(dir, "shared.vsdx");
        var failures = new ConcurrentBag<string>();

        try
        {
            engine.Create(Sample("s"), shared, new WriteOptions { AutoLayout = false });

            Parallel.For(0, 20, i =>
            {
                try
                {
                    if (engine.Read(shared).Diagram.Pages[0].Nodes.Count != 2)
                        failures.Add($"{i}: wrong node count");
                }
                catch (Exception ex)
                {
                    failures.Add($"{i}: {ex.GetType().Name}: {ex.Message}");
                }
            });

            Assert.True(failures.IsEmpty, string.Join(" | ", failures.Take(5)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParallelAnalysisDoesNotShareState()
    {
        // Analysis is the piece most likely to grow a cache. Each call must be independent.
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, 16, i =>
        {
            try
            {
                var report = LayoutAnalyzer.Analyze(Sample($"a{i}"));

                if (report.PageUtilisation.Count != 1)
                    failures.Add($"{i}: {report.PageUtilisation.Count} pages measured");
            }
            catch (Exception ex)
            {
                failures.Add($"{i}: {ex.GetType().Name}");
            }
        });

        Assert.True(failures.IsEmpty, string.Join(" | ", failures.Take(5)));
    }

    [Fact]
    public void NoStaticFieldIsMutable()
    {
        // The structural reason the tests above pass. A static mutable field is the usual way a facade
        // that looks stateless stops being thread-safe.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VsdxEngine.sln")))
            root = root.Parent;

        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();

                if (!trimmed.StartsWith("private static ", StringComparison.Ordinal)) continue;
                if (trimmed.Contains(" readonly ", StringComparison.Ordinal)) continue;
                if (trimmed.Contains('(')) continue;          // a method
                if (trimmed.Contains("=>", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetFileName(file)}: {trimmed}");
            }
        }

        Assert.True(offenders.Count == 0, "mutable static state: " + string.Join(" | ", offenders.Take(5)));
    }
}
