// PressureStackup.cs — feed-system pressure stackup from tank ullage
// down to the chamber.
//
//   P_tank_ullage
//     − ΔP_feedline        (Darcy–Weisbach on a straight pipe)
//     − ΔP_filter          (per-preset nominal — clean + dirty)
//     − ΔP_umbilical_QD    (per-preset nominal from UmbilicalStandards)
//     − ΔP_main_valve      (valve Cv → ΔP at given Q, ρ)
//     − ΔP_dome            (1.5 velocity heads fallback; proper model when DomeHydraulics is opted in)
//     − ΔP_injector        (from pattern DeltaPInjFraction × Pc)
//     = P_chamber
//
// Opt-in via `OperatingConditions.TankUllagePressure_Pa > 0`. When 0,
// the stackup is skipped and no result is attached — preserves
// backward-compat with legacy saved designs that predate this field.
//
// Scope (MVP):
//   • One feed line per propellant treated symmetrically; use the more
//     constrained of the two for the gate.
//   • Dome loss is hardcoded at 1.5 · ½·ρ·v² at the dome inlet
//     (velocity from bore area + mass flow). A proper DomeHydraulics
//     correlation supersedes this when opted in.
//   • Pump-fed branch not wired — pressure-fed only.
//
// References:
//   Huzel & Huang AIAA Vol. 147 §5 (feed-system sizing); Sutton &
//   Biblarz RPE 9e §6.9.

using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.FeedSystem;

/// <summary>
/// One segment of the feed-system pressure budget. Name is a short
/// stable key used in reports; ΔP is the pressure drop across the
/// segment (positive = loss, negative = gain — only the tank ullage
/// is positive). OutletPressure_Pa is the pressure at the downstream
/// end of this segment.
/// </summary>
public readonly record struct FeedSegment(
    string Name,
    double DeltaP_Pa,
    double OutletPressure_Pa);

public sealed record PressureStackupResult(
    FeedSegment[] Segments,
    double TankUllagePressure_Pa,
    double PredictedChamberPressure_Pa,
    double TargetChamberPressure_Pa,
    double MarginFraction,            // (predicted − target) / target
    bool IsFeasible,
    string[] Warnings,
    // ── Sprint 19 (2026-04-23): blow-down end-of-burn stackup ──────
    // Populated only when cond.BlowDownFinalPressure_Pa > 0 (blow-down
    // mode). EndOfBurnTankPressure_Pa carries the user's specified
    // end-point; the three "EndOfBurn*" fields mirror the equivalent
    // start-of-burn fields, computed at the lower tank pressure.
    // All zero on regulated designs (default = no blow-down).
    double EndOfBurnTankPressure_Pa = 0.0,
    double EndOfBurnPredictedChamberPressure_Pa = 0.0,
    double EndOfBurnMarginFraction = 0.0,
    bool   EndOfBurnIsFeasible = true);

public static class PressureStackup
{
    /// <summary>
    /// Minimum fractional margin between predicted and target chamber P
    /// before the feasibility gate flags the design. 0 = predicted meets
    /// target exactly; 0.05 = 5 % headroom.
    /// </summary>
    public const double MinMarginFraction = 0.0;

