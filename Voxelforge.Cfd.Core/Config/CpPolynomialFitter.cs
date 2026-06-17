// CpPolynomialFitter.cs — Fits a degree-4 Cp(T) polynomial from PropellantState anchor
// points and derives a temperature-averaged effective γ for the SU2 IDEAL_GAS model.
//
// Sprint C.3 (2026-05-07): replaces the frozen chamber γ with
//   γ_eff = Cp_mean / (Cp_mean − R)
// where Cp_mean is the integral average of the fitted Cp(T) over [T_throat, T_chamber].
//
// For frozen-flow states (GammaThroat = GammaChamber) Cp is constant by definition, so
// the polynomial is trivially flat and IsFlatCp=true is returned — callers pass null to
// Su2ConfigInputs.PolynomialCp, keeping behaviour identical to Sprint C.2 for those runs.

using Voxelforge.Combustion;

namespace Voxelforge.Cfd.Config;

/// <summary>
/// Output of <see cref="CpPolynomialFitter.Fit"/>: a degree-4 Cp(T) polynomial fit plus
/// the derived temperature-averaged γ_eff for emission as SU2 GAMMA_VALUE.
/// </summary>
public sealed record CpPolynomialResult(
    /// <summary>
    /// Polynomial coefficients [b0..b4] so that Cp(T) ≈ b0 + b1·T + b2·T² + b3·T³ + b4·T⁴
    /// in J/(kg·K). Length is always 5; higher-order terms are zero for a linear fit.
    /// </summary>
    double[] Coefficients,
    /// <summary>
    /// Temperature-averaged effective specific-heat ratio
    /// γ_eff = Cp_mean / (Cp_mean − R), clamped to [1.05, 2.0].
    /// Emit as SU2 GAMMA_VALUE in place of the frozen chamber γ.
    /// </summary>
    double GammaEffective,
    /// <summary>
    /// True when GammaThroat ≈ GammaChamber (frozen-flow table) — polynomial is flat and
    /// γ_eff equals the chamber value. Callers should pass null to
    /// <see cref="Su2ConfigInputs.PolynomialCp"/> when this flag is set.
    /// </summary>
    bool IsFlatCp);

/// <summary>
/// Derives a Cp(T) polynomial from the two-point (chamber + throat) anchor data already
/// present in <see cref="PropellantState"/>, then integrates to get γ_eff.
/// </summary>
public static class CpPolynomialFitter
{
    private const double R_Universal = 8314.462618; // J / (kmol·K)
    private const int    NCoeff      = 5;           // degree 4 → 5 coefficients

    /// <summary>
    /// Fits a degree-4 Cp(T) polynomial using chamber and throat anchor points derived from
    /// <paramref name="gas"/>, then computes the integral-averaged γ_eff.
    /// Returns a degenerate (IsFlatCp=true) result for frozen-flow states or on any
    /// numerical failure — γ_eff is set to gas.GammaChamber in that case.
    /// </summary>
    public static CpPolynomialResult Fit(PropellantState gas)
    {
        double rGas = R_Universal / gas.MolecularWeight;

        // Guard invalid inputs
        if (!double.IsFinite(gas.ChamberTemp_K) || gas.ChamberTemp_K <= 0.0
            || !double.IsFinite(gas.Cp_Jkg)      || gas.Cp_Jkg <= 0.0
            || !double.IsFinite(gas.GammaChamber) || gas.GammaChamber <= 1.0
            || !double.IsFinite(gas.GammaThroat)  || gas.GammaThroat <= 1.0
            || !double.IsFinite(gas.MolecularWeight) || gas.MolecularWeight <= 0.0)
            return Flat(gas);

        double tC  = gas.ChamberTemp_K;
        double cpC = gas.Cp_Jkg;
        double tT  = tC * 2.0 / (gas.GammaChamber + 1.0); // isentropic at M = 1
        double cpT = gas.GammaThroat / (gas.GammaThroat - 1.0) * rGas;

        // Frozen flow: GammaThroat = GammaChamber → Cp constant → no improvement over C.2
        if (Math.Abs(gas.GammaThroat - gas.GammaChamber) < 1e-9)
            return Flat(gas);

        if (tT <= 0.0 || tT >= tC || !double.IsFinite(cpT) || cpT <= 0.0)
            return Flat(gas);

        // 7 anchor points: T linearly spaced in [tT, tC], Cp linearly interpolated
        const int N = 7;
        double[] Ts  = new double[N];
        double[] CPs = new double[N];
        for (int i = 0; i < N; i++)
        {
            double frac = i / (double)(N - 1);
            Ts[i]  = tT + frac * (tC - tT);
            CPs[i] = cpT + frac * (cpC - cpT);
        }

        double[]? coeffs = FitPolynomial(Ts, CPs);
        if (coeffs is null)
            return Flat(gas);

        double cpMean = IntegralAverage(coeffs, tT, tC);
        if (!double.IsFinite(cpMean) || cpMean <= rGas)
            return Flat(gas);

        double gammaEff = cpMean / (cpMean - rGas);
        if (!double.IsFinite(gammaEff) || gammaEff < 1.05 || gammaEff > 2.0)
            return Flat(gas);

        return new CpPolynomialResult(coeffs, gammaEff, IsFlatCp: false);
    }

