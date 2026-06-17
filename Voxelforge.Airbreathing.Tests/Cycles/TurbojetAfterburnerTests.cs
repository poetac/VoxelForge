// TurbojetAfterburnerTests.cs — Wave-2 unit tests for the afterburner
// (reheat) augmentation of the TurbojetCycleSolver (issue #428 sub-task 3).
//
// Covers: afterburner station 7 population, thrust increase vs dry, Isp
// decrease vs dry, fuel flow increase, ThermalEfficiency reference, gate
// interaction, static-condition NaN guard, and determinism.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbojetAfterburnerTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static AirbreathingEngineDesign DryDesign(double piC = 8.0)
        => new(
            Kind:                    AirbreathingEngineKind.Turbojet,
            InletThroatArea_m2:      0.20,
            CombustorArea_m2:        0.10,
            CombustorLength_m:       0.40,
            NozzleThroatArea_m2:     0.08,
            NozzleExitArea_m2:       0.12,
            EquivalenceRatio:        0.32,
            CompressorPressureRatio: piC);

    private static AirbreathingEngineDesign WetDesign(double fAb = 0.025, double piC = 8.0)
        => DryDesign(piC) with
        {
            EnableAfterburner      = true,
            AfterburnerFuelAirRatio = fAb,
        };

    private static FlightConditions SeaLevel()
        => new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.JetA);

    private static FlightConditions Subsonic()
        => new(Altitude_m: 8000.0, MachNumber: 0.80, Fuel: AirbreathingFuel.JetA);

    // ── 1. Dry mode: station 6 and 7 remain NaN ──────────────────────────

    [Fact]
    public void Dry_Stations_6_And_7_Are_NaN()
    {
        var solver = new TurbojetCycleSolver();
        var result = solver.Solve(DryDesign(), SeaLevel());
        Assert.True(double.IsNaN(result.Stations.Station(6).StagnationT_K),
            "Station 6 should be NaN for dry turbojet.");
        Assert.True(double.IsNaN(result.Stations.Station(7).StagnationT_K),
            "Station 7 should be NaN for dry turbojet.");
    }

    // ── 2. Wet mode: station 7 is populated and hotter than station 5 ───

    [Fact]
    public void Wet_Station7_IsPopulated_AndHotterThanStation5()
    {
        var solver = new TurbojetCycleSolver();
        var result = solver.Solve(WetDesign(), SeaLevel());
        double T_t5 = result.Stations.Station(5).StagnationT_K;
        double T_t7 = result.Stations.Station(7).StagnationT_K;
        Assert.False(double.IsNaN(T_t7), "Station 7 should not be NaN when afterburner is enabled.");
        Assert.True(T_t7 > T_t5,
            $"T_t7 ({T_t7:F1} K) should be hotter than T_t5 ({T_t5:F1} K) after afterburner.");
    }

    // ── 3. Wet thrust exceeds dry thrust ─────────────────────────────────

    [Fact]
    public void Wet_Thrust_ExceedsDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry = solver.Solve(DryDesign(), SeaLevel());
        var wet = solver.Solve(WetDesign(), SeaLevel());
        Assert.True(
            wet.Stations.ThrustNet_N > dry.Stations.ThrustNet_N,
            $"Afterburner thrust {wet.Stations.ThrustNet_N:F0} N should exceed dry {dry.Stations.ThrustNet_N:F0} N.");
    }

    // ── 4. Wet Isp is less than dry Isp (more fuel, diminishing specific return) ─

    [Fact]
    public void Wet_Isp_LessThanDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry = solver.Solve(DryDesign(), SeaLevel());
        var wet = solver.Solve(WetDesign(), SeaLevel());
        Assert.True(
            wet.Stations.SpecificImpulse_s < dry.Stations.SpecificImpulse_s,
            $"Wet Isp {wet.Stations.SpecificImpulse_s:F0} s should be less than dry {dry.Stations.SpecificImpulse_s:F0} s.");
    }

    // ── 5. Fuel flow increases with afterburner ───────────────────────────

    [Fact]
    public void Wet_FuelMassFlow_ExceedsDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry = solver.Solve(DryDesign(), SeaLevel());
        var wet = solver.Solve(WetDesign(), SeaLevel());
        Assert.True(
            wet.Stations.FuelMassFlow_kg_s > dry.Stations.FuelMassFlow_kg_s,
            $"Wet fuel flow {wet.Stations.FuelMassFlow_kg_s:F4} kg/s should exceed dry {dry.Stations.FuelMassFlow_kg_s:F4} kg/s.");
    }

    // ── 6. EnableAfterburner=true but f_ab=0 acts like dry ───────────────

    [Fact]
    public void AfterburnerEnabled_ZeroFab_ActsLikeDry()
    {
        var solver = new TurbojetCycleSolver();
        var dry = solver.Solve(DryDesign(), SeaLevel());
        var zeroAb = solver.Solve(WetDesign(fAb: 0.0), SeaLevel());
        // f_ab = 0 → afterburner branch skips → same as dry
        Assert.Equal(dry.Stations.ThrustNet_N, zeroAb.Stations.ThrustNet_N, precision: 3);
        Assert.True(double.IsNaN(zeroAb.Stations.Station(7).StagnationT_K),
            "Station 7 should be NaN when f_ab = 0.");
    }

    // ── 7. Higher f_ab → higher thrust ────────────────────────────────────

    [Fact]
    public void Higher_Fab_HigherThrust()
    {
        var solver = new TurbojetCycleSolver();
        var lo = solver.Solve(WetDesign(fAb: 0.010), SeaLevel());
        var hi = solver.Solve(WetDesign(fAb: 0.030), SeaLevel());
        Assert.True(
            hi.Stations.ThrustNet_N > lo.Stations.ThrustNet_N,
            $"Higher f_ab should yield higher thrust: lo={lo.Stations.ThrustNet_N:F0} N, hi={hi.Stations.ThrustNet_N:F0} N.");
    }

    // ── 8. Sea-level static: no NaN in wet mode ───────────────────────────

    [Fact]
    public void Wet_StaticConditions_NoNaN()
    {
        var solver = new TurbojetCycleSolver();
        var result = solver.Solve(WetDesign(), SeaLevel());
        Assert.False(double.IsNaN(result.Stations.ThrustNet_N),  "ThrustNet_N is NaN (wet static)");
        Assert.False(double.IsNaN(result.Stations.SpecificImpulse_s), "Isp is NaN (wet static)");
        Assert.False(double.IsNaN(result.Stations.Station(7).StagnationT_K), "T_t7 is NaN (wet static)");
        Assert.False(double.IsInfinity(result.Stations.ThrustNet_N), "ThrustNet_N is infinity (wet static)");
    }

    // ── 9. Wet determinism ───────────────────────────────────────────────

    [Fact]
    public void Wet_IsDeterministic()
    {
        var solver = new TurbojetCycleSolver();
        var r1 = solver.Solve(WetDesign(), SeaLevel());
        var r2 = solver.Solve(WetDesign(), SeaLevel());
        Assert.Equal(r1.Stations.ThrustNet_N,       r2.Stations.ThrustNet_N);
        Assert.Equal(r1.Stations.SpecificImpulse_s, r2.Stations.SpecificImpulse_s);
        Assert.Equal(r1.Stations.Station(7).StagnationT_K, r2.Stations.Station(7).StagnationT_K);
    }

    // ── 10. Wet mode at subsonic cruise: positive thrust ─────────────────

    [Fact]
    public void Wet_SubsonicCruise_PositiveThrust()
    {
        var solver = new TurbojetCycleSolver();
        var result = solver.Solve(WetDesign(), Subsonic());
        Assert.True(
            result.Stations.ThrustNet_N > 0.0,
            $"Expected positive thrust at subsonic cruise (wet); got {result.Stations.ThrustNet_N:G4} N.");
    }
}
