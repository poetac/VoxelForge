// VasimrCycleSolver.cs — top-level VASIMR cycle solver. Wraps
// HeliconIcrhMagneticNozzleModel with the design/conditions API so
// ElectricPropulsionOptimization.GenerateWith can dispatch to the
// VASIMR pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated VASIMR cycle result. Carries both the raw 3-stage model
/// outputs and the <see cref="VasimrPlasmaState"/> snapshot consumed
/// by gates and reporting.
/// </summary>
/// <param name="Helicon">Raw HeliconIcrhMagneticNozzleModel output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record.</param>
public sealed record VasimrCycleResult(
    HeliconIcrhMagneticNozzleResult Helicon,
    VasimrPlasmaState               PlasmaState);

/// <summary>
/// VASIMR cycle solver. Validates design inputs, calls
/// <see cref="HeliconIcrhMagneticNozzleModel.Solve"/>, and packages
/// the result with a typed <see cref="VasimrPlasmaState"/>.
/// </summary>
public static class VasimrCycleSolver
{
    /// <summary>
    /// Solve the VASIMR cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.Vasimr"/> and the 5
    /// required VASIMR fields populated.
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (P_helicon + P_icrh rejected at SA bind
    /// time when above bus power).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not Vasimr or when
    /// any required VASIMR field is NaN.
    /// </exception>
    public static VasimrCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.Vasimr)
            throw new ArgumentException(
                $"VasimrCycleSolver.Solve called with Kind={design.Kind}; expected Vasimr.",
                nameof(design));

        if (double.IsNaN(design.VasimrHeliconRfPower_W) ||
            double.IsNaN(design.VasimrIcrhRfPower_W) ||
            double.IsNaN(design.VasimrSolenoidField_T) ||
            double.IsNaN(design.VasimrNozzleExitRadius_mm) ||
            double.IsNaN(design.VasimrArgonMassFlow_kgs))
        {
            throw new ArgumentException(
                "VASIMR design has NaN required field(s); populate "
              + "VasimrHeliconRfPower_W, VasimrIcrhRfPower_W, "
              + "VasimrSolenoidField_T, VasimrNozzleExitRadius_mm, "
              + "VasimrArgonMassFlow_kgs.",
                nameof(design));
        }

        var helicon = HeliconIcrhMagneticNozzleModel.Solve(
            heliconRfPower_W:    design.VasimrHeliconRfPower_W,
            icrhRfPower_W:       design.VasimrIcrhRfPower_W,
            solenoidField_T:     design.VasimrSolenoidField_T,
            nozzleExitRadius_mm: design.VasimrNozzleExitRadius_mm,
            argonMassFlow_kgs:   design.VasimrArgonMassFlow_kgs);

        var plasmaState = new VasimrPlasmaState(
            IonExitVelocity_ms:           helicon.ExitVelocity_ms,
            BeamCurrent_A:                helicon.BeamCurrent_A,
            PlumeDivergenceHalfAngle_rad: helicon.PlumeDivergence_rad,
            IonTemperature_eV:            helicon.IonTemperature_eV,
            MagneticMirrorRatio:          helicon.MagneticMirrorRatio,
            IonisationFraction:           helicon.IonisationFraction,
            NozzleConversionEfficiency:   helicon.NozzleConversionEfficiency);

        return new VasimrCycleResult(helicon, plasmaState);
    }
}
