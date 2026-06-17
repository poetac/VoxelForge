// BB-4 (2026-04-29): canonical design fixtures for BDN microbenches.
//
// Mirrors the seed shapes used by `Voxelforge.Benchmarks/
// CanonicalDesigns.cs` (which is internal to that assembly). We
// duplicate the EngineSpec values rather than promoting CanonicalDesigns
// to public — the spec values are stable enough that drift between
// the two is acceptable for micro-bench purposes, and the cross-
// assembly coupling is avoided.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks.Helpers;

internal static class CanonicalDesignFixtures
{
    public static (OperatingConditions cond, RegenChamberDesign design)
        MerlinSeed()
    {
        var spec = new EngineSpec(
            PropellantPair:      PropellantPair.LOX_CH4,
            Thrust_N:            15_000.0,
            ChamberPressure_Pa:  4e6,
            ExpansionRatio:      16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = AutoSeeder.Seed(spec);
        var cond = ConditionsFromSpec(spec);
        return (cond, seed.Design);
    }

    public static (OperatingConditions cond, RegenChamberDesign design)
        AerospikeSeed()
    {
        var spec = new EngineSpec(
            PropellantPair:          PropellantPair.LOX_CH4,
            Thrust_N:                20_000.0,
            ChamberPressure_Pa:      7e6,
            ExpansionRatio:          15.0,
            ChannelTopologyOverride: ChannelTopology.Aerospike);
        var seed = AutoSeeder.Seed(spec);
        var cond = ConditionsFromSpec(spec);
        return (cond, seed.Design);
    }

    public static (OperatingConditions cond, RegenChamberDesign design)
        Rl10Seed()
    {
        var spec = new EngineSpec(
            PropellantPair:      PropellantPair.LOX_H2,
            Thrust_N:            100_000.0,
            ChamberPressure_Pa:  4e6,
            ExpansionRatio:      84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var seed = AutoSeeder.Seed(spec);
        var cond = ConditionsFromSpec(spec);
        return (cond, seed.Design);
    }

    public static (OperatingConditions cond, RegenChamberDesign design)
        PintleSeed()
    {
        var spec = new EngineSpec(
            PropellantPair:      PropellantPair.LOX_CH4,
            Thrust_N:            10_000.0,
            ChamberPressure_Pa:  6e6,
            ExpansionRatio:      25.0,
            ElementTypeOverride: "Pintle");
        var raw = AutoSeeder.Seed(spec);
        var design = raw.Design with { OuterJacketThickness_mm = 6.0 };
        var cond = ConditionsFromSpec(spec);
        return (cond, design);
    }

    // ExpansionRatio lives on RegenChamberDesign, not OperatingConditions.
    // The seed already has it baked in via AutoSeeder.Seed; the conditions
    // record only carries the propulsion-side knobs.
    private static OperatingConditions ConditionsFromSpec(EngineSpec spec) =>
        new()
        {
            PropellantPair      = spec.PropellantPair,
            Thrust_N            = spec.Thrust_N,
            ChamberPressure_Pa  = spec.ChamberPressure_Pa,
        };
}
