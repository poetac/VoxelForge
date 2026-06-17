// ProofTestAnalysis.cs — Hydrostatic proof-test and burst-margin analysis.
//
// Standard practice for flight hardware: after fabrication and before any
// hot-fire, the chamber is filled with water (or inert fluid) and pressurised
// to a proof factor times Maximum Expected Operating Pressure (MEOP). For
// liquid rocket engines, typical proof factors are:
//
//   • NASA/STD-5012: 1.5 × MEOP for pressure vessels (non-damage tolerant)
//   • MIL-STD-1540:  1.25 × MEOP for composite-overwrapped / 1.5 for metals
//   • Sutton 9e:     1.5 × MEOP is the widely-accepted LRE convention
//
// The engine passes the proof test if:
//   1. No permanent deformation (stays elastic — peak VM stress < σ_y).
//   2. No leak (integrity preserved — usually checked separately via helium
//      leak-down, not modelled here).
//
// This module re-runs the structural check at the elevated pressure using
// COLD wall temperatures (hydrostatic test is done cold, so thermal stress
// vanishes and only mechanical pressure stress remains). The result is a
// separate StructuralSummary reported alongside the steady-state hot-fire one.
//
// Burst margin — separately, compute the pressure at which a room-temperature
// chamber first yields (purely elastic-limit burst, NOT ultimate rupture):
//
//     P_burst_elastic = σ_y(T_cold) · t_wall / r_max
//
// Real burst pressure is typically 1.2–1.5× P_burst_elastic for ductile
// materials due to strain hardening, but certifying to ultimate requires
// measured uniaxial stress-strain curves; we stop at yield for safety.
//
// Not modelled (defer to FEA or testing):
//   • Localised stress concentrations around ports/flanges.
//   • Fatigue (LCF/HCF) — only static yield is checked.
//   • Buckling under external over-pressure (unlikely for regen chambers).
//   • Fracture mechanics with flaw-growth assumptions.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Structure;

public sealed record ProofTestInputs(
    double ProofPressure_Pa,             // typically 1.5 × MEOP
    double TestTemperature_K = 293.0);   // usually room temperature

public sealed record ProofTestResult(
    double ProofPressure_Pa,
    double ProofFactor,                  // ProofPressure / MEOP
    StructuralSummary ColdStructure,     // structural check with no thermal stress
    double ElasticBurstPressure_Pa,      // P at which cold chamber first yields
    double BurstMarginFactor,            // ElasticBurst / MEOP
    bool Passes,                         // all SF >= 1.0 at proof pressure
    string[] Warnings,
    // Hash of the (cond, design) this proof was run against. Compare
    // to the current gen.DesignHash to detect staleness.
    string DesignHash = "");

