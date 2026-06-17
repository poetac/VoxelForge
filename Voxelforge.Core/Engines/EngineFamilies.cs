// EngineFamilies.cs — canonical family-discriminator strings.
//
// Centralizes the magic strings used by `IEngineDesign.Family` /
// `IEngineConditions.Family` / `IEngine.Family` so a typo can't put a
// design and an engine in disagreement at runtime. New families add a
// constant here as they ship.

namespace Voxelforge.Engines;

/// <summary>
/// Canonical engine-family discriminator strings. Each engine family
/// gets one constant; sub-classifications (rocket-regen vs aerospike,
/// ramjet vs turbojet) live inside the family's design record (e.g.
/// <c>ChannelTopology</c>, <c>AirbreathingEngineKind</c>) so the IEngine
/// dispatcher only needs to pick the correct family-level orchestrator.
/// </summary>
public static class EngineFamilies
{
    /// <summary>Liquid-bipropellant rocket. Covers regen-cooled bell + aerospike topologies.</summary>
    public const string Rocket = "rocket";

    /// <summary>Air-breathing engines. Covers ramjet, turbojet, turbofan, scramjet, RBCC.</summary>
    public const string Airbreathing = "airbreathing";

    /// <summary>
    /// Electric propulsion. Wave-1 covers resistojet (electrothermal); Wave-2
    /// extends to HET / MPD / gridded ion / arcjet, gated by the Team-P
    /// plasma-state audit per <see href="../../docs/ADR/ADR-026-multi-pillar-coordination.md">ADR-026 §6</see>.
    /// </summary>
    public const string ElectricPropulsion = "electric";

    /// <summary>Marine vehicles. Covers AUV mid-body (M1), displacement hulls (M4-M5, Wave 2+).</summary>
    public const string Marine = "marine";

    /// <summary>
    /// Nuclear thermal rockets. Wave-1 covers NERVA-class solid-core NTR (LH2 propellant).
    /// Wave-2+ extends to bimodal NTR and Project Pluto nuclear ramjet.
    /// </summary>
    public const string Nuclear = "nuclear";
}
