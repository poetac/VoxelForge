// PreburnerCooling.cs — Sprint 9 Track B (2026-04-22):
// First-order preburner-wall thermal estimator. High-Pc preburners
// (>15 MPa) on staged-combustion / full-flow cycles see warm-gas
// temperatures of 800–1100 K brushing up against CuCrZr's 800 K
// service limit — without regen cooling the chamber wall runs hot
// enough to shorten fatigue life.
//
// Model fidelity
// ──────────────
// This is a LUMPED-PARAMETER balance, not a full axial march like
// HeatTransfer.RegenCoolingSolver. The preburner is small (typically
// a few hundred mL internal volume; L/D ≈ 2) and the gate-level
// question — "does the wall run past its service limit?" — is
// adequately answered by one representative wall-T estimate. If a
// design lands near the gate limit, a downstream CFD / per-station
// solve is warranted; the regen chamber's RegenCoolingSolver is the
// template for that future work.
//
// Physics
// ───────
//   1. Chamber geometry from PreburnerResult.ChamberVolume_mm3 at
//      L/D = 2 (typical preburner aspect):
//        V = π D² / 4 × 2D = π D³ / 2  ⇒  D = (2V/π)^(1/3)
//   2. Warm-gas Cp from γ + R_specific = 8314 / MW:
//        Cp = γ · R_specific / (γ − 1)
//   3. Hot-side HTC via Bartz-ish chamber proportionality:
//        h_g ≈ C_Bartz · (ṁ / A_chamber) · Cp
//      with C_Bartz = 0.026 (matches regen chamber Bartz scale).
//   4. Recovery: T_aw = WarmGasTemp_K · 0.90 (turbulent Pr^(1/3)
//      constant-approximation used by the regen Bartz path).
//   5. Cold-side h_c via Dittus-Boelter on the channel hydraulic
//      diameter — same correlation InjectorFaceThermal uses for the
//      bore-cooled injector face.
//   6. Wall equilibrium (steady-state single-node balance):
//        h_g · (T_aw − T_wall) = h_c · (T_wall − T_coolant)
//      ⇒ T_wall = (h_g · T_aw + h_c · T_coolant) / (h_g + h_c)
//   7. Total heat load = h_g · (T_aw − T_wall) · A_inner_surface
//   8. Coolant ΔT = Q / (ṁ_coolant · Cp_coolant)
//
// Scope caveats
// ─────────────
//   • Single-station (peak) T_wall; no axial march.
//   • No axial variation in coolant T.
//   • Constant properties (coolant fluid evaluated at inlet state).
//   • No film cooling, no injector-face coupling.
// These simplifications are appropriate for a feasibility gate; they
// are NOT appropriate for thermal design sign-off.

using System;

namespace Voxelforge.HeatTransfer;

/// <summary>
/// Result of <see cref="PreburnerCooling.Solve"/>. Attached to a
/// <see cref="Chamber.PreburnerResult"/> when
/// <see cref="Optimization.RegenChamberDesign.IncludePreburnerRegenCooling"/>
/// is true. Consumed by
/// <see cref="Optimization.FeasibilityGate"/>'s <c>PREBURNER_WALL_TEMP</c>
/// gate.
/// </summary>
public sealed record PreburnerThermalResult(
    double PeakWallT_K,              // equilibrium gas-side wall T
    double TAwCore_K,                 // recovery T of the warm gas
    double CoolantOutletT_K,          // bulk coolant outlet T
    double CoolantPressureDrop_Pa,    // not tracked in MVP — reports 0
    double TotalHeatLoad_W,           // integrated over the chamber walls
    double HGasSide_Wm2K,             // hot-side HTC
    double HCoolantSide_Wm2K,         // cold-side HTC
    double ChamberInnerSurfaceArea_m2,
    double ChannelHydraulicDiameter_mm,
    string[] Warnings);

/// <summary>
/// Sprint 9 Track B (2026-04-22): lumped-parameter preburner-wall
/// thermal estimator. Deterministic, thread-safe, no PicoGK dependency
/// — callable from xUnit.
/// </summary>
public static class PreburnerCooling
{
    /// <summary>
    /// Bartz chamber-scale HTC coefficient. Same value as
    /// <see cref="AerospikeInjectorFaceThermal.BartzChamberScale"/>;
    /// conservative-by-factor-2 on the gas-side convection.
    /// </summary>
    public const double BartzChamberScale = 0.026;

