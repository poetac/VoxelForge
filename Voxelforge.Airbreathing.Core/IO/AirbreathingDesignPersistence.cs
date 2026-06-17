// AirbreathingDesignPersistence.cs — JSON save/load for air-breathing engine designs.
//
// Schema-versioned persistence mirroring the rocket-side DesignPersistence.cs pattern.
// Every saved file carries a top-level "Schema" tag. On load:
//   • Tag matches CurrentSchemaVersion → parse as-is.
//   • Tag older than current → run migrations in sequence until current.
//   • Tag newer than current → throw UnsupportedAirbreathingSchemaException.
//   • Tag missing entirely → assume v1 (the only current version).
//
// Migration contract: each migration edits a JsonNode in place. Migrations are
// idempotent and declared in one place so a schema bump is a single-file change.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Voxelforge.Airbreathing;

namespace Voxelforge.Airbreathing.IO;

public sealed class SavedAirbreathingDesign
{
    public string Schema { get; set; } = AirbreathingDesignPersistence.CurrentSchemaVersion;
    public string Version { get; set; } = "1.0";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string AppName { get; set; } = "Voxelforge.Airbreathing";
    public FlightConditions? Conditions { get; set; }
    public AirbreathingEngineDesign? Design { get; set; }
}

/// <summary>
/// Thrown on load when the saved schema is newer than this binary supports
/// (forward incompatibility).
/// </summary>
public sealed class UnsupportedAirbreathingSchemaException : InvalidOperationException
{
    public string FoundSchema { get; }
    public string CurrentSchema { get; }

    public UnsupportedAirbreathingSchemaException(string found, string current)
        : base($"Saved design schema '{found}' is newer than this build supports "
             + $"(current: '{current}'). Update Voxelforge to a newer version, "
             + $"or re-save the file from an older build.")
    {
        FoundSchema = found;
        CurrentSchema = current;
    }
}

public static class AirbreathingDesignPersistence
{
    public static string CurrentSchemaVersion => AirbreathingSchemaVersion.Current;

    static AirbreathingDesignPersistence()
    {
        // Migration completeness guard — every consecutive Known[i] → Known[i+1] pair
        // must have a registered migration. With only v1 the loop runs zero times so
        // this is a no-op today; it catches a future bump that forgets to register.
        for (int i = 0; i < AirbreathingSchemaVersion.Known.Length - 1; i++)
        {
            var pair = (AirbreathingSchemaVersion.Known[i], AirbreathingSchemaVersion.Known[i + 1]);
            if (!Migrations.ContainsKey(pair))
                throw new InvalidOperationException(
                    $"AirbreathingDesignPersistence: missing migration for "
                  + $"{AirbreathingSchemaVersion.Known[i]} → {AirbreathingSchemaVersion.Known[i + 1]}. "
                  + $"Add an entry to Migrations (use an identity body if no data transform is required).");
        }
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static void SaveJson(AirbreathingEngineDesign design, FlightConditions cond, string path)
    {
        var envelope = new SavedAirbreathingDesign
        {
            Schema = CurrentSchemaVersion,
            Conditions = cond,
            Design = design,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(envelope, Opts));
    }

    public static (AirbreathingEngineDesign design, FlightConditions cond) LoadJson(string path)
    {
        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json);
        if (node is null)
            throw new InvalidOperationException($"Failed to parse JSON from '{path}'.");

        string schema = node["Schema"]?.ToString()
            ?? node["schema"]?.ToString()
            ?? AirbreathingSchemaVersion.Current;

        if (!IsKnownSchema(schema))
            throw new UnsupportedAirbreathingSchemaException(schema, CurrentSchemaVersion);

        int startIdx = Array.IndexOf(AirbreathingSchemaVersion.Known, schema);
        if (startIdx < 0) startIdx = 0;
        for (int i = startIdx; i < AirbreathingSchemaVersion.Known.Length - 1; i++)
        {
            var fromTo = (AirbreathingSchemaVersion.Known[i], AirbreathingSchemaVersion.Known[i + 1]);
            if (Migrations.TryGetValue(fromTo, out var migrate))
                migrate(node);
            node["Schema"] = AirbreathingSchemaVersion.Known[i + 1];
        }

        var loaded = JsonSerializer.Deserialize<SavedAirbreathingDesign>(node.ToJsonString(), Opts);
        ValidateRequiredFields(loaded, schema);
        return (loaded!.Design!, loaded.Conditions!);
    }

