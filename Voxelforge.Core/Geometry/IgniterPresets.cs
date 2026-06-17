// IgniterPresets.cs — Igniter hardware selection + ignition-energy budgeting.
//
// Four presets cover the common first-fire styles:
//   • None                  — no igniter geometry, no energy check
//   • SparkTorch            — gas-gas torch with small spark plug
//   • AugmentedSpark        — high-energy plug in an augmenter chamber
//   • PyrotechnicCartridge  — one-shot pyro charge
//
// Each preset carries the cavity geometry (bore + depth + feed-bore
// diameter when applicable) and an ignition-energy rating in joules.
// The feasibility gate `IGNITER_ENERGY_INSUFFICIENT` compares the rating
// against the per-pair floor (LOX/HC spark-discharge JANNAF 0.050–0.200 J;
// LOX/RP-1 deployed-pyro / TEA-TEB chemical-authority floor 500 J).
//
// Pre-PH-12 the field was named `IgnitionEnergy_mJ` and values straddled
// two physical regimes (spark-discharge stored capacitor energy = mJ;
// pyrotechnic chemical authority = J–kJ). PH-12 (2026-04-29) renames to
// `IgnitionEnergy_J` and rescales pyrotechnic + LOX/RP-1 floor values to
// match Huzel & Huang §7.2 / NASA SP-8051 / F-1 / Merlin / RS-27 deployed
// hardware (kJ-class). Spark / augmented-spark stored-capacitor energies
// remain in their physically-correct ranges (0.150 J / 5 J).
//
// References:
//   JANNAF: Solid Rocket Motor Propellant Ignition Qualification.
//   Huzel & Huang AIAA Vol. 147 §7 (ignition systems).
//   NASA SP-8051 Solid Rocket Motor Igniters §4.2.

namespace Voxelforge.Geometry;

public enum IgniterType
{
    None = 0,
    SparkTorch,
    AugmentedSpark,
    PyrotechnicCartridge,
}

/// <summary>
/// Geometry + energy spec for one igniter preset. Cavity is a simple
/// cylindrical bore drilled through the injector flange; FeedBore is
/// an optional perpendicular bore for a torch fuel / ox feed line.
/// IgnitionEnergy_J is the preset's rated spark / combustion energy in
/// joules. Spark-class presets use stored capacitor energy (mJ-scale,
/// expressed as a fractional joule); pyrotechnic presets use chemical
/// authority (J–kJ scale).
/// </summary>
public readonly record struct IgniterSpec(
    IgniterType Id,
    string DisplayName,
    double CavityDiameter_mm,
    double CavityDepth_mm,
    double FeedBoreDiameter_mm,   // 0 = no feed bore
    double IgnitionEnergy_J);

public static class IgniterPresets
{
    /// <summary>JANNAF lower bound on spark-discharge ignition-energy (J) for LOX/HC propellants.</summary>
    public const double JANNAFMin_J = 0.050;
    /// <summary>JANNAF upper bound on spark-discharge ignition-energy recommendation (J) for LOX/HC.</summary>
    public const double JANNAFMax_J = 0.200;

    public static readonly System.Collections.Generic.Dictionary<IgniterType, IgniterSpec> All =
        new()
        {
            [IgniterType.None] = new(
                IgniterType.None, "(none)",
                CavityDiameter_mm: 0, CavityDepth_mm: 0,
                FeedBoreDiameter_mm: 0, IgnitionEnergy_J: 0),

            [IgniterType.SparkTorch] = new(
                IgniterType.SparkTorch, "Spark torch (GOX/GCH4)",
                CavityDiameter_mm: 8.0, CavityDepth_mm: 18.0,
                FeedBoreDiameter_mm: 2.5, IgnitionEnergy_J: 0.150),

            [IgniterType.AugmentedSpark] = new(
                IgniterType.AugmentedSpark, "Augmented spark plug",
                CavityDiameter_mm: 12.0, CavityDepth_mm: 22.0,
                FeedBoreDiameter_mm: 3.0, IgnitionEnergy_J: 5.0),

            [IgniterType.PyrotechnicCartridge] = new(
                IgniterType.PyrotechnicCartridge, "Pyro cartridge (one-shot)",
                CavityDiameter_mm: 10.0, CavityDepth_mm: 25.0,
                FeedBoreDiameter_mm: 0, IgnitionEnergy_J: 1000.0),
        };

    public static IgniterSpec SpecFor(IgniterType t) => All[t];

    /// <summary>
    /// True iff the preset's rated energy clears the JANNAF spark-discharge
    /// lower bound. None always returns true — no igniter means no geometry
    /// to gate against; the user accepts responsibility.
    /// </summary>
    public static bool MeetsMinimumEnergy(IgniterType t)
    {
        if (t == IgniterType.None) return true;
        return All[t].IgnitionEnergy_J >= JANNAFMin_J;
    }
}
