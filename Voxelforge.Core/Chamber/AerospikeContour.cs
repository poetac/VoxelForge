// AerospikeContour.cs — Parametric aerospike / plug-nozzle contour generator.
//
// Problem this solves
// ───────────────────
// A conventional bell nozzle separates its external-expansion flow from
// the ambient atmosphere with a physical wall, so the exit-plane area
// ratio fixes the nozzle's optimum altitude. An aerospike (plug or
// spike) nozzle replaces the outer wall with the ambient air itself —
// the plume expands against the atmosphere along the plug's outer
// surface. The nozzle automatically "shortens" at low altitude (shock
// sits mid-plug) and "lengthens" at high altitude (shock slides off the
// truncation), giving near-ideal C_F over a 0 → vacuum altitude sweep.
// This is the "altitude compensation" headline advantage that's been
// pulled into the public conversation by recent altitude-compensating
// demos.
//
// Two design flavours
// ───────────────────
//   • Full spike — plug tapers to a point at the tip. Maximum C_F at
//     vacuum; longest part; hardest to print (sharp tip is the thinnest
//     feature and sits in the worst thermal environment).
//   • Truncated plug — plug is cut off at `PlugLengthRatio × L_full`.
//     Typical 0.20–0.40 truncation. Trades ~1% C_F for ~60% shorter
//     hardware, easier cooling, flat base you can mount a plate on.
//
// Parametric method (Angelino 1964 approximation)
// ───────────────────────────────────────────────
// The "Angelino approximation" (Angelino, "Approximate Method for Plug
// Nozzle Design", AIAA Journal 2(10), 1964) gives the plug profile via
// Prandtl-Meyer turning from the throat Mach number (=1 at sonic) to
// the design exit Mach number:
//
//   ν(M) = √((γ+1)/(γ-1)) · atan(√((γ-1)/(γ+1) · (M²-1)))
//          − atan(√(M²-1))                  (Prandtl-Meyer function)
//
// At every local Mach number M along the plug, the local area ratio
// A(M)/A_throat = (1/M)·[(2+(γ-1)M²)/(γ+1)]^((γ+1)/(2(γ-1))), and the
// local plug radius tapers so the characteristic through the throat
// tip lands on the plug at the flow angle φ = ν_exit − ν(M).
//
// In Angelino's approximation (valid for conical-cross-section plugs
// to engineering tolerance ±2% on C_F), the plug is parametrically
//     x(M) = L · (ν_e − ν(M)) / ν_e        (axial position, 0 at throat tip)
//     r(M) = R_t · (1 − (x(M)/L) · cos(θ))  simplified inner-plug taper
// where L is the full-spike length, θ the effective turning angle.
//
// This file implements the Angelino method directly. For annular-throat
// geometry, the outer throat lip is placed at the nozzle-outlet cowl
// radius R_o and the inner plug nose at R_i — the annular throat area
// is A_t = π(R_o² − R_i²).
//
// Simplifications retained
// ────────────────────────
//   • Conical-plug approximation (Angelino), NOT full method-of-
//     characteristics. Accurate to ±2 % on C_F at ε ∈ [5, 30].
//   • No plume-merge correction — we assume the plume separates cleanly
//     from the plug at the truncation for the purposes of radius-vs-x.
//   • Single expansion ratio — the aerospike's altitude-compensation
//     behaviour is a downstream fluid-dynamics consequence of geometry,
//     not an input parameter.
//
// Units: millimetres for length, radians for angles, dimensionless
// ratios. All record fields are doubles.

namespace Voxelforge.Chamber;

/// <summary>
/// One axial station of the aerospike contour. An aerospike contour
/// carries <b>two</b> radii per station (inner plug surface + optional
/// outer cowl surface) unlike <see cref="ContourStation"/> which only
/// carries one. Outer radius is <see cref="double.NaN"/> beyond the
/// cowl's axial extent (plug surface exposed to free stream).
/// </summary>
public readonly record struct AerospikeStation(
    double X_mm,                  // axial position, 0 = throat lip plane
    double R_inner_mm,             // local plug radius (inner)
    double R_outer_mm,             // local cowl radius (NaN if no cowl)
    double FlowAngle_rad,          // plug-surface slope angle from axis
    AerospikeRegion Region);

