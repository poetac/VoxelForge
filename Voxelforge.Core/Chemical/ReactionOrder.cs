// ReactionOrder.cs — Sprint CHM.W2 reaction-order discriminator.
//
// Wave-2 generalises CHM.W1's first-order-only model to also handle
// second-order reactions (A + B → C with rate r = k · C_A · C_B, often
// dimer formation or hydrolysis-like). The reaction-order discriminator
// is an init-only enum on ReactorDesign that defaults to First (the
// CHM.W1 baseline) for backwards-compat bit-identity.

namespace Voxelforge.Chemical;

/// <summary>
/// Kinetic reaction order (Sprint CHM.W2).
/// </summary>
internal enum ReactionOrder
{
    /// <summary>
    /// First-order: r = k · C_A. CHM.W1 default; preserves Wave-1
    /// bit-identity when this enum's default value is read.
    /// </summary>
    First = 0,

    /// <summary>
    /// Second-order in A: r = k · C_A². Captures the bimolecular case
    /// 2 A → B common in dimerization + catalyst-saturated kinetics.
    /// </summary>
    SecondInA = 1,
}
