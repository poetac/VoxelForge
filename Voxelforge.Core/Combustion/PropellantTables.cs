// PropellantTables.cs — Public entry point for combustion-gas properties.
//
// This file used to carry the LOX/CH₄ table inline; it has been split into
// the extensible framework described in PropellantPair.cs +
// IPropellantTable.cs. The only things that remain here are:
//
//   • The `PropellantState` record (common data contract).
//   • Isentropic-flow helpers (Mach↔area ratio, T/T₀, P/P₀, T_aw).
//   • `Lookup(pair, MR, Pc)` dispatching through the `IPropellantTable`
//     registry with memoization + equilibrium-correction routing.
//
// To add a new propellant pair:
//   1. Add an enum entry in PropellantPair.cs.
//   2. Add a metadata entry in PropellantPairs.All.
//   3. Add a new internal sealed class XxxTable : CeaTableBase under
//      Combustion/ with populated 1D-MR arrays.
//   4. Add the switch arm in PropellantPairs.GetTable().
//   5. (UI will pick up the dropdown entry automatically.)

using System.Collections.Concurrent;

namespace Voxelforge.Combustion;

/// <summary>
/// Combustion-gas stagnation state consumed by every downstream module.
/// Same record regardless of which propellant pair produced it.
/// <para>
/// PH-24 (2026-04-25): the legacy single <c>Gamma</c> field was split
/// into <see cref="GammaChamber"/> + <see cref="GammaThroat"/>. For
/// frozen-flow tables both values are equal (no composition change
/// across the chamber-to-throat acceleration). Future 2-D propellant
/// tables (PH-4, blocked on external CEA data) and the Gordon-McBride
/// solver will populate them separately. The <c>Gamma</c> property is
/// kept as a back-compat alias that returns <see cref="GammaChamber"/>;
/// gas-dynamics consumers (BartzHeatFlux, MachFromAreaRatio,
/// PrandtlMeyer expansion, etc.) should migrate to
/// <see cref="GammaThroat"/> once a meaningful difference exists.
/// </para>
/// <para>
/// PH-30 (2026-04-25): <see cref="IsFrozen"/> flag distinguishes a
/// frozen-table state from one already processed through
/// <see cref="EquilibriumCorrection"/>. The correction noops on
/// <c>!IsFrozen</c> to keep the operation idempotent.
/// </para>
/// </summary>
public readonly record struct PropellantState(
    double MixtureRatio,       // O/F mass ratio
    double ChamberPressure_Pa, // stagnation pressure
    double ChamberTemp_K,      // stagnation temperature T_c
    double GammaChamber,       // specific heat ratio γ at chamber stagnation
    double GammaThroat,        // specific heat ratio γ at throat (= GammaChamber for frozen flow)
    double MolecularWeight,    // kg/kmol
    double SpecificGasConst,   // R = R_u / MW   [J/(kg·K)]
    double Cp_Jkg,             // specific heat at chamber conditions [J/(kg·K)]
    double Viscosity_PaS,      // dynamic viscosity μ [Pa·s]
    double Prandtl,            // Pr = μ·Cp/k
    double CStar_ms,           // characteristic velocity C*
    double IspVacuum_s,        // vacuum specific impulse at ε = 40 (reference)
    string PropellantName,
    bool IsFrozen = true)      // false after EquilibriumCorrection.Correct
{
    /// <summary>
    /// Back-compat alias for legacy callers — returns
    /// <see cref="GammaChamber"/>. Gas-dynamics consumers (anything
    /// dealing with Mach number, area ratios, Prandtl-Meyer expansion,
    /// adiabatic-wall temperature in the nozzle) should prefer
    /// <see cref="GammaThroat"/> directly.
    /// </summary>
    public double Gamma => GammaChamber;

    /// <summary>
    /// Z3 #14 / F-6 (2026-04-29): diagnostic notes attached when a
    /// post-processor (e.g. <see cref="EquilibriumCorrection"/>) had to
    /// clamp or otherwise sanitize a derived value. Null when no
    /// warnings were emitted (the common case). Each entry is a
    /// short human-readable string identifying the clamp:
    /// <c>"tcFactor clamped low (Pc 0.10 MPa, raw 0.802 → 0.85)"</c>.
    /// Serializable as part of the record state so callers
    /// (UI / logs / report writers) can surface them. Not part of any
    /// JSON schema; consumed defensively via <c>?.Count</c>.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; init; }
}

public static class PropellantTables
{
    // Universal gas constant [J/(kmol·K)]
    public const double R_UNIVERSAL = 8314.462618;

    /// <summary>
    /// Characteristic velocity C* efficiency correction for realistic
    /// combustion. Typical well-designed chamber: η_C* ≈ 0.93–0.97.
    /// </summary>
    public const double DefaultCStarEfficiency = 0.95;

    // ─────────────────────────────────────────────────────────────────
    //  Public lookup — preferred entry point.
    // ─────────────────────────────────────────────────────────────────

