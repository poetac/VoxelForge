// NoyronTierC4PolishTests.cs — Tier C polish follow-on tests:
//   • PreburnerVoxelGeometry: capsule sizing from PreburnerResult.ChamberVolume_mm3.
//   • PumpMountFlange: annular-disc + bolt-hole sizing.
//   • FeedBendImplicit fillet: backwards-compatible constructor + non-zero fillet radius.
//
// The tests run as pure-math forcing functions — no PicoGK Library
// init required (same pattern as NoyronTierC4Tests.cs), so they
// slot into the existing test suite without needing the Benchmarks
// console-app escape hatch.

using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Turbopump;

namespace Voxelforge.Tests;

public class NoyronTierC4PolishTests
{
    // ════════════════════════════════════════════════════════════
    //  Preburner voxel geometry — Tier C2 follow-on
    // ════════════════════════════════════════════════════════════

    private static PreburnerResult SamplePreburnerResult(double volume_mm3 = 1_000_000.0)
    {
        return new PreburnerResult(
            Cycle:                  EngineCycle.StagedCombustion,
            MixtureRatio:           0.60,
            ChamberPressure_Pa:     10e6,
            WarmGasTemperature_K:   900,
            WarmGasCStar_ms:        1800,
            WarmGasGamma:           1.20,
            WarmGasMolecularWeight: 22,
            MassFlow_kgs:           5,
            CharacteristicLength_m: 0.40,
            ChamberVolume_mm3:      volume_mm3,
            Notes:                  "test",
            Warnings:               System.Array.Empty<string>());
    }

    [Fact]
    public void PreburnerVoxel_Size_InnerRadiusMatchesVolumeFormula()
    {
        var pre = SamplePreburnerResult(1_000_000);
        var vox = PreburnerVoxel.Size(pre);
        // Derived inner radius must satisfy V = π·r³·(2·ld + 4/3) @ ld=1.0.
        double expectedR = System.Math.Pow(
            1_000_000 / (System.Math.PI * (2.0 + 4.0 / 3.0)),
            1.0 / 3.0);
        Assert.Equal(expectedR, vox.InnerRadius_mm, precision: 6);
    }

    [Fact]
    public void PreburnerVoxel_Size_OuterIsInnerPlusWall()
    {
        var vox = PreburnerVoxel.Size(SamplePreburnerResult(), wallThickness_mm: 3.0);
        Assert.Equal(vox.InnerRadius_mm + 3.0, vox.OuterRadius_mm, precision: 6);
    }

    [Fact]
    public void PreburnerVoxel_Size_TotalLengthIncludesHemispheres()
    {
        var vox = PreburnerVoxel.Size(SamplePreburnerResult());
        // Total length = cylindrical portion + 2 × outerR (two hemispheres).
        Assert.Equal(vox.CylinderLength_mm + 2 * vox.OuterRadius_mm, vox.TotalLength_mm, precision: 6);
    }

    [Fact]
    public void PreburnerVoxel_Size_MassMonotonicWithVolume()
    {
        var small = PreburnerVoxel.Size(SamplePreburnerResult(500_000));
        var large = PreburnerVoxel.Size(SamplePreburnerResult(2_000_000));
        Assert.True(large.EstimatedMass_g > small.EstimatedMass_g);
    }

    [Fact]
    public void PreburnerVoxel_Size_NullThrows()
    {
        Assert.Throws<System.ArgumentNullException>(() => PreburnerVoxel.Size(null!));
    }

