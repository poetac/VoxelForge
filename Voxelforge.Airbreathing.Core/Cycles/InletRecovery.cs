// InletRecovery.cs — air-breathing inlet stagnation pressure recovery.
//
// Subsonic + supersonic recovery model. Captures the ram-compression
// + (in supersonic) shock-train losses through the inlet. Subsonic
// regime is well-modeled by a single recovery factor (~0.97 typical).
// Supersonic regime uses the MIL-STD-5007D (now MIL-STD-5007E)
// recovery curve, the default reference for preliminary design.
//
// References
// ----------
//   - MIL-STD-5007D (now -E), "Engine, Aircraft, Turbojet and
//     Turbofan, General Specification for", §3.7.5.1 inlet pressure
//     recovery.
//   - Mattingly *Elements of Propulsion: Gas Turbines and Rockets*,
//     AIAA 2006, §6.5.4 ("Subsonic and Supersonic Inlets").

using System;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Closed-form stagnation pressure recovery for an air-breathing
/// inlet. Returns the ratio P_t2 / P_t0 — the fraction of freestream
/// stagnation pressure that survives ingestion through the inlet.
/// </summary>
public static class InletRecovery
{
    /// <summary>
    /// Default subsonic recovery factor. Production aircraft data
    /// clusters around 0.95-0.99; 0.97 is the standard preliminary-
    /// design value (Mattingly §6.5.4).
    /// </summary>
    public const double SubsonicRecoveryFactor = 0.97;

    /// <summary>
    /// Mechanical-loss multiplier applied on top of the MIL-STD-5007D
    /// shock-train recovery curve. Accounts for boundary-layer +
    /// inlet-vane losses not captured by the inviscid shock model.
    /// Production data clusters around 0.93-0.97; 0.95 is the
    /// preliminary-design value.
    /// </summary>
    public const double SupersonicMechanicalEfficiency = 0.95;

    /// <summary>
    /// Stagnation pressure recovery factor π_d = P_t2 / P_t0 for the
    /// supplied freestream Mach number.
    /// </summary>
    /// <param name="freestreamMach">Freestream Mach number.</param>
    /// <returns>Recovery factor in (0, 1].</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="freestreamMach"/> &lt; 0 or &gt; 5
    /// (the MIL-STD curve domain). Hypersonic inlets (M &gt; 5) need
    /// scramjet-specific physics that ships in sub-step 1d.
    /// </exception>
    public static double Pi_d(double freestreamMach)
    {
        if (double.IsNaN(freestreamMach) || freestreamMach < 0.0 || freestreamMach > 5.0)
            throw new ArgumentOutOfRangeException(nameof(freestreamMach),
                $"Freestream Mach {freestreamMach} outside the supported [0, 5] range. "
              + "Hypersonic inlets land in the scramjet sub-step (1d).");

        if (freestreamMach < 1.0)
            return SubsonicRecoveryFactor;

        // MIL-STD-5007D supersonic shock-train recovery:
        //   π_d_max(M) = 1 − 0.075 · (M − 1)^1.35     for 1 ≤ M ≤ 5
        // Multiply by mechanical-loss factor.
        double piDMax = 1.0 - 0.075 * Math.Pow(freestreamMach - 1.0, 1.35);
        return SupersonicMechanicalEfficiency * piDMax;
    }
}