    private static void ValidateRequiredFields(SavedAirbreathingDesign? sd, string schemaTag)
    {
        if (sd?.Conditions is null)
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) is missing FlightConditions — "
              + $"file is corrupt or was hand-edited.");
        if (sd.Design is null)
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) is missing AirbreathingEngineDesign — "
              + $"file is corrupt or was hand-edited.");

        var d = sd.Design;
        var c = sd.Conditions;

        if (d.Kind == AirbreathingEngineKind.None)
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) has Kind=None. "
              + $"A valid design must specify an engine kind.");

        RequirePositive(d.InletThroatArea_m2, nameof(d.InletThroatArea_m2), schemaTag);
        RequirePositive(d.CombustorArea_m2, nameof(d.CombustorArea_m2), schemaTag);
        RequirePositive(d.CombustorLength_m, nameof(d.CombustorLength_m), schemaTag);
        RequirePositive(d.NozzleThroatArea_m2, nameof(d.NozzleThroatArea_m2), schemaTag);
        RequirePositive(d.NozzleExitArea_m2, nameof(d.NozzleExitArea_m2), schemaTag);
        RequirePositive(d.EquivalenceRatio, nameof(d.EquivalenceRatio), schemaTag);
        RequirePositive(c.MachNumber, nameof(c.MachNumber), schemaTag);
        RequireNonNegative(c.Altitude_m, nameof(c.Altitude_m), schemaTag);
    }

    private static void RequirePositive(double v, string fieldName, string schemaTag)
    {
        if (v <= 0 || double.IsNaN(v) || double.IsInfinity(v))
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) has invalid {fieldName}={v} "
              + $"(must be > 0 and finite). File is likely corrupt or a missing "
              + $"field was defaulted to 0 by the JSON deserialiser.");
    }

    private static void RequireNonNegative(double v, string fieldName, string schemaTag)
    {
        if (v < 0 || double.IsNaN(v) || double.IsInfinity(v))
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) has invalid {fieldName}={v} "
              + $"(must be ≥ 0 and finite). File is likely corrupt or was hand-edited.");
    }

    private static bool IsKnownSchema(string schema)
    {
        foreach (var s in AirbreathingSchemaVersion.Known)
            if (s == schema) return true;
        return false;
    }

    /// <summary>
    /// Migration chain. Keys are (fromVersion, toVersion). Each migration mutates
    /// the JsonNode in place. Add entries here when the schema bumps.
    /// </summary>
    private static readonly Dictionary<(string from, string to), Action<JsonNode>>
        Migrations = new()
        {
            // v1 → v2: TurbineCoolingFraction added with default 0.0.
            // JSON init defaults handle missing fields; nothing to mutate.
            [("v1", "v2")] = _ => { },
            // v2 → v3: RecuperatorEffectiveness + ShaftPowerTarget_W added
            // with defaults 0.0. JSON init defaults handle missing fields;
            // nothing to mutate.
            [("v2", "v3")] = _ => { },
            // v3 → v4: PiFan (nullable double) added with default null.
            // JSON init defaults handle missing fields; nothing to mutate.
            [("v3", "v4")] = _ => { },
            // v4 → v5: SteamBoilerPressure_bar + SteamCondensePressure_bar +
            // SteamSuperheatDeltaT_K added with defaults 0.0. JSON init defaults
            // handle missing fields; nothing to mutate.
            [("v4", "v5")] = _ => { },
            // v5 → v6: PulsejetTubeLength_m + PulsejetIntakeArea_m2 +
            // PulsejetTailpipeArea_m2 added with defaults 0.0 (Wave 1
            // sub-step 1a.5, ADR-026 §2). JSON init defaults handle
            // missing fields; nothing to mutate.
            [("v5", "v6")] = _ => { },
            // v6 → v7: EnableAfterburner (bool, default false) +
            // AfterburnerFuelAirRatio (double, default 0.0) added for
            // turbojet augmentation (Wave-2, issue #428 sub-task 3).
            // JSON init defaults handle missing fields; nothing to mutate.
            [("v6", "v7")] = _ => { },
            // v7 → v8: PulsejetVariant enum field added with default
            // Standard (= 0). JSON string-enum converter reads "Standard"
            // from persisted JSON, or a missing field defaults via C# init.
            // Nothing to mutate.
            [("v7", "v8")] = _ => { },
            // v8 → v9: PropellerPowerExtraction_frac added with default 0.0
            // (turboprop + turboshaft Wave-2, issue #428). JSON init defaults
            // handle missing fields; nothing to mutate.
            [("v8", "v9")] = _ => { },
            // v9 → v10: BypassDuctWallThickness_mm added with default 2.0
            // for the turbofan voxel builder (Wave-2 follow-on, issue #441).
            // JSON init defaults handle missing fields; nothing to mutate.
            [("v9", "v10")] = _ => { },
            // v10 → v11 (Sprint A.W3): 4 init-only LACE fields added with
            // 0.0 defaults (PrecoolerEffectiveness, LH2MassFlow_kgs,
            // LaceChamberPressure_bar, LaceAirToFuelRatio). Identity
            // migration — the LACE pipeline only runs when
            // AirbreathingEngineKind = LiquidAirCycle, so round-tripping a
            // non-LACE design across v11 is bit-identical.
            [("v10", "v11")] = _ => { },
            // v11 → v12 (Sprint A.W4): 4 init-only RDE numeric fields +
            // 1 int (RdeWaveCount) added with 0/0.0 defaults
            // (RdePressureGainRatio, RdeAnnularOuterDiameter_m,
            // RdeAnnularInnerDiameter_m, RdeAnnularLength_m). Identity
            // migration — the RDE pipeline only runs when
            // AirbreathingEngineKind = RotatingDetonation, so round-tripping
            // a non-RDE design across v12 is bit-identical.
            [("v11", "v12")] = _ => { },
        };
}
