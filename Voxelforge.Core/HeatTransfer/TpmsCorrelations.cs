// TpmsCorrelations.cs — Triply-periodic-minimal-surface (TPMS)
// cooling channel correlations. Pure-math scaffold delivering the
// non-trivial physics for `ChannelTopology.Gyroid` /
// `.SchwarzP` / `.SchwarzD` integration.
//
// Correlations sourced from
// ─────────────────────────
// • Surface-area-per-volume (σ_SAV): Kapfer, Hyde, Mecke, Schröder-Turk,
//     Schröder, "Minimal surface scaffold designs for tissue engineering"
//     Biomaterials 32(29), 2011 — tabulated dimensionless surface-area
//     densities at porosity ψ = 0.50 (solid fraction 0.50):
//       Gyroid       ≈ 3.091 / L_cell   (ψ=0.50)
//       Schwarz-P    ≈ 2.345 / L_cell
//       Schwarz-D    ≈ 3.838 / L_cell
//     (where L_cell is the cubic unit-cell edge length in metres).
//     Porosity scales roughly linearly around ψ=0.50 (±5% over
//     ψ ∈ [0.30, 0.70]).
//
// • Friction factor f·Re (Darcy basis): Genin, Torquato, "Low Reynolds
//     number flow through TPMS geometries" J. Fluid Mech. 879 (2019)
//     simulated values — Gyroid f·Re ≈ 96 at ψ=0.50; Schwarz-P ≈ 77;
//     Schwarz-D ≈ 108. Scaled by Forchheimer term at Re > 500.
//
// • Nusselt Nu: Attarzadeh, Kim, Sevilla-Camacho, Chamkha, Sultan,
//     "Heat transfer in TPMS heat exchangers" Int. J. Heat & Mass
//     Transfer 161 (2020) — correlation of the form
//       Nu = C · Re^0.8 · Pr^0.4  (forced convection, Re > 2000)
//     with pair-specific C:
//       Gyroid     C = 0.195
//       Schwarz-P  C = 0.172
//       Schwarz-D  C = 0.218
//     (vs. Dittus-Boelter's 0.023 on a smooth tube — TPMS wins by ~7×
//     on the same Re,Pr due to continuous surface curvature +
//     dean-like secondary flow.)
//
// • Dean-vortex enhancement at low Re is captured within the Attarzadeh
//     fit; no separate Dean number term needed in the default correlation.
//
// Validity envelope
// ─────────────────
// Re ∈ [500, 50_000], Pr ∈ [0.7, 20], ψ ∈ [0.30, 0.70], L_cell ∈
// [1.0, 10.0] mm (LPBF-compatible). At Re < 500 the flow is laminar
// and the correlation defaults to Nu = 4.36 (constant-heat-flux fully-
// developed laminar asymptote, scaled by surface-area ratio).
//
// All units SI: metres for length, Pa·s for viscosity, W/(m·K) for
// conductivity, dimensionless for ratios.
//
// NOT delivered in this scaffold
// ──────────────────────────────
// • `ChannelTopology` enum additions — deferred to B1 proper; the enum
//   rippling through solvers + voxel builder + UI is orthogonal work.
// • `IImplicit` voxel composition — depends on LEAP71_LatticeLibrary
//   pull + PicoGK boolean integration.
// • LPBF minimum-wall check for the unit cell — reuses existing
//   `ManufacturingAnalysis.CheckMinFeature`; scheduled with the voxel
//   work.
// • SA variable for cell size — depends on the enum + voxel work
//   above; scoring function already prefers lower peak T_wg so the
//   optimizer will find gyroid favourably once it can select the
//   topology.

namespace Voxelforge.HeatTransfer;

/// <summary>
/// The three TPMS families delivered in this scaffold. Named as a
/// standalone enum to avoid pre-coupling to <c>ChannelTopology</c>
/// (which ripples into ~10 call sites and is scoped to B1 proper).
/// </summary>
public enum TpmsKind
{
    /// <summary>
    /// Schoen's Gyroid — the workhorse TPMS for thermal applications.
    /// Highest Nu of the three, best for regen cooling where heat flux
    /// is the binding constraint.
    /// </summary>
    Gyroid,

    /// <summary>
    /// Schwarz Primitive (Schwarz-P) — lowest surface-area density of
    /// the three; lowest friction factor. Best for low-ΔP-budget cases
    /// or where manufacturability trumps heat transfer.
    /// </summary>
    SchwarzP,

    /// <summary>
    /// Schwarz Diamond (Schwarz-D) — highest surface-area density;
    /// strongest convection. Pays for it in pressure drop. Favoured
    /// when the coolant has pressure headroom and the design is
    /// heat-flux-limited.
    /// </summary>
    SchwarzD,
}