public enum AerospikeRegion
{
    Throat,             // at the annular-throat lip plane (x = 0)
    PlugExpansion,      // along the contoured expansion surface
    PlugTruncation,     // flat base cut at PlugLengthRatio × L_full
    Cowl,               // short outer cowl around the throat
}

/// <summary>
/// Full aerospike contour as an ordered set of axial stations plus
/// the summary geometry the downstream solver / voxel builder consume.
/// </summary>
public sealed record AerospikeContour(
    AerospikeStation[] Stations,
    int ThroatIndex,                   // index of the throat-lip station
    double ThroatAnnulusArea_mm2,      // A_t = π(R_o² − R_i²) axisymmetric; 2·h·W for linear slots
    double ThroatOuterRadius_mm,       // R_o at throat lip (axisymmetric) / plug half-height (linear)
    double ThroatInnerRadius_mm,       // R_i at throat lip (plug nose; 0 for linear)
    double ExitArea_mm2,               // A_e on the truncated-plug base
    double ExpansionRatio,             // A_e / A_t
    double PlugFullLength_mm,          // L_full — where the spike would taper to a point
    double PlugTruncatedLength_mm,     // PlugLengthRatio × PlugFullLength_mm
    double PlugLengthRatio,            // 0.20 – 1.0; < 1.0 = truncated
    double CowlLength_mm,              // 0 when no cowl
    double CowlOuterRadius_mm,         // at cowl mouth (= ThroatOuterRadius_mm + cowl flare)
    double Gamma,                       // specific-heat ratio used in Prandtl-Meyer
    double DesignExitMach)              // M_e consistent with ExpansionRatio
{
    // ── Sprint 26 (2026-04-23) — linear aerospike extensions ──────
    //
    // The Angelino 2D expansion curve is identical whether revolved
    // (classic axisymmetric plug, Sprints 1-15) or extruded along a
    // transverse axis (X-33 / XRS-2200 lineage). The difference lives
    // entirely in the downstream voxelisation / thermal / feasibility
    // path: axisymmetric treats Stations[i].R_inner_mm as revolve
    // radius; linear treats it as the half-height of a rectangular
    // plug cross-section at that station. Two surfaces are cooled
    // (top + bottom) rather than a single wrapped 2π·r surface.
    //
    // Three init-only properties suffice — no new positional record
    // parameter, so every existing construction call stays valid and
    // non-linear callers can ignore the fields entirely.

    /// <summary>
    /// Sprint 26: true when this contour represents a linear (extruded-
    /// rectangular) aerospike plug rather than the classic axisymmetric
    /// (revolved) plug. Defaults to false; constructors set it to true
    /// in <see cref="LinearAerospikeContourGenerator.Generate"/>.
    /// Downstream code (voxel builder, thermal solver, feasibility
    /// gate) branches on this flag.
    /// </summary>
    public bool IsLinear { get; init; } = false;

    /// <summary>
    /// Sprint 26: transverse extrusion width (mm) of a linear plug.
    /// The plug's rectangular cross-section is <c>(2 × R_inner_mm) ×
    /// PlugWidth_mm</c> at every axial station. Zero on axisymmetric
    /// contours (the <see cref="IsLinear"/> flag gates every read).
    /// </summary>
    public double PlugWidth_mm { get; init; } = 0.0;

    /// <summary>
    /// Sprint 26: design aspect ratio of the linear plug, defined as
    /// <c>PlugTruncatedLength_mm / PlugWidth_mm</c>. Drives the
    /// <c>LINEAR_AEROSPIKE_ASPECT_RATIO</c> feasibility gate which
    /// fires outside the [0.3, 5.0] band documented in the X-33
    /// XRS-2200 programme ("side-wall recirculation failure mode" when
    /// the plug is too stubby, "print-envelope overflow + unacceptable
    /// bending stiffness" when too slender). Zero on axisymmetric
    /// contours. <see cref="IsLinear"/> gates consumption.
    /// </summary>
    public double LinearAspectRatio { get; init; } = 0.0;

    /// <summary>Convenience: total axial length (throat plane → plug base).</summary>
    public double TotalLength_mm => PlugTruncatedLength_mm;

    /// <summary>Convenience: plug base radius (the "annulus" of the truncated plug).</summary>
    public double PlugBaseRadius_mm => Stations[^1].R_inner_mm;

    /// <summary>
    /// Nearest-station index for a given axial position. Mirrors
    /// <see cref="ChamberContour.StationAt"/> — used by CFD field export
    /// and any downstream tool that needs to look up per-station data
    /// (wall T, heat flux, etc.) at an arbitrary x. Sprint 14 / Track I /
    /// P4: O(log N) binary search on the monotonically-increasing
    /// <c>Stations[i].X_mm</c> sequence; previously O(N) linear scan.
    /// </summary>
    public int StationAt(double x_mm)
    {
        int n = Stations.Length;
        if (n == 0) return 0;

        int lo = 0, hi = n - 1;
        if (x_mm <= Stations[lo].X_mm) return lo;
        if (x_mm >= Stations[hi].X_mm) return hi;

        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (Stations[mid].X_mm <= x_mm) lo = mid;
            else                            hi = mid;
        }

        // Pick the nearer of the two bracketing stations to match the
        // pre-P4 nearest-station semantics exactly.
        return (x_mm - Stations[lo].X_mm) <= (Stations[hi].X_mm - x_mm) ? lo : hi;
    }

    /// <summary>
    /// Trapezoidal segment length at station index <paramref name="i"/>
    /// (halfway to neighbours). Mirrors
    /// <see cref="ChamberContour.SegmentLength_mm"/>; used by the
    /// plug-cooling solver to integrate heat-uptake over the station
    /// span.
    /// </summary>
    public double SegmentLengthApprox_mm(int i)
    {
        if (Stations.Length < 2) return 0;
        if (i == 0) return (Stations[1].X_mm - Stations[0].X_mm) * 0.5;
        if (i == Stations.Length - 1)
            return (Stations[i].X_mm - Stations[i - 1].X_mm) * 0.5;
        return (Stations[i + 1].X_mm - Stations[i - 1].X_mm) * 0.5;
    }
}

