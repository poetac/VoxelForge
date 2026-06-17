// HydrostaticEquilibriumTests.cs — unit tests for HydrostaticEquilibrium.
//
// Key behavioural checks per the plan:
//   "Solid steel sphere sinks (ΔF < 0)"
//   "Hollow Al cylinder floats"
//   REMUS-100 seed produces positive buoyancy.

using System;
using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests;

public sealed class HydrostaticEquilibriumTests
{
    // ── Basic contract ────────────────────────────────────────────────────────

    [Fact]
    public void Solve_NullFairing_Throws()
    {
        var design = MakeRemus100Design();
        var cond   = MakeRemus100Conditions();
        Assert.Throws<ArgumentNullException>(
            () => HydrostaticEquilibrium.Solve(null!, design, cond));
    }

    [Fact]
    public void Solve_NullDesign_Throws()
    {
        var fairing = MyringFairingGeometry.Compute(MakeRemus100Design());
        var cond    = MakeRemus100Conditions();
        Assert.Throws<ArgumentNullException>(
            () => HydrostaticEquilibrium.Solve(fairing, null!, cond));
    }

    // ── Physics ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuoyancyForce_IsPositive()
    {
        var design  = MakeRemus100Design();
        var cond    = MakeRemus100Conditions();
        var fairing = MyringFairingGeometry.Compute(design);
        var result  = HydrostaticEquilibrium.Solve(fairing, design, cond);
        Assert.True(result.BuoyancyForce_N > 0);
    }

    [Fact]
    public void HullMass_IsPositive()
    {
        var design  = MakeRemus100Design();
        var fairing = MyringFairingGeometry.Compute(design);
        var result  = HydrostaticEquilibrium.Solve(fairing, design, MakeRemus100Conditions());
        Assert.True(result.HullMass_kg > 0);
    }

    [Fact]
    public void REMUS100_IsPositivelyBuoyant()
    {
        // REMUS-100 hollow Al hull at 4 mm wall should float.
        var design  = MakeRemus100Design();
        var cond    = MakeRemus100Conditions();
        var fairing = MyringFairingGeometry.Compute(design);
        var result  = HydrostaticEquilibrium.Solve(fairing, design, cond);
        Assert.True(result.BuoyantWeight_N > 0,
            $"BuoyantWeight = {result.BuoyantWeight_N:F2} N should be positive (floats).");
    }

    [Fact]
    public void DenseThickWall_Sinks()
    {
        // Steel (index 2) at 15 mm wall — very heavy shell, should sink.
        var design = MakeRemus100Design() with
        {
            MaterialIndex    = 2,   // AISI-316L, 7950 kg/m³
            WallThickness_m  = 0.015,
        };
        var fairing = MyringFairingGeometry.Compute(design);
        var cond    = MakeRemus100Conditions();
        var result  = HydrostaticEquilibrium.Solve(fairing, design, cond);
        Assert.True(result.BuoyantWeight_N < 0,
            $"Heavy-wall steel hull BuoyantWeight = {result.BuoyantWeight_N:F2} N should be negative (sinks).");
    }

    [Fact]
    public void HullMass_ScalesWithWallThickness()
    {
        // Doubling wall thickness should roughly double the shell volume and hull mass.
        var thin  = MakeRemus100Design();
        var thick = thin with { WallThickness_m = thin.WallThickness_m * 2.0 };
        var cond  = MakeRemus100Conditions();
        var rThin  = HydrostaticEquilibrium.Solve(MyringFairingGeometry.Compute(thin),  thin,  cond);
        var rThick = HydrostaticEquilibrium.Solve(MyringFairingGeometry.Compute(thick), thick, cond);
        // Mass ratio should be ≈ 2 (thin-wall approx: V_shell ≈ S_wet × t)
        Assert.InRange(rThick.HullMass_kg / rThin.HullMass_kg, 1.8, 2.2);
    }

    [Fact]
    public void DisplacedVolume_MatchesFairingExternalVolume()
    {
        var design  = MakeRemus100Design();
        var fairing = MyringFairingGeometry.Compute(design);
        var result  = HydrostaticEquilibrium.Solve(fairing, design, MakeRemus100Conditions());
        Assert.Equal(fairing.ExternalVolume_m3, result.DisplacedVolume_m3, precision: 12);
    }
}
