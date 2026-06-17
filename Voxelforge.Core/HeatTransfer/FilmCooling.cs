// FilmCooling.cs — Axisymmetric gaseous film-cooling effectiveness model.
//
// Real liquid-rocket engines keep the gas-side wall alive by bleeding a
// small fraction of fuel through outermost injector orifices to form a
// thin, cooler layer (the "film") along the chamber wall. Without film
// cooling, even the best regen design for LOX/CH₄ at Pc ≥ 7 MPa predicts
// wall temperatures of 2500–3000 K — well past any metal's service limit.
// Real engines run wall temps of 900–1200 K because film cooling cuts the
// effective recovery temperature by hundreds of Kelvin near the wall.
//
// This module implements the Stechman / Ewen decay correlation, widely
// used for preliminary LRE design:
//
//     η(x) = exp( -β · (x − x_inj) / s · (G_g / G_f)^0.25 )
//
// where
//   η       = film cooling effectiveness (1 = pure film, 0 = pure core)
//   x_inj   = axial position where film is injected (injector face)
//   s       = film slot height (radial thickness of injected film)
//   G_g     = ρ_g · u_g  = core mass flux      (kg/m²·s)
//   G_f     = ρ_f · u_f  = film mass flux       (kg/m²·s)
//   β       = empirical decay coefficient, ≈ 0.15 for gaseous methane
//
// The effective recovery temperature becomes:
//
//     T_aw_eff = T_aw,core − η(x) · (T_aw,core − T_film)
//
// Film mass depletes from burnout (mixing with core combustion gas):
//
//     ṁ_f(x) = ṁ_f0 · max(0, 1 − (x − x_inj)/L_burn)
//
// When ṁ_f → 0, η → 0 automatically because G_f → 0 in the denominator.
//
// This is a **MVP correlation**. Production design should calibrate β
// against a CFD or firing test for the specific injector element style
// (coaxial vs impinging vs showerhead). Typical tuning range β ∈ [0.05, 0.30].
//
// References:
//   Stechman, R.C. "Design Criteria for Film Cooling for Small Liquid-
//     Propellant Rocket Engines." AIAA 68-617.
//   Ewen, R.L. & Evans, D.C. "Analytical and Experimental Investigation
//     of Film Cooling." JPL TR-6 (1968).
//   Huzel & Huang, Ch. 4.5.

namespace Voxelforge.HeatTransfer;

public sealed record FilmCoolingInputs
{
    /// <summary>Enable film cooling (false = pure regen).</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Fraction of fuel mass flow diverted to film. Typical 0.03–0.10.</summary>
    public double FuelFractionAsFilm { get; init; } = 0.05;

    /// <summary>Axial position where film is injected (mm, from injector face).</summary>
    public double InjectionX_mm { get; init; } = 0.0;

    /// <summary>Radial thickness of injected film (mm). Typical 0.3–1.0.</summary>
    public double FilmSlotHeight_mm { get; init; } = 0.6;

    /// <summary>Film injection temperature (K). Defaults to coolant inlet T if unset.</summary>
    public double FilmInletTemp_K { get; init; } = 150.0;

    /// <summary>Burnout length (mm) — film fully consumed by this distance downstream.</summary>
    public double BurnoutLength_mm { get; init; } = 200.0;

    /// <summary>Stechman decay coefficient β. Typical 0.05–0.30. MVP default 0.15.</summary>
    public double DecayCoefficient { get; init; } = 0.15;

    /// <summary>
    /// Turbulent mixing "boost" factor applied to η after sharp contour
    /// curvature events (throat entry). Degrades film by ~20–30% through
    /// the throat. Set to 0 to disable.
    /// </summary>
    public double ThroatMixingDegradation { get; init; } = 0.25;
}

/// <summary>
/// Precomputed film-cooling state along the contour. Stations indexed the
/// same as <see cref="Voxelforge.Chamber.ChamberContour.Stations"/>.
/// </summary>
public sealed record FilmCoolingProfile(
    double[] Effectiveness,          // η at each station [0..1]
    double[] RemainingMassFlow_kgs,  // ṁ_f(x) in film
    double[] FilmBulkTemp_K,         // rough film T (heats as it moves)
    double TotalFilmMassFlow_kgs,    // initial film flow
    string[] Warnings);

