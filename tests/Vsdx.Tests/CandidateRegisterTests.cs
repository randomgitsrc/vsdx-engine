using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// A candidate marked "not fixed" must actually be unfixed.
///
/// The register drifted: C3, C4, C5, C6, C9, C10 and C11 were each recorded with a long investigation
/// ending in "**未修复**" — and every one of them was fixed in a later commit. A reader planning work
/// from that document would have redone seven fixes.
///
/// This test cannot tell whether a candidate is *fixed*; it can tell whether the document contradicts
/// itself, which is the failure that actually happened: several entries carried both an investigation
/// and a stale conclusion.
/// </summary>
public sealed class CandidateRegisterTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string RegisterText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "phase8", "phase8.3-candidates.md"));

    /// <summary>Splits the register into one block per `## Cn` heading.</summary>
    private static Dictionary<string, string> Sections()
    {
        var text = RegisterText();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var matches = Regex.Matches(text, @"^## (C\d+)\b.*$", RegexOptions.Multiline);

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            result[matches[i].Groups[1].Value] = text[start..end];
        }

        return result;
    }

    [Fact]
    public void NoCandidateBothClaimsAndDeniesAFix()
    {
        var contradictory = new List<string>();

        foreach (var (id, body) in Sections())
        {
            // A section may legitimately record "not fixed" for an early round and "fixed" for a
            // later one — C20 took four attempts. What is not legitimate is leaving the early
            // conclusion as the last word, so only the *final* outcome counts.
            bool claimsFixed = Regex.IsMatch(body, @"\*\*已修|### 已修");
            bool claimsOpen = Regex.IsMatch(body, @"\*\*未修复\*\*");

            if (claimsFixed && claimsOpen)
            {
                // Tolerated only when the "not fixed" text precedes every "fixed" heading: that is a
                // history of attempts, not a contradiction.
                int lastFixed = Math.Max(
                    body.LastIndexOf("**已修", StringComparison.Ordinal),
                    body.LastIndexOf("### 已修", StringComparison.Ordinal));
                int lastOpen = body.LastIndexOf("**未修复", StringComparison.Ordinal);

                if (lastOpen < lastFixed) continue;
            }

            if (claimsFixed && claimsOpen) contradictory.Add(id);
        }

        Assert.True(contradictory.Count == 0,
            $"marked both fixed and unfixed: {string.Join(", ", contradictory.OrderBy(x => x))}");
    }

    [Fact]
    public void EverySectionStatesAnOutcome()
    {
        // An entry that ends mid-investigation leaves a reader unable to tell whether to act.
        var silent = new List<string>();

        foreach (var (id, body) in Sections())
        {
            bool settled = Regex.IsMatch(
                body, @"\*\*已修|### 已修|\*\*未修复\*\*|已撤回|已登记");

            if (!settled) silent.Add(id);
        }

        Assert.True(silent.Count == 0,
            $"no stated outcome: {string.Join(", ", silent.OrderBy(x => x))}");
    }

    [Fact]
    public void TheSevenStaleEntriesAreNowClosed()
    {
        // The specific drift this test was written for, pinned so it cannot come back.
        string[] wasStale = { "C3", "C4", "C5", "C6", "C9", "C10", "C11" };
        var sections = Sections();

        foreach (var id in wasStale)
        {
            Assert.True(sections.ContainsKey(id), $"{id} missing from the register");
            Assert.DoesNotMatch(@"\*\*未修复\*\*", sections[id]);
        }
    }
}
