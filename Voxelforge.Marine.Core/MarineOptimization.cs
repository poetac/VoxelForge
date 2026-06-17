// MarineOptimization.cs — top-level static orchestrator for the marine pillar.
//
// Analogous to AirbreathingOptimization.GenerateWith on the air-breathing
// side. Calls each physics solver in sequence and collects results into
// a MarineResult. The order is: fairing geometry → drag → buoyancy →
// buckling → gate evaluation.
//
// Wave-1 + Wave-2 shipped. Dispatches on MarineKind: AuvMidBody
// (Myring / CylindricalHemi fairing → Hoerner drag → hydrostatic
// equilibrium → buckling), SurfaceHull (Savitsky 1964 planing),
// DisplacementSurface (simplified Holtrop-Mennen 1984). Hydrodynamics
// solvers live in Voxelforge.Marine.Core/Hydrodynamics/.

using System;
using Voxelforge.Engines;
using Voxelforge.Marine.Hydrodynamics;
using Voxelforge.Marine.Optimization;
using Voxelforge.Marine.Structure;

namespace Voxelforge.Marine;

/// <summary>
/// Top-level orchestrator for the marine pillar. Coordinates physics
/// solvers and produces a <see cref="MarineResult"/>.
/// </summary>
public static class MarineOptimization
{
    /// <summary>
    /// Evaluate a marine hull design against operating conditions.
    /// Returns a <see cref="MarineResult"/> with drag, buoyancy, structural
    /// safety factor, and gate violations. Dispatches on
    /// <see cref="MarineDesign.Kind"/>:
    /// <see cref="MarineKind.AuvMidBody"/> runs the submerged-AUV pipeline
    /// (Myring or CylindricalHemi fairing → Hoerner drag → hydrostatic
    /// equilibrium → buckling). <see cref="MarineKind.SurfaceHull"/> (Wave-3
    /// Sprint M.W3) runs the planing pipeline via
    /// <see cref="SavitskyPlaningModel"/>.
    /// </summary>
    public static MarineResult GenerateWith(MarineDesign design, MarineConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));
        if (design.Family != EngineFamilies.Marine)
            throw new ArgumentException($"Design family '{design.Family}' is not marine.", nameof(design));
        if (cond.Family != EngineFamilies.Marine)
            throw new ArgumentException($"Conditions family '{cond.Family}' is not marine.", nameof(cond));

        return design.Kind switch
        {
            MarineKind.AuvMidBody           => RunAuvPipeline(design, cond),
            MarineKind.SurfaceHull          => RunSurfaceHullPipeline(design, cond),
            MarineKind.DisplacementSurface  => RunDisplacementSurfacePipeline(design, cond),
            _ => throw new NotSupportedException(
                $"MarineKind={design.Kind} is not implemented. Supported kinds: "
              + "AuvMidBody, SurfaceHull, DisplacementSurface."),
        };
    }

    private static MarineResult RunDisplacementSurfacePipeline(
        MarineDesign design,
        MarineConditions cond)
    {
        if (design.HullFamily != HullFamily.DisplacementSurface)
            throw new ArgumentException(
                $"DisplacementSurface pipeline requires HullFamily=DisplacementSurface; "
              + $"got {design.HullFamily}.",
                nameof(design));

        var holtrop = HoltropMennenResistanceModel.Solve(
            speed_ms:               cond.CruiseSpeed_ms,
            lengthWaterline_m:      design.Length_m,
            beamWaterline_m:        design.BeamWaterline_m,
            draft_m:                design.DraftDesign_m,
            blockCoefficient:       design.BlockCoefficient,
            massDisplacement_kg:    design.DisplacementMass_kg,
            waterDensity_kgm3:      cond.WaterDensity_kgm3,
            kinematicViscosity_m2s: MarineConditions.KinematicViscosity_m2s,
            enableSemiDisplacementCorrection: design.EnableSemiDisplacementCorrection);

        var (violations, advisories) = MarineGates.EvaluateDisplacementSurface(
            design, cond, holtrop);

        // DisplacementSurface fills resistance fields + draft-derived
        // hull-mass; NaN-out the AUV-shape submerged fields. Re-uses the
        // planing result-field shape for cross-surface-kind comparability.
        return new MarineResult(
            Design:                      design,
            Conditions:                  cond,
            DragForce_N:                 holtrop.TotalResistance_N,
            DragCoefficient:             holtrop.TotalResistance_N
                                         / (0.5 * cond.WaterDensity_kgm3
                                            * cond.CruiseSpeed_ms * cond.CruiseSpeed_ms
                                            * holtrop.WettedSurfaceArea_m2),
            BuoyancyForce_N:             holtrop.DisplacedVolume_m3 * cond.WaterDensity_kgm3 * 9.80665,
            DisplacedVolume_m3:          holtrop.DisplacedVolume_m3,
            BuoyantWeight_N:             double.NaN,
            CriticalBucklingPressure_Pa: double.NaN,
            BucklingSafetyFactor:        double.NaN,
            HullMass_kg:                 design.DisplacementMass_kg,
            CgCbOffset_m:                0.0,
            Violations:                  violations,
            Advisories:                  advisories,
            IsFeasible:                  violations.Count == 0)
        {
            // Re-use planing's result fields for cross-surface-kind shape
            // consistency. TrimAngle is meaningless for displacement hulls
            // (no significant trim under design cruise) — left at NaN.
            TrimAngle_deg           = double.NaN,
            WettedLengthToBeamRatio = double.NaN,
            SpeedCoefficient        = holtrop.FroudeNumber,
            WettedSurfaceArea_m2    = holtrop.WettedSurfaceArea_m2,
        };
    }

    private static MarineResult RunAuvPipeline(MarineDesign design, MarineConditions cond)
    {
        var fairing = design.HullFamily switch
        {
            HullFamily.Myring          => MyringFairingGeometry.Compute(design),
            HullFamily.CylindricalHemi => CylHemiFairingGeometry.Compute(design),
            _ => throw new ArgumentOutOfRangeException(nameof(design.HullFamily),
                     design.HullFamily,
                     "AUV pipeline requires Myring or CylindricalHemi hull family."),
        };
        var drag     = HoernerDragSolver.Solve(fairing, cond);
        var hydro    = HydrostaticEquilibrium.Solve(fairing, design, cond);
        var buckling = PressureHullBuckling.Solve(design, cond);

        var (violations, advisories) = MarineGates.Evaluate(design, cond, drag, hydro, buckling);

        return new MarineResult(
            Design:                      design,
            Conditions:                  cond,
            DragForce_N:                 drag.DragForce_N,
            DragCoefficient:             drag.DragCoefficient,
            BuoyancyForce_N:             hydro.BuoyancyForce_N,
            DisplacedVolume_m3:          hydro.DisplacedVolume_m3,
            BuoyantWeight_N:             hydro.BuoyantWeight_N,
            CriticalBucklingPressure_Pa: buckling.CriticalBucklingPressure_Pa,
            BucklingSafetyFactor:        buckling.BucklingSafetyFactor,
            HullMass_kg:                 hydro.HullMass_kg,
            CgCbOffset_m:                0.0,
            Violations:                  violations,
            Advisories:                  advisories,
            IsFeasible:                  violations.Count == 0);
    }

    private static MarineResult RunSurfaceHullPipeline(MarineDesign design, MarineConditions cond)
    {
        if (design.HullFamily != HullFamily.Planing)
            throw new ArgumentException(
                $"SurfaceHull pipeline requires HullFamily=Planing; got {design.HullFamily}.",
                nameof(design));

        var planing = SavitskyPlaningModel.Solve(
            speed_ms:               cond.CruiseSpeed_ms,
            beamMidship_m:          design.BeamMidship_m,
            deadriseAngle_deg:      design.DeadriseAngle_deg,
            massDisplacement_kg:    design.MassDisplacement_kg,
            waterDensity_kgm3:      cond.WaterDensity_kgm3,
            kinematicViscosity_m2s: MarineConditions.KinematicViscosity_m2s);

        var (violations, advisories) = MarineGates.EvaluatePlaning(design, cond, planing);

        // SurfaceHull populates the planing-specific init-only fields and
        // NaN-out the AUV-specific positional fields. HullMass = mass
        // displacement (vessel weight is the natural mass quantity for
        // planing). Buoyancy / buckling fields are NaN.
        return new MarineResult(
            Design:                      design,
            Conditions:                  cond,
            DragForce_N:                 planing.TotalResistance_N,
            DragCoefficient:             planing.ResistanceCoefficient,
            BuoyancyForce_N:             double.NaN,
            DisplacedVolume_m3:          double.NaN,
            BuoyantWeight_N:             double.NaN,
            CriticalBucklingPressure_Pa: double.NaN,
            BucklingSafetyFactor:        double.NaN,
            HullMass_kg:                 design.MassDisplacement_kg,
            CgCbOffset_m:                0.0,
            Violations:                  violations,
            Advisories:                  advisories,
            IsFeasible:                  planing.Converged && violations.Count == 0)
        {
            TrimAngle_deg           = planing.TrimAngle_deg,
            WettedLengthToBeamRatio = planing.WettedLengthToBeamRatio,
            SpeedCoefficient        = planing.SpeedCoefficient,
            WettedSurfaceArea_m2    = planing.WettedSurfaceArea_m2,
        };
    }
}