/// <summary>
/// Pure-math aerospike contour generator. Deterministic; thread-safe;
/// no PicoGK / filesystem dependency.
/// </summary>
public static class AerospikeContourGenerator
{
    /// <summary>
    /// Minimum plug-length ratio (below this the plug is functionally a
    /// bluff-base disc and the Angelino method loses physical meaning).
    /// </summary>
    public const double MinPlugLengthRatio = 0.15;

    /// <summary>Maximum plug-length ratio = full spike.</summary>
    public const double MaxPlugLengthRatio = 1.00;

    /// <summary>
    /// Build an aerospike contour from four spec inputs. All are SI-ish
    /// (throat radius in mm, area ratio dimensionless, ψ of γ).
    /// </summary>
    /// <param name="throatOuterRadius_mm">R_o at the throat lip (the outer-ring throat radius).</param>
    /// <param name="expansionRatio">A_e / A_t on the truncated-plug base.</param>
    /// <param name="plugLengthRatio">Truncation fraction (0.15–1.00). 1.0 = full spike.</param>
    /// <param name="gamma">Specific-heat ratio of the chamber gas. LOX/CH4 ≈ 1.15.</param>
    /// <param name="stationCount">Number of axial stations along the plug. Default 80.</param>
    /// <param name="includeCowl">Whether to generate a short outer cowl around the throat.</param>
    public static AerospikeContour Generate(
        double throatOuterRadius_mm,
        double expansionRatio,
        double plugLengthRatio = 0.30,
        double gamma = 1.15,
        int stationCount = 80,
        bool includeCowl = true)
    {
        if (throatOuterRadius_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(throatOuterRadius_mm),
                "throat outer radius must be positive");
        if (expansionRatio < 1.5)
            throw new System.ArgumentOutOfRangeException(nameof(expansionRatio),
                "expansion ratio must be ≥ 1.5 (otherwise no plug to contour)");
        if (plugLengthRatio < MinPlugLengthRatio || plugLengthRatio > MaxPlugLengthRatio)
            throw new System.ArgumentOutOfRangeException(nameof(plugLengthRatio),
                $"plug length ratio must be in [{MinPlugLengthRatio:F2}, {MaxPlugLengthRatio:F2}]");
        if (gamma <= 1.0 || gamma >= 2.0)
            throw new System.ArgumentOutOfRangeException(nameof(gamma),
                "gamma must be in (1.0, 2.0)");
        if (stationCount < 8)
            throw new System.ArgumentOutOfRangeException(nameof(stationCount),
                "need at least 8 stations");

        // ── Prandtl-Meyer-driven plug profile ──────────────────────
        // At throat (M=1) ν=0. At exit plane M=M_e chosen so A_e/A_t =
        // expansionRatio — Newton-invert the isentropic area-Mach relation.
        double mExit = SolveExitMachFromAreaRatio(expansionRatio, gamma);
        double nuExit = PrandtlMeyer(mExit, gamma);

        // ── Inner throat radius from annulus area ──────────────────
        // Choose the annulus so the combination (R_o, R_i) gives the
        // desired throat area A_t in the annular form π(R_o² − R_i²).
        // Use R_i = 0.40 × R_o as a typical aerospike geometric start.
        // The absolute value depends on where the chamber feeds in; a
        // future sprint can parameterise this.
        double rInner_mm = 0.40 * throatOuterRadius_mm;
        double throatAnnulus_mm2 = System.Math.PI
            * (throatOuterRadius_mm * throatOuterRadius_mm
             - rInner_mm * rInner_mm);

        // Full-spike length from the Angelino last-characteristic identity:
        // at the design exit (perfect expansion, flow parallel to axis) the
        // last left-running characteristic from the throat lip travels at
        // the Mach angle μ_e = arcsin(1/M_e) to the centreline, so it
        // intersects the axis at axial distance R_o / tan(μ_e) =
        // R_o · cot(μ_e). Reproduces Angelino Table 1 at γ=1.15 / ε=15:
        // M_e ≈ 3.4, μ_e ≈ 0.299 rad, cot ≈ 3.34 → L ≈ 3.3 × R_o.
        //
        // The previous formula R_o · (ε−1) / (2·tan(ν_exit)) (pre-#548-B)
        // happened to land near the same value at the γ=1.15/ε=15 anchor
        // but diverged at high ν_e: for γ=1.15 / ε=58 (XRS-2200), ν_exit
        // crossed π/2 and tan flipped sign, producing a negative plug
        // length that cascaded into the XRS-2200 fixture failures. The
        // cot(μ_e) form is monotonically positive across the full ε range.
        double muExit = System.Math.Asin(1.0 / mExit);
        double plugFullLength_mm = throatOuterRadius_mm / System.Math.Tan(muExit);
        double plugTruncLength_mm = plugLengthRatio * plugFullLength_mm;

        // ── Station march along the plug ───────────────────────────
        // Parameterise by axial position (uniform); compute plug radius
        // via the isentropic area-Mach back-solve (PH-1, Sprint 31)
        // and local flow angle from the remaining Prandtl-Meyer turn
        // (PH-15, Sprint 31).
        var stations = new AerospikeStation[stationCount + 1];
        for (int i = 0; i <= stationCount; i++)
        {
            double t = (double)i / stationCount;                  // 0..1 along the truncated plug
            double x_mm = t * plugTruncLength_mm;
            // Angelino parametric: the point on the plug at axial x has
            // been reached by a Prandtl-Meyer turn of ν_local =
            // ν_exit · (x / L_full). Invert ν→M for the local Mach.
            double nuLocal = nuExit * (x_mm / plugFullLength_mm);
            double mLocal  = SolveMachFromPrandtlMeyer(nuLocal, gamma);

            // **PH-1 (Sprint 31, 2026-04-24):** area-Mach back-solve
            // replaces the legacy linear-cone formula
            // r(x) = R_i · (1 − x / L_full). The flow area at station x
            // satisfies the isentropic relation A/A* = AreaRatio(M, γ),
            // and for the annular envelope between the (approximately
            // fixed) outer free-jet boundary R_o and the plug surface:
            //   π · (R_o² − r_plug²) = A_throat · AreaRatio(M_local, γ)
            //   ⇒ r_plug = √(R_o² − A_throat · AreaRatio / π)
            // Self-consistent at the throat: r_plug(0) = R_i because
            // AreaRatio(1, γ) = 1, A(0) = A_throat = π(R_o² − R_i²).
            double areaRatio_local = AreaRatio(mLocal, gamma);
            double A_local_mm2     = throatAnnulus_mm2 * areaRatio_local;
            double rSquared        = throatOuterRadius_mm * throatOuterRadius_mm
                                   - A_local_mm2 / System.Math.PI;
            double r_physics_mm    = rSquared > 0 ? System.Math.Sqrt(rSquared) : 0.0;

            // **Sprint fix (2026-04-25):** PH-1's area-Mach formula assumes
            // the outer free-jet boundary is fixed at R_o. For typical
            // aerospike designs (ε ≳ 5) that constraint forces r_plug → 0
            // at very small x (≲ R_o · cot(ν_exit)) — much earlier than
            // the Angelino-scaled L_full = R_o(ε−1)/(2 tan ν_exit). The
            // resulting "plug" was a tiny cone right at the throat plane
            // with the rest of the stations sitting at r=0. The
            // area-Mach formula is the right MODEL but its assumption
            // breaks down at high ε; the PHYSICAL plug shape is closer
            // to the linear-cone interpolation R_i · (1 − x/L_full).
            // Use max(physics, linear) so the visualisation shows the
            // full-length plug while preserving PH-1's r at small x where
            // the formula is well-conditioned. PH-1's per-station
            // FlowAngle / Mach number remain unchanged for downstream
            // physics (cooling, thrust scoring).
            double r_linear_mm = rInner_mm
                * System.Math.Max(0.0, 1.0 - x_mm / plugFullLength_mm);
            double r_mm = System.Math.Max(r_physics_mm, r_linear_mm);

            double rInner_local = r_mm;
            double rOuter_local = includeCowl && i == 0
                ? throatOuterRadius_mm + 2.0   // short cowl flare at throat
                : double.NaN;

            // **PH-15 (Sprint 31):** per-station FlowAngle = remaining
            // Prandtl-Meyer turn = ν_exit − ν_local. Pre-Sprint-31 was
            // a single constant cone half-angle for every station, which
            // mis-represented the upstream-bowed flow direction at the
            // throat and the axial flow direction at the design-Mach
            // exit. The new value varies smoothly from ν_exit at the
            // throat (full turn ahead) to 0 at exit.
            double flowAngle = nuExit - nuLocal;

            AerospikeRegion region = (i == 0) ? AerospikeRegion.Throat
                                   : (i == stationCount) ? AerospikeRegion.PlugTruncation
                                   : AerospikeRegion.PlugExpansion;
            stations[i] = new AerospikeStation(
                X_mm:          x_mm,
                R_inner_mm:    rInner_local,
                R_outer_mm:    rOuter_local,
                FlowAngle_rad: flowAngle,
                Region:        region);
        }

        double exitArea_mm2 = System.Math.PI
            * stations[^1].R_inner_mm * stations[^1].R_inner_mm;
        double cowlLength_mm = includeCowl ? 0.25 * throatOuterRadius_mm : 0.0;
        double cowlOuterRadius_mm = includeCowl ? throatOuterRadius_mm + 2.0 : 0.0;

        return new AerospikeContour(
            Stations:                stations,
            ThroatIndex:             0,
            ThroatAnnulusArea_mm2:   throatAnnulus_mm2,
            ThroatOuterRadius_mm:    throatOuterRadius_mm,
            ThroatInnerRadius_mm:    rInner_mm,
            ExitArea_mm2:            exitArea_mm2,
            ExpansionRatio:          expansionRatio,
            PlugFullLength_mm:       plugFullLength_mm,
            PlugTruncatedLength_mm:  plugTruncLength_mm,
            PlugLengthRatio:         plugLengthRatio,
            CowlLength_mm:           cowlLength_mm,
            CowlOuterRadius_mm:      cowlOuterRadius_mm,
            Gamma:                   gamma,
            DesignExitMach:          mExit);
    }

