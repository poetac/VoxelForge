// WaveMakingFloorRegressionTests.cs — regression guard for the Holtrop
// wave-making floor bug (red-team round-2 finding).
//
// The dominant-term wave resistance used c₁·∇·ρ·g·exp(m₁·Fn²), which leaves a
// non-zero floor c₁·∇·ρ·g at Fn = 0 (≈ 6 kN for a 600 t hull) — a body at rest
// making thousands of newtons of wave drag. That floor dominated the
// (correctly V²-scaling) friction at low speed, inflating low-Fn resistance and
// inverting the wave-making *fraction*: it ran ~0.90 at Fn≈0.05 down to ~0.29
// near the hump, so the HOLTROP_WAVE_MAKING_DOMINANT advisory fired at low speed
// instead of near the hump. The fix subtracts the floor (exp(...) − 1), giving
// the correct rest limit (R_W → 0 as V → 0) and a fraction that rises with Fn.

using Voxelforge.Marine.Hydrodynamics;

namespace Voxelforge.Marine.Tests;

public sealed class WaveMakingFloorRegressionTests
{
    // Representative coastal-cargo displacement hull.
    private static HoltropMennenResult Solve(double froude)
    {
        const double L = 40.0;
        double V = froude * System.Math.Sqrt(9.80665 * L);
        return HoltropMennenResistanceModel.Solve(
            speed_ms:            V,
            lengthWaterline_m:   L,
            beamWaterline_m:     8.0,
            draft_m:             3.0,
            blockCoefficient:    0.65,
            massDisplacement_kg: 600_000.0,
            waterDensity_kgm3:   1025.0,
            kinematicViscosity_m2s: 1.35e-6);
    }

    private static double Frac(HoltropMennenResult r)
        => r.WaveMakingResistance_N / r.TotalResistance_N;

    [Fact]
    public void WaveMakingResistance_VanishesTowardRest_NotAFloor()
    {
        // At Fn ≈ 0.05 a displacement hull is friction-dominated; wave making
        // is a small fraction. The old floored model reported ~0.90.
        double frac = Frac(Solve(0.05));
        Assert.True(frac < 0.2,
            $"low-speed wave-making fraction should be small (friction-dominated); got {frac:F3}");
    }

    [Fact]
    public void WaveMakingFraction_RisesWithFroudeNumber()
    {
        // The wave-making share must grow toward the hump. The old floored
        // model inverted this (fraction fell as Fn rose).
        double fracLow  = Frac(Solve(0.10));
        double fracHigh = Frac(Solve(0.35));
        Assert.True(fracHigh > fracLow,
            $"wave-making fraction should rise with Fn; low(0.10)={fracLow:F3}, high(0.35)={fracHigh:F3}");
    }
}
