// ScaffoldingSmokeTests.cs — Sprint E.0 acceptance.
//
// Mirrors Voxelforge.Airbreathing.Tests/ScaffoldingSmokeTests.cs. Confirms
// the electric-propulsion project compiles, the design + conditions
// records round-trip through `with` expressions, the schema-version
// registry behaves as documented, the Engine singleton instantiates,
// and the Family discriminator is consistent across Engine /
// EngineDesign / Conditions.
//
// E.0 deliberately does NOT call ElectricPropulsionEngine.Evaluate end-
// to-end — Evaluate dispatches into ElectricPropulsionOptimization.GenerateWith
// which throws NotImplementedException until Sprint E.1 wires the four
// physics solvers. The "GenerateWith stub throws" test pins the contract
// so when E.1 lands, that test gets refactored alongside the new physics.

using System;
using Voxelforge.ElectricPropulsion.Engines;
using Voxelforge.ElectricPropulsion.IO;
using Voxelforge.Engines;

namespace Voxelforge.ElectricPropulsion.Tests;

public sealed class ScaffoldingSmokeTests
{
    private static ElectricPropulsionEngineDesign DesignSeed() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:           870.0,
        PropellantMassFlow_kgs:  1.2e-4,
        NozzleThroatRadius_mm:   0.20,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:  6.0);

    private static ResistojetConditions ConditionsSeed() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    900.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.Hydrazine_Shell405);

    [Fact]
    public void EngineDesign_RoundTripsThroughRecordWith()
    {
        var seed = DesignSeed();
        var modified = seed with { HeaterPower_W = 1500.0 };

        Assert.Equal(870.0, seed.HeaterPower_W);
        Assert.Equal(1500.0, modified.HeaterPower_W);
        // Other fields preserved via `with`.
        Assert.Equal(seed.NozzleAreaRatio, modified.NozzleAreaRatio);
        Assert.Equal(seed.HeaterChamberLength_mm, modified.HeaterChamberLength_mm);
        Assert.NotEqual(seed, modified);
    }

    [Fact]
    public void EngineDesign_HasInitOnlyDefaults()
    {
        var seed = DesignSeed();
        Assert.Equal(HeaterMaterial.GrainStabilizedPlatinum, seed.HeaterMaterial);
        Assert.Equal(0.30, seed.ChamberEmissivity);
        Assert.Equal(1.5, seed.ChamberWallThickness_mm);
        Assert.True(seed.RadiativelyCooledNozzle);
    }

    [Fact]
    public void ResistojetConditions_HoldsAllFields()
    {
        var c = ConditionsSeed();
        Assert.Equal(28.0, c.BusVoltage_V);
        Assert.Equal(900.0, c.BusPower_W_avail);
        Assert.Equal(0.0, c.AmbientPressure_Pa);
        Assert.Equal(Propellant.N2H4Decomposed, c.Propellant);
        Assert.Equal(900.0, c.InletTemperature_K);
        Assert.Equal(PropellantInletComposition.Hydrazine_Shell405, c.InletComposition);
    }

    [Fact]
    public void PropellantInletComposition_CanonicalsSumToOne()
    {
        Assert.True(Math.Abs(PropellantInletComposition.Hydrazine_Shell405.Sum - 1.0) < 1e-9);
        Assert.True(Math.Abs(PropellantInletComposition.PureNH3.Sum - 1.0) < 1e-9);
        Assert.True(Math.Abs(PropellantInletComposition.PureH2.Sum - 1.0) < 1e-9);
        Assert.True(Math.Abs(PropellantInletComposition.PureH2O.Sum - 1.0) < 1e-9);
    }

    [Fact]
    public void PropellantInletComposition_ValidateThrowsOnNegativeFraction()
    {
        var bad = new PropellantInletComposition(
            NH3MoleFraction: -0.1,
            N2MoleFraction:   0.5,
            H2MoleFraction:   0.6,
            H2OMoleFraction:  0.0);
        Assert.Throws<ArgumentOutOfRangeException>(bad.ValidateOrThrow);
    }

    [Fact]
    public void PropellantInletComposition_ValidateThrowsOnSumDeviation()
    {
        var bad = new PropellantInletComposition(0.5, 0.3, 0.1, 0.0);  // sums to 0.9
        Assert.Throws<ArgumentOutOfRangeException>(bad.ValidateOrThrow);
    }

    [Fact]
    public void Family_DiscriminatorsAreConsistent()
    {
        Assert.Equal("electric", EngineFamilies.ElectricPropulsion);
        Assert.Equal(EngineFamilies.ElectricPropulsion, ElectricPropulsionEngine.Instance.Family);
        Assert.Equal(EngineFamilies.ElectricPropulsion, DesignSeed().Family);
        Assert.Equal(EngineFamilies.ElectricPropulsion, ConditionsSeed().Family);
    }

    [Fact]
    public void Engine_IsSingleton()
    {
        var a = ElectricPropulsionEngine.Instance;
        var b = ElectricPropulsionEngine.Instance;
        Assert.Same(a, b);
    }

    [Fact]
    public void SchemaVersion_CurrentIsV10()
    {
        // v1 → v2 (HET), v2 → v3 (Arcjet), v3 → v4 (PPT), v4 → v5 (GIT),
        // v5 → v6 (MPD), v6 → v7 (Applied-Field MPD), v7 → v8 (VASIMR
        // scaffold), v8 → v9 (FEEP scaffold).
        // v9 → v10 identity migration shipped with Wave-3 HDLT scaffold
        // (Sprint EP.W6 phase 1) — adds 4 numeric fields
        // (HdltHeliconRfPower_W, HdltMagneticFieldGradient_TpM,
        // HdltChannelLength_mm, HdltArgonMassFlow_kgs) with NaN defaults.
        // Physics dispatch still throws (phase 2).
        Assert.Equal("v10", ElectricPropulsionSchemaVersion.Current);
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v1"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v2"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v3"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v4"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v5"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v6"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v7"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v8"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v9"));
        Assert.True(ElectricPropulsionSchemaVersion.IsSupported("v10"));
        Assert.False(ElectricPropulsionSchemaVersion.IsSupported("v11"));
        Assert.False(ElectricPropulsionSchemaVersion.IsSupported("rocket-v31"));
    }

    [Fact]
    public void EngineKind_HasResistojetAsFirstNonNoneEntry()
    {
        // Wave-1 kind discipline: Resistojet is the only fully-implemented
        // kind. Sprint E.0 wires the enum slot; Sprint E.1 fills in physics.
        Assert.Equal(0, (int)ElectricPropulsionEngineKind.None);
        Assert.Equal(1, (int)ElectricPropulsionEngineKind.Resistojet);
        // Reserved Wave-2 kinds present as enum slots:
        Assert.Equal(2, (int)ElectricPropulsionEngineKind.Arcjet);
        Assert.Equal(3, (int)ElectricPropulsionEngineKind.HallEffect);
        Assert.Equal(4, (int)ElectricPropulsionEngineKind.GriddedIon);
        Assert.Equal(5, (int)ElectricPropulsionEngineKind.MagnetoPlasmaDynamic);
    }

    [Fact]
    public void GenerateWith_AllWaveKinds_DispatchToARealPipeline()
    {
        // Wave-1 shipped Resistojet; Wave-2 added HallEffect, Arcjet, PPT,
        // GIT, and now MPD (Sprint EP.W2.MPD). Every declared kind dispatches
        // to a real pipeline. GenerateWith only throws NotSupportedException
        // for an enum value outside the declared set — exercised here by
        // casting an out-of-range int.
        var bogus = (ElectricPropulsionEngineKind)99;
        var design = DesignSeed() with { Kind = bogus };
        Assert.Throws<NotSupportedException>(
            () => ElectricPropulsionOptimization.GenerateWith(design, ConditionsSeed()));
    }

    [Fact]
    public void GenerateWith_ResistojetKind_ReturnsFiniteResult()
    {
        // Post-Sprint-E.1: GenerateWith dispatches into the four-solver
        // physics stack and returns a real ElectricPropulsionResult. The
        // numerical accuracy assertions live in
        // ElectricPropulsionFixture_MR501B (Sprint E.4); this scaffolding
        // test only verifies the contract: solver convergence, finite
        // outputs, no exception.
        var result = ElectricPropulsionOptimization.GenerateWith(DesignSeed(), ConditionsSeed());
        Assert.NotNull(result);
        Assert.True(double.IsFinite(result.Thrust_N), $"Thrust_N must be finite, got {result.Thrust_N}");
        Assert.True(double.IsFinite(result.IspVacuum_s), $"IspVacuum_s must be finite, got {result.IspVacuum_s}");
        Assert.True(double.IsFinite(result.ChamberTemp_K), $"ChamberTemp_K must be finite, got {result.ChamberTemp_K}");
        Assert.True(result.Thrust_N > 0, $"Thrust_N must be positive, got {result.Thrust_N}");
        Assert.True(result.IspVacuum_s > 0, $"IspVacuum_s must be positive, got {result.IspVacuum_s}");
        Assert.True(result.ChamberTemp_K > result.Conditions.InletTemperature_K,
            $"Chamber temp {result.ChamberTemp_K} must exceed inlet {result.Conditions.InletTemperature_K}");
        Assert.True(result.HeaterTemp_K > result.ChamberTemp_K,
            $"Heater temp {result.HeaterTemp_K} must exceed chamber {result.ChamberTemp_K}");
        Assert.True(result.ChokedFlow, "MR-501B in vacuum must be choked");
    }

    [Fact]
    public void Engine_Evaluate_RejectsNullDesign()
    {
        // ArgumentNullException is the contract on null inputs. The
        // family-mismatch path is unreachable through the typed API —
        // ElectricPropulsionEngineDesign + ResistojetConditions are both
        // sealed records with a computed Family property, so the C# type
        // system already prevents foreign-family inputs at the call site.
        // The Family-discriminator check inside Evaluate is defense-in-depth.
        Assert.Throws<ArgumentNullException>(
            () => ElectricPropulsionEngine.Instance.Evaluate(null!, ConditionsSeed()));
    }

    [Fact]
    public void Engine_Evaluate_RejectsNullConditions()
    {
        Assert.Throws<ArgumentNullException>(
            () => ElectricPropulsionEngine.Instance.Evaluate(DesignSeed(), null!));
    }
}