    [Fact]
    public void PreburnerVoxel_Size_ZeroVolumeThrows()
    {
        var zero = SamplePreburnerResult(0);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => PreburnerVoxel.Size(zero));
    }

    [Fact]
    public void PreburnerCapsule_InsideWallIsNegative()
    {
        var vox = PreburnerVoxel.Size(SamplePreburnerResult());
        var imp = new PreburnerCapsuleImplicit(
            cylLength_mm: (float)vox.CylinderLength_mm,
            outerR_mm: (float)vox.OuterRadius_mm,
            wallThickness_mm: (float)vox.WallThickness_mm);
        // Sample radially within the wall, halfway along cylinder.
        float r = (float)(vox.InnerRadius_mm + vox.WallThickness_mm * 0.5);
        var p = new Vector3((float)(vox.CylinderLength_mm * 0.5), r, 0);
        Assert.True(imp.fSignedDistance(p) < 0);
    }

    [Fact]
    public void PreburnerCapsule_InsideCavityIsPositive()
    {
        var vox = PreburnerVoxel.Size(SamplePreburnerResult());
        var imp = new PreburnerCapsuleImplicit(
            cylLength_mm: (float)vox.CylinderLength_mm,
            outerR_mm: (float)vox.OuterRadius_mm,
            wallThickness_mm: (float)vox.WallThickness_mm);
        // Point at the axis, midway — should be in the cavity (positive SDF).
        var p = new Vector3((float)(vox.CylinderLength_mm * 0.5), 0, 0);
        Assert.True(imp.fSignedDistance(p) > 0);
    }

    [Fact]
    public void PreburnerCapsule_FarOutsideIsPositive()
    {
        var vox = PreburnerVoxel.Size(SamplePreburnerResult());
        var imp = new PreburnerCapsuleImplicit(
            cylLength_mm: (float)vox.CylinderLength_mm,
            outerR_mm: (float)vox.OuterRadius_mm,
            wallThickness_mm: (float)vox.WallThickness_mm);
        var far = new Vector3(0, (float)(vox.OuterRadius_mm * 10), 0);
        Assert.True(imp.fSignedDistance(far) > 0);
    }

    [Fact]
    public void PreburnerCapsule_NonPositiveWallThrows()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new PreburnerCapsuleImplicit(cylLength_mm: 50, outerR_mm: 10, wallThickness_mm: 0));
    }

    [Fact]
    public void PreburnerCapsule_WallGreaterThanOuterThrows()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new PreburnerCapsuleImplicit(cylLength_mm: 50, outerR_mm: 10, wallThickness_mm: 15));
    }

    // ════════════════════════════════════════════════════════════
    //  Pump mount flange — Tier C3 follow-on
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void PumpMountFlange_Size_DefaultsAreSane()
    {
        var g = PumpMountFlange.Size(casingOuterRadius_mm: 40);
        Assert.Equal(40, g.InnerRadius_mm, precision: 6);
        Assert.Equal(40 + PumpMountFlange.DefaultRadialProjection_mm, g.OuterRadius_mm, precision: 6);
        Assert.Equal(PumpMountFlange.DefaultThickness_mm, g.Thickness_mm, precision: 6);
        Assert.Equal(PumpMountFlange.DefaultBoltCount, g.BoltCount);
        Assert.True(g.EstimatedMass_g > 0);
    }

    [Fact]
    public void PumpMountFlange_Size_BoltCircleBetweenInnerAndOuter()
    {
        var g = PumpMountFlange.Size(casingOuterRadius_mm: 40);
        Assert.InRange(g.BoltCircleRadius_mm, g.InnerRadius_mm, g.OuterRadius_mm);
    }

    [Fact]
    public void PumpMountFlange_Size_ZeroRadiusThrows()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PumpMountFlange.Size(casingOuterRadius_mm: 0));
    }

    [Fact]
    public void PumpMountFlange_Size_BoltHoleLargerThanProjectionThrows()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PumpMountFlange.Size(
                casingOuterRadius_mm: 40,
                radialProjection_mm: 10,
                boltHoleDiameter_mm: 12));   // wider than 0.9 × 10 mm projection
    }

    [Fact]
    public void PumpMountFlangeImplicit_InsideMaterialIsNegative()
    {
        var g = PumpMountFlange.Size(casingOuterRadius_mm: 40);
        var imp = new PumpMountFlangeImplicit(g);
        // Sample at z = t/2, r = midway between inner and outer, angle between bolts.
        float r = (float)(0.5 * (g.InnerRadius_mm + g.OuterRadius_mm) + 0.25 * (g.OuterRadius_mm - g.InnerRadius_mm));
        // Avoid bolt positions by choosing an angle offset between bolts.
        float theta = MathF.PI / g.BoltCount;  // between bolts 0 and 1
        var p = new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), (float)(g.Thickness_mm * 0.5));
        Assert.True(imp.fSignedDistance(p) < 0);
    }

    [Fact]
    public void PumpMountFlangeImplicit_OutsideMaterialIsPositive()
    {
        var g = PumpMountFlange.Size(casingOuterRadius_mm: 40);
        var imp = new PumpMountFlangeImplicit(g);
        // Point well above the flange axially — outside.
        var p = new Vector3(50, 0, 50);
        Assert.True(imp.fSignedDistance(p) > 0);
    }

    [Fact]
    public void PumpMountFlangeImplicit_InsideBoltHoleIsPositive()
    {
        var g = PumpMountFlange.Size(casingOuterRadius_mm: 40);
        var imp = new PumpMountFlangeImplicit(g);
        // Sample at the first bolt's centre.
        float bx = (float)(g.BoltCircleRadius_mm * MathF.Cos(0));
        float by = (float)(g.BoltCircleRadius_mm * MathF.Sin(0));
        var p = new Vector3(bx, by, (float)(g.Thickness_mm * 0.5));
        // Inside the bolt hole (in the flange's axial extent) → positive SDF.
        Assert.True(imp.fSignedDistance(p) > 0);
    }

    [Fact]
    public void PumpMountFlange_MassScalesWithCasingRadius()
    {
        var small = PumpMountFlange.Size(casingOuterRadius_mm: 30);
        var large = PumpMountFlange.Size(casingOuterRadius_mm: 60);
        Assert.True(large.EstimatedMass_g > small.EstimatedMass_g);
    }

    // ════════════════════════════════════════════════════════════
    //  Feed-bend fillet — Tier C4 follow-on
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void FeedBend_V452Ctor_ProducesSameSdfAsZeroFillet()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(50, 0, 0);
        var c = new Vector3(50, 30, 0);
        var legacy = new FeedBendImplicit(a, b, c, outerRadius_mm: 5f);
        var withZero = new FeedBendImplicit(a, b, c, outerRadius_mm: 5f, filletRadius_mm: 0f);
        // Sample a grid and assert bit-identical SDFs.
        for (int x = -10; x <= 60; x += 7)
        for (int y = -10; y <= 40; y += 7)
        {
            var p = new Vector3(x, y, 2);
            float d1 = legacy.fSignedDistance(p);
            float d2 = withZero.fSignedDistance(p);
            Assert.Equal(d1, d2);
        }
    }

    [Fact]
    public void FeedBend_WithFillet_AddsMaterialAtCorner()
    {
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(50, 0, 0);
        var c = new Vector3(50, 30, 0);
        var bare = new FeedBendImplicit(a, b, c, outerRadius_mm: 5f);
        var filleted = new FeedBendImplicit(a, b, c, outerRadius_mm: 5f, filletRadius_mm: 6f);
        // Sample a point *inside* the filleted arc (at the bend's
        // interior, just off the mitre line). The fillet must either
        // preserve-or-decrease the SDF (= add or preserve material).
        // It should never *increase* the SDF above the mitred value.
        for (int y = 1; y < 20; y += 3)
        for (int x = 40; x < 50; x += 3)
        {
            var p = new Vector3(x, y, 0);
            float mitred = bare.fSignedDistance(p);
            float fille = filleted.fSignedDistance(p);
            Assert.True(fille <= mitred + 1e-4f,
                $"fillet should add/preserve material; mitred={mitred}, fille={fille} @ {p}");
        }
    }

    [Fact]
    public void FeedBend_WithFillet_CollinearIsUnchanged()
    {
        // Start → corner → end collinear: no bend, no fillet region.
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(25, 0, 0);
        var c = new Vector3(50, 0, 0);
        var bare = new FeedBendImplicit(a, b, c, outerRadius_mm: 5f);
        var filleted = new FeedBendImplicit(a, b, c, outerRadius_mm: 5f, filletRadius_mm: 10f);
        // Should behave identically (fillet mathematically degenerates).
        var p = new Vector3(25, 2, 0);
        Assert.Equal(bare.fSignedDistance(p), filleted.fSignedDistance(p));
    }

    [Fact]
    public void FeedTube_FilletRadius_RoundTripsThroughRecord()
    {
        var t = new FeedTube(
            Label: "fuel-discharge",
            Start_mm: new Vector3(0, 0, 0),
            Corner_mm: new Vector3(50, 0, 0),
            End_mm: new Vector3(50, 30, 0),
            OuterRadius_mm: 5.0,
            FilletRadius_mm: 8.0);
        Assert.Equal(8.0, t.FilletRadius_mm);
    }

    [Fact]
    public void FeedTube_FilletRadius_DefaultIsZero()
    {
        // Legacy construction must still work (default fillet = 0).
        var t = new FeedTube(
            Label: "fuel-feed",
            Start_mm: new Vector3(0, 0, 0),
            Corner_mm: null,
            End_mm: new Vector3(50, 0, 0),
            OuterRadius_mm: 5.0);
        Assert.Equal(0.0, t.FilletRadius_mm);
    }

    [Fact]
    public void Route_FilletRadiusThreadedToBentTubes()
    {
        var inj = new Vector3(0, 0, 0);
        var fuelIn = new Vector3(50, 80, -40);
        var fuelDis = new Vector3(80, 80, 20);
        var oxIn = new Vector3(50, -80, -40);
        var oxDis = new Vector3(20, -80, 20);
        var layout = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, inj, fuelIn, fuelDis, oxIn, oxDis,
            bendFilletRadius_mm: 8.0);
        // Bent tubes carry the fillet radius; straight tubes stay at 0.
        foreach (var t in layout.Tubes)
        {
            if (t.Corner_mm is not null)
                Assert.Equal(8.0, t.FilletRadius_mm);
            else
                Assert.Equal(0.0, t.FilletRadius_mm);
        }
    }

    [Fact]
    public void Route_DefaultFilletRadiusIsZero()
    {
        // Default call (no fillet arg) must preserve legacy mitred behaviour.
        var inj = new Vector3(0, 0, 0);
        var fuelIn = new Vector3(50, 80, -40);
        var fuelDis = new Vector3(80, 80, 20);
        var oxIn = new Vector3(50, -80, -40);
        var oxDis = new Vector3(20, -80, 20);
        var layout = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, inj, fuelIn, fuelDis, oxIn, oxDis);
        foreach (var t in layout.Tubes)
            Assert.Equal(0.0, t.FilletRadius_mm);
    }

    [Fact]
    public void DefaultBendFilletRadius_IsPositive()
    {
        // The constant should advertise a sane default so the monolithic
        // builder opts in automatically. Zero would make the default
        // reproduce the legacy mitred behaviour identically — we want
        // the opposite.
        Assert.True(FeedManifoldRouter.DefaultBendFilletRadius_mm > 0);
    }
}
