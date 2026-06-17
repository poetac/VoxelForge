// WallMaterial.cs — Metal AM thrust-chamber wall materials.
//
// All properties are temperature-interpolated between a room-T and a high-T
// anchor; adequate for scoping since chamber walls operate 500–900 K and
// the full thermal-history curves are weakly non-linear in that range.

namespace Voxelforge.HeatTransfer;

public readonly record struct WallMaterial(
    string Name,
    double Density_kgm3,
    double ConductivityCold_WmK,        // at 300 K
    double ConductivityHot_WmK,         // at 900 K
    double SpecificHeat_Jkg,            // approx., weakly T-dependent
    double YieldStrengthCold_MPa,       // σ_y at 300 K
    double YieldStrengthHot_MPa,        // σ_y at 800 K
    double ElasticModulusCold_GPa,      // E at 300 K
    double ElasticModulusHot_GPa,       // E at 800 K
    double CTE_perK,                    // α thermal expansion
    double MaxServiceTemp_K,            // typical upper limit (onset of creep / oxidation)
    double PrintCostPerCm3_USD,
    double MeltingPoint_K,
    string DataSource,                  // provenance — which handbook / vendor card
    string LPBFProcessNote,             // LPBF-specific vs wrought caveat
    string CertificationStatus,         // what qualification level this value is good for
    // Z3-m1 (2026-04-29): bimetallic-stack metadata. When > 0 marks the
    // wall as a series-resistance composite of an inner liner (GRCop-42)
    // and an outer jacket (Inconel 625) at the given liner fraction. The
    // RegenCoolingSolver consults this to evaluate per-layer conductivity
    // at each layer's representative temperature (liner near T_wg, jacket
    // near T_wc) instead of a shared single-T composition. Default 0 =
    // pure-material wall (legacy single-T `ConductivityAt` path).
    double LinerFraction = 0.0)
{
    public double ConductivityAt(double T_K)
        => Lerp(ConductivityCold_WmK, ConductivityHot_WmK, T_K, 300, 900);

    public double YieldStrengthAt_MPa(double T_K)
        => Lerp(YieldStrengthCold_MPa, YieldStrengthHot_MPa, T_K, 300, 800);

    public double ElasticModulusAt_GPa(double T_K)
        => Lerp(ElasticModulusCold_GPa, ElasticModulusHot_GPa, T_K, 300, 800);

    private static double Lerp(double a, double b, double T, double Ta, double Tb)
    {
        if (T <= Ta) return a;
        if (T >= Tb) return b;
        double t = (T - Ta) / (Tb - Ta);
        return a + t * (b - a);
    }
}

public static class WallMaterials
{
    // ─────────────────────────────────────────────────────────────
    //  NOTE: Numbers here are scoping-grade.  For a real chamber
    //  qualifying a material, use vendor-certified material cards
    //  with full T-dependent yield, fatigue, and creep curves.
    // ─────────────────────────────────────────────────────────────

    public static readonly WallMaterial GRCop42 = new(
        Name: "GRCop-42 (NASA Cu-Cr-Nb, LPBF)",
        Density_kgm3: 8756,
        ConductivityCold_WmK: 326,
        ConductivityHot_WmK: 285,
        SpecificHeat_Jkg: 390,
        YieldStrengthCold_MPa: 230,
        YieldStrengthHot_MPa: 180,
        ElasticModulusCold_GPa: 127,
        ElasticModulusHot_GPa: 100,
        CTE_perK: 17.5e-6,
        MaxServiceTemp_K: 1150,     // 877°C — sustained service ceiling; NASA PURS validated cyclic to ~1200 K (~927°C)
        PrintCostPerCm3_USD: 20.0,
        MeltingPoint_K: 1350,
        DataSource: "NASA TM-2019-220217 (GRCop-42 Process & Properties); Elementum 3D vendor card; NASA PURS cyclic tests",
        LPBFProcessNote: "LPBF process map calibrated by NASA MSFC. HIP 1000°C/100 MPa/4h required for full density. Anisotropy typically < 10%.",
        CertificationStatus: "Preliminary design. For flight qualification: use vendor-certified material cards with LCF/HCF curves.");

