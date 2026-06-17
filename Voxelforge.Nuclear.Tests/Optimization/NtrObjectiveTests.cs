// NtrObjectiveTests.cs — direct tests for NtrObjective (factory + Pack /
// Unpack). Per audit 05-test-gaps.md §5 the type had no dedicated tests;
// this PR mirrors the existing ResistojetObjective / PptObjective shape.

using System;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Nuclear.Tests.Optimization;

public sealed class NtrObjectiveTests
{
    private static NuclearThermalDesign MakeNrxA6() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions MakeCond()
        => new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    // ── Vector-layout invariants (pillar spec §2) ────────────────────────

    [Fact]
    public void DefaultVariableNames_Has6Slots_InDocumentedOrder()
    {
        // Vector layout is load-bearing: Pack / Unpack reorder based on it.
        // Pillar spec §2 fixes the slot order.
        Assert.Equal(6, NtrObjective.DefaultVariableNames.Length);
        Assert.Equal("ReactorThermalPower_MW",  NtrObjective.DefaultVariableNames[0]);
        Assert.Equal("PropellantMassFlow_kgs",  NtrObjective.DefaultVariableNames[1]);
        Assert.Equal("ChamberPressure_bar",     NtrObjective.DefaultVariableNames[2]);
        Assert.Equal("ThroatRadius_mm",         NtrObjective.DefaultVariableNames[3]);
        Assert.Equal("ExpansionRatio",          NtrObjective.DefaultVariableNames[4]);
        Assert.Equal("RegenChannelDepth_mm",    NtrObjective.DefaultVariableNames[5]);
    }

    [Fact]
    public void DefaultBounds_MatchPillarSpec_NumericRanges()
    {
        // Audit cross-check: pillar spec §2 publishes these envelopes.
        // ReactorThermalPower_MW [50, 2000], PropellantMassFlow_kgs [1, 50],
        // ChamberPressure_bar [25, 80], ThroatRadius_mm [5, 200],
        // ExpansionRatio [20, 200], RegenChannelDepth_mm [0.5, 5.0].
        Assert.Equal(6, NtrObjective.DefaultBounds.Length);
        Assert.Equal(  50.0, NtrObjective.DefaultBounds[0].Min);
        Assert.Equal(2000.0, NtrObjective.DefaultBounds[0].Max);
        Assert.Equal(   1.0, NtrObjective.DefaultBounds[1].Min);
        Assert.Equal(  50.0, NtrObjective.DefaultBounds[1].Max);
        Assert.Equal(  25.0, NtrObjective.DefaultBounds[2].Min);
        Assert.Equal(  80.0, NtrObjective.DefaultBounds[2].Max);
        Assert.Equal(   5.0, NtrObjective.DefaultBounds[3].Min);
        Assert.Equal( 200.0, NtrObjective.DefaultBounds[3].Max);
        Assert.Equal(  20.0, NtrObjective.DefaultBounds[4].Min);
        Assert.Equal( 200.0, NtrObjective.DefaultBounds[4].Max);
        Assert.Equal(   0.5, NtrObjective.DefaultBounds[5].Min);
        Assert.Equal(   5.0, NtrObjective.DefaultBounds[5].Max);
    }

    [Fact]
    public void NervaBounds_NarrowerThanDefault_AtBothEnds()
    {
        // NERVA cluster bounds tighten the default envelope to the NRX-A6
        // operating regime. Sanity: each dim's bounds sit fully inside the
        // wide defaults (Min ≥ default Min, Max ≤ default Max).
        for (int i = 0; i < NtrObjective.DefaultBounds.Length; i++)
        {
            var d = NtrObjective.DefaultBounds[i];
            var n = NtrObjective.NervaBounds[i];
            Assert.Equal(d.Name, n.Name);
            Assert.True(n.Min >= d.Min,
                $"Dim {i} ({n.Name}): NERVA Min {n.Min} should be ≥ default Min {d.Min}.");
            Assert.True(n.Max <= d.Max,
                $"Dim {i} ({n.Name}): NERVA Max {n.Max} should be ≤ default Max {d.Max}.");
        }
    }

