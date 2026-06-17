// ScramjetCycleSolverTests.cs — Sprint A10 unit tests for the
// scramjet cycle solver.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class ScramjetCycleSolverTests
{
    // ── Fixture helpers ──────────────────────────────────────────────────

    private static AirbreathingEngineDesign ReferenceDesign(
        double phi           = 0.60,
        double aInlet        = 0.20,
        double aCombustor    = 0.30,
        double lCombustor    = 1.50,
        double aNozzleThroat = 0.25,
        double aNozzleExit   = 1.00,
        double lIsolator     = 0.80)
        => new(
            Kind:                    AirbreathingEngineKind.Scramjet,
            InletThroatArea_m2:      aInlet,
            CombustorArea_m2:        aCombustor,
            CombustorLength_m:       lCombustor,
            NozzleThroatArea_m2:     aNozzleThroat,
            NozzleExitArea_m2:       aNozzleExit,
            EquivalenceRatio:        phi,
            IsolatorLength_m:        lIsolator);

    private static FlightConditions ReferenceCond(
        double mach      = 8.0,
        double altitude  = 25_000.0)
        => new(altitude, mach, AirbreathingFuel.H2);

    // ── Basic contract tests ─────────────────────────────────────────────

    [Fact]
    public void Kind_IsScramjet()
    {
        Assert.Equal(AirbreathingEngineKind.Scramjet, new ScramjetCycleSolver().Kind);
    }

    [Fact]
    public void Solve_RejectsNonScramjetDesign()
    {
        var solver = new ScramjetCycleSolver();
        var design = ReferenceDesign() with { Kind = AirbreathingEngineKind.Ramjet };
        Assert.Throws<ArgumentException>(() => solver.Solve(design, ReferenceCond()));
    }

    [Fact]
    public void Solve_RejectsBelowMinimumMach()
    {
        var solver = new ScramjetCycleSolver();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => solver.Solve(ReferenceDesign(), ReferenceCond(mach: 2.0)));
    }

    [Fact]
    public void Solve_PopulatesAllTenStations()
    {
        var result = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond());
        Assert.Equal(10, result.Stations.Stations.Count);
    }

    [Fact]
    public void Solve_Stations6And7_AreDegenerate()
    {
        var result = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond());
        Assert.Equal(0.0, result.Stations.Station(6).MassFlow_kg_s);
        Assert.Equal(0.0, result.Stations.Station(7).MassFlow_kg_s);
        Assert.True(double.IsNaN(result.Stations.Station(6).StagnationT_K));
        Assert.True(double.IsNaN(result.Stations.Station(7).StagnationT_K));
    }

    // ── Thermodynamic invariants ─────────────────────────────────────────

    [Fact]
    public void Solve_StagnationTemperature_MonotoneIncreasing()
    {
        // Adiabatic inlet + isolator → T_t0 = T_t1 = T_t2 = T_t3;
        // combustor adds heat so T_t4 > T_t3; nozzle is adiabatic so T_t9 = T_t4.
        var r = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond());
        double T0 = r.Stations.Station(0).StagnationT_K;
        double T2 = r.Stations.Station(2).StagnationT_K;
        double T3 = r.Stations.Station(3).StagnationT_K;
        double T4 = r.Stations.Station(4).StagnationT_K;
        double T9 = r.Stations.Station(9).StagnationT_K;

        Assert.Equal(T0, T2, precision: 6);    // adiabatic inlet
        Assert.Equal(T2, T3, precision: 6);    // adiabatic isolator
        Assert.True(T4 > T3, $"T_t4 {T4:F1} K should exceed T_t3 {T3:F1} K");
        Assert.Equal(T4, T9, precision: 6);    // adiabatic nozzle
    }

    [Fact]
    public void Solve_StagnationPressure_MonotoneDecreasing()
    {
        var r = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond());
        double P0 = r.Stations.Station(0).StagnationP_Pa;
        double P2 = r.Stations.Station(2).StagnationP_Pa;
        double P3 = r.Stations.Station(3).StagnationP_Pa;
        double P4 = r.Stations.Station(4).StagnationP_Pa;
        double P9 = r.Stations.Station(9).StagnationP_Pa;

        Assert.True(P0 > P2, $"P_t0 {P0:E3} > P_t2 {P2:E3} (oblique-shock loss)");
        Assert.True(P2 > P3, $"P_t2 {P2:E3} > P_t3 {P3:E3} (isolator loss)");
        Assert.True(P3 > P4, $"P_t3 {P3:E3} > P_t4 {P4:E3} (Rayleigh loss)");
        Assert.True(P4 > P9, $"P_t4 {P4:E3} > P_t9 {P9:E3} (nozzle loss)");
    }

    [Fact]
    public void Solve_CombustorExitMach_SupersonicAtDesignPoint()
    {
        var r = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond());
        double M4 = r.Stations.Station(4).MachNumber;
        Assert.True(M4 > 1.0, $"M_4 = {M4:F3} should be > 1 (scramjet stays supersonic)");
    }

    // ── Performance acceptance ───────────────────────────────────────────

    [Fact]
    public void Solve_MattinglyReferenceDesign_PositiveThrustAndIspInRange()
    {
        // Mattingly §17 scramjet reference: M_∞ = 8, 25 km, H2, φ = 0.6.
        // Expected: F_net > 0, Isp ∈ [800, 5000] s.
        // Upper bound is generous because H2-fuelled scramjets with
        // constant-property ideal-cycle models produce high specific impulse
        // (~3500 s at design point) due to the large energy content of H2
        // (LHV = 120 MJ/kg) relative to the low fuel-air ratio (f ~ 0.017).
        // Real scramjets deliver 1000-4000 s depending on design conservatism;
        // the ideal-cycle model sits toward the upper band.
        var result = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond());
        Assert.True(result.Stations.ThrustNet_N > 0.0,
            $"F_net = {result.Stations.ThrustNet_N:F0} N should be positive");
        Assert.InRange(result.Stations.SpecificImpulse_s, 800.0, 5000.0);
    }

    // ── Sensitivity tests ────────────────────────────────────────────────

    [Fact]
    public void Solve_HigherEquivalenceRatio_ProducesHigherCombustorT()
    {
        var solver = new ScramjetCycleSolver();
        var cond   = ReferenceCond();
        var low    = solver.Solve(ReferenceDesign(phi: 0.40), cond);
        var high   = solver.Solve(ReferenceDesign(phi: 0.80), cond);
        Assert.True(high.Stations.Station(4).StagnationT_K
                  > low.Stations.Station(4).StagnationT_K,
            "Higher φ should produce higher T_t4");
    }

    [Fact]
    public void Solve_HigherAltitude_ProducesLessThrust()
    {
        var solver = new ScramjetCycleSolver();
        var design = ReferenceDesign();
        var low    = solver.Solve(design, ReferenceCond(altitude: 20_000.0));
        var high   = solver.Solve(design, ReferenceCond(altitude: 30_000.0));
        Assert.True(low.Stations.ThrustNet_N > high.Stations.ThrustNet_N,
            "Lower altitude (denser air) should produce more thrust");
    }

    // ── Off-design robustness ────────────────────────────────────────────

    [Fact]
    public void Solve_AtMach4_ProducesValidStationMap()
    {
        var result = new ScramjetCycleSolver().Solve(ReferenceDesign(), ReferenceCond(mach: 4.0));
        Assert.Equal(10, result.Stations.Stations.Count);
        Assert.True(result.Stations.Station(4).StagnationT_K > 0);
    }

    // ── Determinism ──────────────────────────────────────────────────────

    [Fact]
    public void Solve_RepeatedCalls_AreBitIdentical()
    {
        var solver = new ScramjetCycleSolver();
        var design = ReferenceDesign();
        var cond   = ReferenceCond();
        var a = solver.Solve(design, cond);
        var b = solver.Solve(design, cond);
        Assert.Equal(a.Stations.ThrustNet_N,       b.Stations.ThrustNet_N);
        Assert.Equal(a.Stations.SpecificImpulse_s, b.Stations.SpecificImpulse_s);
        Assert.Equal(a.Stations.FuelMassFlow_kg_s, b.Stations.FuelMassFlow_kg_s);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(a.Stations.Station(i).StagnationT_K,
                         b.Stations.Station(i).StagnationT_K);
            Assert.Equal(a.Stations.Station(i).StagnationP_Pa,
                         b.Stations.Station(i).StagnationP_Pa);
        }
    }
}
