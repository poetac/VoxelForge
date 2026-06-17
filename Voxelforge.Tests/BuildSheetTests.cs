using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class BuildSheetTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N = 5_000,
        ChamberPressure_Pa = 6.9e6,
        MixtureRatio = 3.3,
        PropellantPair = PropellantPair.LOX_CH4,
        WallMaterialIndex = 1,
        UmbilicalStandard = UmbilicalStandard.AN_MS33656_08,
    };

    private static RegenChamberDesign DefaultDesign() => new()
    {
        MountingFlangeStandard = MountingFlangeStandard.MilStd_4Bolt_Small,
        CoolantPortStandard = PortStandard.G_1_4,
        PropellantPortStandard = PortStandard.G_1_8,
    };

    [Fact]
    public void Markdown_HasAllRequiredSections()
    {
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), DefaultDesign());

        Assert.Contains("# Test-Stand Build Sheet", md);
        Assert.Contains("## Engine summary", md);
        Assert.Contains("## Thrust-takeout flange", md);
        Assert.Contains("### Recommended bolt-up torque", md);
        Assert.Contains("## Ground-side umbilical", md);
        Assert.Contains("## Threaded ports on the chamber", md);
        Assert.Contains("## Instrumentation bosses", md);
        Assert.Contains("## Feed lines", md);
        Assert.Contains("## Pre-fire checklist", md);
        Assert.Contains("## Limitations", md);
    }

    [Fact]
    public void EngineSummary_ListsThrustChamberPressureAndCycle()
    {
        var cond = DefaultConditions() with { Thrust_N = 10_000, ChamberPressure_Pa = 10e6 };
        var md = BuildSheet.BuildMarkdown(cond, DefaultDesign());

        Assert.Contains("LOX_CH4", md);
        Assert.Contains("10000 N", md);
        Assert.Contains("10.00 MPa", md);
        Assert.Contains("PressureFed", md);
    }

    [Fact]
    public void ThrustTakeout_ListsBoltCountAndDiameter()
    {
        var design = DefaultDesign() with { MountingFlangeStandard = MountingFlangeStandard.MilStd_6Bolt_Clocked };
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), design);

        var spec = MountingFlangePresets.SpecFor(MountingFlangeStandard.MilStd_6Bolt_Clocked);
        Assert.Contains(spec.DisplayName, md);
        Assert.Contains($"| Bolt count | {spec.BoltCount} |", md);
        Assert.Contains($"M{spec.BoltDiameter_mm:F0}", md);
    }

    [Fact]
    public void BoltTorque_M5_MatchesIso898()
    {
        var design = DefaultDesign() with { MountingFlangeStandard = MountingFlangeStandard.Generic8Bolt };
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), design);

        Assert.Contains("6.0 N·m", md);
        Assert.Contains("4.2 N·m", md);
    }

    [Fact]
    public void FastenerTorqueTable_LookupsAreSensible()
    {
        Assert.Equal(6.0, FastenerTorqueTable.Lookup(5).Class88_Nm);
        Assert.Equal(25.0, FastenerTorqueTable.Lookup(8).Class88_Nm);
        Assert.Equal(50.0, FastenerTorqueTable.Lookup(10).Class88_Nm);
        Assert.True(FastenerTorqueTable.Lookup(5).Stainless_Nm < FastenerTorqueTable.Lookup(5).Class88_Nm,
            "stainless A2-70 torque should be lower than class 8.8 steel for the same size");
    }

    [Fact]
    public void FastenerTorqueTable_OutOfRangeBolt_NearestNeighbour()
    {
        var t14 = FastenerTorqueTable.Lookup(14);
        Assert.Equal(12, t14.BoltDiameter_mm);
        var t1 = FastenerTorqueTable.Lookup(1);
        Assert.Equal(3, t1.BoltDiameter_mm);
    }

    [Fact]
    public void Umbilical_None_ReportsNoneSelected()
    {
        var cond = DefaultConditions() with { UmbilicalStandard = UmbilicalStandard.None };
        var md = BuildSheet.BuildMarkdown(cond, DefaultDesign());

        Assert.Contains("**No umbilical selected**", md);
    }

    [Fact]
    public void Umbilical_Selected_ListsSpec()
    {
        var cond = DefaultConditions() with { UmbilicalStandard = UmbilicalStandard.Cryo_QD_Half_Inch };
        var md = BuildSheet.BuildMarkdown(cond, DefaultDesign());

        var spec = UmbilicalStandards.SpecFor(UmbilicalStandard.Cryo_QD_Half_Inch);
        Assert.Contains(spec.DisplayName, md);
        Assert.Contains($"{spec.FaceOuterDiameter_mm:F1} mm", md);
        Assert.Contains($"{spec.LossCoefficientK:F2}", md);
    }

    [Fact]
    public void Ports_Plain_ShowDashes_NotThreadDimensions()
    {
        var design = DefaultDesign() with
        {
            CoolantPortStandard = PortStandard.Plain,
            PropellantPortStandard = PortStandard.Plain,
        };
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), design);

        Assert.Contains("| Coolant inlet/outlet | Plain bore | — | — | — | — |", md);
        Assert.Contains("| Propellant inlet | Plain bore | — | — | — | — |", md);
    }

    [Fact]
    public void Ports_Threaded_ListMajorDiaAndPitch()
    {
        var design = DefaultDesign() with
        {
            CoolantPortStandard = PortStandard.G_1_4,
            PropellantPortStandard = PortStandard.NPT_1_8,
        };
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), design);

        Assert.Contains("G 1/4", md);
        Assert.Contains("1/8 NPT", md);
        Assert.Contains("13.16 mm", md);
        Assert.Contains("10.24 mm", md);
    }

    [Fact]
    public void NoSensorBosses_OmitsTable_AndReportsNone()
    {
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), DefaultDesign());

        Assert.Contains("**No instrumentation bosses on this design.**", md);
        Assert.DoesNotContain("| # | Type", md);
    }

    [Fact]
    public void WithSensorBosses_TableLists_AxialAzimuthBore()
    {
        var design = DefaultDesign() with
        {
            SensorBosses = new[]
            {
                new SensorBoss(AxialFraction: 0.10, AzimuthDeg:  0, Type: SensorBossType.Pressure_M5),
                new SensorBoss(AxialFraction: 0.50, AzimuthDeg: 90, Type: SensorBossType.Thermocouple_1_8_NPT),
            },
        };
        var md = BuildSheet.BuildMarkdown(DefaultConditions(), design);

        Assert.Contains("2 boss(es) on this design.", md);
        Assert.Contains("| 1 | Pressure tap (M5) |", md);
        Assert.Contains("| 2 | Thermocouple (1/8 NPT) |", md);
        Assert.Contains("| 0.10 |", md);
        Assert.Contains("| 0.50 |", md);
        Assert.Contains("| 90° |", md);
    }

    [Fact]
    public void CryogenicPair_FlagsCryoLineRequirements()
    {
        var cond = DefaultConditions() with { PropellantPair = PropellantPair.LOX_H2 };
        var md = BuildSheet.BuildMarkdown(cond, DefaultDesign());

        Assert.Contains("Cryogenic service | YES", md);
        Assert.Contains("vacuum-jacketed", md);
        Assert.Contains("**Cryo-line callouts:**", md);
    }

    [Fact]
    public void NonCryogenicPair_OmitsCryoCallouts()
    {
        var cond = DefaultConditions() with { PropellantPair = PropellantPair.LOX_RP1 };
        var md = BuildSheet.BuildMarkdown(cond, DefaultDesign());

        Assert.Contains("Cryogenic service | no", md);
        Assert.DoesNotContain("**Cryo-line callouts:**", md);
    }

    [Fact]
    public void SaveMarkdown_RoundTrip()
    {
        var cond = DefaultConditions();
        var design = DefaultDesign();
        string expected = BuildSheet.BuildMarkdown(cond, design);

        using var tmp = TestTempFile.WithUniqueName("build-sheet-test", "md");
        BuildSheet.SaveMarkdown(tmp.Path, cond, design);
        Assert.Equal(expected, File.ReadAllText(tmp.Path));
    }

    [Fact]
    public void SchemaVersion_IsExposed()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildSheet.ReportSchemaVersion));
        Assert.StartsWith("v", BuildSheet.ReportSchemaVersion);
    }
}
