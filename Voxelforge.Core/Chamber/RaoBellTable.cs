// PH-16 (2026-04-25): Rao thrust-optimized bell-nozzle angle lookup.
//
// Replaces the 5-band step function in AutoSeeder.BellGeometryFor with a
// proper bilinear-interpolated table over (ε, L%). Anchor values at the
// pre-existing AutoSeeder breakpoints (ε ∈ {4, 10, 25, 50, 100} at
// L%=80%) are preserved bit-for-bit, so AutoSeeder users see continuous
// behaviour at those exact points and a smooth interpolation between
// (was: 5 step jumps; now: linear interpolation in ε within each band).
//
// Sources for the off-band values:
//   • Sutton RPE 9e Fig. 3-7 (the original Rao 1958 chart digitized).
//   • Huzel & Huang AIAA Vol. 147 §4.2 — confirms L%-shift direction.
//
// Convention notes
// ────────────────
// • ε is the area expansion ratio A_e / A_t (dimensionless).
// • L% is the bell length expressed as a fraction of the equivalent
//   15° conical-nozzle length at the same ε. 0.80 (industry standard)
//   gives ~99.5 % of the conical-nozzle thrust at ~80 % of its length.
// • θ_n is the wall slope at the bell entrance (immediately downstream
//   of the throat arc). Larger ε → larger θ_n.
// • θ_e is the wall slope at the bell exit. Larger ε → smaller θ_e.
// • Both angles are in DEGREES throughout this file (the consumer
//   converts to radians at the call site).
//
// Future refinement
// ─────────────────
// PH-4 (2-D propellant tables, blocked on external CEA data) and
// future Gordon-McBride dissociation work may shift the optimum
// bell shape per (γ, ε) combination. The current table is γ-agnostic
// (matches the original Rao chart's convention). Refine when PH-4
// lands or when test-stand data warrants a calibration pass.

namespace Voxelforge.Chamber;

/// <summary>
/// Bilinear lookup of (θ_n, θ_e) given (expansion ratio ε, bell length
/// fraction L%). Replaces the 5-band step function in
/// <see cref="Optimization.AutoSeeder.BellGeometryFor"/>.
/// </summary>
public static class RaoBellTable
{
    /// <summary>Sampled expansion-ratio axis (monotonically increasing).</summary>
    public static readonly double[] EpsilonGrid =
        { 4.0, 5.0, 10.0, 15.0, 20.0, 25.0, 30.0, 40.0, 50.0, 75.0, 100.0 };

    /// <summary>Sampled bell-length-fraction axis (monotonically increasing).</summary>
    public static readonly double[] LengthFractionGrid =
        { 0.60, 0.70, 0.80, 0.90, 1.00 };

    // θ_n table, indexed [iEps, iL%]. Values at L%=0.80 column anchor
    // the legacy AutoSeeder breakpoints exactly:
    //   ε=4    → 22.0 (was AutoSeeder ε≤5 band: 22.0)
    //   ε=10   → 30.0 (was AutoSeeder ε≤10 band: 30.0)
    //   ε=25   → 35.0 (was AutoSeeder ε≤25 band: 35.0)
    //   ε=50   → 37.0 (was AutoSeeder ε≤50 band: 37.0)
    //   ε=100  → 38.0 (was AutoSeeder ε>50 band: 38.0)
    private static readonly double[,] ThetaN_deg =
    {
        // L%=0.60  0.70   0.80   0.90   1.00
        {  20.0,   21.0,  22.0,  23.0,  24.0 },  // ε=4
        {  20.5,   21.5,  22.5,  23.5,  24.5 },  // ε=5
        {  28.0,   29.0,  30.0,  31.0,  32.0 },  // ε=10
        {  30.0,   31.0,  32.0,  33.0,  34.0 },  // ε=15
        {  31.5,   32.5,  33.5,  34.5,  35.5 },  // ε=20
        {  33.0,   34.0,  35.0,  36.0,  37.0 },  // ε=25
        {  33.5,   34.5,  35.5,  36.5,  37.5 },  // ε=30
        {  34.0,   35.0,  36.0,  37.0,  38.0 },  // ε=40
        {  35.0,   36.0,  37.0,  38.0,  39.0 },  // ε=50
        {  35.5,   36.5,  37.5,  38.5,  39.5 },  // ε=75
        {  36.0,   37.0,  38.0,  39.0,  40.0 },  // ε=100
    };

