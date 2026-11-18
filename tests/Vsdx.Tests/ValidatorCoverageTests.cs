using System.IO.Compression;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// What the validator does and does not cover, pinned so the gap is known rather than assumed.
///
/// Of seventeen checks, thirteen are OPC container structure and four are Visio-level. The Visio ones
/// catch a dangling connector reference (`visio.connect-to`) but **not** a dangling master reference:
/// rewriting `Master="17"` to `Master="99"` on a real package leaves it `valid: true`, and Visio would
/// open it with a broken shape.
///
/// `shapes-not-valid` is the kind of error Visio shows for this, and the engine has no equivalent.
/// The test records the gap rather than pretending it is covered — changing it to "passes" would
/// require implementing the check, not adjusting the assertion.
/// </summary>
public sealed class ValidatorCoverageTests
{
    private static string Build(Action<Dictionary<string, byte[]>> damage)
    {
        var good = Path.Combine(Path.GetTempPath(), $"vsdx-cov-{Guid.NewGuid():N}.vsdx");

        // A real scenario, not a hand-built fixture: two plain nodes produce no master references and
        // no container membership, so the corruptions below had nothing to damage.
        var ir = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DiagramIr>(
            File.ReadAllText(Path.Combine(Root(), "examples", "realworld", "cbo.ir.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var spec = System.Text.Json.JsonSerializer.Deserialize<Vsdx.Protocol.DesignSpec>(
            """{"specVersion":"1.0","theme":{"palette":"enterprise"}}""",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        new Vsdx.Protocol.ProtocolRunner().Execute(ir, spec, good);

        var parts = new Dictionary<string, byte[]>();

        using (var zip = ZipFile.OpenRead(good))
        {
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                parts[entry.FullName] = buffer.ToArray();
            }
        }

        File.Delete(good);
        damage(parts);

        var damaged = Path.Combine(Path.GetTempPath(), $"vsdx-cov2-{Guid.NewGuid():N}.vsdx");

        using (var zip = ZipFile.Open(damaged, ZipArchiveMode.Create))
        {
            foreach (var (name, bytes) in parts)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return damaged;
    }

    [Fact]
    public void ItCatchesADanglingConnectorButNotADanglingMaster()
    {
        // The connector case is covered; this asserts the contrast so neither claim drifts.
        var path = Build(parts =>
        {
            var key = parts.Keys.First(k => k.StartsWith("visio/pages/page", StringComparison.Ordinal) && k != "visio/pages/pages.xml");
            var text = System.Text.Encoding.UTF8.GetString(parts[key]);

            int at = text.IndexOf("Master=\"", StringComparison.Ordinal);
            Assert.True(at > 0, "the package has no master reference to corrupt");

            int close = text.IndexOf('"', at + 8);
            string id = text[(at + 8)..close];

            parts[key] = System.Text.Encoding.UTF8.GetBytes(
                text.Remove(at + 8, id.Length).Insert(at + 8, "9999"));
        });

        try
        {
            var result = new VsdxEngine().Validate(path);

            // Documented gap: a master reference to a non-existent master is accepted.
            Assert.True(result.IsValid,
                "the validator now rejects dangling master references — update this test and the docs");

            Assert.DoesNotContain(result.Errors, e => e.Code.Contains("master", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ItDoesCheckShapeIdUniqueness()
    {
        var path = Build(parts =>
        {
            var key = parts.Keys.First(k => k.StartsWith("visio/pages/page", StringComparison.Ordinal) && k != "visio/pages/pages.xml");
            var text = System.Text.Encoding.UTF8.GetString(parts[key]);

            // Duplicate the first shape's ID onto the second.
            int first = text.IndexOf("<Shape ID=\"", StringComparison.Ordinal);
            int idStart = first + "<Shape ID=\"".Length;
            int idEnd = text.IndexOf('"', idStart);
            string id = text[idStart..idEnd];

            int second = text.IndexOf("<Shape ID=\"", first + 1, StringComparison.Ordinal);

            if (second > 0)
            {
                int s2 = second + "<Shape ID=\"".Length;
                int e2 = text.IndexOf('"', s2);
                text = text.Remove(s2, e2 - s2).Insert(s2, id);
                parts[key] = System.Text.Encoding.UTF8.GetBytes(text);
            }
        });

        try
        {
            var result = new VsdxEngine().Validate(path);

            Assert.False(result.IsValid, "duplicate shape ids were accepted");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ItChecksGroupMembership()
    {
        // A container that claims a member the page does not have.
        var path = Build(parts =>
        {
            var key = parts.Keys.First(k => k.StartsWith("visio/pages/page", StringComparison.Ordinal) && k != "visio/pages/pages.xml");
            var text = System.Text.Encoding.UTF8.GetString(parts[key]);

            int at = text.IndexOf("<Shape ID=\"", StringComparison.Ordinal);
            int close = text.IndexOf('>', at);

            if (at > 0 && close > at)
            {
                text = text.Insert(close + 1, "<Cell N=\"User.msvStructureType\" V=\"Container\"/>");
                parts[key] = System.Text.Encoding.UTF8.GetBytes(text);
            }
        });

        try
        {
            // Whether this passes or fails, the check must run and be reported by name.
            var result = new VsdxEngine().Validate(path);

            Assert.Contains(result.Checks, c => c.Name == "visio.group-membership");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheCheckInventoryIsRecorded()
    {
        var path = Build(_ => { });

        try
        {
            var result = new VsdxEngine().Validate(path);
            var names = result.Checks.Select(c => c.Name).ToList();

            Assert.Contains("zip.readable", names);
            Assert.Contains("zip.has-content-types", names);
            Assert.Contains("visio.pages-present", names);
            Assert.Contains("visio.connector-references", names);
            Assert.Contains("backend.reload", names);
        }
        finally { File.Delete(path); }
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
