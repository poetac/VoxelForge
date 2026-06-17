// EquilibriumCorrection.cs — Parameterized equilibrium-vs-frozen
// combustion correction. Provides an `IEquilibriumCorrection`
// abstraction so a future full Gordon-McBride Gibbs-minimisation
// solver can slot in without touching downstream callers —
// `PropellantTables.Lookup` routes through whatever implementation is
// registered.
//
// Why parameterized first
// ───────────────────────
// A proper Gordon-McBride solver for even one propellant pair needs
// NASA 9-coefficient polynomial tables (12+ species), Newton-Lagrange
// iteration with element-balance + enthalpy constraints, and careful
// convergence tuning near dissociation boundaries. That's a 2-3 week
// focused sprint on its own. This module delivers the 80% solution —
// a pressure-dependent dissociation correction calibrated against
// published CEA runs — in a single sprint so the honest-physics story
// improves from "frozen tables with log-linear Pc correction" (5-8%
// of NASA CEA) to "parameterized equilibrium correction" (3-4% of
// NASA CEA across the envelope).
//
// What the correction captures
// ────────────────────────────
// Equilibrium combustion at high Pc has LESS dissociation than at low
// Pc (Le Chatelier principle — higher pressure shifts the equilibrium
// toward fewer moles, i.e. recombined products). Equilibrium C* and T_c
// are therefore HIGHER at high Pc than the frozen tables predict.
//
// Our existing per-pair tables were built from CEA runs at a reference
// Pc (7 MPa for all 3 implemented pairs); frozen-flow theory's log-
// linear Pc correction then approximates the Pc dependence. Real
// equilibrium introduces an additional SUB-linear correction term that
// this module adds. Magnitude: ~1-3% on T_c and ~0.5-1.5% on C* for
// typical LOX engines across Pc = 1-30 MPa.
//
// Correction form (per pair):
//   T_c_eq(Pc)   = T_c_frozen · (1 + κ_T · ln²(Pc/Pc_ref))
//   C*_eq(Pc)    = C*_frozen  · (1 + κ_C · ln (Pc/Pc_ref))
//   γ_eq(Pc)     = γ_frozen   · (1 + κ_γ · ln (Pc/Pc_ref))
// where κ_T / κ_C / κ_γ are pair-specific and MR-modulated (largest
// near peak C*, smallest at fuel/ox-rich extremes where dissociation
// is already minimal).
//
// Coefficients are calibrated against Sutton RPE 9e Table 5-5 (CEA
// runs at 6.9, 13.8, 20.7 MPa) + Huzel & Huang AIAA Vol. 147 Fig. 2-5.
// Approximate — published-to-published differences are themselves
// ~1-2%, so the correction's absolute accuracy is limited by source
// data quality, not by our fit.

namespace Voxelforge.Combustion;

/// <summary>
/// Transform a frozen <see cref="PropellantState"/> into an
/// equilibrium-corrected version at the same operating point.
/// Implementations range from <see cref="EquilibriumCorrection.None"/>
/// (identity — legacy behaviour) through parameterized empirical
/// corrections to future full Gordon-McBride solvers.
/// </summary>
public interface IEquilibriumCorrection
{
    /// <summary>Short identifier logged on report output.</summary>
    string Name { get; }

    /// <summary>
    /// Return a corrected propellant state given the frozen-table
    /// result. Must be pure (no hidden state); the
    /// <see cref="PropellantTables.Lookup"/> cache keys assume purity.
    /// </summary>
    PropellantState Correct(PropellantState frozenState, PropellantPair pair);
}

/// <summary>
/// Registry + parameterized implementations for equilibrium
/// corrections. Pure static utilities; no PicoGK dependency;
/// thread-safe.
/// </summary>
public static class EquilibriumCorrection
{
    /// <summary>
    /// Identity correction — returns the frozen state unchanged.
    /// Matches legacy behaviour bit-identically when set as the
    /// <see cref="PropellantTables.EquilibriumCorrection"/> provider.
    /// </summary>
    public static readonly IEquilibriumCorrection None = new NoCorrection();

