using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A background colour the engine cannot parse must be reported, not dropped.
///
/// `VisioBackgroundBridge` does `if (!ColorUtil.TryParse(colour, out …)) continue;` — the page's background is
/// silently omitted, the write returns success, and no compatibility issue is recorded. A caller who wrote
/// `"background": "#GGG"` gets a white page and no indication that anything was rejected.
///
/// That is the rule this project exists to enforce: a request is either honoured or listed.
///
/// Measured: `#FF0000` reaches `PageBackground`, `not-a-color` and `#GGG` leave the part untouched and
/// produce no warning.
/// </summary>
public sealed class BackgroundBridgeTests
{
    private static Diagram WithBackground(string? colour) => new()
    {
        Id = "t",
        Pages =
        {
            new Page
            {
                Name = "p",
                Size = new PageSize { Width = 10, Height = 8 },
                Background = colour
            }
        }
    };

    private static string Temp() => Path.Combine(Path.GetTempPath(), $"vsdx-bg-{Guid.NewGuid():N}.vsdx");

    [Fact]
    public void AValidColourIsWritten()
    {
        var path = Temp();

        try
        {
            new VsdxEngine().Create(WithBackground("#FF0000"), path, new WriteOptions { AutoLayout = false });

            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName == "visio/pages/pages.xml").Open());

            Assert.Contains("#FF0000", reader.ReadToEnd());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("not-a-color")]
    [InlineData("#GGG")]
    [InlineData("rgb(oops)")]
    public void AnUnparseableColourIsReported(string colour)
    {
        var path = Temp();

        try
        {
            var result = new VsdxEngine().Create(
                WithBackground(colour), path, new WriteOptions { AutoLayout = false });

            bool wrote = result.Compatibility.Issues.All(i => i.Code != "write.background-unparsed");
            bool reported = result.Compatibility.Issues.Any(i =>
                i.Code == "write.background-unparsed" && i.Severity != "info");

            Assert.True(!wrote || reported,
                $"'{colour}' was dropped without a warning");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AnOutOfRangeComponentIsClampedAndWrittenAsHex()
    {
        // `rgb(999,0,0)` parses — the components clamp. What mattered was writing the parsed value rather
        // than the original text: `PageBackground` is a colour cell, and Visio does not accept `rgb(...)`.
        var path = Temp();

        try
        {
            new VsdxEngine().Create(WithBackground("rgb(999,0,0)"), path, new WriteOptions { AutoLayout = false });

            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            using var reader = new StreamReader(zip.Entries
                .First(e => e.FullName == "visio/pages/pages.xml").Open());
            var xml = reader.ReadToEnd();

            Assert.Contains("#FF0000", xml);
            Assert.DoesNotContain("rgb(999", xml);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void NoBackgroundMeansNoComplaint()
    {
        // Only a request that was made and dropped deserves a line.
        var path = Temp();

        try
        {
            var result = new VsdxEngine().Create(WithBackground(null), path, new WriteOptions { AutoLayout = false });

            Assert.DoesNotContain(result.Compatibility.Issues, i => i.Code.Contains("background", StringComparison.Ordinal));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
