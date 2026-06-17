// ElectricPropulsionSchemaVersion.cs — pillar schema-version registry.
//
// Wave-1 ships v1 (initial). Bumps follow ADR-022 (rocket schema-version
// pattern) and ADR-026 §4 (per-pillar schema-version constants).
//
// Identity migrations (defaults preserve bit-identical reads) do not
// require migration code; they only bump Current + extend Known. Breaking
// migrations require an inline migration step in
// ElectricPropulsionDesignPersistence.Load (added in Sprint E.4).

using System;

namespace Voxelforge.ElectricPropulsion.IO;

/// <summary>
/// Schema-version registry for electric-propulsion design + conditions
/// JSON persistence. See
/// <c>Voxelforge/docs/family-allocations.md</c> §3 for the per-pillar
/// schema-version index.
/// </summary>
internal static class ElectricPropulsionSchemaVersion
{
    /// <summary>Currently-emitted schema version.</summary>
    internal const string Current = "v10";

    /// <summary>
    /// All accepted schema versions on read.
    ///   v1 — Wave-1 (Resistojet only).
    ///   v2 — Wave-2 HET (identity migration — 8 new init-only HET fields
    ///        on <see cref="ElectricPropulsionEngineDesign"/> default to NaN /
    ///        <c>None</c> and round-trip Resistojet designs unchanged).
    ///   v3 — Wave-2 Arcjet (identity migration — 5 new init-only Arcjet
    ///        fields default to NaN / <c>None</c> and round-trip Resistojet
    ///        and HET designs unchanged).
    ///   v4 — Wave-2 PPT (identity migration — 6 new init-only PPT fields
    ///        default to NaN and round-trip Resistojet / HET / Arcjet
    ///        designs unchanged).
    ///   v5 — Wave-2 GIT / Gridded-Ion Thruster (identity migration — 6 new
    ///        init-only GIT fields default to NaN and round-trip Resistojet /
    ///        HET / Arcjet / PPT designs unchanged).
    ///   v6 — Wave-2 MPD / Magnetoplasmadynamic Thruster (identity migration —
    ///        4 new init-only MPD numeric fields + 1 new MpdCathodeMaterial
    ///        enum default to NaN / <c>None</c> and round-trip Resistojet /
    ///        HET / Arcjet / PPT / GIT designs unchanged).
    ///   v7 — Wave-3 Applied-Field MPD (Sprint EP.W3.AF; identity migration —
    ///        2 new init-only fields <c>MpdAppliedFieldStrength_T</c> +
    ///        <c>MpdAppliedFieldCouplingOverride</c> default to NaN and
    ///        round-trip self-field MPD / Resistojet / HET / Arcjet / PPT /
    ///        GIT designs unchanged; the model surface picks up zero
    ///        applied-field augmentation when B is NaN/0).
    ///   v8 — Wave-3 VASIMR scaffold (Sprint EP.W4 phase 1; identity
    ///        migration — 5 new init-only VASIMR design fields
    ///        (<c>VasimrHeliconRfPower_W</c>, <c>VasimrIcrhRfPower_W</c>,
    ///        <c>VasimrSolenoidField_T</c>, <c>VasimrNozzleExitRadius_mm</c>,
    ///        <c>VasimrArgonMassFlow_kgs</c>) default to NaN and round-trip
    ///        all prior kinds unchanged. The physics dispatch for
    ///        Kind=Vasimr still throws NotImplementedException pending
    ///        EP.W4 phase 2 (the helicon + ICRH + magnetic-nozzle solver).
    ///   v9 — Wave-3 FEEP scaffold (Sprint EP.W5 phase 1; identity
    ///        migration — 3 new init-only numeric FEEP design fields
    ///        (<c>FeepAcceleratingVoltage_V</c>, <c>FeepBeamCurrent_A</c>,
    ///        <c>FeepEmitterTipRadius_mm</c>) + 1 new FeepPropellant enum
    ///        default to NaN / <c>None</c> and round-trip all prior kinds
    ///        unchanged. The physics dispatch for Kind=Feep still throws
    ///        NotImplementedException pending EP.W5 phase 2 (the Mair-
    ///        Lozano emitter-model + space-charge limited beam solver).
    ///   v10 — Wave-3 HDLT scaffold (Sprint EP.W6 phase 1; identity
    ///        migration — 4 new init-only numeric HDLT design fields
    ///        (<c>HdltHeliconRfPower_W</c>, <c>HdltMagneticFieldGradient_TpM</c>,
    ///        <c>HdltChannelLength_mm</c>, <c>HdltArgonMassFlow_kgs</c>)
    ///        default to NaN and round-trip all prior kinds unchanged.
    ///        The physics dispatch for Kind=Hdlt still throws
    ///        NotImplementedException pending EP.W6 phase 2 (helicon +
    ///        current-free double-layer solver).
    /// </summary>
    internal static readonly string[] Known = { "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9", "v10" };

    /// <summary>True iff <paramref name="version"/> is in <see cref="Known"/>.</summary>
    internal static bool IsSupported(string version) => Array.IndexOf(Known, version) >= 0;
}