    /// <summary>
    /// Prandtl-Meyer function ν(M; γ) in radians. Standard form from
    /// Anderson <i>Modern Compressible Flow</i> §9.6:
    ///   ν = √((γ+1)/(γ-1)) · atan(√((γ-1)(M²-1)/(γ+1))) − atan(√(M²-1))
    /// Defined for M ≥ 1; returns 0 at M = 1.
    /// </summary>
    public static double PrandtlMeyer(double mach, double gamma)
    {
        if (mach <= 1.0) return 0.0;
        double gp = gamma + 1.0;
        double gm = gamma - 1.0;
        double m2m1 = mach * mach - 1.0;
        return System.Math.Sqrt(gp / gm)
             * System.Math.Atan(System.Math.Sqrt(gm / gp * m2m1))
             - System.Math.Atan(System.Math.Sqrt(m2m1));
    }

    /// <summary>
    /// Invert ν(M; γ) — given a turning angle ν in radians, find the
    /// corresponding Mach number M ≥ 1. Newton's method from M = 1.3.
    /// </summary>
    public static double SolveMachFromPrandtlMeyer(double nu, double gamma)
    {
        if (nu <= 0.0) return 1.0;
        double m = 1.3;   // starting guess
        for (int iter = 0; iter < 30; iter++)
        {
            double f = PrandtlMeyer(m, gamma) - nu;
            // Derivative dν/dM = √(M²−1) / (M · (1 + (γ−1)/2 · M²))
            double m2m1 = System.Math.Max(m * m - 1.0, 1e-12);
            double dnudM = System.Math.Sqrt(m2m1)
                         / (m * (1.0 + 0.5 * (gamma - 1.0) * m * m));
            double step = f / System.Math.Max(dnudM, 1e-9);
            m -= step;
            if (m < 1.001) m = 1.001;
            if (m > 25.0) m = 25.0;
            if (System.Math.Abs(step) < 1e-6) break;
        }
        return m;
    }

