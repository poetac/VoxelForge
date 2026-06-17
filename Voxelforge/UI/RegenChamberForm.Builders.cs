// RegenChamberForm.Builders.cs — Sprint 6 Track B (2026-04-22):
// Partial-class sibling carrying the pure-logic WinForms control factories
// extracted from the 3254-LOC RegenChamberForm.cs monolith.
//
// Why a partial class instead of a separate static utility class:
//   • These methods are called ~300+ times across the form's
//     constructor / UpdateResults / event-handler paths. Changing
//     every call site from `Num(...)` to `RegenChamberFormBuilders.Num(...)`
//     is high-noise, high-risk churn. A partial declaration lets the
//     existing call sites keep working bit-identically.
//   • The visibility stays `private static` — these helpers are
//     intentionally form-internal.
//   • No behaviour change. Compile the solution with this file removed
//     and re-added; the resulting RegenChamberForm metadata + MSIL is
//     identical (modulo file-position line numbers in PDB output).
//
// What lives here:
//   Num, SetNum  — numeric up-down factories with value clamping.
//   Out          — read-only Consolas label (results column).
//   Pill         — traffic-light label for stability states.
//   ApplyStabilityPill — mutator for a Pill label based on StabilityRating.
//   Row          — label + input horizontal layout container.
//   Group        — section container with Panel + bold header + FlowLayoutPanel.
//   Group (collapsible overload) — same with a click-to-toggle header.
//   MakeHelp     — italic inline-help label (group footnote).
//
// What does NOT live here (stays in RegenChamberForm.cs):
//   Event handlers, control-field declarations, InitializeComponent
//   equivalent (constructor), SessionSettings wiring, SharedState
//   marshalling, UpdateResults, any method touching `this.*` form state.

using System.Windows.Forms;

namespace Voxelforge.UI;

public sealed partial class RegenChamberForm
{
    // ═══════════════════════════════════════════════════════════════
    //   UI builders (extracted from RegenChamberForm.cs in Sprint 6
    //   Track B; behaviour unchanged).
    // ═══════════════════════════════════════════════════════════════

    private static NumericUpDown Num(double val, double min, double max, double step, int decimals)
    {
        decimal dMin = (decimal)min, dMax = (decimal)max;
        decimal dVal = System.Math.Max(dMin, System.Math.Min(dMax, (decimal)val));
        return new NumericUpDown
        {
            Minimum = dMin, Maximum = dMax, Value = dVal,
            Increment = (decimal)step, DecimalPlaces = decimals, Width = 140
        };
    }

    private static void SetNum(NumericUpDown n, double v)
    {
        decimal dv = (decimal)v;
        if (dv < n.Minimum) dv = n.Minimum;
        if (dv > n.Maximum) dv = n.Maximum;
        n.Value = dv;
    }

    private static Label Out(string initial) => new Label
    {
        Text = initial,
        AutoSize = false,
        // 2026-04-22 UX pass: bumped width 620 → 780 so long lines like
        // `Isp (vac/sl ideal): 365 / 339 s | C_F 1.50` and `Material src:
        // ASM Handbook Vol 2 (C18150)` stop clipping in the right column.
        // The right panel docks Fill so it effectively has ≈780 px usable
        // at the default 1480-wide form; the label just needs to match.
        Width = 780,
        Height = 20,
        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        Font = new System.Drawing.Font("Consolas", 9f),
        Margin = new Padding(3, 2, 3, 2),
        AutoEllipsis = true,
    };

    /// <summary>
    /// Traffic-light "pill" style label. Width is fixed; colour is set
    /// by the caller (background tracks pass/marginal/fail). Used by
    /// the Combustion Stability group — three side-by-side pills.
    /// </summary>
    private static Label Pill(string initial) => new Label
    {
        Text = initial,
        AutoSize = false,
        Width = 210,
        Height = 28,
        TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
        Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
        Margin = new Padding(3, 2, 3, 2),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = System.Drawing.Color.LightGray,
        ForeColor = System.Drawing.Color.Black,
    };

    /// <summary>
    /// Colour a stability pill per the traffic-light rating.
    /// Green = Pass, Amber = Marginal, Red = Fail. Routes every
    /// (bg, fg) pair through <see cref="ThemeManager"/> so
    /// High-Contrast + Windows dark-mode users see a consistent pill
    /// instead of hardcoded light-theme RGB triples.
    /// </summary>
    private static void ApplyStabilityPill(
        Label pill, string title, Combustion.Stability.StabilityRating rating)
    {
        string word = Combustion.Stability.StabilityReport.RatingWord(rating);
        pill.Text = $"{title}: {word}";
        var sev = rating switch
        {
            Combustion.Stability.StabilityRating.Pass     => PillSeverity.Pass,
            Combustion.Stability.StabilityRating.Marginal => PillSeverity.Marginal,
            Combustion.Stability.StabilityRating.Fail     => PillSeverity.Fail,
            _                                             => PillSeverity.Neutral,
        };
        var palette = ThemeManager.Pill(sev);
        pill.BackColor = palette.Background;
        pill.ForeColor = palette.Foreground;
    }

