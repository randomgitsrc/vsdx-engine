using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The matrix must not record a capability as missing when the code implements it.
///
/// Autofixing the previous round's gaps exposed two entries that had drifted the wrong way: multi-page
/// was marked ❌ "IR has no pages" while `DiagramIr.Pages` exists and two-page output validates, and
/// Z-order was marked ⚠️ "read but never written" while `VisioZOrderBridge` exists specifically to fix
/// that audit finding.
///
/// Understating capability is as harmful as overstating it: a reader would rebuild what is already
/// there, or distrust a guarantee that holds.
/// </summary>
public sealed class MatrixAccuracyTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Matrix() =>
        File.ReadAllText(Path.Combine(Root(), "docs", "phase8", "capability-matrix.md"));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(Root(), relative));

    [Fact]
    public void MultiPageIsNotRecordedAsUnsupported()
    {
        var matrix = Matrix();

        // The IR has the field; the matrix used to say it did not.
        Assert.Contains("List<IrPage> Pages", Source("src/Vsdx.Protocol/DiagramIr.cs"));
        Assert.DoesNotMatch(@"\| 多页 \|[^\n]*❌", matrix);
        Assert.DoesNotMatch(@"\| 多页 \|[^\n]*协议层不支持", matrix);
    }

    [Fact]
    public void ZOrderIsNotRecordedAsLost()
    {
        var matrix = Matrix();

        Assert.True(File.Exists(Path.Combine(Root(), "src", "Vsdx.Adapters", "VisioZOrderBridge.cs")));
        Assert.DoesNotMatch(@"\| Z-order \|[^\n]*从不写入", matrix);
        Assert.DoesNotMatch(@"\| Z-order \|[^\n]*每次保存丢失", matrix);
    }

    [Fact]
    public void GeometryPreservationIsNotRecordedAsMissing()
    {
        Assert.Contains("VisioGeometryPreserver",
            Source("src/Vsdx.Adapters/OfficeImoIncrementalEditor.cs"));
    }

    [Fact]
    public void DuplicateCapabilityRowsDoNotContradictEachOther()
    {
        // `多页`, `Z-order` and `稳定 ID` each appear twice in different sections; that is a legitimate
        // cross-reference, but only if the two agree.
        //
        // The experiment table in §3 is excluded: its ✅ means "the format can carry it", which is a
        // different claim from the capability tables' status column — images are ✅ there and 🔬 here,
        // and both are correct.
        var matrix = Matrix();
        // The §3 experiment table starts at its header row and runs to the end of that block.
        int tableAt = matrix.IndexOf("| 项 | 格式层 | Engine 产品层 |", StringComparison.Ordinal);
        int tableEnd = tableAt > 0
            ? matrix.IndexOf("**自由路径已从", tableAt, StringComparison.Ordinal)
            : -1;

        if (tableAt > 0 && tableEnd > tableAt)
            matrix = matrix[..tableAt] + matrix[tableEnd..];

        var lines = matrix.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal));

        var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var cells = line.Split('|');
            if (cells.Length < 4) continue;

            var name = cells[1].Trim();
            var status = cells[2].Trim();

            var marker = new[] { "✅", "⚠️", "🔬", "📄", "🧪", "❌", "🚫", "⛔" }
                .FirstOrDefault(status.StartsWith);

            if (marker is null) continue;

            if (!byName.TryGetValue(name, out var list)) byName[name] = list = new List<string>();
            list.Add(marker);
        }

        var contradictions = byName
            .Where(kv => kv.Value.Distinct().Count() > 1)
            .Select(kv => $"{kv.Key}: {string.Join("/", kv.Value)}")
            .ToList();

        Assert.True(contradictions.Count == 0, string.Join("; ", contradictions));
    }

    [Fact]
    public void TheDistributionSummaryNamesWhatIsActuallyMissing()
    {
        // It used to blame "几何/Z-order" for gaps that have since been closed.
        var matrix = Matrix();
        int at = matrix.IndexOf("**分布解读：**", StringComparison.Ordinal);

        Assert.True(at > 0, "the distribution summary is missing");
        var summary = matrix[at..matrix.IndexOf("\n\n", at, StringComparison.Ordinal)];

        Assert.Contains("图片", summary);

        // The summary says the remaining gaps are NOT about fidelity — geometry and Z-order are done.
        // It may mention the phrase only to deny it.
        if (summary.Contains("保真细节", StringComparison.Ordinal))
            Assert.Contains("而不是", summary);
    }
}
