using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The matrix's summary counts must match its own table rows.
///
/// The charter requires a capability matrix that distinguishes supported, partial, unsupported and
/// "implemented but unverified". A summary that disagrees with the rows makes the matrix unusable for
/// the one thing it is for — deciding what to work on next.
///
/// The counts had drifted: free paths were implemented and the row was updated, but the summary still
/// said 57 verified and listed free paths among the three format-only entries. Measured against the
/// rows: 59 / 18 / 16 / 6 / 3 / 2 / 1 / 1 = 106.
/// </summary>
public sealed class CapabilityMatrixTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string MatrixPath() =>
        Path.Combine(RepoRoot(), "docs", "phase8", "capability-matrix.md");

    private static readonly string[] Markers = { "✅", "⚠️", "🔬", "📄", "🧪", "❌", "🚫", "⛔" };

    /// <summary>Counts the status marker in column three of every table row in sections 1-9.</summary>
    private static Dictionary<string, int> CountRows()
    {
        var text = File.ReadAllText(MatrixPath());
        var body = text[..text.IndexOf("## 10.", StringComparison.Ordinal)];

        var counts = Markers.ToDictionary(m => m, _ => 0, StringComparer.Ordinal);

        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal)) continue;

            var cells = line.Split('|');
            if (cells.Length < 4) continue;

            var status = cells[2].Trim();
            var marker = Markers.FirstOrDefault(status.StartsWith);

            if (marker is not null) counts[marker]++;
        }

        return counts;
    }

    /// <summary>Reads the count claimed in the summary table.</summary>
    private static Dictionary<string, int> ClaimedCounts()
    {
        var text = File.ReadAllText(MatrixPath());
        var summary = text[text.IndexOf("## 10.", StringComparison.Ordinal)..];

        var claimed = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in summary.Split('\n'))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal)) continue;

            var cells = line.Split('|');
            if (cells.Length < 4) continue;

            var label = cells[1].Trim();
            var marker = Markers.FirstOrDefault(label.StartsWith);
            if (marker is null) continue;

            if (int.TryParse(cells[2].Trim(), out int n)) claimed[marker] = n;
        }

        return claimed;
    }

    [Fact]
    public void SummaryMatchesTheRows()
    {
        var actual = CountRows();
        var claimed = ClaimedCounts();

        var mismatches = Markers
            .Where(m => claimed.TryGetValue(m, out int c) && c != actual[m])
            .Select(m => $"{m}: summary {claimed[m]}, rows {actual[m]}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void TheStatedTotalMatchesTheSum()
    {
        var actual = CountRows();
        int sum = actual.Values.Sum();

        var text = File.ReadAllText(MatrixPath());
        var summary = text[text.IndexOf("## 10.", StringComparison.Ordinal)..];

        Assert.Contains($"**{sum}**", summary);
    }

    [Fact]
    public void FreePathsAreNoLongerListedAsUnsupported()
    {
        // The specific drift this test was written for.
        var text = File.ReadAllText(MatrixPath());
        var body = text[..text.IndexOf("## 10.", StringComparison.Ordinal)];

        var row = body.Split('\n').FirstOrDefault(l => l.Contains("自由路径", StringComparison.Ordinal) &&
                                                        l.StartsWith("| ", StringComparison.Ordinal));

        Assert.NotNull(row);
        Assert.Contains("✅", row);
    }
}
