// SteamTurbineCycleSolverTests.cs — Rankine-cycle steam turbine unit tests.

using System;
using System.IO;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.IO;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class SteamTurbineCycleSolverTests
{
    // ── factory helpers ───────────────────────────────────────────────────────

    private static AirbreathingEngineDesign Design(
        double pBoil_bar = 60.0,
        double pCond_bar = 0.04,
        double dT_sup    = 0.0)
        => new(
            Kind:                    AirbreathingEngineKind.SteamTurbine,
            InletThroatArea_m2:      0.05,
            CombustorArea_m2:        1.0,    // proxy for steam mass flow
            CombustorLength_m:       1.0,
            NozzleThroatArea_m2:     0.05,
            NozzleExitArea_m2:       0.10,
            EquivalenceRatio:        0.5,
            CompressorPressureRatio: 1.0)
        {
            SteamBoilerPressure_bar    = pBoil_bar,
            SteamCondensePressure_bar  = pCond_bar,
            SteamSuperheatDeltaT_K     = dT_sup,
        };

    private static FlightConditions Cond()
        => new(0.0, 0.001, AirbreathingFuel.Jp8);

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"steam-test-{Guid.NewGuid():N}.json");

    // ── 1. Thermal efficiency at 60 bar / 0.04 bar with 100 K superheat ─────────

    [Fact]
    public void ThermalEfficiency_WithSuperheat_IsInExpectedBand()
    {
        var solver = new SteamTurbineCycleSolver();
        var r = solver.Solve(Design(dT_sup: 100.0), Cond());
        double etaTh = r.ThermalEfficiency;
        Assert.True(etaTh >= 0.15 && etaTh <= 0.50,
            $"η_th = {etaTh:P1} outside expected 15–50% band at 60 bar / 0.04 bar + 100 K superheat.");
    }

    // ── 2. Superheat ΔT=100 K increases η_th vs no superheat ─────────────────

    [Fact]
    public void Superheat_100K_IncreasesEfficiency()
    {
        var solver = new SteamTurbineCycleSolver();
        var noSup  = solver.Solve(Design(dT_sup: 0.0),   Cond());
        var withSup = solver.Solve(Design(dT_sup: 100.0), Cond());
        Assert.True(withSup.ThermalEfficiency > noSup.ThermalEfficiency,
            $"Superheat should raise η_th: no-sup={noSup.ThermalEfficiency:P2}, "
          + $"with-sup={withSup.ThermalEfficiency:P2}");
    }

    // ── 3. Higher boiler pressure → higher η_th ───────────────────────────────

    [Fact]
    public void HigherBoilerPressure_IncreasesEfficiency()
    {
        var solver = new SteamTurbineCycleSolver();
        var lo = solver.Solve(Design(pBoil_bar: 20.0), Cond());
        var hi = solver.Solve(Design(pBoil_bar: 80.0), Cond());
        Assert.True(hi.ThermalEfficiency > lo.ThermalEfficiency,
            $"Higher boiler P should raise η_th: 20 bar={lo.ThermalEfficiency:P2}, "
          + $"80 bar={hi.ThermalEfficiency:P2}");
    }

    // ── 4. Lower condenser pressure → higher η_th ────────────────────────────

    [Fact]
    public void LowerCondenserPressure_IncreasesEfficiency()
    {
        var solver = new SteamTurbineCycleSolver();
        var hi = solver.Solve(Design(pCond_bar: 0.10), Cond());
        var lo = solver.Solve(Design(pCond_bar: 0.04), Cond());
        Assert.True(lo.ThermalEfficiency > hi.ThermalEfficiency,
            $"Lower condenser P should raise η_th: 0.10 bar={hi.ThermalEfficiency:P2}, "
          + $"0.04 bar={lo.ThermalEfficiency:P2}");
    }

    // ── 5. T_sat(boiler) matches Antoine equation ─────────────────────────────

    [Fact]
    public void TSat_MatchesAntoineCurve_AtBoilerPressure()
    {
        double P_Pa = 60.0 * 1e5;  // 60 bar
        double expected = SteamTurbineCycleSolver.T_sat(P_Pa);
        // Antoine inverse: T = 2061.6 / (5.526 - log10(P / 101325))
        double logRatio = Math.Log10(P_Pa / 101_325.0);
        double manual   = 2061.6 / (5.526 - logRatio);
        Assert.Equal(manual, expected, 9);
        // Should be ~549 K (276 °C) at 60 bar
        Assert.True(expected > 530.0 && expected < 570.0,
            $"T_sat at 60 bar = {expected:F1} K (expected ~549 K)");
    }

    // ── 6. Latent heat at 100°C ≈ 2.257 MJ/kg (Watson anchor) ───────────────

    [Fact]
    public void LatentHeat_AtBoilingPoint_MatchesWatsonAnchor()
    {
        double dh = SteamTurbineCycleSolver.Dh_vap(373.15);
        // Watson gives exactly 2.257e6 J/kg when T = T_crit - 274.15 = 373.15 K
        Assert.Equal(2.257e6, dh, 0);   // within 1 J/kg
    }

    // ── 7. STEAM_CONDENSE_BELOW_VACUUM fires at P_cond = 0.005 bar ───────────

    [Fact]
    public void SteamCondenseBelowVacuum_Fires_AtSubFloorPressure()
    {
        var result = AirbreathingOptimization.GenerateWith(
            Design(pCond_bar: 0.005), Cond());
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "STEAM_CONDENSE_BELOW_VACUUM");
    }

    // ── 8. STEAM_CONDENSE_BELOW_VACUUM does not fire at P_cond = 0.04 bar ────

    [Fact]
    public void SteamCondenseBelowVacuum_DoesNotFire_AtAcceptablePressure()
    {
        var result = AirbreathingOptimization.GenerateWith(
            Design(pCond_bar: 0.04), Cond());
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "STEAM_CONDENSE_BELOW_VACUUM");
    }

    // ── 9. Schema v4→v5 round-trip preserves steam fields ────────────────────

    [Fact]
    public void Schema_RoundTrip_SteamFields_Preserved()
    {
        var design = Design(pBoil_bar: 60.0, pCond_bar: 0.04, dT_sup: 50.0);
        var path   = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(design, Cond(), path);
            var (loaded, _) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.Equal(60.0, loaded.SteamBoilerPressure_bar,    9);
            Assert.Equal(0.04, loaded.SteamCondensePressure_bar,  9);
            Assert.Equal(50.0, loaded.SteamSuperheatDeltaT_K,     9);
        }
        finally { File.Delete(path); }
    }

    // ── 10. SteamTurbine kind persists and loads correctly ────────────────────

    [Fact]
    public void Schema_RoundTrip_SteamTurbineKind_Preserved()
    {
        var design = Design();
        var path   = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(design, Cond(), path);
            var (loaded, _) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.Equal(AirbreathingEngineKind.SteamTurbine, loaded.Kind);
        }
        finally { File.Delete(path); }
    }

    // ── 11. Solver reports positive shaft power ───────────────────────────────

    [Fact]
    public void ShaftPower_IsPositive_AtBaselineDesign()
    {
        var solver = new SteamTurbineCycleSolver();
        var r = solver.Solve(Design(), Cond());
        Assert.True(r.ShaftPower_W > 0,
            $"Expected positive shaft power; got {r.ShaftPower_W:F0} W");
    }

    // ── 12. Station 9 (turbine exit) T is below station 3 (boiler exit) T ────

    [Fact]
    public void TurbineExit_Station9_CoolerThanBoilerExit_Station3()
    {
        var solver = new SteamTurbineCycleSolver();
        var r = solver.Solve(Design(), Cond());
        double T3 = r.Stations.Station(3).StagnationT_K;
        double T9 = r.Stations.Station(9).StagnationT_K;
        Assert.True(T9 < T3,
            $"Turbine exit T9 = {T9:F1} K should be below boiler exit T3 = {T3:F1} K");
    }

    // ── 13. Kind property ──────────────────────────────────────────────────────

    [Fact]
    public void Kind_IsSteamTurbine()
    {
        Assert.Equal(AirbreathingEngineKind.SteamTurbine, new SteamTurbineCycleSolver().Kind);
    }

    // ── 14. Solver rejects wrong Kind ──────────────────────────────────────────

    [Fact]
    public void Solve_RejectsNonSteamTurbineDesign()
    {
        var solver = new SteamTurbineCycleSolver();
        var wrong = Design() with { Kind = AirbreathingEngineKind.GasTurbine };
        Assert.Throws<ArgumentException>(() => solver.Solve(wrong, Cond()));
    }
}
