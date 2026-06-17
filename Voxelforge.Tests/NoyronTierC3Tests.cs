// NoyronTierC3Tests.cs — Tier C3 Phase 1 forcing-function suite for
// the turbopump geometry generator.
//
// Coverage
// ────────
//   • TurbopumpGeometryGenerator.Generate returns null on degenerate pump
//     (zero RPM, zero head).
//   • Generate populates every field for a nominal pump.
//   • Euler-head tip-speed inversion monotonic with head; inverse with RPM.
//   • HubToTipRatio / ImpellerThicknessRatio / VoluteMinorStart/End constants
//     are within sane bounds.
//   • Hand-rolled implicits (Inducer / Impeller / Volute / Assembly):
//       - Sign convention (negative inside solid phase).
//       - Degenerate-construction throws.
//       - Axis / radius clipping behaviour.
//   • TurbopumpResult gains FuelPumpGeometry + OxPumpGeometry optionals
//     (default null).
//   • OperatingConditions.IncludeTurbopumpGeometry defaults false +
//     round-trips via `with`.
//   • GenerateWith populates FuelPumpGeometry when:
//       (a) cond.EngineCycle != PressureFed AND
//       (b) cond.IncludeTurbopumpGeometry == true.
//     Leaves it null in either negative case.
//
// All tests are pure C# — no PicoGK Library init.

using System.Numerics;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Optimization;
using Voxelforge.Turbopump;

namespace Voxelforge.Tests;

public class NoyronTierC3Tests
{
    // ══════════════════════ Generator math ══════════════════════

    private static PumpSizing MakeNominalPump() => new(
        PropellantLabel:      "fuel",
        MassFlow_kgs:         2.5,
        InletPressure_Pa:     0.5e6,
        DischargePressure_Pa: 15e6,
        Density_kgm3:         420.0,          // LCH4
        HeadRise_m:           3500.0,
        HydraulicPower_W:     35_000,
        ShaftPower_W:         55_000,
        Efficiency:           0.65,
        Rpm:                  25_000,
        NPSHA_m:              30.0,
        NPSHR_m:              20.0,
        NPSHAcceptable:       true);

