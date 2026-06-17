// ThemeManagerTests.cs — Forcing-function suite for the
// UI/ThemeManager palette. Covers:
//   • Pill(severity, theme) returns a non-default palette for every
//     theme × severity combination (catches future theme additions
//     that forget to populate an entry).
//   • Light theme bit-identical to legacy pill colours (60,179,113
//     green / 230,170,40 amber / 205,70,70 red / LightGray neutral).
//   • Dark theme pill backgrounds are darker than their Light
//     counterparts (regression guard on the Dark-mode muting).
//   • HighContrast theme routes through SystemColors (so Windows owns
//     the contrast).
//   • StatusForeground semantics match the pill semantics (Pass =
//     cool/green, Marginal = amber/hot, Fail = red/hot, Neutral =
//     grey).
//   • SetOverrideForTests + Refresh round-trip.
//
// All tests are pure C# — no PicoGK Library required; WinForms
// SystemColors probe works on the xUnit host.

using System.Drawing;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class ThemeManagerTests
{
    // ══════════════════════ Pill palette coverage ══════════════════════

    [Theory]
    [InlineData(UiTheme.Light,        PillSeverity.Pass)]
    [InlineData(UiTheme.Light,        PillSeverity.Marginal)]
    [InlineData(UiTheme.Light,        PillSeverity.Fail)]
    [InlineData(UiTheme.Light,        PillSeverity.Neutral)]
    [InlineData(UiTheme.Dark,         PillSeverity.Pass)]
    [InlineData(UiTheme.Dark,         PillSeverity.Marginal)]
    [InlineData(UiTheme.Dark,         PillSeverity.Fail)]
    [InlineData(UiTheme.Dark,         PillSeverity.Neutral)]
    [InlineData(UiTheme.HighContrast, PillSeverity.Pass)]
    [InlineData(UiTheme.HighContrast, PillSeverity.Marginal)]
    [InlineData(UiTheme.HighContrast, PillSeverity.Fail)]
    [InlineData(UiTheme.HighContrast, PillSeverity.Neutral)]
    public void Pill_EveryThemeSeverityPair_ReturnsPopulatedPalette(
        UiTheme theme, PillSeverity sev)
    {
        var pal = ThemeManager.Pill(sev, theme);
        // Background and foreground should not be the default Color
        // (which would indicate an uninitialised palette table).
        Assert.NotEqual(default(Color), pal.Background);
        Assert.NotEqual(default(Color), pal.Foreground);
        // Background != Foreground (otherwise the pill is unreadable).
        Assert.NotEqual(pal.Background, pal.Foreground);
    }

    // ══════════════════════ Light theme bit-identity ══════════════════════

    [Fact]
    public void LightTheme_PassPill_Matches_PreV447_Green()
    {
        var pal = ThemeManager.Pill(PillSeverity.Pass, UiTheme.Light);
        Assert.Equal(Color.FromArgb(60, 179, 113), pal.Background);
        Assert.Equal(Color.White, pal.Foreground);
    }

    [Fact]
    public void LightTheme_MarginalPill_Matches_PreV447_Amber()
    {
        var pal = ThemeManager.Pill(PillSeverity.Marginal, UiTheme.Light);
        Assert.Equal(Color.FromArgb(230, 170, 40), pal.Background);
        Assert.Equal(Color.Black, pal.Foreground);
    }

    [Fact]
    public void LightTheme_FailPill_Matches_PreV447_Red()
    {
        var pal = ThemeManager.Pill(PillSeverity.Fail, UiTheme.Light);
        Assert.Equal(Color.FromArgb(205, 70, 70), pal.Background);
        Assert.Equal(Color.White, pal.Foreground);
    }

    [Fact]
    public void LightTheme_NeutralPill_IsLightGray()
    {
        var pal = ThemeManager.Pill(PillSeverity.Neutral, UiTheme.Light);
        Assert.Equal(Color.LightGray, pal.Background);
        Assert.Equal(Color.Black, pal.Foreground);
    }

    // ══════════════════════ Dark theme muted-vs-Light guard ══════════════════════

    [Theory]
    [InlineData(PillSeverity.Pass)]
    [InlineData(PillSeverity.Marginal)]
    [InlineData(PillSeverity.Fail)]
    public void DarkTheme_PillBackground_IsDarkerThanLight(PillSeverity sev)
    {
        var light = ThemeManager.Pill(sev, UiTheme.Light).Background;
        var dark  = ThemeManager.Pill(sev, UiTheme.Dark).Background;
        // Luminance proxy via simple RGB sum — dark backgrounds should
        // have lower luminance than their light-mode counterpart so the
        // pill doesn't glow against dark chrome.
        int lLum = light.R + light.G + light.B;
        int dLum = dark .R + dark .G + dark .B;
        Assert.True(dLum < lLum,
            $"Dark {sev} pill should be muted vs Light: dark={dLum} light={lLum}");
    }

    // ══════════════════════ HighContrast → SystemColors ══════════════════════

    [Fact]
    public void HighContrastTheme_PassPill_UsesSystemHighlightPair()
    {
        var pal = ThemeManager.Pill(PillSeverity.Pass, UiTheme.HighContrast);
        Assert.Equal(SystemColors.Highlight,     pal.Background);
        Assert.Equal(SystemColors.HighlightText, pal.Foreground);
    }

    [Fact]
    public void HighContrastTheme_MarginalPill_UsesSystemInfoPair()
    {
        var pal = ThemeManager.Pill(PillSeverity.Marginal, UiTheme.HighContrast);
        Assert.Equal(SystemColors.Info,     pal.Background);
        Assert.Equal(SystemColors.InfoText, pal.Foreground);
    }

    [Fact]
    public void HighContrastTheme_NeutralPill_UsesSystemControlPair()
    {
        var pal = ThemeManager.Pill(PillSeverity.Neutral, UiTheme.HighContrast);
        Assert.Equal(SystemColors.Control,     pal.Background);
        Assert.Equal(SystemColors.ControlText, pal.Foreground);
    }

    // ══════════════════════ StatusForeground semantics ══════════════════════

    [Fact]
    public void StatusForeground_Light_Pass_IsDarkGreen()
    {
        Assert.Equal(Color.DarkGreen, ThemeManager.StatusForeground(PillSeverity.Pass, UiTheme.Light));
    }

    [Fact]
    public void StatusForeground_Light_Marginal_IsDarkOrange()
    {
        Assert.Equal(Color.DarkOrange, ThemeManager.StatusForeground(PillSeverity.Marginal, UiTheme.Light));
    }

    [Fact]
    public void StatusForeground_Light_Fail_IsFirebrick()
    {
        Assert.Equal(Color.Firebrick, ThemeManager.StatusForeground(PillSeverity.Fail, UiTheme.Light));
    }

    [Fact]
    public void StatusForeground_Light_Neutral_IsDimGray()
    {
        Assert.Equal(Color.DimGray, ThemeManager.StatusForeground(PillSeverity.Neutral, UiTheme.Light));
    }

    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    [InlineData(UiTheme.HighContrast)]
    public void StatusForeground_EveryTheme_ReturnsPopulatedColor(UiTheme theme)
    {
        foreach (var sev in new[] { PillSeverity.Pass, PillSeverity.Marginal,
                                    PillSeverity.Fail, PillSeverity.Neutral })
        {
            var c = ThemeManager.StatusForeground(sev, theme);
            Assert.NotEqual(default(Color), c);
        }
    }

    // ══════════════════════ Override + refresh ══════════════════════

    [Fact]
    public void SetOverrideForTests_SteersActiveTheme()
    {
        try
        {
            ThemeManager.SetOverrideForTests(UiTheme.Dark);
            Assert.Equal(UiTheme.Dark, ThemeManager.ActiveTheme);
            ThemeManager.SetOverrideForTests(UiTheme.HighContrast);
            Assert.Equal(UiTheme.HighContrast, ThemeManager.ActiveTheme);
        }
        finally
        {
            // Restore auto-detection so other tests aren't affected.
            ThemeManager.SetOverrideForTests(null);
            ThemeManager.Refresh();
        }
    }

    [Fact]
    public void ActiveTheme_AutoDetect_ReturnsOneOfThreeThemes()
    {
        // No override → auto-probe → must resolve to one of the three.
        ThemeManager.SetOverrideForTests(null);
        ThemeManager.Refresh();
        var t = ThemeManager.ActiveTheme;
        Assert.True(
            t == UiTheme.Light || t == UiTheme.Dark || t == UiTheme.HighContrast,
            $"ActiveTheme returned {t}");
    }

    [Fact]
    public void PillOverload_NoThemeArg_MatchesActiveTheme()
    {
        try
        {
            ThemeManager.SetOverrideForTests(UiTheme.Dark);
            var a = ThemeManager.Pill(PillSeverity.Pass);
            var b = ThemeManager.Pill(PillSeverity.Pass, UiTheme.Dark);
            Assert.Equal(b, a);
        }
        finally
        {
            ThemeManager.SetOverrideForTests(null);
        }
    }
}