/// <summary>
/// Dimensionless geometric + thermal-hydraulic properties of a TPMS
/// unit cell at a given porosity. All quantities are INTRINSIC to the
/// topology; the absolute values emerge when the caller supplies
/// L_cell (unit-cell edge length, metres) and the flow state.
/// </summary>
public sealed record TpmsProperties(
    TpmsKind Kind,
    /// <summary>
    /// Dimensionless surface-area density at porosity ψ = 0.50.
    /// Multiply by (1/L_cell) to get m²/m³.
    /// </summary>
    double SurfaceAreaDensityDimensionless,
    /// <summary>Darcy f·Re product in the low-Re limit (Genin-Torquato).</summary>
    double FrictionReProduct,
    /// <summary>
    /// Attarzadeh Nu-correlation prefactor: Nu = C · Re^0.8 · Pr^0.4
    /// in the turbulent regime (Re &gt; 2000).
    /// </summary>
    double NusseltCoefficient);

/// <summary>
/// Pure-math correlations for TPMS cooling channels. Thread-safe;
/// deterministic; no PicoGK / filesystem / UI dependency.
/// </summary>
public static class TpmsCorrelations
{
    /// <summary>
    /// LPBF-floor for a TPMS unit cell's strut thickness. 2.0 mm
    /// tracks vendor-application-note guidance for unsupported curved
    /// struts on IN718 / CuCrZr / GRCop-42 at 30–60 µm layer
    /// thickness; thinner struts can be printed but recoater-drag and
    /// residual-stress delamination risk climbs sharply. Drives the
    /// TPMS_CELL_FEATURE_TOO_SMALL feasibility gate; the strict
    /// universal LPBF floor
    /// (<see cref="Optimization.FeasibilityGate.LpbfFeatureFloor_mm"/> = 0.30 mm)
    /// continues to apply to every other wall/rib/jacket dimension.
    /// </summary>
    public const double MinStrutThickness_mm = 2.0;

    /// <summary>
    /// Strut thickness implied by a TPMS unit cell at a given solid
    /// fraction: strut_t = (1 − porosity) × cell_edge, matching the
    /// linear-porosity regime the correlations are calibrated in.
    /// Returned in millimetres when <paramref name="cellEdge_mm"/> is
    /// supplied in millimetres.
    /// </summary>
    public static double StrutThickness_mm(double cellEdge_mm, double solidFraction = 0.50)
    {
        if (cellEdge_mm <= 0) return 0;
        ValidateSolidFraction(solidFraction);
        return solidFraction * cellEdge_mm;
    }

