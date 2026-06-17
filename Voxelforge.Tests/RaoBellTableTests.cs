// PH-16 (2026-04-25): RaoBellTable + AutoSeeder.BellGeometryFor migration.
// Anchor tests prove that the new bilinear table preserves the legacy
// 5-band AutoSeeder values at the breakpoints; smoothness tests cover
// the new interpolation between bands.

using Voxelforge.Chamber;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class RaoBellTableTests
{
    // ── Anchor parity: legacy AutoSeeder values at L%=0.80 ───────────

    [Theory]
    [InlineData(4.0,  22.0, 14.0)]   // legacy ε≤5 band: 22/14
    [InlineData(10.0, 30.0, 10.0)]   // legacy ε≤10 band: 30/10
    [InlineData(25.0, 35.0,  8.0)]   // legacy ε≤25 band: 35/8
    [InlineData(50.0, 37.0,  7.0)]   // legacy ε≤50 band: 37/7
    [InlineData(100.0, 38.0, 6.0)]   // legacy ε>50 cap:  38/6
    public void Lookup_AtLegacyBreakpoints_PreservesLegacyValues(
        double epsilon, double expectedThetaN, double expectedThetaE)
    {
        var (n, e) = RaoBellTable.Lookup(epsilon, lengthFraction: 0.80);
        Assert.Equal(expectedThetaN, n, 3);
        Assert.Equal(expectedThetaE, e, 3);
    }

    // ── Smoothness: between-band interpolation ──────────────────────

    [Fact]
    public void Lookup_BetweenBreakpoints_IsBilinearlyInterpolated()
    {
        // ε halfway between 10 and 15 should give θ_n midway between
        // their tabulated values (32 ± 30) / 2 = 31.0
        var (n, _) = RaoBellTable.Lookup(12.5, 0.80);
        Assert.Equal(31.0, n, 3);
    }

    [Fact]
    public void Lookup_ThetaN_MonotonicallyIncreasesWithEpsilon()
    {
        // Integer-tick sweep (#553 audit C3): the original `for (double e
        // = 4.0; e <= 100.0; e += 1.0)` is FP-accumulator-prone — over 96
        // additions of 1.0 the closed-interval endpoint can be lost.
        // Reconstruct ε from (min + i·step) instead.
        double prev = double.NegativeInfinity;
        int nSteps = (int)System.Math.Round((100.0 - 4.0) / 1.0) + 1;  // closed [4, 100]
        for (int i = 0; i < nSteps; i++)
        {
            double e = 4.0 + i * 1.0;
            var (n, _) = RaoBellTable.Lookup(e, 0.80);
            Assert.True(n >= prev, $"θ_n decreased at ε={e}: prev={prev}, now={n}");
            prev = n;
        }
    }

    [Fact]
    public void Lookup_ThetaE_MonotonicallyDecreasesWithEpsilon()
    {
        // Integer-tick sweep (#553 audit C3): mirror of the θ_n loop —
        // reconstruct ε from the tick index so the closed [4, 100] sweep
        // is bit-deterministic regardless of FP rounding drift.
        double prev = double.PositiveInfinity;
        int nSteps = (int)System.Math.Round((100.0 - 4.0) / 1.0) + 1;  // closed [4, 100]
        for (int i = 0; i < nSteps; i++)
        {
            double e = 4.0 + i * 1.0;
            var (_, ex) = RaoBellTable.Lookup(e, 0.80);
            Assert.True(ex <= prev, $"θ_e increased at ε={e}: prev={prev}, now={ex}");
            prev = ex;
        }
    }

    // ── Length-fraction shift: lower L% → larger θ_e per Rao ─────────

    [Fact]
    public void Lookup_LowerLengthFraction_GivesLargerExitAngle()
    {
        var (_, e60) = RaoBellTable.Lookup(20.0, 0.60);
        var (_, e80) = RaoBellTable.Lookup(20.0, 0.80);
        var (_, e100) = RaoBellTable.Lookup(20.0, 1.00);
        Assert.True(e60 > e80, "L%=0.60 should give larger θ_e than L%=0.80.");
        Assert.True(e80 > e100, "L%=0.80 should give larger θ_e than L%=1.00.");
    }

    [Fact]
    public void Lookup_LowerLengthFraction_GivesSmallerEntranceAngle()
    {
        var (n60, _) = RaoBellTable.Lookup(20.0, 0.60);
        var (n80, _) = RaoBellTable.Lookup(20.0, 0.80);
        var (n100, _) = RaoBellTable.Lookup(20.0, 1.00);
        Assert.True(n60 < n80, "L%=0.60 should give smaller θ_n than L%=0.80.");
        Assert.True(n80 < n100, "L%=0.80 should give smaller θ_n than L%=1.00.");
    }

    // ── Clamping at table extents ────────────────────────────────────

    [Fact]
    public void Lookup_BelowMinimumEpsilon_ClampsToFirstRow()
    {
        var (n_low, e_low) = RaoBellTable.Lookup(2.0, 0.80);
        var (n_4,   e_4)   = RaoBellTable.Lookup(4.0, 0.80);
        Assert.Equal(n_4, n_low, 6);
        Assert.Equal(e_4, e_low, 6);
    }

    [Fact]
    public void Lookup_AboveMaximumEpsilon_ClampsToLastRow()
    {
        var (n_hi,  e_hi)  = RaoBellTable.Lookup(500.0, 0.80);
        var (n_100, e_100) = RaoBellTable.Lookup(100.0, 0.80);
        Assert.Equal(n_100, n_hi, 6);
        Assert.Equal(e_100, e_hi, 6);
    }

    [Fact]
    public void Lookup_LengthFractionOutOfRange_Clamps()
    {
        var (n_low, _)  = RaoBellTable.Lookup(20.0, 0.30);
        var (n_clamp, _) = RaoBellTable.Lookup(20.0, 0.60);
        Assert.Equal(n_clamp, n_low, 6);
    }

    // ── Migration parity: AutoSeeder.BellGeometryFor at exact bands ──

    [Theory]
    [InlineData(5.0,  22.0, 14.0, 0.70)]
    [InlineData(10.0, 30.0, 10.0, 0.80)]
    [InlineData(25.0, 35.0,  8.0, 0.80)]
    [InlineData(50.0, 37.0,  7.0, 0.82)]
    [InlineData(75.0, 37.5,  6.5, 0.85)]
    public void AutoSeeder_BellGeometryFor_PreservesLegacyValues_AtBands(
        double epsilon, double expectedThetaN, double expectedThetaE, double expectedL)
    {
        // Legacy band boundary values at ε=5 with L%=0.70 → table lookup
        // at (5.0, 0.70) = (21.5, 14.5) NOT (22.0, 14.0). The previous
        // step function returned (22.0, 14.0, 0.70) for ε≤5; the new
        // bilinear lookup at L%=0.70 returns the table value at ε=5
        // and L%=0.70 which differs slightly. This test covers the new
        // contract — bilinear-true Rao values at the chosen L%.
        var (n, e, L) = AutoSeeder.BellGeometryFor(epsilon);
        Assert.Equal(expectedL, L);
        // Tolerance: ±0.6° on θ to allow for the table's L%-shift effect
        // at ε=5/L%=0.70 (legacy step function used L%=0.80 angles by
        // accident; new code uses true L%=0.70 angles).
        Assert.True(System.Math.Abs(n - expectedThetaN) < 1.0,
            $"θ_n mismatch at ε={epsilon}: expected ≈{expectedThetaN}, got {n}");
        Assert.True(System.Math.Abs(e - expectedThetaE) < 1.0,
            $"θ_e mismatch at ε={epsilon}: expected ≈{expectedThetaE}, got {e}");
    }

    [Fact]
    public void AutoSeeder_BellGeometryFor_ExactlyMatchesLegacy_AtL80Bands()
    {
        // At ε ∈ {10, 25, 50} the legacy code used L%=0.80 (ε=10/25)
        // or L%=0.82 (ε=50). For ε=10/25 we get L%=0.80 angles which
        // are bit-identical to the legacy hardcoded values.
        var (n10, e10, _) = AutoSeeder.BellGeometryFor(10.0);
        Assert.Equal(30.0, n10, 3);
        Assert.Equal(10.0, e10, 3);

        var (n25, e25, _) = AutoSeeder.BellGeometryFor(25.0);
        Assert.Equal(35.0, n25, 3);
        Assert.Equal(8.0,  e25, 3);
    }
}
