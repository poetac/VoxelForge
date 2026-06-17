// RbccCycleSolverTests.cs — Sprint A11 unit tests for RbccCycleSolver.
//
// Covers: kind assertion, null-guard, wrong-kind rejection, all three
// operating modes (DuctedRocket / Ramjet / Scramjet), physics sanity,
// delegation equivalence, and determinism.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class RbccCycleSolverTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static AirbreathingEngineDesign RbccDesign(
        RbccOperatingMode mode,
        double phi          = 0.55,
        double inletArea    = 0.10,
        double er           = 1.5,
        double isolator     = 0.50)
        => new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Rbcc,
            InletThroatArea_m2:      inletArea,
            CombustorArea_m2:        0.30,
            CombustorLength_m:       0.50,
            NozzleThroatArea_m2:     0.085,
            NozzleExitArea_m2:       0.20,
            EquivalenceRatio:        phi,
            IsolatorLength_m:        isolator,
            RbccMode:                mode,
            EjectorEntrainmentRatio: er);

    private static FlightConditions RamjetCond()
        => new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2);

    private static FlightConditions ScramjetCond()
        => new FlightConditions(25_000.0, 7.0, AirbreathingFuel.H2);

    private static FlightConditions DuctedRocketCond()
        => new FlightConditions(0.0, 0.5, AirbreathingFuel.H2);

    // ── Kind assertion ────────────────────────────────────────────────────

    [Fact]
    public void Kind_IsRbcc()
    {
        var solver = new RbccCycleSolver();
        Assert.Equal(AirbreathingEngineKind.Rbcc, solver.Kind);
    }

    // ── Null guards ───────────────────────────────────────────────────────

    [Fact]
    public void Solve_RejectsNullDesign()
    {
        var solver = new RbccCycleSolver();
        Assert.Throws<ArgumentNullException>(
            () => solver.Solve(null!, RamjetCond()));
    }

    [Fact]
    public void Solve_RejectsNullConditions()
    {
        var solver = new RbccCycleSolver();
        var design = RbccDesign(RbccOperatingMode.Ramjet);
        Assert.Throws<ArgumentNullException>(
            () => solver.Solve(design, null!));
    }

    // ── Wrong-kind rejection ──────────────────────────────────────────────

    [Fact]
    public void Solve_RejectsNonRbccDesign()
    {
        var solver = new RbccCycleSolver();
        var notRbcc = new AirbreathingEngineDesign(
            Kind: AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2: 0.10, CombustorArea_m2: 0.30,
            CombustorLength_m: 0.50, NozzleThroatArea_m2: 0.085,
            NozzleExitArea_m2: 0.20, EquivalenceRatio: 0.55);
        Assert.Throws<ArgumentException>(
            () => solver.Solve(notRbcc, RamjetCond()));
    }

    // ── DuctedRocket mode ─────────────────────────────────────────────────

    [Fact]
    public void DuctedRocket_PopulatesAllTenStations()
    {
        var solver = new RbccCycleSolver();
        var result = solver.Solve(RbccDesign(RbccOperatingMode.DuctedRocket), DuctedRocketCond());

        // Stations 0, 1, 2, 4, 5, 8, 9 should be populated (non-NaN T).
        Assert.False(double.IsNaN(result.Stations.Station(0).StagnationT_K));
        Assert.False(double.IsNaN(result.Stations.Station(1).StagnationT_K));
        Assert.False(double.IsNaN(result.Stations.Station(2).StagnationT_K));
        Assert.False(double.IsNaN(result.Stations.Station(4).StagnationT_K));
        Assert.False(double.IsNaN(result.Stations.Station(5).StagnationT_K));
        Assert.False(double.IsNaN(result.Stations.Station(8).StagnationT_K));
        Assert.False(double.IsNaN(result.Stations.Station(9).StagnationT_K));
        // Stations 3, 6, 7 are degenerate (no compressor, no afterburner).
        Assert.True(double.IsNaN(result.Stations.Station(3).StagnationT_K));
        Assert.True(double.IsNaN(result.Stations.Station(6).StagnationT_K));
        Assert.True(double.IsNaN(result.Stations.Station(7).StagnationT_K));
    }

    [Fact]
    public void DuctedRocket_ThrustPositiveAtSubsonicFlight()
    {
        var solver = new RbccCycleSolver();
        var result = solver.Solve(RbccDesign(RbccOperatingMode.DuctedRocket), DuctedRocketCond());
        Assert.True(result.Stations.ThrustNet_N > 0,
            $"Expected positive net thrust; got {result.Stations.ThrustNet_N:F0} N.");
    }

    [Fact]
    public void DuctedRocket_IspInPhysicallyReasonableRange_1000_to_6000s()
    {
        var solver = new RbccCycleSolver();
        var result = solver.Solve(RbccDesign(RbccOperatingMode.DuctedRocket), DuctedRocketCond());
        double isp = result.Stations.SpecificImpulse_s;
        Assert.True(isp > 1_000 && isp < 6_000,
            $"Ducted-rocket Isp = {isp:F0} s is outside reasonable 1000-6000 s range.");
    }

    [Fact]
    public void DuctedRocket_HigherER_IncreasesThrust()
    {
        var solver = new RbccCycleSolver();
        var cond   = DuctedRocketCond();
        var lowER  = solver.Solve(RbccDesign(RbccOperatingMode.DuctedRocket, er: 0.5), cond);
        var highER = solver.Solve(RbccDesign(RbccOperatingMode.DuctedRocket, er: 2.5), cond);

        // Higher entrainment ratio pulls more secondary air through the
        // ejector duct → larger momentum addition → higher net thrust
        // when V_9 > V_∞.
        Assert.True(highER.Stations.ThrustNet_N > lowER.Stations.ThrustNet_N,
            $"Higher ER should give more thrust: ER=0.5 → {lowER.Stations.ThrustNet_N:F0} N, "
          + $"ER=2.5 → {highER.Stations.ThrustNet_N:F0} N.");
    }

    // ── Ramjet delegation ─────────────────────────────────────────────────

    [Fact]
    public void RamjetMode_StagnationTRisesAcrossCombustor()
    {
        var solver = new RbccCycleSolver();
        var result = solver.Solve(RbccDesign(RbccOperatingMode.Ramjet), RamjetCond());
        var s2 = result.Stations.Station(2);
        var s4 = result.Stations.Station(4);
        Assert.True(s4.StagnationT_K > s2.StagnationT_K,
            $"T_t4 ({s4.StagnationT_K:F0} K) must exceed T_t2 ({s2.StagnationT_K:F0} K).");
    }

    [Fact]
    public void RamjetMode_MatchesDirectRamjetSolverAtSameConditions()
    {
        var rbcc   = new RbccCycleSolver();
        var ramjet = new RamjetCycleSolver();
        var cond   = RamjetCond();

        var rbccDesign = RbccDesign(RbccOperatingMode.Ramjet, phi: 0.45);
        var ramjetDesign = rbccDesign with { Kind = AirbreathingEngineKind.Ramjet };

        var rbccResult   = rbcc.Solve(rbccDesign, cond);
        var ramjetResult = ramjet.Solve(ramjetDesign, cond);

        double ispRbcc   = rbccResult.Stations.SpecificImpulse_s;
        double ispRamjet = ramjetResult.Stations.SpecificImpulse_s;
        Assert.Equal(ispRamjet, ispRbcc, precision: 0);  // identical — pure delegation
    }

    // ── Scramjet delegation ───────────────────────────────────────────────

    [Fact]
    public void ScramjetMode_MatchesDirectScramjetSolverAtSameConditions()
    {
        var rbcc    = new RbccCycleSolver();
        var scramjet = new ScramjetCycleSolver();
        var cond    = ScramjetCond();

        var rbccDesign = RbccDesign(RbccOperatingMode.Scramjet, phi: 0.40, isolator: 0.80);
        var scramjetDesign = rbccDesign with { Kind = AirbreathingEngineKind.Scramjet };

        var rbccResult    = rbcc.Solve(rbccDesign, cond);
        var scramjetResult = scramjet.Solve(scramjetDesign, cond);

        double ispRbcc    = rbccResult.Stations.SpecificImpulse_s;
        double ispScramjet = scramjetResult.Stations.SpecificImpulse_s;
        Assert.Equal(ispScramjet, ispRbcc, precision: 0);  // identical — pure delegation
    }

    [Fact]
    public void ScramjetMode_CombustorTRisesAboveInletT()
    {
        var solver = new RbccCycleSolver();
        var result = solver.Solve(
            RbccDesign(RbccOperatingMode.Scramjet, phi: 0.40, isolator: 0.80),
            ScramjetCond());
        var s2 = result.Stations.Station(2);
        var s4 = result.Stations.Station(4);
        Assert.True(s4.StagnationT_K > s2.StagnationT_K,
            $"Scramjet T_t4 ({s4.StagnationT_K:F0} K) must exceed T_t2 ({s2.StagnationT_K:F0} K).");
    }

    // ── Determinism ───────────────────────────────────────────────────────

    [Fact]
    public void Solve_IsDeterministic_AcrossMultipleCalls()
    {
        var solver = new RbccCycleSolver();
        var design = RbccDesign(RbccOperatingMode.Ramjet);
        var cond   = RamjetCond();

        var r1 = solver.Solve(design, cond);
        var r2 = solver.Solve(design, cond);
        var r3 = solver.Solve(design, cond);

        Assert.Equal(r1.Stations.SpecificImpulse_s, r2.Stations.SpecificImpulse_s);
        Assert.Equal(r1.Stations.SpecificImpulse_s, r3.Stations.SpecificImpulse_s);
        Assert.Equal(r1.Stations.ThrustNet_N,        r2.Stations.ThrustNet_N);
    }
}
