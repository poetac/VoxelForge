// OOB-7 (issue #343): rotating detonation engine combustion physics.
using System;

namespace Voxelforge.Combustion;

/// <summary>
/// OOB-7 (issue #343): physics helpers for rotating detonation engine (RDE) combustors.
/// Equations reference Wolański 2013 "Detonative propulsion" and CEA-calibrated gain
/// factors benchmarked against Bykovskii et al. 2006 LOX/H₂ continuous spin detonation data.
/// </summary>
public static class RdeCombustion
{
    // Chapman-Jouguet detonation cell length (used for wave-count geometry).
    // Empirical from Shepherd 1986 / Wolański 2013 Table 1 for hydrocarbon/H2 fuel-O2
    // mixtures at stoichiometric conditions and ambient pressure. Effective per-cell
    // path length for the wave structure is ~20 mm at typical RDE conditions.
    private const double _lcj_m = 0.020;

    // CEA-calibrated Isp-gain intercepts and slopes at MR ≈ stoichiometric.
    // Anchor: LOX/CH4 at Pc=2 MPa → +5 % gain (Bykovskii 2006, Kindracki 2011).
    // Slope from Pc sensitivity in CEA detonation tables (Shepherd 1986 §3).
    // Clamped to physically plausible range per available literature:
    //   LOX/CH4: [1.03, 1.08]  LOX/H2: [1.05, 1.10]  LOX/RP1: [1.02, 1.07]
    private static readonly (double intercept, double slope, double lo, double hi)[]
        _gainParams = new[]
        {
            // indexed by (int)PropellantPair: CH4=0, H2=1, RP1=2
            (1.05, 0.001,  1.03, 1.08),   // LOX_CH4
            (1.07, 0.0005, 1.05, 1.10),   // LOX_H2
            (1.04, 0.0008, 1.02, 1.07),   // LOX_RP1
        };

    /// <summary>
    /// Isp gain factor relative to deflagration combustion for the given propellant pair
    /// and chamber pressure. Returns a value ≥ 1.0 for all supported pairs.
    /// Unsupported pairs return 1.0 (no gain).
    /// </summary>
    /// <param name="pair">Propellant pair.</param>
    /// <param name="chamberPressure_Pa">Mean chamber pressure (Pa).</param>
    public static double IspGain(PropellantPair pair, double chamberPressure_Pa)
    {
        int idx = (int)pair;
        if (idx < 0 || idx >= _gainParams.Length) return 1.0;
        var (intercept, slope, lo, hi) = _gainParams[idx];
        double pc_MPa = chamberPressure_Pa / 1e6;
        double gain = intercept + slope * (pc_MPa - 2.0);
        return Math.Clamp(gain, lo, hi);
    }

    /// <summary>
    /// Estimated number of simultaneous detonation waves that can propagate
    /// around an annulus of the given circumference.
    /// Based on <c>N = round(C / (f × L_CJ))</c> per Wolański 2013 §4.
    /// Returns at least 1.
    /// </summary>
    /// <param name="annulusCircumference_m">Outer-annulus circumference (m).</param>
    /// <param name="waveSpeedFraction">
    /// Ratio of observed wave speed to Chapman-Jouguet wave speed.
    /// Typical values 0.85–0.95; default 0.90.
    /// </param>
    public static int DetonationWaveCount(
        double annulusCircumference_m, double waveSpeedFraction = 0.90)
    {
        if (waveSpeedFraction <= 0.0) waveSpeedFraction = 0.90;
        double nRaw = annulusCircumference_m / (waveSpeedFraction * _lcj_m);
        return Math.Max(1, (int)Math.Round(nRaw));
    }

    /// <summary>
    /// Annulus fill time (µs) — the time available for fresh propellant to
    /// refill the annulus channel before the next detonation wave arrives.
    /// <c>τ_fill = h / √(2 × ΔP / ρ)</c> where h is channel height, ΔP
    /// is injector pressure drop, and ρ is propellant mixture density.
    /// If fill time exceeds the inter-wave period, the channel starves
    /// (gate <c>RDE_ANNULUS_FILL_STARVED</c>).
    /// </summary>
    /// <param name="channelHeight_m">RDE annulus channel height (m).</param>
    /// <param name="injectorDp_Pa">Injector pressure drop (Pa).</param>
    /// <param name="propDensity_kgm3">Mean propellant mixture density (kg/m³).</param>
    public static double AnnulusFillTime_us(
        double channelHeight_m, double injectorDp_Pa, double propDensity_kgm3)
    {
        if (injectorDp_Pa <= 0.0 || propDensity_kgm3 <= 0.0) return double.PositiveInfinity;
        double fillVelocity_ms = Math.Sqrt(2.0 * injectorDp_Pa / propDensity_kgm3);
        if (fillVelocity_ms <= 0.0) return double.PositiveInfinity;
        return (channelHeight_m / fillVelocity_ms) * 1e6;
    }
}