public static class ProofTestAnalysis
{
    /// <summary>
    /// Z2.8 follow-on (2026-04-28): cheap burst-margin calculation extracted
    /// from <see cref="Evaluate"/>. Lets the SA-hot-path feasibility gate
    /// fire <c>BURST_MARGIN_INSUFFICIENT</c> without paying for a full
    /// proof-test pass (which synthesises cold stations + re-runs
    /// <see cref="StructuralCheck.Evaluate"/>).
    /// <para>
    /// Z2.10 follow-on (2026-04-28): for bimetallic / multi-wall designs
    /// the burst-pressure calculation now uses the composite hoop formula:
    /// <c>P_burst = (σ_y_inner × t_inner + σ_y_jacket × t_jacket) / r</c>.
    /// This matches the multi-wall hoop credit <see cref="StructuralCheck.Evaluate"/>
    /// already applies (Sprint G', 2026-04-27) and the composite yield
    /// from A1-follow-on (jacketMaterial parameter, PR #113). Without
    /// this, BURST_MARGIN_INSUFFICIENT was firing on designs that
    /// StructuralCheck.Evaluate's hot-path SF passed — inconsistent
    /// physics across the two analyses. Pre-Z2.10 only the inner liner's
    /// thickness × yield contributed to the burst calc, blind to the
    /// jacket's structural credit. Pintle (10 kN, 6 MPa, r ≈ 54 mm,
    /// 0.6 mm GRCop-42 inner liner) was firing 100 % of SA candidates
    /// at 0.43× burst margin pre-Z2.10; with composite-wall credit on
    /// the GRCop42_Inconel625 bimetallic stack it clears 2.5×.
    /// </para>
    /// <para>
    /// Per-station wall thickness profile supported via <paramref name="gasSideWallProfile_mm"/>;
    /// null falls back to the legacy worst-r + scalar-t formula. Default
    /// <c>outerJacketThickness_mm = 0</c> + <c>jacketMaterial = null</c>
    /// preserves the pre-Z2.10 single-wall behaviour bit-identically for
    /// callers that haven't been updated.
    /// </para>
    /// </summary>
    public static double ComputeBurstMarginFactor(
        RegenSolverOutputs thermal,
        WallMaterial wall,
        double gasSideWallThickness_mm,
        double chamberPressure_MEOP_Pa,
        double[]? gasSideWallProfile_mm = null,
        // Z2.10: outer jacket thickness + material. When both supplied,
        // the burst-pressure calc uses composite hoop credit (inner + jacket).
        double outerJacketThickness_mm = 0.0,
        WallMaterial? jacketMaterial = null)
    {
        if (thermal.Stations.Length == 0) return 0;
        double sigmaY_inner_cold_Pa = wall.YieldStrengthAt_MPa(293) * 1e6;
        // Z2.10 composite-wall hoop credit. When jacketMaterial is null
        // BUT outerJacketThickness > 0 the jacket is same-alloy as the
        // inner liner (typical pure-material LRE chamber: e.g., all
        // GRCop-42 wall with structural Inconel surround used only for
        // hoop thickness, not yield credit). Use inner σ_y for the
        // jacket contribution so the burst calc matches Sprint G''s
        // multi-wall hoop formula in StructuralCheck.Evaluate
        // (`t_eff = t_inner + t_jacket`). Only when jacketMaterial is
        // explicitly supplied do we credit a different yield strength
        // (true bimetallic, e.g., GRCop-42 liner + IN625 jacket).
        double sigmaY_jacket_cold_Pa = jacketMaterial.HasValue
            ? jacketMaterial.Value.YieldStrengthAt_MPa(293) * 1e6
            : sigmaY_inner_cold_Pa;
        double t_jacket_m = System.Math.Max(outerJacketThickness_mm, 0.0) * 1e-3;
        double jacketHoopCapacity = sigmaY_jacket_cold_Pa * t_jacket_m;   // [Pa·m]

        double P_burst_elastic;
        if (gasSideWallProfile_mm is { Length: > 0 } profile && profile.Length == thermal.Stations.Length)
        {
            P_burst_elastic = double.PositiveInfinity;
            for (int i = 0; i < thermal.Stations.Length; i++)
            {
                double r_i = thermal.Stations[i].R_mm * 1e-3;
                double t_i = profile[i] * 1e-3;
                if (r_i <= 0 || t_i <= 0) continue;
                // Composite hoop: inner-liner stress × t_inner + jacket-stress × t_jacket
                // both sum at the same circumferential strain (Sprint G').
                double pBurst_i = (sigmaY_inner_cold_Pa * t_i + jacketHoopCapacity) / r_i;
                if (pBurst_i < P_burst_elastic) P_burst_elastic = pBurst_i;
            }
            if (double.IsPositiveInfinity(P_burst_elastic)) P_burst_elastic = 0;
        }
        else
        {
            double maxR_mm = 0;
            for (int i = 0; i < thermal.Stations.Length; i++)
                if (thermal.Stations[i].R_mm > maxR_mm) maxR_mm = thermal.Stations[i].R_mm;
            double r_m = maxR_mm * 1e-3;
            double t_m = gasSideWallThickness_mm * 1e-3;
            P_burst_elastic = (sigmaY_inner_cold_Pa * t_m + jacketHoopCapacity)
                            / Math.Max(r_m, 1e-6);
        }
        return P_burst_elastic / Math.Max(chamberPressure_MEOP_Pa, 1);
    }

    /// <summary>
    /// ASME BPVC §VIII Div 1 ground-test threshold for elastic burst
    /// margin. The pre-PH-33 2.0× threshold passed hardware that ASME
    /// would flag. 4.0× is the human-rated / man-rated convention; we
    /// don't enforce that floor.
    /// </summary>
    public const double MinBurstMarginFactor = 2.5;