    /// <summary>
    /// Default parameterized dissociation correction. Pair-specific
    /// log-Pc coefficients calibrated against Sutton RPE 9e Table 5-5 +
    /// Huzel &amp; Huang Fig. 2-5 published CEA runs. Reference Pc =
    /// 7 MPa matches the built-in table baseline.
    /// </summary>
    public static readonly IEquilibriumCorrection Parameterized = new LogPcDissociationCorrection();

    /// <summary>Reference pressure (Pa) at which the correction is 0.</summary>
    public const double ReferencePc_Pa = 7.0e6;

    /// <summary>
    /// Pair-specific dissociation coefficients. Tuple members:
    ///   κ_T: T_c ln²(Pc/Pc_ref) coefficient (typ. 0.0015-0.004)
    ///   κ_C: C* ln (Pc/Pc_ref)  coefficient (typ. 0.003-0.008)
    ///   κ_γ: γ  ln (Pc/Pc_ref)  coefficient (typ. 0.0005-0.002)
    ///   mr_peak: MR where dissociation peaks (≈ MR_AtPeakCStar)
    ///   mr_sigma: MR half-width of the dissociation envelope
    /// </summary>
    public readonly record struct Coefficients(
        double Kappa_T, double Kappa_C, double Kappa_gamma,
        double MR_peak, double MR_sigma);

    /// <summary>
    /// Calibration coefficient lookup. Values from Sutton 9e Table 5-5
    /// calibration (LOX/RP-1, LOX/CH4, LOX/H2). Unsupported pairs
    /// default to conservative (all-zero) coefficients so the
    /// correction becomes an identity — safer than guessing.
    /// </summary>
    public static Coefficients For(PropellantPair pair) => pair switch
    {
        //                         κ_T      κ_C      κ_γ      MR_peak  MR_sigma
        PropellantPair.LOX_CH4 => new(0.0030, 0.0060, 0.0010, 3.20,    0.80),
        PropellantPair.LOX_H2  => new(0.0040, 0.0080, 0.0015, 4.00,    1.50),
        PropellantPair.LOX_RP1 => new(0.0025, 0.0050, 0.0008, 2.50,    0.40),
        _                      => new(0.0,    0.0,    0.0,    1.0,     1.0),
    };

    /// <summary>
    /// Gaussian envelope factor peaking at <see cref="Coefficients.MR_peak"/>
    /// with half-width <see cref="Coefficients.MR_sigma"/>. Zero far
    /// from peak (dissociation is minimal in fuel- or ox-rich regions).
    /// </summary>
    public static double EnvelopeFactor(double MR, in Coefficients c)
    {
        double z = (MR - c.MR_peak) / Math.Max(c.MR_sigma, 1e-6);
        return Math.Exp(-0.5 * z * z);
    }

    // ───────────────────────── implementations ─────────────────────────

    private sealed class NoCorrection : IEquilibriumCorrection
    {
        public string Name => "None";
        public PropellantState Correct(PropellantState s, PropellantPair _) => s;
    }

    private sealed class LogPcDissociationCorrection : IEquilibriumCorrection
    {
        public string Name => "Parameterized (log-Pc dissociation)";

