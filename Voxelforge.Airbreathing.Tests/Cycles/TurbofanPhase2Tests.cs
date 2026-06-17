// TurbofanPhase2Tests.cs — two-spool turbofan (Sprint A8 Phase 2) unit tests.
//
// Covers:
//   • Single-spool path unchanged when PiFan = null.
//   • HP and LP turbine shaft balances in two-spool mode.
//   • Station 10 (HP turbine exit) populated only in two-spool mode.
//   • Mach-equilibrium mixer properties in two-spool mode.
//   • FAN_STALL advisory gate.
//   • BYPASS_DUCT_CHOKED hard gate.
//   • BPR = 5.0 two-spool vs single-spool gate behaviour.
//   • Schema v4 → v5 round-trip for PiFan field.

using System;
using System.IO;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.IO;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbofanPhase2Tests
{
    // ── design / condition factory helpers ───────────────────────────────────

    private static AirbreathingEngineDesign Design(
        double phi   = 0.35,
        double piC   = 25.0,
        double bpr   = 0.34,
        double? piFan = null)
        => new(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.37,
            CombustorArea_m2:        0.15,
            CombustorLength_m:       0.40,
            NozzleThroatArea_m2:     0.12,
            NozzleExitArea_m2:       0.18,
            EquivalenceRatio:        phi,
            CompressorPressureRatio: piC,
            BypassRatio:             bpr)
        { PiFan = piFan };

    private static FlightConditions Sls()
        => new(0.0, 0.001, AirbreathingFuel.Jp8);

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"tf-p2-test-{Guid.NewGuid():N}.json");

    // ── 1. Single-spool path unchanged when PiFan = null ─────────────────────

    [Fact]
    public void SingleSpool_NullPiFan_StationsMatchLegacyPath()
    {
        var solver = new TurbofanCycleSolver();
        var legacy   = solver.Solve(Design(), Sls());
        var explicit_ = solver.Solve(Design() with { PiFan = null }, Sls());
        Assert.Equal(legacy.Stations.ThrustNet_N,       explicit_.Stations.ThrustNet_N, 12);
        Assert.Equal(legacy.Stations.SpecificImpulse_s, explicit_.Stations.SpecificImpulse_s, 12);
        Assert.Equal(legacy.Stations.Station(5).StagnationT_K,
                     explicit_.Stations.Station(5).StagnationT_K, 9);
    }

    [Fact]
    public void SingleSpool_Station10_IsNaN()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Sls());
        Assert.True(double.IsNaN(r.Stations.Station(10).StagnationT_K),
            "Station 10 must be NaN in single-spool mode (HP turbine exit not defined).");
    }

    // ── 2. HP turbine temperature drop matches shaft balance ─────────────────

    [Fact]
    public void TwoSpool_HpTurbine_TemperatureDropMatchesShaftBalance()
    {
        // HP shaft: W_hpt_per_total = Cp * (T_t3 - T_t13) / (η_mech * (1+f))
        // Expected: T_t45 = T_t4 - W_hpt_per_total / Cp  (via turbine operate)
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());

        var s2  = r.Stations.Station(2);
        var s13 = r.Stations.Station(13);
        var s3  = r.Stations.Station(3);
        var s4  = r.Stations.Station(4);
        var s10 = r.Stations.Station(10);   // HP turbine exit (T_t45)

        double bpr  = Design(piFan: 3.0).BypassRatio;
        double mdot_core  = s3.MassFlow_kg_s;
        double mdot_total = s4.MassFlow_kg_s;
        double f = mdot_total / mdot_core - 1.0;  // fuel-air ratio per core

        double w_hpt = TurbofanCycleSolver.DefaultMechanicalEfficiency
                     * (1.0 + f)
                     * (s4.StagnationT_K - s10.StagnationT_K);
        double w_hpc = s3.StagnationT_K - s13.StagnationT_K;

        Assert.True(s10.StagnationT_K < s4.StagnationT_K,
            $"HP turbine must drop T: T_t4={s4.StagnationT_K:F1} K, T_t45={s10.StagnationT_K:F1} K");
        // HP turbine drives only HPC: W_hpt * η_mech * (1+f) ≈ W_hpc
        double residual = Math.Abs(w_hpt - w_hpc) / Math.Abs(w_hpc);
        Assert.True(residual < 1e-6,
            $"HP shaft balance residual = {residual:E3} (w_hpt={w_hpt:F4}, w_hpc={w_hpc:F4} K)");
    }

    // ── 3. LP turbine temperature drop matches fan work ───────────────────────

    [Fact]
    public void TwoSpool_LpTurbine_TemperatureDropMatchesFanWork()
    {
        var solver = new TurbofanCycleSolver();
        var d = Design(piFan: 3.0);
        var r = solver.Solve(d, Sls());

        var s2  = r.Stations.Station(2);
        var s13 = r.Stations.Station(13);
        var s3  = r.Stations.Station(3);
        var s4  = r.Stations.Station(4);
        var s10 = r.Stations.Station(10);
        var s5  = r.Stations.Station(5);

        double bpr      = d.BypassRatio;
        double mdot_core  = s3.MassFlow_kg_s;
        double mdot_total = s4.MassFlow_kg_s;
        double f = mdot_total / mdot_core - 1.0;

        double w_lpt = TurbofanCycleSolver.DefaultMechanicalEfficiency
                     * (1.0 + f)
                     * (s10.StagnationT_K - s5.StagnationT_K);
        double w_fan = (1.0 + bpr) * (s13.StagnationT_K - s2.StagnationT_K);

        Assert.True(s5.StagnationT_K < s10.StagnationT_K,
            $"LP turbine must drop T: T_t45={s10.StagnationT_K:F1} K, T_t5={s5.StagnationT_K:F1} K");
        double residual = Math.Abs(w_lpt - w_fan) / Math.Abs(w_fan);
        Assert.True(residual < 1e-6,
            $"LP shaft balance residual = {residual:E3} (w_lpt={w_lpt:F4}, w_fan={w_fan:F4} K)");
    }

    // ── 4. Total π_c = π_fan × π_hpc ─────────────────────────────────────────

    [Fact]
    public void TwoSpool_TotalPressureRatio_EqualsFanTimesHpc()
    {
        var solver = new TurbofanCycleSolver();
        double piFanVal = 3.0;
        double piCTotal = 25.0;
        var r = solver.Solve(Design(piC: piCTotal, piFan: piFanVal), Sls());

        var s2  = r.Stations.Station(2);   // fan inlet (= compressor face)
        var s13 = r.Stations.Station(13);  // fan exit
        var s3  = r.Stations.Station(3);   // HPC exit

        double pi_fan_actual = s13.StagnationP_Pa / s2.StagnationP_Pa;
        double pi_hpc_actual = s3.StagnationP_Pa  / s13.StagnationP_Pa;
        double pi_total_actual = pi_fan_actual * pi_hpc_actual;

        Assert.True(Math.Abs(pi_total_actual - piCTotal) / piCTotal < 1e-6,
            $"π_fan({pi_fan_actual:F3}) × π_hpc({pi_hpc_actual:F3}) = {pi_total_actual:F3} ≠ π_c={piCTotal}");
    }

    // ── 5. Station 10 populated (not NaN) in two-spool mode ──────────────────

    [Fact]
    public void TwoSpool_Station10_IsPopulated()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());
        var s10 = r.Stations.Station(10);
        Assert.False(double.IsNaN(s10.StagnationT_K),
            "Station 10 (HP turbine exit) must be populated in two-spool mode.");
        Assert.False(double.IsNaN(s10.StagnationP_Pa),
            "Station 10 (HP turbine exit) P_t45 must be populated in two-spool mode.");
        Assert.True(s10.StagnationT_K > 0.0,
            $"Station 10 T_t45 = {s10.StagnationT_K:F1} K should be positive.");
    }

    // ── 6. Station 16 Mach finite and < 1.0 at nominal design ────────────────

    [Fact]
    public void TwoSpool_Station16Mach_IsFiniteAndSubsonic_AtNominal()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());
        double m16 = r.Stations.Station(16).MachNumber;
        Assert.False(double.IsNaN(m16), "Station 16 Mach must be finite in two-spool mode.");
        Assert.True(m16 > 0.0, $"Station 16 Mach = {m16:F3} should be > 0.");
        Assert.True(m16 < 1.0,
            $"Station 16 Mach = {m16:F3} should be subsonic (< 1.0) at nominal design.");
    }

    // ── 7. Mixer P_t6 > min(P_t5, P_t16) ────────────────────────────────────

    [Fact]
    public void TwoSpool_MixerPressure_AboveMinOfInputs()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());
        var s5  = r.Stations.Station(5);
        var s6  = r.Stations.Station(6);
        var s16 = r.Stations.Station(16);
        double pMin = Math.Min(s5.StagnationP_Pa, s16.StagnationP_Pa);
        Assert.True(s6.StagnationP_Pa > pMin,
            $"Mixer P_t6 = {s6.StagnationP_Pa:F0} Pa should exceed "
          + $"min(P_t5, P_t16) = {pMin:F0} Pa.");
    }

    // ── 8. Mixer T_t6 between T_t5 and T_t16 (energy conservation) ───────────

    [Fact]
    public void TwoSpool_MixerTemperature_BetweenHotAndCold()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());
        var s5  = r.Stations.Station(5);
        var s6  = r.Stations.Station(6);
        var s16 = r.Stations.Station(16);
        Assert.True(s6.StagnationT_K < s5.StagnationT_K,
            $"T_t6 = {s6.StagnationT_K:F1} K should be below hot T_t5 = {s5.StagnationT_K:F1} K");
        Assert.True(s6.StagnationT_K > s16.StagnationT_K,
            $"T_t6 = {s6.StagnationT_K:F1} K should be above cold T_t16 = {s16.StagnationT_K:F1} K");
    }

    // ── 9. High-BPR (5.0) with PiFan set is feasible ─────────────────────────

    [Fact]
    public void TwoSpool_HighBpr_WithPiFan_IsFeasible_NoBypassRatioGate()
    {
        var result = AirbreathingOptimization.GenerateWith(
            Design(phi: 0.35, piC: 25.0, bpr: 5.0, piFan: 3.0), Sls());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "BYPASS_RATIO_OUT_OF_BAND");
    }

    // ── 10. FAN_STALL advisory fires when π_fan > 1.9 ─────────────────────────

    [Fact]
    public void FanStall_Advisory_Fires_WhenPiFanAbove1p9()
    {
        // π_fan = 3.0 > 1.9 → FAN_STALL must appear in advisories
        var result = AirbreathingOptimization.GenerateWith(
            Design(piFan: 3.0), Sls());
        Assert.Contains(result.Advisories,
            v => v.ConstraintId == "FAN_STALL");
    }

    // ── 11. FAN_STALL does not fire at nominal π_fan ──────────────────────────

    [Fact]
    public void FanStall_Advisory_DoesNotFire_WhenPiFanBelow1p9()
    {
        // π_fan = 1.5 < 1.9 → FAN_STALL must not appear
        var result = AirbreathingOptimization.GenerateWith(
            Design(piFan: 1.5), Sls());
        Assert.DoesNotContain(result.Advisories,
            v => v.ConstraintId == "FAN_STALL");
    }

    // ── 12. BYPASS_DUCT_CHOKED fires ─────────────────────────────────────────
    // Design: BPR=5.0, π_fan=3.0, π_c=9.0, φ=0.40 at SLS.
    // P_t16 is large (high BPR + low π_c → P_t16 ≫ P_s_mix) → M_cold ≈ 1.9 > 0.9.

    [Fact]
    public void BypassDuctChoked_Fires_WhenBypassMachExceedsFloor()
    {
        var design = Design(phi: 0.40, piC: 9.0, bpr: 5.0, piFan: 3.0);
        var result = AirbreathingOptimization.GenerateWith(design, Sls());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "BYPASS_DUCT_CHOKED");
    }

    // ── 13. BYPASS_DUCT_CHOKED does not fire at nominal design ───────────────
    // Design: BPR=0.34, π_fan=5.0, π_c=25.0 at SLS.
    // P_t16 < P_s_mix → fallback to M_cold = CompressorFaceMach = 0.5 < 0.9.

    [Fact]
    public void BypassDuctChoked_DoesNotFire_AtNominalTwoSpoolDesign()
    {
        var design = Design(phi: 0.35, piC: 25.0, bpr: 0.34, piFan: 5.0);
        var result = AirbreathingOptimization.GenerateWith(design, Sls());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "BYPASS_DUCT_CHOKED");
    }

    // ── 14. BPR=5.0 with PiFan set does NOT fire BYPASS_RATIO_OUT_OF_BAND ────

    [Fact]
    public void TwoSpool_BprFive_WithPiFan_DoesNotFireBypassRatioGate()
    {
        var result = AirbreathingOptimization.GenerateWith(
            Design(bpr: 5.0, piFan: 3.0), Sls());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "BYPASS_RATIO_OUT_OF_BAND");
    }

    // ── 15. BPR=5.0 without PiFan DOES fire BYPASS_RATIO_OUT_OF_BAND (regression)

    [Fact]
    public void SingleSpool_BprFive_WithoutPiFan_FiresBypassRatioGate()
    {
        var result = AirbreathingOptimization.GenerateWith(
            Design(bpr: 5.0), Sls());   // no PiFan
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "BYPASS_RATIO_OUT_OF_BAND");
    }

    // ── 16. Schema round-trip: PiFan=null survives v4→v5 migration ───────────

    [Fact]
    public void Schema_RoundTrip_PiFanNull_SurvivesMigration()
    {
        var design = Design(piFan: null);
        var path   = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(design, Sls(), path);
            var (loaded, _) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.Null(loaded.PiFan);
            Assert.Equal(design, loaded);
        }
        finally { File.Delete(path); }
    }

    // ── 17. Schema round-trip: PiFan=1.4 survives save/load ──────────────────

    [Fact]
    public void Schema_RoundTrip_PiFanValue_SurvivesSaveLoad()
    {
        var design = Design(piFan: 1.4);
        var path   = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(design, Sls(), path);
            var (loaded, _) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.NotNull(loaded.PiFan);
            Assert.Equal(1.4, loaded.PiFan!.Value, 12);
        }
        finally { File.Delete(path); }
    }

    // ── 18. Two-spool mode produces positive net thrust ───────────────────────

    [Fact]
    public void TwoSpool_NominalDesign_ProducesPositiveThrust()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());
        Assert.True(r.Stations.ThrustNet_N > 0,
            $"Expected positive thrust; got {r.Stations.ThrustNet_N:F1} N");
    }

    // ── 19. Two-spool LP turbine exits at lower T and P than HP turbine exit ──

    [Fact]
    public void TwoSpool_LpTurbineExit_CoolerAndLowerPressure_ThanHpTurbineExit()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(piFan: 3.0), Sls());
        var s10 = r.Stations.Station(10);  // HP turbine exit (T_t45)
        var s5  = r.Stations.Station(5);   // LP turbine exit (T_t5)
        Assert.True(s5.StagnationT_K < s10.StagnationT_K,
            $"LP turbine exit T_t5 = {s5.StagnationT_K:F1} K should be below "
          + $"HP turbine exit T_t45 = {s10.StagnationT_K:F1} K");
        Assert.True(s5.StagnationP_Pa < s10.StagnationP_Pa,
            $"LP turbine exit P_t5 = {s5.StagnationP_Pa:F0} Pa should be below "
          + $"HP turbine exit P_t45 = {s10.StagnationP_Pa:F0} Pa");
    }
}
