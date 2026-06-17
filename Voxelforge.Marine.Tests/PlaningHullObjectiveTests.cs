// PlaningHullObjectiveTests.cs — Sprint M.W3 objective-adapter tests.
// Covers Pack/Unpack round-trip, bounds shape, kind-mismatch, and the
// IObjective.Evaluate happy + infeasible paths.

using System;
using Voxelforge.Marine;
using Voxelforge.Marine.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class PlaningHullObjectiveTests
{
    private static MarineConditions DefaultConditions() => new(
        CruiseSpeed_ms: 12.86,
        MaxDepth_m:      0.0);

    private static MarineDesign BaselinePlaning() => new(
        Kind:                MarineKind.SurfaceHull,
        Length_m:           11.0,
        Diameter_m:          1.0,
        NoseFairingFraction: 0.25,
        TailFairingFraction: 0.25,
        WallThickness_m:     0.005,
        MaterialIndex:       0,
        DepthRating_m:       1.0,
        HullFamily:          HullFamily.Planing)
    {
        BeamMidship_m          =     3.0,
        DeadriseAngle_deg      =    18.0,
        MassDisplacement_kg    =  5000.0,
        FreeboardHeight_m      =     0.6,
        LongitudinalCgFraction =     0.50,
    };

    // ── Pack / Unpack round-trip ────────────────────────────────────────

    [Fact]
    public void PackThenUnpack_RoundTripsEveryField()
    {
        var original = BaselinePlaning();
        double[] vec = PlaningHullObjective.Pack(original);
        var restored = PlaningHullObjective.Unpack(vec, original.FreeboardHeight_m);
        Assert.Equal(original.Length_m,               restored.Length_m,               precision: 6);
        Assert.Equal(original.BeamMidship_m,          restored.BeamMidship_m,          precision: 6);
        Assert.Equal(original.DeadriseAngle_deg,      restored.DeadriseAngle_deg,      precision: 6);
        Assert.Equal(original.MassDisplacement_kg,    restored.MassDisplacement_kg,    precision: 6);
        Assert.Equal(original.LongitudinalCgFraction, restored.LongitudinalCgFraction, precision: 6);
        Assert.Equal(original.FreeboardHeight_m,      restored.FreeboardHeight_m,      precision: 6);
        Assert.Equal(MarineKind.SurfaceHull,          restored.Kind);
        Assert.Equal(HullFamily.Planing,              restored.HullFamily);
    }

    [Fact]
    public void Pack_ReturnsFiveElementVector()
    {
        double[] vec = PlaningHullObjective.Pack(BaselinePlaning());
        Assert.Equal(5, vec.Length);
        Assert.Equal(PlaningHullObjective.DefaultVariableNames.Length, vec.Length);
    }

    [Fact]
    public void Pack_PreservesElementOrder()
    {
        double[] vec = PlaningHullObjective.Pack(BaselinePlaning());
        Assert.Equal(11.0,   vec[0], precision: 6);
        Assert.Equal( 3.0,   vec[1], precision: 6);
        Assert.Equal(18.0,   vec[2], precision: 6);
        Assert.Equal(5000.0, vec[3], precision: 6);
        Assert.Equal( 0.50,  vec[4], precision: 6);
    }

    // ── Bounds shape ────────────────────────────────────────────────────

    [Fact]
    public void DefaultBounds_MatchVariableNamesShape()
    {
        Assert.Equal(PlaningHullObjective.DefaultVariableNames.Length, PlaningHullObjective.DefaultBounds.Length);
        for (int i = 0; i < PlaningHullObjective.DefaultBounds.Length; i++)
            Assert.Equal(PlaningHullObjective.DefaultVariableNames[i], PlaningHullObjective.DefaultBounds[i].Name);
    }

    [Fact]
    public void DefaultBounds_AreOrderedAndPositive()
    {
        foreach (var b in PlaningHullObjective.DefaultBounds)
        {
            Assert.True(b.Min < b.Max, $"{b.Name}: Min={b.Min} ≥ Max={b.Max}");
            Assert.True(b.Min > 0,     $"{b.Name}: Min={b.Min} not strictly positive");
        }
    }

    // ── IObjective.Evaluate ─────────────────────────────────────────────

    [Fact]
    public void Evaluate_BaselineYacht_ReturnsFiniteScore()
    {
        var obj = PlaningHullObjective.WithDefaultBounds(DefaultConditions());
        double[] vec = PlaningHullObjective.Pack(BaselinePlaning());
        var result = obj.Evaluate(vec);
        Assert.True(result.Score < double.PositiveInfinity);
        Assert.True(result.Score > 0,
            $"Total resistance should be positive; got {result.Score}");
    }

    [Fact]
    public void Evaluate_DesignWithInvalidBeam_ReturnsInfiniteScore()
    {
        // Beam = 0 fails the Savitsky solver's positive-input guard.
        var obj = PlaningHullObjective.WithDefaultBounds(DefaultConditions());
        double[] bad = PlaningHullObjective.Pack(BaselinePlaning());
        bad[1] = 0.0;
        var result = obj.Evaluate(bad);
        Assert.Equal(double.PositiveInfinity, result.Score);
    }

    [Fact]
    public void Evaluate_VectorWrongLength_Throws()
    {
        var obj = PlaningHullObjective.WithDefaultBounds(DefaultConditions());
        Assert.Throws<ArgumentException>(() => obj.Evaluate(new double[3]));
    }

    [Fact]
    public void Constructor_NullConditions_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new PlaningHullObjective(null!, PlaningHullObjective.DefaultBounds));

    [Fact]
    public void Constructor_NullVariables_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new PlaningHullObjective(DefaultConditions(), null!));

    [Fact]
    public void Constructor_NonPositiveFreeboard_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlaningHullObjective(
                DefaultConditions(), PlaningHullObjective.DefaultBounds, freeboardHeight_m: 0.0));

    [Fact]
    public void Unpack_WrongVectorLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => PlaningHullObjective.Unpack(new double[3]));
    }
}
