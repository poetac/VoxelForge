// PptCycleSolver.cs — top-level PPT cycle solver.
//
// Mirror of ArcjetCycleSolver / HetCycleSolver for the PPT variant. Wraps
// AblationDischargeModel with the design/conditions API so
// ElectricPropulsionOptimization.GenerateWith can dispatch to the resistojet,
// HET, arcjet, or PPT pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated PPT cycle result. Carries both the raw Solbes-Vondra
/// ablation-discharge-model outputs and the <see cref="PptPlasmaState"/>
/// snapshot consumed by gates and reporting.
/// </summary>
/// <param name="Ablation">Raw Solbes-Vondra ablation-discharge model output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record (mirrors the model output).</param>
public sealed record PptCycleResult(
    AblationDischargeResult Ablation,
    PptPlasmaState          PlasmaState);

/// <summary>
/// PPT cycle solver. Validates design inputs, calls
/// <see cref="AblationDischargeModel.Solve"/>, and packages the result with
/// a typed <see cref="PptPlasmaState"/>.
/// </summary>
public static class PptCycleSolver
{
    /// <summary>
    /// Solve the PPT cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.PulsedPlasmaThruster"/>
    /// and the 5 PPT-specific fields populated (NaN sentinels indicate a
    /// non-PPT design). PPT does not consume any of the resistojet-shape
    /// continuous-flow fields (HeaterPower_W / PropellantMassFlow_kgs /
    /// NozzleThroatRadius_mm / NozzleAreaRatio / HeaterChamberLength_mm /
    /// HeaterChamberRadius_mm) — the discharge is per-pulse and there is
    /// no nozzle.
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (E_cap × f_pulse rejected at SA bind time
    /// when above bus power).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not PulsedPlasmaThruster
    /// or when any required PPT-specific field is NaN (categorically malformed).
    /// </exception>
    public static PptCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.PulsedPlasmaThruster)
            throw new ArgumentException(
                $"PptCycleSolver.Solve called with Kind={design.Kind}; expected PulsedPlasmaThruster.",
                nameof(design));

        // NaN-trap: every PPT field except the optional Isp calibration must
        // be populated.
        if (double.IsNaN(design.CapacitorEnergy_J) ||
            double.IsNaN(design.PulseFrequency_Hz) ||
            double.IsNaN(design.PptElectrodeGap_mm) ||
            double.IsNaN(design.PptPropellantBarLength_mm) ||
            double.IsNaN(design.PptElectrodeWidth_mm))
        {
            throw new ArgumentException(
                "PPT design has NaN required field(s); populate CapacitorEnergy_J, " +
                "PulseFrequency_Hz, PptElectrodeGap_mm, PptPropellantBarLength_mm, " +
                "PptElectrodeWidth_mm. PptIspCalibration may stay NaN (cluster anchor).",
                nameof(design));
        }

        // PptIspCalibration: NaN means "use the cluster anchor"; AblationDischargeModel
        // branches internally on the NaN sentinel.
        var ablation = AblationDischargeModel.Solve(
            capacitorEnergy_J:      design.CapacitorEnergy_J,
            pulseFrequency_Hz:      design.PulseFrequency_Hz,
            electrodeGap_mm:        design.PptElectrodeGap_mm,
            propellantBarLength_mm: design.PptPropellantBarLength_mm,
            electrodeWidth_mm:      design.PptElectrodeWidth_mm,
            ispOverride_s:          design.PptIspCalibration);

        var plasmaState = new PptPlasmaState(
            IonExitVelocity_ms:           ablation.ExitVelocity_ms,
            BeamCurrent_A:                0.0,                                  // PPT has no continuous current path
            PlumeDivergenceHalfAngle_rad: ablation.PlumeDivergenceHalfAngle_rad,
            ImpulseBit_Ns:                ablation.ImpulseBit_Ns,
            MassPerPulse_kg:              ablation.MassPerPulse_kg,
            PulseFrequency_Hz:            design.PulseFrequency_Hz,
            CapacitorEnergy_J:            design.CapacitorEnergy_J,
            AveragePower_W:               ablation.AveragePower_W);

        return new PptCycleResult(ablation, plasmaState);
    }
}
