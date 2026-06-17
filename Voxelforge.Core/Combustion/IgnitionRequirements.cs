// IgnitionRequirements.cs — Sprint 29 (2026-04-24):
// Per-propellant-pair ignition-energy + recommended-modality table.
// Third hot-fire-readiness item.
//
// Why this module exists
// ──────────────────────
// Pre-Sprint-29 the IGNITER_ENERGY_INSUFFICIENT gate used a single
// universal 50 mJ JANNAF floor regardless of propellant pair. In
// reality:
//   • LOX/H2 needs ~0.1–1 mJ (lowest-MW fuel, wide flammability limits).
//   • LOX/CH4 needs 10–50 mJ (~JANNAF floor works here).
//   • LOX/RP-1 needs ≥500 J of deployed chemical authority (pyrotechnic
//     cartridge or TEA-TEB hypergolic slug) for reliable cold start
//     (Huzel & Huang §7.2; NASA SP-8051 §4.2; F-1, Merlin, RS-27).
//   • N2O4/MMH is hypergolic — zero external ignition energy needed.
//   • H2O2/RP-1 uses catalyst decomposition — also no spark igniter.
//
// And modality suitability is a separate axis: even if an AugmentedSpark's
// rated capacitor energy approached the LOX/RP-1 floor, field practice
// is to require AugmentedSpark+ on kerosene engines because the spark-
// torch pilot flame is unreliable in RP-1's atomisation regime (Huzel &
// Huang §7.2). This module captures both: a minimum energy AND a minimum
// acceptable modality per pair.
//
// PH-12 (2026-04-29): MinEnergy units migrated from mJ to J. LOX/HC
// spark-discharge floors stay numerically the same in physical magnitude
// (0.050 J / 0.005 J) but LOX/RP-1 is bumped from 500 mJ → 500 J to
// match deployed-pyro/TEA-TEB chemical authority. Pyrotechnic preset
// rating likewise rescaled. Spark-class preset capacitor energies
// (0.150 J / 5.0 J) remain in their physically-correct ranges.
//
// PH-29 (2026-04-29): unknown PropellantPair throws ArgumentOutOfRangeException
// instead of returning the permissive (JANNAF floor, SparkTorch) default,
// so a future pair added to the enum cannot silently inherit unsafe
// defaults — the contributor must register an explicit IgnitionRequirement.
//
// API
// ───
//   IgnitionRequirements.For(pair) → IgnitionRequirement
//   IgnitionRequirement.IsHypergolic      — no igniter needed
//   IgnitionRequirement.MinEnergy_J       — energy floor (J) for the gate
//   IgnitionRequirement.MinModality       — lowest acceptable IgniterType
//     (ordinal: SparkTorch < AugmentedSpark < PyrotechnicCartridge)
//   IgnitionRequirement.Notes             — UI / gate-description help
//
// References: Huzel & Huang AIAA Vol. 147 §7; NASA SP-8051 §4.2;
// JANNAF Solid Rocket Motor Propellant Ignition Qualification.

using System;
using Voxelforge.Geometry;

namespace Voxelforge.Combustion;

/// <summary>
/// Sprint 29 (2026-04-24): per-pair ignition requirements consumed by
/// the <c>IGNITER_ENERGY_INSUFFICIENT</c>, <c>IGNITER_MISSING</c>, and
/// <c>IGNITER_MODALITY_UNSUITABLE</c> feasibility gates.
/// </summary>
/// <param name="Pair">Propellant pair this requirement applies to.</param>
/// <param name="IsHypergolic">True when the pair self-ignites on contact
/// (N2O4/MMH) or ignites via catalyst decomposition (H2O2/RP-1) so no
/// external spark / pyro is required. Gate silence: when true, every
/// igniter-energy / modality check short-circuits.</param>
/// <param name="MinEnergy_J">Minimum ignition energy (joules) the selected
/// igniter preset must match or exceed. 0 on hypergolic pairs. LOX/HC
/// spark floors stay sub-joule (0.050 / 0.005); LOX/RP-1 deployed-pyro
/// floor is 500 J (Huzel &amp; Huang §7.2). PH-12 (2026-04-29).</param>
/// <param name="MinModality">Lowest acceptable
/// <see cref="IgniterType"/>. <see cref="IgniterType.None"/> is only
/// acceptable on hypergolic pairs.</param>
/// <param name="Notes">One-line remediation hint surfaced in gate
/// descriptions (e.g., "Augmented-spark or pyro recommended — plain
/// spark torches are marginal on kerosene atomisation").</param>
public readonly record struct IgnitionRequirement(
    PropellantPair Pair,
    bool           IsHypergolic,
    double         MinEnergy_J,
    IgniterType    MinModality,
    string         Notes);

