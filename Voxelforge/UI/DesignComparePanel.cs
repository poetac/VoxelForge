// DesignComparePanel.cs — Per-design side-by-side comparison.
//
// Loads two designs (A = "current" = whatever ApplyDesign last set,
// B = "loaded from disk" via the Compare with… file dialog), runs
// the same `GenerateWith` against both via the fast
// `skipVoxelGeometry: true` path, and shows ~12 headline numbers in
// two columns + a colour-coded delta column.
//
// Colour convention (matches the rest of the form):
//   • Green text = B is BETTER than A
//   • Red text   = B is WORSE than A
//   • Black      = within ±1 % (effectively unchanged)
//
// "Better" is metric-specific: lower peak T = better; higher SF =
// better; lower mass = better; etc. The metric → direction mapping
// is hard-coded in `BetterIfLower` below.
//
// Buttons:
//   • Compare with… → file picker, loads B from disk
//   • Swap A ↔ B   → swaps the two columns visually
//   • Open B as A  → applies B's design to the main form (calls
//                    the host form's ApplyDesign hook)
//
// The panel is a `UserControl`, embedded in the right-column flow
// next to the existing analysis groups. Hidden by default; expanded
// when the user clicks Compare with…
//
// Implementation note: the comparison runs `GenerateWith` on the
// task thread via a host-supplied callback (so PicoGK voxel safety
// is preserved even though we use `skipVoxelGeometry: true` and
// don't actually touch voxels). The host posts the work and reports
// the result back via `SetResults`.

using System.Drawing;
using System.Windows.Forms;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

public sealed class DesignComparePanel : UserControl
{
    /// <summary>
    /// Host-supplied callback to load + generate a design from a path.
    /// Returns null on failure (file missing, JSON invalid, etc.).
    /// Runs on the host's task thread / threadpool — must not touch
    /// the form directly; results come back via <see cref="SetB"/>.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public System.Func<string, RegenGenerationResult?>? LoadAndGenerate { get; set; }

    /// <summary>
    /// Host-supplied callback fired when the user clicks "Open B as A".
    /// The host should `ApplyDesign(b.Conditions, b's design)` on its
    /// main form to make B the active design.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public System.Action<OperatingConditions, RegenChamberDesign>? OpenB { get; set; }

    private RegenGenerationResult? _a;
    private RegenGenerationResult? _b;
    // B's design (we don't persist it on the result — host passes it).
    private RegenChamberDesign? _bDesign;

    private readonly Label _lblHeader;
    private readonly Button _btnLoadB, _btnSwap, _btnOpenB;
    private readonly Panel _grid;

    public DesignComparePanel()
    {
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        Padding = new Padding(4);

        _lblHeader = new Label
        {
            Text = "Design comparison — load B to see deltas vs current.",
            AutoSize = false, Width = 760, Height = 22,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _btnLoadB  = new Button { Text = "Compare with…", Width = 130, Height = 28, Margin = new Padding(2) };
        _btnSwap   = new Button { Text = "Swap A ↔ B",   Width = 110, Height = 28, Margin = new Padding(2), Enabled = false };
        _btnOpenB  = new Button { Text = "Open B as A",  Width = 110, Height = 28, Margin = new Padding(2), Enabled = false };

        _btnLoadB.Click += (_, _) => OnLoadBClick();
        _btnSwap.Click  += (_, _) => OnSwapClick();
        _btnOpenB.Click += (_, _) => OnOpenBClick();

        var btnFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true, Width = 760,
        };
        btnFlow.Controls.AddRange(new Control[] { _btnLoadB, _btnSwap, _btnOpenB });

        _grid = new Panel
        {
            Width = 760, Height = 340, AutoScroll = true,
            BackColor = Color.White,
        };

        var outer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true,
            Dock = DockStyle.Fill,
        };
        outer.Controls.Add(_lblHeader);
        outer.Controls.Add(btnFlow);
        outer.Controls.Add(_grid);
        Controls.Add(outer);

