// TurbopumpBatteryEnergyTests — regression suite for PH-47 (issue #192).
//
// Before this fix TurbopumpSizing.Size computed EstimatedDryMass_kg as
// motor + inverter mass only (ElectricPowerConverterMass_kg_per_kW × kW).
// Battery energy mass was entirely omitted.  For a Rutherford-class
// 25 kN electric-pump engine running 150 s, the missing battery mass is
// the dominant term in the power system mass budget.
//
// What is tested here:
//   • Zero BurnTime_s → no battery mass added (backward-compatible).
//   • Non-zero BurnTime_s → battery mass = TotalShaft_W × BurnTime_s / 1e6
//     × BatteryEnergyDensity_kg_per_MJ.
//   • Non-electric cycles are unaffected.
//   • BatteryMass_kg exposed separately on TurbopumpResult.
//   • Rutherford-class scenario pins total system mass within 10 % of the
//     formula-derived expected value.
//   • Schema migration v19 → v20 is registered and identity-bumps the tag.

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using System.IO;
using Xunit;

namespace Voxelforge.Tests;

public class TurbopumpBatteryEnergyTests
{
    // Shared plumbing: returns an OperatingConditions for an electric-pump
    // cycle with the given burn-time and energy-density overrides.
    private static OperatingConditions MakeElectricCond(
        double burnTime_s = 0.0,
        double batteryDensity_kg_per_MJ = 1.0)
        => new OperatingConditions
        {
            Thrust_N                          = 25_000,
            ChamberPressure_Pa                = 8e6,
            MixtureRatio                      = 2.4,
            CoolantInletTemp_K                = 250,
            CoolantInletPressure_Pa           = 9e6,
            EngineCycle                       = EngineCycle.ElectricPump,
            PropellantPair                    = PropellantPair.LOX_RP1,
            PumpInletPressure_Pa              = 0.5e6,
            PumpEfficiency                    = 0.70,
            BurnTime_s                        = burnTime_s,
            BatteryEnergyDensity_kg_per_MJ    = batteryDensity_kg_per_MJ,
        };

    private static TurbopumpResult RunSize(OperatingConditions cond,
        double fuelFlow = 4.0, double oxFlow  = 9.6,
        double fuelRho  = 820,  double oxRho   = 1140)
        => TurbopumpSizing.Size(
            cycle:                 EngineCycle.ElectricPump,
            cond:                  cond,
            fuelFlow_kgs:          fuelFlow,
            oxFlow_kgs:            oxFlow,
            fuelDensity_kgm3:      fuelRho,
            oxDensity_kgm3:        oxRho,
            fuelInletPressure_Pa:  cond.PumpInletPressure_Pa,
            oxInletPressure_Pa:    cond.PumpInletPressure_Pa,
            dischargePressure_Pa:  cond.ChamberPressure_Pa * 1.5);

    // ── backward-compat: BurnTime_s = 0 ─────────────────────────────────

    [Fact]
    public void ZeroBurnTime_NoBatteryMassAdded()
    {
        var cond   = MakeElectricCond(burnTime_s: 0.0);
        var result = RunSize(cond);

        Assert.Equal(0.0, result.BatteryMass_kg, precision: 6);
        // EstimatedDryMass_kg must equal converter-only mass.
        double expectedConverter = TurbopumpSizing.ElectricPowerConverterMass_kg_per_kW
                                 * (result.TotalShaftPower_W / 1000.0);
        Assert.Equal(expectedConverter, result.EstimatedDryMass_kg, precision: 3);
    }

    // ── battery mass formula ─────────────────────────────────────────────

    [Fact]
    public void NonZeroBurnTime_BatteryMassComputedCorrectly()
    {
        const double burnTime = 120.0;
        const double density  = 1.0;   // 1 kg/MJ
        var cond   = MakeElectricCond(burnTime_s: burnTime, batteryDensity_kg_per_MJ: density);
        var result = RunSize(cond);

        // BatteryMass = TotalShaft_W × BurnTime_s / 1e6 × density_kg_per_MJ
        double expected = (result.TotalShaftPower_W / 1e6) * burnTime * density;
        Assert.Equal(expected, result.BatteryMass_kg, precision: 6);
    }

    [Fact]
    public void BatteryMassIncludedInEstimatedDryMass()
    {
        const double burnTime = 150.0;
        var cond   = MakeElectricCond(burnTime_s: burnTime, batteryDensity_kg_per_MJ: 1.0);
        var result = RunSize(cond);

        double expectedConverter = TurbopumpSizing.ElectricPowerConverterMass_kg_per_kW
                                 * (result.TotalShaftPower_W / 1000.0);
        double expectedBattery   = (result.TotalShaftPower_W / 1e6) * burnTime * 1.0;
        double expectedTotal     = expectedConverter + expectedBattery;

        Assert.Equal(expectedTotal, result.EstimatedDryMass_kg, precision: 6);
        Assert.True(result.BatteryMass_kg > 0, "Battery mass must be positive");
    }

