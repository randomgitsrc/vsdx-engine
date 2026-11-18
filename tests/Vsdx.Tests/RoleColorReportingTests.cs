using System.Text.Json;
using Vsdx.Engine;
using Vsdx.Model;
using Vsdx.Protocol;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A role colour map that does not cover a role must say so.
///
/// The lookup used TryGetValue and skipped on a miss, with no fallback and no report. Misspell a
/// key — `databse` for `database` — and the node stays white while `unmetConstraints` stays empty:
/// the caller sees a successful run and an uncoloured diagram, with nothing to explain why.
///
/// Two behaviours are wanted. A `default` entry colours roles the map does not name, and any role
/// still uncovered is reported once per role rather than once per node.
/// </summary>
public sealed class RoleColorReportingTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private const string TwoRoleIr = """
    {"irVersion":"1.0","id":"p","kind":"architecture",
     "nodes":[{"id":"a","label":"A","role":"database"},
              {"id":"b","label":"B","role":"service"},
              {"id":"c","label":"C","role":"database"}]}
    """;

    private sealed record Outcome(ExecutionResult Result, Diagram Diagram, string Path);

    private static Outcome Run(string specJson)
    {
        var ir = JsonSerializer.Deserialize<DiagramIr>(TwoRoleIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(specJson, Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-rolecolor-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);

        return new Outcome(result, new VsdxEngine().Read(path).Diagram, path);
    }

    [Fact]
    public void ACoveredRoleIsColouredAndNotReported()
    {
        var run = Run("""{"specVersion":"1.0","theme":{"roleColors":{"database":"#AA0000"}}}""");
        try
        {
            var db = run.Diagram.Pages[0].Nodes.First(n => n.Id == "a");
            Assert.Equal("#AA0000", db.Style?.Fill);

            // `service` is uncovered here, so it is reported — but `database` must not be.
            Assert.DoesNotContain(run.Result.UnmetConstraints,
                u => u.Code.Contains("role", StringComparison.OrdinalIgnoreCase)
                  && (u.Reason ?? "").Contains("database", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void AnUncoveredRoleIsReported()
    {
        // The misspelling case: the map names `databse`, the nodes say `database`.
        var run = Run("""{"specVersion":"1.0","theme":{"roleColors":{"databse":"#AA0000"}}}""");
        try
        {
            Assert.Contains(run.Result.UnmetConstraints,
                u => u.Code.Contains("role", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void TheReportNamesTheUncoveredRole()
    {
        // "Some role was not coloured" is not actionable; the role name is.
        var run = Run("""{"specVersion":"1.0","theme":{"roleColors":{"databse":"#AA0000"}}}""");
        try
        {
            var text = string.Join(" ", run.Result.UnmetConstraints.Select(u => $"{u.Code} {u.Reason}"));

            Assert.Contains("database", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void UncoveredRolesAreReportedOncePerRoleNotOncePerNode()
    {
        // There are two `database` nodes; one complaint about the role is a signal, two are noise.
        var run = Run("""{"specVersion":"1.0","theme":{"roleColors":{"databse":"#AA0000"}}}""");
        try
        {
            var complaints = run.Result.UnmetConstraints
                .Where(u => u.Code.Contains("role", StringComparison.OrdinalIgnoreCase)
                         && (u.Reason ?? "").Contains("database", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Single(complaints);
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void ADefaultEntryColoursRolesTheMapDoesNotName()
    {
        // `database` is named explicitly, `service` falls through to default.
        var run = Run(
            """{"specVersion":"1.0","theme":{"roleColors":{"database":"#AA0000","default":"#00AA00"}}}""");
        try
        {
            var db = run.Diagram.Pages[0].Nodes.First(n => n.Id == "a");
            var svc = run.Diagram.Pages[0].Nodes.First(n => n.Id == "b");

            Assert.Equal("#AA0000", db.Style?.Fill);
            Assert.Equal("#00AA00", svc.Style?.Fill);
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void WithADefaultNoRoleComplaintsRemain()
    {
        var run = Run(
            """{"specVersion":"1.0","theme":{"roleColors":{"database":"#AA0000","default":"#00AA00"}}}""");
        try
        {
            Assert.DoesNotContain(run.Result.UnmetConstraints,
                u => u.Code.Contains("role", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(run.Path);
        }
    }

    [Fact]
    public void NodesWithoutARoleAreNotReported()
    {
        // No role is a legitimate state, not an uncovered one.
        const string plainIr = """
        {"irVersion":"1.0","id":"p","kind":"architecture",
         "nodes":[{"id":"a","label":"A"}]}
        """;

        var ir = JsonSerializer.Deserialize<DiagramIr>(plainIr, Json)!;
        var spec = JsonSerializer.Deserialize<DesignSpec>(
            """{"specVersion":"1.0","theme":{"roleColors":{"database":"#AA0000"}}}""", Json)!;

        var path = Path.Combine(Path.GetTempPath(), $"vsdx-rolecolor2-{Guid.NewGuid():N}.vsdx");
        var result = new ProtocolRunner().Execute(ir, spec, path);

        try
        {
            Assert.DoesNotContain(result.UnmetConstraints,
                u => u.Code.Contains("role", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
