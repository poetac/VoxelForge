// ScramjetInletRecovery.cs — oblique-shock inlet stagnation pressure
// recovery for hypersonic (scramjet) inlets.
//
// Companion to InletRecovery.cs, which covers M ≤ 5 (MIL-STD-5007D
// subsonic + supersonic range). That file hard-throws for M > 5;
// this file handles the scramjet-relevant hypersonic domain M ∈ [4, 15].
//
// Physics reference: Mattingly, *Elements of Gas Turbine Propulsion*
// §17.2, Table 17.1 — multi-shock oblique-shock inlet total-pressure
// recovery for a 3-ramp inlet system. The recovery drops steeply with
// Mach because each oblique shock carries increasing entropy loss.
//
// Recovery formula (fit to Mattingly §17.2 Table 17.1 mid-band):
//   π_d_max(M) = exp(−0.27 × (M − 4)^0.65)
//   π_d = MechanicalEfficiency × π_d_max
//
// Closed-form values at MechanicalEfficiency = 0.90 (issue #593 —
// the docstring values now match what Pi_d() actually returns):
//
//   M  =  4 → π_d ≈ 0.90 (subsonic-diffuser equivalent at Mach 4 entry)
//   M  =  6 → π_d ≈ 0.59
//   M  =  8 → π_d ≈ 0.46
//   M  = 10 → π_d ≈ 0.38
//   M  = 12 → π_d ≈ 0.32
//   M  = 15 → π_d ≈ 0.25
//
// These sit on the lower end of the literature band for 3-ramp
// variable-geometry inlets (Anderson, Bertin & Cummings, Heiser &
// Pratt typically quote 0.55–0.75 at M = 6, 0.30–0.50 at M = 12 for
// well-tuned ramp geometries). Real designs vary ±10–15 % depending
// on ramp angles + bleed-off schemes; this fit is appropriate for
// preliminary-design margin work where the conservative-recovery
// posture is more useful than chasing the peak of the literature band.
//
// Combustor-inlet Mach after oblique-shock compression
// (ScramjetInletRecovery.CombustorInletMach):
//   3-shock ramp system compresses M_∞ down to roughly M × 0.35,
//   floored at 1.8 to ensure the combustor sees supersonic inflow.
//   This is a representative first-order estimate; real designs vary
//   with ramp geometry. Used to seed the Rayleigh-flow combustor march.

using System;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Stagnation pressure recovery and combustor-inlet Mach estimate for
/// a hypersonic multi-ramp oblique-shock inlet (Mattingly §17.2).
/// Valid for freestream Mach ∈ [4, 15].
/// </summary>
public static class ScramjetInletRecovery
{
    /// <summary>
    /// Mechanical-loss multiplier applied on top of the oblique-shock
    /// recovery. Accounts for boundary-layer + inlet-bleed + spillage
    /// losses. 0.90 is the standard preliminary-design value for a
    /// 3-ramp variable-geometry hypersonic inlet.
    /// </summary>
    public const double MechanicalEfficiency = 0.90;

    /// <summary>
    /// Minimum supported freestream Mach. Below this the MIL-STD-5007D
    /// curve in <see cref="InletRecovery.Pi_d"/> applies.
    /// </summary>
    public const double MinMach = 4.0;

    /// <summary>
    /// Maximum supported freestream Mach.
    /// </summary>
    public const double MaxMach = 15.0;

    /// <summary>
    /// Stagnation pressure recovery π_d = P_t2 / P_t0 for the supplied
    /// hypersonic freestream Mach (Mattingly §17.2 multi-shock oblique-
    /// shock inlet).
    /// </summary>
    /// <param name="freestreamMach">Freestream Mach number.</param>
    /// <returns>Recovery factor in (0, 1].</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="freestreamMach"/> is outside
    /// [<see cref="MinMach"/>, <see cref="MaxMach"/>].
    /// </exception>
    public static double Pi_d(double freestreamMach)
    {
        if (double.IsNaN(freestreamMach)
            || freestreamMach < MinMach
            || freestreamMach > MaxMach)
        {
            throw new ArgumentOutOfRangeException(nameof(freestreamMach),
                $"Freestream Mach {freestreamMach:F2} outside the scramjet inlet "
              + $"domain [{MinMach}, {MaxMach}]. For M ≤ 5 use InletRecovery.Pi_d.");
        }

        double piDMax = Math.Exp(-0.27 * Math.Pow(freestreamMach - 4.0, 0.65));
        return MechanicalEfficiency * piDMax;
    }

    /// <summary>
    /// Estimated combustor-inlet Mach number after the multi-ramp
    /// oblique-shock compression system. Floored at 1.8 to guarantee
    /// supersonic inflow to the isolator.
    /// </summary>
    /// <param name="freestreamMach">Freestream Mach number.</param>
    /// <returns>Mach number at the combustor face (station 2).</returns>
    public static double CombustorInletMach(double freestreamMach)
    {
        const double CompressionFraction = 0.35;
        const double FloorMach = 1.8;
        return Math.Max(FloorMach, freestreamMach * CompressionFraction);
    }
}
