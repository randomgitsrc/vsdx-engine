using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The matrix must cover the dimensions the charter names.
///
/// §5.1 lists them explicitly: file structure, page, Shape, Group, Connector, geometry, text, style,
/// **object identity**, **incremental modification**, render, validation, compatibility. The matrix had
/// one row for incremental editing and one for identity, while the editor emits nine operation kinds
/// and identity has three distinct failure modes — so two of the project's central promises were
/// summarised in a line each.
///
/// Covered: §6a (identity, 9 rows) and §6b (incremental, 12 rows).
/// </summary>
public sealed class MatrixCoverageTests
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

    private static string Editor() =>
        File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Adapters", "OfficeImoIncrementalEditor.cs"));

    [Fact]
    public void ItHasADedicatedIdentitySection()
    {
        var matrix = Matrix();

        Assert.Contains("6a. 对象身份", matrix);
        Assert.Contains("VsdxModelId", matrix);
    }

    [Fact]
    public void ItHasADedicatedIncrementalSection()
    {
        Assert.Contains("6b. 增量修改", Matrix());
    }

    [Fact]
    public void EveryOperationTheEditorEmitsAppearsInTheMatrix()
    {
        // The matrix claimed "incremental editing" in one row; the editor emits nine distinct kinds.
        var matrix = Matrix();
        var kinds = Regex.Matches(Editor(), @"Kind = ""((?:node|edge|group|page)\.[a-z]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(kinds.Count >= 9, $"only {kinds.Count} operation kinds found — is the regex right?");

        var missing = kinds.Where(k => !matrix.Contains(k, StringComparison.Ordinal)).ToList();

        Assert.True(missing.Count == 0, $"not in the matrix: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ItStatesTheTwoImplementationConstraintsThatBreakThisCode()
    {
        // Both cost real debugging time and are invisible from the public API.
        var matrix = Matrix();

        Assert.Contains("AddToContainer", matrix);
        Assert.Contains("DiagramLayout.Apply", matrix);
    }

    [Fact]
    public void ItRecordsThatTextBlockIdsAreUnstable()
    {
        // The one identity failure mode that is a real limitation rather than a guarantee.
        Assert.Contains("文本块 ID", Matrix());
    }
}
