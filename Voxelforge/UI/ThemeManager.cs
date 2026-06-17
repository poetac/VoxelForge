// ThemeManager.cs — Centralised palette for semantic-coloured UI
// pills (stability /
// structural-confidence / NPSH / safety factor) so High-Contrast and
// Windows dark mode don't silently break the traffic-light-colour
// convention used across the form.
//
// Three themes resolved in order of precedence
// ────────────────────────────────────────────
//   1. HighContrast — when SystemInformation.HighContrast is active,
//      palette routes through SystemColors so Windows' contrast
//      guarantees apply. Background uses Highlight/Control/Info;
//      foreground uses the matched HighlightText/ControlText/InfoText.
//   2. Dark — Windows 10/11 "Use dark mode for apps" setting detected
//      via the HKCU\…\Themes\Personalize\AppsUseLightTheme registry
//      value. Palette keeps the traffic-light semantics but rebalances
//      saturation so the existing LightGreen / DarkOrange / Firebrick
//      entries don't look like neon on dark chrome.
//   3. Light (default) — legacy behaviour, bit-identical. The
//      LIGHT_* constants preserve every Color.FromArgb + named-colour
//      call site so regression tests that assert on legacy values
//      (there were none at sprint start, but a future contributor
//      asserting against the Light palette is fine) stay stable.
//
// Theme detection is SAFE on any OS — every probe is wrapped in
// try/catch. If Windows fails to report its theme (non-Windows host,
// stripped registry, etc.) the palette falls back to Light.
//
// No explicit refresh watcher is installed in this sprint — the form
// reads the palette at control-paint time (pills re-apply on every
// GenerateAnalyses / FinalizeOpt), so flipping the OS theme while the
// app is running will pick up the new palette on the next refresh.
// A system-wide WM_SETTINGCHANGE watcher is documented as a Tier U4
// follow-on for anyone who cares about live theme swaps.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Voxelforge.UI;

/// <summary>
/// Semantic category for a coloured pill. Consumers never pick an
/// RGB triple directly — they ask for a semantic category and get
/// the (background, foreground) pair for the active theme.
/// </summary>
public enum PillSeverity
{
    /// <summary>Green — all gates pass / in-spec / high confidence.</summary>
    Pass,
    /// <summary>Amber — soft-warning / marginal / medium confidence.</summary>
    Marginal,
    /// <summary>Red — hard-gate fail / yield exceeded / NPSH insufficient.</summary>
    Fail,
    /// <summary>Grey — N/A or opt-out (e.g. pressure-fed → no turbopump).</summary>
    Neutral,
}

/// <summary>
/// Resolved themes. The form reads
/// <see cref="ThemeManager.ActiveTheme"/> at paint time.
/// </summary>
public enum UiTheme
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>
/// Pair of colours for a pill — pill background + text foreground. The
/// pairing keeps every pill accessible regardless of the theme.
/// </summary>
public readonly record struct PillPalette(Color Background, Color Foreground);

/// <summary>
/// Central palette + theme-probe service. All members are
/// deterministic w.r.t. the OS theme state; the probe itself is
/// memoised at first access (see <see cref="Refresh"/> to force a
/// re-probe after a system setting change).
/// </summary>
public static class ThemeManager
{
    private static UiTheme? _cachedTheme;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// The theme currently in effect. Probed on first access and
    /// cached. Call <see cref="Refresh"/> after a WM_SETTINGCHANGE
    /// hint to re-probe.
    /// </summary>
    public static UiTheme ActiveTheme
    {
        get
        {
            lock (_cacheLock)
                return _cachedTheme ??= Probe();
        }
    }

    /// <summary>
    /// Force a re-probe on the next <see cref="ActiveTheme"/> read.
    /// Intended for tests that mutate process-level state and for a
    /// future WM_SETTINGCHANGE handler.
    /// </summary>
    public static void Refresh()
    {
        lock (_cacheLock) _cachedTheme = null;
    }

    /// <summary>
    /// Override the detected theme. Test-only; production code never
    /// calls this. Pass null to restore auto-detection.
    /// </summary>
    internal static void SetOverrideForTests(UiTheme? theme)
    {
        lock (_cacheLock) _cachedTheme = theme;
    }

    /// <summary>
    /// (background, foreground) palette for a pill at the given
    /// severity. Uses <see cref="ActiveTheme"/>.
    /// </summary>
    public static PillPalette Pill(PillSeverity sev) => Pill(sev, ActiveTheme);

