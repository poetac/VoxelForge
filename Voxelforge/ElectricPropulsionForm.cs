// ElectricPropulsionForm.cs — WinForms UI for the electric-propulsion pillar.
//
// Wave-1 surfaces the resistojet (the only kind shipped). The kind ComboBox
// is future-proofed for HET / arcjet / ion / MPD when Wave-2 lands. Issue #441.

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Voxelforge.ElectricPropulsion;
using Voxelforge.ElectricPropulsion.Geometry;

namespace Voxelforge.UI;

internal sealed class ElectricPropulsionForm : Form
{
    private readonly ComboBox      _cmbKind         = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox      _cmbPropellant   = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };

    private readonly NumericUpDown _nudBusVoltage   = MakeNud(12m,   200m,    28m,   0);
    private readonly NumericUpDown _nudBusPower     = MakeNud(50m,   10_000m, 500m,  0);
    private readonly NumericUpDown _nudInletTempK   = MakeNud(280m,  1500m,   900m,  0);

    private readonly NumericUpDown _nudHeaterPowerW       = MakeNud(50m,    3000m, 500m,   0);
    private readonly NumericUpDown _nudPropellantMassFlow = MakeNud(0.0001m, 0.05m, 0.0010m, 4);
    private readonly NumericUpDown _nudThroatRadiusMm     = MakeNud(0.05m,  3.0m,  0.40m,  2);
    private readonly NumericUpDown _nudAreaRatio          = MakeNud(2m,     200m,  50m,    1);
    private readonly NumericUpDown _nudHeaterChamberLenMm = MakeNud(5m,     100m,  20m,    1);
    private readonly NumericUpDown _nudHeaterChamberRadMm = MakeNud(2m,     50m,   8m,     1);

    private readonly Button  _btnGenerate = new() { Text = "Generate", Height = 36 };
    private readonly TextBox _txtResults  = new() { Multiline = true, ReadOnly = true, Height = 220, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    private readonly Label   _lblStatus   = new() { Text = "Ready.", AutoSize = true };

    private readonly Action<ResistojetConditions, ElectricPropulsionEngineDesign> _onGenerate;

    internal ElectricPropulsionForm(
        Action<ResistojetConditions, ElectricPropulsionEngineDesign> onGenerate)
    {
        _onGenerate = onGenerate ?? throw new ArgumentNullException(nameof(onGenerate));

        // Wave-1: only Resistojet shipped. HET / Arcjet / GriddedIon / MPD slots
        // appear in the ComboBox as disabled-looking entries when Wave-2 lands.
        _cmbKind.Items.Add("Resistojet");
        _cmbKind.SelectedIndex = 0;

        _cmbPropellant.Items.AddRange(new object[]
        {
            "NH3 (ammonia)",
            "N2H4 decomposed (Shell-405)",
            "H2 (gaseous hydrogen)",
            "H2O (water vapor)",
        });
        _cmbPropellant.SelectedIndex = 1; // N2H4Decomposed (MR-501B reference)

        Text            = "Voxelforge — Electric-Propulsion Viewer  [resistojet]";
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
        layout.Controls.Add(BuildLabeledGroup("Engine kind", ("Kind:", _cmbKind)));
        layout.Controls.Add(BuildLabeledGroup("Operating conditions",
            ("Bus voltage (V):", _nudBusVoltage),
            ("Bus power available (W):", _nudBusPower),
            ("Propellant:", _cmbPropellant),
            ("Inlet T (K):", _nudInletTempK)));
        layout.Controls.Add(BuildLabeledGroup("Resistojet design",
            ("Heater power (W):", _nudHeaterPowerW),
            ("Mass flow (kg/s):", _nudPropellantMassFlow),
            ("Throat radius (mm):", _nudThroatRadiusMm),
            ("Area ratio ε:", _nudAreaRatio),
            ("Chamber length (mm):", _nudHeaterChamberLenMm),
            ("Chamber radius (mm):", _nudHeaterChamberRadMm)));
        layout.Controls.Add(_btnGenerate);
        layout.Controls.Add(_txtResults);
        layout.Controls.Add(_lblStatus);

        Controls.Add(layout);

        _btnGenerate.Dock     = DockStyle.Fill;
        _txtResults.Dock      = DockStyle.Fill;
        _txtResults.BackColor = SystemColors.Window;

        _btnGenerate.Click += (_, _) => PostGenerate();
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

    private Propellant SelectedPropellant() => _cmbPropellant.SelectedIndex switch
    {
        0 => Propellant.NH3,
        1 => Propellant.N2H4Decomposed,
        2 => Propellant.H2,
        3 => Propellant.H2O,
        _ => Propellant.N2H4Decomposed,
    };

    private static PropellantInletComposition CompositionFor(Propellant p) => p switch
    {
        Propellant.NH3            => PropellantInletComposition.PureNH3,
        Propellant.N2H4Decomposed => PropellantInletComposition.Hydrazine_Shell405,
        Propellant.H2             => PropellantInletComposition.PureH2,
        Propellant.H2O            => PropellantInletComposition.PureH2O,
        _                         => PropellantInletComposition.Hydrazine_Shell405,
    };

    private void PostGenerate()
    {
        SetStatus("Solving…");
        var prop = SelectedPropellant();
        var cond = new ResistojetConditions(
            BusVoltage_V:        (double)_nudBusVoltage.Value,
            BusPower_W_avail:    (double)_nudBusPower.Value,
            AmbientPressure_Pa:  0.0,
            Propellant:          prop,
            InletTemperature_K:  (double)_nudInletTempK.Value,
            InletComposition:    CompositionFor(prop));

        var design = new ElectricPropulsionEngineDesign(
            Kind:                   ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:          (double)_nudHeaterPowerW.Value,
            PropellantMassFlow_kgs: (double)_nudPropellantMassFlow.Value,
            NozzleThroatRadius_mm:  (double)_nudThroatRadiusMm.Value,
            NozzleAreaRatio:        (double)_nudAreaRatio.Value,
            HeaterChamberLength_mm: (double)_nudHeaterChamberLenMm.Value,
            HeaterChamberRadius_mm: (double)_nudHeaterChamberRadMm.Value);

        _onGenerate(cond, design);
    }

    internal void SetStatus(string msg)
    {
        if (IsDisposed) return;
        _lblStatus.Text = msg;
    }

    internal void UpdateResults(ElectricPropulsionResult result, ResistojetGeometryResult? geo = null)
    {
        if (IsDisposed) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(string.Format(ci, "Thrust:           {0:F4} N", result.Thrust_N));
        sb.AppendLine(string.Format(ci, "Isp (vacuum):     {0:F1} s", result.IspVacuum_s));
        sb.AppendLine(string.Format(ci, "Exit velocity:    {0:F1} m/s", result.ExitVelocity_ms));
        sb.AppendLine(string.Format(ci, "Thrust efficiency: {0:F3}", result.ThrustEfficiency));
        sb.AppendLine(string.Format(ci, "Heater T:         {0:F0} K", result.HeaterTemp_K));
        sb.AppendLine(string.Format(ci, "Chamber T:        {0:F0} K", result.ChamberTemp_K));
        sb.AppendLine(string.Format(ci, "Exit Mach:        {0:F2}", result.ExitMachNumber));
        sb.AppendLine(string.Format(ci, "Exit P:           {0:F1} Pa", result.ExitPressure_Pa));
        sb.AppendLine(string.Format(ci, "Radiation loss:   {0:F1} %", 100.0 * result.RadiationLossFraction));
        sb.AppendLine(string.Format(ci, "Choked flow:      {0}", result.ChokedFlow ? "yes" : "NO"));

        // Voxel preview metadata (when present).
        if (geo is not null)
        {
            sb.AppendLine();
            sb.AppendLine(string.Format(ci, "Length:           {0:F1} mm", geo.BoundingLength_mm));
            sb.AppendLine(string.Format(ci, "OD:               {0:F1} mm", geo.BoundingDiameter_mm));
            sb.AppendLine(string.Format(ci, "Wall:             {0:F2} mm", geo.WallThickness_mm));
            sb.AppendLine(string.Format(ci, "Mass est.:        {0:F1} g", geo.TotalMass_g));
            sb.AppendLine(string.Format(ci, "ε (nozzle):       {0:F1}", geo.AreaRatio));
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
            ? $"Feasible ✓  Thrust {result.Thrust_N:F4} N,  Isp {result.IspVacuum_s:F0} s"
            : $"Infeasible — {result.Violations.Count} gate(s) failed");
    }
}
