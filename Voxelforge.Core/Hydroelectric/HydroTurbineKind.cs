// HydroTurbineKind.cs — Sprint HE.W1 hydroelectric turbine sub-classifier.
//
// Wave-1 ships the three dominant commercial topologies, each adapted
// to a head + flow-rate envelope:
//
//   Pelton (impulse):    H ∈ [200, 2000] m, low Q → small high-speed jet
//                        impinges buckets. Examples: Bieudron 1.9 GW,
//                        Cleuson-Dixence 1.8 GW (highest-head Pelton on
//                        Earth at 1869 m).
//   Francis (reaction):  H ∈ [10, 700] m, medium Q. Workhorse of large-
//                        scale hydro. Examples: Three Gorges 700 MW
//                        Francis units; Itaipu 700 MW Francis units.
//   Kaplan (axial-flow): H ∈ [2, 40] m, high Q. Run-of-river + tidal
//                        barrage. Examples: Sayano-Shushenskaya, Bonneville.
//
// Wave-2+ will add bulb (low-head tidal), Turgo (high-head impulse
// alternative), and pump-turbine (reversible Francis for pumped-storage).

namespace Voxelforge.Hydroelectric;

/// <summary>
/// Sub-classification of hydroelectric turbine (Sprint HE.W1).
/// </summary>
internal enum HydroTurbineKind
{
    /// <summary>Degenerate sentinel — not a valid design choice.</summary>
    None = 0,

    /// <summary>
    /// Pelton (impulse) — high-head + low-flow envelope. High-velocity
    /// water jets strike spoon-shaped buckets on a runner periphery.
    /// </summary>
    Pelton = 1,

    /// <summary>
    /// Francis (reaction) — medium-head + medium-flow envelope. Water
    /// enters radially through guide vanes, accelerates through a
    /// shaped runner, exits axially. The dominant utility-scale topology.
    /// </summary>
    Francis = 2,

    /// <summary>
    /// Kaplan (axial-flow propeller, often with adjustable blades) —
    /// low-head + high-flow envelope. Run-of-river + tidal-barrage
    /// installations.
    /// </summary>
    Kaplan = 3,
}