public static class IgnitionRequirements
{
    /// <summary>
    /// Per-propellant-pair ignition requirement. Callers should treat
    /// the result as immutable metadata.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pair"/> has no explicit registration
    /// in this lookup. PH-29 (2026-04-29): replaces the pre-existing
    /// permissive (JANNAF spark floor, SparkTorch) fallback so a future
    /// PropellantPair enum value cannot silently bypass per-pair ignition
    /// safety. Contributors adding a new pair must register an explicit
    /// case below.
    /// </exception>
    public static IgnitionRequirement For(PropellantPair pair) => pair switch
    {
        PropellantPair.LOX_CH4 => new(
            Pair:         PropellantPair.LOX_CH4,
            IsHypergolic: false,
            MinEnergy_J:  0.050,    // JANNAF LOX/HC spark-discharge floor — CH4 atomises well
            MinModality:  IgniterType.SparkTorch,
            Notes:        "Spark-torch GOX/GCH4 or higher. Pre-chill to -150 K "
                        + "before first fire; cold LOX on warm CH4 spray can "
                        + "hard-start."),

        PropellantPair.LOX_H2 => new(
            Pair:         PropellantPair.LOX_H2,
            IsHypergolic: false,
            MinEnergy_J:  0.005,    // H2 has the widest flammability limits of any
                                     // fuel; a ~0.1-1 mJ spark is physically sufficient,
                                     // but 5 mJ (= 0.005 J) gives a conservative margin
                                     // for cold / LH2 atomisation.
            MinModality:  IgniterType.SparkTorch,
            Notes:        "LH2 is the easiest fuel to light — any rated "
                        + "igniter suffices. Hydrogen-embrittlement on the "
                        + "igniter electrodes is the real design concern."),

        PropellantPair.LOX_RP1 => new(
            Pair:         PropellantPair.LOX_RP1,
            IsHypergolic: false,
            MinEnergy_J:  500.0,    // Kerosene atomisation slow; spark-class capacitor
                                     // energies (≤ 5 J) are insufficient. Deployed
                                     // pyrotechnic cartridges and TEA-TEB hypergolic
                                     // slugs deliver ≥ 500 J of chemical authority on
                                     // kerosene first-fire (F-1, Merlin, RS-27).
                                     // Huzel & Huang §7.2; NASA SP-8051 §4.2.
            MinModality:  IgniterType.AugmentedSpark,
            Notes:        "Kerosene needs an augmented-spark or pyrotechnic "
                        + "cartridge. Plain spark torches produce unreliable "
                        + "ignition on RP-1 cold start (Huzel & Huang §7.2)."),

        PropellantPair.N2O4_MMH => new(
            Pair:         PropellantPair.N2O4_MMH,
            IsHypergolic: true,
            MinEnergy_J:  0.0,
            MinModality:  IgniterType.None,
            Notes:        "Hypergolic — spontaneous ignition on contact. "
                        + "No external igniter needed; the None selection "
                        + "is correct for this pair."),

        PropellantPair.H2O2_RP1 => new(
            Pair:         PropellantPair.H2O2_RP1,
            IsHypergolic: true,     // Catalyst-decomposition start; "hypergolic-like"
                                     // in gate semantics (no spark / pyro required).
            MinEnergy_J:  0.0,
            MinModality:  IgniterType.None,
            Notes:        "Catalyst-bed decomposition of peroxide auto-lights "
                        + "the RP-1 stream (Black Arrow heritage). No spark "
                        + "igniter needed; None is correct."),

        _ => throw new ArgumentOutOfRangeException(
            nameof(pair),
            pair,
            $"PropellantPair {pair} has no IgnitionRequirements registration. " +
            $"Add an explicit case to IgnitionRequirements.For when introducing " +
            $"a new pair — pre-PH-29 a permissive (JANNAF floor, SparkTorch) " +
            $"fallback masked unsafe defaults for pairs that need stronger " +
            $"ignition (e.g. N2O4/N2H4)."),
    };

    /// <summary>
    /// Ordinal of an <see cref="IgniterType"/> for modality comparison.
    /// Higher number = stronger modality.
    /// None = 0, SparkTorch = 1, AugmentedSpark = 2, PyrotechnicCartridge = 3.
    /// </summary>
    public static int ModalityOrdinal(IgniterType t) => t switch
    {
        IgniterType.None                 => 0,
        IgniterType.SparkTorch           => 1,
        IgniterType.AugmentedSpark       => 2,
        IgniterType.PyrotechnicCartridge => 3,
        _                                => 0,
    };
}