    /// <summary>
    /// Turbulent recovery factor: r = Pr^(1/3) for forced convection on a
    /// flat plate or chamber wall. Z2.9 follow-on (2026-04-28): this
    /// constant is now a fallback only — production callers consult
    /// <see cref="Combustion.PropellantTables.RecoveryFactor(double)"/>
    /// with the actual local Prandtl number. Hardcoded 0.90 corresponds
    /// to Pr ≈ 0.73 (typical hot-gas combustion product) which is right
    /// for LOX/CH4 / LOX/RP-1 but slightly low for LOX/H2 (Pr ≈ 0.58 →
    /// r ≈ 0.83) and high for ammonia / decomposed-N2H4 systems.
    /// External-audit F-5.
    /// </summary>
    public const double RecoveryFactor = 0.90;

    /// <summary>Assumed preburner L/D ratio — typical small LRE preburner.</summary>
    public const double ChamberLengthToDiameter = 2.0;

    /// <summary>
    /// Lower bound for the coolant mass flow. Below this the Dittus-
    /// Boelter correlation is out of validity; emit a warning and
    /// clamp to this value for the h_c computation.
    /// </summary>
    public const double MinCoolantMassFlow_kgs = 0.01;

    /// <summary>
    /// Evaluate the preburner wall thermal state. Consumes a sized
    /// <see cref="Chamber.PreburnerResult"/> + channel geometry +
    /// coolant inlet state + wall material, returns a
    /// <see cref="PreburnerThermalResult"/> the feasibility gate can
    /// compare against the material service limit.
    /// </summary>
    public static PreburnerThermalResult Solve(
        Chamber.PreburnerResult preburner,
        int channelCount,
        double channelWidth_mm,
        double channelDepth_mm,
        double wallThickness_mm,   // hot-side wall between gas + coolant (informational; not used in the MVP balance)
        double coolantMassFlow_kgs,
        double coolantInletT_K,
        double coolantInletP_Pa,
        Coolant.ICoolantFluid coolantFluid,
        WallMaterial wall,
        double gasPrandtl = double.NaN)   // Z2.9 follow-on (2026-04-28): when finite, recovery factor = Pr^(1/3)
    {
        if (preburner is null) throw new ArgumentNullException(nameof(preburner));
        if (coolantFluid is null) throw new ArgumentNullException(nameof(coolantFluid));
        // Z3-m1 sibling for preburner (2026-04-29 — closes #236): wall
        // thickness is now consumed by a 1-D conduction term in the
        // series-resistance balance below. Pre-#236 it was discarded;
        // the lumped energy balance treated the wall as having infinite
        // conductivity. For typical preburner walls (t ≈ 0.8-1.5 mm,
        // GRCop-42 k ≈ 280 W/m·K → R_wall ≈ 3-5 × 10⁻⁶ K·m²/W) the
        // resistance is small but non-negligible at the high heat flux
        // of staged-combustion preburners.

        // Sprint 14 / Track I / P9: pre-size at 4 — see RegenCoolingSolver.
        var warnings = new System.Collections.Generic.List<string>(4);

        // 1. Chamber geometry from volume assuming L/D = 2.
        double V_m3 = preburner.ChamberVolume_mm3 * 1e-9;
        if (V_m3 <= 0)
        {
            warnings.Add("Preburner chamber volume is zero — returning a degenerate thermal result.");
            return EmptyResult(warnings.ToArray());
        }
        // V = π D³ / 2  ⇒  D = (2V/π)^(1/3)
        double D_m = Math.Pow(2 * V_m3 / Math.PI, 1.0 / 3.0);
        double R_m = D_m / 2.0;
        double L_m = ChamberLengthToDiameter * D_m;
        double A_chamber_m2 = Math.PI * R_m * R_m;
        double A_innerSurface_m2 = 2 * Math.PI * R_m * L_m;

        // 2. Warm-gas Cp from γ + molecular weight.
        double R_specific = 8314.0 / Math.Max(preburner.WarmGasMolecularWeight, 1e-6);
        double Cp_gas = preburner.WarmGasGamma * R_specific
                      / Math.Max(preburner.WarmGasGamma - 1.0, 1e-6);

        // 3. Hot-side HTC (Bartz proportionality).
        double rhoU_chamber = preburner.MassFlow_kgs / Math.Max(A_chamber_m2, 1e-9);
        double h_g = BartzChamberScale * rhoU_chamber * Cp_gas;

        // 4. Recovery T. Z2.9 follow-on (2026-04-28): when caller supplies
        // a finite gas Prandtl number, use the textbook turbulent-recovery
        // correlation r = Pr^(1/3) instead of the legacy 0.90 constant. The
        // 0.90 constant corresponds to Pr ≈ 0.73 (typical hot-gas combustion
        // product); LOX/H2 sits closer to Pr ≈ 0.58 → r ≈ 0.83 (about 8 %
        // lower) and LOX/RP-1 close to 0.85 → r ≈ 0.95. External-audit F-5.
        double recoveryFactor = double.IsFinite(gasPrandtl) && gasPrandtl > 0
            ? Combustion.PropellantTables.RecoveryFactor(gasPrandtl)
            : RecoveryFactor;
        double T_aw_K = preburner.WarmGasTemperature_K * recoveryFactor;

        // 5. Cold-side HTC (Dittus-Boelter on channel D_h).
        double mdot_cool = coolantMassFlow_kgs;
        if (mdot_cool < MinCoolantMassFlow_kgs)
        {
            warnings.Add($"Coolant mass flow {mdot_cool:F4} kg/s below "
                       + $"{MinCoolantMassFlow_kgs:F2} kg/s lower bound — "
                       + "h_c estimate clamped to lower-bound flow; real wall "
                       + "may run hotter at the actual flow.");
            mdot_cool = MinCoolantMassFlow_kgs;
        }
        double channelArea_m2 = channelWidth_mm * channelDepth_mm * 1e-6;
        double totalCoolantArea_m2 = Math.Max(channelCount * channelArea_m2, 1e-9);
        // Hydraulic diameter of a rectangular channel: 4·A / P.
        double D_h_m = (channelCount > 0 && channelWidth_mm > 0 && channelDepth_mm > 0)
            ? 2.0 * channelWidth_mm * channelDepth_mm
              / (channelWidth_mm + channelDepth_mm) * 1e-3
            : 1e-3;
        var coolantBulk = coolantFluid.GetState(coolantInletT_K, coolantInletP_Pa);
        double coolantV = mdot_cool / (totalCoolantArea_m2 * Math.Max(coolantBulk.Density_kgm3, 1e-6));
        double Re = coolantBulk.Density_kgm3 * coolantV * D_h_m
                  / Math.Max(coolantBulk.Viscosity_PaS, 1e-8);
        double Pr = Math.Max(coolantBulk.Prandtl, 1e-3);
        double Nu = 0.023 * Math.Pow(Re, 0.8) * Math.Pow(Pr, 0.4);
        double h_c = Nu * coolantBulk.Conductivity_WmK / Math.Max(D_h_m, 1e-6);

        // 6. Wall equilibrium — PH-46 (2026-04-29) one-step Picard on
        // mid-bulk T. Pre-PH-46 the wall T balance used the cold inlet
        // temperature on the coolant side, biasing T_wall low (and
        // therefore under-predicting peak wall T) by ~5-15 % on high-flux
        // preburner cooling paths. The lumped-parameter model from Sprint 9
        // Track B can't march axially, but it can take one Picard step:
        //   (a) seed T_wall from inlet T; (b) compute heat load and ΔT;
        //   (c) re-solve T_wall using T_in + 0.5·ΔT.
        // The two-step form converges in one iteration for the lumped
        // model because the only nonlinearity is the coolant-side T
        // appearing on both sides of the energy balance.
        double cp_cool = Math.Max(coolantBulk.Cp_Jkg, 1e-3);

        // Z3-m1 sibling (2026-04-29 — closes #236): series resistance with
        // wall conduction. R_wall = t / k_wall(T) for pure-material walls;
        // bimetallic walls (LinerFraction > 0) split into R_liner(T_wg) +
        // R_jacket(T_wc) — same per-layer-T pattern shipped for the regen
        // solver in PR #232 (Z3-m1).
        //
        // Preburner walls are thin enough that the log-mean / thin-wall
        // distinction is negligible (t ≪ r); we use thin-wall t/k. The
        // bimetallic per-layer-T evaluation uses the wall's own metadata
        // — no additional plumbing through OperatingConditions needed.
        //
        // Series form (referenced to the inner-wall area):
        //   q = (T_aw − T_bulk) / (1/h_g + R_wall + 1/h_c)
        //   T_wg = T_aw − q/h_g    (peak gas-side wall T — gate target)
        //   T_wc = T_bulk + q/h_c
        double t_wall_m = wallThickness_mm * 1e-3;
        double R_wall_seed = ComputePreburnerWallResistance(
            wall, t_wall_m, T_wg_K: T_aw_K * 0.7, T_wc_K: coolantInletT_K + 50);
        double R_total_seed = 1.0 / Math.Max(h_g, 1e-9)
                            + R_wall_seed
                            + 1.0 / Math.Max(h_c, 1e-9);
        double q_seed_W_per_m2 = (T_aw_K - coolantInletT_K) / R_total_seed;
        double q_seed_W = q_seed_W_per_m2 * A_innerSurface_m2;
        double deltaT_seed = q_seed_W / Math.Max(coolantMassFlow_kgs * cp_cool, 1e-9);
        double T_bulk_mid = coolantInletT_K + 0.5 * deltaT_seed;

        // Refined pass: re-evaluate R_wall with the seed wall temperatures
        // (one-step Picard, matching the existing PH-46 mid-bulk T pattern).
        double T_wg_seed = T_aw_K - q_seed_W_per_m2 / Math.Max(h_g, 1e-9);
        double T_wc_seed = coolantInletT_K + q_seed_W_per_m2 / Math.Max(h_c, 1e-9);
        double R_wall = ComputePreburnerWallResistance(
            wall, t_wall_m, T_wg_K: T_wg_seed, T_wc_K: T_wc_seed);
        double R_total = 1.0 / Math.Max(h_g, 1e-9)
                       + R_wall
                       + 1.0 / Math.Max(h_c, 1e-9);
        double q_per_m2 = (T_aw_K - T_bulk_mid) / R_total;
        double T_wall = T_aw_K - q_per_m2 / Math.Max(h_g, 1e-9);  // peak T_wg

        // 7. Total heat load (consistent with the refined T_wall).
        double q_total_W = q_per_m2 * A_innerSurface_m2;

        // 8. Coolant ΔT (re-evaluated against the refined T_wall).
        double deltaT_K = q_total_W / (coolantMassFlow_kgs * cp_cool);
        double coolantOutlet_K = coolantInletT_K + deltaT_K;

        if (T_wall > wall.MaxServiceTemp_K)
            warnings.Add($"Preburner peak wall T {T_wall:F0} K exceeds "
                       + $"{wall.Name} service limit {wall.MaxServiceTemp_K:F0} K — "
                       + "feasibility gate will fire.");

        return new PreburnerThermalResult(
            PeakWallT_K:                 T_wall,
            TAwCore_K:                   T_aw_K,
            CoolantOutletT_K:            coolantOutlet_K,
            CoolantPressureDrop_Pa:      0.0,   // MVP; not modelled
            TotalHeatLoad_W:             q_total_W,
            HGasSide_Wm2K:               h_g,
            HCoolantSide_Wm2K:           h_c,
            ChamberInnerSurfaceArea_m2:  A_innerSurface_m2,
            ChannelHydraulicDiameter_mm: D_h_m * 1e3,
            Warnings:                    warnings.ToArray());
    }

