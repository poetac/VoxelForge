// CompressorKind.cs — Sprint CMP.W1 compressor sub-classifier.
//
// Wave-1 ships the centrifugal compressor — the workhorse of small
// gas turbines, turbochargers, industrial compressed-air systems,
// HVAC chillers, and refrigeration cycles. Wave-2+ will add axial-flow
// (large-Pratio multi-stage), reciprocating (positive-displacement),
// screw / scroll / vane (industrial low-flow), and Roots-blower
// (high-flow low-Pratio).

namespace Voxelforge.Compressor;

/// <summary>
/// Sub-classification of compressor (Sprint CMP.W1).
/// </summary>
internal enum CompressorKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Centrifugal (radial-outflow) compressor. Single-stage Pratios
    /// typically 2-5 for industrial air, up to ~ 6 for aero turbochargers
    /// (Garrett GT35-class).
    /// </summary>
    Centrifugal = 1,

    /// <summary>
    /// Axial-flow compressor (Sprint CMP.W2). Multi-stage with low
    /// per-stage Pratio (~ 1.2-1.4) but very high overall (10-50+) via
    /// stage chaining. Workhorse of large turbofans (PW JT9D ~ 25:1)
    /// + ground-based gas turbines (GE LM2500 ~ 18:1). Higher η than
    /// centrifugal at large π_c.
    /// </summary>
    AxialFlow = 2,
}