    /// <summary>
    /// Compute the pressure stackup. Returns null when the user hasn't
    /// opted in (TankUllagePressure_Pa == 0).
    /// </summary>
    public static PressureStackupResult? Compute(
        OperatingConditions cond,
        RegenChamberDesign design,
        HeatTransfer.RegenSolverOutputs thermal,
        double injectorDeltaPInj_Pa,
        double fuelMassFlow_kgs,
        double oxMassFlow_kgs,
        double chamberRadius_mm = 20.0)
    {
        if (cond.TankUllagePressure_Pa <= 1.0) return null;   // not configured

        var warnings = new List<string>();
        var segments = new List<FeedSegment>();

        // Fuel side is usually the worse case in regen-cooled engines
        // because the fuel also pays the jacket ΔP. Size the stackup
        // around it; mark the ox side in a warning if its line is worse.
        var (fuelRho, oxRho) = FuelOxDensities(cond);
        var (fuelMu, oxMu)   = FuelOxViscosities(cond);
        double fuelFlow = System.Math.Max(fuelMassFlow_kgs, 1e-6);
        double oxFlow   = System.Math.Max(oxMassFlow_kgs,   1e-6);

        // ── 1. Tank ullage (start) ───────────────────────────────
        double p = cond.TankUllagePressure_Pa;
        segments.Add(new FeedSegment("Tank ullage", 0, p));

        // ── 2. Feed-line friction (Darcy-Weisbach; fuel side) ────
        // A5 (post-Phase-6 audit): pass per-fluid μ so the Reynolds-driven
        // friction regime is correct. Pre-A5, both calls used the LineLoss
        // default (3e-4 Pa·s, the LOX value), inflating LH2 line μ by 25×
        // and silently distorting friction predictions on hydrogen designs.
        double lineDP_fuel = LineLoss.FrictionDP(
            length_m:  cond.FeedLineLength_m,
            dia_m:     cond.FeedLineDiameter_mm * 1e-3,
            massFlow_kgs: fuelFlow,
            density_kgm3: fuelRho,
            viscosity_PaS: fuelMu);
        p -= lineDP_fuel;
        segments.Add(new FeedSegment("Feed line (fuel)", lineDP_fuel, p));

        double lineDP_ox = LineLoss.FrictionDP(
            length_m:  cond.FeedLineLength_m,
            dia_m:     cond.FeedLineDiameter_mm * 1e-3,
            massFlow_kgs: oxFlow,
            density_kgm3: oxRho,
            viscosity_PaS: oxMu);
        if (lineDP_ox > lineDP_fuel)
            warnings.Add($"Ox-side feed-line ΔP {lineDP_ox / 1e6:F2} MPa > fuel-side {lineDP_fuel / 1e6:F2} MPa — size ox pipe larger.");

        // ── 3. Jacket pressure drop (already sized by thermal solver) ──
        // The coolant has already absorbed the regen-jacket ΔP; roll it
        // into the stackup so the user sees the full budget. Reported
        // with a negative sign because thermal.CoolantPressureDrop_Pa
        // is already the delta (positive loss).
        double jacketDP = thermal.CoolantPressureDrop_Pa;
        p -= jacketDP;
        segments.Add(new FeedSegment("Regen jacket", jacketDP, p));

        // ── 4. Filter (preset, or Custom → user scalar) ──
        // Preset replaces the legacy scalar entirely; Custom mode reads
        // the scalar as the clean value with a generic 3× dirty cap.
        double filterDP = FilterPresets.EffectiveDeltaP_Pa(
            cond.FilterStandard,
            customCleanDP_Pa:        cond.FilterDeltaP_Pa,
            contaminationFraction:   cond.FilterContaminationFraction);
        var filterSpec = FilterPresets.SpecFor(cond.FilterStandard);
        string filterName = cond.FilterStandard == FilterStandard.Custom
            ? "Filter (custom)"
            : $"Filter ({filterSpec.DisplayName}, {cond.FilterContaminationFraction * 100:F0}% loaded)";
        p -= filterDP;
        segments.Add(new FeedSegment(filterName, filterDP, p));

        // ── 5. Umbilical / quick-disconnect ──────────────────────
        double umbDP = UmbilicalStandards.NominalDeltaP_Pa(
            cond.UmbilicalStandard, fuelFlow, fuelRho);
        p -= umbDP;
        segments.Add(new FeedSegment("Umbilical / QD", umbDP, p));

        // ── 6. Main valve (Cv model) ─────────────────────────────
        double valveDP = ValveCv.DeltaP(
            Cv_gpm_psi: System.Math.Max(cond.MainValveCv, 0.1),
            massFlow_kgs: fuelFlow,
            density_kgm3: fuelRho);
        p -= valveDP;
        segments.Add(new FeedSegment("Main valve", valveDP, p));

        // ── 7. Injector dome (real model when opted in) ──
        // When the user has sized a dome (FuelDomeDepth_mm > 0), run the
        // Borda–Carnot + distribution + baffle model from DomeHydraulics.
        // Otherwise fall back to the 1.5-velocity-head placeholder
        // — keeps backward-compat with legacy designs.
        double domeDP;
        string domeSegmentName;
        if (design.FuelDomeDepth_mm > 0)
        {
            var spec = new Injector.DomeSpec(
                DomeDepth_mm:            design.FuelDomeDepth_mm,
                DomeRadius_mm:           0.95 * chamberRadius_mm,
                InletDiameter_mm:        design.DomeInletDiameter_mm,
                IncludeAntiVortexBaffle: design.IncludeAntiVortexBaffle);
            var dome = Injector.DomeHydraulics.Compute(spec, fuelFlow, fuelRho);
            domeDP = dome.TotalDP_Pa;
            domeSegmentName = design.IncludeAntiVortexBaffle
                ? "Injector dome (fuel, baffled)"
                : "Injector dome (fuel)";
        }
        else
        {
            domeDP = ApproximateDomeLoss_Pa(fuelFlow, fuelRho);
            domeSegmentName = "Injector dome (fuel, approx)";
        }
        p -= domeDP;
        segments.Add(new FeedSegment(domeSegmentName, domeDP, p));

        // ── 8. Injector orifices ─────────────────────────────────
        p -= injectorDeltaPInj_Pa;
        segments.Add(new FeedSegment("Injector ΔP", injectorDeltaPInj_Pa, p));

        // ── Result ───────────────────────────────────────────────
        double predicted = p;
        double target = cond.ChamberPressure_Pa;
        double margin = (predicted - target) / System.Math.Max(target, 1);
        bool ok = margin >= MinMarginFraction;

        if (!ok)
            warnings.Add($"Feed stackup shortfall: predicted Pc {predicted / 1e6:F2} MPa < target {target / 1e6:F2} MPa. "
                       + $"Raise tank ullage or reduce line / valve / injector ΔP.");
        else if (margin < 0.15)
            warnings.Add($"Feed margin {margin * 100:F1} % — less than the 15 % rule of thumb for pressure-fed hardware.");

        // ── Sprint 19: end-of-burn stackup for blow-down mode ─────────
        // When BlowDownFinalPressure_Pa > 0, re-run the flow-invariant
        // ΔP segments at the reduced tank pressure. The per-segment
        // drops are flow-rate driven (not tank-pressure driven) so we
        // can reuse the same sum — subtract the same ΔP total from the
        // lower starting pressure. Mass flow may drop slightly as Pc
        // drops (orifice choking effect) but for this preliminary-design
        // screening, holding ΔP constant is the conservative choice
        // (over-estimates end-of-burn Pc slightly → user sees the real
        // shortfall sooner on borderline designs).
        double endOfBurnTankP = 0.0;
        double endOfBurnPredicted = 0.0;
        double endOfBurnMargin = 0.0;
        bool   endOfBurnOk = true;

        if (cond.BlowDownFinalPressure_Pa > 1.0)
        {
            endOfBurnTankP = cond.BlowDownFinalPressure_Pa;
            if (endOfBurnTankP >= cond.TankUllagePressure_Pa)
            {
                warnings.Add(
                    $"Blow-down final tank pressure {endOfBurnTankP / 1e6:F2} MPa ≥ "
                  + $"initial ullage {cond.TankUllagePressure_Pa / 1e6:F2} MPa — "
                  + "this isn't actually blow-down. End-of-burn stackup skipped.");
            }
            else
            {
                double totalDP = cond.TankUllagePressure_Pa - predicted;
                endOfBurnPredicted = endOfBurnTankP - totalDP;
                endOfBurnMargin = (endOfBurnPredicted - target)
                                / System.Math.Max(target, 1);
                endOfBurnOk = endOfBurnMargin >= MinMarginFraction;

                if (!endOfBurnOk)
                    warnings.Add(
                        $"Blow-down end-of-burn shortfall: predicted Pc "
                      + $"{endOfBurnPredicted / 1e6:F2} MPa < target "
                      + $"{target / 1e6:F2} MPa at the lower tank pressure "
                      + $"{endOfBurnTankP / 1e6:F2} MPa. Raise the end-of-"
                      + "burn tank pressure (smaller ullage ratio) or switch "
                      + "to a regulated-pressure feed.");
                else if (endOfBurnMargin < 0.15)
                    warnings.Add(
                        $"Blow-down end-of-burn margin {endOfBurnMargin * 100:F1}% "
                      + "— less than the 15% rule of thumb for pressure-fed hardware.");
            }
        }

        return new PressureStackupResult(
            Segments: segments.ToArray(),
            TankUllagePressure_Pa: cond.TankUllagePressure_Pa,
            PredictedChamberPressure_Pa: predicted,
            TargetChamberPressure_Pa: target,
            MarginFraction: margin,
            IsFeasible: ok,
            Warnings: warnings.ToArray(),
            EndOfBurnTankPressure_Pa:              endOfBurnTankP,
            EndOfBurnPredictedChamberPressure_Pa:  endOfBurnPredicted,
            EndOfBurnMarginFraction:               endOfBurnMargin,
            EndOfBurnIsFeasible:                   endOfBurnOk);
    }