    // ── Build factory ────────────────────────────────────────────────────

    [Fact]
    public void Build_Default_ProducesObjectiveWith6DimVariableLayout()
    {
        var obj = NtrObjective.Build(MakeCond(), MakeNrxA6());
        Assert.Equal(6, obj.Variables.Count);
        Assert.Equal("ReactorThermalPower_MW", obj.Variables[0].Name);
        Assert.Equal("RegenChannelDepth_mm",   obj.Variables[5].Name);
    }

    [Fact]
    public void Build_DefaultBounds_AreNervaBounds()
    {
        // When `variables` is omitted, Build falls back to NervaBounds.
        var obj = NtrObjective.Build(MakeCond(), MakeNrxA6());
        for (int i = 0; i < NtrObjective.NervaBounds.Length; i++)
        {
            Assert.Equal(NtrObjective.NervaBounds[i].Name, obj.Variables[i].Name);
            Assert.Equal(NtrObjective.NervaBounds[i].Min,  obj.Variables[i].Min);
            Assert.Equal(NtrObjective.NervaBounds[i].Max,  obj.Variables[i].Max);
        }
    }

    [Fact]
    public void Build_CustomVariables_AreApplied()
    {
        var customBounds = new DesignVariableInfo[]
        {
            new("ReactorThermalPower_MW",   500.0, 1500.0),
            new("PropellantMassFlow_kgs",    20.0,   40.0),
            new("ChamberPressure_bar",       34.0,   34.0001),
            new("ThroatRadius_mm",          120.0,  120.0001),
            new("ExpansionRatio",           100.0,  100.0001),
            new("RegenChannelDepth_mm",       2.0,    2.0001),
        };
        var obj = NtrObjective.Build(MakeCond(), MakeNrxA6(), customBounds);
        Assert.Equal( 500.0, obj.Variables[0].Min);
        Assert.Equal(1500.0, obj.Variables[0].Max);
        Assert.Equal(  20.0, obj.Variables[1].Min);
        Assert.Equal(  40.0, obj.Variables[1].Max);
    }

    // ── Typed exceptions on Build ────────────────────────────────────────

