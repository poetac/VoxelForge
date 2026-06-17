// DesignPersistence.cs — JSON save/load for regen chamber designs.
//
// Schema-versioned persistence. Every saved design carries a
// top-level `"schema"` tag. On load:
//   • Tag matches CurrentSchemaVersion → parse as-is.
//   • Tag older than current → run migrations in sequence until current.
//   • Tag newer than current → throw UnsupportedSchemaException; the user
//     is on an older binary than the file was written from.
//   • Tag missing entirely → legacy file (< v5), assume pre-schema v4
//     and migrate forward.
//
// Migration contract: each migration edits a `JsonDocument`-derived
// mutable representation in place. Migrations are idempotent and
// declared in one place so a new schema bump is a single-file change.

using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelforge.Optimization;

namespace Voxelforge.IO;

public sealed class SavedDesign
{
    /// <summary>
    /// Schema version, set to <see cref="DesignPersistence.CurrentSchemaVersion"/>
    /// on save. Consumed by the migration chain on load.
    /// </summary>
    public string Schema { get; set; } = DesignPersistence.CurrentSchemaVersion;
    public string Version { get; set; } = "1.0";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
    // PR-2 namespace rename (2026-04-30): AppName field default kept as
    // the literal "RegenChamberDesigner" so existing on-disk JSON saves
    // round-trip without forcing a schema bump (v21 → v22). Updating to
    // "Voxelforge" is a separate, demand-gated decision.
    public string AppName { get; set; } = "RegenChamberDesigner";
    public OperatingConditions? Conditions { get; set; }
    public RegenChamberDesign? Design { get; set; }
    public SavedResults? Results { get; set; }
}

public sealed class SavedResults
{
    public double ThroatDiameter_mm { get; set; }
    public double ExitDiameter_mm { get; set; }
    public double ChamberLength_mm { get; set; }
    public double TotalLength_mm { get; set; }
    public double MassFlowTotal_kgs { get; set; }
    public double PeakWallT_K { get; set; }
    public double CoolantOutletT_K { get; set; }
    public double CoolantDP_Pa { get; set; }
    public double TotalHeatLoad_W { get; set; }
    public double Mass_g { get; set; }
    public double Cost_USD { get; set; }
    public double MinSafetyFactor { get; set; }
    public double IspVacuum_s { get; set; }
}

/// <summary>
/// Thrown on load when the saved schema is newer than this binary
/// supports (forward incompatibility).
/// </summary>
public sealed class UnsupportedSchemaException : InvalidOperationException
{
    public string FoundSchema { get; }
    public string CurrentSchema { get; }
    public UnsupportedSchemaException(string found, string current)
        : base($"Saved design schema '{found}' is newer than this build supports "
             + $"(current: '{current}'). Update Voxelforge to a newer "
             + $"version, or re-save the file from an older build.")
    {
        FoundSchema = found;
        CurrentSchema = current;
    }
}

public static class DesignPersistence
{
    /// <summary>
    /// Current schema tag. Bump whenever a breaking change to the
    /// on-disk `RegenChamberDesign` / `OperatingConditions` shape lands.
    /// Migration chain in <see cref="Migrations"/> must cover every
    /// older version up to this one.
    /// </summary>
    public const string CurrentSchemaVersion = "v31";

    /// <summary>
    /// Ordered list of schemas from oldest → newest. Each entry maps to
    /// a migration in <see cref="Migrations"/> (except the current one).
    /// </summary>
    public static readonly string[] KnownSchemas = { "v4", "v5", "v6", "v7", "v8", "v9", "v10", "v11", "v12", "v13", "v14", "v15", "v16", "v17", "v18", "v19", "v20", "v21", "v22", "v23", "v24", "v25", "v26", "v27", "v28", "v29", "v30", "v31" };