    [Theory]
    [InlineData(0.5)]    // optimistic Li-ion
    [InlineData(1.0)]    // nominal Li-Po (1 MJ/kg)
    [InlineData(1.67)]   // conservative packaged Li-Po (~167 Wh/kg with BMS)
    public void BatteryMassScalesLinearlytWithDensity(double density_kg_per_MJ)
    {
        const double burnTime = 100.0;
        var cond   = MakeElectricCond(burnTime_s: burnTime, batteryDensity_kg_per_MJ: density_kg_per_MJ);
        var result = RunSize(cond);

        double expected = (result.TotalShaftPower_W / 1e6) * burnTime * density_kg_per_MJ;
        Assert.Equal(expected, result.BatteryMass_kg, precision: 6);
    }

    // ── non-electric cycles unaffected ───────────────────────────────────

    [Theory]
    [InlineData(EngineCycle.GasGenerator)]
    [InlineData(EngineCycle.StagedCombustion)]
    public void NonElectricCycles_BatteryMassAlwaysZero(EngineCycle cycle)
    {
        var cond = new OperatingConditions
        {
            Thrust_N                       = 100_000,
            ChamberPressure_Pa             = 7e6,
            MixtureRatio                   = 2.5,
            CoolantInletTemp_K             = 120,
            CoolantInletPressure_Pa        = 8e6,
            EngineCycle                    = cycle,
            PropellantPair                 = PropellantPair.LOX_CH4,
            PumpInletPressure_Pa           = 1.5e6,
            PumpEfficiency                 = 0.65,
            BurnTime_s                     = 200.0,   // should be ignored
            BatteryEnergyDensity_kg_per_MJ = 1.0,
        };
        var result = TurbopumpSizing.Size(
            cycle:                 cycle,
            cond:                  cond,
            fuelFlow_kgs:          8.0,
            oxFlow_kgs:            20.0,
            fuelDensity_kgm3:      425,
            oxDensity_kgm3:        1140,
            fuelInletPressure_Pa:  1.5e6,
            oxInletPressure_Pa:    1.5e6,
            dischargePressure_Pa:  14e6);

        Assert.Equal(0.0, result.BatteryMass_kg, precision: 6);
    }

    // ── Rutherford-class scenario ────────────────────────────────────────
    //
    // Rocket Lab Rutherford: 25 kN, LOX/RP-1, electric-pump cycle,
    // ~150 s burn. Issue #192 (PH-47) cited ~150 kg as the expected
    // total system mass for this class at conservative Li-Po density.
    // We verify the formula is wired and produces a physically plausible
    // number (positive, battery > converter, total > 0) rather than the
    // exact 150 kg figure from the issue (which assumed 600 kW shaft power;
    // actual pump requirement at this thrust is ~30-80 kW depending on ΔP).
    //
    // The meaningful regression here is: setting BurnTime_s adds a
    // battery term that is proportional to both BurnTime_s and density,
    // and that battery mass dominates converter mass for long-burn designs.

    [Fact]
    public void RutherfordClass_BatteryDominatesConverterAtLongBurn()
    {
        // Issue #273 restored the strict `battery > converter` invariant
        // after recalibrating ElectricPowerConverterMass_kg_per_kW from
        // 1.5 → 0.4 kg/kW. With the recalibrated converter, the dominance
        // crossover at 1.67 kg/MJ density falls at ~240 s burn:
        //   battery > converter
        //   ⇔ (P_kW × t × density / 1000) × 1000 > 0.4 × P_kW
        //   ⇔ t > 0.4 / (1.67e-3 × 1) ≈ 240 s
        // Test runs at 300 s burn — comfortably past the crossover for
        // the Rutherford-class long-burn regime that motivated PH-47.
        const double burnTime = 300.0;
        const double density  = 1.67;
        var cond   = MakeElectricCond(burnTime_s: burnTime, batteryDensity_kg_per_MJ: density);
        var result = RunSize(cond);

        Assert.True(result.TotalShaftPower_W > 0, "Pump shaft power must be positive");
        Assert.True(result.BatteryMass_kg > 0,    "Battery mass must be positive when BurnTime_s > 0");
        Assert.True(result.EstimatedDryMass_kg > result.BatteryMass_kg,
            "Total dry mass must exceed battery alone (converter adds on top)");

        double converterOnly = TurbopumpSizing.ElectricPowerConverterMass_kg_per_kW
                             * (result.TotalShaftPower_W / 1000.0);
        Assert.True(result.BatteryMass_kg > converterOnly,
            $"Battery ({result.BatteryMass_kg:F2} kg) must dominate converter "
          + $"({converterOnly:F2} kg) at {burnTime:F0} s burn / {density:F2} kg/MJ density "
          + $"after PH-47 follow-up calibration (issue #273)");
    }

