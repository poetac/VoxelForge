// RefrigerationMode.cs — Sprint RFG.W1 cycle-direction discriminator.
//
// The same hardware (compressor + condenser + expansion valve +
// evaporator) operates as a refrigeration cycle (useful output =
// Q_cold) or a heat pump (useful output = Q_hot) — they differ only
// in which side is "free" and which is the figure of merit.

namespace Voxelforge.Refrigeration;

/// <summary>
/// Vapor-compression cycle mode (Sprint RFG.W1).
/// </summary>
internal enum RefrigerationMode
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Refrigeration / air-conditioning mode — useful output is heat
    /// extraction from the cold reservoir (Q_cold). Figure of merit:
    /// COP_cooling = Q_cold / W_compressor.
    /// </summary>
    Cooling = 1,

    /// <summary>
    /// Heat-pump mode — useful output is heat delivery to the hot
    /// reservoir (Q_hot). Figure of merit: COP_heating = Q_hot /
    /// W_compressor (always = COP_cooling + 1 by energy balance).
    /// </summary>
    Heating = 2,
}