    static DesignPersistence()
    {
        // Migration completeness: every consecutive (KnownSchemas[i],
        // KnownSchemas[i+1]) pair must have a registered migration. A
        // future schema bump that forgets to register a migration would
        // otherwise silently load files with the wrong shape (the loop
        // in Load() uses TryGetValue and skips unknown pairs). Catching
        // the omission at type-init time turns a corrupt-data risk into
        // a fast crash on first use.
        for (int i = 0; i < KnownSchemas.Length - 1; i++)
        {
            var pair = (KnownSchemas[i], KnownSchemas[i + 1]);
            if (!Migrations.ContainsKey(pair))
                throw new InvalidOperationException(
                    $"DesignPersistence: missing migration for "
                  + $"{KnownSchemas[i]} → {KnownSchemas[i + 1]}. Add an "
                  + $"entry to Migrations (use an identity body if no "
                  + $"data transform is required).");
        }
    }

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(string path, OperatingConditions c, RegenChamberDesign d, RegenGenerationResult? r)
    {
        var sd = new SavedDesign
        {
            Schema = CurrentSchemaVersion,
            Conditions = c,
            Design = d,
            Results = r == null ? null : new SavedResults
            {
                ThroatDiameter_mm = r.Derived.ThroatDiameter_mm,
                ExitDiameter_mm = 2 * r.Contour.ExitRadius_mm,
                ChamberLength_mm = r.Contour.ChamberLength_mm,
                TotalLength_mm = r.Contour.TotalLength_mm,
                MassFlowTotal_kgs = r.Derived.TotalMassFlow_kgs,
                PeakWallT_K = r.Thermal.PeakGasSideWallT_K,
                CoolantOutletT_K = r.Thermal.CoolantOutletT_K,
                CoolantDP_Pa = r.Thermal.CoolantPressureDrop_Pa,
                TotalHeatLoad_W = r.Thermal.TotalHeatLoad_W,
                Mass_g = r.Geometry.TotalMass_g,
                Cost_USD = r.Geometry.PrintedCost_USD,
                MinSafetyFactor = r.Stress.MinSafetyFactor,
                IspVacuum_s = r.Derived.IdealIspVacuum_s,
            },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(sd, Opts));
    }

    public static SavedDesign? Load(string path)
    {
        var json = File.ReadAllText(path);
        // First parse as a raw JsonNode so we can inspect the schema tag
        // and run migrations before binding into SavedDesign. The deserialiser
        // otherwise silently defaults any missing new fields.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node is null) return null;

        string schema = node["Schema"]?.ToString() ?? node["schema"]?.ToString() ?? "v4";

        if (!KnownFutureCompatible(schema))
            throw new UnsupportedSchemaException(schema, CurrentSchemaVersion);

        int startIdx = Array.IndexOf(KnownSchemas, schema);
        if (startIdx < 0) startIdx = 0;      // unknown old tag → migrate from v4
        for (int i = startIdx; i < KnownSchemas.Length - 1; i++)
        {
            var fromTo = (KnownSchemas[i], KnownSchemas[i + 1]);
            if (Migrations.TryGetValue(fromTo, out var migrate))
                migrate(node);
            node["Schema"] = KnownSchemas[i + 1];
        }

        var loaded = JsonSerializer.Deserialize<SavedDesign>(node.ToJsonString(), Opts);
        if (loaded != null) ValidateRequiredFields(loaded, schema);
        return loaded;
    }

