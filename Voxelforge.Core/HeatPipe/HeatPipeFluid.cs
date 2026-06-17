// HeatPipeFluid.cs — Sprint HP.W1 working-fluid discriminator.
//
// Wave-1 ships three commercial working-fluid clusters spanning the
// 0-1500 °C envelope:
//
//   Water   (10 – 200 °C):    Cu-water heat pipes — laptop / GPU cooling,
//                              CPU heat sinks, spacecraft thermal bus.
//   Sodium  (400 – 800 °C):   Na-stainless heat pipes — high-T process
//                              heat, nuclear reactor decay-heat removal.
//   Lithium (1000 – 1500 °C): Li-tungsten heat pipes — space nuclear
//                              reactor primary loop (SAFE-400, KRUSTY),
//                              fusion divertor candidate.
//
// Wave-2+ will add ammonia (cryo + spacecraft), methanol (medium-T),
// and mercury / Dowtherm (specialty).

namespace Voxelforge.HeatPipe;

/// <summary>
/// Heat-pipe working-fluid choice (Sprint HP.W1).
/// </summary>
internal enum HeatPipeFluid
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Water — 10 – 200 °C, Cu-water cluster.</summary>
    Water = 1,

    /// <summary>Sodium — 400 – 800 °C, Na-stainless cluster.</summary>
    Sodium = 2,

    /// <summary>Lithium — 1000 – 1500 °C, Li-tungsten cluster.</summary>
    Lithium = 3,
}
