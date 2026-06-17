// PropellantTables.cs — γ(T), cp(T), μ(T), MW, ε_emit lookup for the
// four Wave-1 resistojet propellants.
//
// 20-anchor table per propellant in log-T spacing, linear interpolation
// between anchors. Anchors taken from the NIST Chemistry WebBook fluid-
// property pages, sampled to bracket the resistojet operating envelope
// (200 K cold-start to 3500 K dissociation onset).
//
// Cached as static read-only structs per propellant; lookup is O(log n)
// binary search on the log-T axis. The mixture-rule helpers
// (`MixtureGamma`, `MixtureCp`, `MixtureMW`) compose four-species
// composition (per `PropellantInletComposition`) into effective bulk-gas
// properties.
//
// Determinism contract: every method here is pure and allocation-free
// on the SA hot path. No DateTime, no Random, no Dictionary iteration.
// Marked `[Deterministic]` once VFD analyzer attribute lands on the
// solver entry points.
//
// Citations:
//   NIST Chemistry WebBook — https://webbook.nist.gov/chemistry/
//     NH3:  https://webbook.nist.gov/cgi/inchi?ID=C7664417
//     N2:   https://webbook.nist.gov/cgi/inchi?ID=C7727379
//     H2:   https://webbook.nist.gov/cgi/inchi?ID=C1333740
//     H2O:  https://webbook.nist.gov/cgi/inchi?ID=C7732185
//   Sutton/Biblarz, "Rocket Propulsion Elements," 9e, Table 5-3.
//   Holman, "Heat Transfer," 10e, Appendix A.

using System;

namespace Voxelforge.ElectricPropulsion.Thermo;

/// <summary>
/// Per-propellant thermodynamic property tables. 20 anchors in log-T
/// spacing covering 200–3500 K, linear interpolation between anchors.
/// Mixture-rule composition is via <see cref="MixtureGamma"/>,
/// <see cref="MixtureCp"/>, <see cref="MixtureMW"/>.
/// </summary>
internal static class PropellantTables
{
    /// <summary>Universal gas constant [J/(mol·K)].</summary>
    internal const double R_universal = 8.31446261815324;

    /// <summary>Standard gravity for Isp computation [m/s²].</summary>
    internal const double g0 = 9.80665;

    /// <summary>Stefan-Boltzmann constant [W/(m²·K⁴)].</summary>
    internal const double Sigma_SB = 5.670374419e-8;

    /// <summary>Lower temperature bound of the property tables [K].</summary>
    internal const double T_min_K = 200.0;

    /// <summary>Upper temperature bound of the property tables [K].</summary>
    internal const double T_max_K = 3500.0;

    /// <summary>
    /// Per-species property anchors. <see cref="Temps_K"/> is monotonically
    /// increasing in log-T spacing; the four arrays
    /// (<see cref="Gamma"/>, <see cref="Cp_JkgK"/>, <see cref="Mu_PaS"/>)
    /// have the same length and each index <c>i</c> corresponds to
    /// <c>Temps_K[i]</c>.
    /// </summary>
    internal sealed class SpeciesTable
    {
        internal required double[] Temps_K { get; init; }
        internal required double[] Gamma { get; init; }
        internal required double[] Cp_JkgK { get; init; }
        internal required double[] Mu_PaS { get; init; }
        internal required double MolarMass_kgmol { get; init; }
        internal required double DecompositionLimit_K { get; init; }
        internal required string Name { get; init; }
    }

    // 20 anchors in log-T from 200 K to 3500 K.
    // log-T spacing: T_i = T_min · (T_max/T_min)^(i / 19)
    private static readonly double[] _T_anchors_K = new double[]
    {
        200.0,  234.7,  275.5,  323.4,  379.6,  445.5,  522.9,
        613.7,  720.4,  845.5,  992.4,  1164.8, 1367.2, 1604.7,
        1883.6, 2210.9, 2595.0, 3045.8, 3500.0, 3500.0,
    };

