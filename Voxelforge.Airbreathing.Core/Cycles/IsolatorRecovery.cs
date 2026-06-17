// IsolatorRecovery.cs — pseudo-shock-train stagnation pressure recovery
// for a scramjet constant-area isolator.
//
// In a dual-mode scramjet the isolator sits between the inlet throat
// (station 2) and the combustor entrance (station 3). When combustor
// back-pressure rises, a pseudo-shock train forms in the isolator that
// can unstart the inlet. The recovery factor π_iso = P_t3 / P_t2
// captures the total-pressure loss through this shock train.
//
// Reference: Mattingly, *Elements of Gas Turbine Propulsion* §17.4,
// empirical pseudo-shock correlation (Eq. 17.32 vicinity). The
// pseudo-shock loss scales with the square of the local Mach excess
// above 1:
//   π_iso(M) = 1 − 0.015 × (M² − 1)
// clamped to [IsolatorRecoveryFloor, 1.0].
//
// Spot-check against literature:
//   M = 1.0 → π = 1.00 (no shock, no loss)
//   M = 2.0 → π = 0.955  (literature range: 0.90 – 0.97)
//   M = 3.0 → π = 0.880  (literature range: 0.80 – 0.93)
//   M = 4.0 → π = 0.775  (literature range: 0.70 – 0.85)
//
// The correlation is valid for M_isolator ∈ [1, 5]. Below M = 1 the
// isolator carries subsonic flow and π_iso = 1 by convention. The
// method throws for M < 1 because the ScramjetCycleSolver always
// ensures supersonic flow at station 2; receiving subsonic Mach
// indicates a coding error in the caller.

using System;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Pseudo-shock-train stagnation pressure recovery for a scramjet
/// constant-area isolator (Mattingly §17.4).
/// </summary>
public static class IsolatorRecovery
{
    /// <summary>
    /// Hard floor for the isolator recovery factor. Below this value
    /// the pseudo-shock train spans the full isolator length and the
    /// inlet is considered unstarted. Fires the
    /// <c>ISOLATOR_UNSTART</c> feasibility gate.
    /// </summary>
    public const double IsolatorRecoveryFloor = 0.30;

    /// <summary>
    /// Stagnation pressure recovery factor π_iso = P_t3 / P_t2 for
    /// the supplied isolator-inlet Mach number (Mattingly §17.4
    /// empirical pseudo-shock fit).
    /// </summary>
    /// <param name="isolatorInletMach">
    /// Mach number at the isolator inlet (station 2). Must be ≥ 1.
    /// </param>
    /// <returns>
    /// Recovery factor in [<see cref="IsolatorRecoveryFloor"/>, 1.0].
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="isolatorInletMach"/> is &lt; 1.
    /// </exception>
    public static double Pi_iso(double isolatorInletMach)
    {
        if (double.IsNaN(isolatorInletMach) || isolatorInletMach < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(isolatorInletMach),
                $"Isolator-inlet Mach {isolatorInletMach:F3} must be ≥ 1 (supersonic "
              + "flow required; subsonic flow cannot sustain a pseudo-shock train).");
        }

        double raw = 1.0 - 0.015 * (isolatorInletMach * isolatorInletMach - 1.0);
        return Math.Max(IsolatorRecoveryFloor, Math.Min(1.0, raw));
    }
}
