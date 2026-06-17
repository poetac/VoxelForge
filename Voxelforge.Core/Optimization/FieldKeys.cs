// FieldKeys.cs — UI overhaul Sprint 1, Step 1 (2026-04-28).
//
// Compile-time-checked string constants identifying every field the
// UI knows about. Each constant pairs a UI control with a rule entry
// in UiVisibilityRules, so the rules table can refer to fields by
// constant name (compiler-checked) rather than ad-hoc strings (only
// caught at runtime).
//
// Two key invariants:
//   1. Every [SaDesignVariable] property in RegenChamberDesign has a
//      matching FieldKeys constant — the SaDimCompletenessTest
//      reflection-based test fails the build if someone adds an SA
//      dim and forgets a key here.
//   2. Every constant declared here has a rule in UiVisibilityRules
//      — the RuleCompletenessTest fails the build if someone adds
//      a key here and forgets the rule.
//
// Together these make the rules table impossible to leave out of sync
// with the SA registry or the UI controls.

namespace Voxelforge.Optimization;

/// <summary>
/// Source-of-truth string keys for every UI field whose visibility may
/// be conditionally controlled. Reference these from
/// <see cref="UiVisibilityRules"/> rather than hard-coding strings.
/// </summary>
public static class FieldKeys
{
    // ── SA design variables (RegenChamberDesign) ─────────────────────
    // Every [SaDesignVariable]-tagged property MUST have a matching key
    // here. SaDimCompletenessTest fails the build otherwise.

    public const string ContractionRatio                    = "contractionRatio";
    public const string ExpansionRatio                      = "expansionRatio";
    public const string CharacteristicLength_m              = "characteristicLength_m";
    public const string BellEntranceAngle_deg               = "bellEntranceAngle_deg";
    public const string BellExitAngle_deg                   = "bellExitAngle_deg";
    public const string BellLengthFraction                  = "bellLengthFraction";
    public const string ChannelCount                        = "channelCount";
    public const string ChannelHeightChamber_mm             = "channelHeightChamber_mm";
    public const string ChannelHeightThroat_mm              = "channelHeightThroat_mm";
    public const string ChannelHeightExit_mm                = "channelHeightExit_mm";
    public const string RibThickness_mm                     = "ribThickness_mm";
    public const string GasSideWallThickness_mm             = "gasSideWallThickness_mm";
    public const string OuterJacketThickness_mm             = "outerJacketThickness_mm";
    public const string TpmsCellEdge_mm                     = "tpmsCellEdge_mm";
    public const string TpmsSolidFraction                   = "tpmsSolidFraction";
    public const string PreburnerMrRatio                    = "preburnerMrRatio";
    public const string FlangeRadialProjection_mm           = "flangeRadialProjection_mm";
    public const string PlugLengthRatio                     = "plugLengthRatio";
    public const string AerospikeContractionRatio           = "aerospikeContractionRatio";
    public const string FilmFuelFraction                    = "filmFuelFraction";
    public const string FilmSlotHeightOverride_mm           = "filmSlotHeightOverride_mm";
    public const string PintleDiameterOverride_mm           = "pintleDiameterOverride_mm";
    public const string PintleSleeveHoleCountOverride       = "pintleSleeveHoleCountOverride";
    public const string ChamberWallThicknessOverride_mm     = "chamberWallThicknessOverride_mm";
    public const string ThroatWallThicknessOverride_mm      = "throatWallThicknessOverride_mm";
    public const string ExitWallThicknessOverride_mm        = "exitWallThicknessOverride_mm";

    // ── Categorical discriminators ───────────────────────────────────
    // Always-shown (these are the pickers that drive other visibility).

    public const string PropellantPair                      = "propellantPair";
    public const string EngineCycle                         = "engineCycle";
    public const string ChannelTopology                     = "channelTopology";
    public const string MixtureRatio                        = "mixtureRatio";
    public const string ChamberPressure_MPa                 = "chamberPressure_MPa";
    public const string Thrust_N                            = "thrust_N";
    public const string AmbientPressure_kPa                 = "ambientPressure_kPa";
    public const string WallMaterial                        = "wallMaterial";
    public const string InjectorPatternKind                 = "injectorPatternKind";