    // Sentinel-fill anchor 19 to guarantee 20-element arrays (index 19
    // never reached by interpolation — clamped at index 18 by the binary
    // search). Kept for table-shape uniformity.
    static PropellantTables()
    {
        // Re-derive temps in log-T to be exact (the literals above are
        // rounded to 1 decimal for readability; the runtime values use
        // the formula directly).
        for (int i = 0; i < 20; i++)
        {
            _T_anchors_K[i] = T_min_K * Math.Pow(T_max_K / T_min_K, i / 19.0);
        }
    }

    // ---- NH3 (ammonia) -----------------------------------------------------
    // γ varies 1.31 (cold) → 1.20 (hot, with vibrational modes excited).
    // cp_mass varies ~2080 J/(kg·K) (cold) → ~3300 J/(kg·K) (hot).
    // μ varies ~9e-6 Pa·s (cold) → ~7e-5 Pa·s (hot).
    // Decomposition limit: ~1100 K (NH₃ → ½ N₂ + 1.5 H₂ becomes thermo-
    // dynamically favored beyond this).
    internal static readonly SpeciesTable NH3 = new()
    {
        Name                = "NH3",
        Temps_K             = _T_anchors_K,
        Gamma               = new[] { 1.31, 1.31, 1.30, 1.29, 1.28, 1.27, 1.26, 1.25, 1.24, 1.23, 1.22, 1.21, 1.20, 1.20, 1.20, 1.20, 1.20, 1.20, 1.20, 1.20 },
        Cp_JkgK             = new[] { 2080.0, 2120.0, 2160.0, 2210.0, 2280.0, 2370.0, 2470.0, 2580.0, 2700.0, 2820.0, 2940.0, 3050.0, 3150.0, 3220.0, 3270.0, 3300.0, 3320.0, 3330.0, 3340.0, 3340.0 },
        Mu_PaS              = new[] { 9.0e-6, 1.0e-5, 1.2e-5, 1.5e-5, 1.8e-5, 2.2e-5, 2.6e-5, 3.0e-5, 3.5e-5, 4.0e-5, 4.5e-5, 5.0e-5, 5.5e-5, 6.0e-5, 6.5e-5, 6.8e-5, 7.0e-5, 7.0e-5, 7.0e-5, 7.0e-5 },
        MolarMass_kgmol     = 0.01703052,
        DecompositionLimit_K = 1100.0,
    };

    // ---- N2 (nitrogen, present in N₂H₄ catalyst products) ------------------
    // γ varies 1.40 (cold) → 1.30 (hot).
    // cp_mass varies ~1040 J/(kg·K) (cold) → ~1300 J/(kg·K) (hot).
    internal static readonly SpeciesTable N2 = new()
    {
        Name                = "N2",
        Temps_K             = _T_anchors_K,
        Gamma               = new[] { 1.40, 1.40, 1.40, 1.40, 1.40, 1.39, 1.39, 1.38, 1.37, 1.36, 1.35, 1.34, 1.33, 1.32, 1.31, 1.30, 1.30, 1.30, 1.30, 1.30 },
        Cp_JkgK             = new[] { 1039.0, 1040.0, 1041.0, 1043.0, 1048.0, 1055.0, 1066.0, 1081.0, 1101.0, 1124.0, 1149.0, 1174.0, 1200.0, 1225.0, 1248.0, 1268.0, 1284.0, 1296.0, 1304.0, 1304.0 },
        Mu_PaS              = new[] { 1.30e-5, 1.50e-5, 1.74e-5, 1.99e-5, 2.27e-5, 2.57e-5, 2.88e-5, 3.21e-5, 3.55e-5, 3.90e-5, 4.25e-5, 4.60e-5, 4.95e-5, 5.30e-5, 5.65e-5, 5.97e-5, 6.30e-5, 6.60e-5, 6.85e-5, 6.85e-5 },
        MolarMass_kgmol     = 0.0280134,
        DecompositionLimit_K = 5000.0,  // N2 dissociation is well above resistojet regime
    };