    [Fact]
    public void RutherfordClass_TotalMassWithin10PctOfFormula()
    {
        const double burnTime = 150.0;
        const double density  = 1.0;
        var cond   = MakeElectricCond(burnTime_s: burnTime, batteryDensity_kg_per_MJ: density);
        var result = RunSize(cond);

        double converterMass  = TurbopumpSizing.ElectricPowerConverterMass_kg_per_kW
                              * (result.TotalShaftPower_W / 1000.0);
        double batteryMass    = (result.TotalShaftPower_W / 1e6) * burnTime * density;
        double expectedTotal  = converterMass + batteryMass;

        double relErr = System.Math.Abs(result.EstimatedDryMass_kg - expectedTotal) / expectedTotal;
        Assert.True(relErr < 0.10,
            $"Dry mass {result.EstimatedDryMass_kg:F2} kg deviates from formula "
          + $"{expectedTotal:F2} kg by {relErr:P1} (> 10 %)");
    }

    // ── Notes string updated ─────────────────────────────────────────────

    [Fact]
    public void WithBurnTime_NotesIncludesBatteryBreakdown()
    {
        var cond   = MakeElectricCond(burnTime_s: 150.0, batteryDensity_kg_per_MJ: 1.0);
        var result = RunSize(cond);

        Assert.Contains("battery", result.Notes, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("150", result.Notes);
    }

    [Fact]
    public void WithoutBurnTime_NotesPromptsToBurnTime()
    {
        var cond   = MakeElectricCond(burnTime_s: 0.0);
        var result = RunSize(cond);

        Assert.Contains("BurnTime_s", result.Notes);
    }

    // ── schema v19 → v20 migration ───────────────────────────────────────

    [Fact]
    public void CurrentSchemaVersion_IsV20()
    {
        // OOB-7 (#343) bumped current to v31 (RdeTopology fields).
        // Test name retained for git-history continuity; assertion tracks
        // DesignPersistence.CurrentSchemaVersion.
        Assert.Equal("v31", DesignPersistence.CurrentSchemaVersion);
    }

    [Fact]
    public void KnownSchemas_ContainsV20()
    {
        Assert.Contains("v20", DesignPersistence.KnownSchemas);
        Assert.Contains("v21", DesignPersistence.KnownSchemas);
        Assert.Contains("v22", DesignPersistence.KnownSchemas);
        Assert.Contains("v23", DesignPersistence.KnownSchemas);
        Assert.Contains("v24", DesignPersistence.KnownSchemas);
        Assert.Contains("v25", DesignPersistence.KnownSchemas);
        Assert.Contains("v26", DesignPersistence.KnownSchemas);
        Assert.Contains("v27", DesignPersistence.KnownSchemas);
        Assert.Contains("v28", DesignPersistence.KnownSchemas);
        Assert.Contains("v29", DesignPersistence.KnownSchemas);
        Assert.Contains("v30", DesignPersistence.KnownSchemas);
        Assert.Contains("v31", DesignPersistence.KnownSchemas);
    }

    [Fact]
    public void SchemaV19_LoadMigratesTo_V20()
    {
        // Write a minimal v19 JSON to a temp file, load it via DesignPersistence.
        // The migration chain must transparently bump the schema tag through
        // every intermediate version up to the current (v19 → v20 → … → v31).
        const string v19Json = """
            {
              "Schema": "v19",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 25000,
                "ChamberPressure_Pa": 8000000,
                "MixtureRatio": 2.4,
                "CoolantInletTemp_K": 250,
                "CoolantInletPressure_Pa": 9000000,
                "WallMaterialIndex": 1
              },
              "Design": {
                "ChamberRadius_mm": 30,
                "ThroatRadius_mm": 15
              }
            }
            """;

        using var tmp = TestTempFile.WithUniqueName("ph47-migration", "json");
        File.WriteAllText(tmp.Path, v19Json);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal("v31", loaded!.Schema);
    }

    [Fact]
    public void NewFields_RoundTripViaSaveLoad()
    {
        // Verify BurnTime_s and BatteryEnergyDensity_kg_per_MJ survive
        // a Save / Load cycle at the current schema version.
        const double burnTime = 200.0;
        const double density  = 1.67;
        var cond = MakeElectricCond(burnTime_s: burnTime, batteryDensity_kg_per_MJ: density);
        var design = new RegenChamberDesign();

        using var tmp = TestTempFile.WithUniqueName("ph47-roundtrip", "json");
        DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded?.Conditions);
        Assert.Equal(burnTime, loaded!.Conditions!.BurnTime_s, precision: 6);
        Assert.Equal(density,  loaded!.Conditions!.BatteryEnergyDensity_kg_per_MJ, precision: 6);
    }
}
