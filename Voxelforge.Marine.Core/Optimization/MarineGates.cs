// MarineGates.cs — feasibility gate evaluation for the marine pillar.
//
// Mirrors the AirbreathingFeasibility pattern: standalone static evaluator
// called from MarineOptimization.GenerateWith. NOT wired into the shared
// GateRegistry (whose Emit signature is rocket-specific).
//
// Gate census: 5 Hard + 5 Advisory = 10 total (Sprint M.2).
//
// Hard gates  — violations.Count > 0 → IsFeasible = false.
// Advisory    — returned separately; never flip IsFeasible.
//
// References:
//   ASME BPVC §VIII Div 1 UG-28 (2023): SF ≥ 1.5 for external-pressure buckling.
//   Hoerner, S. F. (1965). Fluid-Dynamic Drag §6-2.
//   ADR-026: Gate IDs prefixed MARINE_* or HULL_* (pillar discriminator).

using System;
using System.Collections.Generic;
using Voxelforge.Marine.Hydrodynamics;
using Voxelforge.Marine.Structure;
using Voxelforge.Optimization;

namespace Voxelforge.Marine.Optimization;

/// <summary>
/// Evaluates all marine feasibility gates for a given hull design and conditions.
/// </summary>
internal static class MarineGates
{
    // Thresholds ---------------------------------------------------------------

    private const double BucklingSfHardFloor     = 1.5;    // ASME UG-28 mandatory minimum
    private const double BucklingSfAdvisoryFloor = 2.0;    // advisory margin band
    private const double WallThicknessHardMin_m  = 0.0015; // 1.5 mm — LPBF + margin hard floor
    private const double WallThicknessAdvMin_m   = 0.0020; // 2.0 mm — LPBF advisory floor
    private const double DragCoeffAdvisoryMax    = 0.20;   // Hoerner §6 slender-body upper band (frontal-area based)
    private const double CgCbOffsetFractionAdv   = 0.05;   // |z_CG − z_CB| > 0.05 D → advisory
    private const double FinenesHardMin          = 4.0;    // below this → poor hydrodynamics
    private const double FinenesHardMax          = 15.0;   // above this → structural concerns
    private const double FinenesAdvMin           = 5.0;    // Hoerner §6-2 optimum low bound
    private const double FinenesAdvMax           = 12.0;   // Hoerner §6-2 optimum high bound

    // ── Planing (SurfaceHull) thresholds — Sprint M.W3 ────────────────────────

    /// <summary>Savitsky envelope low edge for beam-Froude C_v.</summary>
    internal const double PlaningSpeedCoefficientHardMin = 1.0;

    /// <summary>Savitsky envelope high edge for beam-Froude C_v.</summary>
    internal const double PlaningSpeedCoefficientHardMax = 13.0;

    /// <summary>Operational trim hard band lower edge [°].</summary>
    internal const double PlaningTrimHardMin_deg = 1.0;

    /// <summary>Operational trim hard band upper edge [°] — porpoising risk above 10°.</summary>
    internal const double PlaningTrimHardMax_deg = 10.0;

    /// <summary>Savitsky-validity λ envelope lower edge.</summary>
    internal const double PlaningLambdaHardMin = 0.8;

    /// <summary>Savitsky-validity λ envelope upper edge.</summary>
    internal const double PlaningLambdaHardMax = 6.0;

    /// <summary>Hard-chine deadrise advisory low edge [°].</summary>
    internal const double PlaningDeadriseAdvMin_deg = 5.0;

    /// <summary>Hard-chine deadrise advisory high edge [°] — deep-V cluster sits at 22–24°.</summary>
    internal const double PlaningDeadriseAdvMax_deg = 25.0;

    /// <summary>LCG-fraction operational band lower edge.</summary>
    internal const double PlaningLcgAdvMin = 0.42;

    /// <summary>LCG-fraction operational band upper edge.</summary>
    internal const double PlaningLcgAdvMax = 0.58;

    /// <summary>Cluster resistance-coefficient ceiling for planing efficiency.</summary>
    internal const double PlaningResistanceCoefficientAdvMax = 0.20;

    // ── DisplacementSurface (Holtrop-Mennen) thresholds — Sprint M.W4 ─────────