    /// <summary>
    /// Run a cold hydrostatic proof-test structural check. MEOP is taken as
    /// the chamber pressure from operating conditions. Proof pressure is
    /// that × proofFactor (default 1.5 per NASA/STD-5012).
    /// </summary>
    public static ProofTestResult Evaluate(
        RegenSolverOutputs thermal,
        WallMaterial wall,
        double gasSideWallThickness_mm,
        double chamberPressure_MEOP_Pa,
        double proofFactor = 1.5,
        // Sprint feasibility-audit-G' (2026-04-27): optional outer jacket
        // thickness for multi-wall hoop credit during proof test. Real
        // bimetallic chambers have inner liner + outer jacket sharing
        // hoop load; both must survive proof. Default 0 = legacy single-
        // wall behavior.
        double outerJacketThickness_mm = 0.0,
        // Track B (2026-04-27): per-station gas-side wall thickness profile
        // for designs with non-uniform liner thickness (typical use case:
        // RL10-class large-ε engines that thicken the exit station). When
        // null, falls back to the uniform `gasSideWallThickness_mm`.
        double[]? gasSideWallProfile_mm = null)
    {
        var warnings = new List<string>();
        double P_proof = chamberPressure_MEOP_Pa * proofFactor;

        // Clone thermal stations at cold temperature (no temperature gradient).
        // We reuse the existing Evaluate() by synthesizing an outputs bundle with
        // isothermal wall temperatures equal to the test temperature.
        var coldStations = new StationResult[thermal.Stations.Length];
        for (int i = 0; i < thermal.Stations.Length; i++)
        {
            var s = thermal.Stations[i];
            coldStations[i] = s with
            {
                GasSideWallTemp_K = 293,
                CoolantSideWallTemp_K = 293,
                CoolantBulkPressure_Pa = P_proof,
                // Zero thermal gradient by design — both wall temps equal.
                HeatFlux_Wm2 = 0,
                AxialConductionFlux_Wm2 = 0,
                EffectiveRecoveryTemp_K = 293,
                FilmEffectiveness = 0,
            };
        }
        var coldThermal = thermal with
        {
            Stations = coldStations,
            PeakGasSideWallT_K = 293,
            PeakCoolantSideWallT_K = 293,
            WallTempExceedsLimit = false,
        };

        // Proof testing is performed cold — no hot gas, no isentropic flow.
        // Pass gasGamma: 0.0 explicitly to activate the constant-Pc path.
        // Track B: forward the per-station wall profile when provided.
        var coldStruct = StructuralCheck.Evaluate(
            coldThermal, wall, gasSideWallThickness_mm, P_proof,
            gasGamma: 0.0,
            outerJacketThickness_mm: outerJacketThickness_mm,
            gasSideWallProfile_mm: gasSideWallProfile_mm);

        bool passes = coldStruct.MinSafetyFactor >= 1.0 && !coldStruct.YieldExceeded;
        if (!passes)
            warnings.Add($"Proof test fails — min SF {coldStruct.MinSafetyFactor:F2} < 1.0 at "
                       + $"{P_proof / 1e6:F1} MPa ({proofFactor:F2}× MEOP).");

        // Elastic-burst estimate: find min pressure at which SF drops to 1.0.
        // σ_hoop = P · r / t. With a per-station profile, evaluate burst at
        // every station and take the minimum (worst case). Without the
        // profile, fall back to the legacy worst-r + scalar-t formula —
        // bit-identical for uniform-thickness designs. Z2.8 follow-on
        // (2026-04-28): factored out into ComputeBurstMarginFactor for
        // reuse by the BURST_MARGIN_INSUFFICIENT feasibility gate.
        double burstMargin = ComputeBurstMarginFactor(
            thermal, wall, gasSideWallThickness_mm,
            chamberPressure_MEOP_Pa, gasSideWallProfile_mm);
        double P_burst_elastic = burstMargin * Math.Max(chamberPressure_MEOP_Pa, 1);

        // PH-33 (2026-04-27): warning threshold raised 2.0 → 2.5× MEOP to
        // align with ASME BPVC §VIII Div 1 ground-test convention. The
        // pre-PH-33 2.0× threshold passed hardware that ASME would flag.
        // 4.0× is the human-rated / man-rated one-off convention; we don't
        // enforce that floor here, but the warning above 2.5× is cheap and
        // catches under-spec'd hardware before hot-fire.
        if (burstMargin < MinBurstMarginFactor)
            warnings.Add($"Elastic burst margin only {burstMargin:F2}× MEOP — "
                       + $"below the {MinBurstMarginFactor:F1}× ASME BPVC §VIII Div 1 ground-test threshold. "
                       + "Increase wall thickness or re-check material strength.");

        return new ProofTestResult(
            ProofPressure_Pa: P_proof,
            ProofFactor: proofFactor,
            ColdStructure: coldStruct,
            ElasticBurstPressure_Pa: P_burst_elastic,
            BurstMarginFactor: burstMargin,
            Passes: passes,
            Warnings: warnings.ToArray());
    }
}