    // ── Cycle-dependent fields (turbopump / preburner / turbine) ────

    public const string PumpInletPressure_MPa               = "pumpInletPressure_MPa";
    public const string PreburnerChamberPressure_MPa        = "preburnerChamberPressure_MPa";
    public const string TurbineInletTemperature_K           = "turbineInletTemperature_K";
    public const string TurbinePressureRatio                = "turbinePressureRatio";
    public const string PreburnerCoolingChannelCount        = "preburnerCoolingChannelCount";
    public const string PreburnerCoolingChannelDepth_mm     = "preburnerCoolingChannelDepth_mm";

    // ── Aerospike-only fields ───────────────────────────────────────

    public const string AerospikePlugCooling                = "aerospikePlugCooling";
    public const string LinearAerospikePlugWidth_mm         = "linearAerospikePlugWidth_mm";
    public const string LinearAerospikePlugDepth_mm         = "linearAerospikePlugDepth_mm";

    // ── Film cooling subsystem (opt-in) ─────────────────────────────

    public const string FilmCoolingEnabled                  = "filmCoolingEnabled";
    public const string FilmInjectionAxialFraction          = "filmInjectionAxialFraction";

    // ── Hot-fire readiness subsystems (opt-in) ──────────────────────

    public const string ChilldownEnabled                    = "chilldownEnabled";
    public const string StartTransientEnabled               = "startTransientEnabled";
    public const string LpbfPrintabilityEnabled             = "lpbfPrintabilityEnabled";

    // ── Mounting / instrumentation / igniter ────────────────────────

    public const string MountingFlangeEnabled               = "mountingFlangeEnabled";
    public const string MountingFlangeStandard              = "mountingFlangeStandard";
    public const string IgniterType                         = "igniterType";
    public const string SensorBosses                        = "sensorBosses";

    // ── Acoustic dampers (OOB-6 / Sprint B-3, 2026-04-30) ───────────
    // The three SA-tagged knobs (HelmholtzNeckArea_mm2 dim 31,
    // HelmholtzCavityVolume_mm3 dim 32, QuarterWaveLength_mm dim 33)
    // each need a FieldKey so the SaDimCompleteness completeness test
    // round-trips. Naming convention: lowercase first letter, mirrors
    // the CamelCase property name 1:1.
    public const string HelmholtzNeckArea_mm2               = "helmholtzNeckArea_mm2";
    public const string HelmholtzCavityVolume_mm3           = "helmholtzCavityVolume_mm3";
    public const string QuarterWaveLength_mm                = "quarterWaveLength_mm";

    // ── Injector face import (opt-in STL passthrough) ───────────────

    public const string InjectorStlEnabled                  = "injectorStlEnabled";
    public const string InjectorStlPath                     = "injectorStlPath";

    // ── Voxel + run-control settings (always shown) ─────────────────

    public const string VoxelSize_mm                        = "voxelSize_mm";
    public const string SaIterations                        = "saIterations";
    public const string SaSeed                              = "saSeed";

    /// <summary>
    /// Every key defined above, returned as a sorted list. Used by
    /// the rule-completeness test to verify that
    /// <see cref="UiVisibilityRules"/> has a rule for every key.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = BuildAll();

    private static System.Collections.Generic.List<string> BuildAll()
    {
        // Reflection over our own consts. Stable + maintenance-free —
        // adding a new const above automatically appears here.
        var keys = new System.Collections.Generic.List<string>();
        foreach (var f in typeof(FieldKeys).GetFields(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.FlattenHierarchy))
        {
            if (f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            {
                if (f.GetValue(null) is string value)
                    keys.Add(value);
            }
        }
        keys.Sort(System.StringComparer.Ordinal);
        return keys;
    }
}