    // Cache the (pair, MR, Pc) → PropellantState map. Lookup is pure
    // (input fully determines output, no hidden state) and is called
    // once per `GenerateWith` invocation — i.e. once per SA candidate ×
    // 8 workers × ~300 iterations ≈ 2400 calls per run, with (pair, MR,
    // Pc) FIXED across an SA run. The cache typically holds exactly one
    // entry per session. ConcurrentDictionary ⇒ thread-safe for the
    // parallel-SA batch.
    //
    // Cache key is extended with a bool for UseEquilibrium so flipping
    // the flag mid-session doesn't serve stale entries.
    private static readonly ConcurrentDictionary<(PropellantPair, double, double, bool), PropellantState>
        _lookupCache = new();

    /// <summary>
    /// When true, <see cref="Lookup"/> post-processes the frozen-table
    /// result through <see cref="EquilibriumCorrection"/> to produce an
    /// equilibrium-adjusted state. Default false preserves legacy
    /// behaviour bit-identically. <see cref="AutoSeeder"/> auto-enables
    /// at Pc &gt; 10 MPa; the <c>--equilibrium</c> CLI flag overrides.
    /// </summary>
    public static bool UseEquilibrium { get; set; } = false;

    /// <summary>
    /// The active correction implementation. Replaceable — a future
    /// Gordon-McBride sprint drops in a proper Gibbs-minimisation
    /// solver here without touching call sites. Default is the shipped
    /// parameterized log-Pc dissociation correction calibrated against
    /// Sutton RPE 9e + Huzel &amp; Huang.
    /// </summary>
    public static IEquilibriumCorrection EquilibriumCorrectionProvider { get; set; }
        = EquilibriumCorrection.Parameterized;

    /// <summary>
    /// Look up combustion-gas properties for any supported propellant pair
    /// at a given mixture ratio and chamber pressure. Delegates to the
    /// pair-specific CEA table registered in
    /// <see cref="PropellantPairs.GetTable"/>. When
    /// <see cref="UseEquilibrium"/> is true, post-processes the result
    /// through <see cref="EquilibriumCorrectionProvider"/>.
    /// </summary>
    public static PropellantState Lookup(
        PropellantPair pair, double mixtureRatio, double chamberPressure_Pa)
    {
        bool eq = UseEquilibrium;
        return _lookupCache.GetOrAdd(
            (pair, mixtureRatio, chamberPressure_Pa, eq),
            k =>
            {
                var frozen = PropellantPairs.GetTable(k.Item1).GetState(k.Item2, k.Item3);
                return k.Item4
                    ? EquilibriumCorrectionProvider.Correct(frozen, k.Item1)
                    : frozen;
            });
    }

    /// <summary>
    /// Internal testing hook — purge the static lookup cache. Required
    /// when callers swap <see cref="EquilibriumCorrectionProvider"/>
    /// in-session: cache keys include <see cref="UseEquilibrium"/> but
    /// not the provider instance, so a test that replaces the provider
    /// while UseEquilibrium is true would otherwise see stale entries.
    /// Production code never needs this — the provider is set at
    /// startup and not swapped.
    /// </summary>
    internal static void ClearLookupCacheForTests() => _lookupCache.Clear();

    // ─────────────────────────────────────────────────────────────────
    //  Isentropic-flow helpers — propellant-independent.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Isentropic Mach number at a station given area ratio A/A_t.
    /// Converging side returns subsonic root (M &lt; 1); diverging side
    /// returns supersonic root (M &gt; 1). Newton iteration, ~1e-8 tol.
    /// </summary>
    public static double MachFromAreaRatio(double areaRatio, double gamma, bool supersonic)
    {
        double g = gamma;
        double gp1 = g + 1.0;
        double gm1 = g - 1.0;
        double M = supersonic ? 2.5 : 0.3;
        for (int i = 0; i < 60; i++)
        {
            double term = (2.0 / gp1) * (1.0 + 0.5 * gm1 * M * M);
            double f = (1.0 / M) * Math.Pow(term, gp1 / (2.0 * gm1)) - areaRatio;
            double dTerm_dM = (2.0 / gp1) * gm1 * M;
            double df = -(1.0 / (M * M)) * Math.Pow(term, gp1 / (2.0 * gm1))
                      + (1.0 / M) * (gp1 / (2.0 * gm1)) * Math.Pow(term, gp1 / (2.0 * gm1) - 1.0) * dTerm_dM;
            double step = f / df;
            M -= step;
            if (M <= 0) M = supersonic ? 1.05 : 0.05;
            if (M >= 1.0 && !supersonic) M = 0.99;
            if (M <= 1.0 && supersonic) M = 1.01;
            if (Math.Abs(step) < 1e-8) break;
        }
        return M;
    }

    public static double StaticTemp(double T0, double M, double gamma)
        => T0 / (1.0 + 0.5 * (gamma - 1.0) * M * M);

    public static double StaticPressure(double P0, double M, double gamma)
        => P0 * Math.Pow(1.0 + 0.5 * (gamma - 1.0) * M * M, -gamma / (gamma - 1.0));

    public static double RecoveryFactor(double prandtl) => Math.Cbrt(prandtl);

    public static double AdiabaticWallTemp(double Tstatic, double M, double gamma, double prandtl)
    {
        double r = RecoveryFactor(prandtl);
        return Tstatic * (1.0 + r * 0.5 * (gamma - 1.0) * M * M);
    }
}