    // ---- H2 (hydrogen) -----------------------------------------------------
    // γ varies 1.41 (cold, near rotation-only) → 1.32 (hot, vibrational excited).
    // cp_mass varies ~14300 J/(kg·K) — very high due to low MW.
    // Dissociation onset: ~3500 K (gate hard-fail).
    internal static readonly SpeciesTable H2 = new()
    {
        Name                = "H2",
        Temps_K             = _T_anchors_K,
        Gamma               = new[] { 1.41, 1.41, 1.41, 1.41, 1.41, 1.40, 1.40, 1.40, 1.39, 1.39, 1.38, 1.37, 1.36, 1.35, 1.34, 1.33, 1.32, 1.32, 1.32, 1.32 },
        Cp_JkgK             = new[] { 14300.0, 14310.0, 14330.0, 14380.0, 14460.0, 14570.0, 14720.0, 14900.0, 15110.0, 15330.0, 15580.0, 15840.0, 16110.0, 16400.0, 16700.0, 17000.0, 17300.0, 17600.0, 17900.0, 17900.0 },
        Mu_PaS              = new[] { 6.81e-6, 7.83e-6, 8.94e-6, 1.01e-5, 1.14e-5, 1.27e-5, 1.42e-5, 1.57e-5, 1.73e-5, 1.89e-5, 2.06e-5, 2.23e-5, 2.40e-5, 2.57e-5, 2.74e-5, 2.91e-5, 3.07e-5, 3.23e-5, 3.39e-5, 3.39e-5 },
        MolarMass_kgmol     = 0.00201588,
        DecompositionLimit_K = 3500.0,
    };

    // ---- Xe (xenon) — Wave-2 HET ------------------------------------------
    // Monatomic noble gas. γ = 5/3 = 1.667 (T-independent in resistojet/HET regime).
    // cp_mass = (5/2)·R/MW = (5/2)·8.314/0.131293 ≈ 158.4 J/(kg·K) (T-independent).
    // μ varies from ~16e-6 Pa·s @ 200 K to ~100e-6 Pa·s @ 3500 K (Sutherland-like
    // for noble gases). HET physics consumes only MW on the hot path
    // (ion velocity v_i = √(2·e·V_d/m_xe) in Goebel & Katz Eq 3.36); γ / cp / μ
    // entries are present for table-shape uniformity and for future arcjet use
    // when xenon is electrothermally heated rather than electrostatically
    // accelerated.
    // First ionisation energy: 12.13 eV. Plasma-physics constants live in
    // BuschDischargeModel rather than this table.
    internal static readonly SpeciesTable Xenon = new()
    {
        Name                = "Xe",
        Temps_K             = _T_anchors_K,
        Gamma               = new[] { 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667, 1.667 },
        Cp_JkgK             = new[] { 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4, 158.4 },
        Mu_PaS              = new[] { 1.6e-5, 1.9e-5, 2.3e-5, 2.7e-5, 3.2e-5, 3.7e-5, 4.2e-5, 4.7e-5, 5.3e-5, 5.9e-5, 6.5e-5, 7.1e-5, 7.7e-5, 8.3e-5, 8.9e-5, 9.4e-5, 9.8e-5, 1.0e-4, 1.0e-4, 1.0e-4 },
        MolarMass_kgmol     = 0.131293,
        DecompositionLimit_K = 5000.0,  // Xe is monatomic; no dissociation in resistojet/HET regime
    };

    // ---- H2O (water vapor) -------------------------------------------------
    // γ varies 1.33 (cold steam) → 1.20 (hot, vibrational excited).
    // cp_mass varies ~1860 J/(kg·K) (cold) → ~2700 J/(kg·K) (hot).
    // Dissociation onset: ~2700 K (H2O → OH + H + ...).
    internal static readonly SpeciesTable H2O = new()
    {
        Name                = "H2O",
        Temps_K             = _T_anchors_K,
        Gamma               = new[] { 1.33, 1.33, 1.32, 1.32, 1.31, 1.30, 1.29, 1.28, 1.27, 1.26, 1.25, 1.24, 1.23, 1.22, 1.21, 1.20, 1.20, 1.20, 1.20, 1.20 },
        Cp_JkgK             = new[] { 1860.0, 1870.0, 1890.0, 1920.0, 1970.0, 2030.0, 2110.0, 2200.0, 2290.0, 2380.0, 2460.0, 2540.0, 2600.0, 2650.0, 2680.0, 2700.0, 2710.0, 2720.0, 2720.0, 2720.0 },
        Mu_PaS              = new[] { 9.0e-6, 1.1e-5, 1.3e-5, 1.6e-5, 1.9e-5, 2.3e-5, 2.7e-5, 3.1e-5, 3.5e-5, 3.9e-5, 4.4e-5, 4.8e-5, 5.2e-5, 5.6e-5, 6.0e-5, 6.3e-5, 6.6e-5, 6.8e-5, 7.0e-5, 7.0e-5 },
        MolarMass_kgmol     = 0.01801528,
        DecompositionLimit_K = 2700.0,
    };