    /// <summary>
    /// Forward isentropic area-Mach relation
    ///   A/A* = (1/M) · [(2 + (γ-1)M²) / (γ+1)] ^ ((γ+1)/(2(γ-1)))
    /// Returns 1.0 at M = 1, ∞ as M → ∞. Used by the Sprint 31 PH-1
    /// area-Mach back-solve in <see cref="Generate"/> to compute the
    /// plug radius at each station from the local Mach number.
    /// </summary>
    public static double AreaRatio(double mach, double gamma)
    {
        if (mach <= 1.0) return 1.0;
        double exp = (gamma + 1.0) / (2.0 * (gamma - 1.0));
        double inner = (2.0 + (gamma - 1.0) * mach * mach) / (gamma + 1.0);
        return (1.0 / mach) * System.Math.Pow(inner, exp);
    }

    /// <summary>
    /// Solve the isentropic area-Mach relation
    ///   A/A* = (1/M) · [(2 + (γ-1)M²) / (γ+1)] ^ ((γ+1)/(2(γ-1)))
    /// for supersonic M given an area ratio A/A* ≥ 1. Newton from M = 2.
    /// </summary>
    public static double SolveExitMachFromAreaRatio(double areaRatio, double gamma)
    {
        if (areaRatio <= 1.0) return 1.0;
        double m = 2.0;
        for (int iter = 0; iter < 40; iter++)
        {
            double g = (gamma + 1.0) / (2.0 * (gamma - 1.0));
            double inner = (2.0 + (gamma - 1.0) * m * m) / (gamma + 1.0);
            double f = (1.0 / m) * System.Math.Pow(inner, g) - areaRatio;
            // Numerical derivative by central differences — the analytic
            // derivative is error-prone and the solve is not performance-
            // critical (once per contour generation).
            double h = 1e-5;
            double inner_p = (2.0 + (gamma - 1.0) * (m + h) * (m + h)) / (gamma + 1.0);
            double inner_m = (2.0 + (gamma - 1.0) * (m - h) * (m - h)) / (gamma + 1.0);
            double fp = (1.0 / (m + h)) * System.Math.Pow(inner_p, g) - areaRatio;
            double fm = (1.0 / (m - h)) * System.Math.Pow(inner_m, g) - areaRatio;
            double df = (fp - fm) / (2.0 * h);
            double step = f / System.Math.Max(System.Math.Abs(df), 1e-9)
                        * System.Math.Sign(df);
            m -= step;
            if (m < 1.01) m = 1.01;
            if (m > 25.0) m = 25.0;
            if (System.Math.Abs(step) < 1e-6) break;
        }
        return m;
    }
}