public static class FilmCooling
{
    /// <summary>
    /// Compute η(x) and remaining film flow at each contour station.
    /// Must be called before the regen solver so T_aw_eff(x) feeds the
    /// Bartz balance at every station.
    /// </summary>
    public static FilmCoolingProfile Compute(
        Chamber.ChamberContour contour,
        FilmCoolingInputs film,
        double totalFuelMassFlow_kgs,
        double gasStaticTempAtChamber_K,
        double gasDensityAtChamber_kgm3,
        double gasVelocityAtChamber_ms,
        double filmDensity_kgm3 = 10.0,     // cold CH₄ at ~150 K, 10 MPa
        double filmVelocity_ms = 50.0,      // typical injection velocity
        double[]? gasMassFluxPerStation_kg_m2_s = null)
    {
        int N = contour.Stations.Length;
        var eta = new double[N];
        var mFilm = new double[N];
        var tFilm = new double[N];
        // Sprint 14 / Track I / P9: pre-size at 4 — see RegenCoolingSolver.
        var warnings = new List<string>(4);

        if (!film.Enabled || film.FuelFractionAsFilm <= 0)
        {
            return new FilmCoolingProfile(
                Effectiveness: eta,
                RemainingMassFlow_kgs: mFilm,
                FilmBulkTemp_K: tFilm,
                TotalFilmMassFlow_kgs: 0,
                Warnings: Array.Empty<string>());
        }

        double mFilm0 = totalFuelMassFlow_kgs * film.FuelFractionAsFilm;
        double slotHeight_m = film.FilmSlotHeight_mm * 1e-3;
        if (slotHeight_m < 1e-4)
        {
            warnings.Add("Film slot height < 0.1 mm — treated as no film.");
            return new FilmCoolingProfile(eta, mFilm, tFilm, 0, warnings.ToArray());
        }

        // Z3-F1 (2026-04-29): support per-station G_g (gas mass flux) so the
        // Stechman momentum-ratio factor `(G_g/G_f)^0.25` reflects axial
        // variation (G_g grows toward the throat as area shrinks; G_g·A is
        // constant by mass conservation). The chamber-side scalar G_g is
        // accurate at the injector face but under-predicts at the throat by
        // ~the contraction ratio, which biases η high mid-chamber. When the
        // caller (RegenCoolingSolver) provides a per-station array, we use
        // it; otherwise fall back to the scalar for back-compat.
        bool usePerStationG = gasMassFluxPerStation_kg_m2_s is not null
                              && gasMassFluxPerStation_kg_m2_s.Length == N;
        double G_g_chamber = gasDensityAtChamber_kgm3 * gasVelocityAtChamber_ms;
        double G_f = filmDensity_kgm3 * filmVelocity_ms;
        if (G_f < 1.0) G_f = 1.0;
        double momFactor_chamber = Math.Pow(G_g_chamber / G_f, 0.25);

        double x_throat = contour.Stations[contour.ThroatIndex].X_mm;
        double x_inj = film.InjectionX_mm;
        double L_burn = Math.Max(film.BurnoutLength_mm, 1.0);

        for (int i = 0; i < N; i++)
        {
            double x = contour.Stations[i].X_mm;
            double dx = x - x_inj;

            if (dx < 0)
            {
                // Upstream of injection point — no film yet.
                eta[i] = 0; mFilm[i] = 0; tFilm[i] = film.FilmInletTemp_K;
                continue;
            }

            // Remaining film fraction — linear burnout.
            double fRemain = Math.Max(0, 1.0 - dx / L_burn);
            mFilm[i] = mFilm0 * fRemain;
            if (fRemain <= 0)
            {
                eta[i] = 0;
                tFilm[i] = gasStaticTempAtChamber_K;
                continue;
            }

            // Stechman decay. Use per-station G_g when supplied, else the
            // chamber scalar (back-compat path).
            double momFactor = usePerStationG
                ? Math.Pow(Math.Max(gasMassFluxPerStation_kg_m2_s![i], 1.0) / G_f, 0.25)
                : momFactor_chamber;
            double arg = film.DecayCoefficient * (dx * 1e-3) / slotHeight_m * momFactor;
            double baseEta = Math.Exp(-arg);

            // Throat mixing penalty — linearly reduce η once past the
            // start of the converging section (x > 0.8 · x_throat).
            double throatPenalty = 0;
            if (x > 0.8 * x_throat && film.ThroatMixingDegradation > 0)
            {
                double s = Math.Clamp((x - 0.8 * x_throat) / (0.2 * x_throat + 1e-6), 0, 1);
                throatPenalty = film.ThroatMixingDegradation * s;
            }
            eta[i] = Math.Clamp(baseEta * (1.0 - throatPenalty), 0, 1);

            // Rough film bulk T: heats linearly with dx from inlet to
            // mixed-out (gas static T at chamber) over 2·L_burn.
            double heatFrac = Math.Clamp(dx / (2.0 * L_burn), 0, 0.9);
            tFilm[i] = film.FilmInletTemp_K
                     + heatFrac * (gasStaticTempAtChamber_K - film.FilmInletTemp_K);
        }

        if (mFilm0 > 0.25 * totalFuelMassFlow_kgs)
            warnings.Add($"Film flow is {100.0 * film.FuelFractionAsFilm:F0}% of fuel — performance penalty ≥ {100.0 * film.FuelFractionAsFilm * 0.8:F0}% Isp.");

        return new FilmCoolingProfile(
            Effectiveness: eta,
            RemainingMassFlow_kgs: mFilm,
            FilmBulkTemp_K: tFilm,
            TotalFilmMassFlow_kgs: mFilm0,
            Warnings: warnings.ToArray());
    }

