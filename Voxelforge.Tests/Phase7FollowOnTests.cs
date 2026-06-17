// Phase7FollowOnTests.cs — Contract tests for the UI-polish sprint:
//   • ShortcutRouter  — Keys → Action mapper
//   • DragDropRouter  — file-path → Target mapper
//   • AboutInfo       — version / build-date surface
//   • RegenChamberForm.IsBusyStatusMessage — marquee trigger heuristic (U3.16)
//
// Each helper is a pure function so tests don't need a live Form.
// The form-side wiring (KeyPreview, AllowDrop, MenuStrip, status-bar
// panel, chkRunAllAnalyses) is covered by manual smoke + DEMO_SCRIPT.

using System.Windows.Forms;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class Phase7FollowOnTests
{
    // ─────────────────────────────────────────────────────────────────
    //  ShortcutRouter (U3.11)
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.F5,                    ShortcutRouter.Action.Generate)]
    [InlineData(Keys.Control | Keys.G,      ShortcutRouter.Action.Generate)]
    [InlineData(Keys.Control | Keys.S,      ShortcutRouter.Action.SaveDesign)]
    [InlineData(Keys.Control | Keys.O,      ShortcutRouter.Action.LoadDesign)]
    [InlineData(Keys.Control | Keys.E,      ShortcutRouter.Action.ExportStl)]
    [InlineData(Keys.Escape,                ShortcutRouter.Action.StopOpt)]
    [InlineData(Keys.F1,                    ShortcutRouter.Action.About)]
    public void ShortcutRouter_BoundKeys_MapToExpectedAction(Keys keys, ShortcutRouter.Action expected)
    {
        Assert.Equal(expected, ShortcutRouter.Resolve(keys));
    }

    [Theory]
    [InlineData(Keys.A)]                            // bare letter
    [InlineData(Keys.Control | Keys.A)]             // Ctrl+A (select all — not bound)
    [InlineData(Keys.Shift | Keys.F5)]              // Shift+F5
    [InlineData(Keys.F2)]                           // other function key
    [InlineData(Keys.Alt | Keys.G)]                 // Alt+G
    public void ShortcutRouter_Unbound_ReturnsNone(Keys keys)
    {
        Assert.Equal(ShortcutRouter.Action.None, ShortcutRouter.Resolve(keys));
    }

    // Shortcuts surface in About dialog.
    // Bindings is the single source of truth — FormatShortcutsList
    // prints it, the About dialog renders that, and these tests guard
    // the round-trip so a future rebinding can't silently desync the
    // Resolve switch from the documented list.

    [Fact]
    public void ShortcutRouter_Bindings_IsNonEmpty()
    {
        Assert.NotEmpty(ShortcutRouter.Bindings);
    }

    [Fact]
    public void ShortcutRouter_Bindings_CoverEveryNonNoneAction()
    {
        var covered = new System.Collections.Generic.HashSet<ShortcutRouter.Action>();
        foreach (var b in ShortcutRouter.Bindings) covered.Add(b.Action);
        foreach (ShortcutRouter.Action a in System.Enum.GetValues(typeof(ShortcutRouter.Action)))
        {
            if (a == ShortcutRouter.Action.None) continue;
            Assert.Contains(a, covered);
        }
    }

    [Fact]
    public void ShortcutRouter_FormatShortcutsList_ContainsEveryKeyLabel()
    {
        string list = ShortcutRouter.FormatShortcutsList();
        foreach (var b in ShortcutRouter.Bindings)
        {
            Assert.Contains(b.KeyLabel, list);
            Assert.Contains(b.ActionLabel, list);
        }
    }

    [Fact]
    public void ShortcutRouter_FormatShortcutsList_IsMultiLine()
    {
        string list = ShortcutRouter.FormatShortcutsList();
        // At least N-1 newlines for N bindings.
        int newlines = 0;
        foreach (char c in list) if (c == '\n') newlines++;
        Assert.Equal(ShortcutRouter.Bindings.Count - 1, newlines);
    }

    // ─────────────────────────────────────────────────────────────────
    //  DragDropRouter (U2.8)
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\work\chamber.rcd.json",       DragDropRouter.Target.DesignJson)]
    [InlineData("chamber.RCD.JSON",                DragDropRouter.Target.DesignJson)]   // case-insensitive
    [InlineData("INJECTOR.stl",                    DragDropRouter.Target.InjectorStl)]
    [InlineData(@"/tmp/test.stl",                  DragDropRouter.Target.InjectorStl)]  // forward slashes
    [InlineData("hotfire_run42.csv",               DragDropRouter.Target.MeasuredData)]
    [InlineData("notes.txt",                       DragDropRouter.Target.None)]
    [InlineData("some.json",                       DragDropRouter.Target.None)]          // bare .json doesn't count
    public void DragDropRouter_Resolves_ByExtension(string path, DragDropRouter.Target expected)
    {
        Assert.Equal(expected, DragDropRouter.Resolve(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DragDropRouter_Blank_ReturnsNone(string? path)
    {
        Assert.Equal(DragDropRouter.Target.None, DragDropRouter.Resolve(path));
    }

    [Fact]
    public void DragDropRouter_CompoundExtension_WinsOverBareJson()
    {
        // "foo.rcd.json" must route to DesignJson, not fall through to None
        // (the bare ".json" check doesn't exist but the compound suffix does).
        Assert.Equal(DragDropRouter.Target.DesignJson, DragDropRouter.Resolve("foo.rcd.json"));
        Assert.Equal(DragDropRouter.Target.None,       DragDropRouter.Resolve("foo.json"));
    }

    // ─────────────────────────────────────────────────────────────────
    //  AboutInfo (U3.12)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AboutInfo_ProductName_And_Version_Match_ProjectStatus()
    {
        // Stamp the current version once here; when the project bumps
        // past this value the test turns into the forcing function
        // that reminds the next contributor to update AboutInfo too.
        // See AboutInfo.cs comment for the versioning convention.
        Assert.Equal("Voxelforge", AboutInfo.ProductName);
        Assert.Equal("v1.0.0",               AboutInfo.Version);
    }

    [Fact]
    public void AboutInfo_AssemblyVersion_NonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(AboutInfo.AssemblyVersion));
    }

    [Fact]
    public void AboutInfo_FormatSummary_Contains_ProductName_And_Version()
    {
        string s = AboutInfo.FormatSummary();
        Assert.Contains(AboutInfo.ProductName, s);
        Assert.Contains(AboutInfo.Version,     s);
    }

    [Fact]
    public void AboutInfo_TestCount_Matches_ProjectStatus()
    {
        // Forcing function for the test-count snapshot surfaced by the
        // About dialog. When a future sprint adds or removes tests, this
        // assertion trips and reminds the contributor to re-stamp
        // AboutInfo.TestCount in lockstep. Sprint 15 / Track G added 4
        // tests for the aerospike plug-channel cooling opt-in
        // (1103 → 1107); Sprint 18 added 5 for the Pintle injector
        // (1107 → 1112); Sprint 21 added 23 for the CycleSolver
        // cycle-balance refactor (1112 → 1135); Sprint 20 added 15
        // for the dual-bell nozzle (1135 → 1150); Sprint 19 added 6
        // for pressure-fed blow-down + small-thrust preset (1150 → 1156);
        // Sprint 23 added 12 for expander cycle (1156 → 1168);
        // Sprint 24 added 11 for ORSC (1168 → 1179); Sprint 25
        // added 14 for TapOff cycle (1179 → 1193); Sprint 27 added 23
        // for the LPBF printability analysis + three new gates
        // (1193 → 1216); Sprint 28-StlExporter added 7 for CliArgs +
        // BuildSubprocessRequest (1216 → 1223); Sprint 26 cascaded +15
        // for linear aerospike (contour/builder/gate + SDF follow-on)
        // (1223 → 1238); Sprint 28 instrumentation-clash added 14
        // (1238 → 1252); Sprint 29 added 18 for per-pair ignition
        // requirements + IGNITER_MISSING / IGNITER_MODALITY_UNSUITABLE
        // gates (1252 → 1270); Sprint 30 added 14 for PH-2 NPSHR Thoma
        // form + PH-3 trapped-powder threshold (1270 → 1284); Sprint 31
        // added 8 for PH-1 Angelino back-solve + PH-15 per-station
        // FlowAngle (1284 → 1292); Sprint 32 added 0 (consistency
        // fixes re-baselined existing tests, 1292 → 1292).
        Assert.Equal(1292, AboutInfo.TestCount);
    }

    [Fact]
    public void AboutInfo_FormatSummary_Contains_TestCount()
    {
        string s = AboutInfo.FormatSummary();
        Assert.Contains(AboutInfo.TestCount.ToString(), s);
        Assert.Contains("tests",                        s);
    }

    [Fact]
    public void AboutInfo_TestCount_Is_Positive()
    {
        Assert.True(AboutInfo.TestCount > 0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  AboutInfo.FormatDiagnosticInfo() + ShortcutRouter auto-sized key column
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AboutInfo_FormatDiagnosticInfo_ContainsVersion()
    {
        string s = AboutInfo.FormatDiagnosticInfo();
        Assert.Contains(AboutInfo.ProductName, s);
        Assert.Contains(AboutInfo.Version,     s);
    }

    [Fact]
    public void AboutInfo_FormatDiagnosticInfo_ContainsTestCount()
    {
        string s = AboutInfo.FormatDiagnosticInfo();
        Assert.Contains(AboutInfo.TestCount.ToString(), s);
    }

    [Fact]
    public void AboutInfo_FormatDiagnosticInfo_ContainsRuntimeSection()
    {
        string s = AboutInfo.FormatDiagnosticInfo();
        Assert.Contains("OS:",         s);
        Assert.Contains("Runtime:",    s);
        Assert.Contains("Processors:", s);
        Assert.Contains(System.Environment.ProcessorCount.ToString(), s);
    }

    [Fact]
    public void AboutInfo_FormatDiagnosticInfo_ContainsShortcutsSection()
    {
        string s = AboutInfo.FormatDiagnosticInfo();
        Assert.Contains("Keyboard shortcuts", s);
        // Every documented shortcut must round-trip through the
        // diagnostic block, so pasting it into a bug report gives
        // triage enough to reproduce the user's key sequence.
        foreach (var b in ShortcutRouter.Bindings)
        {
            Assert.Contains(b.KeyLabel,    s);
            Assert.Contains(b.ActionLabel, s);
        }
    }

    [Fact]
    public void AboutInfo_FormatDiagnosticInfo_IsMultiLine()
    {
        string s = AboutInfo.FormatDiagnosticInfo();
        int newlines = 0;
        foreach (char c in s) if (c == '\n') newlines++;
        // Summary + OS + Runtime + Processors lines, then a blank line,
        // then "Keyboard shortcuts" header + N-1 separators between
        // bindings. Demand at least five line breaks so a future layout
        // regression (collapse to one line) trips.
        Assert.True(newlines >= 5, $"expected ≥5 newlines, got {newlines}");
    }

    [Fact]
    public void ShortcutRouter_FormatShortcutsList_KeyColumn_AlignsToLongestKey()
    {
        // Guard the auto-sizing contract: every line's "— " separator
        // must sit at the same column index. Adding a longer chord
        // (e.g. "Ctrl+Shift+X") to Bindings must not break alignment.
        string list = ShortcutRouter.FormatShortcutsList();
        string[] lines = list.Split('\n');
        int firstSepIdx = lines[0].IndexOf("— ", System.StringComparison.Ordinal);
        Assert.True(firstSepIdx > 0, "separator should not be at column 0");
        for (int i = 1; i < lines.Length; i++)
        {
            int idx = lines[i].IndexOf("— ", System.StringComparison.Ordinal);
            Assert.Equal(firstSepIdx, idx);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  IsBusyStatusMessage — U3.16 marquee trigger heuristic
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Regenerating\u2026",             true)]   // single-char …
    [InlineData("Running tolerance sweep\u2026",  true)]
    [InlineData("Working...",                     true)]   // three ASCII dots
    [InlineData("Starting batch run\u2026  ",     true)]   // trailing whitespace tolerated
    [InlineData("Ready.",                         false)]
    [InlineData("Error: bad file",                false)]
    [InlineData("Wrote 42 Pareto points \u2192 pareto.csv", false)]
    [InlineData("",                               false)]
    [InlineData(null,                             false)]
    public void IsBusyStatusMessage_Matches_EllipsisSuffix(string? msg, bool expected)
    {
        Assert.Equal(expected, Voxelforge.UI.RegenChamberForm.IsBusyStatusMessage(msg));
    }
}