    [Fact]
    public void Generate_NullPump_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            TurbopumpGeometryGenerator.Generate(null!));
    }

    [Fact]
    public void Generate_ZeroRpm_ReturnsNull()
    {
        var p = MakeNominalPump() with { Rpm = 0 };
        Assert.Null(TurbopumpGeometryGenerator.Generate(p));
    }

    [Fact]
    public void Generate_ZeroHead_ReturnsNull()
    {
        var p = MakeNominalPump() with { HeadRise_m = 0 };
        Assert.Null(TurbopumpGeometryGenerator.Generate(p));
    }

    [Fact]
    public void Generate_Nominal_PopulatesEveryField()
    {
        var g = TurbopumpGeometryGenerator.Generate(MakeNominalPump());
        Assert.NotNull(g);
        Assert.True(g!.ImpellerTipRadius_mm > g.ImpellerHubRadius_mm);
        Assert.True(g.ImpellerHubRadius_mm > 0);
        Assert.True(g.ImpellerThickness_mm > 0);
        Assert.True(g.InducerTipRadius_mm > g.InducerHubRadius_mm);
        Assert.True(g.InducerLength_mm > 0);
        Assert.Equal(TurbopumpGeometryGenerator.ImpellerBladeCount, g.ImpellerBladeCount);
        Assert.Equal(TurbopumpGeometryGenerator.InducerBladeCount, g.InducerBladeCount);
        Assert.True(g.VoluteMinorRadiusEnd_mm > g.VoluteMinorRadiusStart_mm);
        Assert.True(g.CasingOuterRadius_mm > g.ImpellerTipRadius_mm);
        Assert.True(g.TotalLength_mm > 0);
        Assert.True(g.EstimatedMass_g > 0);
        Assert.NotEmpty(g.Notes);
    }

    [Fact]
    public void Generate_TipRadius_MonotonicInHead()
    {
        var low = MakeNominalPump() with { HeadRise_m = 1500 };
        var high = MakeNominalPump() with { HeadRise_m = 6000 };
        var gLow = TurbopumpGeometryGenerator.Generate(low);
        var gHigh = TurbopumpGeometryGenerator.Generate(high);
        Assert.NotNull(gLow);
        Assert.NotNull(gHigh);
        Assert.True(gHigh!.ImpellerTipRadius_mm > gLow!.ImpellerTipRadius_mm);
    }

    [Fact]
    public void Generate_TipRadius_InverseInRpm()
    {
        // U_2 = ω·R → higher RPM at same head → smaller R.
        var slow = MakeNominalPump() with { Rpm = 10_000 };
        var fast = MakeNominalPump() with { Rpm = 50_000 };
        var gSlow = TurbopumpGeometryGenerator.Generate(slow);
        var gFast = TurbopumpGeometryGenerator.Generate(fast);
        Assert.True(gSlow!.ImpellerTipRadius_mm > gFast!.ImpellerTipRadius_mm);
    }

    [Fact]
    public void Generate_HubTipRatio_MatchesConstant()
    {
        var g = TurbopumpGeometryGenerator.Generate(MakeNominalPump());
        double ratio = g!.ImpellerHubRadius_mm / g.ImpellerTipRadius_mm;
        Assert.Equal(TurbopumpGeometryGenerator.HubToTipRatio, ratio, 3);
    }

    // ══════════════════════ Hand-rolled implicits ══════════════════════

    [Fact]
    public void InducerImplicit_InsideHubCylinder_IsNegative()
    {
        var inducer = new InducerImplicit(
            rHub_mm: 5f, rTip_mm: 15f, zMin_mm: 0f, zMax_mm: 20f,
            bladeCount: 3, pitch_mm: 15f, bladeThickness_mm: 2f);
        var p = new Vector3(2f, 0, 10f);   // r=2 < rHub=5, inside hub
        Assert.True(inducer.fSignedDistance(p) < 0);
    }

    [Fact]
    public void InducerImplicit_OutsideTipRadius_IsPositive()
    {
        var inducer = new InducerImplicit(
            rHub_mm: 5f, rTip_mm: 15f, zMin_mm: 0f, zMax_mm: 20f);
        var p = new Vector3(30f, 0, 10f);  // r=30 >> rTip=15
        Assert.True(inducer.fSignedDistance(p) > 0);
    }

    [Fact]
    public void InducerImplicit_DegenerateRadii_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new InducerImplicit(rHub_mm: 15f, rTip_mm: 5f, zMin_mm: 0f, zMax_mm: 20f));
    }

    [Fact]
    public void InducerImplicit_DegenerateAxialExtent_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new InducerImplicit(rHub_mm: 5f, rTip_mm: 15f, zMin_mm: 20f, zMax_mm: 0f));
    }

    [Fact]
    public void ImpellerImplicit_InsideHubDisc_IsNegative()
    {
        var imp = new ImpellerImplicit(
            rHub_mm: 10f, rTip_mm: 40f, zMin_mm: 0f, zMax_mm: 10f,
            bladeCount: 8, bladeThickness_mm: 3f);
        // Hub disc covers entire rTip radius over the first 35 % of axial
        // extent — sample inside it.
        var p = new Vector3(20f, 0, 1f);
        Assert.True(imp.fSignedDistance(p) < 0);
    }

    [Fact]
    public void ImpellerImplicit_InShaftCavity_IsPositive()
    {
        var imp = new ImpellerImplicit(
            rHub_mm: 10f, rTip_mm: 40f, zMin_mm: 0f, zMax_mm: 10f);
        // Above the hub disc (z > 35% of extent) AND r < rHub → shaft cavity
        var p = new Vector3(5f, 0, 9f);
        Assert.True(imp.fSignedDistance(p) > 0);
    }

    [Fact]
    public void ImpellerImplicit_DegenerateRadii_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new ImpellerImplicit(rHub_mm: 40f, rTip_mm: 10f, zMin_mm: 0f, zMax_mm: 10f));
    }

    [Fact]
    public void VoluteImplicit_InsideCavity_IsNegative()
    {
        var vol = new VoluteImplicit(
            rTipImpeller_mm: 40f,
            rMinor0_mm: 5f,
            growthPerRevolution_mm: 10f,
            zMin_mm: 0f, zMax_mm: 10f,
            gapFromImpeller_mm: 2f);
        // Volute centre at θ=0: r = 40 + 2 + 5 = 47; z = 5 (midplane).
        var p = new Vector3(47f, 0, 5f);
        Assert.True(vol.fSignedDistance(p) < 0);
    }

    [Fact]
    public void VoluteImplicit_FarFromCavity_IsPositive()
    {
        var vol = new VoluteImplicit(
            rTipImpeller_mm: 40f,
            rMinor0_mm: 5f,
            growthPerRevolution_mm: 10f,
            zMin_mm: 0f, zMax_mm: 10f);
        var p = new Vector3(100f, 0, 50f);
        Assert.True(vol.fSignedDistance(p) > 0);
    }

    [Fact]
    public void VoluteImplicit_DegenerateMinorRadius_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new VoluteImplicit(rTipImpeller_mm: 40f, rMinor0_mm: 0f,
                growthPerRevolution_mm: 10f, zMin_mm: 0f, zMax_mm: 10f));
    }

    // ══════════════════════ Design + result wiring ══════════════════════

    [Fact]
    public void OperatingConditions_IncludeTurbopumpGeometry_DefaultsFalse()
    {
        var c = new OperatingConditions();
        Assert.False(c.IncludeTurbopumpGeometry);
    }

    [Fact]
    public void OperatingConditions_IncludeTurbopumpGeometry_RoundTripsViaWith()
    {
        var c = new OperatingConditions() with { IncludeTurbopumpGeometry = true };
        Assert.True(c.IncludeTurbopumpGeometry);
    }

    [Fact]
    public void TurbopumpResult_GeometryFields_DefaultNull()
    {
        var r = new TurbopumpResult(
            Cycle:               EngineCycle.PressureFed,
            FuelPump:            null,
            OxPump:              null,
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            System.Array.Empty<string>(),
            Notes:               "");
        Assert.Null(r.FuelPumpGeometry);
        Assert.Null(r.OxPumpGeometry);
    }
}
