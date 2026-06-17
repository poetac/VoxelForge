// CylHemiFairingGeometryTests.cs — unit tests for CylHemiFairingGeometry (Wave-2 M2).

using System;
using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class CylHemiFairingGeometryTests
{
    // REMUS-100-class CylHemi: L=1.595m, D=0.190m, R=0.095m
    private static MarineDesign MakeCylHemiDesign(
        double length = 1.595, double diameter = 0.190) => new(
        Kind:                MarineKind.AuvMidBody,
        Length_m:            length,
        Diameter_m:          diameter,
        NoseFairingFraction: 0.10,   // ignored for CylindricalHemi
        TailFairingFraction: 0.10,   // ignored for CylindricalHemi
        WallThickness_m:     0.005,
        MaterialIndex:       1,
        DepthRating_m:       100.0,
        HullFamily:          HullFamily.CylindricalHemi);

    // ── Geometry ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_WettedArea_EqualsPI_D_L()
    {
        var g = CylHemiFairingGeometry.Compute(MakeCylHemiDesign());
        double expected = Math.PI * 0.190 * 1.595;
        Assert.Equal(expected, g.WettedArea_m2, precision: 9);
    }

    [Fact]
    public void Compute_ExternalVolume_MatchesClosedForm()
    {
        double d = 0.190, l = 1.595;
        var g = CylHemiFairingGeometry.Compute(MakeCylHemiDesign(l, d));
        double expected = (Math.PI / 6.0) * d * d * d
                        + (Math.PI / 4.0) * d * d * (l - d);
        Assert.Equal(expected, g.ExternalVolume_m3, precision: 9);
    }

    [Fact]
    public void Compute_NoseTailLength_EachEqualsRadius()
    {
        var g = CylHemiFairingGeometry.Compute(MakeCylHemiDesign());
        double r = 0.190 / 2.0;
        Assert.Equal(r, g.NoseLength_m, precision: 9);
        Assert.Equal(r, g.TailLength_m, precision: 9);
    }

    [Fact]
    public void Compute_MidBodyLength_EqualsLengthMinusDiameter()
    {
        var g = CylHemiFairingGeometry.Compute(MakeCylHemiDesign());
        double expected = 1.595 - 0.190;
        Assert.Equal(expected, g.MidBodyLength_m, precision: 9);
    }

    // ── RadiusAt profile ─────────────────────────────────────────────────────

    // REMUS-100-class: L=1.595, R=0.095
    // x=0.000 → nose tip: r=0
    // x=0.095 → hemisphere equator: r=R=0.095
    // x=0.500 → cylinder: r=R=0.095
    // x=1.500 → cylinder: r=R=0.095
    // x=1.595 → tail tip: r≈0

    [Theory]
    [InlineData(0.000, 0.000)]        // nose tip
    [InlineData(0.095, 0.095)]        // hemisphere equator = max radius
    [InlineData(0.500, 0.095)]        // cylinder section
    [InlineData(1.500, 0.095)]        // cylinder section
    [InlineData(1.595, 0.000)]        // tail tip
    public void RadiusAt_KnownStations_MatchExpected(double x, double expectedR)
    {
        // CylHemiFairingGeometry.RadiusAt is internal; access via reflection
        var method = typeof(CylHemiFairingGeometry).GetMethod(
            "RadiusAt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        double r = (double)method!.Invoke(null, new object[] { x, 1.595, 0.095 })!;
        // precision: 6 (within 1e-6 m) — at tip stations, FP cancellation in the
        // hemisphere sqrt can produce ~1e-8 residual; 6 decimal places is meaningful
        // for geometry (1 µm tolerance) and immune to the cancellation.
        Assert.Equal(expectedR, r, precision: 6);
    }

    [Fact]
    public void RadiusAt_NoseHemi_NeverExceedsRadius()
    {
        var method = typeof(CylHemiFairingGeometry).GetMethod(
            "RadiusAt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        for (int i = 0; i <= 20; i++)
        {
            double x = i * 0.005;   // 0 .. 0.095 in steps of 0.005
            double r = (double)method!.Invoke(null, new object[] { x, 1.595, 0.095 })!;
            Assert.True(r <= 0.095 + 1e-10, $"r({x:F3}) = {r} > R");
        }
    }

    [Fact]
    public void RadiusAt_IsSymmetric()
    {
        var method = typeof(CylHemiFairingGeometry).GetMethod(
            "RadiusAt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        double l = 1.595, radius = 0.095;
        for (int i = 0; i <= 10; i++)
        {
            double xNose = i * 0.009;
            double xTail = l - xNose;
            double rNose = (double)method!.Invoke(null, new object[] { xNose, l, radius })!;
            double rTail = (double)method!.Invoke(null, new object[] { xTail, l, radius })!;
            // precision: 6 (1 µm) — FP cancellation at tip stations can give ~1e-8 residual
            Assert.Equal(rNose, rTail, precision: 6);
        }
    }

    // ── Validation guard ─────────────────────────────────────────────────────

    [Fact]
    public void Compute_ThrowsWhen_DiameterEqualToLength()
    {
        var design = MakeCylHemiDesign(length: 0.190, diameter: 0.190);
        Assert.Throws<ArgumentException>(() => CylHemiFairingGeometry.Compute(design));
    }

    [Fact]
    public void Compute_ThrowsWhen_DiameterExceedsLength()
    {
        var design = MakeCylHemiDesign(length: 0.150, diameter: 0.190);
        Assert.Throws<ArgumentException>(() => CylHemiFairingGeometry.Compute(design));
    }

    // ── Physics round-trip ───────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_CylHemi_IsFeasible()
    {
        var design = MakeCylHemiDesign();
        var cond   = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0);
        var result = MarineOptimization.GenerateWith(design, cond);
        Assert.True(result.IsFeasible,
            $"Expected feasible. Violations: {string.Join(", ", result.Violations)}");
    }

    [Fact]
    public void GenerateWith_CylHemi_BuoyancyIsPositive()
    {
        var design = MakeCylHemiDesign();
        var cond   = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0);
        var result = MarineOptimization.GenerateWith(design, cond);
        Assert.True(result.BuoyancyForce_N > 0);
    }

    [Fact]
    public void GenerateWith_CylHemi_BucklingSfExceedsFloor()
    {
        var design = MakeCylHemiDesign();
        var cond   = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0);
        var result = MarineOptimization.GenerateWith(design, cond);
        Assert.True(result.BucklingSafetyFactor >= 1.5,
            $"SF = {result.BucklingSafetyFactor:F3} must be ≥ 1.5");
    }

    [Fact]
    public void GenerateWith_CylHemi_IsDeterministic()
    {
        var design = MakeCylHemiDesign();
        var cond   = new MarineConditions(CruiseSpeed_ms: 1.5, MaxDepth_m: 100.0);
        var r1 = MarineOptimization.GenerateWith(design, cond);
        var r2 = MarineOptimization.GenerateWith(design, cond);
        Assert.Equal(r1.DragForce_N,                 r2.DragForce_N);
        Assert.Equal(r1.BuoyancyForce_N,             r2.BuoyancyForce_N);
        Assert.Equal(r1.CriticalBucklingPressure_Pa, r2.CriticalBucklingPressure_Pa);
        Assert.Equal(r1.HullMass_kg,                 r2.HullMass_kg);
    }
}
