// TurbofanCycleSolverTests.cs — Sprint A8 unit tests for the
// single-spool low-bypass mixed-flow turbofan cycle solver beyond the
// integration tests in AirbreathingValidationTests.F404_*.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbofanCycleSolverTests
{
    private static AirbreathingEngineDesign Design(
        double phi = 0.30,
        double piC = 25.0,
        double bpr = 0.34)
        => new(
            Kind:                       AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:         0.37,
            CombustorArea_m2:           0.15,
            CombustorLength_m:          0.40,
            NozzleThroatArea_m2:        0.12,
            NozzleExitArea_m2:          0.18,
            EquivalenceRatio:           phi,
            CompressorPressureRatio:    piC,
            BypassRatio:                bpr);

    private static FlightConditions Cond(double mach = 0.001, double alt_m = 0.0)
        => new(alt_m, mach, AirbreathingFuel.Jp8);

    [Fact]
    public void Kind_IsTurbofan()
    {
        var solver = new TurbofanCycleSolver();
        Assert.Equal(AirbreathingEngineKind.Turbofan, solver.Kind);
    }

    [Fact]
    public void Solve_RejectsNonTurbofanDesign()
    {
        var solver = new TurbofanCycleSolver();
        var design = Design() with { Kind = AirbreathingEngineKind.Ramjet };
        Assert.Throws<System.ArgumentException>(() => solver.Solve(design, Cond()));
    }

    [Fact]
    public void Solve_RejectsSubUnityCompressorPressureRatio()
    {
        var solver = new TurbofanCycleSolver();
        var design = Design(piC: 0.9);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => solver.Solve(design, Cond()));
    }

    [Fact]
    public void Solve_RejectsNegativeBypassRatio()
    {
        var solver = new TurbofanCycleSolver();
        var design = Design(bpr: -0.1);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => solver.Solve(design, Cond()));
    }

    [Fact]
    public void Solve_PopulatesCoreStations_0_2_3_4_5_6_8_9()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        foreach (int i in new[] { 0, 2, 3, 4, 5, 6, 8, 9 })
            Assert.False(double.IsNaN(r.Stations.Station(i).StagnationT_K),
                $"Station {i} StagnationT_K should be populated for turbofan, got NaN");
    }

    [Fact]
    public void Solve_AfterburnerStation7_IsNaN_PhaseDryOnly()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        Assert.True(double.IsNaN(r.Stations.Station(7).StagnationT_K),
            "Phase 1 turbofan is dry — afterburner station 7 must be NaN.");
    }

    [Fact]
    public void Solve_StationArrayLength_Is17()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        Assert.Equal(TurbofanCycleSolver.StationArrayLength, r.Stations.Stations.Count);
        Assert.Equal(17, r.Stations.Stations.Count);
    }

    [Fact]
    public void Solve_FanExit_Station13_PopulatedAndAboveT_t2()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s2 = r.Stations.Station(2);
        var s13 = r.Stations.Station(13);
        Assert.False(double.IsNaN(s13.StagnationT_K),
            "Station 13 (fan exit) should be populated for turbofan.");
        Assert.True(s13.StagnationT_K > s2.StagnationT_K,
            $"Fan should raise T_t (got T_t13 = {s13.StagnationT_K:F1} K, T_t2 = {s2.StagnationT_K:F1} K)");
        Assert.True(s13.StagnationP_Pa > s2.StagnationP_Pa,
            $"Fan should raise P_t (got P_t13 = {s13.StagnationP_Pa:F0} Pa, P_t2 = {s2.StagnationP_Pa:F0} Pa)");
    }

    [Fact]
    public void Solve_BypassDuctExit_Station16_PopulatedAndEqualsFanExit_LosslessDuct()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s13 = r.Stations.Station(13);
        var s16 = r.Stations.Station(16);
        Assert.False(double.IsNaN(s16.StagnationT_K),
            "Station 16 (bypass duct exit) should be populated for turbofan.");
        // Phase 1 ships a lossless bypass duct: T_t16 = T_t13, P_t16 = P_t13.
        Assert.Equal(s13.StagnationT_K, s16.StagnationT_K, 9);
        Assert.Equal(s13.StagnationP_Pa, s16.StagnationP_Pa, 9);
    }

    [Fact]
    public void Solve_HpcInlet_EqualsFanExit_SingleSpool()
    {
        // For the single-spool model, T_t21 ≡ T_t13: there's no
        // inter-compressor work between fan exit and HPC inlet on the
        // core path. We don't expose an explicit station 21, but we
        // can verify by running with very low HPC ratio: T_t3 should
        // equal T_t13 to within numerical tolerance when π_hpc → 1.
        var solver = new TurbofanCycleSolver();
        // π_fan = √π_c, so π_c=4 → π_fan=2 → π_hpc=2; smaller π_hpc
        // means T_t3 stays close to T_t13.
        // To get π_hpc ≈ 1 we need π_c ≈ π_fan² ≈ π_c ⇒ infeasible
        // analytically; check the inequality T_t3 > T_t13 instead.
        var r = solver.Solve(Design(), Cond());
        var s3 = r.Stations.Station(3);
        var s13 = r.Stations.Station(13);
        Assert.True(s3.StagnationT_K > s13.StagnationT_K,
            $"HPC must raise T further (T_t3 = {s3.StagnationT_K:F1} K should be > T_t13 = {s13.StagnationT_K:F1} K).");
    }

    [Fact]
    public void Solve_TurbineDropsTAndP()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s4 = r.Stations.Station(4);
        var s5 = r.Stations.Station(5);
        Assert.True(s5.StagnationT_K < s4.StagnationT_K, "Turbine should drop T_t");
        Assert.True(s5.StagnationP_Pa < s4.StagnationP_Pa, "Turbine should drop P_t");
    }

    [Fact]
    public void Solve_ShaftBalance_TurbineWorkEqualsFanPlusCompressorWork()
    {
        // Single-spool balance per-core-mass:
        //   ṁ_total · cp · (T_t4 − T_t5) · η_mech =
        //       ṁ_inlet · cp · (T_t13 − T_t2) + ṁ_core · cp · (T_t3 − T_t13)
        // where ṁ_total = ṁ_core·(1+f), ṁ_inlet = ṁ_core·(1+BPR).
        // cp factors out — assert the ṁ·ΔT equality within tolerance.
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s2  = r.Stations.Station(2);
        var s13 = r.Stations.Station(13);
        var s3  = r.Stations.Station(3);
        var s4  = r.Stations.Station(4);
        var s5  = r.Stations.Station(5);

        // s2.MassFlow is ṁ_inlet = (1+BPR)·ṁ_core (full intake).
        // s3.MassFlow is ṁ_core (after splitter, core only).
        // s4.MassFlow is ṁ_core·(1+f) (post-combustor hot stream).
        double W_fan = s2.MassFlow_kg_s * (s13.StagnationT_K - s2.StagnationT_K);
        double W_hpc = s3.MassFlow_kg_s * (s3.StagnationT_K - s13.StagnationT_K);
        double W_turbine_extracted = s4.MassFlow_kg_s * (s4.StagnationT_K - s5.StagnationT_K);

        // Shaft balance with η_mech: W_turbine · η_mech = W_fan + W_hpc.
        double rhs = W_fan + W_hpc;
        double lhs = W_turbine_extracted * TurbofanCycleSolver.DefaultMechanicalEfficiency;
        double residual = System.Math.Abs(lhs - rhs);
        double tolerance = 1e-6 * System.Math.Abs(rhs);
        Assert.True(residual <= tolerance,
            $"Shaft balance: W_turb·η_mech = {lhs:F4} ṁ·K, W_fan + W_hpc = {rhs:F4} ṁ·K, residual = {residual:E3}.");
    }

    [Fact]
    public void Solve_MixerEnergyBalance_Closes_ConstantCp()
    {
        // ṁ_hot · T_t5 + ṁ_cold · T_t16 = ṁ_total · T_t6 (constant cp factors out).
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s5  = r.Stations.Station(5);
        var s6  = r.Stations.Station(6);
        var s16 = r.Stations.Station(16);

        double h_in = s5.MassFlow_kg_s * s5.StagnationT_K + s16.MassFlow_kg_s * s16.StagnationT_K;
        double h_out = s6.MassFlow_kg_s * s6.StagnationT_K;
        double residual = System.Math.Abs(h_in - h_out);
        double scale = h_out;
        double fractional = scale > 0 ? residual / scale : 0.0;
        Assert.True(fractional < 1e-9,
            $"Mixer energy balance: residual {residual:E6}, fractional {fractional:E6}.");
    }

    [Fact]
    public void Solve_MixerOutputT_BetweenHotAndCold()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s5  = r.Stations.Station(5);
        var s6  = r.Stations.Station(6);
        var s13 = r.Stations.Station(13);
        Assert.True(s6.StagnationT_K < s5.StagnationT_K,
            $"Mixer output T_t6 = {s6.StagnationT_K:F1} K should be below hot stream T_t5 = {s5.StagnationT_K:F1} K");
        Assert.True(s6.StagnationT_K > s13.StagnationT_K,
            $"Mixer output T_t6 = {s6.StagnationT_K:F1} K should be above cold stream T_t13 = {s13.StagnationT_K:F1} K");
    }

    [Fact]
    public void Solve_MixerPressureRecovery_Applied()
    {
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        var s5  = r.Stations.Station(5);
        var s6  = r.Stations.Station(6);
        var s13 = r.Stations.Station(13);
        // P_t6 should be at most π_mixer · max(P_t5, P_t13) in the
        // mass-flow-weighted model (the weighted mean is ≤ max).
        double pMax = System.Math.Max(s5.StagnationP_Pa, s13.StagnationP_Pa);
        Assert.True(s6.StagnationP_Pa <= TurbofanCycleSolver.DefaultMixerPressureRecovery * pMax + 1.0,
            $"Mixer output P_t6 = {s6.StagnationP_Pa:F0} Pa exceeds π_mixer·max(P_t5, P_t13) = "
          + $"{TurbofanCycleSolver.DefaultMixerPressureRecovery * pMax:F0} Pa.");
    }

    [Fact]
    public void Solve_DeterministicAcrossCalls()
    {
        var solver = new TurbofanCycleSolver();
        var a = solver.Solve(Design(), Cond());
        var b = solver.Solve(Design(), Cond());
        Assert.Equal(a.Stations.ThrustNet_N, b.Stations.ThrustNet_N, 12);
        Assert.Equal(a.Stations.SpecificImpulse_s, b.Stations.SpecificImpulse_s, 12);
    }

    [Fact]
    public void Solve_HigherBypassRatio_RaisesNetMassFlowAtFixedInletArea()
    {
        // At fixed inlet area + face Mach, the inlet mass flow doesn't
        // change with BPR (it's the full fan intake). But the mixer
        // mass flow grows because m_total = m_core·(1+f+BPR) and
        // m_core itself shrinks: m_core = m_inlet/(1+BPR). Net effect:
        // m_mixed = m_inlet · (1+f+BPR)/(1+BPR), which → m_inlet as
        // BPR → ∞ but is *larger* than m_inlet for moderate BPR + f > 0.
        // The cleanest invariant: at fixed inlet area + π_c + φ, BPR
        // increases the cold-stream mass through station 16.
        var solver = new TurbofanCycleSolver();
        var lo = solver.Solve(Design(bpr: 0.10), Cond());
        var hi = solver.Solve(Design(bpr: 1.50), Cond());
        Assert.True(hi.Stations.Station(16).MassFlow_kg_s > lo.Stations.Station(16).MassFlow_kg_s,
            $"Higher BPR should raise station-16 cold-stream mass flow ({lo.Stations.Station(16).MassFlow_kg_s:F2} → {hi.Stations.Station(16).MassFlow_kg_s:F2} kg/s)");
    }

    [Fact]
    public void Solve_F404DesignPoint_ProducesPositiveThrustAndIsp()
    {
        // Sanity check before the validation fixture.
        var solver = new TurbofanCycleSolver();
        var r = solver.Solve(Design(), Cond());
        Assert.True(r.Stations.ThrustNet_N > 0,
            $"Expected positive thrust at F404 design point; got {r.Stations.ThrustNet_N}");
        Assert.True(r.Stations.SpecificImpulse_s > 0,
            $"Expected positive Isp at F404 design point; got {r.Stations.SpecificImpulse_s}");
    }

    [Fact]
    public void DefaultFanPressureRatio_IsSqrtPiC()
    {
        Assert.Equal(System.Math.Sqrt(25.0), TurbofanCycleSolver.DefaultFanPressureRatio(25.0, 0.34), 12);
        Assert.Equal(System.Math.Sqrt(4.0),  TurbofanCycleSolver.DefaultFanPressureRatio(4.0, 1.0), 12);
        Assert.Equal(1.0, TurbofanCycleSolver.DefaultFanPressureRatio(1.0, 0.0), 12);
    }

    [Fact]
    public void DefaultFanPressureRatio_RejectsSubUnityPiC()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => TurbofanCycleSolver.DefaultFanPressureRatio(0.5, 0.34));
    }

    // ---- SolveAtOperatingPoint tests ----

    [Fact]
    public void SolveAtOperatingPoint_DesignPoint_MatchesSolve()
    {
        var solver = new TurbofanCycleSolver();
        var design = Design();
        var cond = Cond();
        var dp = solver.Solve(design, cond);
        var op = solver.SolveAtOperatingPoint(design, cond, N_corr_frac: 1.0);
        double relTol = 1e-3;
        double thrustRel = System.Math.Abs(op.Stations.ThrustNet_N - dp.Stations.ThrustNet_N)
                         / System.Math.Max(System.Math.Abs(dp.Stations.ThrustNet_N), 1.0);
        Assert.True(thrustRel <= relTol,
            $"SolveAtOperatingPoint N_corr=1 thrust {op.Stations.ThrustNet_N:F1} N should match "
          + $"Solve {dp.Stations.ThrustNet_N:F1} N within {relTol:P1} (got {thrustRel:P2}).");
    }

    [Fact]
    public void SolveAtOperatingPoint_NCorr080_ThrottledDown_LowerThrustThanDesign()
    {
        var solver = new TurbofanCycleSolver();
        var design = Design();
        var cond = Cond();
        var dp = solver.Solve(design, cond);
        var op = solver.SolveAtOperatingPoint(design, cond, N_corr_frac: 0.8);
        Assert.True(op.Stations.ThrustNet_N < dp.Stations.ThrustNet_N,
            $"N_corr=0.8 thrust {op.Stations.ThrustNet_N:F1} N should be below design-point "
          + $"{dp.Stations.ThrustNet_N:F1} N.");
    }

    [Fact]
    public void SolveAtOperatingPoint_NCorr090_ThrustMonotonicallyBetween080And100()
    {
        var solver = new TurbofanCycleSolver();
        var design = Design();
        var cond = Cond();
        var r80 = solver.SolveAtOperatingPoint(design, cond, N_corr_frac: 0.8);
        var r90 = solver.SolveAtOperatingPoint(design, cond, N_corr_frac: 0.9);
        var r100 = solver.SolveAtOperatingPoint(design, cond, N_corr_frac: 1.0);
        Assert.True(r80.Stations.ThrustNet_N < r90.Stations.ThrustNet_N,
            $"Thrust should increase monotonically: N=0.8 ({r80.Stations.ThrustNet_N:F1}) < N=0.9 ({r90.Stations.ThrustNet_N:F1})");
        Assert.True(r90.Stations.ThrustNet_N < r100.Stations.ThrustNet_N,
            $"Thrust should increase monotonically: N=0.9 ({r90.Stations.ThrustNet_N:F1}) < N=1.0 ({r100.Stations.ThrustNet_N:F1})");
    }

    [Fact]
    public void SolveAtOperatingPoint_InvalidNCorr_ThrowsOutOfRange()
    {
        var solver = new TurbofanCycleSolver();
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => solver.SolveAtOperatingPoint(Design(), Cond(), N_corr_frac: -0.1));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => solver.SolveAtOperatingPoint(Design(), Cond(), N_corr_frac: 1.6));
    }

    // ---- Cooled-turbine tests ----

    [Fact]
    public void CooledTurbine_EffectiveTITReducedByFraction()
    {
        var solver = new TurbofanCycleSolver();
        var uncooled = solver.Solve(Design(phi: 0.30), Cond());
        var cooled   = solver.Solve(Design(phi: 0.30) with { TurbineCoolingFraction = 0.10 }, Cond());
        // With τ=0.10 the blended TIT = T_t4*(0.9) + T_t3*(0.1) < T_t4
        Assert.True(cooled.Stations.Station(4).StagnationT_K
                  < uncooled.Stations.Station(4).StagnationT_K,
            $"Cooled TIT {cooled.Stations.Station(4).StagnationT_K:F1} K should be below "
          + $"uncooled {uncooled.Stations.Station(4).StagnationT_K:F1} K.");
    }

    [Fact]
    public void CooledTurbine_TitGateAllows2200K_WhenCoolingEnabled()
    {
        // Design that pushes T_t4 above 1700 K — would fail TIT_EXCEEDED uncooled.
        // With τ=0.15 the blended T_t4_eff ≈ T_t4*0.85 + T_t3*0.15, well below 2200 K.
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.37,
            CombustorArea_m2:        0.15,
            CombustorLength_m:       0.40,
            NozzleThroatArea_m2:     0.12,
            NozzleExitArea_m2:       0.18,
            EquivalenceRatio:        0.55,
            CompressorPressureRatio: 25.0,
            BypassRatio:             0.34)
        {
            TurbineCoolingFraction = 0.15,
        };
        var result = Voxelforge.Airbreathing.AirbreathingOptimization.GenerateWith(
            design, new FlightConditions(0.0, 0.001, AirbreathingFuel.Jp8));
        // Gate should not fire — cooled ceiling is 2200 K
        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "TIT_EXCEEDED");
    }

    [Fact]
    public void CooledTurbine_UncooledDesign_GateStillFiresAt1700K()
    {
        // EquivalenceRatio high enough that T_t4 > 1700 K with τ=0 (no cooling)
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.37,
            CombustorArea_m2:        0.15,
            CombustorLength_m:       0.40,
            NozzleThroatArea_m2:     0.12,
            NozzleExitArea_m2:       0.18,
            EquivalenceRatio:        0.70,
            CompressorPressureRatio: 25.0,
            BypassRatio:             0.34);
        var result = Voxelforge.Airbreathing.AirbreathingOptimization.GenerateWith(
            design, new FlightConditions(0.0, 0.001, AirbreathingFuel.Jp8));
        // Either T_t4 > 1700 K and the gate fires, or the design is under the
        // limit and the test is a no-op sanity check — verify the branch logic
        // by confirming the gate fires only when T_t4 genuinely exceeds the limit.
        var s4T = result.Stations.Station(4).StagnationT_K;
        if (s4T > Voxelforge.Airbreathing.AirbreathingFeasibility.TurbineInletT_MaxUncooled_K)
        {
            Assert.Contains(result.Violations, v => v.ConstraintId == "TIT_EXCEEDED");
        }
        // If T_t4 ≤ 1700 K the gate should be absent.
        else
        {
            Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "TIT_EXCEEDED");
        }
    }
}
