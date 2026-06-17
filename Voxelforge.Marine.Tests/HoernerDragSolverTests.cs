// HoernerDragSolverTests.cs — unit tests for HoernerDragSolver.
//
// Reference values:
//   REMUS-100: drag ~3.9 N at 1.5 m/s (Hoerner §6-2, wetted-area formulation).
//     The Allen et al. (1997) "0.7 N" figure refers to shaft thrust at ~60 %
//     propulsive efficiency, not bare-hull hydrodynamic drag.
//   Slender-body fineness L/D ≈ 8.4 → C_f_wetted ≈ 0.003-0.005
//     (skin friction dominant, Hoerner §6-2).

using System;
using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests;

public sealed class HoernerDragSolverTests
{
    // ── Basic contract ────────────────────────────────────────────────────────

    [Fact]
    public void Solve_NullFairing_Throws()
    {
        var cond = MakeRemus100Conditions();
        Assert.Throws<ArgumentNullException>(() => HoernerDragSolver.Solve(null!, cond));
    }

    [Fact]
    public void Solve_NullConditions_Throws()
    {
        var fairing = MakeRemusFairing();
        Assert.Throws<ArgumentNullException>(() => HoernerDragSolver.Solve(fairing, null!));
    }

    [Fact]
    public void Solve_ZeroSpeed_Throws()
    {
        var fairing = MakeRemusFairing();
        var cond = new MarineConditions(CruiseSpeed_ms: 0.0, MaxDepth_m: 100.0);
        Assert.Throws<InvalidOperationException>(() => HoernerDragSolver.Solve(fairing, cond));
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    [Fact]
    public void ReynoldsNumber_IsPositive()
    {
        var result = HoernerDragSolver.Solve(MakeRemusFairing(), MakeRemus100Conditions());
        Assert.True(result.ReynoldsNumber > 0);
    }

    [Fact]
    public void SkinFrictionCoefficient_IsInTurbulentRange()
    {
        var result = HoernerDragSolver.Solve(MakeRemusFairing(), MakeRemus100Conditions());
        // Turbulent flat-plate C_f for AUV Re ≈ 2e6 is roughly 0.003–0.006.
        Assert.InRange(result.SkinFrictionCoefficient, 0.001, 0.010);
    }

    [Fact]
    public void DragForce_IsPositive()
    {
        var result = HoernerDragSolver.Solve(MakeRemusFairing(), MakeRemus100Conditions());
        Assert.True(result.DragForce_N > 0);
    }

    [Fact]
    public void DragForce_REMUS100_WithinToleranceBand()
    {
        // Hoerner §6-2 wetted-area model at Re_L ≈ 1.77e6, S_wet ≈ 0.78 m²:
        // F ≈ 3.9 N at 1.5 m/s.  ±40 % tolerance covers Hoerner correlation accuracy.
        var result = HoernerDragSolver.Solve(MakeRemusFairing(), MakeRemus100Conditions());
        Assert.InRange(result.DragForce_N, 2.0, 6.0);
    }

    [Fact]
    public void DragScalesWithVelocitySquared()
    {
        var fairing = MakeRemusFairing();
        var cond1   = new MarineConditions(CruiseSpeed_ms: 1.0, MaxDepth_m: 50.0);
        var cond2   = new MarineConditions(CruiseSpeed_ms: 2.0, MaxDepth_m: 50.0);
        var r1 = HoernerDragSolver.Solve(fairing, cond1);
        var r2 = HoernerDragSolver.Solve(fairing, cond2);
        // F ∝ V² — ratio should be ≈ 4 (within ~10% because C_f changes slightly with Re).
        double ratio = r2.DragForce_N / r1.DragForce_N;
        Assert.InRange(ratio, 3.5, 4.5);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FairingGeometry MakeRemusFairing()
    {
        var design = MakeRemus100Design();
        return MyringFairingGeometry.Compute(design);
    }
}
