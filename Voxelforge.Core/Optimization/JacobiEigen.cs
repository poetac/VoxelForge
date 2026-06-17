// JacobiEigen.cs — Symmetric-matrix eigendecomposition via Jacobi
// rotations. Issue #157 (T1.3 — CMA-ES inner-loop optimizer).
//
// CMA-ES requires the eigendecomposition of an n×n symmetric
// covariance matrix at every generation (to sample candidates from
// N(m, σ²·C)). For n ≤ 24 (the continuous core of voxelforge's SA
// vector), the Jacobi rotation method is fast (O(n³) per call,
// ~10-50 µs at n=24) and dependency-free — vs pulling MathNet.Numerics
// or System.Numerics for one eigendecomp. Implements the cyclic
// Jacobi method per Press et al. "Numerical Recipes" 3e §11.1.

using System;

namespace Voxelforge.Optimization;

/// <summary>
/// Symmetric-matrix eigendecomposition via Jacobi rotations.
/// Stable + accurate for symmetric n×n matrices at small n
/// (n ≤ 100 in practice). Used by <see cref="CmaEsOptimizer"/> on
/// covariance updates.
/// </summary>
internal static class JacobiEigen
{
    /// <summary>
    /// Decompose a symmetric matrix <paramref name="A"/> as
    /// <c>A = V · diag(eigenvalues) · V^T</c>. On return,
    /// <paramref name="A"/> is overwritten with eigenvalues on the
    /// diagonal (other entries become near-zero). Eigenvectors are
    /// stored as columns of <paramref name="V"/>.
    /// </summary>
    /// <param name="A">
    /// Symmetric n×n matrix. Mutated in place — the caller should
    /// pass a copy if the original is needed afterward.
    /// </param>
    /// <param name="V">
    /// Pre-allocated n×n matrix. Receives the orthonormal eigenvector
    /// columns. Initial values ignored (overwritten with the identity
    /// before iteration).
    /// </param>
    /// <param name="eigenvalues">
    /// Pre-allocated length-n array. Receives the eigenvalues in the
    /// order they appear on the post-iteration A diagonal (NOT sorted).
    /// </param>
    /// <param name="maxSweeps">
    /// Maximum number of Jacobi sweeps. 50 sweeps converges to
    /// machine precision for typical symmetric matrices at n ≤ 100.
    /// </param>
    /// <returns>
    /// Number of sweeps used (≤ maxSweeps). Hits maxSweeps only on
    /// pathological matrices.
    /// </returns>
    public static int Decompose(double[,] A, double[,] V, double[] eigenvalues, int maxSweeps = 50)
    {
        int n = A.GetLength(0);
        if (A.GetLength(1) != n) throw new ArgumentException("A must be square", nameof(A));
        if (V.GetLength(0) != n || V.GetLength(1) != n)
            throw new ArgumentException("V must be n×n", nameof(V));
        if (eigenvalues.Length != n) throw new ArgumentException("eigenvalues length mismatch", nameof(eigenvalues));

        // Initialise V as the identity matrix.
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
            V[i, j] = i == j ? 1.0 : 0.0;

        // Cyclic Jacobi sweeps.
        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            // Sum off-diagonal absolutes — convergence check.
            double off = 0.0;
            for (int p = 0; p < n - 1; p++)
            for (int q = p + 1; q < n; q++)
                off += Math.Abs(A[p, q]);
            if (off < 1e-14)
            {
                CopyDiagonal(A, eigenvalues);
                return sweep + 1;
            }

            // Sweep through all (p, q) pairs.
            for (int p = 0; p < n - 1; p++)
            for (int q = p + 1; q < n; q++)
            {
                double apq = A[p, q];
                if (Math.Abs(apq) < 1e-30) continue;

                double app = A[p, p];
                double aqq = A[q, q];
                double theta = (aqq - app) / (2.0 * apq);
                double t = Math.Sign(theta) /
                    (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                if (theta == 0.0) t = 1.0;
                double c = 1.0 / Math.Sqrt(1.0 + t * t);
                double s = t * c;

                // Rotate A.
                A[p, p] = app - t * apq;
                A[q, q] = aqq + t * apq;
                A[p, q] = 0.0;
                A[q, p] = 0.0;
                for (int i = 0; i < n; i++)
                {
                    if (i != p && i != q)
                    {
                        double aip = A[i, p];
                        double aiq = A[i, q];
                        A[i, p] = c * aip - s * aiq;
                        A[p, i] = A[i, p];
                        A[i, q] = s * aip + c * aiq;
                        A[q, i] = A[i, q];
                    }
                }
                // Rotate V.
                for (int i = 0; i < n; i++)
                {
                    double vip = V[i, p];
                    double viq = V[i, q];
                    V[i, p] = c * vip - s * viq;
                    V[i, q] = s * vip + c * viq;
                }
            }
        }
        CopyDiagonal(A, eigenvalues);
        return maxSweeps;
    }

    private static void CopyDiagonal(double[,] A, double[] eigenvalues)
    {
        int n = A.GetLength(0);
        for (int i = 0; i < n; i++) eigenvalues[i] = A[i, i];
    }
}
