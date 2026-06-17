// GaussianProcessSurrogateTests — Issue #199 (OOB-4 Bayesian optimization).
//
// Unit tests for the GP surrogate: training, prediction at fit points,
// prior behaviour on empty fit, Cholesky math, constructor validation.

using System;
using Voxelforge.Optimization.Bayesian;
using Xunit;

namespace Voxelforge.Tests;

public class GaussianProcessSurrogateTests
{
    [Fact]
    public void EmptyFit_ReturnsPrior()
    {
        var bounds = new[] { (0.0, 1.0), (0.0, 1.0) };
        var gp = new GaussianProcessSurrogate(bounds, lengthScale: 0.2,
            signalVariance: 1.0, noiseVariance: 1e-6);
        gp.Fit(Array.Empty<double[]>(), Array.Empty<double>());

        var (mean, variance) = gp.Predict(new double[] { 0.5, 0.5 });
        Assert.Equal(0.0, mean);
        Assert.Equal(1.0, variance);
    }

    [Fact]
    public void PredictAtTrainingPoint_ReturnsTrainingValue()
    {
        // GP with σ_n²=1e-6 should regress to within ~1e-3 of training y
        // at training x.
        var bounds = new[] { (0.0, 10.0), (0.0, 10.0) };
        var gp = new GaussianProcessSurrogate(bounds, lengthScale: 0.3,
            signalVariance: 4.0, noiseVariance: 1e-6);
        var X = new[]
        {
            new double[] { 1.0, 2.0 },
            new double[] { 3.0, 4.0 },
            new double[] { 5.0, 6.0 }
        };
        var y = new double[] { 0.5, -0.7, 1.2 };
        gp.Fit(X, y);

        for (int i = 0; i < X.Length; i++)
        {
            var (mean, variance) = gp.Predict(X[i]);
            Assert.True(Math.Abs(mean - y[i]) < 1e-3,
                $"Predicted mean {mean:G6} at training point {i} != target {y[i]}");
            Assert.True(variance < 1e-3,
                $"Variance {variance:G6} at training point {i} > 1e-3 (expected ~σ_n²)");
        }
    }

    [Fact]
    public void PredictBetweenTrainingPoints_HasUncertainty()
    {
        // Halfway between two training points, the variance should be
        // positive (and noticeably so for length scale ~ inter-point dist).
        var bounds = new[] { (0.0, 10.0) };
        var gp = new GaussianProcessSurrogate(bounds, lengthScale: 0.2,
            signalVariance: 1.0, noiseVariance: 1e-6);
        gp.Fit(
            X: new[] { new[] { 0.0 }, new[] { 10.0 } },
            y: new[] { 0.0, 1.0 });

        var (meanMid, variMid) = gp.Predict(new[] { 5.0 });
        Assert.True(variMid > 0.5,
            $"Mid-point variance {variMid:G6} should be substantial (length scale 0.2 of [0,10] band = 2.0, mid is 5.0 from each)");
        // With length scale 0.2 of the band (i.e. 2.0 in real units),
        // the point at distance 5 from each training point is far enough
        // that the GP reverts substantially toward the prior mean (= 0).
        Assert.True(meanMid > -0.5 && meanMid < 1.5);
    }