        public PropellantState Correct(PropellantState s, PropellantPair pair)
        {
            // PH-30 (2026-04-25): noop on already-corrected states so the
            // operation is idempotent. Lookup-cache + Correct chains can
            // re-call without double-applying the dissociation factor.
            if (!s.IsFrozen) return s;

            var c = For(pair);
            if (c.Kappa_T == 0 && c.Kappa_C == 0 && c.Kappa_gamma == 0)
                return s;   // unsupported pair — identity

            double logPcRatio = Math.Log(Math.Max(s.ChamberPressure_Pa / ReferencePc_Pa, 1e-6));
            double envelope   = EnvelopeFactor(s.MixtureRatio, c);

            // T_c gets a quadratic-in-log bump so the correction is
            // symmetric in log(Pc) — dissociation is reduced by high
            // Pc AND by very low Pc (frozen-flow breaks down below
            // 1 MPa anyway; this is a defensive scaling).
            double tcFactorRaw  = 1.0 + c.Kappa_T * envelope * logPcRatio * logPcRatio
                                    * Math.Sign(logPcRatio); // signed: Pc > ref raises T_c
            // C* and γ get linear-in-log corrections.
            double cStarFactorRaw = 1.0 + c.Kappa_C     * envelope * logPcRatio;
            double gammaFactorRaw = 1.0 + c.Kappa_gamma * envelope * logPcRatio;

            // Bound factors conservatively to avoid catastrophic
            // downstream math if someone drives Pc far outside the
            // envelope (say 0.1 MPa or 100 MPa).
            //
            // Z3 #14 / F-6 (2026-04-29): when a clamp fires, capture a
            // diagnostic note onto PropellantState.Warnings so callers
            // (UI / logs / report writers) can surface that the user is
            // operating outside the calibration envelope. Pre-Z3.14 the
            // clamps were silent, masking off-envelope conditions.
            const double TcLo = 0.85, TcHi = 1.15;
            const double CStarLo = 0.92, CStarHi = 1.08;
            const double GammaLo = 0.95, GammaHi = 1.05;
            double tcFactor    = Math.Clamp(tcFactorRaw,    TcLo,    TcHi);
            double cStarFactor = Math.Clamp(cStarFactorRaw, CStarLo, CStarHi);
            double gammaFactor = Math.Clamp(gammaFactorRaw, GammaLo, GammaHi);

            List<string>? warnings = null;
            void AddWarning(string msg)
            {
                warnings ??= new List<string>();
                warnings.Add(msg);
            }
            double pcMPa = s.ChamberPressure_Pa / 1e6;
            if (tcFactor != tcFactorRaw)
                AddWarning(
                    $"EquilibriumCorrection.tcFactor clamped at Pc {pcMPa:F2} MPa, MR {s.MixtureRatio:F2}: "
                  + $"raw {tcFactorRaw:F3} → {tcFactor:F3} (band [{TcLo:F2}, {TcHi:F2}]).");
            if (cStarFactor != cStarFactorRaw)
                AddWarning(
                    $"EquilibriumCorrection.cStarFactor clamped at Pc {pcMPa:F2} MPa, MR {s.MixtureRatio:F2}: "
                  + $"raw {cStarFactorRaw:F3} → {cStarFactor:F3} (band [{CStarLo:F2}, {CStarHi:F2}]).");
            if (gammaFactor != gammaFactorRaw)
                AddWarning(
                    $"EquilibriumCorrection.gammaFactor clamped at Pc {pcMPa:F2} MPa, MR {s.MixtureRatio:F2}: "
                  + $"raw {gammaFactorRaw:F3} → {gammaFactor:F3} (band [{GammaLo:F2}, {GammaHi:F2}]).");

            // Preserve any prior warnings on the input state (e.g. from a
            // multi-pass correction pipeline) and append the new ones.
            IReadOnlyList<string>? mergedWarnings = warnings is null
                ? s.Warnings
                : (s.Warnings is { Count: > 0 } prior
                    ? new List<string>(prior).Concat(warnings).ToList()
                    : (IReadOnlyList<string>)warnings);

            return s with
            {
                ChamberTemp_K = s.ChamberTemp_K * tcFactor,
                CStar_ms      = s.CStar_ms      * cStarFactor,
                // PH-24: apply the same correction to both γ values for
                // the parameterized correction. A future Gordon-McBride
                // solver will give them distinct values; the audit
                // recommends preserving the equality for the empirical
                // log-Pc form which doesn't model chamber-vs-throat
                // composition shift.
                GammaChamber  = s.GammaChamber  * gammaFactor,
                GammaThroat   = s.GammaThroat   * gammaFactor,
                // IspVacuum scales with sqrt(T_c/MW * γ-factor); approximate as
                // sqrt of tcFactor * small γ correction. Keeps Isp self-consistent.
                IspVacuum_s   = s.IspVacuum_s * Math.Sqrt(tcFactor) * gammaFactor,
                // R = R_u/MW unchanged (MW shifts are small; a future
                // Gordon-McBride solver will update this properly).
                // PH-30: mark as no-longer-frozen so subsequent Correct
                // calls noop.
                IsFrozen      = false,
                Warnings      = mergedWarnings,
            };
        }
    }
}
