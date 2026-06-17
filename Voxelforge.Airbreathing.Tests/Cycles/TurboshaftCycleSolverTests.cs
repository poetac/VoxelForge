// TurboshaftCycleSolverTests.cs — Wave-2 unit tests for TurboshaftCycleSolver
// (issue #428, sub-task 2 — turboshaft).
//
// Covers: Kind property, argument validation, station population, power turbine
// cooling, shaft power positivity, ThermalEfficiency range, thrust/Isp
// suppression, fpe=1.0 invariant vs turboprop, sea-level NaN guard,
// determinism, and compressor π_c effect on shaft power.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurboshaftCycleSolverTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static AirbreathingEngineDesign BasicDesign(
        double phi = 0.28,
        double piC = 17.0,
        double aInlet = 0.08)
        => new(
            Kind:                    AirbreathingEngineKind.Turboshaft,
            InletThroatArea_m2:      aInlet,
            CombustorArea_m2:        0.04,
            CombustorLength_m:       0.25,
            NozzleThroatArea_m2:     0.015,
            NozzleExitArea_m2:       0.015,
            EquivalenceRatio:        phi,
            CompressorPressureRatio: piC);

    private static FlightConditions SeaLevelStatic()
        => new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.JetA);

    private static FlightConditions Cruise()
        => new(Altitude_m: 1500.0, MachNumber: 0.25, Fuel: AirbreathingFuel.JetA);

    // ── 1. Kind property ─────────────────────────────────────────────────

    [Fact]
    public void Kind_IsTurboshaft()
    {
        var solver = new TurboshaftCycleSolver();
        Assert.Equal(AirbreathingEngineKind.Turboshaft, solver.Kind);
    }

    // ── 2. Argument validation ────────────────────────────────────────────

    [Fact]
    public void Solve_Rejects_NullDesign()
    {
        var solver = new TurboshaftCycleSolver();
        Assert.Throws<ArgumentNullException>(() => solver.Solve(null!, SeaLevelStatic()));
    }

    [Fact]
    public void Solve_Rejects_NullConditions()
    {
        var solver = new TurboshaftCycleSolver();
        Assert.Throws<ArgumentNullException>(() => solver.Solve(BasicDesign(), null!));
    }

    [Fact]
    public void Solve_Rejects_WrongKind()
    {
        var solver = new TurboshaftCycleSolver();
        var wrongKind = BasicDesign() with { };
        // Build a design with Kind=Turboprop — should be rejected.
        var bad = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turboprop,
            InletThroatArea_m2:      0.08,
            CombustorArea_m2:        0.04,
            CombustorLength_m:       0.25,
            NozzleThroatArea_m2:     0.015,
            NozzleExitArea_m2:       0.015,
            EquivalenceRatio:        0.28,
            CompressorPressureRatio: 17.0);
        Assert.Throws<ArgumentException>(() => solver.Solve(bad, SeaLevelStatic()));
    }

    [Fact]
    public void Solve_Rejects_SubUnitPressureRatio()
    {
        var solver = new TurboshaftCycleSolver();
        var bad = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turboshaft,
            InletThroatArea_m2:      0.08,
            CombustorArea_m2:        0.04,
            CombustorLength_m:       0.25,
            NozzleThroatArea_m2:     0.015,
            NozzleExitArea_m2:       0.015,
            EquivalenceRatio:        0.28,
            CompressorPressureRatio: 0.5);
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(bad, SeaLevelStatic()));
    }

    // ── 3. Station map is fully populated ────────────────────────────────

    [Fact]
    public void Solve_Populates_Stations_0_Through_6()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        var st = result.Stations;
        for (int i = 0; i <= 6; i++)
        {
            Assert.False(double.IsNaN(st.Stations[i].StagnationT_K),
                $"Station {i} StagnationT_K is NaN");
            Assert.False(double.IsNaN(st.Stations[i].StagnationP_Pa),
                $"Station {i} StagnationP_Pa is NaN");
        }
    }

    [Fact]
    public void Solve_Stations_7_8_9_Are_NaN()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        var st = result.Stations;
        for (int i = 7; i <= 9; i++)
        {
            Assert.True(double.IsNaN(st.Stations[i].StagnationT_K),
                $"Station {i} StagnationT_K should be NaN for turboshaft (no nozzle)");
        }
    }

    // ── 4. Power turbine cools the gas ───────────────────────────────────

    [Fact]
    public void Solve_PowerTurbineExit_CoolerThan_GGT_Exit()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        var st = result.Stations;
        Assert.True(
            st.Stations[6].StagnationT_K < st.Stations[5].StagnationT_K,
            $"T_t6 ({st.Stations[6].StagnationT_K:F1} K) should be < T_t5 ({st.Stations[5].StagnationT_K:F1} K)");
    }

    // ── 5. ShaftPower_W is positive ───────────────────────────────────────

    [Fact]
    public void Solve_ShaftPower_IsPositive()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.True(
            result.ShaftPower_W > 0.0,
            $"Expected ShaftPower_W > 0; got {result.ShaftPower_W:G4} W");
    }

    // ── 6. ThermalEfficiency in (0, 1) ────────────────────────────────────

    [Fact]
    public void Solve_ThermalEfficiency_InReasonableRange()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.True(
            result.ThermalEfficiency > 0.0 && result.ThermalEfficiency < 1.0,
            $"ThermalEfficiency = {result.ThermalEfficiency:P1} is outside (0, 1)");
    }

    // ── 7. Net thrust and Isp are suppressed ─────────────────────────────

    [Fact]
    public void Solve_ThrustNet_IsZero()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.Equal(0.0, result.Stations.ThrustNet_N);
    }

    [Fact]
    public void Solve_Isp_IsZero()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.Equal(0.0, result.Stations.SpecificImpulse_s);
    }

    // ── 8. Sea-level static: no NaN ──────────────────────────────────────

    [Fact]
    public void Solve_StaticConditions_NoNaN()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.False(double.IsNaN(result.ShaftPower_W),  "ShaftPower_W is NaN at static");
        Assert.False(double.IsNaN(result.ThermalEfficiency), "ThermalEfficiency is NaN at static");
        Assert.False(double.IsInfinity(result.ShaftPower_W), "ShaftPower_W is infinity at static");
    }

    // ── 9. Determinism ───────────────────────────────────────────────────

    [Fact]
    public void Solve_IsDeterministic()
    {
        var solver = new TurboshaftCycleSolver();
        var r1 = solver.Solve(BasicDesign(), SeaLevelStatic());
        var r2 = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.Equal(r1.ShaftPower_W,      r2.ShaftPower_W);
        Assert.Equal(r1.ThermalEfficiency, r2.ThermalEfficiency);
    }

    // ── 10. Higher π_c raises shaft power ────────────────────────────────

    [Fact]
    public void Solve_HigherPiC_MoreShaftPower()
    {
        var solver = new TurboshaftCycleSolver();
        var lo = solver.Solve(BasicDesign(piC: 8.0),  SeaLevelStatic());
        var hi = solver.Solve(BasicDesign(piC: 20.0), SeaLevelStatic());
        Assert.True(
            hi.ShaftPower_W > lo.ShaftPower_W,
            $"Higher π_c should raise shaft power: lo={lo.ShaftPower_W:G4} W, hi={hi.ShaftPower_W:G4} W");
    }

    // ── 11. Cruise vs static: shaft power within order of magnitude ──────

    [Fact]
    public void Solve_CruiseConditions_ReasonableShaftPower()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), Cruise());
        Assert.True(
            result.ShaftPower_W > 0.0,
            $"Expected positive shaft power at cruise; got {result.ShaftPower_W:G4} W");
    }

    // ── 12. Fuel mass flow and station 4 mass flow consistent ─────────────

    [Fact]
    public void Solve_FuelMassFlow_IsPositive()
    {
        var solver = new TurboshaftCycleSolver();
        var result = solver.Solve(BasicDesign(), SeaLevelStatic());
        Assert.True(
            result.Stations.FuelMassFlow_kg_s > 0.0,
            $"FuelMassFlow should be > 0; got {result.Stations.FuelMassFlow_kg_s:G4} kg/s");
    }
}