    [Fact]
    public void Determinism_SameTrainingProducesSamePredictions()
    {
        var bounds = new[] { (0.0, 5.0), (0.0, 5.0) };
        var gp1 = new GaussianProcessSurrogate(bounds, lengthScale: 0.25,
            signalVariance: 2.0, noiseVariance: 1e-6);
        var gp2 = new GaussianProcessSurrogate(bounds, lengthScale: 0.25,
            signalVariance: 2.0, noiseVariance: 1e-6);
        var X = new[]
        {
            new double[] { 1.0, 1.0 },
            new double[] { 2.0, 3.0 },
            new double[] { 4.0, 0.5 }
        };
        var y = new double[] { 0.1, 0.5, -0.3 };
        gp1.Fit(X, y);
        gp2.Fit(X, y);

        var probe = new double[] { 2.5, 2.5 };
        var (m1, v1) = gp1.Predict(probe);
        var (m2, v2) = gp2.Predict(probe);
        Assert.Equal(m1, m2);
        Assert.Equal(v1, v2);
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        var bounds = new[] { (0.0, 1.0), (0.0, 1.0) };
        var lscales = new double[] { 0.2, 0.2 };

        Assert.Throws<ArgumentNullException>(() =>
            new GaussianProcessSurrogate(null!, lscales, 1.0, 1e-6));
        Assert.Throws<ArgumentNullException>(() =>
            new GaussianProcessSurrogate(bounds, (double[])null!, 1.0, 1e-6));
        Assert.Throws<ArgumentException>(() =>
            new GaussianProcessSurrogate(bounds, new double[] { 0.2 }, 1.0, 1e-6));    // dim mismatch
        Assert.Throws<ArgumentException>(() =>
            new GaussianProcessSurrogate(Array.Empty<(double, double)>(), Array.Empty<double>(), 1.0, 1e-6));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GaussianProcessSurrogate(bounds, lscales, 0.0, 1e-6));   // signalVariance > 0
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GaussianProcessSurrogate(bounds, lscales, 1.0, -1.0));   // noiseVariance >= 0
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GaussianProcessSurrogate(bounds, new double[] { 0.0, 0.2 }, 1.0, 1e-6));   // lengthScale > 0
        // Inverted bounds → ArgumentException.
        var badBounds = new[] { (1.0, 0.0), (0.0, 1.0) };
        Assert.Throws<ArgumentException>(() =>
            new GaussianProcessSurrogate(badBounds, lscales, 1.0, 1e-6));
    }

    [Fact]
    public void Predict_BeforeFit_Throws()
    {
        var bounds = new[] { (0.0, 1.0) };
        var gp = new GaussianProcessSurrogate(bounds, 0.2, 1.0, 1e-6);
        Assert.Throws<InvalidOperationException>(() => gp.Predict(new double[] { 0.5 }));
    }

    [Fact]
    public void TrainingSize_ReportsRowCount()
    {
        var bounds = new[] { (0.0, 1.0) };
        var gp = new GaussianProcessSurrogate(bounds, 0.2, 1.0, 1e-6);
        Assert.Equal(0, gp.TrainingSize);
        gp.Fit(
            new[] { new[] { 0.1 }, new[] { 0.5 }, new[] { 0.9 } },
            new[] { 1.0, 2.0, 3.0 });
        Assert.Equal(3, gp.TrainingSize);
    }

    [Fact]
    public void Cholesky_3x3_RoundTrip()
    {
        // 3×3 SPD matrix; verify L · L^T == A.
        var A = new[]
        {
            new[] { 4.0, 2.0, 0.5 },
            new[] { 2.0, 5.0, 1.0 },
            new[] { 0.5, 1.0, 3.0 }
        };
        var L = GaussianProcessSurrogate.Cholesky(A);
        // Recompute A' = L · L^T.
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            double sum = 0.0;
            for (int k = 0; k <= Math.Min(i, j); k++)
                sum += L[i][k] * L[j][k];
            Assert.True(Math.Abs(sum - A[i][j]) < 1e-9,
                $"L·L^T[{i},{j}] = {sum:G9}, expected {A[i][j]}");
        }
    }

    [Fact]
    public void CholeskySolve_3x3_RoundTrip()
    {
        // Solve A x = b and verify A x ≈ b.
        var A = new[]
        {
            new[] { 4.0, 2.0, 0.5 },
            new[] { 2.0, 5.0, 1.0 },
            new[] { 0.5, 1.0, 3.0 }
        };
        var L = GaussianProcessSurrogate.Cholesky(A);
        var b = new double[] { 1.0, 2.0, 3.0 };
        var x = GaussianProcessSurrogate.CholeskySolve(L, (double[])b.Clone());
        // Verify A x ≈ b.
        for (int i = 0; i < 3; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < 3; j++) sum += A[i][j] * x[j];
            Assert.True(Math.Abs(sum - b[i]) < 1e-9,
                $"A·x[{i}] = {sum:G9}, expected {b[i]}");
        }
    }

    [Fact]
    public void Cholesky_NonPdMatrix_Throws()
    {
        // Matrix with negative pivot.
        var A = new[]
        {
            new[] { -1.0, 0.0 },
            new[] { 0.0, 1.0 }
        };
        Assert.Throws<InvalidOperationException>(() =>
            GaussianProcessSurrogate.Cholesky(A));
    }
}
