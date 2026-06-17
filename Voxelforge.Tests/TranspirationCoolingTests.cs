// TranspirationCoolingTests.cs — OOB-12 (#342) transpiration cooling.
//
// Covers: pure-math helper, GenerateWith T_wg reduction, zero-bleed
// bit-identity, TRANSPIRATION_BLEED_EXCESSIVE gate, schema v27 → v28
// migration, BuildSheet conditional section, Pack/Unpack round-trip.

using System.IO;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class TranspirationCoolingTests
{
    // ── Pure-math helper ─────────────────────────────────────────────

    [Fact]
    public void ComputeEffectiveAdiabaticWallTemp_ZeroBleed_ReturnsTaw()
    {
        double T_aw = 3000.0;
        double result = TranspirationCooling.ComputeEffectiveAdiabaticWallTemp(
            T_aw_K: T_aw,
            T_coolantInlet_K: 150.0,
            h_gas_Wm2K: 10_000.0,
            bleedMassFluxPerArea_kgm2s: 0.0,
            cpGas_JkgK: 1200.0,
            efficiency: 0.85);
        Assert.Equal(T_aw, result);
    }

    [Fact]
    public void ComputeEffectiveAdiabaticWallTemp_PositiveBleed_ReducesTaw()
    {
        // Any positive bleed with efficiency > 0 must lower T_aw_eff.
        double T_aw = 3000.0;
        double T_c = 150.0;
        double result = TranspirationCooling.ComputeEffectiveAdiabaticWallTemp(
            T_aw_K: T_aw,
            T_coolantInlet_K: T_c,
            h_gas_Wm2K: 10_000.0,
            bleedMassFluxPerArea_kgm2s: 0.10,
            cpGas_JkgK: 1200.0,
            efficiency: 0.85);
        Assert.True(result < T_aw,
            $"Expected T_aw_eff < T_aw ({T_aw} K) but got {result} K.");
        Assert.True(result > T_c,
            $"T_aw_eff must remain above coolant inlet ({T_c} K) but got {result} K.");
    }

    [Fact]
    public void ComputeEffectiveAdiabaticWallTemp_SmallB_ApproachesLinearLimit()
    {
        // For small B, F(B) → 1 − B/2. Verify the helper doesn't
        // diverge at B → 0 (branch: Math.Abs(B) < 1e-9 → F_B = 1.0).
        double result = TranspirationCooling.ComputeEffectiveAdiabaticWallTemp(
            T_aw_K: 3000.0,
            T_coolantInlet_K: 150.0,
            h_gas_Wm2K: 1e15,            // drives B ≈ 0
            bleedMassFluxPerArea_kgm2s: 1e-12,
            cpGas_JkgK: 1200.0,
            efficiency: 0.85);
        Assert.InRange(result, 149.0, 3001.0);   // finite, physically bounded
    }

    // ── GenerateWith integration ─────────────────────────────────────

    private static (OperatingConditions Cond, RegenChamberDesign BaseDesign) HeatLimitedBaseline()
    {
        var cond = new OperatingConditions
        {
            Thrust_N                  = 5_000,
            ChamberPressure_Pa        = 8.0e6,
            MixtureRatio              = 3.3,
            CoolantInletTemp_K        = 150,
            CoolantInletPressure_Pa   = 12e6,
            WallMaterialIndex         = 1,     // GRCop-42
            PropellantPair            = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            GasSideWallThickness_mm = 0.8,
        };
        return (cond, design);
    }

    [Fact]
    public void GenerateWith_TranspirationEnabled_PeakWallTempLower()
    {
        var (cond, baseDesign) = HeatLimitedBaseline();

        var baseline = RegenChamberOptimization.GenerateWith(
            cond,
            baseDesign,
            skipVoxelGeometry: true);

        var withTranspiration = RegenChamberOptimization.GenerateWith(
            cond,
            baseDesign with
            {
                EnableTranspirationCooling = true,
                TranspirationBleedFraction = 0.05,
                TranspirationEfficiency    = 0.85,
            },
            skipVoxelGeometry: true);

        Assert.True(
            withTranspiration.Thermal!.PeakGasSideWallT_K
                < baseline.Thermal!.PeakGasSideWallT_K,
            $"Expected transpiration to reduce peak wall T; "
          + $"baseline={baseline.Thermal.PeakGasSideWallT_K:F1} K, "
          + $"transpiration={withTranspiration.Thermal.PeakGasSideWallT_K:F1} K.");
    }

    [Fact]
    public void GenerateWith_ZeroBleedFraction_BitIdenticalToBaseline()
    {
        // EnableTranspirationCooling = true with BleedFraction = 0.0 must
        // produce bit-identical PeakGasSideWallT_K to the disabled path.
        var (cond, baseDesign) = HeatLimitedBaseline();

        var disabled = RegenChamberOptimization.GenerateWith(
            cond, baseDesign, skipVoxelGeometry: true);

        var zeroBleed = RegenChamberOptimization.GenerateWith(
            cond,
            baseDesign with
            {
                EnableTranspirationCooling = true,
                TranspirationBleedFraction = 0.0,
            },
            skipVoxelGeometry: true);

        Assert.Equal(
            disabled.Thermal!.PeakGasSideWallT_K,
            zeroBleed.Thermal!.PeakGasSideWallT_K,
            precision: 9);
    }

    // ── Gate ─────────────────────────────────────────────────────────

    [Fact]
    public void Gate_TranspirationBleedExcessive_FiresAbove15Percent()
    {
        // Synthetic result with bleed = 0.20 must fire the gate.
        var gen  = MakeMinimalResult(enableTranspiration: true, bleedFraction: 0.20);
        var gate = FeasibilityGate.Evaluate(gen);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "TRANSPIRATION_BLEED_EXCESSIVE");
    }

    [Fact]
    public void Gate_TranspirationBleedExcessive_SilentAt15Percent()
    {
        var gen  = MakeMinimalResult(enableTranspiration: true, bleedFraction: 0.15);
        var gate = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "TRANSPIRATION_BLEED_EXCESSIVE");
    }

    [Fact]
    public void Gate_TranspirationBleedExcessive_SilentWhenDisabled()
    {
        var gen  = MakeMinimalResult(enableTranspiration: false, bleedFraction: 0.20);
        var gate = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "TRANSPIRATION_BLEED_EXCESSIVE");
    }

    private static RegenGenerationResult MakeMinimalResult(
        bool enableTranspiration, double bleedFraction)
    {
        // Build a minimal but structurally-valid result that lets the
        // transpiration gate run without needing a full GenerateWith.
        var (cond, design) = HeatLimitedBaseline();
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true);
        return gen with
        {
            EnableTranspirationCooling = enableTranspiration,
            TranspirationBleedFraction = bleedFraction,
        };
    }

    // ── Schema migration ─────────────────────────────────────────────

    [Fact]
    public void Schema_V27Design_LoadsWithTranspirationDefaults()
    {
        // A v27 JSON file must migrate to current schema with transpiration
        // fields at their C# init-only defaults (false / 0.02 / 0.85).
        const string v27Json = """
            {
              "Schema": "v27",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 5000,
                "ChamberPressure_Pa": 6900000,
                "MixtureRatio": 3.3,
                "WallMaterialIndex": 1
              },
              "Design": { "ChamberRadius_mm": 30, "ThroatRadius_mm": 15 }
            }
            """;

        using var tmp = TestTempFile.WithUniqueName("transpiration-pre-v28", "json");
        File.WriteAllText(tmp.Path, v27Json);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.False(loaded.Design!.EnableTranspirationCooling);
        Assert.Equal(0.02, loaded.Design!.TranspirationBleedFraction, precision: 9);
        Assert.Equal(0.85, loaded.Design!.TranspirationEfficiency, precision: 9);
    }

    // ── BuildSheet ────────────────────────────────────────────────────

    [Fact]
    public void BuildSheet_IncludesTranspirationSection_WhenEnabled()
    {
        var cond   = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            EnableTranspirationCooling = true,
            TranspirationBleedFraction = 0.03,
            MountingFlangeStandard     = MountingFlangeStandard.MilStd_4Bolt_Small,
        };

        var md = BuildSheet.BuildMarkdown(cond, design);

        Assert.Contains("Transpiration cooling", md);
        Assert.Contains("3.0%", md);
    }

    [Fact]
    public void BuildSheet_OmitsTranspirationSection_WhenDisabled()
    {
        var cond   = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            EnableTranspirationCooling = false,
            MountingFlangeStandard     = MountingFlangeStandard.MilStd_4Bolt_Small,
        };

        var md = BuildSheet.BuildMarkdown(cond, design);

        Assert.DoesNotContain("Transpiration cooling", md);
    }

    // ── Pack / Unpack round-trip ──────────────────────────────────────

    [Fact]
    public void PackUnpack_PreservesTranspirationFlag()
    {
        // EnableTranspirationCooling is NOT an [SaDesignVariable], so
        // Pack / Unpack must preserve it via the baseline `with { }` path.
        var original = new RegenChamberDesign
        {
            EnableTranspirationCooling = true,
            TranspirationBleedFraction = 0.05,
            TranspirationEfficiency    = 0.90,
        };

        var packed   = RegenChamberOptimization.Pack(original);
        var unpacked = RegenChamberOptimization.Unpack(packed, original);

        Assert.True(unpacked.EnableTranspirationCooling,
            "EnableTranspirationCooling must survive Pack/Unpack round-trip.");
        Assert.Equal(0.05, unpacked.TranspirationBleedFraction, precision: 9);
        Assert.Equal(0.90, unpacked.TranspirationEfficiency, precision: 9);
    }
}
