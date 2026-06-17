// AblativeThroatTests.cs — OOB-14 (#341) ablative + regen hybrid throat.
//
// Covers: dispatcher family/HasChannelPhase, GenerateWith gen.Ablative non-null,
// ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET gate, ABLATIVE_REGEN_INTERFACE_OVERTEMP gate,
// schema v28 → v29 migration, BuildSheet conditional section.

using System.IO;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class AblativeThroatTests
{
    // ── Dispatcher ───────────────────────────────────────────────────

    [Fact]
    public void Dispatcher_ClassifyFamily_ReturnsAblativeThroat()
    {
        var family = ChannelTopologyDispatcher.ClassifyFamily(ChannelTopology.AblativeThroat);
        Assert.Equal(ChannelTopologyDispatcher.Family.AblativeThroat, family);
    }

    [Fact]
    public void Dispatcher_IsAblativeThroat_ReturnsTrue()
    {
        Assert.True(ChannelTopologyDispatcher.IsAblativeThroat(ChannelTopology.AblativeThroat));
        Assert.False(ChannelTopologyDispatcher.IsAblativeThroat(ChannelTopology.Axial));
        Assert.False(ChannelTopologyDispatcher.IsAblativeThroat(ChannelTopology.None));
    }

    [Fact]
    public void Dispatcher_HasChannelPhase_TrueForAblativeThroat()
    {
        // AblativeThroat runs regen channels in the chamber + divergence band.
        // HasChannelPhase must return true so the full coolant march executes.
        Assert.True(ChannelTopologyDispatcher.HasChannelPhase(ChannelTopology.AblativeThroat));
    }

    [Fact]
    public void Design_AblativeZoneDefaults_Are030And070()
    {
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.AblativeThroat,
        };
        Assert.Equal(0.30, design.AblativeZoneStart_frac, precision: 9);
        Assert.Equal(0.70, design.AblativeZoneEnd_frac,   precision: 9);
    }

    // ── GenerateWith integration ─────────────────────────────────────

    [Fact]
    public void GenerateWith_AblativeThroat_SilicaPhenolic_AttachesAblativeResult()
    {
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.AblativeThroat,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 8.0,
            AblativeBurnDuration_s = 30,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        Assert.NotNull(gen.Ablative);
        Assert.Equal(AblativeMaterial.SilicaPhenolic, gen.Ablative!.Material);
        Assert.Equal(ChannelTopology.AblativeThroat, gen.ChannelTopology);
    }

    // ── ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET gate ────────────────

    [Fact]
    public void Gate_RecessionExceedsBudget_FiresWhenNotAcceptable()
    {
        // Under-sized ablative + long burn → IsAcceptable = false → gate fires.
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.AblativeThroat,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 0.5,   // too thin
            AblativeBurnDuration_s = 90,    // too long
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        Assert.NotNull(gen.Ablative);
        Assert.False(gen.Ablative!.IsAcceptable,
            "Thin ablative + long burn should be marked unacceptable.");

        var gate = FeasibilityGate.Evaluate(gen);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET");
    }

    [Fact]
    public void Gate_RecessionExceedsBudget_SilentWhenAcceptable()
    {
        // Adequately thick ablative for a short burn should be acceptable.
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.AblativeThroat,
            AblativeMaterial       = AblativeMaterial.GraphitePyrolytic,
            AblativeThickness_mm   = 15.0,
            AblativeBurnDuration_s = 5,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        Assert.NotNull(gen.Ablative);
        Assert.True(gen.Ablative!.IsAcceptable,
            "Thick GraphitePyrolytic for a 5 s burn should be acceptable.");

        var gate = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET");
    }

    // ── ABLATIVE_REGEN_INTERFACE_OVERTEMP gate ───────────────────────

    [Fact]
    public void Gate_InterfaceOvertemp_FiresWhenPeakTExceedsCharTemp()
    {
        // Inject PeakGasSideWallT_K above SilicaPhenolic char temp (1500 K)
        // via record `with` expression. Gate should fire.
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.AblativeThroat,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 8.0,
            AblativeBurnDuration_s = 30,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        // Synthetically raise peak T well above SilicaPhenolic char T (1500 K).
        var hotGen = gen with
        {
            Thermal = gen.Thermal! with { PeakGasSideWallT_K = 1800.0 },
        };

        var gate = FeasibilityGate.Evaluate(hotGen);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "ABLATIVE_REGEN_INTERFACE_OVERTEMP");
    }

    [Fact]
    public void Gate_InterfaceOvertemp_SilentForNonAblativeThroatTopology()
    {
        // The gate must only fire for AblativeThroat topology — a standard
        // axial-channel design with high T should NOT trigger it.
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.Axial,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 8.0,
            AblativeBurnDuration_s = 30,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        var hotGen = gen with
        {
            Thermal = gen.Thermal! with { PeakGasSideWallT_K = 1800.0 },
        };

        var gate = FeasibilityGate.Evaluate(hotGen);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "ABLATIVE_REGEN_INTERFACE_OVERTEMP");
    }

    [Fact]
    public void Gate_InterfaceOvertemp_SilentWhenPeakTBelowCharTemp()
    {
        // Peak T at 1000 K < SilicaPhenolic char T 1500 K → gate silent.
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.AblativeThroat,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 8.0,
            AblativeBurnDuration_s = 30,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        var coolGen = gen with
        {
            Thermal = gen.Thermal! with { PeakGasSideWallT_K = 1000.0 },
        };

        var gate = FeasibilityGate.Evaluate(coolGen);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "ABLATIVE_REGEN_INTERFACE_OVERTEMP");
    }

    // ── Schema migration ─────────────────────────────────────────────

    [Fact]
    public void Schema_V28Design_LoadsWithAblativeZoneDefaults()
    {
        // A v28 JSON file must migrate to current schema with AblativeZone
        // fields at their C# init-only defaults (0.30 / 0.70).
        const string v28Json = """
            {
              "Schema": "v28",
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

        using var tmp = TestTempFile.WithUniqueName("ablative-pre-v29", "json");
        File.WriteAllText(tmp.Path, v28Json);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.Equal(0.30, loaded.Design!.AblativeZoneStart_frac, precision: 9);
        Assert.Equal(0.70, loaded.Design!.AblativeZoneEnd_frac,   precision: 9);
    }

    // ── BuildSheet ────────────────────────────────────────────────────

    [Fact]
    public void BuildSheet_IncludesAblativeThroatSection_WhenEnabled()
    {
        var cond   = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            ChannelTopology        = ChannelTopology.AblativeThroat,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 8.0,
            AblativeBurnDuration_s = 30,
            AblativeZoneStart_frac = 0.25,
            AblativeZoneEnd_frac   = 0.75,
            MountingFlangeStandard = MountingFlangeStandard.MilStd_4Bolt_Small,
        };

        var md = BuildSheet.BuildMarkdown(cond, design);

        Assert.Contains("Ablative", md);
        Assert.Contains("SilicaPhenolic", md);
    }

    [Fact]
    public void BuildSheet_OmitsAblativeThroatSection_WhenTopologyIsAxial()
    {
        var cond   = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            ChannelTopology        = ChannelTopology.Axial,
            AblativeMaterial       = AblativeMaterial.None,
            MountingFlangeStandard = MountingFlangeStandard.MilStd_4Bolt_Small,
        };

        var md = BuildSheet.BuildMarkdown(cond, design);

        Assert.DoesNotContain("Ablative + regen hybrid throat", md);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static (OperatingConditions cond, RegenChamberDesign design) Baseline()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 40,
        };
        return (cond, design);
    }
}
