using System.Text.RegularExpressions;
using Xunit;

namespace Vsdx.Tests;

/// <summary>
/// The Agent skill must describe the engine that exists.
///
/// `skill/SKILL.md` documented only the `create` path. `execute` — the DesignSpec-driven path where
/// most of this project's capability lives, with 31 spec fields — was not mentioned at all, so an
/// agent reading the skill would not know it existed. `canvas`, `theme.palette` and `typography`
/// were likewise absent.
///
/// A skill is the interface an agent actually reads; being wrong there is worse than being terse.
/// </summary>
public sealed class SkillDocumentationTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VsdxEngine.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Skill() =>
        File.ReadAllText(Path.Combine(Root(), "skill", "SKILL.md"));

    [Fact]
    public void TheSkillDocumentsTheExecutePath()
    {
        Assert.Contains("vsdx-tool execute", Skill());
    }

    [Theory]
    [InlineData("canvas")]
    [InlineData("theme.palette")]
    [InlineData("typography.baseSize")]
    [InlineData("containerLayouts")]
    [InlineData("unmetConstraints")]
    public void TheSkillNamesTheSpecFieldsThatMatter(string field)
    {
        Assert.Contains(field, Skill());
    }

    [Fact]
    public void EveryPaletteNameInTheSkillExists()
    {
        var skill = Skill();

        foreach (var name in new[] { "enterprise", "muted", "technical", "midnight" })
        {
            if (!skill.Contains(name, StringComparison.Ordinal)) continue;

            Assert.True(Vsdx.Model.Palettes.TryGet(name, out _),
                $"the skill mentions palette '{name}', which does not exist");
        }
    }

    [Fact]
    public void TheSkillDoesNotOfferANonExistentPalette()
    {
        // `office` appeared in a source comment once and does not exist.
        Assert.DoesNotContain("`office`", Skill());
    }

    [Fact]
    public void TheErrorCodesItNamesAreReal()
    {
        var skill = Skill();

        foreach (var code in new[] { "text.overflow-horizontal", "text.overflow-vertical" })
        {
            if (!skill.Contains(code, StringComparison.Ordinal)) continue;

            Assert.Contains(code, File.ReadAllText(
                Path.Combine(Root(), "src", "Vsdx.Engine", "LayoutAnalyzer.cs")));
        }
    }

    [Fact]
    public void TheSkillStatesTheGateItShould()
    {
        // `hardConstraintsPassed` is the one field a caller must check before delivering.
        Assert.Contains("hardConstraintsPassed", Skill());
    }
}
