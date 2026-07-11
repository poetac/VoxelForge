// HydroTurbineSolverTests — pins the HE.W1/W2 closed-form hydro chain:
// P_hydraulic = ρgQH, the per-kind cluster η_peak registry (Pelton 0.90 /
// Francis 0.93 / Kaplan 0.91), the linear off-envelope head de-rating
// (clamped at 30 %), the η_turbine·η_generator roll-up, and the HE.W2
// head-based kind auto-selection. Every expected value hand-derived and
// independently recomputed (python3, mirroring the solver's operation
// order) before assertion. Backfills coverage onto the Linux 'core' CI
// leg (ROADMAP → Now §2).

using System;
using Voxelforge.Hydroelectric;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class HydroTurbineSolverTests
{
    // Three-Gorges-class Francis unit: H = 80 m, Q = 850 m³/s,
    // η_generator = 0.98, fresh water.
    private static HydroTurbineDesign ThreeGorgesClassFrancis()
        => new(HydroTurbineKind.Francis,
               Head_m:                 80.0,
               VolumetricFlowRate_m3s: 850.0,
               GeneratorEfficiency:    0.98);

    [Fact]
    public void Solve_FrancisAnchor_ReproducesPowerRollUp()
    {
        var r = HydroTurbineSolver.Solve(ThreeGorgesClassFrancis());

        // P_hyd = 1000·9.80665·850·80 = 666 852 200 W (≈ 667 MW unit).
        Assert.Equal(666852200.0, r.HydraulicPower_W, 3);
        Assert.True(r.HeadInValidEnvelope);

        // In-envelope Francis runs at the cluster peak η = 0.93.
        Assert.Equal(0.93, r.HydraulicEfficiency, 12);
        Assert.Equal(0.98, r.GeneratorEfficiency, 12);
        Assert.Equal(0.9114, r.OverallEfficiency, 12);

        Assert.Equal(620172546.0, r.ShaftPower_W, 3);
        Assert.Equal(607769095.08, r.ElectricalPower_W, 3);
    }

    [Fact]
    public void Solve_OffEnvelopeHead_DeratesLinearlyAndClamps()
    {
        // Kaplan envelope is [2, 40] m (width 38). H = 78 is 38 m past
        // the edge → fraction exactly 1.0 → the full 30 % de-rating:
        // η = 0.91·0.70.
        var kaplan = HydroTurbineSolver.Solve(new HydroTurbineDesign(
            HydroTurbineKind.Kaplan, 78.0, 850.0, 0.98));
        Assert.False(kaplan.HeadInValidEnvelope);
        Assert.Equal(0.6369999999999999, kaplan.HydraulicEfficiency, 12);

        // Fraction clamps at 1.0: twice as far out gives the same η.
        var farther = HydroTurbineSolver.Solve(new HydroTurbineDesign(
            HydroTurbineKind.Kaplan, 116.0, 850.0, 0.98));
        Assert.Equal(kaplan.HydraulicEfficiency, farther.HydraulicEfficiency, 12);

        // Pelton envelope is [200, 2000] m (width 1800). H = 100 is
        // 100 m below the edge → fraction 100/1800 →
        // η = 0.90·(1 − 0.30·100/1800) = 0.885.
        var pelton = HydroTurbineSolver.Solve(new HydroTurbineDesign(
            HydroTurbineKind.Pelton, 100.0, 10.0, 0.98));
        Assert.Equal(0.885, pelton.HydraulicEfficiency, 12);
    }

    [Fact]
    public void ComputeOffEnvelopePenalty_IsUnityInsideTheEnvelope()
    {
        var francis = HydroTurbineRegistry.Francis;
        Assert.Equal(1.0, HydroTurbineSolver.ComputeOffEnvelopePenalty(10.0, francis), 12);
        Assert.Equal(1.0, HydroTurbineSolver.ComputeOffEnvelopePenalty(350.0, francis), 12);
        Assert.Equal(1.0, HydroTurbineSolver.ComputeOffEnvelopePenalty(700.0, francis), 12);
        // Penalty is bounded in [0.70, 1.0] everywhere.
        for (double h = 1.0; h <= 3000.0; h *= 1.5)
            Assert.InRange(HydroTurbineSolver.ComputeOffEnvelopePenalty(h, francis),
                1.0 - HydroTurbineSolver.OffEnvelopeMaxDerating, 1.0);
    }

    [Fact]
    public void SelectKindForHead_PicksTheTextbookTopologyBands()
    {
        // ≥ 200 m → Pelton; ≥ 10 m → Francis; below → Kaplan.
        Assert.Equal(HydroTurbineKind.Pelton, HydroTurbineSolver.SelectKindForHead(1869.0));
        Assert.Equal(HydroTurbineKind.Pelton, HydroTurbineSolver.SelectKindForHead(200.0));
        Assert.Equal(HydroTurbineKind.Francis, HydroTurbineSolver.SelectKindForHead(199.0));
        Assert.Equal(HydroTurbineKind.Francis, HydroTurbineSolver.SelectKindForHead(10.0));
        Assert.Equal(HydroTurbineKind.Kaplan, HydroTurbineSolver.SelectKindForHead(9.9));
        Assert.Equal(HydroTurbineKind.Kaplan, HydroTurbineSolver.SelectKindForHead(2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HydroTurbineSolver.SelectKindForHead(0.0));
    }

    [Fact]
    public void Solve_PowerCascade_NeverCreatesEnergy()
    {
        // P_elec ≤ P_shaft ≤ P_hydraulic for every kind, in and out of
        // envelope — robust even if the cluster η anchors are recalibrated.
        foreach (var kind in new[] { HydroTurbineKind.Pelton,
                                     HydroTurbineKind.Francis,
                                     HydroTurbineKind.Kaplan })
        {
            foreach (double head in new[] { 5.0, 50.0, 500.0 })
            {
                var r = HydroTurbineSolver.Solve(new HydroTurbineDesign(
                    kind, head, 100.0, 0.97));
                Assert.True(r.ElectricalPower_W <= r.ShaftPower_W + 1e-9);
                Assert.True(r.ShaftPower_W < r.HydraulicPower_W);
                Assert.InRange(r.HydraulicEfficiency, 0.0, 1.0);
                Assert.InRange(r.OverallEfficiency, 0.0, r.HydraulicEfficiency);
            }
        }
    }

    [Fact]
    public void Solve_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            HydroTurbineSolver.Solve(ThreeGorgesClassFrancis() with
            { Kind = HydroTurbineKind.None }));
        Assert.Throws<ArgumentException>(() =>
            HydroTurbineSolver.Solve(ThreeGorgesClassFrancis() with { Head_m = 0.0 }));
        Assert.Throws<ArgumentException>(() =>
            HydroTurbineSolver.Solve(ThreeGorgesClassFrancis() with
            { VolumetricFlowRate_m3s = -1.0 }));
        Assert.Throws<ArgumentException>(() =>
            HydroTurbineSolver.Solve(ThreeGorgesClassFrancis() with
            { GeneratorEfficiency = 1.01 }));
        Assert.Throws<ArgumentException>(() =>
            HydroTurbineSolver.Solve(ThreeGorgesClassFrancis() with
            { WaterDensity_kgm3 = 0.0 }));
    }
}
