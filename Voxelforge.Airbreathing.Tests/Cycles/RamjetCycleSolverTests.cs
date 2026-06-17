// RamjetCycleSolverTests.cs — Sprint A4 unit tests for the ramjet
// cycle solver beyond the integration test in
// AirbreathingValidationTests.MattinglySyntheticRamjet_*.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class RamjetCycleSolverTests
{
    private static AirbreathingEngineDesign Design(double phi = 0.40, double aInlet = 0.10)
        => new(
            Kind: AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2: aInlet,
            CombustorArea_m2: 0.30,
            CombustorLength_m: 0.50,
            NozzleThroatArea_m2: 0.0848,
            NozzleExitArea_m2: 0.20,
            EquivalenceRatio: phi);

    private static FlightConditions Cond(double mach = 2.0, double altitude_m = 12_000.0)
        => new(altitude_m, mach, AirbreathingFuel.H2);

    [Fact]
    public void Kind_IsRamjet()
    {
        var solver = new RamjetCycleSolver();
        Assert.Equal(AirbreathingEngineKind.Ramjet, solver.Kind);
    }

    [Fact]
    public void Solve_RejectsNonRamjetDesign()
    {
        var solver = new RamjetCycleSolver();
        var design = Design() with { Kind = AirbreathingEngineKind.Turbojet };
        Assert.Throws<System.ArgumentException>(() => solver.Solve(design, Cond()));
    }

    [Fact]
    public void Solve_PopulatesAllTenStations()
    {
        var solver = new RamjetCycleSolver();
        var result = solver.Solve(Design(), Cond());
        Assert.Equal(10, result.Stations.Stations.Count);
    }

    [Fact]
    public void Solve_RamjetSkipsCompressorAndAfterburnerStations()
    {
        var solver = new RamjetCycleSolver();
        var result = solver.Solve(Design(), Cond());
        // Stations 3 (compressor exit), 6 + 7 (afterburner) are
        // degenerate for a ramjet — convention is NaN T/P + zero
        // mass flow.
        Assert.Equal(0.0, result.Stations.Station(3).MassFlow_kg_s);
        Assert.Equal(0.0, result.Stations.Station(6).MassFlow_kg_s);
        Assert.Equal(0.0, result.Stations.Station(7).MassFlow_kg_s);
    }

    [Fact]
    public void Solve_StagnationTemperatureRisesAcrossCombustor()
    {
        var solver = new RamjetCycleSolver();
        var result = solver.Solve(Design(), Cond());
        Assert.True(result.Stations.Station(4).StagnationT_K > result.Stations.Station(2).StagnationT_K,
            "Combustor should raise stagnation T from station 2 to station 4");
    }

    [Fact]
    public void Solve_StagnationPressureFallsAcrossCombustor()
    {
        var solver = new RamjetCycleSolver();
        var result = solver.Solve(Design(), Cond());
        Assert.True(result.Stations.Station(4).StagnationP_Pa < result.Stations.Station(2).StagnationP_Pa,
            "Combustor π_b < 1 should drop stagnation P from station 2 to station 4");
    }

    [Fact]
    public void Solve_NozzleStagnationTemperatureMatchesCombustorExit()
    {
        // Adiabatic CD nozzle: T_t9 = T_t4 to numerical precision.
        var solver = new RamjetCycleSolver();
        var result = solver.Solve(Design(), Cond());
        Assert.Equal(result.Stations.Station(4).StagnationT_K,
                     result.Stations.Station(9).StagnationT_K, 6);
    }

    [Fact]
    public void Solve_FuelMassFlowEqualsAirMassFlowTimesF()
    {
        var solver = new RamjetCycleSolver();
        var result = solver.Solve(Design(phi: 0.40), Cond());
        var s0 = result.Stations.Station(0);
        // f = φ · f_st with H2: 0.40 · 0.0291 = 0.01164
        double expectedF = 0.40 * 0.0291;
        double computedF = result.Stations.FuelMassFlow_kg_s / s0.MassFlow_kg_s;
        Assert.Equal(expectedF, computedF, 5);
    }

    [Fact]
    public void Solve_HigherEquivalenceRatioRaisesT_t4()
    {
        var solver = new RamjetCycleSolver();
        var lean = solver.Solve(Design(phi: 0.30), Cond());
        var rich = solver.Solve(Design(phi: 0.60), Cond());
        Assert.True(rich.Stations.Station(4).StagnationT_K > lean.Stations.Station(4).StagnationT_K);
    }

    [Fact]
    public void Solve_HigherAltitudeProducesLowerThrust()
    {
        // Same Mach, lower density at altitude → less captured ṁ_a → less thrust.
        var solver = new RamjetCycleSolver();
        var sl = solver.Solve(Design(), new FlightConditions(0, 2.0, AirbreathingFuel.H2));
        var hi = solver.Solve(Design(), new FlightConditions(20_000.0, 2.0, AirbreathingFuel.H2));
        Assert.True(hi.Stations.ThrustNet_N < sl.Stations.ThrustNet_N,
            $"Thrust at altitude ({hi.Stations.ThrustNet_N:F1} N) should be lower than at sea level ({sl.Stations.ThrustNet_N:F1} N).");
    }

    [Fact]
    public void Solve_DeterministicAcrossCalls()
    {
        // Deterministic invariant — load-bearing for SA optimizer.
        var solver = new RamjetCycleSolver();
        var a = solver.Solve(Design(), Cond());
        var b = solver.Solve(Design(), Cond());
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(a.Stations.Station(i).StagnationT_K, b.Stations.Station(i).StagnationT_K, 12);
            Assert.Equal(a.Stations.Station(i).StagnationP_Pa, b.Stations.Station(i).StagnationP_Pa, 12);
        }
        Assert.Equal(a.Stations.ThrustNet_N, b.Stations.ThrustNet_N, 12);
        Assert.Equal(a.Stations.SpecificImpulse_s, b.Stations.SpecificImpulse_s, 12);
    }
}