    /// <summary>Holtrop-Mennen Froude envelope low edge.</summary>
    internal const double HoltropFroudeHardMin = 0.05;

    /// <summary>Holtrop-Mennen Froude envelope high edge (above this, planing regime).</summary>
    internal const double HoltropFroudeHardMax = 0.40;

    /// <summary>
    /// Sprint M.W5. Loosened Holtrop-Mennen Froude high edge for the
    /// semi-displacement regime. Active only when the design enables the
    /// SD-correction flag; otherwise the pure-displacement
    /// <see cref="HoltropFroudeHardMax"/> = 0.40 ceiling applies.
    /// </summary>
    internal const double HoltropSemiDisplacementFroudeHardMax = 0.55;

    /// <summary>
    /// Sprint M.W5. Onset Froude number for the semi-displacement
    /// transition advisory. Identical to
    /// <see cref="Hydrodynamics.HoltropMennenResistanceModel.SemiDisplacementOnsetFn"/>.
    /// </summary>
    internal const double HoltropSemiDisplacementOnsetFn = 0.30;

    /// <summary>L/B advisory low edge for displacement hulls.</summary>
    internal const double HoltropLengthToBeamAdvMin = 4.0;

    /// <summary>L/B advisory high edge — slender beyond this is exotic.</summary>
    internal const double HoltropLengthToBeamAdvMax = 12.0;

    /// <summary>B/T advisory low edge.</summary>
    internal const double HoltropBeamToDraftAdvMin = 1.5;

    /// <summary>B/T advisory high edge.</summary>
    internal const double HoltropBeamToDraftAdvMax = 5.0;

    /// <summary>
    /// Form factor (1 + k₁) advisory ceiling. Cluster mid-band for round-
    /// bilge displacement hulls is 1.10–1.25; 1.30 catches genuinely bluff
    /// forms (high Cb + wide beam + deep draft).
    /// </summary>
    internal const double HoltropFormFactorAdvMax = 1.30;

