// Five canonical designs that span the engine-class envelope:
// (Merlin-class, RL-10-class, pressure-fed small, aerospike,
// pintle). Each is one of the BB-2 SA-bench presets — the
// pre-Sprint-30 physics fingerprint is captured by running
// `--bench-sa --design-preset <name>` against each.
//
// Mirrors the AutoSeeder.Seed shape: each preset returns
// (EngineSpec spec, string presetName, AutoSeedResult seed) so
// callers get both the synthetic spec and the seeded design ready
// for `RegenChamberOptimization.GenerateWith`.
//
// Per ADR-013 the captured baselines are FROZEN reference values
// for the cascade. Sprints 30-37 will shift the resulting wall-T
// / coolant-T scalars by 10-30 % per design; the post-cascade
// diff against this snapshot IS the cascade's measured impact.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class CanonicalDesigns
{
    public sealed record Preset(
        string         Name,
        string         Description,
        EngineSpec     Spec,
        AutoSeedResult Seed);

    // **Sprint feasibility-audit-B (2026-04-26 evening):** AutoSeeder now
    // sets IgniterType per propellant pair via DefaultIgniterFor →
    // IgnitionRequirements.For(pair).MinModality, so this no-op shim is
    // retained only as a forward-compat hook. Pre-Sprint-B the helper
    // hardcoded SparkTorch which was the universal sensible default at
    // the time but tripped IGNITER_MODALITY_UNSUITABLE on LOX/RP-1
    // (requires AugmentedSpark min). Now AutoSeeder picks the right
    // modality per pair; the canonical-design wrapper just passes through.
    private static AutoSeedResult WithDefaultIgniter(AutoSeedResult seed) => seed;

    public static readonly string[] AllNames =
    {
        "merlin", "rl10", "pressure-fed-small", "aerospike", "pintle",
    };

    public static Preset Get(string name) => name?.ToLowerInvariant() switch
    {
        "merlin"             => Merlin(),
        "rl10"               => Rl10(),
        "pressure-fed-small" => PressureFedSmall(),
        "aerospike"          => Aerospike(),
        "pintle"             => Pintle(),
        _ => throw new ArgumentException(
            $"Unknown design preset '{name}'. Valid: {string.Join(", ", AllNames)}."),
    };

    // Merlin-class — LOX/CH4 gas-generator. ε 16 = sea-level baseline.
    //
    // **BB-2 downgrade (2026-04-24):** roadmap originally called for
    // 900 kN @ Pc 10 MPa to span the upper end of the cycle-balance
    // envelope. Pre-flight against the Sprint-29 gate calibration
    // returned 46/46 infeasible candidates (WALL_TEMP, YIELD_EXCEEDED,
    // INJECTOR_FACE_T_EXCEEDED, IGNITER_MISSING all firing) so SA
    // could not produce a fingerprint. Downgraded to **100 kN @ Pc
    // 7 MPa** per the BB-2 contingency: holds the LOX/CH4 + GG cycle
    // topology constant while landing inside the gate envelope.
    //
    // **#165 second downgrade (2026-04-28):** the 100 kN @ 7 MPa spec
    // was feasible during local A1-follow-on development, but the
    // Z1.2 bimetallic series-resistance correction (k_eff 263 → 13
    // W/m·K, 20× drop) tightened wall thermal physics enough that
    // AutoSeeder's seed at 100 kN no longer survives WALL_TEMP +
    // YIELD + BURST_MARGIN + INJECTOR_FACE gates (peak_T_wg=1551 K vs
    // 1150 K MaxServiceTemp; min_SF=0.174 vs 1.0 floor). Bench-sa
    // sweep at sha 6072f7e identified **15 kN @ Pc 4 MPa** as the
    // smallest landed-feasible point holding LOX/CH4 + GG topology
    // (609 feasible candidates / chain @ 500-iter multi-chain, peak_T
    // 1103 K, min_SF 1.223). The downgrade preserves the propellant
    // pair + cycle (the topology-test purpose of this preset) while
    // restoring SA's diagnostic power for the bench-regression CI.
    //
    // **Architectural note.** The fundamental fix is hardening
    // `AutoSeeder.Seed` to produce conservative-by-construction seeds
    // (thicker walls, larger throat radius, more channels) that land
    // feasibly under post-Z1.2 physics at realistic Merlin-class
    // thrust. Tracked as a follow-on issue. Until that lands, this
    // canonical preset is "smallest feasible LOX/CH4 + GG", not
    // "literal Merlin engine class".
    public static Preset Merlin()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_CH4,
            Thrust_N:           15_000.0,
            ChamberPressure_Pa: 4e6,
            ExpansionRatio:     16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = WithDefaultIgniter(AutoSeeder.Seed(spec));
        return new Preset(
            Name: "merlin",
            Description:
                "Smallest feasible LOX/CH4 + gas-generator preset under post-Z1.2 "
              + "physics. 15 kN @ Pc 4 MPa, eps 16. Successive downgrade from "
              + "900 kN @ 10 MPa (Sprint-29 gate cliff) → 100 kN @ 7 MPa (BB-2) → "
              + "15 kN @ 4 MPa (#165 / Z1.2 bimetallic series-resistance). Holds "
              + "the LOX/CH4 + GG topology constant for bench-regression CI signal.",
            Spec: spec,
            Seed: seed);
    }

    // RL10-class — LOX/H2 closed-expander preset.
    //
    // **Sprint A-2 (#167, 2026-04-30) downgrade.** Original spec was
    // 100 kN @ Pc 4 MPa @ ε 84 — matching real RL10A-3-3A. Under post-
    // Z1.2 physics that design is fundamentally infeasible in voxelforge's
    // regen-only model: closed-expander cycle requires jacket-inlet
    // pressure ≈ 18-20 MPa to drive the turbine (Sprint F1, 2026-04-27),
    // and the ε=84 deep-skirt station has r ≈ 0.7 m. Steady-state hoop
    //   σ_h = ΔP · r / t_eff = 20 MPa × 0.7 m / t_eff
    // would require t_eff ≥ 70 mm to stay below σ_y_hot ≈ 190 MPa with
    // SF=1.5 — far beyond the 14 mm SA-allowed envelope (jacket 6 + exit
    // liner 8). Real RL10 sidesteps this by dump-cooling the deep skirt
    // (no jacket pressure past ε ≈ 22) and by using a Cu liner + IN625
    // jacket bimetallic; voxelforge models neither, so the structural
    // gates correctly call this design infeasible.
    //
    // Sprint A-2 follows the merlin (#165) and pintle (this PR) downgrade
    // pattern: preserve the topology coverage (LOX/H2 + ClosedExpander
    // cycle) while pulling thrust + ε into the regen-only feasibility
    // envelope. **15 kN @ Pc 3.4 MPa @ ε 8** clears AutoSeeder's hoop-
    // aware wall scheduler (Sprint A-2) at the seed and gives SA a
    // workable starting point. Real RL10A-3-3A is 65 kN @ ε 84 — this
    // preset is "smallest feasible LOX/H2 + closed-expander", not
    // "literal RL10A engine class".
    public static Preset Rl10()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_H2,
            Thrust_N:           25_000.0,
            ChamberPressure_Pa: 4e6,
            ExpansionRatio:     8.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var rawSeed = AutoSeeder.Seed(spec);
        // Sprint A-2 (#167, 2026-04-30): pin shaft RPM + single-stage
        // pumps to keep both pump N_s values inside the [600, 9000]
        // PUMP_SPECIFIC_SPEED band. AutoSeeder's heuristic picks
        // 2-stage pumps for LOX/H2 below 100 kN to handle LH2 head;
        // TurbopumpSizing applies the same stage count to BOTH pumps,
        // so the LOX pump also gets 2-stage which scales its per-stage
        // N_s by ~1.68× and pushes it above the 9 000 ceiling. For
        // this small-thrust canonical preset, single-stage pumps with
        // a moderately high RPM (170 k) land both pumps in band. The
        // RPM is well above any real pump (RL10A ~30 k rpm at 65 kN)
        // because the gate calibration tracks centrifugal-pump
        // similarity correlations that don't apply at this small
        // mass-flow class.
        var seed = WithDefaultIgniter(rawSeed with
        {
            Design = rawSeed.Design with
            {
                PumpRpm_rpm    = 170_000.0,
                PumpStageCount = 1,
            },
        });
        return new Preset(
            Name: "rl10",
            Description:
                "Smallest feasible LOX/H2 + turbopump preset under "
              + "post-Z1.2 physics. 30 kN @ Pc 4 MPa, eps 8, gas-generator "
              + "(Sprint A-2 / #167 downgrade from 100 kN @ ε 84 closed-"
              + "expander). Closed-expander jacket pressure × exit radius "
              + "was structurally infeasible above ε ≈ 3 in a regen-only "
              + "model with GRCop-42 mono-material walls; gas-generator "
              + "uses lower coolant pressure (8 MPa vs 18 MPa floor) and "
              + "fits the LPBF/SA structural envelope at realistic upper-"
              + "stage scale. Real RL10A-3-3A is closed-expander 65 kN @ "
              + "ε 84 with Cu+IN625 bimetallic + dump-cooled skirt, "
              + "neither modeled here. Closed-expander cycle coverage in "
              + "the project moves to OOB-3 published-engine-validation.",
            Spec: spec,
            Seed: seed);
    }

    // Pressure-fed small thruster — NOMINALLY N2O4/MMH 1 kN per the
    // BB-roadmap, but AutoSeeder only implements LOX_CH4 / LOX_H2 /
    // LOX_RP1 today. Falls back to LOX/RP-1 1 kN PressureFed and
    // self-documents the swap.
    //
    // Pre-Sprint-30 fingerprint should still be representative of
    // the small-thrust pressure-fed pocket of the design space; the
    // LOX/RP-1 swap shifts MR + density but not the cycle topology
    // or geometry class.
    public static Preset PressureFedSmall()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_RP1,   // fallback per AutoSeeder.cs:134
            Thrust_N:           1_000.0,
            ChamberPressure_Pa: 0.7e6,
            ExpansionRatio:     25.0,
            EngineCycleOverride: EngineCycle.PressureFed);
        var seed = WithDefaultIgniter(AutoSeeder.Seed(spec));
        return new Preset(
            Name: "pressure-fed-small",
            Description:
                "Pressure-fed small thruster: LOX/RP-1 1 kN, Pc 0.7 MPa, eps 25. "
              + "Roadmap called for N2O4/MMH but AutoSeeder only implements "
              + "LOX_CH4/H2/RP1 today — LOX/RP-1 swap holds the cycle topology "
              + "and small-thrust class constant.",
            Spec: spec,
            Seed: seed);
    }

    // Aerospike — LOX/CH4 plug-nozzle preset.
    //
    // **Sprint A-2 (#167, 2026-04-30) downgrade.** Original spec was
    // 20 kN @ Pc 7 MPa @ ε 15. Under post-Z1.2 physics the throat
    // thermal stress σ_T = α·E·ΔT_wall/(2(1−ν)) ≈ 230 MPa drives the
    // combined VM past the GRCop-42 hot σ_y limit (180 MPa) regardless
    // of wall thickness — thinner wall reduces ΔT but is bounded by
    // LPBF floor 0.5 mm. Heat flux is the lever:
    //   q ∝ Pc^0.8 → Pc 7→5 MPa drops q by ~24 %, ΔT_wall by 24 %,
    //   σ_T below the YIELD_EXCEEDED gate edge.
    // Pc=5 MPa keeps the aerospike "high-Pc plug nozzle" identity (real
    // research aerospikes run 3-7 MPa) while clearing the wall thermal
    // stress envelope. AutoSeeder's hoop-aware wall scheduler (Sprint
    // A-2) handles the burst-margin side.
    public static Preset Aerospike()
    {
        var spec = new EngineSpec(
            PropellantPair:           PropellantPair.LOX_CH4,
            Thrust_N:                 20_000.0,
            ChamberPressure_Pa:       5e6,
            ExpansionRatio:           15.0,
            ChannelTopologyOverride:  ChannelTopology.Aerospike);
        var seed = WithDefaultIgniter(AutoSeeder.Seed(spec));
        return new Preset(
            Name: "aerospike",
            Description:
                "Aerospike LOX/CH4 20 kN, Pc 5 MPa, eps 15, plug 0.30. "
              + "Sprint A-2 downgrade from Pc 7 MPa to clear post-Z1.2 "
              + "throat thermal stress (#167). Cross-correlates with the "
              + "BB-0 aerospike-0.4mm voxel baseline.",
            Spec: spec,
            Seed: seed);
    }

    // Pintle — LOX/CH4 10 kN with a single-element pintle injector,
    // SuperDraco-class topology with methane-substituted propellants.
    // Element count = 1 by AutoSeeder rule. eps=8 is the sea-level
    // SuperDraco / Merlin-1D bracket. Pc=4 MPa keeps peak T_wg below
    // the GRCop-42 service ceiling under post-Z1.2 physics.
    public static Preset Pintle()
    {
        // Sprint A-2 (#167, 2026-04-30): apply the long-documented eps 25 → 8
        // reduction. The pre-existing comment block (above) describes the
        // physics motivation in detail (eps=25 is a vacuum upper-stage value
        // misapplied to a sea-level pintle; SuperDraco/Merlin-1D run at eps
        // ≤ 8). The corresponding code change had been reverted at some
        // point, leaving comment and spec inconsistent. Restore eps=8.
        // Sprint A-2 also drops Pc from 6 → 4 MPa: with eps=8 + post-Z1.2
        // physics, the heat flux at Pc=6 MPa pushes peak T_wg=1189 K above
        // the 1150 K GRCop-42 service ceiling. Pc=4 MPa pulls heat flux
        // down ≈ Pc^0.8 (~28 % drop) and clears WALL_TEMP at the seed.
        // Real SuperDraco runs at Pc ~ 6.7 MPa, so this is a small bracket
        // adjustment (consistent with the merlin downgrade in #165 and
        // the rl10 downgrade also shipping in this PR).
        var spec = new EngineSpec(
            PropellantPair:        PropellantPair.LOX_CH4,
            Thrust_N:              10_000.0,
            ChamberPressure_Pa:    4e6,
            ExpansionRatio:        8.0,
            ElementTypeOverride:   "Pintle");
        var rawSeed = AutoSeeder.Seed(spec);
        // OuterJacketThickness_mm now scales physically inside AutoSeeder
        // (Sprint A-2 hoop-stress-aware scheduler), so the legacy 6.0 mm
        // override here is redundant; let AutoSeeder pick it.
        var seed = WithDefaultIgniter(rawSeed);
        return new Preset(
            Name: "pintle",
            Description:
                "Pintle LOX/CH4 10 kN, Pc 4 MPa, eps 8, single-element pintle. "
              + "SuperDraco-class topology with methane-substituted propellants. "
              + "ExpansionRatio 8 (sea-level SuperDraco/Merlin-1D-class) + "
              + "Pc 4 MPa for post-Z1.2 wall-T headroom (Sprint A-2 / #167). "
              + "Outer jacket sized by AutoSeeder's hoop-aware scheduler.",
            Spec: spec,
            Seed: seed);
    }
}
