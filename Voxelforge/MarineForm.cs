// MarineForm.cs — WinForms UI for the marine pillar (AUV displacement hulls).
//
// Wave-1 + Wave-2 cover AuvMidBody with HullFamily ∈ {Myring, CylindricalHemi}.
// Surface hulls + planing hulls slot in via the MarineKind ComboBox when later
// waves land. Issue #441.

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Voxelforge.Marine;
using Voxelforge.Marine.Geometry;

namespace Voxelforge.UI;

internal sealed class MarineForm : Form
{
    private readonly ComboBox      _cmbKind         = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox      _cmbHullFamily   = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ComboBox      _cmbMaterial     = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

    // Operating conditions
    private readonly NumericUpDown _nudCruiseSpeed  = MakeNud(0.1m,  10m,    1.5m,   2);
    private readonly NumericUpDown _nudMaxDepth     = MakeNud(10m,   6000m,  100m,   0);
    private readonly NumericUpDown _nudWaterTemp    = MakeNud(270m,  300m,   277.15m, 2);
    private readonly NumericUpDown _nudSalinity     = MakeNud(0m,    40m,    35m,    1);

    // Common hull design
    private readonly NumericUpDown _nudLength       = MakeNud(0.5m,  20m,    1.6m,   3);
    private readonly NumericUpDown _nudDiameter     = MakeNud(0.05m, 5m,     0.19m,  3);
    private readonly NumericUpDown _nudWallThickMm  = MakeNud(1m,    100m,   5m,     1);
    private readonly NumericUpDown _nudDepthRating  = MakeNud(10m,   6000m,  100m,   0);

    // Myring-specific
    private readonly NumericUpDown _nudNoseFraction = MakeNud(0.05m, 0.45m,  0.20m,  3);
    private readonly NumericUpDown _nudTailFraction = MakeNud(0.05m, 0.45m,  0.30m,  3);

    private GroupBox? _myringGroup;