    /// <summary>Wave-making fraction (R_W / R_T) advisory ceiling.</summary>
    internal const double HoltropWaveMakingFractionAdvMax = 0.60;

    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate all marine gates. Returns (hard violations, advisory violations).
    /// </summary>
    /// <param name="cgCbOffset_m">
    /// Axial CG/CB offset [m]. Pass 0.0 for symmetric hulls (M1 AuvMidBody).
    /// </param>
    internal static (IReadOnlyList<FeasibilityViolation> Violations,
                     IReadOnlyList<FeasibilityViolation> Advisories)
        Evaluate(
            MarineDesign       design,
            MarineConditions   cond,
            DragResult         drag,
            HydrostaticResult  hydro,
            BucklingResult     buckling,
            double             cgCbOffset_m = 0.0)
    {
        if (design   is null) throw new ArgumentNullException(nameof(design));
        if (cond     is null) throw new ArgumentNullException(nameof(cond));
        if (drag     is null) throw new ArgumentNullException(nameof(drag));
        if (hydro    is null) throw new ArgumentNullException(nameof(hydro));
        if (buckling is null) throw new ArgumentNullException(nameof(buckling));

        var violations = new List<FeasibilityViolation>();
        var advisories = new List<FeasibilityViolation>();

        // ── Hard gates ────────────────────────────────────────────────────────

        // HULL_BUOYANCY_NEGATIVE — vessel must have non-negative buoyant weight.
        if (hydro.BuoyantWeight_N < 0.0)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HullBuoyancyNegative,
                Description:  "Hull net buoyancy is negative — vessel sinks.",
                ActualValue:  hydro.BuoyantWeight_N,
                Limit:        0.0));
        }

        // HULL_BUCKLING_INSUFFICIENT — P_cr safety factor must be ≥ 1.5.
        if (buckling.BucklingSafetyFactor < BucklingSfHardFloor)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HullBucklingInsufficient,
                Description:  $"Pressure-hull buckling safety factor {buckling.BucklingSafetyFactor:F3} " +
                              $"is below ASME UG-28 minimum of {BucklingSfHardFloor}.",
                ActualValue:  buckling.BucklingSafetyFactor,
                Limit:        BucklingSfHardFloor));
        }

        // HULL_WATERTIGHT_INTEGRITY — wall must be ≥ 1.5 mm (LPBF min feature + margin).
        if (design.WallThickness_m < WallThicknessHardMin_m)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HullWatertightIntegrity,
                Description:  $"Wall thickness {design.WallThickness_m * 1e3:F2} mm is below " +
                              $"LPBF minimum feature floor of {WallThicknessHardMin_m * 1e3:F1} mm.",
                ActualValue:  design.WallThickness_m,
                Limit:        WallThicknessHardMin_m));
        }

        // DEPTH_RATING_EXCEEDED — operating depth must not exceed declared depth rating.
        if (cond.MaxDepth_m > design.DepthRating_m)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.DepthRatingExceeded,
                Description:  $"Operating depth {cond.MaxDepth_m:F1} m exceeds design depth rating " +
                              $"{design.DepthRating_m:F1} m.",
                ActualValue:  cond.MaxDepth_m,
                Limit:        design.DepthRating_m));
        }

        // HULL_FINENESS_EXTREME — L/D outside hydrodynamically viable band.
        double fr = design.FinenessRatio;
        if (fr < FinenesHardMin || fr > FinenesHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.FinenessTooExtremeHard,
                Description:  $"Fineness ratio L/D = {fr:F2} is outside hard band [{FinenesHardMin}, {FinenesHardMax}].",
                ActualValue:  fr,
                Limit:        fr < FinenesHardMin ? FinenesHardMin : FinenesHardMax));
        }

        // ── Advisory gates ────────────────────────────────────────────────────

        // HULL_DRAG_ABOVE_BAND — total Cd above Hoerner §6 slender-body band.
        if (drag.DragCoefficient > DragCoeffAdvisoryMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HullDragAboveBand,
                Description:  $"Total drag coefficient {drag.DragCoefficient:F4} exceeds " +
                              $"Hoerner §6 advisory ceiling {DragCoeffAdvisoryMax}.",
                ActualValue:  drag.DragCoefficient,
                Limit:        DragCoeffAdvisoryMax));
        }

        // HULL_FINENESS_OUT_OF_BAND — L/D outside Hoerner §6-2 optimum band [5, 12].
        if (fr >= FinenesHardMin && fr <= FinenesHardMax &&
            (fr < FinenesAdvMin || fr > FinenesAdvMax))
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.FinenesRatioOutOfBand,
                Description:  $"Fineness ratio L/D = {fr:F2} is outside Hoerner §6-2 " +
                              $"optimum band [{FinenesAdvMin}, {FinenesAdvMax}].",
                ActualValue:  fr,
                Limit:        fr < FinenesAdvMin ? FinenesAdvMin : FinenesAdvMax));
        }

        // HULL_CG_CB_OFFSET_LARGE — |z_CG − z_CB| > 5 % D.
        double cgCbLimit = CgCbOffsetFractionAdv * design.Diameter_m;
        if (cgCbOffset_m > cgCbLimit)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.CgCbOffsetLarge,
                Description:  $"CG/CB axial offset {cgCbOffset_m * 1e3:F1} mm exceeds " +
                              $"5 %×D advisory ceiling {cgCbLimit * 1e3:F1} mm.",
                ActualValue:  cgCbOffset_m,
                Limit:        cgCbLimit));
        }

        // HULL_LPBF_WALL_TOO_THIN — wall < 2.0 mm (LPBF advisory floor).
        if (design.WallThickness_m >= WallThicknessHardMin_m &&
            design.WallThickness_m < WallThicknessAdvMin_m)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.LpbfHullWallTooThin,
                Description:  $"Wall thickness {design.WallThickness_m * 1e3:F2} mm is below " +
                              $"LPBF advisory floor of {WallThicknessAdvMin_m * 1e3:F1} mm.",
                ActualValue:  design.WallThickness_m,
                Limit:        WallThicknessAdvMin_m));
        }

        // HULL_BUCKLING_SF_MARGINAL — SF in [1.5, 2.0) advisory band.
        if (buckling.BucklingSafetyFactor >= BucklingSfHardFloor &&
            buckling.BucklingSafetyFactor < BucklingSfAdvisoryFloor)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.BucklingSfMarginal,
                Description:  $"Buckling safety factor {buckling.BucklingSafetyFactor:F3} passes ASME UG-28 " +
                              $"but is in the marginal advisory band (< {BucklingSfAdvisoryFloor}).",
                ActualValue:  buckling.BucklingSafetyFactor,
                Limit:        BucklingSfAdvisoryFloor));
        }

        return (violations.AsReadOnly(), advisories.AsReadOnly());
    }

    /// <summary>
    /// Sprint M.W3 — evaluate the planing-hull (SurfaceHull) gates. Distinct
    /// evaluator from the AUV path because the input shape is different
    /// (no DragResult/HydrostaticResult/BucklingResult — just the Savitsky
    /// state).
    /// </summary>
    /// <returns>Tuple of (Hard violations, Advisory violations).</returns>
    internal static (IReadOnlyList<FeasibilityViolation> Violations,
                     IReadOnlyList<FeasibilityViolation> Advisories)
        EvaluatePlaning(
            MarineDesign                              design,
            MarineConditions                          cond,
            Hydrodynamics.SavitskyPlaningResult       planing)
    {
        if (design  is null) throw new ArgumentNullException(nameof(design));
        if (cond    is null) throw new ArgumentNullException(nameof(cond));
        if (planing is null) throw new ArgumentNullException(nameof(planing));

        var violations = new List<FeasibilityViolation>();
        var advisories = new List<FeasibilityViolation>();

        // ── Hard gates ────────────────────────────────────────────────────────

        // PLANING_SPEED_COEFFICIENT_OUT_OF_BAND — C_v outside Savitsky envelope.
        if (planing.SpeedCoefficient < PlaningSpeedCoefficientHardMin
            || planing.SpeedCoefficient > PlaningSpeedCoefficientHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.PlaningSpeedCoefficientOutOfBand,
                Description:  $"Beam-Froude C_v = {planing.SpeedCoefficient:F2} outside the Savitsky envelope " +
                              $"[{PlaningSpeedCoefficientHardMin}, {PlaningSpeedCoefficientHardMax}].",
                ActualValue:  planing.SpeedCoefficient,
                Limit:        planing.SpeedCoefficient < PlaningSpeedCoefficientHardMin
                                ? PlaningSpeedCoefficientHardMin
                                : PlaningSpeedCoefficientHardMax));
        }

        // PLANING_TRIM_OUT_OF_BAND — equilibrium trim outside the operational band.
        if (planing.TrimAngle_deg < PlaningTrimHardMin_deg
            || planing.TrimAngle_deg > PlaningTrimHardMax_deg)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.PlaningTrimOutOfBand,
                Description:  $"Equilibrium trim {planing.TrimAngle_deg:F2}° outside the operational band " +
                              $"[{PlaningTrimHardMin_deg}, {PlaningTrimHardMax_deg}]° — porpoising / bow-down risk.",
                ActualValue:  planing.TrimAngle_deg,
                Limit:        planing.TrimAngle_deg < PlaningTrimHardMin_deg
                                ? PlaningTrimHardMin_deg
                                : PlaningTrimHardMax_deg));
        }

        // PLANING_WETTED_LENGTH_TO_BEAM_OUT_OF_BAND — λ outside Savitsky-validity envelope.
        if (planing.WettedLengthToBeamRatio < PlaningLambdaHardMin
            || planing.WettedLengthToBeamRatio > PlaningLambdaHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.PlaningWettedLengthToBeamOutOfBand,
                Description:  $"Wetted length-to-beam λ = {planing.WettedLengthToBeamRatio:F2} outside the Savitsky " +
                              $"envelope [{PlaningLambdaHardMin}, {PlaningLambdaHardMax}].",
                ActualValue:  planing.WettedLengthToBeamRatio,
                Limit:        planing.WettedLengthToBeamRatio < PlaningLambdaHardMin
                                ? PlaningLambdaHardMin
                                : PlaningLambdaHardMax));
        }

        // ── Advisory gates ────────────────────────────────────────────────────

        // PLANING_DEADRISE_OUT_OF_BAND — outside hard-chine cluster.
        if (design.DeadriseAngle_deg < PlaningDeadriseAdvMin_deg
            || design.DeadriseAngle_deg > PlaningDeadriseAdvMax_deg)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.PlaningDeadriseOutOfBand,
                Description:  $"Deadrise β = {design.DeadriseAngle_deg:F1}° outside hard-chine planing band " +
                              $"[{PlaningDeadriseAdvMin_deg}, {PlaningDeadriseAdvMax_deg}]°.",
                ActualValue:  design.DeadriseAngle_deg,
                Limit:        design.DeadriseAngle_deg < PlaningDeadriseAdvMin_deg
                                ? PlaningDeadriseAdvMin_deg
                                : PlaningDeadriseAdvMax_deg));
        }

        // PLANING_LCG_OUT_OF_BAND — LCG fraction outside operational band.
        if (design.LongitudinalCgFraction < PlaningLcgAdvMin
            || design.LongitudinalCgFraction > PlaningLcgAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.PlaningLcgOutOfBand,
                Description:  $"LCG fraction {design.LongitudinalCgFraction:F3} outside operational band " +
                              $"[{PlaningLcgAdvMin}, {PlaningLcgAdvMax}] — trim instability risk.",
                ActualValue:  design.LongitudinalCgFraction,
                Limit:        design.LongitudinalCgFraction < PlaningLcgAdvMin
                                ? PlaningLcgAdvMin
                                : PlaningLcgAdvMax));
        }

        // PLANING_RESISTANCE_ABOVE_BAND — total resistance coefficient above cluster ceiling.
        if (planing.ResistanceCoefficient > PlaningResistanceCoefficientAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.PlaningResistanceAboveBand,
                Description:  $"Total resistance coefficient {planing.ResistanceCoefficient:F4} above " +
                              $"planing-cluster ceiling {PlaningResistanceCoefficientAdvMax} — design is " +
                              "hydrodynamically inefficient.",
                ActualValue:  planing.ResistanceCoefficient,
                Limit:        PlaningResistanceCoefficientAdvMax));
        }

        return (violations.AsReadOnly(), advisories.AsReadOnly());
    }

    /// <summary>
    /// Sprint M.W4 — evaluate the displacement-surface (Holtrop-Mennen) gates.
    /// Distinct evaluator from the planing path because the input shape is
    /// different (HoltropMennenResult vs SavitskyPlaningResult).
    /// </summary>
    internal static (IReadOnlyList<FeasibilityViolation> Violations,
                     IReadOnlyList<FeasibilityViolation> Advisories)
        EvaluateDisplacementSurface(
            MarineDesign                              design,
            MarineConditions                          cond,
            Hydrodynamics.HoltropMennenResult         holtrop)
    {
        if (design  is null) throw new ArgumentNullException(nameof(design));
        if (cond    is null) throw new ArgumentNullException(nameof(cond));
        if (holtrop is null) throw new ArgumentNullException(nameof(holtrop));

        var violations = new List<FeasibilityViolation>();
        var advisories = new List<FeasibilityViolation>();

        // HOLTROP_FROUDE_OUT_OF_BAND — Hard. Above 0.40 → planing regime;
        // below 0.05 → solver loses fidelity (wave-making term collapses).
        // Sprint M.W5: when EnableSemiDisplacementCorrection is active, the
        // upper hard ceiling is relaxed to 0.55 (the SD-correction validity
        // limit); the SD-regime advisory then flags operation in [0.30, 0.55].
        double upperFroudeLimit = design.EnableSemiDisplacementCorrection
            ? HoltropSemiDisplacementFroudeHardMax
            : HoltropFroudeHardMax;
        if (holtrop.FroudeNumber < HoltropFroudeHardMin
            || holtrop.FroudeNumber > upperFroudeLimit)
        {
            string upperLabel = design.EnableSemiDisplacementCorrection
                ? "semi-displacement"
                : "displacement";
            violations.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HoltropFroudeOutOfBand,
                Description:  $"Froude number Fn = {holtrop.FroudeNumber:F3} outside Holtrop-Mennen "
                            + $"{upperLabel} validity envelope "
                            + $"[{HoltropFroudeHardMin:F2}, {upperFroudeLimit:F2}]. "
                            + "Above the high edge, the planing regime takes over (use "
                            + "HullFamily.Planing instead).",
                ActualValue:  holtrop.FroudeNumber,
                Limit:        holtrop.FroudeNumber < HoltropFroudeHardMin
                                ? HoltropFroudeHardMin
                                : upperFroudeLimit));
        }

        // HOLTROP_SEMI_DISPLACEMENT_REGIME — Advisory. Sprint M.W5. Only
        // emitted when the design opts into the SD correction AND Fn has
        // entered the transition band [0.30, 0.55]. Pure-displacement
        // designs never see this advisory (the upstream Froude hard gate
        // already rejects Fn > 0.40 for them).
        if (design.EnableSemiDisplacementCorrection
            && holtrop.FroudeNumber > HoltropSemiDisplacementOnsetFn
            && holtrop.FroudeNumber <= HoltropSemiDisplacementFroudeHardMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HoltropSemiDisplacementRegime,
                Description:  $"Fn = {holtrop.FroudeNumber:F3} is in the semi-displacement "
                            + $"transition band ({HoltropSemiDisplacementOnsetFn:F2}, "
                            + $"{HoltropSemiDisplacementFroudeHardMax:F2}]; the SD high-Fn "
                            + $"correction is reducing R_W by a factor of "
                            + $"{holtrop.SemiDisplacementReductionFactor:F3}. Validation "
                            + "envelope is ±25 % (wider than the displacement-only ±15 %).",
                ActualValue:  holtrop.FroudeNumber,
                Limit:        HoltropSemiDisplacementOnsetFn));
        }

        // L/B advisory band.
        double LB = design.Length_m / Math.Max(1e-6, design.BeamWaterline_m);
        if (LB < HoltropLengthToBeamAdvMin || LB > HoltropLengthToBeamAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HoltropLengthToBeamOutOfBand,
                Description:  $"L/B = {LB:F2} outside displacement-hull cluster band "
                            + $"[{HoltropLengthToBeamAdvMin:F1}, {HoltropLengthToBeamAdvMax:F1}]. "
                            + "Below: stubby + draggy. Above: slender exotic.",
                ActualValue:  LB,
                Limit:        LB < HoltropLengthToBeamAdvMin
                                ? HoltropLengthToBeamAdvMin
                                : HoltropLengthToBeamAdvMax));
        }

        // B/T advisory band.
        double BT = design.BeamWaterline_m / Math.Max(1e-6, design.DraftDesign_m);
        if (BT < HoltropBeamToDraftAdvMin || BT > HoltropBeamToDraftAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HoltropBeamToDraftOutOfBand,
                Description:  $"B/T = {BT:F2} outside displacement-hull cluster band "
                            + $"[{HoltropBeamToDraftAdvMin:F1}, {HoltropBeamToDraftAdvMax:F1}]. "
                            + "Below: deep narrow (stability risk). Above: wide shallow "
                            + "(roll-stability concern).",
                ActualValue:  BT,
                Limit:        BT < HoltropBeamToDraftAdvMin
                                ? HoltropBeamToDraftAdvMin
                                : HoltropBeamToDraftAdvMax));
        }

        // Form factor (1 + k₁) advisory ceiling.
        if (holtrop.FormFactor > HoltropFormFactorAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: MarineConstraintIds.HoltropFormFactorAboveBand,
                Description:  $"Form factor (1+k₁) = {holtrop.FormFactor:F3} above advisory ceiling "
                            + $"{HoltropFormFactorAdvMax:F2}. Hull form is bluff; viscous-form drag "
                            + "is high. Consider increasing L/B or reducing C_b.",
                ActualValue:  holtrop.FormFactor,
                Limit:        HoltropFormFactorAdvMax));
        }

        // Wave-making fraction advisory — operating near or above hump speed.
        if (holtrop.TotalResistance_N > 0)
        {
            double waveFrac = holtrop.WaveMakingResistance_N / holtrop.TotalResistance_N;
            if (waveFrac > HoltropWaveMakingFractionAdvMax)
            {
                advisories.Add(new FeasibilityViolation(
                    ConstraintId: MarineConstraintIds.HoltropWaveMakingDominant,
                    Description:  $"Wave-making fraction R_W/R_T = {waveFrac:F2} above advisory "
                                + $"ceiling {HoltropWaveMakingFractionAdvMax:F2}. Operating near "
                                + "hump speed; small Fn increases drive disproportionate drag "
                                + "growth. Consider a planing or semi-planing hull form.",
                    ActualValue:  waveFrac,
                    Limit:        HoltropWaveMakingFractionAdvMax));
            }
        }

        return (violations.AsReadOnly(), advisories.AsReadOnly());
    }
}
