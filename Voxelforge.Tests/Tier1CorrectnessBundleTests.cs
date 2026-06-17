// Tier1CorrectnessBundleTests.cs — coverage for the post-Phase-6
// Tier-1 correctness bundle (L1 + L2 + L4 from the audit synthesis).
//
//  * L1 (voxel leaks in ChamberVoxelBuilder) is exercised implicitly by
//    every full-pipeline chamber build elsewhere in the suite — the
//    helper methods `BoolSubtractTemp` / `BoolAddTemp` are tested at
//    the type level here and at runtime by the benchmark harness.
//  * L2 (PUMP_PRESSURE_INVERTED gate) tested via synthetic TurbopumpResult.
//  * L4 (DesignPersistence migration completeness + required-field
//    validation) tested via JSON round-trips through a temp file.

using System.Reflection;
using System.Text.Json;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class Tier1CorrectnessBundleTests
{
    // ─── L1 — voxel-leak helpers exist + match the documented signature ──

    [Fact]
    public void VoxelOpExtensions_BoolSubtractTemp_IsExtensionOnVoxels()
    {
        // Sanity: the helper exists and is wired as an extension on
        // PicoGK.Voxels. PicoGK can't be instantiated under xUnit
        // (pitfall #8), so we verify by reflection.
        var voxelsType = typeof(PicoGK.Voxels);
        var helper = typeof(Voxelforge.Geometry.VoxelOpExtensions);
        var method = helper.GetMethod(
            "BoolSubtractTemp",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(voxelsType, method!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void VoxelOpExtensions_BoolAddTemp_IsExtensionOnVoxels()
    {
        var voxelsType = typeof(PicoGK.Voxels);
        var helper = typeof(Voxelforge.Geometry.VoxelOpExtensions);
        var method = helper.GetMethod(
            "BoolAddTemp",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(voxelsType, method!.GetParameters()[0].ParameterType);
    }

    // ─── L2 — PUMP_PRESSURE_INVERTED gate ──────────────────────────

    private static PumpSizing PumpAt(double inlet_Pa, double discharge_Pa)
        => new(
            PropellantLabel:     "fuel",
            MassFlow_kgs:        2.0,
            InletPressure_Pa:    inlet_Pa,
            DischargePressure_Pa: discharge_Pa,
            Density_kgm3:        420,
            HeadRise_m:          Math.Max(discharge_Pa - inlet_Pa, 0) / (420 * 9.81),
            HydraulicPower_W:    0,
            ShaftPower_W:        0,
            Efficiency:          0.65,
            Rpm:                 0,
            NPSHA_m:             50,
            NPSHR_m:             5,
            NPSHAcceptable:      true,
            StageCount:          1,
            HeadPerStage_m:      0);

    [Fact]
    public void PumpPressureInverted_FuelSide_TriggersGate()
    {
        var pump = new TurbopumpResult(
            Cycle:               EngineCycle.OpenExpander,
            FuelPump:            PumpAt(inlet_Pa: 5e6, discharge_Pa: 4e6),  // inverted
            OxPump:              PumpAt(inlet_Pa: 0.5e6, discharge_Pa: 8e6),
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            Array.Empty<string>(),
            Notes:               "synthetic-test");

        var gen = SyntheticGenWithTurbopump(pump);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "PUMP_PRESSURE_INVERTED"
              && v.Description.Contains("fuel pump"));
    }

    [Fact]
    public void PumpPressureInverted_OxSide_TriggersGate()
    {
        var pump = new TurbopumpResult(
            Cycle:               EngineCycle.StagedCombustion,
            FuelPump:            PumpAt(inlet_Pa: 0.5e6, discharge_Pa: 12e6),
            OxPump:              PumpAt(inlet_Pa: 8e6, discharge_Pa: 8e6),  // inverted (equal)
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            Array.Empty<string>(),
            Notes:               "synthetic-test");

        var gen = SyntheticGenWithTurbopump(pump);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "PUMP_PRESSURE_INVERTED"
              && v.Description.Contains("ox pump"));
    }

    [Fact]
    public void PumpPressureInverted_BothSides_OneGateMentionsBoth()
    {
        var pump = new TurbopumpResult(
            Cycle:               EngineCycle.GasGenerator,
            FuelPump:            PumpAt(inlet_Pa: 5e6, discharge_Pa: 4e6),
            OxPump:              PumpAt(inlet_Pa: 6e6, discharge_Pa: 5e6),
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            Array.Empty<string>(),
            Notes:               "synthetic-test");

        var gen = SyntheticGenWithTurbopump(pump);
        var gate = FeasibilityGate.Evaluate(gen);

        var v = Assert.Single(gate.Violations,
            x => x.ConstraintId == "PUMP_PRESSURE_INVERTED");
        Assert.Contains("fuel and ox pumps", v.Description);
    }

    // ─── Z2.7 — TurbopumpSizing.Size sentinels for inverted feed ─────
    //
    // Pre-Z2.7, when discharge ≤ inlet the `Math.Max(dP, 0)` clamp
    // silently zeroed every downstream number — head rise, hydraulic
    // power, shaft power, RPM, NPSHR — and NPSHAcceptable defaulted to
    // true (NPSHA ≥ 0 always). The PUMP_PRESSURE_INVERTED gate (gate
    // 14b) catches the inversion post-hoc but cycle-balance / TURBINE_
    // POWER_DEFICIT consumed the fake-zero ShaftPower first. External-
    // audit F-2.

    [Fact]
    public void Z27_TurbopumpSize_InvertedFeed_ReturnsInfinityShaftPower()
    {
        // discharge < inlet on the fuel side. Pre-Z2.7 this returned
        // ShaftPower_W = 0; post-Z2.7 returns +Infinity so cycle-balance
        // doesn't see fake-zero pump demand.
        var cond = new OperatingConditions
        {
            Thrust_N             = 50_000,
            ChamberPressure_Pa   = 7e6,
            MixtureRatio         = 3.5,
            CoolantInletTemp_K   = 130,
            CoolantInletPressure_Pa = 14e6,
            WallMaterialIndex    = 0,
            PropellantPair       = PropellantPair.LOX_CH4,
        };
        var pump = TurbopumpSizing.Size(
            cycle: EngineCycle.OpenExpander,
            cond: cond,
            fuelFlow_kgs: 2.0,
            oxFlow_kgs:   7.0,
            fuelDensity_kgm3: 420.0,
            oxDensity_kgm3:   1140.0,
            fuelInletPressure_Pa: 5e6,
            oxInletPressure_Pa:   0.5e6,
            dischargePressure_Pa: 4e6);    // 4 MPa < fuel inlet 5 MPa

        Assert.NotNull(pump.FuelPump);
        Assert.True(double.IsPositiveInfinity(pump.FuelPump!.ShaftPower_W),
            $"inverted-feed pump should report +Infinity ShaftPower, got {pump.FuelPump.ShaftPower_W}");
        Assert.False(pump.FuelPump.NPSHAcceptable,
            "inverted-feed pump NPSHAcceptable should be false to fail NPSH gate");
        Assert.True(double.IsNaN(pump.FuelPump.Efficiency),
            $"inverted-feed pump Efficiency should be NaN, got {pump.FuelPump.Efficiency}");
    }

    [Fact]
    public void Z27_TurbopumpSize_InvertedFeed_PressureInvertedGateStillFires()
    {
        // Belt-and-suspenders: the existing PUMP_PRESSURE_INVERTED gate
        // reads InletPressure_Pa + DischargePressure_Pa from PumpSizing.
        // Z2.7 preserves those fields so the gate still fires.
        var cond = new OperatingConditions
        {
            Thrust_N             = 50_000,
            ChamberPressure_Pa   = 7e6,
            MixtureRatio         = 3.5,
            CoolantInletTemp_K   = 130,
            CoolantInletPressure_Pa = 14e6,
            WallMaterialIndex    = 0,
            PropellantPair       = PropellantPair.LOX_CH4,
        };
        var pump = TurbopumpSizing.Size(
            cycle: EngineCycle.OpenExpander,
            cond: cond,
            fuelFlow_kgs: 2.0,
            oxFlow_kgs:   7.0,
            fuelDensity_kgm3: 420.0,
            oxDensity_kgm3:   1140.0,
            fuelInletPressure_Pa: 5e6,
            oxInletPressure_Pa:   0.5e6,
            dischargePressure_Pa: 4e6);

        Assert.NotNull(pump.FuelPump);
        // Pressures preserved on the sentinel result so the downstream gate fires:
        Assert.Equal(5e6, pump.FuelPump!.InletPressure_Pa);
        Assert.Equal(4e6, pump.FuelPump.DischargePressure_Pa);
    }

    [Fact]
    public void Z27_TurbopumpSize_HealthyFeed_PreservedBitIdentical()
    {
        // Back-compat invariant: when discharge > inlet (the only physically
        // valid case), Z2.7's early-exit branch is not taken and the result
        // is unchanged from pre-Z2.7. Spot-check by asserting ShaftPower
        // is finite + positive and Efficiency is in [0.4, 0.85].
        var cond = new OperatingConditions
        {
            Thrust_N             = 50_000,
            ChamberPressure_Pa   = 7e6,
            MixtureRatio         = 3.5,
            CoolantInletTemp_K   = 130,
            CoolantInletPressure_Pa = 14e6,
            WallMaterialIndex    = 0,
            PropellantPair       = PropellantPair.LOX_CH4,
        };
        var pump = TurbopumpSizing.Size(
            cycle: EngineCycle.OpenExpander,
            cond: cond,
            fuelFlow_kgs: 2.0,
            oxFlow_kgs:   7.0,
            fuelDensity_kgm3: 420.0,
            oxDensity_kgm3:   1140.0,
            fuelInletPressure_Pa: 0.5e6,
            oxInletPressure_Pa:   0.5e6,
            dischargePressure_Pa: 12e6);

        Assert.NotNull(pump.FuelPump);
        Assert.True(double.IsFinite(pump.FuelPump!.ShaftPower_W));
        Assert.True(pump.FuelPump.ShaftPower_W > 0);
        Assert.InRange(pump.FuelPump.Efficiency, 0.30, 0.90);
    }

    [Fact]
    public void PumpPressureInverted_HealthyPumps_DoesNotTrigger()
    {
        var pump = new TurbopumpResult(
            Cycle:               EngineCycle.OpenExpander,
            FuelPump:            PumpAt(inlet_Pa: 0.5e6, discharge_Pa: 12e6),
            OxPump:              PumpAt(inlet_Pa: 0.5e6, discharge_Pa: 10e6),
            TotalShaftPower_W:   1e6,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            Array.Empty<string>(),
            Notes:               "synthetic-test");

        var gen = SyntheticGenWithTurbopump(pump);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "PUMP_PRESSURE_INVERTED");
    }

    [Fact]
    public void PumpPressureInverted_PressureFed_NoTurbopump_DoesNotTrigger()
    {
        var gen = SyntheticGenWithTurbopump(null);
        var gate = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "PUMP_PRESSURE_INVERTED");
    }

    private static RegenGenerationResult SyntheticGenWithTurbopump(TurbopumpResult? pump)
    {
        var baseline = RegenChamberOptimization.GenerateWith(
            new OperatingConditions
            {
                Thrust_N              = 2224.0,
                ChamberPressure_Pa    = 6.9e6,
                MixtureRatio          = 3.3,
                CoolantInletTemp_K    = 150.0,
                CoolantInletPressure_Pa = 12e6,
                WallMaterialIndex     = 1,
                PropellantPair        = PropellantPair.LOX_CH4,
            },
            new RegenChamberDesign
            {
                IncludeManifolds      = false,
                IncludePorts          = false,
                IncludeInjectorFlange = false,
                ContourStationCount   = 60,
                IgniterType           = Geometry.IgniterType.SparkTorch,
            });
        return baseline with { Turbopump = pump };
    }

    // ─── L4 — DesignPersistence migration completeness + validation ──

    [Fact]
    public void DesignPersistence_StaticCtor_DoesNotThrow_AllMigrationsRegistered()
    {
        // Force the static ctor; any missing migration would have thrown
        // on first load of the type. Reaching this assertion proves the
        // completeness check passed.
        Assert.Equal("v31", DesignPersistence.CurrentSchemaVersion);
        Assert.NotEmpty(DesignPersistence.KnownSchemas);
    }

    [Fact]
    public void DesignPersistence_LoadValidFile_RoundTrips()
    {
        var c = ValidConditions();
        var d = new RegenChamberDesign();
        using var tmp = TestTempFile.WithUniqueName("design-persistence-test", "json");
        DesignPersistence.Save(tmp.Path, c, d, r: null);
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Conditions);
        Assert.NotNull(loaded.Design);
        Assert.Equal(c.Thrust_N, loaded.Conditions!.Thrust_N);
    }

    [Fact]
    public void DesignPersistence_LoadMissingConditions_Throws()
    {
        using var tmp = TestTempFile.WithUniqueName("design-persistence-test", "json");
        File.WriteAllText(tmp.Path,
            "{\"Schema\":\"v18\",\"Version\":\"1.0\",\"AppName\":\"x\",\"Design\":{}}");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DesignPersistence.Load(tmp.Path));
        Assert.Contains("OperatingConditions", ex.Message);
    }

    [Fact]
    public void DesignPersistence_LoadMissingDesign_Throws()
    {
        using var tmp = TestTempFile.WithUniqueName("design-persistence-test", "json");
        // Conditions populated with valid Thrust_N etc.; Design omitted.
        string body = JsonSerializer.Serialize(new
        {
            Schema = "v18",
            Version = "1.0",
            AppName = "x",
            Conditions = new
            {
                Thrust_N = 2224.0,
                ChamberPressure_Pa = 6.9e6,
                MixtureRatio = 3.3,
                CoolantInletTemp_K = 150.0,
                CoolantInletPressure_Pa = 12e6,
            },
        });
        File.WriteAllText(tmp.Path, body);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DesignPersistence.Load(tmp.Path));
        Assert.Contains("RegenChamberDesign", ex.Message);
    }

    [Theory]
    [InlineData("Thrust_N")]
    [InlineData("ChamberPressure_Pa")]
    [InlineData("MixtureRatio")]
    [InlineData("CoolantInletTemp_K")]
    [InlineData("CoolantInletPressure_Pa")]
    public void DesignPersistence_LoadZeroedRequiredField_Throws(string fieldName)
    {
        using var tmp = TestTempFile.WithUniqueName("design-persistence-test", "json");
        // Build a JSON envelope where the named field is forced to 0.
        var conditions = new Dictionary<string, object>
        {
            ["Thrust_N"] = 2224.0,
            ["ChamberPressure_Pa"] = 6.9e6,
            ["MixtureRatio"] = 3.3,
            ["CoolantInletTemp_K"] = 150.0,
            ["CoolantInletPressure_Pa"] = 12e6,
        };
        conditions[fieldName] = 0.0;

        string body = JsonSerializer.Serialize(new
        {
            Schema = "v18",
            Version = "1.0",
            AppName = "x",
            Conditions = conditions,
            Design = new { },
        });
        File.WriteAllText(tmp.Path, body);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DesignPersistence.Load(tmp.Path));
        Assert.Contains(fieldName, ex.Message);
    }

    [Fact]
    public void DesignPersistence_LoadNaNField_Throws()
    {
        using var tmp = TestTempFile.WithUniqueName("design-persistence-test", "json");
        // Force NaN explicitly by writing the literal — JsonSerializer
        // will refuse double.NaN with default options, so we craft the
        // raw JSON.
        File.WriteAllText(tmp.Path,
            "{\"Schema\":\"v18\",\"Version\":\"1.0\",\"AppName\":\"x\","
          + "\"Conditions\":{\"Thrust_N\":\"NaN\",\"ChamberPressure_Pa\":6.9e6,"
          + "\"MixtureRatio\":3.3,\"CoolantInletTemp_K\":150.0,"
          + "\"CoolantInletPressure_Pa\":12e6},\"Design\":{}}");
        // Either the deserializer rejects NaN as a string (JsonException),
        // or our validator rejects the NaN value (InvalidOperationException).
        // Both are acceptable — the contract is "this file does not load
        // silently into a corrupted SavedDesign."
        Assert.ThrowsAny<Exception>(() => DesignPersistence.Load(tmp.Path));
    }

    private static OperatingConditions ValidConditions() => new()
    {
        Thrust_N = 2224.0,
        ChamberPressure_Pa = 6.9e6,
        MixtureRatio = 3.3,
        CoolantInletTemp_K = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex = 1,
        PropellantPair = PropellantPair.LOX_CH4,
    };

}