    private readonly Button  _btnGenerate = new() { Text = "Generate", Height = 36 };
    private readonly TextBox _txtResults  = new() { Multiline = true, ReadOnly = true, Height = 220, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    private readonly Label   _lblStatus   = new() { Text = "Ready.", AutoSize = true };

    private readonly Action<MarineConditions, MarineDesign> _onGenerate;

    internal MarineForm(Action<MarineConditions, MarineDesign> onGenerate)
    {
        _onGenerate = onGenerate ?? throw new ArgumentNullException(nameof(onGenerate));

        // Wave-1 only ships AuvMidBody (sentinel None excluded).
        _cmbKind.Items.Add("AuvMidBody");
        _cmbKind.SelectedIndex = 0;

        _cmbHullFamily.Items.AddRange(new object[] { "Myring", "CylindricalHemi" });
        _cmbHullFamily.SelectedIndex = 0;
        _cmbHullFamily.SelectedIndexChanged += (_, _) => UpdateHullFamilyVisibility();

        _cmbMaterial.Items.AddRange(new object[]
        {
            "0 — Ti-6Al-4V",
            "1 — Al-6061",
            "2 — AISI-316L (LPBF)",
        });
        _cmbMaterial.SelectedIndex = 1; // Al-6061 (REMUS-100 reference)

        Text            = "Voxelforge — Marine Hull Viewer  [AUV mid-body]";
        Width           = 480;
        AutoSize        = true;
        AutoSizeMode    = AutoSizeMode.GrowOnly;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        Padding         = new Padding(10);
        StartPosition   = FormStartPosition.Manual;
        Location        = new Point(10, 10);

        var layout = new TableLayoutPanel
        {
            Dock         = DockStyle.Fill,
            ColumnCount  = 1,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.Controls.Add(BuildLabeledGroup("Vehicle kind",
            ("Kind:", _cmbKind),
            ("Hull family:", _cmbHullFamily)));
        layout.Controls.Add(BuildLabeledGroup("Operating conditions",
            ("Cruise speed (m/s):", _nudCruiseSpeed),
            ("Max depth (m):", _nudMaxDepth),
            ("Water T (K):", _nudWaterTemp),
            ("Salinity (g/kg):", _nudSalinity)));
        layout.Controls.Add(BuildLabeledGroup("Hull common",
            ("Length (m):", _nudLength),
            ("Diameter (m):", _nudDiameter),
            ("Wall thickness (mm):", _nudWallThickMm),
            ("Depth rating (m):", _nudDepthRating),
            ("Material:", _cmbMaterial)));
        _myringGroup = BuildLabeledGroup("Myring fairing",
            ("Nose fraction:", _nudNoseFraction),
            ("Tail fraction:", _nudTailFraction));
        layout.Controls.Add(_myringGroup);
        layout.Controls.Add(_btnGenerate);
        layout.Controls.Add(_txtResults);
        layout.Controls.Add(_lblStatus);

        Controls.Add(layout);

        _btnGenerate.Dock     = DockStyle.Fill;
        _txtResults.Dock      = DockStyle.Fill;
        _txtResults.BackColor = SystemColors.Window;

        _btnGenerate.Click += (_, _) => PostGenerate();
        UpdateHullFamilyVisibility();
        Load               += (_, _) => PostGenerate();
    }

    private static NumericUpDown MakeNud(decimal min, decimal max, decimal @default, int decimals)
        => new()
        {
            Minimum       = min,
            Maximum       = max,
            Value         = @default,
            DecimalPlaces = decimals,
            Increment     = decimals == 0 ? 100 : (decimal)Math.Pow(10, -decimals),
            Width         = 110,
        };

    private static GroupBox BuildLabeledGroup(string title, params (string label, Control ctrl)[] rows)
    {
        var gb = new GroupBox
        {
            Text     = title,
            AutoSize = true,
            Dock     = DockStyle.Fill,
            Padding  = new Padding(6, 14, 6, 6),
        };
        var inner = new TableLayoutPanel
        {
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 2,
            Dock         = DockStyle.Fill,
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var (label, ctrl) in rows)
        {
            inner.Controls.Add(new Label { Text = label, AutoSize = true });
            ctrl.Anchor = AnchorStyles.Left;
            inner.Controls.Add(ctrl);
        }
        gb.Controls.Add(inner);
        return gb;
    }

    private HullFamily SelectedHullFamily() => (_cmbHullFamily.SelectedItem as string) switch
    {
        "CylindricalHemi" => HullFamily.CylindricalHemi,
        _                 => HullFamily.Myring,
    };

    private void UpdateHullFamilyVisibility()
    {
        if (_myringGroup is null) return;
        _myringGroup.Visible = SelectedHullFamily() == HullFamily.Myring;
    }

    private void PostGenerate()
    {
        SetStatus("Solving…");

        var cond = new MarineConditions(
            CruiseSpeed_ms:     (double)_nudCruiseSpeed.Value,
            MaxDepth_m:         (double)_nudMaxDepth.Value,
            WaterTemperature_K: (double)_nudWaterTemp.Value,
            Salinity_ppt:       (double)_nudSalinity.Value);

        var family = SelectedHullFamily();
        var design = new MarineDesign(
            Kind:                MarineKind.AuvMidBody,
            Length_m:            (double)_nudLength.Value,
            Diameter_m:          (double)_nudDiameter.Value,
            NoseFairingFraction: family == HullFamily.Myring ? (double)_nudNoseFraction.Value : 0.0,
            TailFairingFraction: family == HullFamily.Myring ? (double)_nudTailFraction.Value : 0.0,
            WallThickness_m:     (double)_nudWallThickMm.Value * 1e-3,
            MaterialIndex:       _cmbMaterial.SelectedIndex,
            DepthRating_m:       (double)_nudDepthRating.Value,
            HullFamily:          family);

        _onGenerate(cond, design);
    }

    internal void SetStatus(string msg)
    {
        if (IsDisposed) return;
        _lblStatus.Text = msg;
    }

    internal void UpdateResults(MarineResult result, MarineHullGeometryResult? geo = null)
    {
        if (IsDisposed) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(string.Format(ci, "Drag force:          {0:F2} N", result.DragForce_N));
        sb.AppendLine(string.Format(ci, "Drag coefficient:    {0:F4}", result.DragCoefficient));
        sb.AppendLine(string.Format(ci, "Buoyancy force:      {0:F1} N", result.BuoyancyForce_N));
        sb.AppendLine(string.Format(ci, "Displaced volume:    {0:F4} m³", result.DisplacedVolume_m3));
        sb.AppendLine(string.Format(ci, "Buoyant weight:      {0:F1} N", result.BuoyantWeight_N));
        sb.AppendLine(string.Format(ci, "Hull mass:           {0:F2} kg", result.HullMass_kg));
        sb.AppendLine(string.Format(ci, "Buckling SF:         {0:F2}", result.BucklingSafetyFactor));
        sb.AppendLine(string.Format(ci, "P_critical:          {0:F0} Pa", result.CriticalBucklingPressure_Pa));
        sb.AppendLine(string.Format(ci, "CG-CB offset:        {0:F4} m", result.CgCbOffset_m));

        // Voxel preview metadata (when present).
        if (geo is not null)
        {
            sb.AppendLine();
            sb.AppendLine(string.Format(ci, "Hull length:         {0:F1} mm", geo.HullLength_mm));
            sb.AppendLine(string.Format(ci, "Hull diameter:       {0:F1} mm", geo.HullDiameter_mm));
            sb.AppendLine(string.Format(ci, "Shell volume:        {0:F0} mm³", geo.ShellVolume_mm3));
            sb.AppendLine(string.Format(ci, "Estimated mass:      {0:F1} g", geo.EstimatedMass_g));
            sb.AppendLine(string.Format(ci, "Voxel size:          {0:F3} mm", geo.VoxelSize_mm));
        }
        else if (!result.IsFeasible)
        {
            sb.AppendLine();
            sb.AppendLine("(no voxel preview — design is infeasible)");
        }

        if (!result.IsFeasible)
        {
            sb.AppendLine();
            sb.AppendLine("⚠ INFEASIBLE:");
            foreach (var v in result.Violations)
                sb.AppendLine($"  • {v.ConstraintId}: {v.Description}");
        }
        else if (result.Advisories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ℹ Advisories:");
            foreach (var a in result.Advisories)
                sb.AppendLine($"  • {a.ConstraintId}: {a.Description}");
        }

        _txtResults.Text = sb.ToString();
        SetStatus(result.IsFeasible
            ? $"Feasible ✓  SF {result.BucklingSafetyFactor:F2},  Drag {result.DragForce_N:F1} N"
            : $"Infeasible — {result.Violations.Count} gate(s) failed");
    }
}