    [Fact]
    public void Build_NullConditions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NtrObjective.Build(null!, MakeNrxA6()));
    }

    [Fact]
    public void Build_NullBaseline_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NtrObjective.Build(MakeCond(), null!));
    }

    [Fact]
    public void Build_WrongLengthVariables_ThrowsArgumentOutOfRangeException()
    {
        // Pillar spec fixes the vector at 6 dims; passing 4 must throw the
        // typed exception that documents the size mismatch.
        var shortBounds = new DesignVariableInfo[]
        {
            new("a", 0.0, 1.0),
            new("b", 0.0, 1.0),
            new("c", 0.0, 1.0),
            new("d", 0.0, 1.0),
        };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NtrObjective.Build(MakeCond(), MakeNrxA6(), shortBounds));
    }

    // ── Pack ─────────────────────────────────────────────────────────────

    [Fact]
    public void Pack_NrxA6_HasExpectedSlotValues()
    {
        double[] v = NtrObjective.Pack(MakeNrxA6());
        Assert.Equal(6, v.Length);
        Assert.Equal(1100.0, v[0]);  // ReactorThermalPower_MW
        Assert.Equal(  33.0, v[1]);  // PropellantMassFlow_kgs
        Assert.Equal(  34.0, v[2]);  // ChamberPressure_bar
        Assert.Equal( 120.0, v[3]);  // ThroatRadius_mm
        Assert.Equal( 100.0, v[4]);  // ExpansionRatio
        Assert.Equal(   2.0, v[5]);  // RegenChannelDepth_mm
    }

    [Fact]
    public void Pack_NullDesign_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => NtrObjective.Pack(null!));
    }

    // ── Unpack ──────────────────────────────────────────────────────────

    [Fact]
    public void Unpack_OverridesPositionalFields_PreservesCategoricalState()
    {
        var baseline = MakeNrxA6();
        double[] v   = { 800.0, 25.0, 45.0, 100.0, 80.0, 1.5 };
        var refined  = NtrObjective.Unpack(v, baseline);

        // Vector dims write through.
        Assert.Equal( 800.0, refined.ReactorThermalPower_MW);
        Assert.Equal(  25.0, refined.PropellantMassFlow_kgs);
        Assert.Equal(  45.0, refined.ChamberPressure_bar);
        Assert.Equal( 100.0, refined.ThroatRadius_mm);
        Assert.Equal(  80.0, refined.ExpansionRatio);
        Assert.Equal(   1.5, refined.RegenChannelDepth_mm);
        // Categorical / non-vector state preserved from baseline.
        Assert.Equal(baseline.Kind,                    refined.Kind);
        Assert.Equal(baseline.ReactorCoreLength_mm,    refined.ReactorCoreLength_mm);
        Assert.Equal(baseline.ReactorCoreDiameter_mm,  refined.ReactorCoreDiameter_mm);
        Assert.Equal(baseline.FuelLoadingFraction,     refined.FuelLoadingFraction);
        Assert.Equal(baseline.NozzleLength_mm,         refined.NozzleLength_mm);
        Assert.Equal(baseline.RegenChannelCount,       refined.RegenChannelCount);
        Assert.Equal(baseline.NozzleWallThickness_mm,  refined.NozzleWallThickness_mm);
    }

    [Fact]
    public void Unpack_PackRoundTrip_IsIdentityOnVectorDims()
    {
        var baseline = MakeNrxA6();
        double[] v   = NtrObjective.Pack(baseline);
        var restored = NtrObjective.Unpack(v, baseline);
        Assert.Equal(baseline.ReactorThermalPower_MW, restored.ReactorThermalPower_MW);
        Assert.Equal(baseline.PropellantMassFlow_kgs, restored.PropellantMassFlow_kgs);
        Assert.Equal(baseline.ChamberPressure_bar,    restored.ChamberPressure_bar);
        Assert.Equal(baseline.ThroatRadius_mm,        restored.ThroatRadius_mm);
        Assert.Equal(baseline.ExpansionRatio,         restored.ExpansionRatio);
        Assert.Equal(baseline.RegenChannelDepth_mm,   restored.RegenChannelDepth_mm);
    }

    [Fact]
    public void Unpack_NullVector_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NtrObjective.Unpack(null!, MakeNrxA6()));
    }

    [Fact]
    public void Unpack_NullBaseline_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NtrObjective.Unpack(new double[] { 1, 2, 3, 4, 5, 6 }, null!));
    }

    [Fact]
    public void Unpack_WrongLengthVector_ThrowsArgumentOutOfRangeException()
    {
        // 6-dim contract; 4-dim vector must throw the typed range exception.
        double[] shortVec = { 1, 2, 3, 4 };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NtrObjective.Unpack(shortVec, MakeNrxA6()));
    }

    // ── End-to-end scoring (smoke) ───────────────────────────────────────

    [Fact]
    public void Build_Evaluate_OnNervaVector_ProducesFiniteScore()
    {
        // Score = −Isp on feasible solves; +∞ on infeasible. Either is
        // acceptable for the published NRX-A6 baseline; both must be
        // finite-or-+∞ (no NaN).
        var obj = NtrObjective.Build(MakeCond(), MakeNrxA6());
        double[] v = NtrObjective.Pack(MakeNrxA6());
        var eval = obj.Evaluate(v.AsSpan());
        Assert.False(double.IsNaN(eval.Score));
    }
}
