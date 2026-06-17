// MarineDesignPersistence.cs — JSON save/load for marine hull designs.
//
// Schema-versioned persistence mirroring the AirbreathingDesignPersistence
// pattern. Every saved file carries a top-level "Schema" tag. Migrations
// chain from the file's schema version to Current. Identity migrations
// (empty body) must be declared for every consecutive Known[i]→Known[i+1]
// pair so a future bump is a single-file change.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Voxelforge.Marine.IO;

public sealed class SavedMarineDesign
{
    public string Schema { get; set; } = MarineDesignPersistence.CurrentSchemaVersion;
    public string Version { get; set; } = "1.0";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string AppName { get; set; } = "Voxelforge.Marine";
    public MarineConditions? Conditions { get; set; }
    public MarineDesign? Design { get; set; }
}

/// <summary>
/// Thrown when the saved schema is newer than this binary supports.
/// </summary>
public sealed class UnsupportedMarineSchemaException : InvalidOperationException
{
    public string FoundSchema { get; }
    public string CurrentSchema { get; }

    public UnsupportedMarineSchemaException(string found, string current)
        : base($"Saved design schema '{found}' is newer than this build supports "
             + $"(current: '{current}'). Update Voxelforge to a newer version.")
    {
        FoundSchema = found;
        CurrentSchema = current;
    }
}

public static class MarineDesignPersistence
{
    public static string CurrentSchemaVersion => MarineSchemaVersion.Current;

    static MarineDesignPersistence()
    {
        for (int i = 0; i < MarineSchemaVersion.Known.Length - 1; i++)
        {
            var pair = (MarineSchemaVersion.Known[i], MarineSchemaVersion.Known[i + 1]);
            if (!Migrations.ContainsKey(pair))
                throw new InvalidOperationException(
                    $"MarineDesignPersistence: missing migration for "
                  + $"{MarineSchemaVersion.Known[i]} → {MarineSchemaVersion.Known[i + 1]}. "
                  + $"Add an entry to Migrations.");
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

    public static void SaveJson(MarineDesign design, MarineConditions cond, string path)
    {
        var envelope = new SavedMarineDesign { Schema = CurrentSchemaVersion, Conditions = cond, Design = design };
        File.WriteAllText(path, JsonSerializer.Serialize(envelope, Opts));
    }

    public static (MarineDesign design, MarineConditions cond) LoadJson(string path)
    {
        var json = File.ReadAllText(path);
        var node = JsonNode.Parse(json)
            ?? throw new InvalidOperationException($"Failed to parse JSON from '{path}'.");

        string schema = node["Schema"]?.ToString()
            ?? node["schema"]?.ToString()
            ?? MarineSchemaVersion.Current;

        if (!MarineSchemaVersion.IsSupported(schema))
            throw new UnsupportedMarineSchemaException(schema, CurrentSchemaVersion);

        int startIdx = Array.IndexOf(MarineSchemaVersion.Known, schema);
        if (startIdx < 0) startIdx = 0;
        for (int i = startIdx; i < MarineSchemaVersion.Known.Length - 1; i++)
        {
            var fromTo = (MarineSchemaVersion.Known[i], MarineSchemaVersion.Known[i + 1]);
            if (Migrations.TryGetValue(fromTo, out var migrate)) migrate(node);
            node["Schema"] = MarineSchemaVersion.Known[i + 1];
        }

        var loaded = JsonSerializer.Deserialize<SavedMarineDesign>(node.ToJsonString(), Opts);
        if (loaded?.Conditions is null)
            throw new InvalidOperationException("Saved design is missing MarineConditions.");
        if (loaded.Design is null)
            throw new InvalidOperationException("Saved design is missing MarineDesign.");

        return (loaded.Design, loaded.Conditions);
    }

    private static readonly Dictionary<(string from, string to), Action<JsonNode>> Migrations = new()
    {
        { ("v1", "v2"), node =>
            {
                // v2 adds HullFamily to MarineDesign; old designs default to Myring.
                var design = node["Design"] as JsonObject;
                design?.TryAdd("HullFamily", JsonValue.Create("Myring"));
            }
        },
        { ("v2", "v3"), _ =>
            {
                // v3 adds 5 init-only planing fields to MarineDesign
                // (BeamMidship_m, DeadriseAngle_deg, MassDisplacement_kg,
                // FreeboardHeight_m, LongitudinalCgFraction). Identity
                // migration — JsonSerializer fills missing properties with
                // the record's default (NaN) values per ADR-022.
                /* identity */
            }
        },
        { ("v3", "v4"), _ =>
            {
                // v4 adds 4 init-only displacement-surface fields to
                // MarineDesign (BeamWaterline_m, DraftDesign_m,
                // BlockCoefficient, DisplacementMass_kg) for the
                // Holtrop-Mennen pipeline + MarineKind.DisplacementSurface +
                // HullFamily.DisplacementSurface enum extensions. Identity
                // migration — round-tripped AUV / Planing designs keep
                // their NaN defaults on the new fields, and the
                // DisplacementSurface pipeline only fires when the kind
                // discriminator selects it.
                /* identity */
            }
        },
        { ("v4", "v5"), _ =>
            {
                // v5 — Sprint M.W5. Adds 1 init-only bool field
                // EnableSemiDisplacementCorrection to MarineDesign (default
                // false). Wave-1/W2/W3/W4 designs deserialise into v5
                // unchanged because the default-false flag leaves the
                // displacement-only behaviour bit-identical to Sprint M.W4
                // (semi-displacement Froude-band correction is dormant).
                /* identity */
            }
        },
    };
}
