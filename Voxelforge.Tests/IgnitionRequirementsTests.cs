// IgnitionRequirementsTests.cs — Sprint 29 (2026-04-24):
// Per-propellant-pair ignition-energy + modality-suitability coverage.
// Third hot-fire-readiness item. Voxel-free per ADR-005.

using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class IgnitionRequirementsTests
{
    // ═══════════════════════════════════════════════════════════════
    //   IgnitionRequirements lookup table
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    // PH-12 (2026-04-29): MinEnergy units migrated mJ → J. LOX/HC spark-
    // discharge floors stay sub-joule (0.050 / 0.005); LOX/RP-1 bumped to
    // 500 J to match deployed-pyro/TEA-TEB chemical authority.
    [InlineData(PropellantPair.LOX_CH4,   false, 0.050,  IgniterType.SparkTorch)]
    [InlineData(PropellantPair.LOX_H2,    false, 0.005,  IgniterType.SparkTorch)]
    [InlineData(PropellantPair.LOX_RP1,   false, 500.0,  IgniterType.AugmentedSpark)]
    [InlineData(PropellantPair.N2O4_MMH,  true,  0.0,    IgniterType.None)]
    [InlineData(PropellantPair.H2O2_RP1,  true,  0.0,    IgniterType.None)]
    public void IgnitionRequirements_ForEveryPair_HasExpectedShape(
        PropellantPair pair,
        bool           expectedHypergolic,
        double         expectedMinEnergy,
        IgniterType    expectedMinModality)
    {
        var req = IgnitionRequirements.For(pair);
        Assert.Equal(pair,                req.Pair);
        Assert.Equal(expectedHypergolic,  req.IsHypergolic);
        Assert.Equal(expectedMinEnergy,   req.MinEnergy_J, precision: 3);
        Assert.Equal(expectedMinModality, req.MinModality);
        Assert.False(string.IsNullOrWhiteSpace(req.Notes));
    }

    // PH-29 (2026-04-29): unknown PropellantPair throws instead of returning
    // a permissive (JANNAF spark floor, SparkTorch) default. This protects
    // future enum additions (e.g. N2O4/N2H4) from silently inheriting unsafe
    // defaults that under-rate ignition energy and modality.
    [Fact]
    public void IgnitionRequirements_For_UnknownPair_Throws()
    {
        // (PropellantPair)999 is outside the registered switch arms.
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IgnitionRequirements.For((PropellantPair)999));
    }

    [Theory]
    [InlineData(IgniterType.None,                 0)]
    [InlineData(IgniterType.SparkTorch,           1)]
    [InlineData(IgniterType.AugmentedSpark,       2)]
    [InlineData(IgniterType.PyrotechnicCartridge, 3)]
    public void ModalityOrdinal_AscendsInStrength(IgniterType type, int expectedOrdinal)
    {
        Assert.Equal(expectedOrdinal, IgnitionRequirements.ModalityOrdinal(type));
    }

    [Fact]
    public void ModalityOrdinal_SparkTorchBelowAugmentedSpark()
    {
        // Invariant the modality-suitability gate depends on: each
        // step up in the IgniterType ordinal is a strictly stronger
        // modality. A future contributor extending IgniterType must
        // keep this ordering monotonic.
        Assert.True(
            IgnitionRequirements.ModalityOrdinal(IgniterType.SparkTorch)
            < IgnitionRequirements.ModalityOrdinal(IgniterType.AugmentedSpark));
        Assert.True(
            IgnitionRequirements.ModalityOrdinal(IgniterType.AugmentedSpark)
            < IgnitionRequirements.ModalityOrdinal(IgniterType.PyrotechnicCartridge));
    }

    // ═══════════════════════════════════════════════════════════════
    //   End-to-end gate behaviour via GenerateWith + Evaluate
    // ═══════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design) Baseline(
        PropellantPair pair,
        IgniterType    ig)
        => (new OperatingConditions { PropellantPair = pair },
            new RegenChamberDesign
            {
                IgniterType           = ig,
                IncludeManifolds      = false,
                IncludePorts          = false,
                IncludeInjectorFlange = true,   // igniter cavity needs the flange
                ContourStationCount   = 40,
            });

    [Fact]
    public void Gate_IGNITER_MISSING_FiresOn_NonHypergolic_WithNoIgniter()
    {
        // LOX/CH4 + IgniterType.None is a hot-fire-unsafe configuration.
        // Pre-Sprint-29 this passed silently; Sprint 29 catches it.
        var (cond, design) = Baseline(PropellantPair.LOX_CH4, IgniterType.None);
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.Contains(feas.Violations, v => v.ConstraintId == "IGNITER_MISSING");
    }

    [Fact]
    public void Gate_IGNITER_MISSING_Silent_OnHypergolicPair()
    {
        // N2O4/MMH self-ignites on contact — IgniterType.None is correct
        // for this pair and must pass silently. The existing propellant-
        // table validation still fires because N2O4/MMH is marked
        // Implemented=false, but the ignition gates should not fire.
        var (cond, design) = Baseline(PropellantPair.N2O4_MMH, IgniterType.None);

        // Generate / evaluate would throw PropellantNotImplementedException
        // on the unimplemented table, so evaluate the Requirements logic
        // directly. The gate code is driven by this.
        var req = IgnitionRequirements.For(cond.PropellantPair);
        Assert.True(req.IsHypergolic);
        Assert.Equal(0.0, req.MinEnergy_J, precision: 6);
        Assert.Equal(IgniterType.None, req.MinModality);
    }

    [Fact]
    public void Gate_IGNITER_ENERGY_INSUFFICIENT_FiresOn_LoxRp1_WithSparkTorch()
    {
        // SparkTorch rated 0.150 J < LOX/RP-1 floor of 500 J. Pre-
        // Sprint-29 the universal 50 mJ floor let this pass; the per-
        // pair requirement now catches it. (PH-12 2026-04-29: units
        // migrated mJ → J; LOX/RP-1 floor bumped to deployed-pyro
        // chemical-authority units.)
        var (cond, design) = Baseline(PropellantPair.LOX_RP1, IgniterType.SparkTorch);
        cond = cond with { MixtureRatio = 2.5 };   // MR peak for LOX/RP-1
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.Contains(feas.Violations, v => v.ConstraintId == "IGNITER_ENERGY_INSUFFICIENT");
    }

    [Fact]
    public void Gate_IGNITER_MODALITY_UNSUITABLE_FiresOn_LoxRp1_WithSparkTorch()
    {
        // Same fixture fires the modality gate too: SparkTorch ordinal
        // (1) < AugmentedSpark ordinal (2) = LOX/RP-1 minimum. Both
        // gates surface so the user sees both complaints (energy AND
        // modality), not just the first failure.
        var (cond, design) = Baseline(PropellantPair.LOX_RP1, IgniterType.SparkTorch);
        cond = cond with { MixtureRatio = 2.5 };
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.Contains(feas.Violations, v => v.ConstraintId == "IGNITER_MODALITY_UNSUITABLE");
    }

    [Fact]
    public void Gate_LoxRp1_WithPyroCartridge_ClearsBothIgnitionChecks()
    {
        // PyrotechnicCartridge at 1000 J clears the 500 J energy floor
        // AND has ordinal 3 ≥ AugmentedSpark's 2. Both ignition gates
        // should be silent.
        var (cond, design) = Baseline(PropellantPair.LOX_RP1, IgniterType.PyrotechnicCartridge);
        cond = cond with { MixtureRatio = 2.5 };
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_ENERGY_INSUFFICIENT");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MODALITY_UNSUITABLE");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MISSING");
    }

    [Fact]
    public void Gate_LoxCh4_WithSparkTorch_ClearsAllIgnitionGates()
    {
        // SparkTorch rated 0.150 J > LOX/CH4 floor of 0.050 J; ordinal 1
        // = LOX/CH4 minimum. Every ignition gate silent.
        var (cond, design) = Baseline(PropellantPair.LOX_CH4, IgniterType.SparkTorch);
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MISSING");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_ENERGY_INSUFFICIENT");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MODALITY_UNSUITABLE");
    }

    [Fact]
    public void Gate_LoxH2_WithSparkTorch_ClearsAllIgnitionGates()
    {
        // LOX/H2 has the easiest ignition of any real pair — 0.005 J
        // floor, SparkTorch minimum. A plain SparkTorch clears both.
        var (cond, design) = Baseline(PropellantPair.LOX_H2, IgniterType.SparkTorch);
        cond = cond with { MixtureRatio = 4.0 };
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MISSING");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_ENERGY_INSUFFICIENT");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MODALITY_UNSUITABLE");
    }

    [Fact]
    public void Gate_LoxCh4_WithAugmentedSpark_Passes()
    {
        // Over-engineered but valid: AugmentedSpark on LOX/CH4. Energy
        // rating 5 J ≫ 0.050 J floor; modality ordinal 2 ≥ 1. No
        // penalty — users can pick a stronger igniter than required.
        var (cond, design) = Baseline(PropellantPair.LOX_CH4, IgniterType.AugmentedSpark);
        var gen  = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var feas = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_ENERGY_INSUFFICIENT");
        Assert.DoesNotContain(feas.Violations, v => v.ConstraintId == "IGNITER_MODALITY_UNSUITABLE");
    }
}
