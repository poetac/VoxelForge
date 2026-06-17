// ChamberContour.cs — Rao-approximated thrust chamber contour generator.
//
// Produces a station-discretized axisymmetric contour from injector face
// through the converging section, throat, and bell nozzle to the exit plane.
//
// References:
//   Sutton & Biblarz, Rocket Propulsion Elements, 9th ed., Ch. 3, 4, 8.
//   Huzel & Huang, Modern Engineering for Design of Liquid-Propellant Rocket
//     Engines, AIAA Progress Series Vol. 147, Ch. 4.
//   NASA SP-8120, Liquid Rocket Engine Nozzles.
//
// Simplifications vs. a real Rao TOP (thrust-optimized parabola):
//   • Converging section is a single circular-arc fillet + conical cone
//     (30° half-angle) rather than a spline matched to the injector.
//   • Diverging bell is a two-segment approximation: initial circular arc
//     of radius 0.382·R_t followed by a quadratic Bezier parabola hitting
//     the specified exit angle.  Rao's true TOP solves a method-of-
//     characteristics optimization; the quadratic approximation is within
//     a few tenths of a percent on C_F for typical ε = 10–40.
//
// All length units are millimetres; all areas are mm².

using System.Numerics;

namespace Voxelforge.Chamber;

/// <summary>
/// One axial station of the axisymmetric chamber contour.
/// </summary>
public readonly record struct ContourStation(
    double X_mm,                 // axial position, 0 = injector face
    double R_mm,                 // local wall radius
    double Area_mm2,             // local cross-sectional area
    double Slope,                // dr/dx (signed — negative in converging, zero at throat)
    ChamberRegion Region);

public enum ChamberRegion
{
    Barrel,         // constant-radius combustion chamber
    Converging,     // contraction section (fillet + cone)
    ThroatArc,      // circular arc through throat
    BellArc,        // initial circular arc of diverging bell
    BellParabola,   // quadratic Bezier parabolic section of bell (single-bell or first half of dual-bell)
    BellParabola2   // Sprint 20: second parabola of a dual-bell nozzle, downstream of the inflection
}

/// <summary>
/// Full chamber contour as an ordered set of axial stations.
/// </summary>
/// <param name="InflectionIndex">
/// Sprint 20: station index of the dual-bell inflection (the slope-
/// discontinuity point where the first parabola ends and the second
/// begins). -1 (default) on single-bell designs; on dual-bell
/// designs the station at that index is the LAST station of
/// <see cref="ChamberRegion.BellParabola"/> and the station at
/// <c>InflectionIndex + 1</c> is the FIRST station of
/// <see cref="ChamberRegion.BellParabola2"/>. Surfaced so downstream
/// voxel / channel / thermal solvers can handle the slope jump
/// cleanly (e.g. channel-height interpolation splits at the
/// inflection instead of smoothing across it).
/// </param>
/// <param name="InflectionRadius_mm">
/// Sprint 20: wall radius at the inflection station. 0.0 on
/// single-bell designs. Equal to
/// <c>ThroatRadius_mm · sqrt(SeaLevelExpansionRatio)</c> when set.
/// </param>
public sealed record ChamberContour(
    ContourStation[] Stations,
    int ThroatIndex,
    double ThroatRadius_mm,
    double ThroatArea_mm2,
    double ChamberRadius_mm,
    double ExitRadius_mm,
    double ContractionRatio,
    double ExpansionRatio,
    double ChamberLength_mm,          // injector face to converging entrance
    double ConvergingLength_mm,
    double BellLength_mm,
    double TotalLength_mm,
    double ChamberVolume_mm3,         // upstream of throat (barrel + converging)
    double CharacteristicLength_m,    // L* = V_c / A_t  (metres — conventional)
    int InflectionIndex = -1,
    double InflectionRadius_mm = 0.0)
{
    /// <summary>
    /// Sprint 20: true when this contour has a dual-bell inflection
    /// (<see cref="InflectionIndex"/> &gt;= 0). Downstream solvers key off
    /// this flag to opt-in to inflection-aware handling.
    /// </summary>
    public bool IsDualBell => InflectionIndex >= 0;
    /// <summary>
    /// Get station index closest to a given axial position. Sprint 14 /
    /// Track I / P4: O(log N) binary search on the
    /// monotonically-increasing <c>Stations[i].X_mm</c> sequence (built
    /// barrel → converging → bell in <see cref="ChamberContourGenerator"/>);
    /// previously O(N) linear scan, called per-voxel in CFD export so
    /// dropped ~0.5-2 s per export at default 1M-voxel grid.
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
    /// Station spacing at index i (trapezoidal: halfway to neighbors).
    /// </summary>
    public double SegmentLength_mm(int i)
    {
        if (Stations.Length < 2) return 0;
        if (i == 0) return (Stations[1].X_mm - Stations[0].X_mm) * 0.5;
        if (i == Stations.Length - 1)
            return (Stations[i].X_mm - Stations[i - 1].X_mm) * 0.5;
        return (Stations[i + 1].X_mm - Stations[i - 1].X_mm) * 0.5;
    }
}