    /// <summary>Evaluates the polynomial at temperature <paramref name="T"/> in K.</summary>
    public static double EvalPoly(double[] coefficients, double T)
    {
        double val = 0.0, Tj = 1.0;
        for (int j = 0; j < coefficients.Length; j++) { val += coefficients[j] * Tj; Tj *= T; }
        return val;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static CpPolynomialResult Flat(PropellantState gas)
    {
        double cpC = double.IsFinite(gas.Cp_Jkg) ? gas.Cp_Jkg : 0.0;
        double gamma = double.IsFinite(gas.GammaChamber) && gas.GammaChamber > 1.0
            ? gas.GammaChamber : 1.4;
        var coeffs = new double[NCoeff];
        coeffs[0] = cpC;
        return new CpPolynomialResult(coeffs, gamma, IsFlatCp: true);
    }

    // Integral average of Cp(T) = Σ b[j]·T^j over [tLo, tHi]:
    //   (1/(tHi−tLo)) · Σ b[j] · (tHi^(j+1) − tLo^(j+1)) / (j+1)
    private static double IntegralAverage(double[] b, double tLo, double tHi)
    {
        double range = tHi - tLo;
        if (range <= 0.0) return double.NaN;
        double integral = 0.0;
        for (int j = 0; j < b.Length; j++)
            integral += b[j] * (Math.Pow(tHi, j + 1) - Math.Pow(tLo, j + 1)) / (j + 1);
        return integral / range;
    }

    // Least-squares fit of degree-4 polynomial to (Ts[i], CPs[i]) via normal equations.
    // Returns null on a singular or numerically degenerate system.
    private static double[]? FitPolynomial(double[] Ts, double[] CPs)
    {
        int n = Ts.Length;

        // Assemble V^T V (NCoeff × NCoeff) and V^T · Cp (NCoeff)
        double[,] A = new double[NCoeff, NCoeff];
        double[]  rhs = new double[NCoeff];

        // powers[p] = Ts[k]^p, p = 0 .. 2*(NCoeff-1); reused across k.
        double[] powers = new double[2 * NCoeff - 1];

        for (int k = 0; k < n; k++)
        {
            powers[0] = 1.0;
            for (int p = 1; p < powers.Length; p++) powers[p] = powers[p - 1] * Ts[k];

            for (int r = 0; r < NCoeff; r++)
            {
                rhs[r] += powers[r] * CPs[k];
                for (int c = 0; c < NCoeff; c++)
                    A[r, c] += powers[r + c];
            }
        }

        return GaussianElimination(A, rhs);
    }

    // Gaussian elimination with partial pivoting on the NCoeff × NCoeff system.
    private static double[]? GaussianElimination(double[,] A, double[] rhs)
    {
        int n = NCoeff;
        // Augmented matrix [A | rhs]
        double[,] M = new double[n, n + 1];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++) M[r, c] = A[r, c];
            M[r, n] = rhs[r];
        }

        for (int col = 0; col < n; col++)
        {
            // Partial pivot
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(M[row, col]) > Math.Abs(M[pivot, col]))
                    pivot = row;

            if (pivot != col)
                for (int c = 0; c <= n; c++)
                    (M[col, c], M[pivot, c]) = (M[pivot, c], M[col, c]);

            double diag = M[col, col];
            if (Math.Abs(diag) < 1e-30) return null;

            for (int row = col + 1; row < n; row++)
            {
                double factor = M[row, col] / diag;
                for (int c = col; c <= n; c++)
                    M[row, c] -= factor * M[col, c];
            }
        }

        // Back-substitution
        double[] x = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            x[row] = M[row, n];
            for (int c = row + 1; c < n; c++)
                x[row] -= M[row, c] * x[c];
            x[row] /= M[row, row];
            if (!double.IsFinite(x[row])) return null;
        }
        return x;
    }
}
