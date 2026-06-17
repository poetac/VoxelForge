// ReactorKind.cs — Sprint CHM.W1 chemical-reactor topology discriminator.
//
// Wave-1 ships the two canonical ideal-reactor models that every
// process-engineering textbook builds on:
//
//   CSTR (Continuous Stirred-Tank Reactor) — perfectly mixed; exit
//        concentration equals tank concentration. Conversion at fixed
//        residence time is LOWER than a PFR (mixing dilutes feed into
//        partially-reacted material).
//   PFR (Plug-Flow Reactor) — no axial mixing; concentration drops
//       monotonically along the length. Higher conversion than CSTR at
//       the same τ for positive-order reactions.
//
// Wave-2+ will add Batch (transient), PBR (packed-bed catalysis),
// fluidized-bed, and CSTR-in-series cascade topologies.

namespace Voxelforge.Chemical;

/// <summary>
/// Ideal chemical-reactor topology (Sprint CHM.W1).
/// </summary>
internal enum ReactorKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Continuous Stirred-Tank Reactor (CSTR) — perfectly mixed.</summary>
    Cstr = 1,

    /// <summary>Plug-Flow Reactor (PFR) — no axial mixing.</summary>
    Pfr = 2,

    /// <summary>
    /// Batch reactor — Wave-2 (Sprint CHM.W2). Transient, closed system;
    /// reaction proceeds until reactants depleted. Conversion is a
    /// function of <i>elapsed time</i> rather than residence time:
    ///   1st-order: X(t) = 1 − exp(−k·t)
    ///   2nd-order: X(t) = k·C_A0·t / (1 + k·C_A0·t)
    /// </summary>
    Batch = 3,
}