    private static PreburnerThermalResult EmptyResult(string[] warnings)
        => new(
            PeakWallT_K:                 0,
            TAwCore_K:                   0,
            CoolantOutletT_K:            0,
            CoolantPressureDrop_Pa:      0,
            TotalHeatLoad_W:             0,
            HGasSide_Wm2K:               0,
            HCoolantSide_Wm2K:           0,
            ChamberInnerSurfaceArea_m2:  0,
            ChannelHydraulicDiameter_mm: 0,
            Warnings:                    warnings);

    /// <summary>
    /// Z3-m1 sibling (2026-04-29 — closes #236): wall thermal resistance
    /// per unit inner-wall area for the preburner energy balance. Pure
    /// materials use single-T thin-wall <c>t/k(T_wg)</c>; bimetallic
    /// walls (<see cref="WallMaterial.LinerFraction"/> &gt; 0) split into
    /// per-layer-T contributions analogous to the regen solver's
    /// <c>BimetallicLogMeanResistance_KperWperM2</c> (PR #232) — liner k
    /// at <paramref name="T_wg_K"/>, jacket k at <paramref name="T_wc_K"/>.
    /// Preburner walls are thin enough (t ≪ r) that the log-mean curvature
    /// correction is negligible vs the regen-chamber path.
    /// </summary>
    private static double ComputePreburnerWallResistance(
        WallMaterial wall, double tWall_m, double T_wg_K, double T_wc_K)
    {
        if (tWall_m <= 0) return 0;
        if (wall.LinerFraction > 0)
        {
            double t_liner_m = wall.LinerFraction * tWall_m;
            double t_jacket_m = (1.0 - wall.LinerFraction) * tWall_m;
            double k_liner = Math.Max(WallMaterials.GRCop42.ConductivityAt(T_wg_K), 1.0);
            double k_jacket = Math.Max(WallMaterials.Inconel625.ConductivityAt(T_wc_K), 1.0);
            return t_liner_m / k_liner + t_jacket_m / k_jacket;
        }
        // Pure material: thin-wall t/k(T_wg). Single-T evaluation matches
        // the regen solver's pre-Z3-m1 fallback for non-bimetallic stacks.
        double k = Math.Max(wall.ConductivityAt(T_wg_K), 1.0);
        return tWall_m / k;
    }
}
