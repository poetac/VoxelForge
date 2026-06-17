// MpdCycleSolver.cs — top-level Magnetoplasmadynamic-thruster cycle solver.
//
// Mirror of GitCycleSolver / PptCycleSolver / ArcjetCycleSolver / HetCycleSolver
// for the MPD variant. Wraps SelfFieldLorentzModel with the design/conditions
// API so ElectricPropulsionOptimization.GenerateWith can dispatch to the
// resistojet, HET, arcjet, PPT, GIT, or MPD pipeline via design.Kind.

using System;
using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Aggregated MPD cycle result. Carries both the raw Lorentz model outputs
/// and the <see cref="MpdPlasmaState"/> snapshot consumed by gates and
/// reporting.
/// </summary>
/// <param name="Lorentz">Raw self-field Maecker model output.</param>
/// <param name="PlasmaState">Strongly-typed plasma-state record (mirrors the model output).</param>
public sealed record MpdCycleResult(
    SelfFieldLorentzResult Lorentz,
    MpdPlasmaState         PlasmaState);

/// <summary>
/// MPD cycle solver. Validates design inputs, calls
/// <see cref="SelfFieldLorentzModel.Solve"/>, and packages the result with
/// a typed <see cref="MpdPlasmaState"/>.
/// </summary>
public static class MpdCycleSolver
{
    /// <summary>
    /// Solve the MPD cycle for one (design, conditions) pair.
    /// </summary>
    /// <param name="design">
    /// Engine design — must have <see cref="ElectricPropulsionEngineDesign.Kind"/>
    /// set to <see cref="ElectricPropulsionEngineKind.MagnetoPlasmaDynamic"/>
    /// and the 4 MPD-specific fields populated (NaN sentinels indicate a
    /// non-MPD design). MPD reuses
    /// <see cref="ElectricPropulsionEngineDesign.PropellantMassFlow_kgs"/>
    /// from the resistojet shape (per ADR-029 D3) — propellant flow is the
    /// continuous Li / Ar mass flow into the cathode.
    /// </param>
    /// <param name="conditions">
    /// Operating conditions. <see cref="ResistojetConditions"/> is reused
    /// per ADR-029 D3. <see cref="ResistojetConditions.BusPower_W_avail"/>
    /// is the binding constraint (V_arc × J_arc rejected at SA bind time
    /// when above bus power).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s Kind is not MagnetoPlasmaDynamic
    /// or when any required MPD-specific field is NaN (categorically malformed).
    /// </exception>
    public static MpdCycleResult Solve(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        if (design.Kind != ElectricPropulsionEngineKind.MagnetoPlasmaDynamic)
            throw new ArgumentException(
                $"MpdCycleSolver.Solve called with Kind={design.Kind}; expected MagnetoPlasmaDynamic.",
                nameof(design));

        // NaN-trap: every MPD field must be populated. PropellantMassFlow_kgs
        // is the continuous Li / Ar feed; the 4 MPD-specific geometry fields
        // are MpdArcCurrent_A, MpdCathodeRadius_mm, MpdAnodeRadius_mm,
        // MpdChamberLength_mm. MpdAppliedFieldStrength_T is optional (Wave-3
        // EP.W3.AF): NaN/0 → self-field-only, finite > 0 → applied-field
        // augmentation enabled.
        if (double.IsNaN(design.MpdArcCurrent_A) ||
            double.IsNaN(design.MpdCathodeRadius_mm) ||
            double.IsNaN(design.MpdAnodeRadius_mm) ||
            double.IsNaN(design.MpdChamberLength_mm) ||
            double.IsNaN(design.PropellantMassFlow_kgs))
        {
            throw new ArgumentException(
                "MPD design has NaN required field(s); populate MpdArcCurrent_A, " +
                "MpdCathodeRadius_mm, MpdAnodeRadius_mm, MpdChamberLength_mm, " +
                "and PropellantMassFlow_kgs (Li or Ar feed).",
                nameof(design));
        }

        // Wave-3 applied-field augmentation. NaN → 0 (self-field only); a
        // finite value enables the Sankaran-2004 fit. Negatives are caught
        // by the model's own argument guard.
        double bApplied = double.IsNaN(design.MpdAppliedFieldStrength_T)
            ? 0.0
            : design.MpdAppliedFieldStrength_T;

        var lorentz = SelfFieldLorentzModel.Solve(
            arcCurrent_A:           design.MpdArcCurrent_A,
            propellantMassFlow_kgs: design.PropellantMassFlow_kgs,
            cathodeRadius_mm:       design.MpdCathodeRadius_mm,
            anodeRadius_mm:         design.MpdAnodeRadius_mm,
            chamberLength_mm:       design.MpdChamberLength_mm,
            appliedFieldStrength_T: bApplied,
            appliedFieldCoupling:   design.MpdAppliedFieldCouplingOverride);

        var plasmaState = new MpdPlasmaState(
            IonExitVelocity_ms:           lorentz.ExitVelocity_ms,
            BeamCurrent_A:                design.MpdArcCurrent_A,
            PlumeDivergenceHalfAngle_rad: lorentz.PlumeDivergenceHalfAngle_rad,
            DischargeVoltage_V:           lorentz.DischargeVoltage_V,
            ThrustCoefficient_NperA2:     lorentz.ThrustCoefficient_NperA2,
            MagneticPressure_Pa:          lorentz.MagneticPressure_Pa,
            CathodeWallTemp_K:            lorentz.CathodeWallTemp_K,
            ThrustEfficiency_Maecker:     lorentz.ThrustEfficiency_Maecker)
        {
            AppliedFieldStrength_T = lorentz.AppliedFieldStrength_T,
            AppliedFieldThrust_N   = lorentz.AppliedFieldThrust_N,
            SelfFieldThrust_N      = lorentz.SelfFieldThrust_N,
        };

        return new MpdCycleResult(lorentz, plasmaState);
    }
}
