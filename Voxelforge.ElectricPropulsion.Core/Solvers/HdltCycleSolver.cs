// HdltCycleSolver.cs — top-level Helicon Double-Layer Thruster cycle
// solver. Wraps HeliconDoubleLayerModel with the design/conditions API
// so ElectricPropulsionOptimization.GenerateWith can dispatch to the
// HDLT pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated HDLT cycle result. Carries both the raw Helicon-DL model
/// outputs and the <see cref="HdltPlasmaState"/> snapshot consumed by
/// gates and reporting.
/// </summary>
/// <param name="Helicon">Raw HeliconDoubleLayerModel output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record.</param>
public sealed record HdltCycleResult(
    HeliconDoubleLayerResult Helicon,
    HdltPlasmaState          PlasmaState);

/// <summary>
/// HDLT cycle solver. Validates design inputs, calls
/// <see cref="HeliconDoubleLayerModel.Solve"/>, and packages the result
/// with a typed <see cref="HdltPlasmaState"/>.
/// </summary>
public static class HdltCycleSolver
{
    /// <summary>
    /// Solve the HDLT cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.Hdlt"/> and the 4
    /// required HDLT fields populated.
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (P_rf rejected at SA bind time when above
    /// bus power).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not Hdlt or when
    /// any required HDLT field is NaN.
    /// </exception>
    public static HdltCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.Hdlt)
            throw new ArgumentException(
                $"HdltCycleSolver.Solve called with Kind={design.Kind}; expected Hdlt.",
                nameof(design));

        if (double.IsNaN(design.HdltHeliconRfPower_W) ||
            double.IsNaN(design.HdltMagneticFieldGradient_TpM) ||
            double.IsNaN(design.HdltChannelLength_mm) ||
            double.IsNaN(design.HdltArgonMassFlow_kgs))
        {
            throw new ArgumentException(
                "HDLT design has NaN required field(s); populate "
              + "HdltHeliconRfPower_W, HdltMagneticFieldGradient_TpM, "
              + "HdltChannelLength_mm, HdltArgonMassFlow_kgs.",
                nameof(design));
        }

        var helicon = HeliconDoubleLayerModel.Solve(
            heliconRfPower_W:          design.HdltHeliconRfPower_W,
            magneticFieldGradient_TpM: design.HdltMagneticFieldGradient_TpM,
            channelLength_mm:          design.HdltChannelLength_mm,
            argonMassFlow_kgs:         design.HdltArgonMassFlow_kgs);

        var plasmaState = new HdltPlasmaState(
            IonExitVelocity_ms:           helicon.ExitVelocity_ms,
            BeamCurrent_A:                helicon.BeamCurrent_A,
            PlumeDivergenceHalfAngle_rad: helicon.PlumeDivergence_rad,
            DoubleLayerStrength_V:        helicon.DoubleLayerStrength_V,
            ElectronTemperature_eV:       helicon.ElectronTemperature_eV,
            IonisationFraction:           helicon.IonisationFraction);

        return new HdltCycleResult(helicon, plasmaState);
    }
}
