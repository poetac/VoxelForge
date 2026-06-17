// AutoSeederSeedingFixTests — pins the SA dim-20 / dim-21 seeding fix
// added by the handoff item #4 audit (`handoff-2026-04-28-evening.md`).
//
// Pattern recap (same as dims 24-25 / 26-27 / 28-30):
//   • RegenChamberDesign exposes an "override-style" SA dim with default
//     value 0.0 and a non-zero SA bound minimum.
//   • RegenChamberOptimization.Pack(seed) packs 0.0 into the SA candidate
//     vector; SetInitialCandidate (Optimizer.cs) clamps every dim to
//     [lo[i], hi[i]] so 0.0 → SA min on iter=0.
//   • Production reads `if (override > 0)` — preflight (design = 0)
//     reaches a fallback (cond field, geometry default, …); SA iter=0
//     reads the SA-min override and silently uses different physics.
//   • Fix: AutoSeeder seeds the design's override dim from the same
//     value the production fallback uses, so `Pack(seed)` produces
//     a vector that survives `SetInitialCandidate` clamping unchanged
//     and SA iter=0 evaluates the same physics the preflight does.
//
// This file covers dims 20 (PreburnerMrRatio) + 21 (FlangeRadialProjection_mm).
// Sibling tests for dims 24-30 live alongside their respective sprints.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class AutoSeederSeedingFixTests
{
    // ── dim 20: PreburnerMrRatio ────────────────────────────────────────

    [Theory]
    [InlineData(EngineCycle.GasGenerator,    PropellantPair.LOX_CH4, 0.60)]
    [InlineData(EngineCycle.GasGenerator,    PropellantPair.LOX_H2,  0.80)]
    [InlineData(EngineCycle.GasGenerator,    PropellantPair.LOX_RP1, 0.40)]
    [InlineData(EngineCycle.StagedCombustion, PropellantPair.LOX_CH4, 0.60)]
    [InlineData(EngineCycle.FullFlow,        PropellantPair.LOX_CH4, 0.60)]
    [InlineData(EngineCycle.TapOff,          PropellantPair.LOX_RP1, 0.40)]
    public void PreburnerCycles_SeedDesignDim20FromSuggestPreburnerMr(
        EngineCycle cycle, PropellantPair pair, double expectedMr)
    {
        var spec = new EngineSpec(
            PropellantPair: pair,
            Thrust_N: 200_000.0,
            ChamberPressure_Pa: 12e6,
            ExpansionRatio: 25.0,
            EngineCycleOverride: cycle);
        var seed = AutoSeeder.Seed(spec);

        // The design override is seeded from the same SuggestPreburnerMr
        // value that cond.PreburnerMrRatio holds. Both must match so SA
        // iter=0 (which reads design.PreburnerMrRatio first per
        // RegenChamberOptimization.ResolveFuelRichMr) sees the same MR
        // the preflight does (which reads cond.PreburnerMrRatio after the
        // design check returns 0).
        Assert.Equal(expectedMr, seed.Design.PreburnerMrRatio, precision: 4);
        Assert.Equal(expectedMr, seed.Conditions.PreburnerMrRatio, precision: 4);
    }

    [Theory]
    [InlineData(EngineCycle.PressureFed)]
    [InlineData(EngineCycle.ElectricPump)]
    [InlineData(EngineCycle.OpenExpander)]
    public void NonPreburnerCycles_SeedDesignDim20AsZero(EngineCycle cycle)
    {
        // PressureFed / ElectricPump / OpenExpander all return 0 from
        // SuggestPreburnerMr (no preburner). The seed must keep
        // design.PreburnerMrRatio at 0 so the back-compat path is bit-
        // identical for these cycles. SA iter=0 still clamps 0 → SA min
        // 0.30 but production never reads design.PreburnerMrRatio (cycle
        // solver's HasFuelRich/HasOxRichPreburner both false → early
        // return) so the value is irrelevant there.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: cycle);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(0.0, seed.Design.PreburnerMrRatio);
    }

    [Fact]
    public void PressureFed_NoCycleOverride_PreburnerMrRatioStaysZero()
    {
        // Default path (no EngineCycleOverride): preMr never computed,
        // design.PreburnerMrRatio stays at the record default (0.0).
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 50_000.0,
            ChamberPressure_Pa: 5e6,
            ExpansionRatio: 12.0);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(0.0, seed.Design.PreburnerMrRatio);
    }

    // ── dim 21: FlangeRadialProjection_mm ───────────────────────────────

    [Theory]
    [InlineData(PropellantPair.LOX_CH4)]
    [InlineData(PropellantPair.LOX_H2)]
    [InlineData(PropellantPair.LOX_RP1)]
    public void Seed_AlwaysSetsFlangeRadialProjectionTo12mm(PropellantPair pair)
    {
        // PumpMountFlange.DefaultRadialProjection_mm = 12.0 in
        // Voxelforge.Voxels (Core can't reference Voxels). The
        // seed pins 12.0 explicitly so SA iter=0 doesn't shrink the flange
        // to the SA min (8.0 mm) on a turbopump preset. Non-turbopump
        // presets don't build a flange so the 12.0 value is irrelevant
        // there but harmless. Constant value is pinned here — if
        // PumpMountFlange.DefaultRadialProjection_mm changes the seed
        // must change to match, and this test will fail on drift.
        var spec = new EngineSpec(
            PropellantPair: pair,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio: 16.0);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(12.0, seed.Design.FlangeRadialProjection_mm);
    }

    // ── Pack survives clamping unchanged ────────────────────────────────

    [Fact]
    public void Pack_PreburnerCycle_Dim20SurvivesSetInitialCandidateClamping()
    {
        // The end-to-end seeding contract: Pack(seed)[20] must land
        // strictly inside the SA bound [0.30, 1.00] for a preburner
        // cycle so SetInitialCandidate's `Math.Clamp(p[20], lo, hi)` is
        // a no-op. If preMr falls outside the band a future propellant
        // pair adds (currently all SuggestPreburnerMr returns 0.40-0.80
        // → in band), this test will surface the drift.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 200_000.0,
            ChamberPressure_Pa: 15e6,
            ExpansionRatio: 30.0,
            EngineCycleOverride: EngineCycle.StagedCombustion);
        var seed = AutoSeeder.Seed(spec);
        var packed = RegenChamberOptimization.Pack(seed.Design);
        var bounds = RegenChamberOptimization.Bounds;

        Assert.True(packed[20] >= bounds[20].Min,
            $"Pack(seed)[20] = {packed[20]} below SA min {bounds[20].Min} "
          + "→ SetInitialCandidate would clamp UP, breaking seed→SA continuity.");
        Assert.True(packed[20] <= bounds[20].Max,
            $"Pack(seed)[20] = {packed[20]} above SA max {bounds[20].Max} "
          + "→ SetInitialCandidate would clamp DOWN, breaking seed→SA continuity.");
    }

    [Fact]
    public void Pack_AnyCycle_Dim21SurvivesSetInitialCandidateClamping()
    {
        // FlangeRadialProjection_mm bound is [8.0, 24.0]; seeded at 12.0
        // sits cleanly in the middle. Pin against drift.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = AutoSeeder.Seed(spec);
        var packed = RegenChamberOptimization.Pack(seed.Design);
        var bounds = RegenChamberOptimization.Bounds;

        Assert.Equal(12.0, packed[21], precision: 6);
        Assert.True(packed[21] >= bounds[21].Min && packed[21] <= bounds[21].Max,
            $"Pack(seed)[21] = {packed[21]} outside SA bound "
          + $"[{bounds[21].Min}, {bounds[21].Max}].");
    }

    // ── dims 26 + 27: Pintle injector overrides ─────────────────────
    //
    // The same override-style seeding pattern applies to the pintle
    // dims. AutoSeeder seeds them from `injectorPattern.PintleDiameter_mm`
    // / `PintleSleeveHoleCount` when the resolved element type is
    // "Pintle", and 0.0 otherwise. The test guards against regression
    // where a future refactor of the seeder forgets to seed the override
    // for a Pintle preset, leaving SetInitialCandidate to clamp dim 26
    // up to SA min (6.0 mm) and dim 27 to SA min (8 holes) on iter=0.
    //
    // For non-Pintle designs the override stays at 0 (and clamps to SA
    // min on iter=0), but production reads the override behind a
    // `if (elementType == "Pintle")` gate so the SA-min value is
    // ignored at evaluation time. The "test" for non-Pintle is just
    // that the seed value is 0 so the gate path is exercised.

    [Fact]
    public void LoxRP1_DefaultsToPintle_SeedsDim26AndDim27ToInjectorPatternValues()
    {
        // LOX/RP-1 → AutoSeeder.InjectorElementTypeFor returns "Pintle"
        // → InjectorFaceLayout produces a pintle pattern → seed pulls
        // PintleDiameter_mm / PintleSleeveHoleCount onto the design's
        // override slots so SA iter=0 evaluates the same physics the
        // preflight does.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_RP1,
            Thrust_N: 50_000.0,
            ChamberPressure_Pa: 6e6,
            ExpansionRatio: 14.0);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal("Pintle", seed.Design.InjectorElementPattern!.ElementType);
        Assert.True(seed.Design.PintleDiameterOverride_mm > 0,
            "Pintle preset should seed dim 26 from injectorPattern.PintleDiameter_mm "
          + $"but got {seed.Design.PintleDiameterOverride_mm}");
        Assert.True(seed.Design.PintleSleeveHoleCountOverride > 0,
            "Pintle preset should seed dim 27 from injectorPattern.PintleSleeveHoleCount "
          + $"but got {seed.Design.PintleSleeveHoleCountOverride}");

        // Cross-check: the seeded value matches the pattern's value so
        // the SA candidate vector survives SetInitialCandidate clamping.
        Assert.Equal(seed.Design.InjectorElementPattern!.PintleDiameter_mm,
            seed.Design.PintleDiameterOverride_mm, precision: 6);
        Assert.Equal((double)seed.Design.InjectorElementPattern!.PintleSleeveHoleCount,
            seed.Design.PintleSleeveHoleCountOverride, precision: 6);
    }

    [Fact]
    public void Pack_PintleSeed_Dim26AndDim27_LandWithinSABounds()
    {
        // Bound check: the seeded values must land inside SA bounds
        // [Bounds[26].Min, Bounds[26].Max] = [6.0, 30.0] for diameter
        // and [Bounds[27].Min, Bounds[27].Max] = [8.0, 32.0] for hole
        // count, otherwise SetInitialCandidate would clamp them on
        // iter=0 and re-introduce the seeding bug. Pintle hardware
        // typically lands in [10, 25] mm × [10, 24] holes — well
        // inside the SA band.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_RP1,
            Thrust_N: 50_000.0,
            ChamberPressure_Pa: 6e6,
            ExpansionRatio: 14.0);
        var seed = AutoSeeder.Seed(spec);
        var packed = RegenChamberOptimization.Pack(seed.Design);
        var bounds = RegenChamberOptimization.Bounds;

        Assert.True(packed[26] >= bounds[26].Min && packed[26] <= bounds[26].Max,
            $"Pack(seed)[26] = {packed[26]} outside SA bound "
          + $"[{bounds[26].Min}, {bounds[26].Max}] — seed must land inside "
          + "the band so SetInitialCandidate doesn't clamp on iter=0.");

        Assert.True(packed[27] >= bounds[27].Min && packed[27] <= bounds[27].Max,
            $"Pack(seed)[27] = {packed[27]} outside SA bound "
          + $"[{bounds[27].Min}, {bounds[27].Max}].");
    }

    [Fact]
    public void NonPintle_SeedsDim26AndDim27ToZero_GatedAtEvaluation()
    {
        // LOX/CH4 default elementType = "Coax" → pintle override
        // slots stay at 0. SA iter=0 will clamp to SA min (6.0 / 8.0)
        // but production gates on elementType == "Pintle" so the
        // SA-min value is ignored. This test pins the seed = 0
        // contract for non-Pintle so a future refactor can't
        // accidentally set non-zero values that would leak into a
        // Coax design's evaluation.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = AutoSeeder.Seed(spec);

        Assert.NotEqual("Pintle", seed.Design.InjectorElementPattern!.ElementType);
        Assert.Equal(0.0, seed.Design.PintleDiameterOverride_mm);
        Assert.Equal(0.0, seed.Design.PintleSleeveHoleCountOverride);
    }
}
