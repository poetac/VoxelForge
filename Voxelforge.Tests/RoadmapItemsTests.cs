// RoadmapItemsTests.cs — Contract tests for the hardening sprint:
//
//   Structural-confidence pill
//   Schema migrations on .rcd.json
//   Measured-data overlay UI wiring (Bartz factor on OperatingConditions)
//   Mounting-flange preset library

using System.Text.Json;
using System.Text.Json.Nodes;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class RoadmapItemsTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Structural-confidence pill
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Confidence_HighWhenNoThreadsNoFlange()
    {
        var r = Generate(d => d with
        {
            IncludeInjectorFlange = false, IncludeMountingFlange = false,
            CoolantPortStandard = PortStandard.Plain,
            PropellantPortStandard = PortStandard.Plain,
        });
        Assert.Equal(StructuralConfidence.High, r.StructuralConfidence);
    }

    [Fact]
    public void Confidence_MediumWhenFlangeOrThreadsPresent()
    {
        var r = Generate(d => d with
        {
            IncludeInjectorFlange = true,     // flange present but plain ports
            CoolantPortStandard = PortStandard.Plain,
            PropellantPortStandard = PortStandard.Plain,
        });
        Assert.Equal(StructuralConfidence.Medium, r.StructuralConfidence);
    }

    [Fact]
    public void Confidence_LowWhenThreadedPropPortsThroughInjectorFlange()
    {
        var r = Generate(d => d with
        {
            IncludeInjectorFlange = true,
            PropellantPortStandard = PortStandard.M6_1p0,
        });
        Assert.Equal(StructuralConfidence.Low, r.StructuralConfidence);
        Assert.Contains("thread-root", r.StructuralConfidenceReason);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Schema migrations
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_CurrentTagIsV5()
    {
        // The schema bumps once per landed feature that touches
        // persistence; the full migration chain v4 →
        // CurrentSchemaVersion stays available. The test pins the
        // chain consistency rather than a hard-coded marker so
        // future bumps do not require touching this assertion.
        //   v7 → v8   — FilterStandard + FilterContaminationFraction
        //   v8 → v9   — Ablative* fields
        //   v9 → v10  — Chilldown* fields
        //   v10 → v11 — StartTransient* fields
        //   v11 → v12 — EngineCycle + Pump* fields
        Assert.Equal(DesignPersistence.CurrentSchemaVersion,
                     DesignPersistence.KnownSchemas[^1]);
        // Parse "vNN" and confirm we are at least at v9 (where filter
        // + ablative landed). Numeric compare avoids the "v10" < "v9"
        // string-ordinal trap.
        int currentN = int.Parse(DesignPersistence.CurrentSchemaVersion[1..]);
        Assert.True(currentN >= 9, $"Expected schema ≥ v9 after Phase 2 landed; got {currentN}.");
    }

    [Fact]
    public void Schema_LoadMissingTag_MigratesAsV4()
    {
        using var tmp = TestTempFile.WithUniqueName("schema_probe_v4_missing", "rcd.json");
        var obj = new JsonObject
        {
            ["Version"]     = "1.0",
            ["AppName"]     = "Voxelforge",
            ["CreatedUtc"]  = DateTime.UtcNow.ToString("o"),
            ["Conditions"]  = JsonNode.Parse(JsonSerializer.Serialize(new OperatingConditions())),
            ["Design"]      = JsonNode.Parse(JsonSerializer.Serialize(new RegenChamberDesign())),
        };
        File.WriteAllText(tmp.Path, obj.ToJsonString());
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        // Migration should have stamped the current schema tag.
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);   // migrated forward
        Assert.NotNull(loaded.Conditions);
        Assert.NotNull(loaded.Design);
    }

    [Fact]
    public void Schema_LoadFutureTag_Throws()
    {
        using var tmp = TestTempFile.WithUniqueName("schema_probe_v99", "rcd.json");
        var obj = new JsonObject
        {
            ["Schema"]      = "v99",
            ["Conditions"]  = JsonNode.Parse(JsonSerializer.Serialize(new OperatingConditions())),
            ["Design"]      = JsonNode.Parse(JsonSerializer.Serialize(new RegenChamberDesign())),
        };
        File.WriteAllText(tmp.Path, obj.ToJsonString());
        Assert.Throws<UnsupportedSchemaException>(() => DesignPersistence.Load(tmp.Path));
    }

    [Fact]
    public void Schema_SaveRoundTrip_PreservesTag()
    {
        using var tmp = TestTempFile.WithUniqueName("schema_probe_roundtrip", "rcd.json");
        DesignPersistence.Save(tmp.Path, new OperatingConditions(), new RegenChamberDesign(), r: null);
        var json = File.ReadAllText(tmp.Path);
        Assert.Contains($"\"Schema\": \"{DesignPersistence.CurrentSchemaVersion}\"", json);
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);   // migrated forward
    }

    // ─────────────────────────────────────────────────────────────────
    //  BartzScalingFactor on OperatingConditions
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BartzScalingFactor_DefaultIsOne()
    {
        var c = new OperatingConditions();
        Assert.Equal(1.0, c.BartzScalingFactor, precision: 6);
    }

    [Fact]
    public void BartzScalingFactor_CarriesOnInitWith()
    {
        var c = new OperatingConditions { BartzScalingFactor = 1.25 };
        Assert.Equal(1.25, c.BartzScalingFactor, precision: 6);
    }

    [Fact]
    public void BartzScalingFactor_ClampsInsideGenerateWith()
    {
        // Sanity: absurd factor should not NaN-propagate through the solver;
        // the Generate path clamps it to [0.2, 3.0] on consumption.
        var cond = new OperatingConditions { BartzScalingFactor = 99.0 };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        var r = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.True(double.IsFinite(r.Thermal.PeakGasSideWallT_K));
    }

    // ─────────────────────────────────────────────────────────────────
    //  §4 — Mounting-flange preset library
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MountingFlangePresets_CoverAllEnumValues()
    {
        foreach (MountingFlangeStandard s in System.Enum.GetValues<MountingFlangeStandard>())
            Assert.True(MountingFlangePresets.All.ContainsKey(s),
                $"Missing preset for {s}.");
    }

    [Fact]
    public void MountingFlangePresets_BoltCountsAreSensible()
    {
        foreach (var spec in MountingFlangePresets.All.Values)
        {
            Assert.InRange(spec.BoltCount, 3, 12);
            Assert.InRange(spec.BoltDiameter_mm, 3.0, 12.0);
            Assert.InRange(spec.FlangeMarginRadius_mm, 5.0, 25.0);
        }
    }

    [Fact]
    public void RegenChamberDesign_DefaultsToGeneric8Bolt()
    {
        var d = new RegenChamberDesign();
        Assert.Equal(MountingFlangeStandard.Generic8Bolt, d.MountingFlangeStandard);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static RegenGenerationResult Generate(Func<RegenChamberDesign, RegenChamberDesign> mutate)
    {
        var cond = new OperatingConditions { PropellantPair = Combustion.PropellantPair.LOX_CH4 };
        var design = mutate(new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            ContourStationCount = 40,
        });
        return RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
    }
}
