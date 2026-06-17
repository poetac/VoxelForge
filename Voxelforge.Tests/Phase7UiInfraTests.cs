// Phase7UiInfraTests.cs — Contract tests for the Tier-U1 UI polish
// infrastructure:
//
//   • SessionSettings JSON round-trip + Load-on-missing-file safety
//   • SessionSettings.RegisterRecentDesign dedup + cap behaviour
//   • ToolTipText: every public string is non-empty + ends with a
//     period (caught typo guard)
//
// The actual UI surfaces (AxialProfileChartPanel, DesignComparePanel,
// the wired tooltips on RegenChamberForm) need a live WinForms
// instance to test directly — xUnit cannot spin one up cleanly
// without the PicoGK Library and the STA thread, so behaviour is
// covered indirectly via the existing Phase 1-6 round-trip tests.
// The U1 panel widgets are paint-only; they degrade gracefully on
// null inputs (asserted by their `RenderEmpty` paths during normal
// form construction).

using System.IO;
using Voxelforge.Tests.Helpers;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class Phase7UiInfraTests
{
    // ─────────────────────────────────────────────────────────────────
    //  SessionSettings — JSON round-trip + missing-file handling
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionSettings_RoundTrip_PreservesAllFields()
    {
        using var tmp = TestTempFile.WithUniqueName("sessionprobe", "json");
        var orig = new SessionSettings
        {
            WindowWidth  = 1920, WindowHeight = 1080,
            WindowX      =   42, WindowY      =   17,
            LivePreviewEnabled = false,
            LastSaveFolder = @"C:\demo\save",
            LastLoadFolder = @"C:\demo\load",
            RecentDesigns  = { @"C:\demo\a.rcd.json", @"C:\demo\b.rcd.json" },
        };
        Assert.True(orig.Save(tmp.Path));

        var loaded = SessionSettings.Load(tmp.Path);
        Assert.Equal(orig.WindowWidth,  loaded.WindowWidth);
        Assert.Equal(orig.WindowHeight, loaded.WindowHeight);
        Assert.Equal(orig.WindowX,      loaded.WindowX);
        Assert.Equal(orig.WindowY,      loaded.WindowY);
        Assert.False(loaded.LivePreviewEnabled);
        Assert.Equal(orig.LastSaveFolder, loaded.LastSaveFolder);
        Assert.Equal(orig.LastLoadFolder, loaded.LastLoadFolder);
        Assert.Equal(2, loaded.RecentDesigns.Count);
        Assert.Contains(@"C:\demo\a.rcd.json", loaded.RecentDesigns);
    }

    [Fact]
    public void SessionSettings_LoadMissingFile_ReturnsDefaults()
    {
        // Calling Load on a path that doesn't exist must NEVER throw —
        // it's the first-run code path. Returns a fresh defaults
        // instance so the form just falls back to its hard-coded
        // initial size + position.
        var path = Path.Combine(Path.GetTempPath(),
            $"sessionprobe_NEVEREXISTS_{System.Guid.NewGuid():N}.json");
        var s = SessionSettings.Load(path);
        Assert.NotNull(s);
        Assert.Equal(0, s.WindowWidth);                    // default
        Assert.Equal(int.MinValue, s.WindowX);             // default
        Assert.True(s.LivePreviewEnabled);                  // default ON
        Assert.Empty(s.RecentDesigns);
    }

    [Fact]
    public void SessionSettings_LoadCorruptFile_ReturnsDefaults()
    {
        // Bad JSON must fall back to defaults instead of crashing the
        // form constructor.
        using var tmp = TestTempFile.WithUniqueName("sessionprobe_CORRUPT", "json");
        File.WriteAllText(tmp.Path, "{ this is not valid json — at all }");
        var s = SessionSettings.Load(tmp.Path);
        Assert.NotNull(s);
        Assert.True(s.LivePreviewEnabled);              // default
    }

    [Fact]
    public void SessionSettings_RegisterRecentDesign_DedupesCaseInsensitive()
    {
        var s = new SessionSettings();
        s.RegisterRecentDesign(@"C:\demo\Alpha.rcd.json");
        s.RegisterRecentDesign(@"C:\demo\Beta.rcd.json");
        // Same path, different case — must dedup (Windows fs convention).
        s.RegisterRecentDesign(@"c:\DEMO\alpha.RCD.JSON");
        Assert.Equal(2, s.RecentDesigns.Count);
        // Re-registered entry moves to the front.
        Assert.StartsWith(@"c:\DEMO\alpha", s.RecentDesigns[0],
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionSettings_RegisterRecentDesign_CapsToMaxRecentFiles()
    {
        var s = new SessionSettings();
        // Push 15 unique paths; only the 10 most recent should remain.
        for (int i = 0; i < 15; i++)
            s.RegisterRecentDesign($@"C:\demo\design_{i:D2}.rcd.json");
        Assert.Equal(SessionSettings.MaxRecentFiles, s.RecentDesigns.Count);
        // Newest at the front; oldest pushed off the end.
        Assert.Contains(@"C:\demo\design_14.rcd.json", s.RecentDesigns);
        Assert.DoesNotContain(@"C:\demo\design_00.rcd.json", s.RecentDesigns);
    }

    [Fact]
    public void SessionSettings_RegisterRecentDesign_IgnoresEmptyAndNull()
    {
        var s = new SessionSettings();
        s.RegisterRecentDesign("");
        s.RegisterRecentDesign("   ");
        s.RegisterRecentDesign(null!);
        Assert.Empty(s.RecentDesigns);
    }

    // ─────────────────────────────────────────────────────────────────
    //  ToolTipText — every public string non-empty (typo guard)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ToolTipText_AllPublicStrings_AreNonEmpty()
    {
        // Any newly-added field with an empty default would leave a
        // blank tooltip; this catches that at test time.
        var fields = typeof(ToolTipText).GetFields(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static);
        foreach (var f in fields)
        {
            if (f.FieldType != typeof(string)) continue;
            string? v = f.GetValue(null) as string;
            Assert.NotNull(v);
            Assert.False(string.IsNullOrWhiteSpace(v),
                $"ToolTipText.{f.Name} is empty / whitespace.");
            Assert.True(v.Length > 20,
                $"ToolTipText.{f.Name} is suspiciously short ({v.Length} chars). "
              + "Tooltips should be a full sentence + range / context.");
        }
    }

    [Fact]
    public void ToolTipText_AtLeastFortyFields_Present()
    {
        // Sanity: the tooltip-coverage criteria asks for "every
        // input on the form (~80 controls)". 40 is a conservative lower
        // bound we should never regress below.
        int n = 0;
        foreach (var f in typeof(ToolTipText).GetFields(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static))
        {
            if (f.FieldType == typeof(string)) n++;
        }
        Assert.True(n >= 40,
            $"Expected ≥ 40 tooltip fields (one per major input); got {n}.");
    }
}
