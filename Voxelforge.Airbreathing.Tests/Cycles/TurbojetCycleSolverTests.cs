// TurbojetCycleSolverTests.cs — Sprint A7 unit tests for the turbojet
// cycle solver beyond the integration test in
// AirbreathingValidationTests.J85_*.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbojetCycleSolverTests
{
    private static AirbreathingEngineDesign Design(double phi = 0.22, double piC = 8.0)
        => new(
            Kind:                       AirbreathingEngineKind.Turbojet,
            InletThroatArea_m2:         0.115,
            CombustorArea_m2:           0.10,
            CombustorLength_m:          0.30,
            NozzleThroatArea_m2:        0.060,
            NozzleExitArea_m2:          0.078,
            EquivalenceRatio:           phi,
            CompressorPressureRatio:    piC);

    private static FlightConditions Cond(double mach = 0.001, double alt_m = 0.0)
        => new(alt_m, mach, AirbreathingFuel.Jp8);

    [Fact]
    public void Kind_IsTurbojet()
    {
        var solver = new TurbojetCycleSolver();
        Assert.Equal(AirbreathingEngineKind.Turbojet, solver.Kind);
    }

    [Fact]
    public void Solve_RejectsNonTurbojetDesign()
    {
        var solver = new TurbojetCycleSolver();
        var design = Design() with { Kind = AirbreathingEngineKind.Ramjet };
        Assert.Throws<System.ArgumentException>(() => solver.Solve(design, Cond()));
    }

    [Fact]
    public void Solve_RejectsSubUnityCompressorPressureRatio()
    {
        var solver = new TurbojetCycleSolver();
        var design = Design(piC: 0.9);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => solver.Solve(design, Cond()));
    }

    [Fact]
    public void Solve_PopulatesStations0Through5And8And9()
    {
        var solver = new TurbojetCycleSolver();
        var r = solver.Solve(Design(), Cond());
        for (int i = 0; i <= 5; i++)
            Assert.False(double.IsNaN(r.Stations.Station(i).StagnationT_K),
                $"Station {i} StagnationT_K should be populated for turbojet, got NaN");
        Assert.False(double.IsNaN(r.Stations.Station(8).StagnationT_K));
        Assert.False(double.IsNaN(r.Stations.Station(9).StagnationT_K));
    }

    [Fact]
    public void Solve_AfterburnerStations6And7_AreNaN()
    {
        // Single-spool dry turbojet — no afterburner.
        var solver = new TurbojetCycleSolver();
        var r = solver.Solve(Design(), Cond());
        Assert.True(double.IsNaN(r.Stations.Station(6).StagnationT_K));
        Assert.True(double.IsNaN(r.Stations.Station(7).StagnationT_K));
    }

    [Fact]
    public void Solve_CompressorRaisesTAndP()
    {
        var solver = new TurbojetCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s2 = r.Stations.Station(2);
        var s3 = r.Stations.Station(3);
        Assert.True(s3.StagnationT_K > s2.StagnationT_K, "Compressor should raise T_t");
        Assert.True(s3.StagnationP_Pa > s2.StagnationP_Pa, "Compressor should raise P_t");
    }

    [Fact]
    public void Solve_TurbineDropsTAndP()
    {
        var solver = new TurbojetCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s4 = r.Stations.Station(4);
        var s5 = r.Stations.Station(5);
        Assert.True(s5.StagnationT_K < s4.StagnationT_K, "Turbine should drop T_t");
        Assert.True(s5.StagnationP_Pa < s4.StagnationP_Pa, "Turbine should drop P_t");
    }

    [Fact]
    public void Solve_ShaftBalance_TurbineWorkEqualsCompressorWork()
    {
        // η_mech = 1 + cp(T) routing: shaft balance is in W (J/s) units
        //   ṁ_a · (h_air(T_t3) − h_air(T_t2)) = ṁ_total · (h_burnt(T_t4) − h_burnt(T_t5))
        // (constant-cp ṁ·ΔT equality no longer holds because the
        // compressor side uses cp_air(T) while the turbine side uses
        // cp_burnt_kerosene(T)).
        var solver = new TurbojetCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s2 = r.Stations.Station(2);
        var s3 = r.Stations.Station(3);
        var s4 = r.Stations.Station(4);
        var s5 = r.Stations.Station(5);
        double W_compressor = s2.MassFlow_kg_s
            * (IdealGasAir.EnthalpyAir(s3.StagnationT_K) - IdealGasAir.EnthalpyAir(s2.StagnationT_K));
        double W_turbine = s4.MassFlow_kg_s
            * (IdealGasAir.EnthalpyBurntKerosene(s4.StagnationT_K) - IdealGasAir.EnthalpyBurntKerosene(s5.StagnationT_K));
        // Loose tolerance: enthalpy inversion + linear interp on the
        // 100-K-step cp(T) tables introduces ~10⁻³ relative round-off.
        // Compare in W (J/s) magnitude.
        double rel = System.Math.Abs(W_compressor - W_turbine) / W_compressor;
        Assert.True(rel < 5e-3,
            $"Shaft balance: W_compressor = {W_compressor:E3} W, W_turbine = {W_turbine:E3} W (rel diff {rel:E3}).");
    }

    [Fact]
    public void Solve_DeterministicAcrossCalls()
    {
        var solver = new TurbojetCycleSolver();
        var a = solver.Solve(Design(), Cond());
        var b = solver.Solve(Design(), Cond());
        Assert.Equal(a.Stations.ThrustNet_N, b.Stations.ThrustNet_N, 12);
        Assert.Equal(a.Stations.SpecificImpulse_s, b.Stations.SpecificImpulse_s, 12);
    }

    [Fact]
    public void Solve_HigherCompressorRatio_RaisesT_t3()
    {
        var solver = new TurbojetCycleSolver();
        var lo = solver.Solve(Design(piC: 4.0), Cond());
        var hi = solver.Solve(Design(piC: 16.0), Cond());
        Assert.True(hi.Stations.Station(3).StagnationT_K > lo.Stations.Station(3).StagnationT_K);
    }

    [Fact]
    public void Solve_J85DesignPoint_ProducesPositiveThrust()
    {
        // Sanity check before the validation fixture — at the J85 SLS
        // design point we should get positive thrust + positive Isp.
        var solver = new TurbojetCycleSolver();
        var r = solver.Solve(Design(), Cond());
        Assert.True(r.Stations.ThrustNet_N > 0,
            $"Expected positive thrust at J85 design point; got {r.Stations.ThrustNet_N}");
        Assert.True(r.Stations.SpecificImpulse_s > 0,
            $"Expected positive Isp at J85 design point; got {r.Stations.SpecificImpulse_s}");
    }
}