    public static readonly WallMaterial CuCrZr = new(
        Name: "CuCrZr (C18150, LPBF)",
        Density_kgm3: 8900,
        ConductivityCold_WmK: 320,
        ConductivityHot_WmK: 260,
        SpecificHeat_Jkg: 385,
        // PH-32 (#181, 2026-04-29): yield anchors revised to LPBF-derated
        // values, not wrought. Pre-PH-32 values (350 / 200 MPa) were
        // wrought C18150 numbers; the file's own LPBFProcessNote already
        // flagged "LPBF retains ~70% of wrought yield" but the anchors
        // ignored their own derate. Brush Wellman C18150 wrought datasheet
        // gives ~360 MPa @ 25 °C and ~100 MPa @ 600 °C (873 K); LPBF post-
        // age at 70 % of wrought lands at ~252 MPa @ 25 °C and ~70 MPa @
        // 600 °C. NASA PURS as-built CuCrZr coupon data corroborates:
        // 280 MPa @ 25 °C and 100 MPa @ 527 °C (800 K = the MaxServiceTemp).
        // Bartz / regen scoring on CuCrZr-heavy designs (Merlin / aerospike)
        // saw ~2× over-credit on hot σ_y and ~1.25× over-credit on cold;
        // PH-32 closes the gap. Compounds with the A1-follow-on
        // bimetallic composite-yield change which stacked on top.
        YieldStrengthCold_MPa: 280,
        YieldStrengthHot_MPa: 100,
        ElasticModulusCold_GPa: 128,
        ElasticModulusHot_GPa: 95,
        CTE_perK: 17.0e-6,
        MaxServiceTemp_K: 800,
        PrintCostPerCm3_USD: 14.0,
        MeltingPoint_K: 1345,
        DataSource: "Brush Wellman C18150 wrought datasheet (≈360 MPa @ 25 °C, ≈100 MPa @ 600 °C); NASA PURS LPBF as-built coupon data; LPBF delta per Fraunhofer ILT (Wegener et al. 2020). PH-32 (#181, 2026-04-29) anchored σ_y array to 70 % of wrought (LPBF derate per the file's own LPBFProcessNote).",
        LPBFProcessNote: "LPBF C18150 retains ~85% of wrought conductivity, ~70% of wrought yield. Yield anchors (280 / 100 MPa) reflect that derate; pre-PH-32 (350 / 200) read wrought values into LPBF slots. Green-laser or IR with absorption enhancer.",
        CertificationStatus: "Preliminary design. Strength values now anchored to NASA PURS / Brush Wellman LPBF data at 70 % of wrought; validate on as-built coupons before flight qualification.");

    public static readonly WallMaterial Inconel625 = new(
        Name: "Inconel 625 (LPBF)",
        Density_kgm3: 8440,
        ConductivityCold_WmK: 10.0,
        ConductivityHot_WmK: 19.0,
        SpecificHeat_Jkg: 490,
        YieldStrengthCold_MPa: 520,
        YieldStrengthHot_MPa: 450,
        ElasticModulusCold_GPa: 208,
        ElasticModulusHot_GPa: 165,
        CTE_perK: 12.8e-6,
        MaxServiceTemp_K: 1250,
        PrintCostPerCm3_USD: 8.0,
        MeltingPoint_K: 1570,
        DataSource: "Special Metals Inc. IN625 mill cert; LPBF delta per Dinda et al. 2009",
        LPBFProcessNote: "LPBF IN625 matches wrought within ±5% post-HIP (1185°C/100 MPa/4h) + solution anneal. Widely qualified.",
        CertificationStatus: "Industry-standard LPBF alloy. Hundreds of flight certifications (e.g. NASA RS-25, Relativity Aeon).");

    public static readonly WallMaterial Inconel718 = new(
        Name: "Inconel 718 (LPBF)",
        Density_kgm3: 8220,
        ConductivityCold_WmK: 11.4,
        ConductivityHot_WmK: 21.0,
        SpecificHeat_Jkg: 435,
        YieldStrengthCold_MPa: 1100,
        YieldStrengthHot_MPa: 950,
        ElasticModulusCold_GPa: 205,
        ElasticModulusHot_GPa: 160,
        CTE_perK: 13.0e-6,
        MaxServiceTemp_K: 973,
        PrintCostPerCm3_USD: 9.0,
        MeltingPoint_K: 1610,
        DataSource: "AMS 5383 IN718 (wrought); LPBF delta per NIST TN-2055 (Slotwinski 2019)",
        LPBFProcessNote: "LPBF IN718 needs HIP 1165°C/103 MPa/3h, solution 968°C/1h AC, double aging 720°C/8h + 620°C/8h FC. As-built strength 20–30% below wrought.",
        CertificationStatus: "Heritage superalloy with robust LPBF process maps.");

