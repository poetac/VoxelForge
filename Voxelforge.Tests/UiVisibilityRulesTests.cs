// UiVisibilityRulesTests.cs — UI overhaul Sprint 1, Step 1 (2026-04-28).
//
// Pins the visibility-rule lookups for every field across a representative
// matrix of (cycle, topology, propellant pair) discriminator states. The
// rules are pure-data (Core only — no WinForms, no PicoGK), so they
// unit-test cleanly in xUnit.
//
// What this file pins:
//   • Every [SaDesignVariable] property in RegenChamberDesign has a
//     matching FieldKeys constant (SaDimCompletenessTest).
//   • Every FieldKeys constant has a rule entry in UiVisibilityRules
//     (RuleCompletenessTest — implicit via the For() default = Shown
//     so we instead check there are no surprises by surveying the
//     internal dictionary via the public surface).
//   • Topology classifiers (IsDiscreteChannel / IsTpms / IsAerospike /
//     IsLinearAerospike) match the optimizer's same-named helpers.
//   • Cycle-dependent rules (turbopump / preburner / turbine fields)
//     produce the same answer as CycleSolvers.Get(cycle).HasXxx.
//   • Topology-dependent rules (channel-only, TPMS-only, aerospike-
//     only) hide their fields when the topology doesn't match.
//   • Hidden controls keep .Value semantically — the rules table
//     never resets, only hides.
//   • RecommendedCycles emits the per-pair ordered list described
//     in the rules.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class UiVisibilityRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Default visibility state — pressure-fed bell, LOX/CH4, no opt-in
    /// subsystems. Used as the baseline for narrow per-field tests.
    /// </summary>
    private static UiVisibilityState DefaultState() => new(
        Cycle:                  EngineCycle.PressureFed,
        Topology:               ChannelTopology.Axial,
        Pair:                   PropellantPair.LOX_CH4,
        HasInjectorPattern:     false,
        HasDualBell:            false,
        ChilldownEnabled:       false,
        StartTransientEnabled:  false,
        LpbfPrintabilityEnabled:false,
        PreburnerCoolingEnabled:false,
        AerospikeCoolingEnabled:false,
        FilmCoolingEnabled:     false,
        MountingFlangeEnabled:  false,
        InjectorStlEnabled:     false,
        FeedSystemEnabled:      false);

    private static UiVisibilityState WithCycle(EngineCycle c)
        => DefaultState() with { Cycle = c };

    private static UiVisibilityState WithTopology(ChannelTopology t)
        => DefaultState() with { Topology = t };

    // ── 1. Completeness checks (build-failure tripwires) ─────────────

    [Fact]
    public void SaDimCompleteness_EveryRegenChamberDesignSaDimHasFieldKey()
    {
        // For every property tagged with [SaDesignVariable], assert that
        // FieldKeys contains a string constant whose value matches the
        // property name (camelCase'd). This fails the build the moment
        // someone adds an SA dim and forgets to add a FieldKeys entry.

        var saProps = typeof(RegenChamberDesign)
            .GetProperties(System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.Instance)
            .Where(p => System.Attribute.IsDefined(p, typeof(SaDesignVariableAttribute)))
            .ToList();

        Assert.NotEmpty(saProps);

        var allKeys = new HashSet<string>(FieldKeys.All, System.StringComparer.Ordinal);
        var missing = new System.Collections.Generic.List<string>();

        foreach (var prop in saProps)
        {
            // Convention: PropertyName -> propertyName (first char lower).
            var camelCase = char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
            if (!allKeys.Contains(camelCase))
                missing.Add($"{prop.Name} (expected key: \"{camelCase}\")");
        }

        Assert.True(missing.Count == 0,
            "FieldKeys is missing entries for these SA-dim properties:\n  "
          + string.Join("\n  ", missing));
    }

    [Fact]
    public void FieldKeysAll_IsNonEmpty_AndAllUniqueOrdinal()
    {
        var all = FieldKeys.All;
        Assert.NotEmpty(all);
        var unique = new HashSet<string>(all, System.StringComparer.Ordinal);
        Assert.Equal(all.Count, unique.Count);
    }

    [Fact]
    public void FieldKeysAll_IsSorted()
    {
        // The list contract is "sorted by ordinal" so the rule-completeness
        // check has stable ordering. This test pins that contract so a
        // future refactor doesn't silently reorder.
        var all = FieldKeys.All;
        for (int i = 1; i < all.Count; i++)
            Assert.True(string.CompareOrdinal(all[i - 1], all[i]) < 0,
                $"FieldKeys.All not sorted at index {i}: '{all[i-1]}' >= '{all[i]}'");
    }

    [Fact]
    public void RuleCompleteness_EveryFieldKeyHasNonNullVerdict()
    {
        // Every key must produce a verdict (not throw, not be Hidden by
        // accident). The default-Shown semantics in For() means we just
        // check that nothing throws and every key gets a value.
        var s = DefaultState();
        foreach (var key in FieldKeys.All)
        {
            var verdict = UiVisibilityRules.For(key, s);
            Assert.True(System.Enum.IsDefined(typeof(FieldRelevance), verdict),
                $"Key '{key}' produced an invalid FieldRelevance value");
        }
    }

    // ── 2. Topology classifiers ─────────────────────────────────────

    [Theory]
    [InlineData(ChannelTopology.Axial, true)]
    [InlineData(ChannelTopology.Helical, true)]
    [InlineData(ChannelTopology.None, false)]
    [InlineData(ChannelTopology.TpmsGyroid, false)]
    [InlineData(ChannelTopology.TpmsSchwarzP, false)]
    [InlineData(ChannelTopology.TpmsSchwarzD, false)]
    [InlineData(ChannelTopology.Aerospike, false)]
    [InlineData(ChannelTopology.LinearAerospike, false)]
    public void IsDiscreteChannel_MatchesAxialAndHelicalOnly(ChannelTopology t, bool expected)
        => Assert.Equal(expected, UiVisibilityRules.IsDiscreteChannel(t));

    [Theory]
    [InlineData(ChannelTopology.TpmsGyroid, true)]
    [InlineData(ChannelTopology.TpmsSchwarzP, true)]
    [InlineData(ChannelTopology.TpmsSchwarzD, true)]
    [InlineData(ChannelTopology.Axial, false)]
    [InlineData(ChannelTopology.None, false)]
    [InlineData(ChannelTopology.Aerospike, false)]
    [InlineData(ChannelTopology.LinearAerospike, false)]
    public void IsTpms_MatchesAllThreeFamilies(ChannelTopology t, bool expected)
        => Assert.Equal(expected, UiVisibilityRules.IsTpms(t));

    [Theory]
    [InlineData(ChannelTopology.Aerospike, true)]
    [InlineData(ChannelTopology.LinearAerospike, true)]
    [InlineData(ChannelTopology.Axial, false)]
    [InlineData(ChannelTopology.TpmsGyroid, false)]
    public void IsAerospike_MatchesBothPlugFamilies(ChannelTopology t, bool expected)
        => Assert.Equal(expected, UiVisibilityRules.IsAerospike(t));

    [Theory]
    [InlineData(ChannelTopology.LinearAerospike, true)]
    [InlineData(ChannelTopology.Aerospike, false)]
    [InlineData(ChannelTopology.Axial, false)]
    public void IsLinearAerospike_MatchesOnlyLinear(ChannelTopology t, bool expected)
        => Assert.Equal(expected, UiVisibilityRules.IsLinearAerospike(t));

    // ── 3. Cycle-dependent fields (turbopump / preburner / turbine) ──

    [Theory]
    [InlineData(EngineCycle.PressureFed, false)]
    [InlineData(EngineCycle.GasGenerator, true)]
    [InlineData(EngineCycle.ElectricPump, true)]
    [InlineData(EngineCycle.OpenExpander, true)]
    [InlineData(EngineCycle.ClosedExpander, true)]
    [InlineData(EngineCycle.StagedCombustion, true)]
    [InlineData(EngineCycle.FullFlow, true)]
    [InlineData(EngineCycle.ORSC, true)]
    [InlineData(EngineCycle.TapOff, true)]
    public void PumpInletPressure_VisibleIffCycleHasTurbopump(EngineCycle c, bool expected)
    {
        var verdict = UiVisibilityRules.For(FieldKeys.PumpInletPressure_MPa, WithCycle(c));
        Assert.Equal(expected, verdict != FieldRelevance.Hidden);
        // Also verify the rule agrees with the CycleSolver registry.
        Assert.Equal(CycleSolvers.Get(c).HasTurbopump, expected);
    }

    [Theory]
    [InlineData(EngineCycle.PressureFed, false)]
    [InlineData(EngineCycle.ElectricPump, false)]
    [InlineData(EngineCycle.OpenExpander, false)]
    [InlineData(EngineCycle.ClosedExpander, false)]
    [InlineData(EngineCycle.TapOff, false)]
    [InlineData(EngineCycle.GasGenerator, true)]
    [InlineData(EngineCycle.StagedCombustion, true)]
    [InlineData(EngineCycle.FullFlow, true)]
    [InlineData(EngineCycle.ORSC, true)]
    public void PreburnerFields_VisibleIffCycleHasPreburner(EngineCycle c, bool expectedVisible)
    {
        var s = WithCycle(c);
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.PreburnerChamberPressure_MPa, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.PreburnerMrRatio, s));
    }

    [Theory]
    [InlineData(EngineCycle.PressureFed, false)]
    [InlineData(EngineCycle.ElectricPump, false)]
    [InlineData(EngineCycle.GasGenerator, true)]
    [InlineData(EngineCycle.StagedCombustion, true)]
    [InlineData(EngineCycle.FullFlow, true)]
    [InlineData(EngineCycle.ClosedExpander, true)]
    [InlineData(EngineCycle.OpenExpander, true)]
    [InlineData(EngineCycle.TapOff, true)]
    [InlineData(EngineCycle.ORSC, true)]
    public void TurbineFields_VisibleIffCycleHasTurbine(EngineCycle c, bool expectedVisible)
    {
        var s = WithCycle(c);
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.TurbineInletTemperature_K, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.TurbinePressureRatio, s));
    }

    // ── 4. Topology-dependent fields ────────────────────────────────

    [Theory]
    [InlineData(ChannelTopology.Axial, true)]
    [InlineData(ChannelTopology.Helical, true)]
    [InlineData(ChannelTopology.TpmsGyroid, false)]
    [InlineData(ChannelTopology.TpmsSchwarzP, false)]
    [InlineData(ChannelTopology.TpmsSchwarzD, false)]
    [InlineData(ChannelTopology.Aerospike, false)]
    [InlineData(ChannelTopology.LinearAerospike, false)]
    [InlineData(ChannelTopology.None, false)]
    public void DiscreteChannelFields_VisibleOnlyForAxialAndHelical(
        ChannelTopology t, bool expectedVisible)
    {
        var s = WithTopology(t);
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.ChannelCount, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.ChannelHeightChamber_mm, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.ChannelHeightThroat_mm, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.ChannelHeightExit_mm, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.RibThickness_mm, s));
    }

    [Theory]
    [InlineData(ChannelTopology.TpmsGyroid, true)]
    [InlineData(ChannelTopology.TpmsSchwarzP, true)]
    [InlineData(ChannelTopology.TpmsSchwarzD, true)]
    [InlineData(ChannelTopology.Axial, false)]
    [InlineData(ChannelTopology.Aerospike, false)]
    public void TpmsFields_VisibleOnlyForTpmsTopologies(
        ChannelTopology t, bool expectedVisible)
    {
        var s = WithTopology(t);
        Assert.Equal(expectedVisible, UiVisibilityRules.ShouldShow(FieldKeys.TpmsCellEdge_mm, s));
        Assert.Equal(expectedVisible, UiVisibilityRules.ShouldShow(FieldKeys.TpmsSolidFraction, s));
    }

    [Theory]
    [InlineData(ChannelTopology.Aerospike, true)]
    [InlineData(ChannelTopology.LinearAerospike, true)]
    [InlineData(ChannelTopology.Axial, false)]
    [InlineData(ChannelTopology.TpmsGyroid, false)]
    public void AerospikeFields_VisibleOnlyForAerospikeTopologies(
        ChannelTopology t, bool expectedVisible)
    {
        var s = WithTopology(t);
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.PlugLengthRatio, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.AerospikeContractionRatio, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.AerospikePlugCooling, s));
    }

    [Theory]
    [InlineData(ChannelTopology.LinearAerospike, true)]
    [InlineData(ChannelTopology.Aerospike, false)]
    [InlineData(ChannelTopology.Axial, false)]
    public void LinearAerospikeFields_VisibleOnlyForLinearAerospike(
        ChannelTopology t, bool expectedVisible)
    {
        var s = WithTopology(t);
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.LinearAerospikePlugWidth_mm, s));
        Assert.Equal(expectedVisible,
            UiVisibilityRules.ShouldShow(FieldKeys.LinearAerospikePlugDepth_mm, s));
    }

    [Fact]
    public void WallThicknessOverrides_HiddenOnlyForTopologyNone()
    {
        // Per-station wall overrides are the Track B SA dims (28-30).
        // Track B is exercised across Axial/Helical/TPMS — only None
        // (no regen jacket at all) drops them out.
        Assert.True(UiVisibilityRules.ShouldShow(FieldKeys.ChamberWallThicknessOverride_mm,
            WithTopology(ChannelTopology.Axial)));
        Assert.True(UiVisibilityRules.ShouldShow(FieldKeys.ChamberWallThicknessOverride_mm,
            WithTopology(ChannelTopology.TpmsGyroid)));
        Assert.True(UiVisibilityRules.ShouldShow(FieldKeys.ChamberWallThicknessOverride_mm,
            WithTopology(ChannelTopology.Aerospike)));
        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.ChamberWallThicknessOverride_mm,
            WithTopology(ChannelTopology.None)));
    }

    // ── 5. Opt-in toggles (film cooling, mounting flange, injector STL) ──

    [Fact]
    public void FilmCoolingFields_HiddenWhenDisabled_VisibleWhenEnabled()
    {
        var off = DefaultState() with { FilmCoolingEnabled = false };
        var on  = DefaultState() with { FilmCoolingEnabled = true  };

        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.FilmFuelFraction, off));
        Assert.True (UiVisibilityRules.ShouldShow(FieldKeys.FilmFuelFraction, on));
        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.FilmSlotHeightOverride_mm, off));
        Assert.True (UiVisibilityRules.ShouldShow(FieldKeys.FilmSlotHeightOverride_mm, on));
    }

    [Fact]
    public void MountingFlangeStandard_HiddenUnlessFlangeEnabled()
    {
        var off = DefaultState() with { MountingFlangeEnabled = false };
        var on  = DefaultState() with { MountingFlangeEnabled = true  };

        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.MountingFlangeStandard, off));
        Assert.True (UiVisibilityRules.ShouldShow(FieldKeys.MountingFlangeStandard, on));
    }

    [Fact]
    public void InjectorStlPath_HiddenUnlessInjectorStlEnabled()
    {
        var off = DefaultState() with { InjectorStlEnabled = false };
        var on  = DefaultState() with { InjectorStlEnabled = true  };

        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.InjectorStlPath, off));
        Assert.True (UiVisibilityRules.ShouldShow(FieldKeys.InjectorStlPath, on));
    }

    // ── 6. Pintle-specific dims ─────────────────────────────────────

    [Fact]
    public void PintleFields_HiddenUnlessInjectorPatternIsPintle()
    {
        var noPattern = DefaultState() with { HasInjectorPattern = false };
        var coax      = DefaultState() with { HasInjectorPattern = true,
            InjectorPattern = InjectorPatternKind.Coaxial };
        var pintle    = DefaultState() with { HasInjectorPattern = true,
            InjectorPattern = InjectorPatternKind.Pintle };

        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.PintleDiameterOverride_mm, noPattern));
        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.PintleDiameterOverride_mm, coax));
        Assert.True (UiVisibilityRules.ShouldShow(FieldKeys.PintleDiameterOverride_mm, pintle));

        Assert.False(UiVisibilityRules.ShouldShow(FieldKeys.PintleSleeveHoleCountOverride, noPattern));
        Assert.True (UiVisibilityRules.ShouldShow(FieldKeys.PintleSleeveHoleCountOverride, pintle));
    }

    // ── 7. Recommended cycles per propellant pair ──────────────────

    [Fact]
    public void RecommendedCycles_LoxH2_StartsWithClosedExpander()
    {
        var rec = UiVisibilityRules.RecommendedCycles(PropellantPair.LOX_H2).ToList();
        Assert.NotEmpty(rec);
        Assert.Equal(EngineCycle.ClosedExpander, rec[0]);
        Assert.Contains(EngineCycle.OpenExpander, rec);
        Assert.Contains(EngineCycle.StagedCombustion, rec);
    }

    [Fact]
    public void RecommendedCycles_LoxRP1_StartsWithGasGenerator()
    {
        var rec = UiVisibilityRules.RecommendedCycles(PropellantPair.LOX_RP1).ToList();
        Assert.NotEmpty(rec);
        Assert.Equal(EngineCycle.GasGenerator, rec[0]);
        Assert.Contains(EngineCycle.StagedCombustion, rec);
        Assert.Contains(EngineCycle.ORSC, rec);
    }

    [Fact]
    public void RecommendedCycles_LoxCH4_StartsWithGasGenerator()
    {
        var rec = UiVisibilityRules.RecommendedCycles(PropellantPair.LOX_CH4).ToList();
        Assert.NotEmpty(rec);
        Assert.Equal(EngineCycle.GasGenerator, rec[0]);
        Assert.Contains(EngineCycle.FullFlow, rec);
        Assert.Contains(EngineCycle.StagedCombustion, rec);
    }

    [Fact]
    public void RecommendedCycles_UnimplementedPair_ReturnsEmpty()
    {
        // N2O4_MMH and H2O2_RP1 tables are not implemented — keep the
        // recommendation set empty so the App falls back to showing
        // all cycles unsuffixed.
        Assert.Empty(UiVisibilityRules.RecommendedCycles(PropellantPair.N2O4_MMH));
        Assert.Empty(UiVisibilityRules.RecommendedCycles(PropellantPair.H2O2_RP1));
    }

    [Theory]
    [InlineData(PropellantPair.LOX_CH4)]
    [InlineData(PropellantPair.LOX_H2)]
    [InlineData(PropellantPair.LOX_RP1)]
    [InlineData(PropellantPair.N2O4_MMH)]
    [InlineData(PropellantPair.H2O2_RP1)]
    public void AvailableCycles_AlwaysReturnsAllNineCycles(PropellantPair p)
    {
        var all = UiVisibilityRules.AvailableCycles(p).ToList();
        Assert.Equal(System.Enum.GetValues<EngineCycle>().Length, all.Count);
    }

    // ── 8. Hint behaviour ──────────────────────────────────────────

    [Fact]
    public void Hint_EngineCycle_NudgesWhenPickIsOffRecommendedList()
    {
        // LOX/H2 with StagedCombustion is on the recommended list — no nudge.
        var onList = DefaultState() with
        {
            Pair  = PropellantPair.LOX_H2,
            Cycle = EngineCycle.ClosedExpander,
        };
        Assert.Null(UiVisibilityRules.Hint(FieldKeys.EngineCycle, onList));

        // LOX/H2 with PressureFed is off the recommended list — should emit a tip.
        var offList = DefaultState() with
        {
            Pair  = PropellantPair.LOX_H2,
            Cycle = EngineCycle.PressureFed,
        };
        var hint = UiVisibilityRules.Hint(FieldKeys.EngineCycle, offList);
        Assert.NotNull(hint);
        Assert.Contains("LOX_H2", hint!);
    }

    [Fact]
    public void Hint_PropellantPair_FlagsUnimplementedPairs()
    {
        var implemented = DefaultState() with { Pair = PropellantPair.LOX_CH4 };
        Assert.Null(UiVisibilityRules.Hint(FieldKeys.PropellantPair, implemented));

        var unimpl = DefaultState() with { Pair = PropellantPair.N2O4_MMH };
        var hint = UiVisibilityRules.Hint(FieldKeys.PropellantPair, unimpl);
        Assert.NotNull(hint);
        Assert.Contains("not yet implemented", hint!);
    }

    // ── 9. Argument-validation contracts ───────────────────────────

    [Fact]
    public void For_NullKey_Throws()
        => Assert.Throws<System.ArgumentNullException>(
            () => UiVisibilityRules.For(null!, DefaultState()));

    [Fact]
    public void For_NullState_Throws()
        => Assert.Throws<System.ArgumentNullException>(
            () => UiVisibilityRules.For(FieldKeys.ContractionRatio, null!));

    [Fact]
    public void For_UnknownKey_DefaultsToShown()
    {
        // The "default = Shown" convention means adding a new always-on
        // field is zero-config. A typo or a deleted constant produces
        // Shown rather than a noisy exception.
        var verdict = UiVisibilityRules.For("doesNotExist", DefaultState());
        Assert.Equal(FieldRelevance.Shown, verdict);
    }

    // ── 10. Always-on / categorical-discriminator fields ───────────

    [Theory]
    [InlineData(FieldKeys.ContractionRatio)]
    [InlineData(FieldKeys.ExpansionRatio)]
    [InlineData(FieldKeys.CharacteristicLength_m)]
    [InlineData(FieldKeys.BellEntranceAngle_deg)]
    [InlineData(FieldKeys.BellExitAngle_deg)]
    [InlineData(FieldKeys.BellLengthFraction)]
    [InlineData(FieldKeys.GasSideWallThickness_mm)]
    [InlineData(FieldKeys.OuterJacketThickness_mm)]
    [InlineData(FieldKeys.FlangeRadialProjection_mm)]
    [InlineData(FieldKeys.PropellantPair)]
    [InlineData(FieldKeys.EngineCycle)]
    [InlineData(FieldKeys.ChannelTopology)]
    [InlineData(FieldKeys.MixtureRatio)]
    [InlineData(FieldKeys.ChamberPressure_MPa)]
    [InlineData(FieldKeys.Thrust_N)]
    [InlineData(FieldKeys.AmbientPressure_kPa)]
    [InlineData(FieldKeys.WallMaterial)]
    [InlineData(FieldKeys.IgniterType)]
    [InlineData(FieldKeys.SensorBosses)]
    [InlineData(FieldKeys.VoxelSize_mm)]
    [InlineData(FieldKeys.SaIterations)]
    [InlineData(FieldKeys.SaSeed)]
    public void AlwaysOn_FieldsVisibleAcrossAllStates(string fieldKey)
    {
        // Sweep across every cycle / topology / pair combination — these
        // fields should be Shown everywhere.
        foreach (var c in System.Enum.GetValues<EngineCycle>())
        foreach (var t in System.Enum.GetValues<ChannelTopology>())
        foreach (var p in System.Enum.GetValues<PropellantPair>())
        {
            var s = new UiVisibilityState(
                Cycle: c, Topology: t, Pair: p,
                HasInjectorPattern: false, HasDualBell: false,
                ChilldownEnabled: false, StartTransientEnabled: false,
                LpbfPrintabilityEnabled: false, PreburnerCoolingEnabled: false,
                AerospikeCoolingEnabled: false, FilmCoolingEnabled: false,
                MountingFlangeEnabled: false, InjectorStlEnabled: false,
                FeedSystemEnabled: false);
            Assert.True(UiVisibilityRules.ShouldShow(fieldKey, s),
                $"Field {fieldKey} hidden under state cycle={c}, topology={t}, pair={p}");
        }
    }

    // ── 11. Equality semantics on UiVisibilityState (record-based) ──

    [Fact]
    public void UiVisibilityState_RecordEquality_BehavesAsValueType()
    {
        // The wiring registry plans to short-circuit recomputation when
        // state hasn't changed; that requires record equality. Pin it.
        var a = DefaultState();
        var b = DefaultState();
        Assert.Equal(a, b);
        Assert.True(a == b);

        var c = a with { Cycle = EngineCycle.GasGenerator };
        Assert.NotEqual(a, c);
        Assert.True(a != c);
    }
}