    /// <summary>
    /// UI overhaul Sprint 1 Step 2 (2026-04-28) — instance-method
    /// overload of <see cref="Row(string, Control)"/> that also
    /// registers the produced row panel with
    /// <see cref="_visibilityRegistry"/> under the supplied field
    /// key. Use this overload everywhere a row's visibility may be
    /// conditionally controlled by cycle / topology / pair / opt-in
    /// state. The optional <paramref name="group"/> lets the registry
    /// cascade group-level visibility — when every child of a group
    /// is hidden, the group container also hides.
    /// </summary>
    private Panel Row(string label, Control input, string fieldKey, Control? group = null)
    {
        var panel = Row(label, input);
        _visibilityRegistry.Register(fieldKey, panel, group);
        return panel;
    }

    private static Panel Row(string label, Control input)
    {
        // UX fix: panel 36 → 40, label 32 → 36 — at 125/150 % DPI the
        // descender of Segoe UI 9.5pt was being trimmed inside the old
        // 32-px label, most visibly on strings with g/p/y (e.g. "Coolant
        // pumping cycle", "Propellant boiled off").
        var p = new Panel { Width = 660, Height = 40, Margin = new Padding(2, 2, 2, 2) };
        var lbl = new Label
        {
            Text = label,
            AutoSize = false,
            Width = 310,
            Height = 36,
            Left = 6,
            Top = 2,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        p.Controls.Add(lbl);
        input.Left = 320;
        input.Top = (p.Height - System.Math.Max(input.Height, 22)) / 2;
        input.Width = System.Math.Max(input.Width, 300);
        p.Controls.Add(input);
        return p;
    }

    /// <summary>
    /// 2026-04-22 UX pass: collapsible variant of <see cref="Group(string, Control[])"/>.
    /// When <paramref name="startCollapsed"/> is true, the inner flow is
    /// hidden at startup; clicking the header toggles visibility. The
    /// header shows a "▸ Title" / "▾ Title" chevron prefix so the user
    /// can tell at a glance which sections are rolled up. Used for
    /// low-frequency tuning groups (Proof Test, LPBF Tolerance, etc.)
    /// that otherwise clutter the initial view.
    /// </summary>
    private static Panel Group(string title, bool startCollapsed, params Control[] children)
    {
        // UX fix: header Height 26 → 32 (and flow Y 30 → 36) so the
        // bold Segoe UI 9.5pt title isn't clipped at 125/150 % DPI. The
        // same bump applies to the non-collapsible `Group` overload below.
        var header = new Label
        {
            AutoSize = false,
            Location = new System.Drawing.Point(0, 0),
            Width = 780,
            Height = 32,
            Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = System.Drawing.SystemColors.ControlLight,
            Cursor = Cursors.Hand,
        };

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Location = new System.Drawing.Point(6, 36),
            Visible = !startCollapsed,
        };
        foreach (var c in children) flow.Controls.Add(c);

        void UpdateHeader() =>
            header.Text = (flow.Visible ? "\u25be  " : "\u25b8  ") + title;
        UpdateHeader();

        header.Click += (_, _) => { flow.Visible = !flow.Visible; UpdateHeader(); };

        var panel = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 4, 4, 8),
            BorderStyle = BorderStyle.FixedSingle,
        };
        panel.Controls.Add(header);
        panel.Controls.Add(flow);
        return panel;
    }

    /// <summary>
    /// 2026-04-22 UX pass: small italic help text rendered beneath the
    /// primary controls of a group. Word-wraps to the group width and
    /// takes its vertical height from the content.
    /// </summary>
    private static Label MakeHelp(string text) => new Label
    {
        Text = text,
        AutoSize = false,
        Width = 660,
        Height = 58,
        TextAlign = System.Drawing.ContentAlignment.TopLeft,
        Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic),
        ForeColor = System.Drawing.Color.DimGray,
        Padding = new Padding(4, 2, 4, 2),
    };

    private static Panel Group(string title, params Control[] children)
    {
        // GOTCHA (2026-04-17): `AutoSize = GrowAndShrink` on the GroupBox
        // combined with an inner `Dock = Top` + `AutoSize = true`
        // FlowLayoutPanel causes the GroupBox to collapse to ~12 px wide —
        // Dock=Top reports preferred width = 0 while the parent is
        // auto-sizing, a well-known WinForms chicken-and-egg.
        //
        // GOTCHA (2026-04-18): GroupBox.AutoSize does NOT include the
        // GroupBox's own title-text height in its preferred-size calculation.
        // At 125 %–150 % DPI (Windows 11 default) the title text grows past
        // the fixed border-title reservation and gets clipped. Symptom: every
        // section heading shows as a thin compressed strip or disappears.
        //
        // Fix: replace GroupBox with Panel + explicit Label title bar.
        // The Label's height is set in pixels, is always fully rendered,
        // and participates correctly in AutoSize on the outer Panel.
        // The inner flow still uses a manual Location (no Dock) to avoid
        // the preferred-width = 0 collapse described above.
        var header = new Label
        {
            Text = title,
            AutoSize = false,
            Location = new System.Drawing.Point(0, 0),
            Width = 780,
            Height = 32,
            Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = System.Drawing.SystemColors.ControlLight,
        };

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Location = new System.Drawing.Point(6, 36),  // below header (32 px) + 4 px gap
        };
        foreach (var c in children) flow.Controls.Add(c);

        var panel = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 4, 4, 8),
            BorderStyle = BorderStyle.FixedSingle,
        };
        panel.Controls.Add(header);
        panel.Controls.Add(flow);
        return panel;
    }
}
