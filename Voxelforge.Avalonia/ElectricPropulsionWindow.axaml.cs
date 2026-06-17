// ElectricPropulsionWindow.axaml.cs — Avalonia code-behind for the
// electric-propulsion viewer (Phase 1, ADR-027).
//
// Functional parity with Voxelforge/ElectricPropulsionForm.cs:
//   • Same controls, same defaults, same PostGenerate() logic.
//   • SetStatus / UpdateResults called from any thread via
//     Dispatcher.UIThread.Post (replaces Control.BeginInvoke).
//   • String building runs on the task thread (outside the Post lambda)
//     so only the property assignment is marshalled to the UI thread.
//   • No PicoGK references — voxel ops stay on the task thread in
//     Program.ElectricPropulsion.cs.

using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Voxelforge.ElectricPropulsion;

namespace Voxelforge.Avalonia;

public sealed partial class ElectricPropulsionWindow : Window
{
    private readonly Action<ResistojetConditions, ElectricPropulsionEngineDesign> _onGenerate;

    public ElectricPropulsionWindow(
        Action<ResistojetConditions, ElectricPropulsionEngineDesign> onGenerate)
    {
        _onGenerate = onGenerate ?? throw new ArgumentNullException(nameof(onGenerate));
        InitializeComponent();

        // Populate ComboBoxes — AXAML cannot bind to enums without MVVM at Phase 1.
        _cmbKind.Items.Add("Resistojet");
        _cmbKind.SelectedIndex = 0;

        _cmbPropellant.Items.Add("NH3 (ammonia)");
        _cmbPropellant.Items.Add("N2H4 decomposed (Shell-405)");
        _cmbPropellant.Items.Add("H2 (gaseous hydrogen)");
        _cmbPropellant.Items.Add("H2O (water vapor)");
        _cmbPropellant.SelectedIndex = 1;   // N2H4Decomposed (MR-501B reference)

        _btnGenerate.Click += (_, _) => PostGenerate();
        Opened             += (_, _) => PostGenerate();   // mirrors ElectricPropulsionForm.Load
    }

    // ── Generate ──────────────────────────────────────────────────────────

    private void PostGenerate()
    {
        SetStatus("Solving…");
        var prop = SelectedPropellant();
        var cond = new ResistojetConditions(
            BusVoltage_V:       (double)(_nudBusVoltage.Value        ?? 28m),
            BusPower_W_avail:   (double)(_nudBusPower.Value          ?? 500m),
            AmbientPressure_Pa: 0.0,
            Propellant:         prop,
            InletTemperature_K: (double)(_nudInletTempK.Value        ?? 900m),
            InletComposition:   CompositionFor(prop));

        var design = new ElectricPropulsionEngineDesign(
            Kind:                   ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:          (double)(_nudHeaterPowerW.Value          ?? 500m),
            PropellantMassFlow_kgs: (double)(_nudPropellantMassFlow.Value    ?? 0.001m),
            NozzleThroatRadius_mm:  (double)(_nudThroatRadiusMm.Value        ?? 0.40m),
            NozzleAreaRatio:        (double)(_nudAreaRatio.Value             ?? 50m),
            HeaterChamberLength_mm: (double)(_nudHeaterChamberLenMm.Value    ?? 20m),
            HeaterChamberRadius_mm: (double)(_nudHeaterChamberRadMm.Value    ?? 8m));

        _onGenerate(cond, design);
    }

    // ── Propellant helpers ─────────────────────────────────────────────────

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

    // ── Cross-thread update API (safe to call from task thread) ───────────
    //
    // Dispatcher.UIThread.Post is Avalonia's equivalent of BeginInvoke:
    // fire-and-forget, returns immediately, executes on the Avalonia thread.

    public void SetStatus(string msg)
    {
        var self = this;
        Dispatcher.UIThread.Post(() =>
        {
            if (self.IsLoaded) self._lblStatus.Text = msg;
        });
    }

    public void UpdateResults(ElectricPropulsionResult result, ResistojetGeometryResult? geo = null)
    {
        // Build the string on the task thread — only assignment goes on the UI thread.
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine(string.Format(ci, "Thrust:            {0:F4} N",  result.Thrust_N));
        sb.AppendLine(string.Format(ci, "Isp (vacuum):      {0:F1} s",  result.IspVacuum_s));
        sb.AppendLine(string.Format(ci, "Exit velocity:     {0:F1} m/s", result.ExitVelocity_ms));
        sb.AppendLine(string.Format(ci, "Thrust efficiency: {0:F3}",    result.ThrustEfficiency));
        sb.AppendLine(string.Format(ci, "Heater T:          {0:F0} K",  result.HeaterTemp_K));
        sb.AppendLine(string.Format(ci, "Chamber T:         {0:F0} K",  result.ChamberTemp_K));
        sb.AppendLine(string.Format(ci, "Exit Mach:         {0:F2}",    result.ExitMachNumber));
        sb.AppendLine(string.Format(ci, "Exit P:            {0:F1} Pa", result.ExitPressure_Pa));
        sb.AppendLine(string.Format(ci, "Radiation loss:    {0:F1} %",  100.0 * result.RadiationLossFraction));
        sb.AppendLine(string.Format(ci, "Choked flow:       {0}",       result.ChokedFlow ? "yes" : "NO"));

        if (geo is not null)
        {
            sb.AppendLine();
            sb.AppendLine(string.Format(ci, "Length:  {0:F1} mm", geo.BoundingLength_mm));
            sb.AppendLine(string.Format(ci, "OD:      {0:F1} mm", geo.BoundingDiameter_mm));
            sb.AppendLine(string.Format(ci, "Wall:    {0:F2} mm", geo.WallThickness_mm));
            sb.AppendLine(string.Format(ci, "Mass:    {0:F1} g",  geo.TotalMass_g));
            sb.AppendLine(string.Format(ci, "e:       {0:F1}",    geo.AreaRatio));
        }
        else if (!result.IsFeasible)
        {
            sb.AppendLine();
            sb.AppendLine("(no voxel preview — infeasible)");
        }

        if (!result.IsFeasible)
        {
            sb.AppendLine();
            sb.AppendLine("INFEASIBLE:");
            foreach (var v in result.Violations)
                sb.AppendLine($"  - {v.ConstraintId}: {v.Description}");
        }
        else if (result.Advisories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Advisories:");
            foreach (var a in result.Advisories)
                sb.AppendLine($"  - {a.ConstraintId}: {a.Description}");
        }

        var text   = sb.ToString();
        var status = result.IsFeasible
            ? $"Feasible   Thrust {result.Thrust_N:F4} N,  Isp {result.IspVacuum_s:F0} s"
            : $"Infeasible — {result.Violations.Count} gate(s) failed";

        var self = this;
        Dispatcher.UIThread.Post(() =>
        {
            if (!self.IsLoaded) return;
            self._txtResults.Text = text;
            self._lblStatus.Text  = status;
        });
    }
}
