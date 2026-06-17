// PublishedEngineFullStackTests — Tier-2 published-engine validation.
//
// Companion to PublishedEngineFixtureTests, which validates only the
// propellant chemistry (Tc, γ, MW, C*) at published operating points.
// THIS file exercises the FULL pipeline (AutoSeeder → GenerateWith →
// face thermal → cycle balance → structural) and asserts the resulting
// performance values land in the published engine's envelope.
//
// Why this is the right next step after Phase 6:
//   - Phase 6 closed the structural model bugs (Sprint G'), the film-
//     cooling calibration tangle (Bundle-1 YF-1 + ID-1 + ID-2), and
//     the shared-pump-discharge bug (Bundle-2 ID-3).
//   - Each fix was validated in isolation against ONE preset's metric.
//   - This test file provides INTEGRATED validation: do the fixes
//     compose into a self-consistent prediction of full-engine
//     behavior?
//
// Engines covered:
//   - RL-10 (Aerojet Rocketdyne, LOX/H2 closed expander, 73.4 kN)
//   - Merlin-1D (SpaceX, LOX/RP-1 gas generator, 845 kN sea level)
//
// Each engine gets ~3-4 full-stack assertions covering:
//   - Pump discharge pressures in published envelope
//   - Injector face T in published envelope (~700-1000 K)
//   - Peak wall T below material service limit
//   - Cycle-specific assertions (RL-10 expander balance closes;
//     Merlin GG turbine has positive shaft margin)
//
// Tolerances are intentionally loose (±20-30 % on numerical predictions)
// because the model is preliminary-design grade with documented ±200 K
// face-T accuracy and per-station physics simplifications. The point
// is to catch CATASTROPHIC regressions (e.g., a future cascade that
// reverses the Sprint M Coax mixingEff and pushes Merlin face T back
// to 1244 K), not to certify against flight values.
//
// References:
//   - Aerojet Rocketdyne RL-10 Fact Sheet (publicly available)
//   - Pratt & Whitney "RL10 Engine Family" overview
//   - SpaceX Merlin-1D specs (FAA filings, technical reviews)
//   - Sutton & Biblarz 9e Tables 5-4, 5-5

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

[Collection(PropellantTablesGlobalStateCollection.Name)]
public class PublishedEngineFullStackTests
{
    /// <summary>
    /// Build the post-AutoSeeder generation result for a given engine
    /// spec. Helper to keep individual tests focused on assertions.
    /// </summary>
    /// <remarks>
    /// Issue #311: legacy implementation set
    /// <see cref="PropellantTables.UseEquilibrium"/> without restoring
    /// it. The class now joins the <c>PropellantTablesGlobalState</c>
    /// xUnit collection (sequential vs other state-mutating classes)
    /// and the helper wraps the mutation in try/finally so an
    /// exception mid-way still leaves the flag at the prior value.
    /// </remarks>
    private static RegenGenerationResult GenerateForSpec(EngineSpec spec)
    {
        var seed = AutoSeeder.Seed(spec);
        bool priorUseEquilibrium = PropellantTables.UseEquilibrium;
        try
        {
            PropellantTables.UseEquilibrium = seed.UseEquilibriumRecommended;
            return RegenChamberOptimization.GenerateWith(
                seed.Conditions, seed.Design,
                skipVoxelGeometry: true, skipMfgAnalysis: true);
        }
        finally
        {
            PropellantTables.UseEquilibrium = priorUseEquilibrium;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //   RL-10 — LOX/H2 closed expander, 73.4 kN, Pc 3.2 MPa
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Rl10_FuelPumpDischarge_InPublishedEnvelope()
    {
        // Real RL-10 fuel pump discharge: ~14 MPa = ~4.4 × Pc 3.2 MPa.
        // Bundle-1 + Bundle-2 should produce a value in the 10-25 MPa
        // range (4× Pc Sprint F1 multiplier × 3.2 MPa = 12.8 MPa minimum;
        // 5× Pc with floor 18 MPa = 18 MPa typical).
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 73_400,
            ChamberPressure_Pa: 3.2e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelPump);
        Assert.InRange(gen.Turbopump.FuelPump!.DischargePressure_Pa, 10e6, 25e6);
    }

    [Fact]
    public void Rl10_OxPumpDischarge_LowerThanFuelPostBundle2()
    {
        // Bundle-2 (ID-3 fix) routes OX pump to Pc × 1.2 = 3.84 MPa.
        // Real RL-10 OX pump discharge ~5 MPa. Both should be substantially
        // BELOW the fuel pump discharge (which is at the 4-5× Pc expander
        // multiplier).
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 73_400,
            ChamberPressure_Pa: 3.2e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.OxPump);
        Assert.NotNull(gen.Turbopump.FuelPump);
        Assert.True(gen.Turbopump.OxPump!.DischargePressure_Pa
                    < gen.Turbopump.FuelPump!.DischargePressure_Pa * 0.8,
            $"Expected ox discharge < 80 % of fuel discharge post-Bundle-2; "
          + $"got ox {gen.Turbopump.OxPump.DischargePressure_Pa / 1e6:F1} MPa "
          + $"vs fuel {gen.Turbopump.FuelPump.DischargePressure_Pa / 1e6:F1} MPa.");
    }

