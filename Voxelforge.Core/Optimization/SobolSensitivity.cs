// OOB-5 (2026-04-25, originally Voxelforge.Benchmarks): Sobol sensitivity
// indices via Saltelli sampling.
//
// Moved from Voxelforge.Benchmarks → Voxelforge.Core (Optimization namespace)
// for OOB-13 part 2 Phase 2 (issue #347) so the Core-side gate-lever
// ranker can consume it without an inverted dependency. Existing
// callers (Benchmarks SobolSensitivityCli + Tests) continue to work
// via InternalsVisibleTo.
//
// Computes first-order S_i and total ST_i sensitivity indices for a
// scalar score function over a D-dimensional input domain. Identifies
// which of the SA design variables actually move scoring — a
// prerequisite for any SA-band tightening, dimension freezing, or
// CMA-ES inner-loop selection. Phase 2 of issue #347 also uses it to
// rank gate levers by per-gate sensitivity to their coupled variables.
//
// Method: Saltelli (2010) extension of Sobol's pick-freeze scheme.
// Generates two N×D base sample matrices A, B; for each dim i builds
// Ai = A with column i replaced by B's column i; evaluates the
// function on A, B, and each Ai (so total N(D+2) evals).
//
// References:
//   • Sobol, I.M. (2001). "Global sensitivity indices for nonlinear
//     mathematical models and their Monte Carlo estimates."
//     Mathematics and Computers in Simulation 55: 271-280.
//   • Saltelli, A. et al. (2010). "Variance based sensitivity analysis
//     of model output. Design and estimator for the total sensitivity
//     index." Computer Physics Communications 181: 259-270.
//
// Estimator forms used (Saltelli 2010 eqs. b, f):
//   S_i  = (1/N) Σ y_B,j · (y_Ai,j − y_A,j)        / Var(y)
//   ST_i = (1/(2N)) Σ (y_A,j − y_Ai,j)²           / Var(y)
//
// Sampling note: this implementation uses a seeded uniform RNG, NOT a
// Sobol low-discrepancy sequence. Uniform sampling produces noisier
// estimates than quasi-MC at the same N but is materially simpler
// (zero external dependency) and is a faithful Monte Carlo estimator.
// A future enhancement could add a Sobol/Halton sequence for ~3-5×
// variance reduction at the same N.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Optimization;

internal sealed record SobolIndex(
    int    DimIndex,
    string DimName,
    double FirstOrder,    // S_i — share of Var(y) explained by dim i alone
    double Total);        // ST_i — share of Var(y) involving dim i (incl. interactions)

internal static class SobolSensitivity
{
    /// <summary>
    /// Compute first-order + total Sobol sensitivity indices for a
    /// scalar function f over a D-dimensional unit hypercube.
    /// </summary>
    /// <param name="f">Score function: x ∈ [0,1]^D → ℝ. Should be deterministic.</param>
    /// <param name="dimNames">Optional human-readable names per dim.</param>
    /// <param name="N">Saltelli sample size; total evals = N(D+2).</param>
    /// <param name="seed">RNG seed for reproducibility.</param>
    public static SobolIndex[] Compute(
        Func<double[], double> f, int D,
        int N = 512, int seed = 42, IReadOnlyList<string>? dimNames = null)
    {
        if (D < 1) throw new ArgumentOutOfRangeException(nameof(D), "D must be ≥ 1");
        if (N < 16) throw new ArgumentOutOfRangeException(nameof(N), "N must be ≥ 16");

        var rng = new Random(seed);
        // Generate two independent N×D sample matrices.
        var A = SampleMatrix(N, D, rng);
        var B = SampleMatrix(N, D, rng);

        // Evaluate baseline samples (y_A, y_B).
        var yA = EvaluateRows(f, A);
        var yB = EvaluateRows(f, B);

        // Pooled mean + variance for normalization.
        double mean = (yA.Average() + yB.Average()) * 0.5;
        double varY = 0.0;
        for (int j = 0; j < N; j++)
        {
            double dA = yA[j] - mean;
            double dB = yB[j] - mean;
            varY += dA * dA + dB * dB;
        }
        varY /= (2 * N);

        // Guard against degenerate cases (constant function — varY ≈ 0).
        // Return zero indices rather than NaN.
        if (varY < 1e-300)
        {
            return Enumerable.Range(0, D)
                .Select(i => new SobolIndex(i, NameOf(dimNames, i), 0.0, 0.0))
                .ToArray();
        }

        var indices = new SobolIndex[D];
        for (int i = 0; i < D; i++)
        {
            // Build A_i: A with column i replaced by B's column i.
            var Ai = (double[][])A.Clone();
            for (int j = 0; j < N; j++)
            {
                Ai[j] = (double[])A[j].Clone();
                Ai[j][i] = B[j][i];
            }
            var yAi = EvaluateRows(f, Ai);

            // Saltelli 2010 estimators.
            double sumS  = 0.0;
            double sumST = 0.0;
            for (int j = 0; j < N; j++)
            {
                sumS  += yB[j] * (yAi[j] - yA[j]);
                double diff = yA[j] - yAi[j];
                sumST += diff * diff;
            }
            double S  = (sumS / N) / varY;
            double ST = (sumST / (2.0 * N)) / varY;

            indices[i] = new SobolIndex(i, NameOf(dimNames, i), S, ST);
        }
        return indices;
    }

    private static string NameOf(IReadOnlyList<string>? names, int i) =>
        names is { Count: > 0 } && i < names.Count ? names[i] : $"dim[{i}]";

    private static double[][] SampleMatrix(int N, int D, Random rng)
    {
        var M = new double[N][];
        for (int j = 0; j < N; j++)
        {
            var row = new double[D];
            for (int i = 0; i < D; i++) row[i] = rng.NextDouble();
            M[j] = row;
        }
        return M;
    }

    private static double[] EvaluateRows(Func<double[], double> f, double[][] M)
    {
        var y = new double[M.Length];
        for (int j = 0; j < M.Length; j++) y[j] = f(M[j]);
        return y;
    }

    /// <summary>
    /// Format the indices as a sorted markdown-style table (descending
    /// by total ST_i). Used by the --sobol-sensitivity CLI dispatch.
    /// </summary>
    public static string FormatSortedTable(SobolIndex[] indices)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Dim | Name                                | S_i      | ST_i     |");
        sb.AppendLine("|----:|-------------------------------------|---------:|---------:|");
        foreach (var idx in indices.OrderByDescending(x => x.Total))
        {
            // InvariantCulture: the F4 numbers must use '.' decimals regardless
            // of the host locale (a comma-decimal culture like de-DE would
            // otherwise corrupt the table). Matches GateExplainer.AppendRankedTable.
            sb.AppendLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "| {0,3} | {1,-35} | {2,8:F4} | {3,8:F4} |",
                idx.DimIndex, idx.DimName, idx.FirstOrder, idx.Total));
        }
        return sb.ToString();
    }
}
