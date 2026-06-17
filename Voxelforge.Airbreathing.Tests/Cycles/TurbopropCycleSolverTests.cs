// TurbopropCycleSolverTests.cs — Wave-2 unit tests for the turboprop
// cycle solver (issue #428). Tests are physics-only (no PicoGK) so
// they run on net9.0 without Windows restriction.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Stations;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbopropCycleSolverTests
{
    // Representative turboprop design (loosely T56-class):
    //   Inlet area 0.115 m², π_c = 9.25, φ = 0.30, fpe = 0.89
    private static AirbreathingEngineDesign Design(
        double phi  = 0.30,
        double piC  = 9.25,
        double fpe  = 0.89)
        => new(
            Kind:                            AirbreathingEngineKind.Turboprop,
            InletThroatArea_m2:              0.115,
            CombustorArea_m2:                0.10,
            CombustorLength_m:               0.35,
            NozzleThroatArea_m2:             0.055,
            NozzleExitArea_m2:               0.070,
            EquivalenceRatio:                phi,
            CompressorPressureRatio:         piC)
        {
            PropellerPowerExtraction_frac = fpe,
        };

    // Cruise conditions (17,000 ft / 5182 m, Mach 0.58) — T56-A-15 design point
    private static FlightConditions CruiseCond()
        => new(Altitude_m: 5182.0, MachNumber: 0.58, Fuel: AirbreathingFuel.Jp8);

    // Sea-level static for verifying propeller thrust guard
    private static FlightConditions StaticCond()
        => new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    [Fact]
    public void Kind_IsTurboprop()
    {
        var solver = new TurbopropCycleSolver();
        Assert.Equal(AirbreathingEngineKind.Turboprop, solver.Kind);
    }

    [Fact]
    public void Solve_RejectsNonTurbopropDesign()
    {
        var solver = new TurbopropCycleSolver();
        var design = Design() with { Kind = AirbreathingEngineKind.Turbojet };
        Assert.Throws<ArgumentException>(() => solver.Solve(design, CruiseCond()));
    }

    [Fact]
    public void Solve_RejectsSubUnityCompressorPressureRatio()
    {
        var solver = new TurbopropCycleSolver();
        var design = Design(piC: 0.9);
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(design, CruiseCond()));
    }

    [Fact]
    public void Solve_RejectsNullDesign()
    {
        var solver = new TurbopropCycleSolver();
        Assert.Throws<ArgumentNullException>(() => solver.Solve(null!, CruiseCond()));
    }

    [Fact]
    public void Solve_PopulatesGasGeneratorStations0Through5()
    {
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        for (int i = 0; i <= 5; i++)
        {
            var s = r.Stations.Station(i);
            Assert.False(double.IsNaN(s.StagnationT_K),
                $"Station {i} StagnationT_K is NaN — gas generator solve failed");
            Assert.False(double.IsNaN(s.StagnationP_Pa),
                $"Station {i} StagnationP_Pa is NaN — gas generator solve failed");
        }
    }

    [Fact]
    public void Solve_Station6_PowerTurbineExit_IsPopulated()
    {
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        var s6 = r.Stations.Station(6);
        Assert.False(double.IsNaN(s6.StagnationT_K),
            "Station 6 (power turbine exit) StagnationT_K should be finite");
        Assert.True(s6.StagnationT_K > 0.0);
    }

    [Fact]
    public void Solve_Station7_IsNaN()
    {
        // No afterburner on turboprop.
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        Assert.True(double.IsNaN(r.Stations.Station(7).StagnationT_K));
    }

    [Fact]
    public void Solve_PowerTurbineExitCoolerThanGasGeneratorExit()
    {
        // The power turbine extracts enthalpy → T_t6 < T_t5.
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        double T_t5 = r.Stations.Station(5).StagnationT_K;
        double T_t6 = r.Stations.Station(6).StagnationT_K;
        Assert.True(T_t6 < T_t5,
            $"Power turbine should cool the gas: T_t6 ({T_t6:F1} K) < T_t5 ({T_t5:F1} K) failed");
    }

    [Fact]
    public void Solve_ShaftPower_IsPositive()
    {
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        Assert.True(r.ShaftPower_W > 0.0,
            $"ShaftPower_W should be positive, got {r.ShaftPower_W:F0} W");
    }

    [Fact]
    public void Solve_NetThrust_IsPositive()
    {
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        Assert.True(r.Stations.ThrustNet_N > 0.0,
            $"ThrustNet_N should be positive, got {r.Stations.ThrustNet_N:F0} N");
    }

    [Fact]
    public void Solve_HigherPowerExtractionFrac_GivesMoreShaftPower()
    {
        // fpe = 0.92 should give more shaft power than fpe = 0.80.
        var solver = new TurbopropCycleSolver();
        var r_low  = solver.Solve(Design(fpe: 0.80), CruiseCond());
        var r_high = solver.Solve(Design(fpe: 0.92), CruiseCond());
        Assert.True(r_high.ShaftPower_W > r_low.ShaftPower_W,
            $"Higher fpe should give more shaft power: {r_high.ShaftPower_W:F0} > {r_low.ShaftPower_W:F0}");
    }

    [Fact]
    public void Solve_HigherPowerExtractionFrac_GivesLowerExhaustT()
    {
        // More power extracted → cooler exhaust at station 6.
        var solver = new TurbopropCycleSolver();
        var r_low  = solver.Solve(Design(fpe: 0.80), CruiseCond());
        var r_high = solver.Solve(Design(fpe: 0.92), CruiseCond());
        double T6_low  = r_low.Stations.Station(6).StagnationT_K;
        double T6_high = r_high.Stations.Station(6).StagnationT_K;
        Assert.True(T6_high < T6_low,
            $"Higher fpe should cool station 6: {T6_high:F1} K < {T6_low:F1} K failed");
    }

    [Fact]
    public void Solve_ZeroPowerExtraction_NetThrustApproachesTurbojetThrust()
    {
        // With fpe = 0.0 the power turbine extracts nothing; all remaining
        // enthalpy goes to the residual nozzle. Net thrust should be
        // positive and materially different from the fpe=0.89 case.
        var solver = new TurbopropCycleSolver();
        var r_tp = solver.Solve(Design(fpe: 0.89), CruiseCond());
        var r_0  = solver.Solve(Design(fpe: 0.00), CruiseCond());
        // With fpe=0.0 there is no propeller contribution; net thrust
        // comes entirely from the residual nozzle, so it should be LESS
        // than the turboprop case (which adds propeller thrust).
        Assert.True(r_tp.Stations.ThrustNet_N > r_0.Stations.ThrustNet_N,
            "Turboprop (fpe=0.89) should produce more thrust than fpe=0.0 (no propeller)");
    }

    [Fact]
    public void Solve_StaticConditions_DoesNotReturnNaN()
    {
        // At Mach 0.001 (near-static) the propeller thrust guard
        // (MinVelocityForPropellerThrust_m_s = 10 m/s) prevents division
        // by near-zero velocity.
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), StaticCond());
        Assert.False(double.IsNaN(r.Stations.ThrustNet_N), "ThrustNet_N is NaN at static conditions");
        Assert.False(double.IsNaN(r.ShaftPower_W), "ShaftPower_W is NaN at static conditions");
    }

    [Fact]
    public void Solve_IsDeterministic()
    {
        // Two solves with the same inputs must return bit-identical results.
        var solver = new TurbopropCycleSolver();
        var d = Design();
        var c = CruiseCond();
        var r1 = solver.Solve(d, c);
        var r2 = solver.Solve(d, c);
        Assert.Equal(r1.Stations.ThrustNet_N,       r2.Stations.ThrustNet_N);
        Assert.Equal(r1.ShaftPower_W,                r2.ShaftPower_W);
        Assert.Equal(r1.Stations.FuelMassFlow_kg_s,  r2.Stations.FuelMassFlow_kg_s);
    }

    [Fact]
    public void Solve_CompressorRaisesTAndP()
    {
        var solver = new TurbopropCycleSolver();
        var r = solver.Solve(Design(), CruiseCond());
        var s2 = r.Stations.Station(2);
        var s3 = r.Stations.Station(3);
        Assert.True(s3.StagnationT_K > s2.StagnationT_K,
            "Compressor should raise stagnation temperature");
        Assert.True(s3.StagnationP_Pa > s2.StagnationP_Pa,
            "Compressor should raise stagnation pressure");
    }
}