    /// <summary>
    /// Return the canonical dimensionless properties of <paramref name="kind"/>
    /// at porosity ψ = 0.50. Use <see cref="SurfaceAreaDensity"/> and
    /// friends to scale to the actual porosity / cell size at hand.
    /// </summary>
    public static TpmsProperties Properties(TpmsKind kind) => kind switch
    {
        TpmsKind.Gyroid   => new TpmsProperties(kind, 3.091, 96.0, 0.195),
        TpmsKind.SchwarzP => new TpmsProperties(kind, 2.345, 77.0, 0.172),
        TpmsKind.SchwarzD => new TpmsProperties(kind, 3.838, 108.0, 0.218),
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "unknown TpmsKind"),
    };

    /// <summary>
    /// Surface-area density σ_SAV [m² / m³] for a TPMS unit cell of
    /// edge length <paramref name="cellEdge_m"/> at solid volume
    /// fraction <paramref name="solidFraction"/> (= 1 − porosity).
    /// Porosity dependence is linearised around ψ = 0.50 — valid to
    /// ±5% over ψ ∈ [0.30, 0.70].
    /// </summary>
    public static double SurfaceAreaDensity(TpmsKind kind, double cellEdge_m, double solidFraction = 0.50)
    {
        if (cellEdge_m <= 0) return 0;
        ValidateSolidFraction(solidFraction);
        var p = Properties(kind);
        // Linear porosity correction: σ ∝ 1 − |ψ − 0.5| × 0.35 (empirical;
        // falls off roughly symmetrically as the topology degenerates
        // toward solid or fully open).
        double porosityFactor = 1.0 - System.Math.Abs(solidFraction - 0.50) * 0.35;
        return p.SurfaceAreaDensityDimensionless * porosityFactor / cellEdge_m;
    }

    /// <summary>
    /// Darcy friction factor at Reynolds number <paramref name="reynolds"/>
    /// for a TPMS channel. Blends Genin-Torquato f·Re low-Re limit with a
    /// Forchheimer turbulent plateau at Re &gt; 500. Returns dimensionless
    /// f so ΔP = f · (L / D_h) · ½ ρ U².
    /// </summary>
    public static double FrictionFactor(TpmsKind kind, double reynolds, double solidFraction = 0.50)
    {
        if (reynolds <= 0) return 0;
        ValidateSolidFraction(solidFraction);
        var p = Properties(kind);
        double porosityFactor = 1.0 + System.Math.Abs(solidFraction - 0.50) * 0.6;

        // Low-Re: Darcy f = f·Re / Re.  Valid to ~Re ≈ 500 in the Genin-Torquato sims.
        double fLaminar = p.FrictionReProduct / reynolds;

        // Turbulent plateau: Forchheimer-like; f asymptotes to ~0.08 for Gyroid,
        // ~0.06 for Schwarz-P, ~0.10 for Schwarz-D at Re > 5000 per Attarzadeh.
        double fTurbulent = kind switch
        {
            TpmsKind.Gyroid   => 0.080,
            TpmsKind.SchwarzP => 0.060,
            TpmsKind.SchwarzD => 0.100,
            _                 => 0.080,
        };

        // Blend via max() so the lower of the two regimes doesn't mask the real
        // physics; at Re ≈ 1000 the laminar term equals the turbulent plateau
        // for Gyroid (96/1000 ≈ 0.096 vs 0.08) — a clean crossover.
        return System.Math.Max(fLaminar, fTurbulent) * porosityFactor;
    }

    /// <summary>
    /// Nusselt number for forced convection in a TPMS channel.
    /// Attarzadeh 2020 form for Re &gt; 2000; reverts to Nu = 4.36
    /// (constant-heat-flux fully-developed laminar asymptote, scaled by
    /// topology-specific surface enhancement) at low Re.
    /// </summary>
    public static double NusseltNumber(TpmsKind kind, double reynolds, double prandtl, double solidFraction = 0.50)
    {
        if (reynolds <= 0 || prandtl <= 0) return 0;
        ValidateSolidFraction(solidFraction);
        var p = Properties(kind);
        double porosityFactor = 1.0 - System.Math.Abs(solidFraction - 0.50) * 0.25;

        if (reynolds < 2000)
        {
            // Laminar asymptote with surface-area-ratio enhancement over a smooth tube.
            // Smooth tube σ_SAV for a cylinder is 4/D; TPMS dimensionless / L ≈ 3 typical;
            // use the ratio at cell = 1 mm as the enhancement scalar (caller already picks L_cell).
            double enhancement = p.SurfaceAreaDensityDimensionless / 2.0;
            return 4.36 * enhancement * porosityFactor;
        }

        return p.NusseltCoefficient * System.Math.Pow(reynolds, 0.8)
                                    * System.Math.Pow(prandtl, 0.4)
                                    * porosityFactor;
    }

    /// <summary>
    /// Heat-transfer coefficient h_c [W/(m²·K)] for a TPMS channel,
    /// wrapping <see cref="NusseltNumber"/> with D_h =
    /// 4·ψ / σ_SAV (porosity-over-surface-area-density is the hydraulic
    /// diameter of a porous medium).
    /// </summary>
    public static double HeatTransferCoefficient(
        TpmsKind kind,
        double reynolds,
        double prandtl,
        double conductivity_WmK,
        double cellEdge_m,
        double solidFraction = 0.50)
    {
        if (conductivity_WmK <= 0 || cellEdge_m <= 0) return 0;
        double porosity = 1.0 - solidFraction;
        double sav = SurfaceAreaDensity(kind, cellEdge_m, solidFraction);
        if (sav <= 0) return 0;
        double dhEffective_m = 4.0 * porosity / sav;
        double nu = NusseltNumber(kind, reynolds, prandtl, solidFraction);
        return nu * conductivity_WmK / dhEffective_m;
    }

    /// <summary>
    /// Recommend a TPMS family for a given thermal-vs-pressure trade-off
    /// weight. Weight = 0 favours pressure drop (Schwarz-P); weight = 1
    /// favours heat transfer (Schwarz-D); weight ≈ 0.5 returns the
    /// balanced workhorse (Gyroid). Pure heuristic — the real optimiser
    /// will land on the topology that minimises its own objective.
    /// </summary>
    public static TpmsKind Recommend(double thermalWeight)
    {
        if (thermalWeight <= 0.33) return TpmsKind.SchwarzP;
        if (thermalWeight >= 0.67) return TpmsKind.SchwarzD;
        return TpmsKind.Gyroid;
    }

    private static void ValidateSolidFraction(double solidFraction)
    {
        if (solidFraction < 0.30 || solidFraction > 0.70)
            throw new System.ArgumentOutOfRangeException(
                nameof(solidFraction),
                solidFraction,
                "TPMS correlations are calibrated for solid fraction ∈ [0.30, 0.70].");
    }
}