    /// <summary>
    /// Bimetallic Inconel-625-jacket over a thin GRCop-42 inner liner.
    /// Production-LRE-class wall for high-Pc LOX/CH4, LOX/RP-1, and
    /// LOX/H2 chambers — combines the gas-side conductivity of the
    /// copper alloy with the high-T strength of the nickel superalloy.
    ///
    /// **A1 / ID-5 (2026-04-27):** properties revised from area-weighted
    /// blends (which over-credited the high-conductivity / high-strength
    /// layer) to physically-correct composite properties:
    ///
    ///   • **Conductivity:** SERIES resistance (heat must pass through
    ///     both layers), not parallel blend. The lower-k IN625 jacket
    ///     dominates the effective k_eff. Pre-A1 used parallel blend
    ///     (~263 W/m·K cold) which overstated effective conductivity by
    ///     ~20×; post-A1 uses series (1 / (t_liner/k_GRCop + t_jacket/k_IN625)),
    ///     yielding ~13 W/m·K cold at the default 25/75 ratio.
    ///   • **Yield strength:** MIN of the two layers' yields (worst
    ///     ply governs structural failure). Pre-A1 used 80/20 blend
    ///     biased to IN625 (~462 MPa cold); post-A1 uses min (230 MPa
    ///     cold = GRCop-42's). The bond zone is in practice the
    ///     weakest section but data is sparse; min-of-bulk is the
    ///     conservative bound.
    ///   • **Elastic modulus:** PARALLEL / Voigt (bonded composite-
    ///     cylinder under hoop tension has uniform circumferential
    ///     strain, so layers carry load in parallel — force balance
    ///     P·r = ε_θ·(E_liner·t_liner + E_jacket·t_jacket) gives
    ///     E_eff = f_liner·E_liner + f_jacket·E_jacket). NOTE this is
    ///     the OPPOSITE pattern to conductivity: heat flow is normal
    ///     to the wall (resistances stack in series), but hoop strain
    ///     is along the wall (stiffnesses act in parallel). Z1
    ///     hot-fix (post-#107) corrected this from series to parallel.
    ///   • **CTE / density / cost / specific heat:** kept as area-
    ///     weighted blends — these are not strongly composition-
    ///     dependent in the same way; weighted blend is a reasonable
    ///     bulk approximation.
    ///
    /// **Z2 #10 (2026-04-29):** the 25 % liner / 75 % jacket ratio is
    /// no longer hardcoded — pass <paramref name="linerFraction"/> to
    /// model designs with thinner or thicker liners (e.g., 0.20 for a
    /// thin SuperDraco-class Cu liner; 0.40 for a heavier-liner Merlin-
    /// MCC-class wall). All composite properties (k, σ_y composition
    /// blend, E, ρ, Cp, α, cost, melting point) are recomputed at the
    /// supplied ratio. Default 0.25 preserves the historical pre-Z2.10
    /// behaviour bit-identically.
    ///
    /// **Not yet modelled.** Bond-zone shear stress from the CTE
    /// mismatch (α_GRCop ≈ 17.5e-6 vs α_IN625 ≈ 12.8e-6 → bond-zone
    /// thermal-cycling shear). Tracked as ID-5 follow-on; would need a
    /// new stress component in `StructuralCheck`.
    ///
    /// References:
    ///   • SSME MCC: Cu-Ag-Zr inner / IN625 jacket (functionally similar).
    ///   • Merlin-1D MCC: bimetallic copper-alloy / Inconel composite per
    ///     SpaceX FAA filings + Aerojet Rocketdyne white papers.
    ///   • NASA TM-2017-219670 "Bimetallic Bond LPBF Process" — process
    ///     maps for direct LPBF deposition of IN625 onto GRCop-84.
    ///   • Hibbeler "Mechanics of Materials" §8.3 — composite-cylinder
    ///     thermal + structural analysis (series-resistance derivation).
    /// </summary>
    /// <param name="linerFraction">Fraction of total wall thickness
    /// occupied by the GRCop-42 inner liner. Must be in (0, 1).
    /// Default 0.25 matches the historical pre-Z2.10 ratio.</param>
    public static WallMaterial GRCop42_Inconel625(double linerFraction = 0.25)
    {
        if (linerFraction <= 0.0 || linerFraction >= 1.0)
            throw new System.ArgumentOutOfRangeException(
                nameof(linerFraction),
                $"linerFraction must be in (0, 1); got {linerFraction}.");
        double jacketFraction = 1.0 - linerFraction;
        return new WallMaterial(
            Name: "GRCop-42 / Inconel-625 bimetallic (LPBF)",
            Density_kgm3: (int)(linerFraction * 8756 + jacketFraction * 8440),
            // Conductivity: series resistance.
            //   k_eff_cold = 1 / (linerFraction/326 + jacketFraction/10.0)
            //   k_eff_hot  = 1 / (linerFraction/285 + jacketFraction/19.0)
            ConductivityCold_WmK: 1.0 / (linerFraction / 326.0 + jacketFraction / 10.0),
            ConductivityHot_WmK:  1.0 / (linerFraction / 285.0 + jacketFraction / 19.0),
            SpecificHeat_Jkg: (int)(linerFraction * 390 + jacketFraction * 490),
            // Yield strength: min of the two bulk layers (worst ply governs).
            // GRCop-42's 230 MPa cold and 180 MPa hot are below IN625's
            // 520 / 450 MPa, so GRCop-42 sets the floor regardless of fraction.
            YieldStrengthCold_MPa: System.Math.Min(230.0, 520.0),
            YieldStrengthHot_MPa:  System.Math.Min(180.0, 450.0),
            // Elastic modulus: parallel / Voigt.
            //   E_eff_cold = linerFraction·127 + jacketFraction·208
            //   E_eff_hot  = linerFraction·100 + jacketFraction·165
            ElasticModulusCold_GPa: linerFraction * 127.0 + jacketFraction * 208.0,
            ElasticModulusHot_GPa:  linerFraction * 100.0 + jacketFraction * 165.0,
            CTE_perK: linerFraction * 17.5e-6 + jacketFraction * 12.8e-6,
            MaxServiceTemp_K: 1150,                            // gas-side GRCop-42 liner limits (NASA PURS cyclic to ~1200 K; 1150 K = sustained service ceiling, well below IN625 jacket limit 1250 K)
            PrintCostPerCm3_USD: linerFraction * 20.0 + jacketFraction * 8.0,
            MeltingPoint_K: (int)(linerFraction * 1350 + jacketFraction * 1570),
            DataSource: "Bimetallic blend of GRCop-42 (NASA TM-2019-220217) + Inconel-625 (Special Metals); LPBF process per NASA TM-2017-219670. Conductivity in series + E in parallel (Voigt) per Hibbeler §8.3.",
            LPBFProcessNote: "Bimetallic LPBF: copper liner printed first, IN625 deposited on top with controlled-melt-pool transition. Bond strength qualified at NASA MSFC. HIP 1100°C/100 MPa/4h required for both materials simultaneously.",
            CertificationStatus: "Production-class LRE wall material — analogue of SSME MCC + Merlin-1D MCC. A1/ID-5 + Z1 hot-fix + Z2 #10 (2026-04-27/28/29): conductivity in series, E in parallel, yield = min(layers); composition ratio is a method parameter (default 0.25). Use vendor mill certs for flight qualification.",
            // Z3-m1 (2026-04-29): mark as bimetallic so RegenCoolingSolver
            // can evaluate per-layer conductivity at each layer's own T
            // (liner at T_wg, jacket at T_wc) instead of a uniform-T
            // composition. The single-T `ConductivityCold/HotWmK` anchors
            // above remain valid as a fallback for callers (e.g. proof-test
            // analysis, structural code) that don't have separate layer
            // temperatures.
            LinerFraction: linerFraction);
    }

    public static readonly WallMaterial[] All = {
        GRCop42, CuCrZr, Inconel625, Inconel718, GRCop42_Inconel625()
    };
}
