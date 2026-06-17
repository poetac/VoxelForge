// RotatingDetonationCycleSolverTests.cs — Sprint A.W4 unit tests for the
// RDE cycle solver.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class RotatingDetonationCycleSolverTests
{
    private static AirbreathingEngineDesign RdeDesign(
        double pgr = 1.25,
        int waves = 4,
        double d_o = 0.150,
        double d_i = 0.110,
        double length = 0.150,
        double phi = 0.50,
        double aInlet = 0.05)
        => new(
            Kind: AirbreathingEngineKind.RotatingDetonation,
            InletThroatArea_m2:  aInlet,
            CombustorArea_m2:    0.30,
            CombustorLength_m:   0.50,
            NozzleThroatArea_m2: 0.020,
            NozzleExitArea_m2:   0.100,
            EquivalenceRatio:    phi)
        {
            RdePressureGainRatio       = pgr,
            RdeWaveCount               = waves,
            RdeAnnularOuterDiameter_m  = d_o,
            RdeAnnularInnerDiameter_m  = d_i,
            RdeAnnularLength_m         = length,
        };

    private static FlightConditions Cond(double mach = 2.0, double altitude_m = 10_000.0)
        => new(altitude_m, mach, AirbreathingFuel.H2);

    // ── Basic contract ──────────────────────────────────────────────────

    [Fact]
    public void Kind_IsRotatingDetonation()
    {
        var solver = new RotatingDetonationCycleSolver();
        Assert.Equal(AirbreathingEngineKind.RotatingDetonation, solver.Kind);
    }

    [Fact]
    public void Solve_RejectsNonRdeDesign()
    {
        var solver = new RotatingDetonationCycleSolver();
        var ram = RdeDesign() with { Kind = AirbreathingEngineKind.Ramjet };
        Assert.Throws<ArgumentException>(() => solver.Solve(ram, Cond()));
    }

    [Fact]
    public void Solve_NullDesign_Throws()
        => Assert.Throws<ArgumentNullException>(() => new RotatingDetonationCycleSolver().Solve(null!, Cond()));

    [Fact]
    public void Solve_NullConditions_Throws()
        => Assert.Throws<ArgumentNullException>(() => new RotatingDetonationCycleSolver().Solve(RdeDesign(), null!));

    [Fact]
    public void Solve_NonPositivePressureGain_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(pgr: 0.0), Cond()));

    [Fact]
    public void Solve_ZeroWaveCount_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(waves: 0), Cond()));

    [Fact]
    public void Solve_NonPositiveOuterDiameter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(d_o: 0.0), Cond()));

    [Fact]
    public void Solve_InnerDiameterAtOuter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(d_o: 0.10, d_i: 0.10), Cond()));

    [Fact]
    public void Solve_InnerDiameterAboveOuter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(d_o: 0.10, d_i: 0.15), Cond()));

    [Fact]
    public void Solve_NonPositiveLength_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(length: 0.0), Cond()));

    [Fact]
    public void Solve_NonPositivePhi_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotatingDetonationCycleSolver().Solve(RdeDesign(phi: 0.0), Cond()));

    // ── Physics ─────────────────────────────────────────────────────────

    [Fact]
    public void Solve_BaselineProducesPositiveThrust()
    {
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(), Cond());
        Assert.True(r.Stations.ThrustNet_N > 0,
            $"Expected positive thrust; got {r.Stations.ThrustNet_N:F1} N");
    }

    [Fact]
    public void Solve_BaselineProducesFiniteIsp()
    {
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(), Cond());
        Assert.True(r.Stations.SpecificImpulse_s > 0);
        Assert.True(double.IsFinite(r.Stations.SpecificImpulse_s));
    }

    [Fact]
    public void Solve_PressureGainRaisesCombustorPressure()
    {
        // Higher PGR → higher P_t4. Compare PGR=1.10 vs PGR=1.30 at fixed
        // everything else.
        var rLow  = new RotatingDetonationCycleSolver().Solve(RdeDesign(pgr: 1.10), Cond());
        var rHigh = new RotatingDetonationCycleSolver().Solve(RdeDesign(pgr: 1.30), Cond());
        double pLow  = rLow.Stations.Station(4).StagnationP_Pa;
        double pHigh = rHigh.Stations.Station(4).StagnationP_Pa;
        Assert.True(pHigh > pLow);
        // Ratio should be ~1.30/1.10 = 1.182 at the combustor.
        Assert.InRange(pHigh / pLow, 1.15, 1.22);
    }

    [Fact]
    public void Solve_PressureGainRaisesThrust()
    {
        // The defining RDE advantage: higher PGR at the same fuel-air ratio
        // produces more thrust.
        var rLow  = new RotatingDetonationCycleSolver().Solve(RdeDesign(pgr: 1.05), Cond());
        var rHigh = new RotatingDetonationCycleSolver().Solve(RdeDesign(pgr: 1.30), Cond());
        Assert.True(rHigh.Stations.ThrustNet_N > rLow.Stations.ThrustNet_N);
    }

    [Fact]
    public void Solve_StationsArePopulated()
    {
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(), Cond());
        var s = r.Stations;
        Assert.True(s.Station(0).MassFlow_kg_s > 0);
        Assert.True(s.Station(1).MassFlow_kg_s > 0);
        Assert.True(s.Station(2).MassFlow_kg_s > 0);
        // Station 3 (compressor exit) is degenerate.
        Assert.Equal(0.0, s.Station(3).MassFlow_kg_s);
        Assert.True(s.Station(4).MassFlow_kg_s > 0);
        Assert.True(s.Station(8).MassFlow_kg_s > 0);
        Assert.True(s.Station(9).MassFlow_kg_s > 0);
    }

    [Fact]
    public void Solve_CombustorPressureMatchesPGRTimesDiffuserPressure()
    {
        // The defining RDE identity: P_t4 = PGR · P_t2.
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(pgr: 1.25), Cond());
        double Pt2 = r.Stations.Station(2).StagnationP_Pa;
        double Pt4 = r.Stations.Station(4).StagnationP_Pa;
        Assert.Equal(1.25 * Pt2, Pt4, precision: 4);
    }

    [Fact]
    public void Solve_NoTurbomachineryDiagnostics()
    {
        var r = new RotatingDetonationCycleSolver().Solve(RdeDesign(), Cond());
        Assert.Null(r.CompressorDiagnostics);
        Assert.Null(r.TurbineDiagnostics);
    }

    // ── Static helpers ──────────────────────────────────────────────────

    [Fact]
    public void ChapmanJouguetVelocity_H2Air_InClusterBand()
    {
        // For H₂/air at φ=1 with q ≈ 3.5 MJ/kg (LHV·f_stoich·η_b at full
        // mixture energy), V_CJ ≈ √(2·(γ²−1)·q) = √(2·0.69·3.5e6) ≈ 2200 m/s
        // — cluster band 1800–2500 m/s.
        double v = RotatingDetonationCycleSolver.ChapmanJouguetVelocity_ms(3.5e6);
        Assert.InRange(v, 1800.0, 2500.0);
    }

    [Fact]
    public void ChapmanJouguetVelocity_ZeroEnergy_ReturnsZero()
        => Assert.Equal(0.0, RotatingDetonationCycleSolver.ChapmanJouguetVelocity_ms(0.0));

    [Fact]
    public void ChapmanJouguetVelocity_NegativeEnergy_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RotatingDetonationCycleSolver.ChapmanJouguetVelocity_ms(-1.0));

    [Fact]
    public void AnnularArea_KnownGeometry_MatchesClosedForm()
    {
        // D_o=0.150, D_i=0.110: A = π/4·(0.150² − 0.110²) = π/4·(0.0225−0.0121)
        //                       = π/4·0.0104 = 8.17e-3 m².
        double area = RotatingDetonationCycleSolver.AnnularArea_m2(0.150, 0.110);
        Assert.InRange(area, 8.0e-3, 8.3e-3);
    }

    [Fact]
    public void AnnularArea_InnerEqOuter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RotatingDetonationCycleSolver.AnnularArea_m2(0.150, 0.150));

    [Fact]
    public void AnnularArea_InnerAboveOuter_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RotatingDetonationCycleSolver.AnnularArea_m2(0.110, 0.150));
}
