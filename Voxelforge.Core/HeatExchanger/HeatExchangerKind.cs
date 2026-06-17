// HeatExchangerKind.cs — Sprint HX.W1 heat-exchanger sub-classifier.
//
// Wave-1 ships the printed plate-fin counterflow geometry — the
// LPBF-natural workhorse topology. The regen-jacket (rocket), pre-
// cooler (LACE), recuperator (closed-cycle Brayton), and condenser /
// evaporator (Rankine) sprints all use special cases of this same
// geometry; the HX pillar consolidates them under one solver to
// remove the per-pillar one-offs.
//
// Wave-2+ will add cross-flow, microchannel, shell-and-tube, plate-
// and-frame, and dual-pressure / spiral-wound geometries.

namespace Voxelforge.HeatExchanger;

/// <summary>
/// Sub-classification of heat exchanger within the heat-exchanger
/// pillar (Sprint HX.W1 scaffold). Wave-1 ships counterflow plate-fin
/// only.
/// </summary>
internal enum HeatExchangerKind
{
    /// <summary>Degenerate sentinel — not a valid design kind.</summary>
    None = 0,

    /// <summary>
    /// Counterflow plate-fin heat exchanger. LPBF-natural workhorse
    /// topology. Sized by the ε-NTU method with Kays-London offset-strip
    /// fin j-factor + f-factor cluster correlations on both sides.
    /// </summary>
    PlateFinCounterflow = 1,
}