public static class ChamberContourGenerator
{
    /// <summary>
    /// Generate a Rao-approximated chamber contour.
    /// </summary>
    /// <param name="throatRadius_mm">Throat radius, R_t.</param>
    /// <param name="contractionRatio">A_chamber / A_throat (ε_c). Typical 3–10.</param>
    /// <param name="expansionRatio">A_exit / A_throat (ε_e). Typical 4–25 for sea-level.</param>
    /// <param name="characteristicLength_m">L* = V_c / A_t in metres. Typical 1.0–1.3 for LOX/CH4.</param>
    /// <param name="thetaN_deg">Bell entrance angle at end of throat arc. Typical 20–35°.</param>
    /// <param name="thetaE_deg">Bell exit angle at exit plane. Typical 7–15°.</param>
    /// <param name="bellLengthFraction">Fraction of equivalent 15° conical length. 0.6–0.9.</param>
    /// <param name="stationCount">Number of discretization points along contour.</param>
    /// <param name="dualBell">Sprint 20: enable altitude-compensating dual-bell contour. When true, <paramref name="seaLevelExpansionRatio"/> and <paramref name="inflectionAngleDeg"/> are consumed; the bell splits into two parabolas joined at the inflection.</param>
    /// <param name="seaLevelExpansionRatio">Sprint 20: intermediate expansion ratio at the dual-bell inflection. Must be in (1.0, <paramref name="expansionRatio"/>). Ignored unless <paramref name="dualBell"/> is true.</param>
    /// <param name="inflectionAngleDeg">Sprint 20: wall slope (degrees from axial) on the first-bell side of the inflection. Must be in (0, <paramref name="thetaN_deg"/>). Typical 3–10°. Ignored unless <paramref name="dualBell"/> is true.</param>
    public static ChamberContour Generate(
        double throatRadius_mm,
        double contractionRatio,
        double expansionRatio,
        double characteristicLength_m,
        double thetaN_deg = 30.0,
        double thetaE_deg = 10.0,
        double bellLengthFraction = 0.8,
        int stationCount = 240,
        bool dualBell = false,
        double seaLevelExpansionRatio = 0.0,
        double inflectionAngleDeg = 7.0)
    {
        if (throatRadius_mm <= 0)
            throw new ArgumentException("throatRadius_mm must be positive",
                nameof(throatRadius_mm));
        if (contractionRatio <= 1)
            throw new ArgumentException("contractionRatio must be > 1",
                nameof(contractionRatio));
        if (expansionRatio <= 1)
            throw new ArgumentException("expansionRatio must be > 1",
                nameof(expansionRatio));
        if (characteristicLength_m <= 0)
            throw new ArgumentException("L* (characteristicLength_m) must be positive",
                nameof(characteristicLength_m));
        if (dualBell)
        {
            if (seaLevelExpansionRatio <= 1.0)
                throw new ArgumentException(
                    "seaLevelExpansionRatio must be > 1.0 when dualBell is enabled", nameof(seaLevelExpansionRatio));
            if (seaLevelExpansionRatio >= expansionRatio)
                throw new ArgumentException(
                    "seaLevelExpansionRatio must be < expansionRatio when dualBell is enabled", nameof(seaLevelExpansionRatio));
            if (inflectionAngleDeg <= 0.0 || inflectionAngleDeg >= thetaN_deg)
                throw new ArgumentException(
                    "inflectionAngleDeg must be in (0, thetaN_deg) when dualBell is enabled", nameof(inflectionAngleDeg));
        }

        double R_t = throatRadius_mm;
        double R_c = R_t * Math.Sqrt(contractionRatio);
        double R_e = R_t * Math.Sqrt(expansionRatio);
        double A_t = Math.PI * R_t * R_t;

        // Required chamber volume from L* (convert L* metres → mm).
        double V_required_mm3 = characteristicLength_m * 1000.0 * A_t;

        // --- Converging section geometry ---------------------------------
        // Downstream fillet: R_d = 1.5·R_t (standard)
        // Upstream fillet:   R_u = R_c * 0.7 (smooth entry from chamber barrel)
        // Cone half-angle:   β = 30° (standard for LRE)
        const double betaDeg = 30.0;
        double beta = betaDeg * Math.PI / 180.0;
        double R_d = 1.5 * R_t;
        double R_u = 0.7 * R_c;

        // Point where downstream fillet meets the cone (tangent at β from vertical):
        // The fillet's centre is at (x_throat - 0, R_t + R_d).  At angle β from
        // the cone tangent, the fillet meets the cone at:
        double xArcEnd = -R_d * Math.Sin(beta);
        double rArcEnd = R_t + R_d * (1.0 - Math.Cos(beta));

        // Upstream fillet centre at (x_U, R_c - R_u), meeting cone at angle β:
        double rUpArcStart = R_c - R_u * (1.0 - Math.Cos(beta));
        // Cone length satisfies:  Δr = (rUpArcStart - rArcEnd) = -Δx · tan(β)
        double coneDeltaR = rUpArcStart - rArcEnd;
        double coneLength = coneDeltaR / Math.Tan(beta);
        double xUpArcEnd = xArcEnd - coneLength;
        double xUpArcStart = xUpArcEnd - R_u * Math.Sin(beta);

        double L_converging = -xUpArcStart;  // magnitude of converging length

        // --- Bell geometry (downstream of throat) -----------------------
        // Initial circular arc: radius R_n = 0.382 · R_t, from vertical tangent
        // (at throat) to angle θ_n.
        double thetaN = thetaN_deg * Math.PI / 180.0;
        double thetaE = thetaE_deg * Math.PI / 180.0;
        double R_n = 0.382 * R_t;
        double xN = R_n * Math.Sin(thetaN);
        double rN = R_t + R_n * (1.0 - Math.Cos(thetaN));

        // Equivalent 15° conical length: L_15 = (R_e - R_t) / tan(15°)
        double L15 = (R_e - R_t) / Math.Tan(15.0 * Math.PI / 180.0);
        double L_bell = bellLengthFraction * L15;   // from throat to exit, total x
        double xExit = L_bell;

        // Parabola: Bezier control point Q where the tangent lines at N (slope
        // tan θ_n) and E (slope tan θ_e) intersect.
        //   Line from N:  r = rN + tan(θ_n) · (x - xN)
        //   Line from E:  r = R_e - tan(θ_e) · (xExit - x)
        //   Solve for intersection (xQ, rQ).
        double m1 = Math.Tan(thetaN);
        double m2 = Math.Tan(thetaE);
        double xQ, rQ;                  // single-bell parabola control point
        double xInflection = 0.0;       // Sprint 20: dual-bell inflection x (local, throat-origin)
        double rInflection = 0.0;       // Sprint 20: dual-bell inflection radius
        double xQ1 = 0.0, rQ1 = 0.0;    // Sprint 20: first-parabola control point (dual-bell)
        double xQ2 = 0.0, rQ2 = 0.0;    // Sprint 20: second-parabola control point (dual-bell)
        if (dualBell)
        {
            // First parabola: (xN, rN) slope θ_n → (xInflection, rInflection) slope θ_i.
            // Second parabola: (xInflection, rInflection) slope θ_n → (xExit, R_e) slope θ_e.
            // Split the x-axis by the 15°-equivalent-cone length of each radial
            // delta: the total bell length matches a single-bell with the same
            // bellLengthFraction (since radial deltas sum), only the parabola
            // is interrupted at the inflection.
            double thetaI = inflectionAngleDeg * Math.PI / 180.0;
            double mi = Math.Tan(thetaI);
            rInflection = R_t * Math.Sqrt(seaLevelExpansionRatio);
            double L15_first = (rInflection - R_t) / Math.Tan(15.0 * Math.PI / 180.0);
            xInflection = bellLengthFraction * L15_first;
            // Clamp to a sliver below xExit so first-parabola is always upstream.
            xInflection = Math.Min(xInflection, xExit - 1e-3);
            xInflection = Math.Max(xInflection, xN + 1e-3);

            if (Math.Abs(m1 - mi) < 1e-9)
            {
                xQ1 = 0.5 * (xN + xInflection);
                rQ1 = 0.5 * (rN + rInflection);
            }
            else
            {
                xQ1 = (rInflection - rN + m1 * xN + mi * xInflection) / (m1 + mi);
                rQ1 = rN + m1 * (xQ1 - xN);
            }

            // Second parabola re-enters at θ_n (the designed angle discontinuity)
            // and targets θ_e at the exit.
            if (Math.Abs(m1 - m2) < 1e-9)
            {
                xQ2 = 0.5 * (xInflection + xExit);
                rQ2 = 0.5 * (rInflection + R_e);
            }
            else
            {
                xQ2 = (R_e - rInflection + m1 * xInflection + m2 * xExit) / (m1 + m2);
                rQ2 = rInflection + m1 * (xQ2 - xInflection);
            }

            // Still compute (xQ, rQ) so the single-bell discretization path
            // compiles; it's never referenced on the dual-bell branch.
            xQ = xQ2;
            rQ = rQ2;
        }
        else if (Math.Abs(m1 - m2) < 1e-9)
        {
            // Parallel tangents — degenerate, use midpoint
            xQ = 0.5 * (xN + xExit);
            rQ = 0.5 * (rN + R_e);
        }
        else
        {
            // rN + m1 (x - xN) = R_e - m2 (xExit - x)
            xQ = (R_e - rN + m1 * xN + m2 * xExit) / (m1 + m2);
            rQ = rN + m1 * (xQ - xN);
        }

        // --- Barrel (chamber) length -----------------------------------
        // Chamber volume contributed by converging section (exact for cone +
        // fillets — use numerical integration for the fillet arcs).
        double V_converging_mm3 = IntegrateConvergingVolume(
            R_c, R_u, R_t, R_d, beta, xUpArcStart, xUpArcEnd, xArcEnd);

        double V_barrel_required = Math.Max(V_required_mm3 - V_converging_mm3, 0.25 * V_required_mm3);
        double L_barrel = V_barrel_required / (Math.PI * R_c * R_c);
        // Clamp to reasonable bounds: L_barrel must be ≥ 0.25·R_c (short combustor
        // limit) and ≤ 5·R_c (combustion residence tractable).
        L_barrel = Math.Clamp(L_barrel, 0.25 * R_c, 5.0 * R_c);

        // --- Global x-axis: 0 = injector face ---------------------------
        // Barrel spans [0, L_barrel].
        // Converging spans [L_barrel, L_barrel + L_converging].
        // Throat at x_t = L_barrel + L_converging.
        // Bell spans [x_t, x_t + L_bell].
        double x_t = L_barrel + L_converging;
        double L_total = x_t + L_bell;

        // --- Discretize ------------------------------------------------
        var stations = new List<ContourStation>(stationCount);

        // Allocate stations proportionally: barrel / converging / bell.
        int nBarrel = (int)(stationCount * L_barrel / L_total);
        int nConv = (int)(stationCount * L_converging / L_total);
        int nBell = stationCount - nBarrel - nConv;
        nBarrel = Math.Max(nBarrel, 10);
        nConv = Math.Max(nConv, 20);   // converging curvature → need density
        nBell = Math.Max(nBell, 30);

        // Barrel
        for (int i = 0; i < nBarrel; i++)
        {
            double x = i * L_barrel / (nBarrel - 1);
            stations.Add(new ContourStation(x, R_c, Math.PI * R_c * R_c, 0.0, ChamberRegion.Barrel));
        }

        // Converging — three sub-segments, parameterised in GLOBAL x
        //   (a) Upstream fillet:   x in [x_barrelEnd,  x_upFilletEnd]   R_c  → rUpArcStart
        //   (b) Cone:              x in [x_upFilletEnd, x_coneEnd]      rUpArcStart → rArcEnd
        //   (c) Downstream fillet: x in [x_coneEnd,     x_throat]       rArcEnd → R_t
        double x_barrelEnd = L_barrel;
        double x_upFilletEnd = x_barrelEnd + R_u * Math.Sin(beta);
        double x_coneEnd = x_upFilletEnd + coneLength;
        double x_throatGlobal = x_coneEnd + R_d * Math.Sin(beta);   // == L_barrel + L_converging

        int nConvA = Math.Max(nConv / 4, 5);
        int nConvC = Math.Max(nConv / 3, 5);
        int nConvB = nConv - nConvA - nConvC;

        // (a) upstream fillet — centre at (x_barrelEnd, R_c - R_u).
        //     At x = x_barrelEnd:   dx = 0, r = R_c          (tangent to barrel)
        //     At x = x_upFilletEnd: dx = R_u sin β, r = rUpArcStart (tangent to cone)
        for (int i = 1; i <= nConvA; i++)
        {
            double t = (double)i / nConvA;
            double x = x_barrelEnd + t * (x_upFilletEnd - x_barrelEnd);
            double dx = x - x_barrelEnd;
            double root = Math.Sqrt(Math.Max(R_u * R_u - dx * dx, 0));
            double r = (R_c - R_u) + root;
            double slope = -dx / Math.Max(root, 1e-9);
            stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, ChamberRegion.Converging));
        }

        // (b) cone — linear, slope -tan β.
        for (int i = 1; i <= nConvB; i++)
        {
            double t = (double)i / nConvB;
            double x = x_upFilletEnd + t * (x_coneEnd - x_upFilletEnd);
            double r = rUpArcStart - (x - x_upFilletEnd) * Math.Tan(beta);
            double slope = -Math.Tan(beta);
            stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, ChamberRegion.Converging));
        }

        // (c) downstream fillet — centre at (x_throatGlobal, R_t + R_d).
        //     At x = x_coneEnd:       dx = R_d sin β, r = rArcEnd (tangent to cone)
        //     At x = x_throatGlobal:  dx = 0,         r = R_t     (tangent to throat)
        for (int i = 1; i <= nConvC; i++)
        {
            double t = (double)i / nConvC;
            double x = x_coneEnd + t * (x_throatGlobal - x_coneEnd);
            double dx = x_throatGlobal - x;                           // >= 0 on this segment
            double root = Math.Sqrt(Math.Max(R_d * R_d - dx * dx, 0));
            double r = (R_t + R_d) - root;
            // dr/dx = -(x_throat - x)/sqrt(...) after chain rule — negative in converging
            double slope = -dx / Math.Max(root, 1e-9);
            var region = (i == nConvC) ? ChamberRegion.ThroatArc : ChamberRegion.Converging;
            stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, region));
        }

        int throatIndex = stations.Count - 1;

        // --- Bell ------------------------------------------------------
        // Initial circular arc, throat x=0 in local coords
        int nBellArc = Math.Max(nBell / 6, 8);
        int nBellPara = nBell - nBellArc;

        for (int i = 1; i <= nBellArc; i++)
        {
            double t = (double)i / nBellArc;
            double theta = t * thetaN;
            double xLocal = R_n * Math.Sin(theta);
            double r = R_t + R_n * (1.0 - Math.Cos(theta));
            double slope = Math.Tan(theta);
            double x = x_t + xLocal;
            stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, ChamberRegion.BellArc));
        }

        // Parabolic section — one parabola (single-bell) or two (dual-bell).
        int inflectionIndex = -1;
        double inflectionRadius_mm = 0.0;
        if (dualBell)
        {
            // Sprint 20: split station budget between the two parabolas.
            // Each parabola gets stations proportional to its x-span, with
            // a minimum so neither segment is starved.
            double firstFrac = Math.Clamp(
                (xInflection - xN) / Math.Max(xExit - xN, 1e-9), 0.1, 0.9);
            int nFirst = Math.Max((int)Math.Round(nBellPara * firstFrac), 6);
            nFirst = Math.Min(nFirst, nBellPara - 6);
            int nSecond = nBellPara - nFirst;

            // First parabola: Bezier from (xN, rN) through (xQ1, rQ1) to (xInflection, rInflection).
            for (int i = 1; i <= nFirst; i++)
            {
                double t = (double)i / nFirst;
                double one = 1.0 - t;
                double xLocal = one * one * xN + 2.0 * one * t * xQ1 + t * t * xInflection;
                double r = one * one * rN + 2.0 * one * t * rQ1 + t * t * rInflection;
                double dxdt = 2.0 * ((xQ1 - xN) * one + (xInflection - xQ1) * t);
                double drdt = 2.0 * ((rQ1 - rN) * one + (rInflection - rQ1) * t);
                double slope = Math.Abs(dxdt) < 1e-9 ? 0 : drdt / dxdt;
                double x = x_t + xLocal;
                stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, ChamberRegion.BellParabola));
            }
            inflectionIndex = stations.Count - 1;
            inflectionRadius_mm = rInflection;

            // Second parabola: Bezier from (xInflection, rInflection) through (xQ2, rQ2) to (xExit, R_e).
            for (int i = 1; i <= nSecond; i++)
            {
                double t = (double)i / nSecond;
                double one = 1.0 - t;
                double xLocal = one * one * xInflection + 2.0 * one * t * xQ2 + t * t * xExit;
                double r = one * one * rInflection + 2.0 * one * t * rQ2 + t * t * R_e;
                double dxdt = 2.0 * ((xQ2 - xInflection) * one + (xExit - xQ2) * t);
                double drdt = 2.0 * ((rQ2 - rInflection) * one + (R_e - rQ2) * t);
                double slope = Math.Abs(dxdt) < 1e-9 ? 0 : drdt / dxdt;
                double x = x_t + xLocal;
                stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, ChamberRegion.BellParabola2));
            }
        }
        else
        {
            // Single-bell parabola, Bezier from (xN, rN) through Q to (xExit, R_e).
            for (int i = 1; i <= nBellPara; i++)
            {
                double t = (double)i / nBellPara;
                double one = 1.0 - t;
                double xLocal = one * one * xN + 2.0 * one * t * xQ + t * t * xExit;
                double r = one * one * rN + 2.0 * one * t * rQ + t * t * R_e;
                // Derivative: dx/dt = 2((xQ-xN)(1-t) + (xExit-xQ)t), dr/dt same form
                double dxdt = 2.0 * ((xQ - xN) * one + (xExit - xQ) * t);
                double drdt = 2.0 * ((rQ - rN) * one + (R_e - rQ) * t);
                double slope = Math.Abs(dxdt) < 1e-9 ? 0 : drdt / dxdt;
                double x = x_t + xLocal;
                stations.Add(new ContourStation(x, r, Math.PI * r * r, slope, ChamberRegion.BellParabola));
            }
        }

        var stationsArr = stations.ToArray();

        double V_chamber = L_barrel * Math.PI * R_c * R_c + V_converging_mm3;
        double LStar_m = V_chamber / A_t / 1000.0;

        return new ChamberContour(
            Stations: stationsArr,
            ThroatIndex: throatIndex,
            ThroatRadius_mm: R_t,
            ThroatArea_mm2: A_t,
            ChamberRadius_mm: R_c,
            ExitRadius_mm: R_e,
            ContractionRatio: contractionRatio,
            ExpansionRatio: expansionRatio,
            ChamberLength_mm: L_barrel,
            ConvergingLength_mm: L_converging,
            BellLength_mm: L_bell,
            TotalLength_mm: L_total,
            ChamberVolume_mm3: V_chamber,
            CharacteristicLength_m: LStar_m,
            InflectionIndex: inflectionIndex,
            InflectionRadius_mm: inflectionRadius_mm);
    }

    /// <summary>
    /// Numerical integration of the converging-section volume using the
    /// disk method. Accurate to 1 % with 200 slices.
    /// </summary>
    private static double IntegrateConvergingVolume(
        double R_c, double R_u, double R_t, double R_d, double beta,
        double xUpArcStart, double xUpArcEnd, double xArcEnd)
    {
        const int slices = 400;
        double xStart = xUpArcStart;
        double xEnd = 0;
        double dx = (xEnd - xStart) / slices;
        double V = 0;
        double rUpArcStartCalc = R_c - R_u * (1.0 - Math.Cos(beta));
        double rArcEndCalc = R_t + R_d * (1.0 - Math.Cos(beta));

        for (int i = 0; i < slices; i++)
        {
            double x0 = xStart + i * dx;
            double x1 = x0 + dx;
            double r0 = ConvergingR(x0, R_c, R_u, R_t, R_d, beta,
                                    xUpArcStart, xUpArcEnd, xArcEnd,
                                    rUpArcStartCalc, rArcEndCalc);
            double r1 = ConvergingR(x1, R_c, R_u, R_t, R_d, beta,
                                    xUpArcStart, xUpArcEnd, xArcEnd,
                                    rUpArcStartCalc, rArcEndCalc);
            V += Math.PI * 0.5 * (r0 * r0 + r1 * r1) * Math.Abs(dx);
        }
        return V;
    }

    private static double ConvergingR(double x,
        double R_c, double R_u, double R_t, double R_d, double beta,
        double xUpArcStart, double xUpArcEnd, double xArcEnd,
        double rUpArcStart, double rArcEnd)
    {
        // x is in local (throat-origin) converging coordinates, x <= 0.
        // xUpArcStart = −L_converging (barrel end), xUpArcEnd = barrel end + R_u sin β (cone start),
        // xArcEnd = −R_d sin β (cone → downstream fillet), 0 = throat.
        if (x <= xUpArcEnd)   // upstream fillet: centre at (xUpArcStart, R_c − R_u)
        {
            double dx = x - xUpArcStart;
            return (R_c - R_u) + Math.Sqrt(Math.Max(R_u * R_u - dx * dx, 0));
        }
        if (x <= xArcEnd)     // linear cone
        {
            return rUpArcStart - (x - xUpArcEnd) * Math.Tan(beta);
        }
        // downstream fillet: centre at (0, R_t + R_d), dx = -x
        double dxT = -x;
        return (R_t + R_d) - Math.Sqrt(Math.Max(R_d * R_d - dxT * dxT, 0));
    }
}
