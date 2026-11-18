using System.IO.Compression;
using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The same input must produce the same drawing, and `diff` must compare drawings rather than bytes.
///
/// Running the same IR three times produces three different files — measured, the MD5s all differ — but
/// only one part differs: `docProps/custom.xml`, and within it only the `created`/`modified` timestamps.
/// Every page, master and geometry part is identical.
///
/// That distinction matters for two reasons. A byte comparison would call two identical drawings
/// different, and a `diff` that ignored the timestamps by ignoring whole parts would miss real changes.
/// Measured: two runs report `identical: true`, while a palette swap reports `identical: false` with the
/// fill colours named.
/// </summary>
public sealed class DeterminismTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Run(string irJson, string specJson, out ExecutionResult result)
    {
        var ir = System.Text.Json.JsonSerializer.Deserialize<DiagramIr>(irJson, Json)!;
        var spec = System.Text.Json.JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-det-{Guid.NewGuid():N}.vsdx");
        result = new ProtocolRunner().Execute(ir, spec, path);

        return path;
    }

    private static Dictionary<string, byte[]> Parts(string path)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using var zip = ZipFile.OpenRead(path);

        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            parts[entry.FullName] = buffer.ToArray();
        }

        return parts;
    }

    private const string Ir = """
    {"irVersion":"1.0","id":"t","kind":"architecture",
     "containers":[{"id":"g","label":"G"}],
     "nodes":[{"id":"a","label":"A","role":"service","containerId":"g"},
              {"id":"b","label":"B","role":"database","containerId":"g"}],
     "edges":[{"id":"e","from":"a","to":"b"}]}
    """;

    private const string Spec = """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""";

    [Fact]
    public void TwoRunsDifferOnlyInTheirTimestamps()
    {
        var first = Run(Ir, Spec, out _);
        var second = Run(Ir, Spec, out _);

        try
        {
            var a = Parts(first);
            var b = Parts(second);

            Assert.Equal(a.Keys.OrderBy(k => k, StringComparer.Ordinal),
                         b.Keys.OrderBy(k => k, StringComparer.Ordinal));

            var differing = a.Keys.Where(k => !a[k].SequenceEqual(b[k])).ToList();

            Assert.Equal(new[] { "docProps/custom.xml" }, differing);

            // And within that part, only the timestamps.
            var text = System.Text.Encoding.UTF8.GetString(a["docProps/custom.xml"]);
            var other = System.Text.Encoding.UTF8.GetString(b["docProps/custom.xml"]);

            int ta = text.IndexOf("\"created\"", StringComparison.Ordinal);
            int tb = other.IndexOf("\"created\"", StringComparison.Ordinal);

            Assert.True(ta > 0 && tb > 0);
            Assert.Equal(text[..ta], other[..tb]);
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public void DiffTreatsTwoRunsAsIdentical()
    {
        // The behaviour a caller depends on: regenerating a file is not a change.
        var first = Run(Ir, Spec, out _);
        var second = Run(Ir, Spec, out _);

        try
        {
            var diff = DiagramDiff.Compute(
                new VsdxEngine().Read(first).Diagram,
                new VsdxEngine().Read(second).Diagram,
                first, second);

            Assert.True(diff.Identical, string.Join("; ", diff.Changes.Select(c => $"{c.Kind} {c.Target}")));
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public void DiffStillDetectsARealChange()
    {
        // The other direction: it must not become identical-everywhere to satisfy the test above.
        var plain = Run(Ir, Spec, out _);
        var recoloured = Run(Ir, """{"specVersion":"1.0","theme":{"palette":"midnight"}}""", out _);

        try
        {
            var diff = DiagramDiff.Compute(
                new VsdxEngine().Read(plain).Diagram,
                new VsdxEngine().Read(recoloured).Diagram,
                plain, recoloured);

            Assert.False(diff.Identical);
            Assert.Contains(diff.Changes, c => c.Property == "fill");
        }
        finally
        {
            if (File.Exists(plain)) File.Delete(plain);
            if (File.Exists(recoloured)) File.Delete(recoloured);
        }
    }

    [Fact]
    public void GeometryIsByteIdenticalAcrossRuns()
    {
        // Narrower than the first test but worth stating: the parts that carry the drawing never vary.
        var first = Run(Ir, Spec, out _);
        var second = Run(Ir, Spec, out _);

        try
        {
            var a = Parts(first);
            var b = Parts(second);

            foreach (var key in a.Keys.Where(k => k.StartsWith("visio/", StringComparison.Ordinal)))
                Assert.True(a[key].SequenceEqual(b[key]), $"{key} differs between runs");
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }
}
