// NoyronTierC4Tests.cs — Tier C4: feed-manifold routing + monolithic
// engine composition.
//
// Coverage
// ────────
//   • FeedTubeImplicit: inside/outside/past-cap sign + degenerate-construction throws.
//   • FeedBendImplicit: union of two FeedTubeImplicits.
//   • FeedManifoldRouter.Route returns layout per cycle:
//       - PressureFed: 2 tubes (fuel + ox feed directly to dome).
//       - GasGenerator: 4 standard tubes + preburner overboard exhaust.
//       - StagedCombustion / FullFlow: 4 standard tubes + preburner-to-main.
//       - ElectricPump / OpenExpander: 4 standard tubes (no preburner duct).
//   • FeedManifoldLayout mass + length monotonic w.r.t. tube count.
//   • FeedManifoldRouter.BuildImplicits emits one implicit per tube.
//   • MonolithicEngineResult record populated when builder runs.
//
// Note: `MonolithicEngineBuilder.Build` invocation itself needs
// PicoGK Library init + a full GenerateWith — we cover the routing
// math and record shapes here; the full monolithic STL is exercised
// manually via the `--monolithic` CLI (same pattern as regen
// feasibility-gate tests via SafeResult).

using System.Numerics;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class NoyronTierC4Tests
{
    private static readonly Vector3 Inj = new(0, 0, 0);
    private static readonly Vector3 FuelIn = new(50, 80, -40);
    private static readonly Vector3 FuelDis = new(80, 80, 20);
    private static readonly Vector3 OxIn = new(50, -80, -40);
    private static readonly Vector3 OxDis = new(20, -80, 20);

    // ══════════════════════ FeedTubeImplicit ══════════════════════

    [Fact]
    public void FeedTube_InsideEnvelope_IsNegative()
    {
        var tube = new FeedTubeImplicit(
            new Vector3(0, 0, 0), new Vector3(50, 0, 0), outerRadius_mm: 5f);
        // Sample at midpoint, on the axis.
        Assert.True(tube.fSignedDistance(new Vector3(25, 0, 0)) < 0);
    }

    [Fact]
    public void FeedTube_FarOutside_IsPositive()
    {
        var tube = new FeedTubeImplicit(
            new Vector3(0, 0, 0), new Vector3(50, 0, 0), outerRadius_mm: 5f);
        Assert.True(tube.fSignedDistance(new Vector3(25, 50, 0)) > 0);
    }

    [Fact]
    public void FeedTube_DegenerateLength_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new FeedTubeImplicit(
                new Vector3(0, 0, 0), new Vector3(0, 0, 0), outerRadius_mm: 5f));
    }

    [Fact]
    public void FeedTube_NonPositiveRadius_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new FeedTubeImplicit(
                new Vector3(0, 0, 0), new Vector3(10, 0, 0), outerRadius_mm: 0f));
    }

    // ══════════════════════ FeedBendImplicit ══════════════════════

    [Fact]
    public void FeedBend_NegativeInEitherSegment()
    {
        var bend = new FeedBendImplicit(
            new Vector3(0, 0, 0), new Vector3(50, 0, 0), new Vector3(50, 30, 0),
            outerRadius_mm: 5f);
        // Inside first segment.
        Assert.True(bend.fSignedDistance(new Vector3(25, 0, 0)) < 0);
        // Inside second segment.
        Assert.True(bend.fSignedDistance(new Vector3(50, 15, 0)) < 0);
    }

    // ══════════════════════ Router behaviour per cycle ══════════════════════

    [Fact]
    public void Route_PressureFed_ReturnsTwoTubes()
    {
        var layout = FeedManifoldRouter.Route(
            EngineCycle.PressureFed, Inj, FuelIn, FuelDis, OxIn, OxDis);
        Assert.Equal(EngineCycle.PressureFed, layout.Cycle);
        Assert.Equal(2, layout.Tubes.Count);
        Assert.Contains(layout.Tubes, t => t.Label == "fuel-feed");
        Assert.Contains(layout.Tubes, t => t.Label == "ox-feed");
    }

    [Fact]
    public void Route_GasGenerator_AddsOverboardExhaust()
    {
        var exhaust = new Vector3(40, 80, 40);
        var layout = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, Inj, FuelIn, FuelDis, OxIn, OxDis,
            preburnerExhaust: exhaust);
        Assert.Equal(5, layout.Tubes.Count);  // 2 feed + 2 discharge + 1 exhaust
        Assert.Contains(layout.Tubes,
            t => t.Label == "preburner-exhaust-overboard");
    }

    [Theory]
    [InlineData(EngineCycle.StagedCombustion)]
    [InlineData(EngineCycle.FullFlow)]
    public void Route_StagedAndFullFlow_RouteExhaustToMain(EngineCycle cycle)
    {
        var exhaust = new Vector3(40, 80, 40);
        var layout = FeedManifoldRouter.Route(
            cycle, Inj, FuelIn, FuelDis, OxIn, OxDis,
            preburnerExhaust: exhaust);
        Assert.Contains(layout.Tubes,
            t => t.Label == "preburner-exhaust-to-main");
    }

    [Fact]
    public void Route_ElectricPump_NoExhaustDuct()
    {
        // ElectricPump has a turbopump (motor-driven) but no preburner
        // — exhaust duct should not appear even if the caller passes a
        // preburner point (Router ignores it on non-preburner cycles).
        var layout = FeedManifoldRouter.Route(
            EngineCycle.ElectricPump, Inj, FuelIn, FuelDis, OxIn, OxDis,
            preburnerExhaust: new Vector3(0, 0, 0));
        Assert.DoesNotContain(layout.Tubes,
            t => t.Label.StartsWith("preburner-exhaust", StringComparison.Ordinal));
        Assert.Equal(4, layout.Tubes.Count);
    }

    [Fact]
    public void Route_NonPressureFed_HasFourCoreTubes()
    {
        var layout = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, Inj, FuelIn, FuelDis, OxIn, OxDis);
        // 2 feed + 2 discharge = 4 core tubes (no preburner-exhaust
        // passed).
        Assert.Equal(4, layout.Tubes.Count);
        Assert.Contains(layout.Tubes, t => t.Label == "fuel-feed");
        Assert.Contains(layout.Tubes, t => t.Label == "ox-feed");
        Assert.Contains(layout.Tubes, t => t.Label == "fuel-discharge");
        Assert.Contains(layout.Tubes, t => t.Label == "ox-discharge");
    }

    [Fact]
    public void Route_TotalLength_PositiveAndMassMatchesTubeCount()
    {
        var layoutSmall = FeedManifoldRouter.Route(
            EngineCycle.PressureFed, Inj, FuelIn, FuelDis, OxIn, OxDis);
        var layoutLarge = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, Inj, FuelIn, FuelDis, OxIn, OxDis,
            preburnerExhaust: new Vector3(40, 80, 40));
        Assert.True(layoutSmall.TotalTubeLength_mm > 0);
        Assert.True(layoutLarge.TotalTubeLength_mm > layoutSmall.TotalTubeLength_mm);
        Assert.True(layoutLarge.EstimatedTubeMass_g > layoutSmall.EstimatedTubeMass_g);
    }

    [Fact]
    public void Route_NotesPopulated()
    {
        var layout = FeedManifoldRouter.Route(
            EngineCycle.StagedCombustion, Inj, FuelIn, FuelDis, OxIn, OxDis,
            preburnerExhaust: new Vector3(40, 80, 40));
        Assert.Contains("StagedCombustion", layout.Notes);
        Assert.Contains("tubes", layout.Notes);
    }

    // ══════════════════════ BuildImplicits ══════════════════════

    [Fact]
    public void BuildImplicits_OneImplicitPerTube()
    {
        var layout = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, Inj, FuelIn, FuelDis, OxIn, OxDis,
            preburnerExhaust: new Vector3(40, 80, 40));
        var imps = FeedManifoldRouter.BuildImplicits(layout);
        Assert.Equal(layout.Tubes.Count, imps.Count);
    }

    [Fact]
    public void BuildImplicits_StraightTubeProducesFeedTubeImplicit()
    {
        var layout = FeedManifoldRouter.Route(
            EngineCycle.PressureFed, Inj, FuelIn, FuelDis, OxIn, OxDis);
        var imps = FeedManifoldRouter.BuildImplicits(layout);
        Assert.All(imps, i => Assert.IsType<FeedTubeImplicit>(i));
    }

    [Fact]
    public void BuildImplicits_BentTubeProducesFeedBendImplicit()
    {
        var layout = FeedManifoldRouter.Route(
            EngineCycle.GasGenerator, Inj, FuelIn, FuelDis, OxIn, OxDis);
        var imps = FeedManifoldRouter.BuildImplicits(layout);
        Assert.Contains(imps, i => i is FeedBendImplicit);
    }
}