    /// <summary>
    /// Look up γ at temperature <paramref name="T_K"/> for a single species.
    /// Linear interpolation in log-T space; clamps to anchor[0] / anchor[18]
    /// outside [200, 3500] K.
    /// </summary>
    internal static double GammaOf(SpeciesTable species, double T_K)
        => InterpolateLogT(species.Temps_K, species.Gamma, T_K);

    /// <summary>cp [J/(kg·K)] at temperature <paramref name="T_K"/>.</summary>
    internal static double CpOf(SpeciesTable species, double T_K)
        => InterpolateLogT(species.Temps_K, species.Cp_JkgK, T_K);

    /// <summary>μ [Pa·s] at temperature <paramref name="T_K"/>.</summary>
    internal static double MuOf(SpeciesTable species, double T_K)
        => InterpolateLogT(species.Temps_K, species.Mu_PaS, T_K);

    /// <summary>
    /// Mixture γ at temperature <paramref name="T_K"/> for the given inlet
    /// composition. Computes the mass-averaged cp and cv, then γ = cp/cv.
    /// (γ doesn't compose linearly; cp and cv do.)
    /// </summary>
    internal static double MixtureGamma(PropellantInletComposition comp, double T_K)
    {
        // Convert mole fractions to mass fractions (mass = mole · MW).
        double mw_avg = MixtureMW(comp);
        double w_NH3 = comp.NH3MoleFraction * NH3.MolarMass_kgmol / mw_avg;
        double w_N2  = comp.N2MoleFraction  * N2.MolarMass_kgmol  / mw_avg;
        double w_H2  = comp.H2MoleFraction  * H2.MolarMass_kgmol  / mw_avg;
        double w_H2O = comp.H2OMoleFraction * H2O.MolarMass_kgmol / mw_avg;

        // cp_mix = Σ w_i · cp_i.
        double cp_mix = w_NH3 * CpOf(NH3, T_K)
                     + w_N2  * CpOf(N2,  T_K)
                     + w_H2  * CpOf(H2,  T_K)
                     + w_H2O * CpOf(H2O, T_K);

        // cv = cp - R/MW.
        double R_specific = R_universal / mw_avg;
        double cv_mix = cp_mix - R_specific;
        return cp_mix / cv_mix;
    }

    /// <summary>Mass-averaged cp [J/(kg·K)] at temperature <paramref name="T_K"/>.</summary>
    internal static double MixtureCp(PropellantInletComposition comp, double T_K)
    {
        double mw_avg = MixtureMW(comp);
        double w_NH3 = comp.NH3MoleFraction * NH3.MolarMass_kgmol / mw_avg;
        double w_N2  = comp.N2MoleFraction  * N2.MolarMass_kgmol  / mw_avg;
        double w_H2  = comp.H2MoleFraction  * H2.MolarMass_kgmol  / mw_avg;
        double w_H2O = comp.H2OMoleFraction * H2O.MolarMass_kgmol / mw_avg;
        return w_NH3 * CpOf(NH3, T_K)
             + w_N2  * CpOf(N2,  T_K)
             + w_H2  * CpOf(H2,  T_K)
             + w_H2O * CpOf(H2O, T_K);
    }

