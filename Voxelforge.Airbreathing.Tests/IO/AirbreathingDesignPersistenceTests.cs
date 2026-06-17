using System.Text.Json;
using System.Text.Json.Nodes;
using Voxelforge.Airbreathing.IO;

namespace Voxelforge.Airbreathing.Tests.IO;

public sealed class AirbreathingDesignPersistenceTests
{
    // ── factory helpers ──────────────────────────────────────────────────────

    private static AirbreathingEngineDesign MakeDesign(AirbreathingEngineKind kind,
        double inletArea = 0.025, double phi = 0.4, double compressorPr = 15.0) =>
        new(kind, inletArea, 0.05, 0.5, 0.015, 0.03, phi, compressorPr);

    private static FlightConditions MakeCond(AirbreathingFuel fuel = AirbreathingFuel.H2) =>
        new(0.0, 0.85, fuel);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"airbreathing-test-{Guid.NewGuid():N}.json");

    // ── round-trip: one test per AirbreathingEngineKind ─────────────────────

    [Fact]
    public void RoundTrip_Ramjet_AllFields_Exact()
    {
        AssertRoundTrip(MakeDesign(AirbreathingEngineKind.Ramjet, compressorPr: 1.0), MakeCond());
    }

    [Fact]
    public void RoundTrip_Turbojet_AllFields_Exact()
    {
        AssertRoundTrip(MakeDesign(AirbreathingEngineKind.Turbojet), MakeCond());
    }

    [Fact]
    public void RoundTrip_Turbofan_AllFields_Exact()
    {
        AssertRoundTrip(MakeDesign(AirbreathingEngineKind.Turbofan, compressorPr: 30.0), MakeCond());
    }

    [Fact]
    public void RoundTrip_Scramjet_AllFields_Exact()
    {
        AssertRoundTrip(MakeDesign(AirbreathingEngineKind.Scramjet, compressorPr: 1.0), MakeCond());
    }

    [Fact]
    public void RoundTrip_Rbcc_AllFields_Exact()
    {
        AssertRoundTrip(MakeDesign(AirbreathingEngineKind.Rbcc, compressorPr: 1.0), MakeCond());
    }

    [Fact]
    public void RoundTrip_Pulsejet_AllFields_Exact()
    {
        // Argus As 109-014 (V-1 buzz bomb) reference geometry — see
        // Voxelforge/docs/pillar-specs/pulsejet.md "Validation fixtures".
        var design = MakeDesign(AirbreathingEngineKind.Pulsejet, compressorPr: 1.0)
            with
            {
                PulsejetTubeLength_m    = 3.40,
                PulsejetIntakeArea_m2   = 0.030,
                PulsejetTailpipeArea_m2 = 0.040,
            };
        AssertRoundTrip(design, MakeCond());
    }

    [Fact]
    public void RoundTrip_FlightConditions_AltitudeMach_Exact()
    {
        // Non-trivial altitude + Mach to verify both survive the round-trip.
        var cond = new FlightConditions(15_000.0, 2.5, AirbreathingFuel.H2);
        AssertRoundTrip(MakeDesign(AirbreathingEngineKind.Ramjet, compressorPr: 1.0), cond);
    }

    [Theory]
    [InlineData(AirbreathingFuel.H2)]
    [InlineData(AirbreathingFuel.JetA)]
    [InlineData(AirbreathingFuel.Jp8)]
    public void RoundTrip_AllFuelValues_Preserved(AirbreathingFuel fuel)
    {
        var cond = new FlightConditions(0.0, 0.85, fuel);
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(MakeDesign(AirbreathingEngineKind.Turbojet), cond, path);
            var (_, loadedCond) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.Equal(fuel, loadedCond.Fuel);
        }
        finally { File.Delete(path); }
    }

    // ── JSON shape pinning ───────────────────────────────────────────────────

    [Fact]
    public void JsonShape_TopLevelFields_AllPresent()
    {
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(MakeDesign(AirbreathingEngineKind.Turbojet), MakeCond(), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("Schema", out _), "Schema missing");
            Assert.True(root.TryGetProperty("Version", out _), "Version missing");
            Assert.True(root.TryGetProperty("CreatedUtc", out _), "CreatedUtc missing");
            Assert.True(root.TryGetProperty("AppName", out _), "AppName missing");
            Assert.True(root.TryGetProperty("Conditions", out _), "Conditions missing");
            Assert.True(root.TryGetProperty("Design", out _), "Design missing");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JsonShape_Design_AllPropertyNamesPresent()
    {
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(MakeDesign(AirbreathingEngineKind.Turbojet), MakeCond(), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var design = doc.RootElement.GetProperty("Design");
            Assert.True(design.TryGetProperty("Kind", out _));
            Assert.True(design.TryGetProperty("InletThroatArea_m2", out _));
            Assert.True(design.TryGetProperty("CombustorArea_m2", out _));
            Assert.True(design.TryGetProperty("CombustorLength_m", out _));
            Assert.True(design.TryGetProperty("NozzleThroatArea_m2", out _));
            Assert.True(design.TryGetProperty("NozzleExitArea_m2", out _));
            Assert.True(design.TryGetProperty("EquivalenceRatio", out _));
            Assert.True(design.TryGetProperty("CompressorPressureRatio", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JsonShape_Conditions_AllPropertyNamesPresent()
    {
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(MakeDesign(AirbreathingEngineKind.Ramjet, compressorPr: 1.0), MakeCond(), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var cond = doc.RootElement.GetProperty("Conditions");
            Assert.True(cond.TryGetProperty("Altitude_m", out _));
            Assert.True(cond.TryGetProperty("MachNumber", out _));
            Assert.True(cond.TryGetProperty("Fuel", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JsonShape_EnumFields_SerializedAsStrings_NotIntegers()
    {
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(MakeDesign(AirbreathingEngineKind.Turbojet), MakeCond(), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var kindToken = doc.RootElement.GetProperty("Design").GetProperty("Kind");
            var fuelToken = doc.RootElement.GetProperty("Conditions").GetProperty("Fuel");
            Assert.Equal(JsonValueKind.String, kindToken.ValueKind);
            Assert.Equal(JsonValueKind.String, fuelToken.ValueKind);
            Assert.Equal("Turbojet", kindToken.GetString());
            Assert.Equal("H2", fuelToken.GetString());
        }
        finally { File.Delete(path); }
    }

    // ── error cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Load_UnknownSchemaVersion_ThrowsUnsupportedException()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, ValidJsonWithSchema("xyz"));
            Assert.Throws<UnsupportedAirbreathingSchemaException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FutureSchemaVersion_ThrowsWithCorrectFoundSchema()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, ValidJsonWithSchema("v99"));
            var ex = Assert.Throws<UnsupportedAirbreathingSchemaException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
            Assert.Equal("v99", ex.FoundSchema);
            Assert.Equal(AirbreathingDesignPersistence.CurrentSchemaVersion, ex.CurrentSchema);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingConditions_ThrowsInvalidOperation()
    {
        const string json = """
            {
              "Schema": "v1",
              "Design": {
                "Kind": "Ramjet",
                "InletThroatArea_m2": 0.025,
                "CombustorArea_m2": 0.05,
                "CombustorLength_m": 0.5,
                "NozzleThroatArea_m2": 0.015,
                "NozzleExitArea_m2": 0.03,
                "EquivalenceRatio": 0.4,
                "CompressorPressureRatio": 1.0
              }
            }
            """;
        var path = TempPath();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidOperationException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingDesign_ThrowsInvalidOperation()
    {
        const string json = """
            {
              "Schema": "v1",
              "Conditions": {
                "Altitude_m": 0.0,
                "MachNumber": 0.85,
                "Fuel": "H2"
              }
            }
            """;
        var path = TempPath();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidOperationException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_NegativeArea_ThrowsInvalidOperation()
    {
        // Save a design with an invalid area; LoadJson validation must catch it.
        var bad = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Ramjet, -1.0, 0.05, 0.5, 0.015, 0.03, 0.4, 1.0);
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(bad, MakeCond(), path);
            Assert.Throws<InvalidOperationException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ZeroMachNumber_ThrowsInvalidOperation()
    {
        var cond = new FlightConditions(0.0, 0.0, AirbreathingFuel.H2);
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(
                MakeDesign(AirbreathingEngineKind.Ramjet, compressorPr: 1.0), cond, path);
            Assert.Throws<InvalidOperationException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_KindNone_ThrowsInvalidOperation()
    {
        var bad = new AirbreathingEngineDesign(
            AirbreathingEngineKind.None, 0.025, 0.05, 0.5, 0.015, 0.03, 0.4, 1.0);
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(bad, MakeCond(), path);
            Assert.Throws<InvalidOperationException>(
                () => AirbreathingDesignPersistence.LoadJson(path));
        }
        finally { File.Delete(path); }
    }

    // ── determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void Determinism_SameDesignSavedTwice_ByteIdentical()
    {
        var design = MakeDesign(AirbreathingEngineKind.Turbojet);
        var cond = MakeCond();
        string path1 = TempPath(), path2 = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(design, cond, path1);
            AirbreathingDesignPersistence.SaveJson(design, cond, path2);

            // CreatedUtc will differ by wall-clock ms; null it out before comparing.
            var json1 = JsonNode.Parse(File.ReadAllText(path1))!;
            var json2 = JsonNode.Parse(File.ReadAllText(path2))!;
            json1["CreatedUtc"] = null;
            json2["CreatedUtc"] = null;
            Assert.Equal(json1.ToJsonString(), json2.ToJsonString());
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    // ── infrastructure ───────────────────────────────────────────────────────

    [Fact]
    public void StaticCtor_AllMigrationsRegistered_DoesNotThrow()
    {
        // Accessing any static member triggers type initialisation.
        // If the migration completeness guard fires it throws here, not silently.
        _ = AirbreathingDesignPersistence.CurrentSchemaVersion;
    }

    [Fact]
    public void SchemaVersion_InSavedFile_EqualsCurrentSchemaVersion()
    {
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(MakeDesign(AirbreathingEngineKind.Turbojet), MakeCond(), path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(
                AirbreathingDesignPersistence.CurrentSchemaVersion,
                doc.RootElement.GetProperty("Schema").GetString());
        }
        finally { File.Delete(path); }
    }

    // ── v9 → v10 identity migration ───────────────────────────────────────────

    [Fact]
    public void Load_V9_AddsBypassDuctWallThicknessDefault()
    {
        // v9-shaped JSON without BypassDuctWallThickness_mm (turbofan voxel
        // builder slice, schema v9 → v10 — issue #441 follow-on). The
        // identity migration should default the new field to 2.0 mm.
        const string v9Json = """
            {
              "Schema": "v9",
              "Conditions": {
                "Altitude_m": 0.0,
                "MachNumber": 0.85,
                "Fuel": "JetA"
              },
              "Design": {
                "Kind": "Turbofan",
                "InletThroatArea_m2": 0.0030,
                "CombustorArea_m2": 0.0050,
                "CombustorLength_m": 0.040,
                "NozzleThroatArea_m2": 0.0020,
                "NozzleExitArea_m2": 0.0030,
                "EquivalenceRatio": 0.85,
                "CompressorPressureRatio": 8.0,
                "BypassRatio": 0.34
              }
            }
            """;
        var path = TempPath();
        try
        {
            File.WriteAllText(path, v9Json);
            var (design, _) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.Equal(AirbreathingEngineKind.Turbofan, design.Kind);
            // Default from AirbreathingEngineDesign init.
            Assert.Equal(2.0, design.BypassDuctWallThickness_mm);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RoundTrip_TurbofanWithBypassDuctWallThickness_PreservesField()
    {
        var design = MakeDesign(AirbreathingEngineKind.Turbofan, compressorPr: 8.0)
            with { BypassDuctWallThickness_mm = 3.5 };
        AssertRoundTrip(design, MakeCond(AirbreathingFuel.JetA));
    }

    // ── shared helpers ───────────────────────────────────────────────────────

    private static void AssertRoundTrip(AirbreathingEngineDesign original, FlightConditions originalCond)
    {
        var path = TempPath();
        try
        {
            AirbreathingDesignPersistence.SaveJson(original, originalCond, path);
            var (design, cond) = AirbreathingDesignPersistence.LoadJson(path);
            Assert.Equal(original, design);
            Assert.Equal(originalCond, cond);
        }
        finally { File.Delete(path); }
    }

    private static string ValidJsonWithSchema(string schema) => $$"""
        {
          "Schema": "{{schema}}",
          "Conditions": {
            "Altitude_m": 0.0,
            "MachNumber": 2.0,
            "Fuel": "H2"
          },
          "Design": {
            "Kind": "Ramjet",
            "InletThroatArea_m2": 0.025,
            "CombustorArea_m2": 0.05,
            "CombustorLength_m": 0.5,
            "NozzleThroatArea_m2": 0.015,
            "NozzleExitArea_m2": 0.03,
            "EquivalenceRatio": 0.4,
            "CompressorPressureRatio": 1.0
          }
        }
        """;
}
