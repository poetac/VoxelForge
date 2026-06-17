// ElectricPropulsionDesignPersistence.cs — JSON save/load for electric-
// propulsion designs.
//
// Schema-versioned persistence mirroring the rocket-side DesignPersistence
// + airbreathing-side AirbreathingDesignPersistence pattern. Wave-1 ships
// schema v1 only; the migration framework is in place so future schema
// bumps are a single-file change.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Voxelforge.ElectricPropulsion.IO;

/// <summary>
/// Top-level persisted shape for a saved resistojet design + conditions.
/// </summary>
public sealed class SavedElectricPropulsionDesign
{
    public string Schema { get; set; } = ElectricPropulsionDesignPersistence.CurrentSchemaVersion;
    public string Version { get; set; } = "1.0";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string AppName { get; set; } = "Voxelforge.ElectricPropulsion";
    public ResistojetConditions? Conditions { get; set; }
    public ElectricPropulsionEngineDesign? Design { get; set; }
}

/// <summary>
/// Thrown on load when the saved schema is newer than this binary supports.
/// </summary>
public sealed class UnsupportedElectricPropulsionSchemaException : InvalidOperationException
{
    public string FoundSchema { get; }
    public string CurrentSchema { get; }

    public UnsupportedElectricPropulsionSchemaException(string found, string current)
        : base($"Saved electric-propulsion design schema '{found}' is newer than this build supports "
             + $"(current: '{current}'). Update Voxelforge to a newer version, "
             + $"or re-save the file from an older build.")
    {
        FoundSchema = found;
        CurrentSchema = current;
    }
}

/// <summary>
/// JSON save / load surface for resistojet designs. Wave-1 ships v1.
/// Future schema bumps add migrations to <see cref="Migrations"/>;
/// the static constructor enforces migration completeness across the
/// declared <see cref="ElectricPropulsionSchemaVersion.Known"/> chain.
/// </summary>
public static class ElectricPropulsionDesignPersistence
{
    public static string CurrentSchemaVersion => ElectricPropulsionSchemaVersion.Current;

