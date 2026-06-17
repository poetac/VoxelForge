// OOB-5 (2026-04-25): Sobol sensitivity math correctness — validate
// the Saltelli estimators against Sobol's canonical g-function with
// known closed-form indices.
//
// g-function (Saltelli 2010 §4.1):
//   g(x_1, ..., x_D) = ∏_i (|4 x_i − 2| + a_i) / (1 + a_i)
//
// First-order indices have a closed form:
//   V_i  = 1 / (3 (1 + a_i)²)
//   Var(g) = ∏_i (1 + V_i) − 1
//   S_i  = V_i / Var(g)
//
// Total indices have a closed form too (Saltelli 2010 eq. 8):
//   ST_i = (V_i ∏_{k≠i}(1 + V_k)) / Var(g)
//
// We use a = [0, 1, 4.5, 9, 99, 99, 99, 99] (D=8). Dim 0 dominates;
// dims 4-7 are vanishingly small.

using System;
using System.Linq;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class SobolSensitivityTests
{
    private static (double S, double ST)[] InvokeCompute(
        Func<double[], double> f, int D, int N, int seed)
    {
        var indices = SobolSensitivity.Compute(f, D, N: N, seed: seed);
        return indices.Select(idx => (idx.FirstOrder, idx.Total)).ToArray();
    }

    // ── canonical g-function ────────────────────────────────────────

    private static readonly double[] GA =
        { 0.0, 1.0, 4.5, 9.0, 99.0, 99.0, 99.0, 99.0 };

    private static double GFunction(double[] x)
    {
        double prod = 1.0;
        for (int i = 0; i < x.Length; i++)
            prod *= (Math.Abs(4.0 * x[i] - 2.0) + GA[i]) / (1.0 + GA[i]);
        return prod;
    }

    // Closed-form first-order index per Saltelli 2010.
    private static double GFirstOrder(int i, double[] a)
    {
        double Vi = 1.0 / (3.0 * (1.0 + a[i]) * (1.0 + a[i]));
        double VarY = 1.0;
        for (int k = 0; k < a.Length; k++)
        {
            double Vk = 1.0 / (3.0 * (1.0 + a[k]) * (1.0 + a[k]));
            VarY *= (1.0 + Vk);
        }
        VarY -= 1.0;
        return Vi / VarY;
    }

    // ── tests ───────────────────────────────────────────────────────

    [Fact]
    public void GFunction_S_OnDominantDim_Within10Pct_OfClosedForm()
    {
        // Dim 0 (a=0) dominates: S_0 ≈ 0.716. Saltelli MC variance at
        // N=2048 typically ≈ ±0.05 absolute → within 10% relative.
        var pairs = InvokeCompute(GFunction, GA.Length, N: 2048, seed: 42);
        double S0_actual = pairs[0].S;
        double S0_expected = GFirstOrder(0, GA);
        double relErr = Math.Abs(S0_actual - S0_expected) / S0_expected;
        Assert.True(relErr < 0.10,
            $"S_0 = {S0_actual:F4}, expected ≈ {S0_expected:F4}, relErr {relErr:P1} > 10%.");
    }

    [Fact]
    public void GFunction_S_OnNegligibleDims_NearZero()
    {
        // Dims 4-7 (a=99) have S ≈ 1.6e-7 — Saltelli at N=2048 will
        // typically produce noise in [-0.05, +0.05]. Assert |S| < 0.10
        // (tolerant — this is testing "essentially zero", not a precise
        // match).
        var pairs = InvokeCompute(GFunction, GA.Length, N: 2048, seed: 42);
        for (int i = 4; i < GA.Length; i++)
        {
            Assert.True(Math.Abs(pairs[i].S) < 0.10,
                $"S_{i} = {pairs[i].S:F4} should be ≈ 0 (a_i = 99).");
        }
    }

    [Fact]
    public void GFunction_TotalIndices_CoverFirstOrder()
    {
        // For an additive function ST_i ≥ S_i; for any function with
        // interactions, ST_i should still be ≥ S_i within MC noise.
        var pairs = InvokeCompute(GFunction, GA.Length, N: 2048, seed: 42);
        for (int i = 0; i < pairs.Length; i++)
        {
            // Allow 0.05 absolute MC slack on each side.
            Assert.True(pairs[i].ST >= pairs[i].S - 0.05,
                $"ST_{i}={pairs[i].ST:F4} < S_{i}={pairs[i].S:F4} (should be ≥ within noise).");
        }
    }

    [Fact]
    public void ConstantFunction_AllIndicesZero()
    {
        // A function that ignores all inputs has Var(y) = 0 — guard the
        // estimator against divide-by-zero.
        var pairs = InvokeCompute((double[] _) => 42.0, D: 4, N: 64, seed: 7);
        Assert.All(pairs, p =>
        {
            Assert.Equal(0.0, p.S);
            Assert.Equal(0.0, p.ST);
        });
    }

    [Fact]
    public void OnlyOneDimMatters_S_OnThatDimIsHigh()
    {
        // f(x) = x_2 — only dim 2 affects output.
        var pairs = InvokeCompute(x => x[2], D: 5, N: 1024, seed: 1);
        // Dim 2 should be the dominant first-order contributor.
        int maxIdx = 0;
        for (int i = 1; i < pairs.Length; i++)
            if (pairs[i].S > pairs[maxIdx].S) maxIdx = i;
        Assert.Equal(2, maxIdx);
        // S_2 should be very high (theoretically 1.0).
        Assert.True(pairs[2].S > 0.7,
            $"S_2 = {pairs[2].S:F4} for f=x_2 should approach 1.0 at N=1024.");
    }
}
