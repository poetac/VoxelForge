// HybridRocketSolverTests — pins the R.W2 closed-form hybrid snapshot:
// G_ox = ṁ_ox/(πR²), the Marxman regression fit r_dot = a·G_ox^n
// (Karabeyoglu LOX/HTPB a = 1.37e-4, n = 0.681), fuel mass flow off the
// cylindrical port ṁ_f = ρ·2πRL·r_dot, O/F, the Sutton LOX/HTPB cluster
// anchors (c* = 1640 m/s; C_F(ε) = 1.62 + 0.08·log10(ε/10)),
// Isp = c*·C_F/g₀ and F = ṁ·Isp·g₀. Every expected value hand-derived
// and independently recomputed (python3, mirroring the solver's
// operation order) before assertion. Backfills coverage onto the Linux
// 'core' CI leg (ROADMAP → Now §2).

using System;
using Voxelforge.Hybrid;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class HybridRocketSolverTests
{
    // Lab-scale LOX/HTPB motor: L = 1 m grain, 50 mm initial port,
    // 150 mm outer grain radius, ṁ_ox = 5 kg/s, P_c = 20 bar, ε = 10
    // (the C_F anchor point exactly).
    private static HybridRocketDesign LabScaleLoxHtpb()
        => new(HybridFuel.HTPB,
               GrainLength_m:        1.0,
               InitialPortRadius_m:  0.05,
               OuterGrainRadius_m:   0.15,
               OxidiserMassFlow_kgs: 5.0,
               ChamberPressure_bar:  20.0,
               ExpansionRatio:       10.0);

    [Fact]
    public void SolveInitial_Anchor_ReproducesMarxmanChain()
    {
        var r = HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb());

        // G_ox = 5/(π·0.05²) ≈ 636.6 kg/(m²·s).
        Assert.Equal(0.05, r.PortRadius_m, 12);
        Assert.Equal(636.6197723675813, r.OxidiserMassFlux_kgm2s, 9);
        // r_dot = 1.37e-4·G^0.681 ≈ 11.1 mm/s.
        Assert.Equal(0.011121488853138075, r.RegressionRate_ms, 12);
        // ṁ_f = 920·(2π·0.05·1.0)·r_dot.
        Assert.Equal(3.21440526637594, r.FuelMassFlow_kgs, 9);
        Assert.Equal(8.214405266375941, r.TotalMassFlow_kgs, 9);
        Assert.Equal(1.555497700399557, r.OxidiserFuelRatio, 10);

        // Sutton cluster anchors: ε = 10 sits exactly on the C_F anchor.
        Assert.Equal(1640.0, r.CharacteristicVelocity_ms, 12);
        Assert.Equal(1.62, r.ThrustCoefficient, 12);
        Assert.Equal(270.918203463976, r.VacuumIsp_s, 9);
        Assert.Equal(21824.0319117076, r.VacuumThrust_N, 6);
    }

    [Fact]
    public void SolveInitial_IsExactlySolveAtTheInitialPortRadius()
    {
        var viaInitial = HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb());
        var viaSolve   = HybridRocketCycleSolver.Solve(LabScaleLoxHtpb(), 0.05);
        Assert.Equal(viaSolve, viaInitial);   // record value equality
    }

    [Fact]
    public void Solve_AsThePortOpens_FluxFallsAndOfShifts()
    {
        // At R = 0.10 m the flux drops 4× and the fuel flow falls as
        // R^(1−2n) = R^−0.362 → O/F shifts oxidiser-rich over the burn.
        var mid = HybridRocketCycleSolver.Solve(LabScaleLoxHtpb(), 0.10);
        Assert.Equal(159.15494309189532, mid.OxidiserMassFlux_kgm2s, 9);
        Assert.Equal(2.501081089208791, mid.FuelMassFlow_kgs, 9);
        Assert.Equal(1.9991355024725463, mid.OxidiserFuelRatio, 10);

        var start = HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb());
        Assert.True(mid.OxidiserMassFlux_kgm2s < start.OxidiserMassFlux_kgm2s);
        Assert.True(mid.FuelMassFlow_kgs < start.FuelMassFlow_kgs);
        Assert.True(mid.OxidiserFuelRatio > start.OxidiserFuelRatio);
    }

    [Fact]
    public void Solve_MassBookkeepingIsExact()
    {
        // ṁ_total = ṁ_ox + ṁ_fuel and F = ṁ_total·c*·C_F, exactly, at
        // every port radius — robust to future anchor recalibration.
        foreach (double radius in new[] { 0.05, 0.08, 0.12, 0.15 })
        {
            var r = HybridRocketCycleSolver.Solve(LabScaleLoxHtpb(), radius);
            Assert.Equal(5.0 + r.FuelMassFlow_kgs, r.TotalMassFlow_kgs, 12);
            Assert.Equal(r.TotalMassFlow_kgs * r.CharacteristicVelocity_ms
                         * r.ThrustCoefficient, r.VacuumThrust_N, 6);
            Assert.Equal(5.0 / r.FuelMassFlow_kgs, r.OxidiserFuelRatio, 12);
        }
    }

    [Fact]
    public void ComputeVacuumThrustCoefficient_FollowsTheLogEpsilonFit()
    {
        Assert.Equal(1.62, HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(10.0), 12);
        // One decade up: 1.62 + 0.08.
        Assert.Equal(1.7000000000000002,
            HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(100.0), 12);
        // Monotonic in ε.
        double prev = 0.0;
        for (double eps = 1.0; eps <= 200.0; eps *= 2.0)
        {
            double cf = HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(eps);
            Assert.True(cf > prev);
            prev = cf;
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridRocketCycleSolver.ComputeVacuumThrustCoefficient(0.9));
    }

    [Fact]
    public void ParaffinRegression_OutpacesHtpbAtTheSameFlux()
    {
        // Entrainment mechanism: a = 4.1e-4 (n = 0.62) regresses faster
        // than HTPB across the whole realistic flux band.
        var htpb     = HybridFuelRegistry.For(HybridFuel.HTPB);
        var paraffin = HybridFuelRegistry.For(HybridFuel.Paraffin);
        foreach (double gox in new[] { 50.0, 200.0, 640.0 })
        {
            double rHtpb = htpb.MarxmanA * Math.Pow(gox, htpb.MarxmanN);
            double rPara = paraffin.MarxmanA * Math.Pow(gox, paraffin.MarxmanN);
            Assert.True(rPara > rHtpb,
                $"paraffin must out-regress HTPB at G_ox = {gox}");
        }
    }

    [Fact]
    public void Solve_RejectsOutOfBandPortRadiiAndMalformedDesigns()
    {
        // Snapshot radius must sit inside [initial port, outer grain].
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridRocketCycleSolver.Solve(LabScaleLoxHtpb(), 0.04));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridRocketCycleSolver.Solve(LabScaleLoxHtpb(), 0.16));
        // No fuel web: initial port ≥ outer grain (categorical failure).
        Assert.Throws<ArgumentException>(() =>
            HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb() with
            { InitialPortRadius_m = 0.15 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb() with
            { OxidiserMassFlow_kgs = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb() with
            { ExpansionRatio = 0.5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HybridRocketCycleSolver.SolveInitial(LabScaleLoxHtpb() with
            { GrainLength_m = double.NaN }));
    }
}