    /// <summary>
    /// Deterministic variant for tests — explicit theme argument
    /// bypasses the cached probe.
    /// </summary>
    public static PillPalette Pill(PillSeverity sev, UiTheme theme) => theme switch
    {
        UiTheme.HighContrast => HighContrastPill(sev),
        UiTheme.Dark         => DarkPill(sev),
        _                    => LightPill(sev),
    };

    /// <summary>
    /// Foreground colour for an inline status label (like the SF
    /// pill, structural-confidence strip, NPSH pump readout). These
    /// don't use a background fill; only the text colour is themed.
    /// </summary>
    public static Color StatusForeground(PillSeverity sev) =>
        StatusForeground(sev, ActiveTheme);

    public static Color StatusForeground(PillSeverity sev, UiTheme theme) => theme switch
    {
        UiTheme.HighContrast => sev switch
        {
            PillSeverity.Pass     => SystemColors.Highlight,
            PillSeverity.Marginal => SystemColors.HotTrack,
            PillSeverity.Fail     => SystemColors.HotTrack,
            _                     => SystemColors.GrayText,
        },
        UiTheme.Dark => sev switch
        {
            PillSeverity.Pass     => Color.FromArgb(120, 215, 160),   // soft green
            PillSeverity.Marginal => Color.FromArgb(245, 190, 100),   // muted amber
            PillSeverity.Fail     => Color.FromArgb(240, 130, 130),   // soft red
            _                     => Color.FromArgb(190, 190, 190),
        },
        _ => sev switch
        {
            PillSeverity.Pass     => Color.DarkGreen,
            PillSeverity.Marginal => Color.DarkOrange,
            PillSeverity.Fail     => Color.Firebrick,
            _                     => Color.DimGray,
        },
    };

    // ── Palette tables (one per theme) ──────────────────────────────

    private static PillPalette LightPill(PillSeverity sev) => sev switch
    {
        PillSeverity.Pass     => new PillPalette(Color.FromArgb( 60, 179, 113), Color.White),
        PillSeverity.Marginal => new PillPalette(Color.FromArgb(230, 170,  40), Color.Black),
        PillSeverity.Fail     => new PillPalette(Color.FromArgb(205,  70,  70), Color.White),
        _                     => new PillPalette(Color.LightGray,               Color.Black),
    };

    private static PillPalette DarkPill(PillSeverity sev) => sev switch
    {
        // Dark-mode palette: more muted backgrounds so the pill reads
        // against a dark form chrome without glowing. Text flips to
        // a higher-contrast shade when the background is light.
        PillSeverity.Pass     => new PillPalette(Color.FromArgb( 46, 125,  80), Color.White),
        PillSeverity.Marginal => new PillPalette(Color.FromArgb(180, 130,  35), Color.Black),
        PillSeverity.Fail     => new PillPalette(Color.FromArgb(165,  55,  55), Color.White),
        _                     => new PillPalette(Color.FromArgb( 85,  85,  85), Color.Gainsboro),
    };

    private static PillPalette HighContrastPill(PillSeverity sev) => sev switch
    {
        // High-Contrast: go through SystemColors so Windows owns the
        // contrast guarantee. Pass = Highlight pair (selection colour);
        // Marginal / Fail = Info pair (tooltip yellow / warning) —
        // SystemColors doesn't give us a three-way traffic-light, so
        // fall back to the Highlight pair for Fail as well. Tests
        // tolerate the reduced palette.
        PillSeverity.Pass     => new PillPalette(SystemColors.Highlight,
                                                  SystemColors.HighlightText),
        PillSeverity.Marginal => new PillPalette(SystemColors.Info,
                                                  SystemColors.InfoText),
        PillSeverity.Fail     => new PillPalette(SystemColors.Highlight,
                                                  SystemColors.HighlightText),
        _                     => new PillPalette(SystemColors.Control,
                                                  SystemColors.ControlText),
    };

    // ── Probe ───────────────────────────────────────────────────────

    private static UiTheme Probe()
    {
        try
        {
            if (SystemInformation.HighContrast) return UiTheme.HighContrast;
        }
        catch { /* fall through on headless / stripped hosts */ }

        if (OperatingSystem.IsWindows() && IsDarkModeEnabled())
            return UiTheme.Dark;

        return UiTheme.Light;
    }

    private static bool IsDarkModeEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key is null) return false;
            var v = key.GetValue("AppsUseLightTheme");
            // AppsUseLightTheme: 1 = light, 0 = dark. Missing value
            // implies "not set" → conservative default to light.
            return v is int iv && iv == 0;
        }
        catch { return false; }
    }
}
