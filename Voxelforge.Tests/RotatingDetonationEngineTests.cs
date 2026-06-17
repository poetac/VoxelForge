// OOB-7 (issue #343): rotating detonation engine topology tests.
using System.IO;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class RotatingDetonationEngineTests
{
    // ── RdeCombustion unit tests ────────────────────────────────────────────

    [Fact]
    public void IspGain_AllPairs_ReturnsGreaterThanOne()
    {
        foreach (var pair in new[] { PropellantPair.LOX_CH4, PropellantPair.LOX_H2, PropellantPair.LOX_RP1 })
        {
            double gain = RdeCombustion.IspGain(pair, 5e6);
            Assert.True(gain > 1.0,
                $"IspGain for {pair} at 5 MPa should exceed 1.0; got {gain:F4}");
        }
    }

    [Fact]
    public void IspGain_HigherPc_IncreasesGain_LOX_CH4()
    {
        double gainLow  = RdeCombustion.IspGain(PropellantPair.LOX_CH4, 2e6);
        double gainHigh = RdeCombustion.IspGain(PropellantPair.LOX_CH4, 10e6);
        Assert.True(gainHigh > gainLow,
            $"Higher Pc should yield more Isp gain: {gainHigh:F4} > {gainLow:F4}");
    }

    [Fact]
    public void IspGain_LOX_H2_HigherGainThanLOX_RP1()
    {
        double gainH2  = RdeCombustion.IspGain(PropellantPair.LOX_H2,  5e6);
        double gainRP1 = RdeCombustion.IspGain(PropellantPair.LOX_RP1, 5e6);
        Assert.True(gainH2 > gainRP1,
            $"LOX/H2 should have larger RDE gain than LOX/RP1: {gainH2:F4} vs {gainRP1:F4}");
    }

    [Fact]
    public void DetonationWaveCount_ReasonableGeometry_AtLeastOne()
    {
        // Outer radius 60 mm → circumference ≈ 0.377 m
        double circumference_m = 2.0 * System.Math.PI * 0.060;
        int n = RdeCombustion.DetonationWaveCount(circumference_m);
        Assert.True(n >= 1,
            $"Wave count must be at least 1 for a 60 mm outer radius annulus; got {n}");
    }

    [Fact]
    public void DetonationWaveCount_LargerAnnulus_MoreWaves()
    {
        double circSmall = 2.0 * System.Math.PI * 0.040;
        double circLarge = 2.0 * System.Math.PI * 0.200;
        int nSmall = RdeCombustion.DetonationWaveCount(circSmall);
        int nLarge = RdeCombustion.DetonationWaveCount(circLarge);
        Assert.True(nLarge >= nSmall,
            $"Larger annulus should support at least as many waves: {nLarge} >= {nSmall}");
    }

    [Fact]
    public void AnnulusFillTime_ReasonableInputs_ReturnsPositiveUs()
    {
        double fillTime = RdeCombustion.AnnulusFillTime_us(
            channelHeight_m: 0.020,
            injectorDp_Pa:   0.20 * 5e6,
            propDensity_kgm3: 700.0);
        Assert.True(fillTime > 0.0,
            $"Fill time must be positive for valid inputs; got {fillTime:F3} µs");
    }

    [Fact]
    public void AnnulusFillTime_ZeroInjectorDp_ReturnsInfinity()
    {
        double fillTime = RdeCombustion.AnnulusFillTime_us(0.020, 0.0, 700.0);
        Assert.True(double.IsPositiveInfinity(fillTime),
            "Zero injector ΔP should yield infinite fill time");
    }

    // ── GenerateWith integration tests ─────────────────────────────────────

    private static OperatingConditions BaseConditions() => new()
    {
        Thrust_N                = 5000.0,
        ChamberPressure_Pa      = 5e6,
        MixtureRatio            = 3.5,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 8e6,
        WallMaterialIndex       = 1,
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    [Fact]
    public void GenerateWith_RdeAnnular_HasElevatedIspVsNone()
    {
        var cond = BaseConditions();
        var designNone = new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts     = false,
            RdeTopology      = RdeTopology.None,
        };
        var designRde = designNone with { RdeTopology = RdeTopology.Annular };

        var resultNone = RegenChamberOptimization.GenerateWith(cond, designNone);
        var resultRde  = RegenChamberOptimization.GenerateWith(cond, designRde);

        Assert.True(resultRde.Derived.IdealIspVacuum_s > resultNone.Derived.IdealIspVacuum_s,
            $"RDE must elevate vacuum Isp: {resultRde.Derived.IdealIspVacuum_s:F3} > "
          + $"{resultNone.Derived.IdealIspVacuum_s:F3}");
    }

    [Fact]
    public void GenerateWith_RdeTopologyNone_EchoFieldsAreDefaults()
    {
        var cond   = BaseConditions();
        var design = new RegenChamberDesign { IncludeManifolds = false, IncludePorts = false };
        var result = RegenChamberOptimization.GenerateWith(cond, design);

        Assert.Equal(RdeTopology.None, result.RdeTopology);
        Assert.Equal(0, result.RdeWaveCount);
        Assert.Equal(0.0, result.RdeAnnulusFillTime_us, precision: 9);
    }

    [Fact]
    public void GenerateWith_RdeAnnular_PopulatesEchoFields()
    {
        var cond   = BaseConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds          = false,
            IncludePorts              = false,
            RdeTopology               = RdeTopology.Annular,
            RdeAnnulusOuterRadius_mm  = 60.0,
            RdeAnnulusWidth_mm        = 15.0,
            RdeChannelHeight_mm       = 20.0,
        };
        var result = RegenChamberOptimization.GenerateWith(cond, design);

        Assert.Equal(RdeTopology.Annular, result.RdeTopology);
        Assert.True(result.RdeWaveCount >= 1, $"Wave count must be ≥ 1; got {result.RdeWaveCount}");
        Assert.True(result.RdeAnnulusFillTime_us > 0.0,
            $"Fill time must be positive; got {result.RdeAnnulusFillTime_us:F3} µs");
    }

    // ── Gate tests ─────────────────────────────────────────────────────────

    [Fact]
    public void Gate_RDE_WAVE_COUNT_BELOW_MINIMUM_FiresWhenN_Below2()
    {
        // Very small outer radius → circumference ~ 6 mm → wave count rounds to 1.
        var cond   = BaseConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds         = false,
            IncludePorts             = false,
            RdeTopology              = RdeTopology.Annular,
            RdeAnnulusOuterRadius_mm = 1.0,   // tiny → 1 wave
        };
        var result = RegenChamberOptimization.GenerateWith(cond, design);
        var gate   = FeasibilityGate.Evaluate(result);

        Assert.Contains(gate.Violations, v => v.ConstraintId == "RDE_WAVE_COUNT_BELOW_MINIMUM");
    }

    [Fact]
    public void Gate_RDE_WAVE_COUNT_BELOW_MINIMUM_SilentWhenTopologyNone()
    {
        var cond   = BaseConditions();
        var design = new RegenChamberDesign { IncludeManifolds = false, IncludePorts = false };
        var result = RegenChamberOptimization.GenerateWith(cond, design);
        var gate   = FeasibilityGate.Evaluate(result);

        Assert.DoesNotContain(gate.Violations, v => v.ConstraintId == "RDE_WAVE_COUNT_BELOW_MINIMUM");
    }

    [Fact]
    public void Gate_RDE_WAVE_COUNT_BELOW_MINIMUM_SilentForLargeAnnulus()
    {
        // Large outer radius → many waves → gate silent.
        var cond   = BaseConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds         = false,
            IncludePorts             = false,
            RdeTopology              = RdeTopology.Annular,
            RdeAnnulusOuterRadius_mm = 200.0,
        };
        var result = RegenChamberOptimization.GenerateWith(cond, design);
        var gate   = FeasibilityGate.Evaluate(result);

        Assert.DoesNotContain(gate.Violations, v => v.ConstraintId == "RDE_WAVE_COUNT_BELOW_MINIMUM");
    }

    // ── Schema tests ────────────────────────────────────────────────────────

    [Fact]
    public void CurrentSchemaVersion_IsV31()
    {
        Assert.Equal("v31", DesignPersistence.CurrentSchemaVersion);
    }

    [Fact]
    public void Schema_V30Design_LoadsWithRdeDefaults()
    {
        const string v30Json = """
            {
              "Schema": "v30",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 5000,
                "ChamberPressure_Pa": 5000000,
                "MixtureRatio": 3.5,
                "CoolantInletTemp_K": 150,
                "CoolantInletPressure_Pa": 8000000,
                "WallMaterialIndex": 1
              },
              "Design": {}
            }
            """;
        using var tmp = TestTempFile.WithUniqueName("rde-migration-v30", "json");
        File.WriteAllText(tmp.Path, v30Json);
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.Equal("v31", loaded!.Schema);
        // v30 designs load with RdeTopology defaulting to None.
        Assert.Equal(RdeTopology.None, loaded.Design!.RdeTopology);
    }
}
