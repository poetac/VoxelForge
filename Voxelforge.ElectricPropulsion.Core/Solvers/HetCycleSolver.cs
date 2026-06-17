// HetCycleSolver.cs — top-level Hall-Effect Thruster cycle solver.
//
// Mirror of ElectrothermalHeaterSolver.Solve for the HET variant.
// Wraps BuschDischargeModel with the design/conditions API so
// ElectricPropulsionOptimization.GenerateWith can dispatch to either
// the resistojet or the HET pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated HET cycle result. Carries both the raw discharge-model
/// outputs and the <see cref="HetPlasmaState"/> snapshot consumed by
/// gates and reporting.
/// </summary>
/// <param name="Discharge">Raw Busch-discharge-model output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record (mirrors discharge).</param>
public sealed record HetCycleResult(
    BuschDischargeResult Discharge,
    HetPlasmaState       PlasmaState);

/// <summary>
/// Hall-Effect Thruster cycle solver. Validates design inputs, calls
/// <see cref="BuschDischargeModel.Solve"/>, and packages the result with
/// a typed <see cref="HetPlasmaState"/>.
/// </summary>
public static class HetCycleSolver
{
    /// <summary>
    /// Solve the HET cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.HallEffect"/> and all
    /// 8 HET fields populated (NaN sentinels indicate a non-HET design).
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3; only <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is consumed by the solver (for sanity checks).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not HallEffect or when
    /// any required HET-specific field is NaN (the design is categorically
    /// malformed for this solver).
    /// </exception>
    public static HetCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.HallEffect)
            throw new ArgumentException(
                $"HetCycleSolver.Solve called with Kind={design.Kind}; expected HallEffect.",
                nameof(design));

        // NaN-trap: every HET field must be populated.
        if (double.IsNaN(design.DischargeVoltage_V) ||
            double.IsNaN(design.DischargeCurrent_A) ||
            double.IsNaN(design.MagneticField_T) ||
            double.IsNaN(design.AnodeRadius_mm) ||
            double.IsNaN(design.ChannelLength_mm) ||
            double.IsNaN(design.XenonMassFlow_kgs))
        {
            throw new ArgumentException(
                "HET design has NaN HET-specific field(s); populate DischargeVoltage_V, " +
                "DischargeCurrent_A, MagneticField_T, AnodeRadius_mm, ChannelLength_mm, XenonMassFlow_kgs.",
                nameof(design));
        }

        var discharge = BuschDischargeModel.Solve(
            dischargeVoltage_V:  design.DischargeVoltage_V,
            dischargeCurrent_A:  design.DischargeCurrent_A,
            magneticField_T:     design.MagneticField_T,
            anodeRadius_mm:      design.AnodeRadius_mm,
            channelLength_mm:    design.ChannelLength_mm,
            xenonMassFlow_kgs:   design.XenonMassFlow_kgs);

        var plasmaState = new HetPlasmaState(
            IonExitVelocity_ms:           discharge.IonExitVelocity_ms,
            BeamCurrent_A:                discharge.BeamCurrent_A,
            PlumeDivergenceHalfAngle_rad: discharge.PlumeDivergenceHalfAngle_rad,
            MagneticField_T:              design.MagneticField_T,
            MassUtilization:              discharge.MassUtilization,
            BeamEfficiency:               BuschDischargeModel.BeamEfficiency,
            DischargePower_W:             discharge.DischargePower_W);

        return new HetCycleResult(discharge, plasmaState);
    }
}
