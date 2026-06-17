// GitCycleSolver.cs — top-level Gridded-Ion-Thruster cycle solver.
//
// Mirror of PptCycleSolver / ArcjetCycleSolver / HetCycleSolver for the
// GIT variant. Wraps ChildLangmuirBeamModel with the design/conditions API
// so ElectricPropulsionOptimization.GenerateWith can dispatch to the
// resistojet, HET, arcjet, PPT, or GIT pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated GIT cycle result. Carries both the raw Child-Langmuir model
/// outputs and the <see cref="IonPlasmaState"/> snapshot consumed by gates
/// and reporting.
/// </summary>
/// <param name="Beam">Raw Child-Langmuir beam-extraction model output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record (mirrors the model output).</param>
public sealed record GitCycleResult(
    ChildLangmuirBeamResult Beam,
    IonPlasmaState          PlasmaState);

/// <summary>
/// GIT cycle solver. Validates design inputs, calls
/// <see cref="ChildLangmuirBeamModel.Solve"/>, and packages the result with
/// a typed <see cref="IonPlasmaState"/>.
/// </summary>
public static class GitCycleSolver
{
    /// <summary>
    /// Solve the GIT cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.GriddedIon"/> and the 5
    /// required GIT fields populated (NaN sentinels indicate a non-GIT design).
    /// GIT does not consume any of the resistojet-shape continuous-flow fields
    /// (HeaterPower_W / PropellantMassFlow_kgs / NozzleThroatRadius_mm /
    /// NozzleAreaRatio / HeaterChamberLength_mm / HeaterChamberRadius_mm) —
    /// the discharge chamber feeds an electrostatic-acceleration grid set and
    /// there is no expansion nozzle.
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (V_b × J_b rejected at SA bind time when
    /// above bus power).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not GriddedIon or when
    /// any required GIT-specific field is NaN (categorically malformed).
    /// </exception>
    public static GitCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.GriddedIon)
            throw new ArgumentException(
                $"GitCycleSolver.Solve called with Kind={design.Kind}; expected GriddedIon.",
                nameof(design));

        // NaN-trap: every GIT field except the optional mass-utilisation
        // override must be populated.
        if (double.IsNaN(design.BeamVoltage_V) ||
            double.IsNaN(design.BeamCurrent_A) ||
            double.IsNaN(design.ScreenGridRadius_mm) ||
            double.IsNaN(design.AccelGridGap_mm) ||
            double.IsNaN(design.NeutralizerCathodeCurrent_A))
        {
            throw new ArgumentException(
                "GIT design has NaN required field(s); populate BeamVoltage_V, " +
                "BeamCurrent_A, ScreenGridRadius_mm, AccelGridGap_mm, " +
                "NeutralizerCathodeCurrent_A. GitMassUtilizationOverride may stay NaN " +
                "(cluster anchor at η_m = 0.90).",
                nameof(design));
        }

        var beam = ChildLangmuirBeamModel.Solve(
            beamVoltage_V:           design.BeamVoltage_V,
            beamCurrentRequested_A:  design.BeamCurrent_A,
            screenGridRadius_mm:     design.ScreenGridRadius_mm,
            accelGridGap_mm:         design.AccelGridGap_mm,
            neutralizerCurrent_A:    design.NeutralizerCathodeCurrent_A,
            massUtilizationOverride: design.GitMassUtilizationOverride);

        var plasmaState = new IonPlasmaState(
            IonExitVelocity_ms:           beam.IonExitVelocity_ms,
            BeamCurrent_A:                beam.BeamCurrent_A,
            PlumeDivergenceHalfAngle_rad: beam.PlumeDivergenceHalfAngle_rad,
            AcceleratingVoltage_V:        design.BeamVoltage_V,
            Perveance_AOverV1p5:          beam.Perveance_AOverV1p5,
            NeutralizerCurrent_A:         design.NeutralizerCathodeCurrent_A,
            ChildLangmuirLimit_A:         beam.ChildLangmuirLimit_A);

        return new GitCycleResult(beam, plasmaState);
    }
}
