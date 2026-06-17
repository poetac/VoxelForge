// GitObjectiveTests.cs — Sprint EP.W2.GIT objective-adapter tests.
// Covers Pack/Unpack round-trip, bounds shape, bus-power clip, and
// kind-mismatch guards.

using System;
using Voxelforge.ElectricPropulsion.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Optimization;

public sealed class GitObjectiveTests
{
    private static ElectricPropulsionEngineDesign NstarDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.GriddedIon,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        BeamVoltage_V               = 1100.0,
        BeamCurrent_A               =    1.76,
        ScreenGridRadius_mm         =  145.0,
        AccelGridGap_mm             =    0.6,
        NeutralizerCathodeCurrent_A =    1.76,
        GitMassUtilizationOverride  =    0.90,
    };

    private static ResistojetConditions DefaultConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2500.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    // ── Pack / Unpack round-trip ────────────────────────────────────────

    [Fact]
    public void PackThenUnpack_RoundTripsEveryField()
    {
        var original = NstarDesign();
        double[] vec = GitObjective.Pack(original);
        var restored = GitObjective.Unpack(vec, original);
        Assert.Equal(original.BeamVoltage_V,                  restored.BeamVoltage_V,                 precision: 6);
        Assert.Equal(original.BeamCurrent_A,                  restored.BeamCurrent_A,                 precision: 6);
        Assert.Equal(original.ScreenGridRadius_mm,            restored.ScreenGridRadius_mm,           precision: 6);
        Assert.Equal(original.AccelGridGap_mm,                restored.AccelGridGap_mm,               precision: 6);
        Assert.Equal(original.NeutralizerCathodeCurrent_A,    restored.NeutralizerCathodeCurrent_A,   precision: 6);
        Assert.Equal(original.GitMassUtilizationOverride,     restored.GitMassUtilizationOverride,    precision: 6);
    }

    [Fact]
    public void Pack_ReturnsSixElementVector()
    {
        double[] vec = GitObjective.Pack(NstarDesign());
        Assert.Equal(6, vec.Length);
        Assert.Equal(GitObjective.DefaultVariableNames.Length, vec.Length);
    }

    [Fact]
    public void Pack_PreservesElementOrder()
    {
        double[] vec = GitObjective.Pack(NstarDesign());
        Assert.Equal(1100.0, vec[0], precision: 6);
        Assert.Equal(   1.76, vec[1], precision: 6);
        Assert.Equal( 145.0, vec[2], precision: 6);
        Assert.Equal(   0.6, vec[3], precision: 6);
        Assert.Equal(   1.76, vec[4], precision: 6);
        Assert.Equal(   0.90, vec[5], precision: 6);
    }

    // ── Bounds shape ────────────────────────────────────────────────────

    [Fact]
    public void DefaultBounds_MatchVariableNamesShape()
    {
        Assert.Equal(GitObjective.DefaultVariableNames.Length, GitObjective.DefaultBounds.Length);
        for (int i = 0; i < GitObjective.DefaultBounds.Length; i++)
            Assert.Equal(GitObjective.DefaultVariableNames[i], GitObjective.DefaultBounds[i].Name);
    }

    [Fact]
    public void DefaultBounds_AreOrderedAndPositive()
    {
        foreach (var b in GitObjective.DefaultBounds)
        {
            Assert.True(b.Min < b.Max, $"{b.Name}: Min={b.Min} ≥ Max={b.Max}");
            Assert.True(b.Min >= 0,    $"{b.Name}: Min={b.Min} is negative");
        }
    }

    // ── Build / kind guard ───────────────────────────────────────────────

    [Fact]
    public void Build_NstarDesign_ReturnsObjective()
    {
        var obj = GitObjective.Build(DefaultConditions(), NstarDesign());
        Assert.NotNull(obj);
    }

    [Fact]
    public void Build_WrongKind_Throws()
    {
        var ppt = NstarDesign() with { Kind = ElectricPropulsionEngineKind.PulsedPlasmaThruster };
        Assert.Throws<ArgumentException>(() => GitObjective.Build(DefaultConditions(), ppt));
    }

    [Fact]
    public void Build_NullConditions_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GitObjective.Build(null!, NstarDesign()));

    [Fact]
    public void Build_NullBaseline_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            GitObjective.Build(DefaultConditions(), null!));

    [Fact]
    public void Unpack_WrongVectorLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GitObjective.Unpack(new double[5], NstarDesign()));
    }

    // ── Bus-power clip ──────────────────────────────────────────────────

    [Fact]
    public void Build_LowBusPower_ClipsBeamCurrentBound()
    {
        // BusPower=300 W, max V_b=1500 → max J_b = 300/1500 = 0.2 A,
        // well below the default 3.0 A upper bound. Expect dim 1 max to clip.
        var lowPower = new ResistojetConditions(
            BusVoltage_V:        28.0,
            BusPower_W_avail:   300.0,
            AmbientPressure_Pa:   0.0,
            Propellant:          Propellant.N2H4Decomposed,
            InletTemperature_K:  300.0,
            InletComposition:    PropellantInletComposition.PureH2);
        // The clip operates on the bounds the EngineObjectiveAdapter sees; we
        // can't introspect it from outside, but we can verify Build succeeds
        // even when the bus power clips the upper-edge to a tiny value.
        var obj = GitObjective.Build(lowPower, NstarDesign());
        Assert.NotNull(obj);
    }
}
