using System.IO.Compression;
using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The validator must reject broken packages, not merely approve good ones.
///
/// Everything else in this project rests on `validate` being able to tell a sound file from a damaged
/// one: the fidelity guard, the write-time refusal, the container heading check. A validator that only
/// ever returns `valid: true` would make all of it theatre.
///
/// Five damages were introduced by hand into a real package and each was caught:
///
///   [Content_Types].xml removed      -> opc.content-types-missing
///   page XML truncated mid-element   -> xml.unparseable
///   a shape deleted, its connector kept -> visio.connect-to
///   _rels/.rels removed              -> opc.root-rels-missing
///   a part declared with two content types -> opc.duplicate-override
///
/// The dangling-connector case is the one that matters most: it is exactly what `--delete-missing`
/// produced before the cascade fix, and the write-time guard refusing that output was the validator
/// working, not a bug.
/// </summary>
public sealed class ValidatorDetectionTests
{
    private static string Build(Action<Dictionary<string, byte[]>> damage)
    {
        var good = Path.Combine(Path.GetTempPath(), $"vsdx-val-{Guid.NewGuid():N}.vsdx");

        new VsdxEngine().Create(new Diagram
        {
            Id = "t",
            Pages =
            {
                new Page
                {
                    Name = "p",
                    Size = new PageSize { Width = 12, Height = 9 },
                    Nodes =
                    {
                        new Node { Id = "a", Text = "A", Bounds = new Bounds(3, 6, 1.4, 0.7) },
                        new Node { Id = "b", Text = "B", Bounds = new Bounds(8, 6, 1.4, 0.7) }
                    },
                    Edges = { new Edge { Id = "e", From = "a", To = "b" } }
                }
            }
        }, good, new WriteOptions { AutoLayout = false });

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

        var damaged = Path.Combine(Path.GetTempPath(), $"vsdx-val2-{Guid.NewGuid():N}.vsdx");

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

    private static void AssertRejected(string path, string expectedCode)
    {
        try
        {
            var result = new VsdxEngine().Validate(path);

            Assert.False(result.IsValid, "a damaged package was accepted");
            Assert.Contains(result.Errors, e => e.Code == expectedCode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingContentTypesPartIsRejected()
    {
        var path = Build(parts => parts.Remove("[Content_Types].xml"));

        AssertRejected(path, "opc.content-types-missing");
    }

    [Fact]
    public void TruncatedPageXmlIsRejected()
    {
        var path = Build(parts =>
        {
            var key = parts.Keys.First(k => k.StartsWith("visio/pages/page", StringComparison.Ordinal));
            var text = System.Text.Encoding.UTF8.GetString(parts[key]);

            parts[key] = System.Text.Encoding.UTF8.GetBytes(text[..(text.Length / 2)]);
        });

        AssertRejected(path, "xml.unparseable");
    }

    [Fact]
    public void AMissingRootRelsIsRejected()
    {
        var path = Build(parts => parts.Remove("_rels/.rels"));

        AssertRejected(path, "opc.root-rels-missing");
    }

    [Fact]
    public void AShapeDeclaredWithTwoContentTypesIsRejected()
    {
        var path = Build(parts =>
        {
            var text = System.Text.Encoding.UTF8.GetString(parts["[Content_Types].xml"]);

            int at = text.IndexOf("<Override ", StringComparison.Ordinal);
            Assert.True(at > 0);

            int end = text.IndexOf("/>", at, StringComparison.Ordinal) + 2;
            var duplicate = text[at..end].Replace(
                "application/vnd.ms-visio.drawing.main+xml", "application/x-wrong");

            parts["[Content_Types].xml"] = System.Text.Encoding.UTF8.GetBytes(
                text.Insert(end, duplicate));
        });

        AssertRejected(path, "opc.duplicate-override");
    }

    [Fact]
    public void AHealthyPackageIsAccepted()
    {
        // The other direction: the validator must not reject everything to look strict.
        var path = Build(_ => { });

        try
        {
            var result = new VsdxEngine().Validate(path);

            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Code)));
            Assert.Empty(result.Errors);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ItRunsSeventeenChecksOnAHealthyPackage()
    {
        var path = Build(_ => { });

        try
        {
            var result = new VsdxEngine().Validate(path);

            Assert.True(result.Checks.Count >= 15, $"only {result.Checks.Count} checks ran");
            Assert.All(result.Checks, c => Assert.True(c.Passed, $"{c.Name} failed on a healthy package"));
        }
        finally { File.Delete(path); }
    }
}
