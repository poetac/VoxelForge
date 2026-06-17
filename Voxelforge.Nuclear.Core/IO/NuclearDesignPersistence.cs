// NuclearDesignPersistence.cs — JSON save/load for nuclear thermal designs.
//
// Schema-versioned persistence mirroring MarineDesignPersistence. Every saved
// file carries a top-level "Schema" tag. Migrations chain from the file's
// schema version to Current. Identity migrations (empty body) must be declared
// for every consecutive Known[i]→Known[i+1] pair so a future bump is a
// single-file change.
//
// Wave-1 schema v1 is the initial version. The completeness guard in the
// static constructor enforces that all consecutive pairs in Known have a
// migration entry — adding v2 without a v1→v2 migration is a startup error.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Voxelforge.Nuclear.IO;

public sealed class SavedNuclearDesign
{
    public string Schema    { get; set; } = NuclearDesignPersistence.CurrentSchemaVersion;
    public string Version   { get; set; } = "1.0";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string AppName   { get; set; } = "Voxelforge.Nuclear";
    public NuclearThermalConditions? Conditions { get; set; }
    public NuclearThermalDesign?     Design     { get; set; }
}

/// <summary>
/// Thrown when the saved schema is newer than this binary supports.
/// </summary>
public sealed class UnsupportedNuclearSchemaException : InvalidOperationException
{
    public string FoundSchema   { get; }
    public string CurrentSchema { get; }

    public UnsupportedNuclearSchemaException(string found, string current)
        : base($"Saved design schema '{found}' is newer than this build supports "
             + $"(current: '{current}'). Update Voxelforge to a newer version.")
    {
        FoundSchema   = found;
        CurrentSchema = current;
    }
}

public static class NuclearDesignPersistence
{
    public static string CurrentSchemaVersion => NuclearSchemaVersion.Current;

    static NuclearDesignPersistence()
    {
        for (int i = 0; i < NuclearSchemaVersion.Known.Length - 1; i++)
        {
            var pair = (NuclearSchemaVersion.Known[i], NuclearSchemaVersion.Known[i + 1]);
            if (!Migrations.ContainsKey(pair))
                throw new InvalidOperationException(
                    $"NuclearDesignPersistence: missing migration for "
                  + $"{NuclearSchemaVersion.Known[i]} → {NuclearSchemaVersion.Known[i + 1]}. "
                  + $"Add an entry to Migrations.");
        }
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented              = true,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
        Converters                 = { new JsonStringEnumConverter() },
        AllowTrailingCommas        = true,
        ReadCommentHandling        = JsonCommentHandling.Skip,
        NumberHandling             = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static void SaveJson(
        NuclearThermalDesign     design,
        NuclearThermalConditions cond,
        string                   path)
    {
        var envelope = new SavedNuclearDesign
        {
            Schema     = CurrentSchemaVersion,
            Conditions = cond,
            Design     = design,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(envelope, Opts));
    }

    public static (NuclearThermalDesign design, NuclearThermalConditions cond)
        LoadJson(string path)
    {
        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json)
            ?? throw new InvalidOperationException($"Failed to parse JSON from '{path}'.");

        string schema = node["Schema"]?.ToString()
            ?? node["schema"]?.ToString()
            ?? NuclearSchemaVersion.Current;

        if (!NuclearSchemaVersion.IsSupported(schema))
            throw new UnsupportedNuclearSchemaException(schema, CurrentSchemaVersion);

        int startIdx = Array.IndexOf(NuclearSchemaVersion.Known, schema);
        if (startIdx < 0) startIdx = 0;
        for (int i = startIdx; i < NuclearSchemaVersion.Known.Length - 1; i++)
        {
            var fromTo = (NuclearSchemaVersion.Known[i], NuclearSchemaVersion.Known[i + 1]);
            if (Migrations.TryGetValue(fromTo, out var migrate)) migrate(node);
            node["Schema"] = NuclearSchemaVersion.Known[i + 1];
        }

        var loaded = JsonSerializer.Deserialize<SavedNuclearDesign>(node.ToJsonString(), Opts);
        if (loaded?.Conditions is null)
            throw new InvalidOperationException("Saved design is missing NuclearThermalConditions.");
        if (loaded.Design is null)
            throw new InvalidOperationException("Saved design is missing NuclearThermalDesign.");

        return (loaded.Design, loaded.Conditions);
    }

    private static readonly Dictionary<(string from, string to), Action<JsonNode>> Migrations
        = new()
        {
            { ("v1", "v2"), _ =>
                {
                    // Sprint NU.W2 — identity migration. 6 new init-only fuel-pin
                    // fields on NuclearThermalDesign (FuelPinDiameter_mm,
                    // FuelPinPitch_mm, FuelPinHexRings, FuelElementCount,
                    // FuelPinLength_m, FuelPinHotChannelFactor) default to
                    // NaN / 0. Wave-1 designs deserialise into v2 unchanged;
                    // the per-pin model is skipped (NuclearOptimization
                    // activation guard) until the four required fields are
                    // populated.
                    /* identity */
                }
            },
            { ("v2", "v3"), _ =>
                {
                    // Sprint NU.W3 — identity migration. 1 new init-only
                    // BimodalMode enum (default Thrust) + 5 new init-only
                    // Brayton fields (ElectricPowerTarget_kWe,
                    // BraytonTurbineInletTemp_K, BraytonHePressure_bar,
                    // AlternatorRpm, BraytonRecuperatorEffectiveness) default
                    // to 0.0 / NaN on NuclearThermalDesign. Wave-1/Wave-2
                    // designs deserialise into v3 unchanged; the Brayton
                    // loop is skipped (NuclearOptimization activation guard)
                    // unless Kind = BimodalNtr and BimodalMode != Thrust.
                    /* identity */
                }
            },
            { ("v3", "v4"), _ =>
                {
                    // Sprint NU.W4 — identity migration. 1 new init-only
                    // NuclearFuelMaterial enum on NuclearThermalDesign
                    // defaults to None. Wave-1/W2/W3 designs deserialise
                    // into v4 unchanged because None resolves to
                    // UO₂-cermet anchors (k=16, T_max=3200 K) — the
                    // same constants the prior Wave-2 model hard-coded.
                    /* identity */
                }
            },
            { ("v4", "v5"), _ =>
                {
                    // Sprint NU.W5 — identity migration. 1 new init-only
                    // UraniumEnrichment enum on NuclearThermalDesign
                    // defaults to None. Wave-1/W2/W3/W4 designs
                    // deserialise into v5 unchanged because None resolves
                    // to HEU (4000 MW/m³ ceiling — the same constant the
                    // prior NTR_THERMAL_FLUX_EXCEEDED gate hard-coded).
                    /* identity */
                }
            },
        };
}