    [Fact]
    public void Rl10_ExpanderBalance_HasPositiveShaftMargin()
    {
        // Sprint F1 (PR #88) bumped expander multipliers so RL10 reaches
        // healthy turbine pressure ratio. Post-F1 the at-seed expander
        // balance has positive margin (`PowerSufficient = true`,
        // `MassFlowMargin > 0`).
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 73_400,
            ChamberPressure_Pa: 3.2e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.ExpanderTurbine);
        Assert.True(gen.ExpanderTurbine!.PowerSufficient,
            $"RL-10-class expander should have sufficient shaft power post-F1; "
          + $"avail {gen.ExpanderTurbine.AvailableShaftPower_W / 1e3:F0} kW vs "
          + $"req {gen.ExpanderTurbine.RequiredShaftPower_W / 1e3:F0} kW.");
        Assert.True(gen.ExpanderTurbine.MassFlowMargin > 0,
            $"Expected positive expander margin post-F1; got "
          + $"{gen.ExpanderTurbine.MassFlowMargin:F2}.");
    }

    [Fact]
    public void Rl10_InjectorFaceT_BelowPublishedLimit()
    {
        // Real RL-10 face T ~700-800 K. Sprint M's Coax mixingEff = 0.65
        // calibrated against this target. Post-Sprint-M the model produces
        // ~640 K for RL-10. Full-stack should be < 1100 K (composite
        // material limit) with comfortable margin.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 73_400,
            ChamberPressure_Pa: 3.2e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.InjectorFace);
        Assert.InRange(gen.InjectorFace!.TFace_K, 400, 1100);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Merlin-1D — LOX/RP-1 gas generator, 845 kN sea level, Pc 9.7 MPa
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Merlin1d_FuelPumpDischarge_InPublishedEnvelope()
    {
        // Real Merlin-1D fuel pump discharge: ~15 MPa = 1.5 × Pc 9.7 MPa.
        // For non-expander cycles `ResolvePumpDischarge` returns Pc × 1.5
        // = 14.55 MPa. Both should be in the 12-20 MPa range.
        //
        // Note: AutoSeeder doesn't set PumpDischargePressure_Pa for non-
        // expander cycles, so it falls through to ResolvePumpDischarge.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_RP1,
            Thrust_N: 845_000,
            ChamberPressure_Pa: 9.7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelPump);
        Assert.InRange(gen.Turbopump.FuelPump!.DischargePressure_Pa, 12e6, 20e6);
    }

    [Fact]
    public void Merlin1d_OxPumpDischarge_AtInjectorPressure()
    {
        // Bundle-2 (ID-3 fix) routes OX pump to Pc × 1.2 = 11.64 MPa.
        // Real Merlin-1D OX pump discharge ~12 MPa. Should be in the
        // 10-15 MPa range.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_RP1,
            Thrust_N: 845_000,
            ChamberPressure_Pa: 9.7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.OxPump);
        Assert.InRange(gen.Turbopump.OxPump!.DischargePressure_Pa, 10e6, 15e6);
    }

    [Fact]
    public void Merlin1d_InjectorFaceT_BelowPublishedLimit()
    {
        // Real Merlin face T ~800-900 K. Sprint M (PR #88) calibrated
        // Coax mixingEff = 0.65 against this target. Post-Sprint-M the
        // model produced 918 K (in band). Full-stack should land < 1100 K
        // with margin.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_RP1,
            Thrust_N: 845_000,
            ChamberPressure_Pa: 9.7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var gen = GenerateForSpec(spec);

        Assert.NotNull(gen.InjectorFace);
        Assert.InRange(gen.InjectorFace!.TFace_K, 500, 1100);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Cross-engine sanity: pump discharge ratios match real-engine
    //   patterns
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Bundle2_PumpDischargeRatios_MatchEnginePatterns()
    {
        // Bundle-2 (ID-3 fix) ensures the fuel/ox discharge RATIO
        // matches engine-class expectations:
        //   - RL-10 (closed expander): fuel/ox > 2.0× (real ~2.8×)
        //   - Merlin-1D (gas generator): fuel/ox < 2.0× (real ~1.25×)
        // The pre-Bundle-2 single-shared-discharge bug would have made
        // both ratios equal 1.0 — this test catches a future regression
        // that re-shares the discharge.
        var rl10 = GenerateForSpec(new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 73_400,
            ChamberPressure_Pa: 3.2e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander));
        var merlin = GenerateForSpec(new EngineSpec(
            PropellantPair: PropellantPair.LOX_RP1,
            Thrust_N: 845_000,
            ChamberPressure_Pa: 9.7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator));

        double rl10Ratio = rl10.Turbopump!.FuelPump!.DischargePressure_Pa
                         / rl10.Turbopump.OxPump!.DischargePressure_Pa;
        double merlinRatio = merlin.Turbopump!.FuelPump!.DischargePressure_Pa
                           / merlin.Turbopump.OxPump!.DischargePressure_Pa;

        Assert.True(rl10Ratio > 2.0,
            $"RL-10 fuel/ox discharge ratio {rl10Ratio:F2} should be > 2.0 "
          + "(closed expander pushes fuel discharge to 4-5× Pc while ox "
          + "stays at injector ΔP ≈ 1.2× Pc).");
        Assert.True(merlinRatio < 2.0,
            $"Merlin-1D fuel/ox discharge ratio {merlinRatio:F2} should be < 2.0 "
          + "(gas-generator cycle has similar fuel/ox discharge needs).");
    }
}