        Width = 780; Height = 410;
        RenderEmpty();
    }

    /// <summary>
    /// Set the "A" (current) result. Host calls this whenever
    /// ApplyDesign / Generate refreshes the main view so B's deltas
    /// stay current.
    /// </summary>
    public void SetA(RegenGenerationResult? a)
    {
        _a = a;
        Render();
    }

    /// <summary>
    /// Set the "B" (compare-with) result + its source design. Called
    /// by the host after the LoadAndGenerate callback finishes on
    /// the task thread.
    /// </summary>
    public void SetB(RegenGenerationResult? b, RegenChamberDesign? bDesign)
    {
        _b = b;
        _bDesign = bDesign;
        _btnSwap.Enabled  = b != null;
        _btnOpenB.Enabled = b != null && bDesign != null;
        Render();
    }

    // ─── Internals ──────────────────────────────────────────────────

    private void OnLoadBClick()
    {
        if (LoadAndGenerate is null)
        { _lblHeader.Text = "Compare unavailable: host did not wire LoadAndGenerate."; return; }
        using var dlg = new OpenFileDialog { Filter = "Regen design|*.rcd.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _lblHeader.Text = $"Comparing… ({System.IO.Path.GetFileName(dlg.FileName)})";
        var result = LoadAndGenerate(dlg.FileName);
        if (result is null)
        {
            _lblHeader.Text = $"Compare failed for {System.IO.Path.GetFileName(dlg.FileName)}.";
            return;
        }
        // Host knows B's design — caller should set _bDesign via SetB
        // following its load. For the simple case, we also expose the
        // result alone via SetB(result, null) which still drives the
        // diff but greys out "Open B as A".
        SetB(result, null);
    }

    private void OnSwapClick()
    {
        (_a, _b) = (_b, _a);
        // Note: _bDesign now refers to the OLD A; we don't have A's
        // design plumbed in (host owns it), so disable Open-B.
        _bDesign = null;
        _btnOpenB.Enabled = false;
        Render();
    }

    private void OnOpenBClick()
    {
        if (_b is null || _bDesign is null || OpenB is null) return;
        OpenB(_b.Conditions, _bDesign);
        _lblHeader.Text = "B promoted to A. Compare panel reset.";
        SetB(null, null);
    }

    private void RenderEmpty()
    {
        _grid.Controls.Clear();
        var empty = new Label
        {
            Text = "Click \"Compare with…\" to load a second .rcd.json. "
                 + "The two designs will be evaluated side-by-side; deltas "
                 + "are marked \u2713 (green) = B better, \u2717 (red) = B worse.",
            Width = 740, Height = 60,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 4, 4, 4),
        };
        _grid.Controls.Add(empty);
    }

    private void Render()
    {
        if (_a is null || _b is null)
        {
            RenderEmpty();
            if (_a is null) _lblHeader.Text = "A is empty — generate a design first.";
            else if (_b is null) _lblHeader.Text = "B is empty — load a second design via Compare with…";
            return;
        }

        _lblHeader.Text = "Comparing  A (current)  vs  B (loaded).  \u2713 (green) = B better, \u2717 (red) = B worse.";
        _grid.Controls.Clear();

        // 4-column grid: metric name | A | B | Δ. One row per metric.
        var rows = BuildRows(_a, _b);

        var table = new TableLayoutPanel
        {
            ColumnCount = 4, RowCount = rows.Count + 1,
            AutoSize = true, Width = 740,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = Color.White,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));

        // Header row.
        table.Controls.Add(MakeCell("Metric",  bold: true), 0, 0);
        table.Controls.Add(MakeCell("A",       bold: true), 1, 0);
        table.Controls.Add(MakeCell("B",       bold: true), 2, 0);
        table.Controls.Add(MakeCell("Δ (B−A)", bold: true), 3, 0);

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            table.Controls.Add(MakeCell(r.Name),                    0, i + 1);
            table.Controls.Add(MakeCell(r.AStr),                    1, i + 1);
            table.Controls.Add(MakeCell(r.BStr),                    2, i + 1);
            table.Controls.Add(MakeCell(r.DeltaStr, fg: r.Color),   3, i + 1);
        }
        _grid.Controls.Add(table);
    }

    private static Label MakeCell(string txt, bool bold = false, Color? fg = null) => new()
    {
        Text = txt, AutoSize = false, Width = 130, Height = 22,
        Font = new Font("Consolas", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = fg ?? Color.Black,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(4, 0, 4, 0),
        // Δ-column strings like "+1234.56 (+56.7 %)" can
        // exceed 130 px in Consolas 9 pt; ellipsis keeps the last chars
        // clipped visibly instead of silently cut.
        AutoEllipsis = true,
    };

    private readonly record struct CompareRow(
        string Name, string AStr, string BStr, string DeltaStr, Color Color);

    private static System.Collections.Generic.List<CompareRow> BuildRows(
        RegenGenerationResult a, RegenGenerationResult b)
    {
        var rows = new System.Collections.Generic.List<CompareRow>();

        Add("Peak wall T (K)",         a.Thermal.PeakGasSideWallT_K,  b.Thermal.PeakGasSideWallT_K,  "F0", betterIfLower: true);
        Add("Coolant ΔP (MPa)",        a.Thermal.CoolantPressureDrop_Pa / 1e6, b.Thermal.CoolantPressureDrop_Pa / 1e6, "F2", betterIfLower: true);
        Add("Coolant T_out (K)",       a.Thermal.CoolantOutletT_K,    b.Thermal.CoolantOutletT_K,    "F0", betterIfLower: false);
        Add("Throat heat flux (MW/m²)",a.Thermal.ThroatHeatFlux_Wm2/1e6, b.Thermal.ThroatHeatFlux_Wm2/1e6, "F2", betterIfLower: true);
        Add("Total heat load (kW)",    a.Thermal.TotalHeatLoad_W/1e3,  b.Thermal.TotalHeatLoad_W/1e3,  "F1", betterIfLower: true);
        Add("Min safety factor",       a.Stress.MinSafetyFactor,       b.Stress.MinSafetyFactor,       "F2", betterIfLower: false);
        Add("Mass (g)",                a.Geometry.TotalMass_g,         b.Geometry.TotalMass_g,         "F0", betterIfLower: true);
        Add("Print cost (USD)",        a.Geometry.PrintedCost_USD,     b.Geometry.PrintedCost_USD,     "F0", betterIfLower: true);
        Add("Min feature (mm)",        a.Manufacturing.MinFeatureSize_mm, b.Manufacturing.MinFeatureSize_mm, "F2", betterIfLower: false);
        Add("Throat dia (mm)",         a.Derived.ThroatDiameter_mm,    b.Derived.ThroatDiameter_mm,    "F2", betterIfLower: false);
        Add("Total length (mm)",       a.Contour.TotalLength_mm,       b.Contour.TotalLength_mm,       "F0", betterIfLower: true);
        Add("Isp vacuum (s)",          a.Derived.IdealIspVacuum_s,     b.Derived.IdealIspVacuum_s,     "F0", betterIfLower: false);

        // Stability composite is a categorical Pass / Marginal / Fail —
        // emit it as text and colour-code the delta as "improved" /
        // "regressed" if the rating changed.
        rows.Add(StabilityRow(a, b));

        return rows;

        void Add(string name, double aVal, double bVal, string fmt, bool betterIfLower)
        {
            double delta = bVal - aVal;
            double frac = aVal != 0 ? delta / System.Math.Abs(aVal) : 0;
            string aStr = aVal.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            string bStr = bVal.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            bool unchanged = System.Math.Abs(frac) < 0.01;
            bool bIsBetter = !unchanged && ((delta < 0) == betterIfLower);
            // Prefix ✓/✗ so colour-blind users can distinguish
            // improvement from regression without relying on green/red.
            // Direction arrows (↑/↓) would mislead on mixed-polarity
            // metrics (e.g., higher Isp = better but arrow-up would
            // normally imply regression on most rows).
            string marker = unchanged ? "  " : (bIsBetter ? "\u2713 " : "\u2717 ");
            string deltaStr = marker
                + delta.ToString("+" + fmt + ";-" + fmt + ";0",
                    System.Globalization.CultureInfo.InvariantCulture)
                + $"  ({frac * 100,+0:F1}%)";
            Color color = unchanged
                ? Color.Black
                : (bIsBetter ? Color.DarkGreen : Color.Firebrick);
            rows.Add(new CompareRow(name, aStr, bStr, deltaStr, color));
        }
    }

    private static CompareRow StabilityRow(RegenGenerationResult a, RegenGenerationResult b)
    {
        var aR = a.Stability.Composite;
        var bR = b.Stability.Composite;
        // Green if B improved (Fail→Marginal/Pass; Marginal→Pass);
        // Red if B regressed; Black otherwise.
        bool improved = (int)bR < (int)aR;
        bool regressed = (int)bR > (int)aR;
        Color color = improved ? Color.DarkGreen
                    : regressed ? Color.Firebrick
                    : Color.Black;
        string marker = improved ? "\u2713 " : regressed ? "\u2717 " : "  ";
        string delta = aR == bR ? "(unchanged)" : $"{marker}{aR} → {bR}";
        return new CompareRow(
            "Stability composite",
            aR.ToString(),
            bR.ToString(),
            delta,
            color);
    }
}