    // Fuel / ox densities at injector-inlet conditions. Re-uses the
    // OrificeModel reference-density table. In a pump-fed branch
    // these will come from a pump-outlet state instead.
    private static (double fuelRho, double oxRho) FuelOxDensities(OperatingConditions cond)
    {
        var (ox, fuel) = Injector.OrificeModel.InjectionDensities(cond.PropellantPair);
        return (fuel, ox);
    }

    private static (double fuelMu, double oxMu) FuelOxViscosities(OperatingConditions cond)
    {
        var (ox, fuel) = Injector.OrificeModel.InjectionViscosities(cond.PropellantPair);
        return (fuel, ox);
    }

    private static double ApproximateDomeLoss_Pa(double massFlow_kgs, double density_kgm3)
    {
        // Assume a dome inlet ≈ 15 mm dia on a small-kN class engine;
        // velocity scales as m_dot / (ρ·A). Hardcoded loss coefficient
        // K_dome = 1.5 is representative of a simple expansion chamber
        // (Huzel §5.2). A proper DomeHydraulics correlation supersedes
        // this when opted in.
        const double K_dome = 1.5;
        const double domeArea_m2 = System.Math.PI * 0.0075 * 0.0075;
        double v = massFlow_kgs / (density_kgm3 * domeArea_m2);
        return K_dome * 0.5 * density_kgm3 * v * v;
    }
}
