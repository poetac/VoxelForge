// FeepCycleSolver.cs — top-level Field-Emission Electric Propulsion
// cycle solver.
//
// Mirror of MpdCycleSolver / GitCycleSolver / PptCycleSolver /
// ArcjetCycleSolver / HetCycleSolver for the FEEP variant. Wraps
// MairLozanoEmitterModel with the design/conditions API so
// ElectricPropulsionOptimization.GenerateWith can dispatch to the FEEP
// pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated FEEP cycle result. Carries both the raw Mair-Lozano
/// emitter outputs and the <see cref="FeepPlasmaState"/> snapshot
/// consumed by gates and reporting.
/// </summary>
/// <param name="Emitter">Raw Mair-Lozano emitter-model output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record (mirrors the model output).</param>
public sealed record FeepCycleResult(
    MairLozanoEmitterResult Emitter,
    FeepPlasmaState         PlasmaState);

/// <summary>
/// FEEP cycle solver. Validates design inputs, calls
/// <see cref="MairLozanoEmitterModel.Solve"/>, and packages the result
/// with a typed <see cref="FeepPlasmaState"/>.
/// </summary>
public static class FeepCycleSolver
{
    /// <summary>
    /// Solve the FEEP cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.Feep"/> and the 4
    /// required FEEP fields populated (NaN sentinels indicate a non-FEEP
    /// design). FEEP does NOT consume any of the resistojet-shape
    /// continuous-flow fields (HeaterPower_W / PropellantMassFlow_kgs /
    /// NozzleThroatRadius_mm / NozzleAreaRatio / HeaterChamberLength_mm /
    /// HeaterChamberRadius_mm) — the emitter draws liquid metal directly
    /// from the propellant reservoir and there is no expansion nozzle.
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (V_acc × I_beam rejected at SA bind time
    /// when above bus power).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not Feep, when any
    /// required FEEP field is NaN, or when
    /// <see cref="ElectricPropulsionEngineDesign.FeepPropellantMaterial"/>
    /// is <see cref="FeepPropellant.None"/>.
    /// </exception>
    public static FeepCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.Feep)
            throw new ArgumentException(
                $"FeepCycleSolver.Solve called with Kind={design.Kind}; expected Feep.",
                nameof(design));

        // NaN-trap: every FEEP field must be populated.
        if (double.IsNaN(design.FeepAcceleratingVoltage_V) ||
            double.IsNaN(design.FeepBeamCurrent_A) ||
            double.IsNaN(design.FeepEmitterTipRadius_mm))
        {
            throw new ArgumentException(
                "FEEP design has NaN required field(s); populate "
              + "FeepAcceleratingVoltage_V, FeepBeamCurrent_A, "
              + "FeepEmitterTipRadius_mm.",
                nameof(design));
        }

        if (design.FeepPropellantMaterial == FeepPropellant.None)
        {
            throw new ArgumentException(
                "FEEP design has FeepPropellantMaterial=None; set it to "
              + "Indium or Cesium to drive the emitter-model branch.",
                nameof(design));
        }

        var emitter = MairLozanoEmitterModel.Solve(
            acceleratingVoltage_V: design.FeepAcceleratingVoltage_V,
            beamCurrent_A:         design.FeepBeamCurrent_A,
            emitterTipRadius_mm:   design.FeepEmitterTipRadius_mm,
            propellant:            design.FeepPropellantMaterial);

        var plasmaState = new FeepPlasmaState(
            IonExitVelocity_ms:           emitter.ExitVelocity_ms,
            BeamCurrent_A:                emitter.BeamCurrent_A,
            PlumeDivergenceHalfAngle_rad: emitter.PlumeDivergence_rad,
            AcceleratingVoltage_V:        design.FeepAcceleratingVoltage_V,
            EmitterTipField_VperM:        emitter.EmitterTipField_VperM,
            EffectiveIonMass_kg:          emitter.EffectiveIonMass_kg,
            PropellantMaterial:           design.FeepPropellantMaterial);

        return new FeepCycleResult(emitter, plasmaState);
    }
}