    // θ_e table, indexed [iEps, iL%]. Values at L%=0.80 anchor the
    // legacy AutoSeeder breakpoints exactly:
    //   ε=4    → 14.0 (was AutoSeeder ε≤5 band: 14.0)
    //   ε=10   → 10.0 (was AutoSeeder ε≤10 band: 10.0)
    //   ε=25   →  8.0 (was AutoSeeder ε≤25 band:  8.0)
    //   ε=50   →  7.0 (was AutoSeeder ε≤50 band:  7.0)
    //   ε=100  →  6.0 (was AutoSeeder ε>50 band:  6.0)
    // L%-shift direction: lower L% → larger θ_e (steeper exit; per Rao).
    private static readonly double[,] ThetaE_deg =
    {
        // L%=0.60  0.70   0.80   0.90   1.00
        {  16.0,   15.0,  14.0,  13.0,  12.0 },  // ε=4
        {  15.5,   14.5,  13.5,  12.5,  11.5 },  // ε=5
        {  12.0,   11.0,  10.0,   9.0,   8.0 },  // ε=10
        {  11.5,   10.5,   9.5,   8.5,   7.5 },  // ε=15
        {  11.0,   10.0,   9.0,   8.0,   7.0 },  // ε=20
        {  10.0,    9.0,   8.0,   7.0,   6.0 },  // ε=25
        {   9.5,    8.5,   7.5,   6.5,   5.5 },  // ε=30
        {   9.2,    8.2,   7.2,   6.2,   5.2 },  // ε=40
        {   9.0,    8.0,   7.0,   6.0,   5.0 },  // ε=50
        {   8.5,    7.5,   6.5,   5.5,   4.5 },  // ε=75
        {   8.0,    7.0,   6.0,   5.0,   4.0 },  // ε=100
    };

    /// <summary>
    /// Look up the Rao thrust-optimized bell entrance + exit angles for a
    /// given expansion ratio + bell length fraction. Inputs are clamped
    /// to the table extents (ε ∈ [4, 100], L% ∈ [0.60, 1.00]); a smooth
    /// bilinear interpolation runs inside.
    /// </summary>
    /// <returns>(θ_n, θ_e) in degrees.</returns>
    public static (double thetaN_deg, double thetaE_deg) Lookup(
        double expansionRatio, double lengthFraction = 0.80)
    {
        double eps = System.Math.Clamp(expansionRatio,
            EpsilonGrid[0], EpsilonGrid[^1]);
        double lf  = System.Math.Clamp(lengthFraction,
            LengthFractionGrid[0], LengthFractionGrid[^1]);

        var (iE, tE) = BracketingFraction(EpsilonGrid, eps);
        var (iL, tL) = BracketingFraction(LengthFractionGrid, lf);

        double thetaN = Bilinear(ThetaN_deg, iE, tE, iL, tL);
        double thetaE = Bilinear(ThetaE_deg, iE, tE, iL, tL);
        return (thetaN, thetaE);
    }

    /// <summary>
    /// PH-19 (#176): bell-nozzle divergence-loss factor
    /// <c>λ_div = (1 + cos θ_e) / 2</c>. Multiplied into <c>C_F</c> as a
    /// per-design loss alongside <see cref="Optimization.OperatingConditions.NozzleCfEfficiency"/>
    /// (which post-PH-19 represents the boundary-layer + two-phase
    /// component only). θ_e is looked up via <see cref="Lookup"/> on
    /// (ε, L%); short steep bells (low L%, large θ_e) take a measurable
    /// hit, long shallow bells (high L%, small θ_e) approach unity.
    /// At ε=25 / L%=0.80 (industry default) the factor is ~0.9951
    /// (θ_e ≈ 8°); at ε=4 / L%=0.60 the factor falls to ~0.9806
    /// (θ_e ≈ 16°). Inputs clamp to the table extents identically to
    /// <see cref="Lookup"/>.
    /// <para>Reference: Sutton/Biblarz, "Rocket Propulsion Elements"
    /// 9e §3.4 (Eq. 3-34); Huzel &amp; Huang AIAA Vol. 147 §4.2.</para>
    /// </summary>
    public static double DivergenceLossFactor(
        double expansionRatio, double lengthFraction = 0.80)
    {
        double thetaE_deg = Lookup(expansionRatio, lengthFraction).thetaE_deg;
        double thetaE_rad = thetaE_deg * System.Math.PI / 180.0;
        return 0.5 * (1.0 + System.Math.Cos(thetaE_rad));
    }

    // ── bilinear plumbing ────────────────────────────────────────────

    private static (int loIdx, double frac) BracketingFraction(double[] xs, double x)
    {
        if (x <= xs[0])  return (0, 0.0);
        if (x >= xs[^1]) return (xs.Length - 2, 1.0);
        // BinarySearch returns ~insertionPoint when not found.
        int idx = System.Array.BinarySearch(xs, x);
        if (idx >= 0) return (System.Math.Min(idx, xs.Length - 2), idx == xs.Length - 1 ? 1.0 : 0.0);
        int hi = ~idx;
        int lo = hi - 1;
        double t = (x - xs[lo]) / (xs[hi] - xs[lo]);
        return (lo, t);
    }

    private static double Bilinear(double[,] grid, int iE, double tE, int iL, double tL)
    {
        double v00 = grid[iE,     iL];
        double v01 = grid[iE,     iL + 1];
        double v10 = grid[iE + 1, iL];
        double v11 = grid[iE + 1, iL + 1];
        double v0  = v00 + tL * (v01 - v00);
        double v1  = v10 + tL * (v11 - v10);
        return v0 + tE * (v1 - v0);
    }
}
