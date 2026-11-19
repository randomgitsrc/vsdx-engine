using Vsdx.Engine;
using Vsdx.Model;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// Every bridge that can fail silently must be able to say so.
///
/// Three bridges were surveyed after the free-path bug: `VisioNestingBridge` already reports a dangling
/// `parentId` (`write.group-parent-missing`), `VisioMasterBridge` counts what it could not restore
/// (`write.master-not-restored`), and `VisioMetadataBridge` swallowed its exception with a comment saying
/// metadata is an enhancement — true, but it meant a description the caller supplied could vanish with
/// nothing recorded.
///
/// A failing write is an edge case here; the test cannot force one. What it can check is that the mechanism
/// exists, because its absence is the actual defect: no amount of correct behaviour in the happy path
/// substitutes for being able to report the unhappy one.
/// </summary>
public sealed class BridgeSilentFailureTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Bridge(string name) =>
        File.ReadAllText(Path.Combine(Root(), "src", "Vsdx.Adapters", $"Visio{name}Bridge.cs"));

    [Theory]
    [InlineData("Metadata")]
    [InlineData("Nesting")]
    [InlineData("Master")]
    [InlineData("Background")]
    [InlineData("FreePath")]
    [InlineData("TextBlock")]
    public void TheBridgeCanReportProblems(string name)
    {
        // A bridge with no `CompatibilityReport` cannot report anything, however it is written.
        var source = Bridge(name);

        Assert.Contains("CompatibilityReport", source);
    }

    [Fact]
    public void MetadataFailuresAreRecordedRatherThanSwallowed()
    {
        var source = Bridge("Metadata");

        // There is a catch block; it must warn rather than only comment.
        Assert.Contains("catch", source);
        Assert.Contains("write.metadata-failed", source);
    }

    [Fact]
    public void ANestingParentThatDoesNotExistIsReported()
    {
        // Emitted by the writer, not the bridge: the reference is checked before the XML patch runs.
        var writer = File.ReadAllText(
            Path.Combine(Root(), "src", "Vsdx.Adapters", "OfficeImoDocumentWriter.cs"));

        Assert.Contains("write.group-parent-missing", writer);
    }

    [Fact]
    public void ABackgroundColourThatCannotBeParsedIsReported()
    {
        Assert.Contains("write.background-unparsed", Bridge("Background"));
    }

    [Fact]
    public void AMasterThatCouldNotBeRestoredIsCounted()
    {
        var source = Bridge("Master");

        Assert.Contains("missing", source);
        Assert.Contains("Warn", source);
    }
}