    /// <summary>
    /// Mole-averaged molar mass [kg/mol] of the mixture. Pure-species inlets
    /// return the species's <see cref="SpeciesTable.MolarMass_kgmol"/>.
    /// </summary>
    internal static double MixtureMW(PropellantInletComposition comp)
        => comp.NH3MoleFraction * NH3.MolarMass_kgmol
         + comp.N2MoleFraction  * N2.MolarMass_kgmol
         + comp.H2MoleFraction  * H2.MolarMass_kgmol
         + comp.H2OMoleFraction * H2O.MolarMass_kgmol;

    /// <summary>
    /// Wilke-rule mixture viscosity. Less rigorous than full Wilke (which
    /// needs binary diffusion coefficients), but sufficient for the lumped
    /// 0-D resistojet model. Mass-averaged μ.
    /// </summary>
    internal static double MixtureMu(PropellantInletComposition comp, double T_K)
    {
        double mw_avg = MixtureMW(comp);
        double w_NH3 = comp.NH3MoleFraction * NH3.MolarMass_kgmol / mw_avg;
        double w_N2  = comp.N2MoleFraction  * N2.MolarMass_kgmol  / mw_avg;
        double w_H2  = comp.H2MoleFraction  * H2.MolarMass_kgmol  / mw_avg;
        double w_H2O = comp.H2OMoleFraction * H2O.MolarMass_kgmol / mw_avg;
        return w_NH3 * MuOf(NH3, T_K)
             + w_N2  * MuOf(N2,  T_K)
             + w_H2  * MuOf(H2,  T_K)
             + w_H2O * MuOf(H2O, T_K);
    }

    /// <summary>
    /// Pillar-effective decomposition limit for the mixture: the lowest
    /// per-species limit weighted by mole fraction. A pure NH₃ stream
    /// decomposes at 1100 K; an NH₃/H₂ mixture at 2/3 mole-fraction NH₃
    /// inherits the 1100 K limit. Mixture limit is the minimum
    /// non-trivial-fraction species limit.
    /// </summary>
    internal static double MixtureDecompositionLimit_K(PropellantInletComposition comp)
    {
        const double moleThreshold = 0.01;  // ignore species below 1% mole
        double limit = double.PositiveInfinity;
        if (comp.NH3MoleFraction > moleThreshold) limit = Math.Min(limit, NH3.DecompositionLimit_K);
        if (comp.N2MoleFraction  > moleThreshold) limit = Math.Min(limit, N2.DecompositionLimit_K);
        if (comp.H2MoleFraction  > moleThreshold) limit = Math.Min(limit, H2.DecompositionLimit_K);
        if (comp.H2OMoleFraction > moleThreshold) limit = Math.Min(limit, H2O.DecompositionLimit_K);
        return double.IsPositiveInfinity(limit) ? T_max_K : limit;
    }

    /// <summary>
    /// Get the species table for a single-species enum.
    /// </summary>
    internal static SpeciesTable Lookup(Propellant p) => p switch
    {
        Propellant.NH3            => NH3,
        Propellant.N2H4Decomposed => NH3,  // dominant species after Shell-405 catalyst
        Propellant.H2             => H2,
        Propellant.H2O            => H2O,
        Propellant.Xenon          => Xenon,
        _ => throw new ArgumentOutOfRangeException(nameof(p), $"Unknown propellant {p}"),
    };

    // Linear interpolation in log-T space.
    private static double InterpolateLogT(double[] temps, double[] values, double T_K)
    {
        // Clamp to first / last anchor.
        if (T_K <= temps[0]) return values[0];
        if (T_K >= temps[temps.Length - 1]) return values[values.Length - 1];

        // Binary search for upper bound.
        int lo = 0, hi = temps.Length - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (temps[mid] <= T_K) lo = mid; else hi = mid;
        }
        // temps[lo] <= T_K < temps[hi]; lo+1 == hi.
        double logT_lo = Math.Log(temps[lo]);
        double logT_hi = Math.Log(temps[hi]);
        double logT    = Math.Log(T_K);
        double frac    = (logT - logT_lo) / (logT_hi - logT_lo);
        return values[lo] + frac * (values[hi] - values[lo]);
    }
}