    static ElectricPropulsionDesignPersistence()
    {
        // Migration completeness guard. With only v1 the loop is a no-op.
        for (int i = 0; i < ElectricPropulsionSchemaVersion.Known.Length - 1; i++)
        {
            var pair = (ElectricPropulsionSchemaVersion.Known[i], ElectricPropulsionSchemaVersion.Known[i + 1]);
            if (!Migrations.ContainsKey(pair))
                throw new InvalidOperationException(
                    $"ElectricPropulsionDesignPersistence: missing migration for "
                  + $"{ElectricPropulsionSchemaVersion.Known[i]} → {ElectricPropulsionSchemaVersion.Known[i + 1]}. "
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

    /// <summary>
    /// Migrations registry. Key is (fromVersion, toVersion); value mutates
    /// the JsonNode in place.
    ///
    /// v1 → v2: identity migration. Wave-2 HET adds 8 init-only HET fields to
    /// <see cref="ElectricPropulsionEngineDesign"/> with NaN / <c>None</c>
    /// defaults. Resistojet v1 designs deserialise into v2 with the new
    /// fields at their defaults, which Resistojet ignores. No node mutation
    /// required — JsonSerializer fills missing properties with the record's
    /// default values per ADR-022.
    ///
    /// v2 → v3: identity migration. Wave-2 Arcjet adds 5 init-only arcjet
    /// fields (ArcVoltage_V, ArcCurrent_A, ArcGap_mm, ArcjetElectrodeMaterial,
    /// ArcjetThermalEfficiency) with NaN / <c>None</c> defaults. Resistojet
    /// + HET v2 designs deserialise into v3 with the new fields at their
    /// defaults, which Resistojet / HET ignore.
    ///
    /// v3 → v4: identity migration. Wave-2 PPT adds 6 init-only PPT fields
    /// (CapacitorEnergy_J, PulseFrequency_Hz, PptElectrodeGap_mm,
    /// PptPropellantBarLength_mm, PptElectrodeWidth_mm, PptIspCalibration)
    /// with NaN defaults. Resistojet / HET / Arcjet v3 designs deserialise
    /// into v4 with the new fields at their defaults, which the older kinds
    /// ignore.
    ///
    /// v4 → v5: identity migration. Wave-2 GIT adds 6 init-only GIT fields
    /// (BeamVoltage_V, BeamCurrent_A, ScreenGridRadius_mm, AccelGridGap_mm,
    /// NeutralizerCathodeCurrent_A, GitMassUtilizationOverride) with NaN
    /// defaults. Resistojet / HET / Arcjet / PPT v4 designs deserialise into
    /// v5 with the new fields at their defaults, which the older kinds
    /// ignore.
    ///
    /// v5 → v6: identity migration. Wave-2 MPD adds 4 init-only numeric MPD
    /// fields (MpdArcCurrent_A, MpdCathodeRadius_mm, MpdAnodeRadius_mm,
    /// MpdChamberLength_mm) with NaN defaults plus an MpdCathodeMaterial
    /// enum with <c>None</c> default. Resistojet / HET / Arcjet / PPT / GIT
    /// v5 designs deserialise into v6 with the new fields at their defaults,
    /// which the older kinds ignore.
    ///
    /// v6 → v7: identity migration. Wave-3 Applied-Field MPD (Sprint EP.W3.AF)
    /// adds 2 init-only numeric fields (MpdAppliedFieldStrength_T,
    /// MpdAppliedFieldCouplingOverride) with NaN defaults. Resistojet /
    /// HET / Arcjet / PPT / GIT / self-field-MPD v6 designs deserialise into
    /// v7 with the new fields at their defaults — NaN is treated as
    /// applied-field-disabled by the solver, producing bit-identical
    /// numerical output to the Wave-2 self-field-only path.
    ///
    /// v7 → v8: identity migration. Wave-3 VASIMR scaffold (Sprint EP.W4
    /// phase 1) adds 5 init-only numeric fields (VasimrHeliconRfPower_W,
    /// VasimrIcrhRfPower_W, VasimrSolenoidField_T,
    /// VasimrNozzleExitRadius_mm, VasimrArgonMassFlow_kgs) with NaN
    /// defaults. All prior kinds round-trip unchanged. The Kind=Vasimr
    /// physics dispatch still throws NotImplementedException pending
    /// EP.W4 phase 2 (helicon + ICRH + magnetic-nozzle solver).
    ///
    /// v8 → v9: identity migration. Wave-3 FEEP scaffold (Sprint EP.W5
    /// phase 1) adds 3 init-only numeric fields (FeepAcceleratingVoltage_V,
    /// FeepBeamCurrent_A, FeepEmitterTipRadius_mm) + 1 new FeepPropellant
    /// enum with NaN / <c>None</c> defaults. All prior kinds round-trip
    /// unchanged. The Kind=Feep physics dispatch still throws
    /// NotImplementedException pending EP.W5 phase 2 (Mair-Lozano emitter
    /// model + space-charge limited beam solver).
    ///
    /// v9 → v10: identity migration. Wave-3 HDLT scaffold (Sprint EP.W6
    /// phase 1) adds 4 init-only numeric fields (HdltHeliconRfPower_W,
    /// HdltMagneticFieldGradient_TpM, HdltChannelLength_mm,
    /// HdltArgonMassFlow_kgs) with NaN defaults. All prior kinds round-
    /// trip unchanged. The Kind=Hdlt physics dispatch still throws
    /// NotImplementedException pending EP.W6 phase 2 (helicon + current-
    /// free double-layer solver).
    /// </summary>
    private static readonly Dictionary<(string, string), Action<JsonNode>> Migrations
        = new()
        {
            { ("v1", "v2"), _ => { /* identity — defaults handle the new HET fields */ } },
            { ("v2", "v3"), _ => { /* identity — defaults handle the new Arcjet fields */ } },
            { ("v3", "v4"), _ => { /* identity — defaults handle the new PPT fields */ } },
            { ("v4", "v5"), _ => { /* identity — defaults handle the new GIT fields */ } },
            { ("v5", "v6"), _ => { /* identity — defaults handle the new MPD fields */ } },
            { ("v6", "v7"), _ => { /* identity — defaults handle the new applied-field MPD fields */ } },
            { ("v7", "v8"), _ => { /* identity — defaults handle the new VASIMR scaffold fields */ } },
            { ("v8", "v9"), _ => { /* identity — defaults handle the new FEEP scaffold fields */ } },
            { ("v9", "v10"), _ => { /* identity — defaults handle the new HDLT scaffold fields */ } },
        };

    /// <summary>
    /// Save a design + conditions pair to <paramref name="path"/> as JSON.
    /// </summary>
    public static void SaveJson(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        string path)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(path);

        var saved = new SavedElectricPropulsionDesign
        {
            Schema = CurrentSchemaVersion,
            Conditions = conditions,
            Design = design,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(saved, Opts));
    }

    /// <summary>
    /// Load a design + conditions pair from <paramref name="path"/>. Runs
    /// schema migrations as needed.
    /// </summary>
    public static SavedElectricPropulsionDesign LoadJson(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var json = File.ReadAllText(path);
        return ParseJson(json);
    }

    /// <summary>
    /// Parse a JSON string into a saved design + conditions pair. Runs
    /// schema migrations as needed.
    /// </summary>
    public static SavedElectricPropulsionDesign ParseJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json) ?? throw new InvalidDataException("JSON parsed to null.");
        string foundSchema = root["Schema"]?.GetValue<string>() ?? "v1";

        // Forward-incompatibility check.
        if (!ElectricPropulsionSchemaVersion.IsSupported(foundSchema))
        {
            // If the schema is "newer" (sorts after Current alphabetically),
            // fail; otherwise we don't know it but might be able to handle.
            throw new UnsupportedElectricPropulsionSchemaException(foundSchema, CurrentSchemaVersion);
        }

        // Apply migrations from foundSchema → CurrentSchemaVersion in sequence.
        int idx = Array.IndexOf(ElectricPropulsionSchemaVersion.Known, foundSchema);
        for (int i = idx; i < ElectricPropulsionSchemaVersion.Known.Length - 1; i++)
        {
            var pair = (ElectricPropulsionSchemaVersion.Known[i], ElectricPropulsionSchemaVersion.Known[i + 1]);
            if (Migrations.TryGetValue(pair, out var migrate))
            {
                migrate(root);
                root["Schema"] = ElectricPropulsionSchemaVersion.Known[i + 1];
            }
        }

        var migrated = root.ToJsonString();
        return JsonSerializer.Deserialize<SavedElectricPropulsionDesign>(migrated, Opts)
            ?? throw new InvalidDataException("SavedElectricPropulsionDesign deserialized to null.");
    }
}