    /// <summary>
    /// Sanity-check the deserialised <see cref="SavedDesign"/> for
    /// fields the runtime treats as non-null / strictly-positive.
    /// JSON deserialisation otherwise silently defaults missing fields
    /// to CLR zero values, producing a downstream NullReferenceException
    /// or divide-by-zero deep inside the solver. Throwing here turns
    /// that into a clear "this save file is corrupt or pre-schema"
    /// message at load time.
    /// </summary>
    private static void ValidateRequiredFields(SavedDesign sd, string schemaTag)
    {
        if (sd.Conditions is null)
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) is missing OperatingConditions — "
              + $"file is corrupt or was hand-edited.");
        if (sd.Design is null)
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) is missing RegenChamberDesign — "
              + $"file is corrupt or was hand-edited.");

        var c = sd.Conditions;
        RequirePositive(c.Thrust_N, nameof(c.Thrust_N), schemaTag);
        RequirePositive(c.ChamberPressure_Pa, nameof(c.ChamberPressure_Pa), schemaTag);
        RequirePositive(c.MixtureRatio, nameof(c.MixtureRatio), schemaTag);
        RequirePositive(c.CoolantInletTemp_K, nameof(c.CoolantInletTemp_K), schemaTag);
        RequirePositive(c.CoolantInletPressure_Pa, nameof(c.CoolantInletPressure_Pa), schemaTag);
    }

    private static void RequirePositive(double v, string fieldName, string schemaTag)
    {
        if (v <= 0 || double.IsNaN(v) || double.IsInfinity(v))
            throw new InvalidOperationException(
                $"Saved design (schema {schemaTag}) has invalid {fieldName}={v} "
              + $"(must be > 0 and finite). Likely cause: missing field defaulted "
              + $"to 0 by the JSON deserialiser.");
    }

    private static bool KnownFutureCompatible(string schema)
    {
        // Anything in KnownSchemas (or older) is fine. Anything else = future.
        foreach (var s in KnownSchemas) if (s == schema) return true;
        return false;
    }

    /// <summary>
    /// Migration chain. Keys are (fromVersion, toVersion). Each migration
    /// mutates <paramref name="node"/> in place with the delta. New
    /// migrations get appended when the schema bumps.
    /// </summary>
    private static readonly Dictionary<(string from, string to), Action<System.Text.Json.Nodes.JsonNode>>
        Migrations = new()
        {
            // v4 → v5 (2026-04-22): added SensorBosses, ChannelTopology,
            // HelixPitchAngle_deg, IncludeCoolantCrossover,
            // CoolantCrossoverDiameter_mm on RegenChamberDesign; added
            // the `Schema` tag on the envelope. All added fields have
            // safe defaults, so the migration only adds the tag — the
            // deserialiser picks up the defaults for any missing field.
            [("v4", "v5")] = node => { node["Schema"] = "v5"; },

            // v5 → v6 (2026-04-23): added
            //   • OperatingConditions.TankUllagePressure_Pa (default 0 = feed
            //     stackup opt-out; existing v5 files stay disabled)
            //   • OperatingConditions.FeedLineLength_m, FeedLineDiameter_mm,
            //     MainValveCv, FilterDeltaP_Pa
            //   • OperatingConditions.UmbilicalStandard (None by default)
            //   • RegenChamberDesign.IgniterType (None by default)
            //   • RegenChamberDesign.IgniterRadialFraction (0)
            // Every added field defaults to a safe no-op value, so the
            // migration is identity on the body — just bumps the tag.
            [("v5", "v6")] = node => { node["Schema"] = "v6"; },

            // v6 → v7: dome + gimbal + purge fields added:
            //   • RegenChamberDesign.FuelDomeDepth_mm (0 = no dome cut)
            //   • RegenChamberDesign.OxDomeDepth_mm
            //   • RegenChamberDesign.DomeInletDiameter_mm (8.0)
            //   • RegenChamberDesign.IncludeAntiVortexBaffle (false)
            //   • RegenChamberDesign.MountConfiguration (FixedFlange)
            //   • RegenChamberDesign.PurgePorts (empty list)
            // All defaults preserve v6 behaviour. Identity migration.
            [("v6", "v7")] = node => { node["Schema"] = "v7"; },

            // v7 → v8: filter fields added:
            //   • OperatingConditions.FilterStandard (Custom — falls
            //     back to the existing FilterDeltaP_Pa scalar)
            //   • OperatingConditions.FilterContaminationFraction (0)
            // Defaults preserve v7 behaviour exactly. Identity migration.
            [("v7", "v8")] = node => { node["Schema"] = "v8"; },

            // v8 → v9: ablative fields added:
            //   • RegenChamberDesign.AblativeMaterial (None — analysis skipped)
            //   • RegenChamberDesign.AblativeThickness_mm (5)
            //   • RegenChamberDesign.AblativeBurnDuration_s (30)
            //   • RegenChamberDesign.AblativeSafetyFactor (1.5)
            // None defaults skip the analysis entirely. Identity migration.
            [("v8", "v9")] = node => { node["Schema"] = "v9"; },

            // v9 → v10: chilldown-transient fields added on
            // OperatingConditions (opt-in via
            // IncludeChilldownTransient). Identity migration — every
            // added field has a no-op default.
            [("v9", "v10")] = node => { node["Schema"] = "v10"; },

            // v10 → v11: start-transient simulator fields added on
            // OperatingConditions (opt-in via IncludeStartTransient).
            // Identity migration.
            [("v10", "v11")] = node => { node["Schema"] = "v11"; },

            // v11 → v12: turbopump-sizing fields added on
            // OperatingConditions (EngineCycle, PumpInletPressure_Pa,
            // PumpDischargePressure_Pa, PumpEfficiency). PressureFed
            // default + auto-sized pressures preserve v11 behaviour.
            // Identity migration.
            [("v11", "v12")] = node => { node["Schema"] = "v12"; },

            // v12 → v13 (Sprint 15 / Track G, 2026-04-22): aerospike
            // plug-channel regen-cooling opt-in fields added on
            // RegenChamberDesign:
            //   • IncludeAerospikeRegenCooling  (default false)
            //   • AerospikePlugChannelCount     (default 24)
            //   • AerospikePlugChannelWidth_mm  (default 2.5)
            //   • AerospikePlugChannelDepth_mm  (default 2.0)
            //   • AerospikePlugWallThickness_mm (default 0.8)
            // The default false on IncludeAerospikeRegenCooling preserves
            // pre-Sprint-15 behaviour bit-identically (geometry-only
            // aerospike pipeline, no thermal solve). Identity migration.
            [("v12", "v13")] = node => { node["Schema"] = "v13"; },

            // v13 → v14 (Sprint 20, 2026-04-22): dual-bell altitude-
            // compensating nozzle opt-in fields added on RegenChamberDesign:
            //   • IncludeDualBell         (default false)
            //   • SeaLevelExpansionRatio  (default 0.0 — not configured)
            //   • InflectionAngle_deg     (default 7.0)
            // The default false on IncludeDualBell preserves pre-Sprint-20
            // single-bell behaviour bit-identically. Identity migration.
            [("v13", "v14")] = node => { node["Schema"] = "v14"; },

            // v14 → v15 (Sprint 19 / Pressure-fed polish, 2026-04-23):
            // BlowDownFinalPressure_Pa added on OperatingConditions.
            // Default 0 disables blow-down mode (regulated pressure-fed)
            // so v14 files round-trip bit-identically. Identity migration.
            // (Rebased from Sprint 19's original v13→v14 slot after Sprint
            // 20 / Sprint 21 landed first and claimed v14.)
            [("v14", "v15")] = node => { node["Schema"] = "v15"; },

            // v15 → v16 (Sprint 27 / Printability gates, 2026-04-23):
            // LPBF printability opt-in fields added on RegenChamberDesign:
            //   • IncludeLpbfPrintabilityAnalysis  (default false)
            //   • LpbfMaterial                     (default CuCrZr)
            //   • LpbfPrintOrientationAxis_deg     (default -1 = auto)
            // Default false on IncludeLpbfPrintabilityAnalysis preserves
            // pre-Sprint-27 behaviour bit-identically (Printability stays
            // null; OVERHANG_ANGLE_EXCEEDED / TRAPPED_POWDER_REGION /
            // DRAIN_PATH_MISSING gates silent). Identity migration.
            [("v15", "v16")] = node => { node["Schema"] = "v16"; },

            // v16 → v17 (Sprint 26 / Linear aerospike, 2026-04-23):
            // ChannelTopology.LinearAerospike enum value added + two
            // opt-in RegenChamberDesign fields:
            //   • LinearAerospikePlugWidth_mm  (default 60.0)
            //   • LinearAerospikeAspectRatio    (default 1.0)
            // The defaults preserve pre-Sprint-26 behaviour bit-
            // identically — existing v16 files never carry
            // LinearAerospike topology (the enum value didn't exist), so
            // the migration is identity on the body and just bumps the tag.
            // (Rebased from Sprint 26's original v15→v16 slot after Sprint
            // 27 landed first and claimed v16.)
            [("v16", "v17")] = node => { node["Schema"] = "v17"; },

            // Sprint 35 / PH-4 (2026-04-25): 1-D propellant tables → 2-D
            // bilinear (Pc × MR) CEA tables. PropellantState is runtime-
            // computed from the table at lookup time and never serialised,
            // so the migration is identity on the body and just bumps the tag.
            [("v17", "v18")] = node => { node["Schema"] = "v18"; },

            // Issue #158 / A6 (2026-04-28): added
            // OperatingConditions.OxidizerInletTemp_K (default 0 =
            // sentinel "use legacy constant table"). Antoine equation
            // computes P_vap from this T when set. Default 0 preserves
            // bit-identical pre-A6 NPSHA computation for v18 files;
            // identity migration just bumps the tag.
            [("v18", "v19")] = node => { node["Schema"] = "v19"; },

            // PH-47 / issue #192 (2026-04-29): added
            // OperatingConditions.BurnTime_s (default 0 = no battery mass)
            // and OperatingConditions.BatteryEnergyDensity_kg_per_MJ
            // (default 1.0 ≈ Li-Po at 1 MJ/kg). Both fields are new;
            // default 0 / 1.0 preserve bit-identical TurbopumpResult for
            // pre-v20 electric-pump designs (BurnTime_s = 0 → no battery).
            [("v19", "v20")] = node => { node["Schema"] = "v20"; },

            // Issue #194 / PH-49 (2026-04-29): added
            // RegenChamberDesign.TapOffAxialStation_frac (default 0.5 =
            // mid-chamber). Identity migration — default preserves the
            // prior flat-chamber-T behaviour for legacy designs.
            [("v20", "v21")] = node => { node["Schema"] = "v21"; },

            // Issue #259 / PH-40 (2026-04-29): added
            // RegenChamberDesign.MissionCycles (default 1 = single-firing
            // dev hardware). Drives Coffin-Manson LCF gating; default 1
            // is below the 100-cycle gate threshold so legacy v21 files
            // load with only the Notes-disclosure firing — bit-identical
            // feasibility for pre-v22 designs.
            [("v21", "v22")] = node => { node["Schema"] = "v22"; },

            // Issue #260 / Hot-fire readiness Item 6 (2026-04-30): added
            // RegenChamberDesign.IncludeThrustTakeoutAdapter (default
            // false) plus five adapter-geometry fields. Default off
            // preserves the legacy "no adapter body" voxel output for
            // legacy designs — feasibility is gate-neutral and the
            // BuildSheet adapter section appears only when the flag is
            // explicitly set.
            [("v22", "v23")] = node => { node["Schema"] = "v23"; },

            // Issue #200 / OOB-6 (2026-04-30): added
            // RegenChamberDesign.DamperType (default None) plus seven
            // Helmholtz / quarter-wave geometry fields, three of which
            // are SA-tagged (HelmholtzNeckArea_mm2 = dim 31,
            // HelmholtzCavityVolume_mm3 = dim 32, QuarterWaveLength_mm =
            // dim 33). DamperType = None preserves legacy "no damper"
            // behaviour bit-identically — StabilityScreening returns a
            // null AcousticDamperResult and the advisory gates self-
            // suppress when no damper is on the design.
            [("v23", "v24")] = node => { node["Schema"] = "v24"; },

            // Issue #213 / OOB-13 (2026-04-30): added
            // ChannelTopology.ExpansionDeflection (E-D nozzle).
            // No new fields on RegenChamberDesign — existing bell knobs
            // (ContractionRatio, ExpansionRatio, BellLengthFraction, …)
            // all apply to the E-D outer bell. The topology is serialised
            // as the string "ExpansionDeflection" and deserialises cleanly
            // via the standard JsonStringEnumConverter path. Identity migration.
            [("v24", "v25")] = node => { node["Schema"] = "v25"; },

            // Issue #348 / OOB-1 follow-on (2026-05-01): added
            // OperatingConditions.CoolantHtcScalingFactor (default 1.0) and
            // OperatingConditions.CoolantFrictionScalingFactor (default 1.0).
            // Both calibration knobs default to 1.0 = identity, so v25 designs
            // load with bit-identical thermal and pressure-drop outputs.
            [("v25", "v26")] = node => { node["Schema"] = "v26"; },

            // Sprint C / #350 (2026-05-04): added
            // OperatingConditions.GimbalOffset_mm (default 0.0).
            // New COMBINED_AXIAL_BENDING_INSUFFICIENT gate self-suppresses at
            // the default, so v26 designs load with bit-identical outputs.
            // Identity migration.
            [("v26", "v27")] = node => { node["Schema"] = "v27"; },

            // OOB-12 (2026-05-04): added transpiration cooling fields on
            // RegenChamberDesign (EnableTranspirationCooling=false,
            // TranspirationBleedFraction=0.02, TranspirationEfficiency=0.85).
            // All default to disabled so v27 designs load with bit-identical
            // thermal outputs. Identity migration.
            [("v27", "v28")] = node => { node["Schema"] = "v28"; },
            // OOB-14 (issue #341): adds ChannelTopology.AblativeThroat + AblativeZoneStart_frac /
            // AblativeZoneEnd_frac with defaults 0.30 / 0.70. Identity migration —
            // v28 designs load with AblativeThroat path unavailable (topology stays whatever
            // it was; zone-bound defaults are inert for non-AblativeThroat topologies).
            [("v28", "v29")] = node => { node["Schema"] = "v29"; },
            // OOB-9 (issue #344): adds OperatingConditions.UseFiniteRateCorrection (default false).
            // Identity migration — v29 designs load with correction disabled, preserving
            // bit-identical equilibrium Isp outputs from pre-OOB-9 designs.
            [("v29", "v30")] = node => { node["Schema"] = "v30"; },
            // OOB-7 (issue #343): adds RegenChamberDesign.RdeTopology (default None) +
            // RdeAnnulusOuterRadius_mm + RdeAnnulusWidth_mm + RdeChannelHeight_mm.
            // Identity migration — v30 designs load with RdeTopology=None, preserving
            // bit-identical deflagration physics from pre-OOB-7 designs.
            [("v30", "v31")] = node => { node["Schema"] = "v31"; },
        };
}
