// PumpKind.cs — Sprint PMP.W1 pump topology discriminator.
//
// Wave-1 ships the centrifugal pump — the dominant industrial +
// rocket-turbopump topology. Wave-2+ will add positive-displacement
// (gear, screw, reciprocating-plunger), axial-flow (low-head high-Q),
// and multi-stage centrifugal (high-head boiler-feed).

namespace Voxelforge.Pump;

/// <summary>
/// Sub-classification of pump (Sprint PMP.W1).
/// </summary>
internal enum PumpKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Single-stage centrifugal pump. Industrial process workhorse
    /// (Goulds 3196, Sulzer OHH-class) + rocket-turbopump LOX/RP-1
    /// impeller geometry (Merlin / Raptor LOX side).
    /// </summary>
    Centrifugal = 1,

    /// <summary>
    /// Positive-displacement pump (Sprint PMP.W2) — gear, screw, or
    /// reciprocating-piston. Flow is constant per revolution (drives
    /// flow rate via geometric displacement × N); head is set by
    /// downstream resistance. Inverse hydrodynamics from centrifugal:
    /// changing N changes Q linearly but not H. No NPSH cavitation
    /// envelope (positive-displacement pumps self-prime).
    /// </summary>
    PositiveDisplacement = 2,
}