    /// <summary>
    /// Blend the core adiabatic wall T with the film temperature using
    /// effectiveness. This is the quantity used in the Bartz heat-flux
    /// balance in place of the original T_aw.
    /// </summary>
    public static double EffectiveRecoveryTemperature(
        double T_aw_core, double T_film, double eta)
        => T_aw_core - eta * (T_aw_core - T_film);

    /// <summary>
    /// Approximate Isp penalty from diverting fuel to film. For gaseous
    /// methane film that fully burns in the chamber, ~80 % of the Isp loss
    /// of pure "unburned" fuel (empirical, Sutton 9e pg 127).
    /// </summary>
    public static double IspPenaltyFraction(double filmFraction_ofFuel, double mixtureRatio)
    {
        // Fraction of TOTAL propellant that is film = film/(1+MR) + 0 (ox)
        double totalMassFrac = filmFraction_ofFuel / (1.0 + mixtureRatio);
        return 0.80 * totalMassFrac;
    }

    /// <summary>
    /// PH-37 (2026-04-29): C* efficiency derate from film-cooling
    /// boundary-layer blockage. Film flow along the wall thickens the
    /// gas-side boundary layer, reducing the effective throat area
    /// (and therefore C*) by ~2–5 % per 10 % film fraction. Closes a
    /// scoring loophole where the SA optimizer could drive
    /// `FilmFuelFraction` arbitrarily high without paying a C*
    /// penalty.
    ///
    /// <para>Formula: <c>η_C*_film = 1 − 0.30 · filmFraction_ofFuel</c>,
    /// clamped to [0.7, 1.0]. The 0.30 coefficient follows the
    /// Stechman / Ewen analysis (boundary-layer blockage scaling
    /// linearly with film mass fraction at small fractions).</para>
    ///
    /// <para>Applied at <c>RegenChamberOptimization.ComputeDerivedValues</c>
    /// alongside <c>cond.CStarEfficiency</c>: both <c>Cstar_eff</c> and
    /// <c>IspVacuum</c> get multiplied by the returned factor, so that
    /// (a) mass flow rises with film fraction (more propellant for the
    /// same thrust) and (b) ideal-Isp falls with film fraction (the
    /// thermodynamic relation <c>Isp = C* · C_F / g₀</c> is preserved).</para>
    /// </summary>
    public static double CStarEfficiencyFactor(double filmFraction_ofFuel)
    {
        if (filmFraction_ofFuel <= 0) return 1.0;
        double f = 1.0 - 0.30 * filmFraction_ofFuel;
        return Math.Clamp(f, 0.7, 1.0);
    }
}
