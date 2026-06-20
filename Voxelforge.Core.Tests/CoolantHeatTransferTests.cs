// CoolantHeatTransferTests.cs — cross-platform coverage for the coolant-side
// friction and channel-curvature correlations in Voxelforge.Core (the regen
// circuit). Petukhov smooth-tube friction, Haaland rough-channel friction
// (LPBF-printed surfaces), and the Dravid Dean-number Nusselt enhancement for
// helical channels — each checked against a textbook reference point and the
// invariants it must satisfy. All PicoGK-free, so they run on the Linux CI.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Core.Tests;

public sealed class CoolantHeatTransferTests
{
    private static void AssertRelClose(double expected, double actual, double relTol = 1e-9)
    {
        double tol = Math.Max(Math.Abs(expected) * relTol, 1e-12);
        Assert.True(Math.Abs(expected - actual) <= tol,
            $"expected {expected} (±{tol}), got {actual}");
    }

    // ───────────────────────── Petukhov smooth-tube friction ─────────────────────────

    [Fact]
    public void Friction_Petukhov_At1e5_MatchesMoodyChart()
    {
        // Smooth turbulent Darcy friction factor at Re = 1e5 is ~0.018 (Moody chart).
        double f = CoolantCorrelations.FrictionFactor(1.0e5);
        Assert.InRange(f, 0.0175, 0.0185);
    }

    [Fact]
    public void Friction_FallsWithReynoldsInTurbulentRegime()
    {
        double f4 = CoolantCorrelations.FrictionFactor(1.0e4);
        double f5 = CoolantCorrelations.FrictionFactor(1.0e5);
        double f6 = CoolantCorrelations.FrictionFactor(1.0e6);
        Assert.True(f4 > f5 && f5 > f6, $"expected falling f, got {f4}, {f5}, {f6}");
    }

    [Fact]
    public void Friction_LaminarFallback_MatchesSixtyFourOverRe()
    {
        // Below Re = 4000 the model falls back to laminar f = 64/Re.
        double f = CoolantCorrelations.FrictionFactor(2_000.0);
        AssertRelClose(64.0 / 2_000.0, f);
    }

    // ───────────────────────── Haaland rough-channel friction ─────────────────────────

    [Fact]
    public void Friction_ZeroRoughness_FallsBackToSmoothTube()
    {
        // Documented: ε/D = 0 reproduces the Petukhov smooth-tube value bit-for-bit.
        double rough0 = CoolantCorrelations.FrictionFactor(1.0e5, 0.0);
        double smooth = CoolantCorrelations.FrictionFactor(1.0e5);
        Assert.Equal(smooth, rough0);
    }

    [Fact]
    public void Friction_RoughChannel_ExceedsSmoothChannel()
    {
        // LPBF channels (ε/D ≈ 0.01–0.05) carry more friction than a smooth tube.
        double smooth = CoolantCorrelations.FrictionFactor(1.0e5, 0.0);
        double rough = CoolantCorrelations.FrictionFactor(1.0e5, 0.03);
        Assert.True(rough > smooth, $"rough {rough} should exceed smooth {smooth}");
    }

    [Fact]
    public void Friction_RisesWithRelativeRoughness()
    {
        double low = CoolantCorrelations.FrictionFactor(1.0e5, 0.01);
        double high = CoolantCorrelations.FrictionFactor(1.0e5, 0.05);
        Assert.True(high > low, $"f should rise with roughness: {high} !> {low}");
    }

    // ───────────────────────── Dravid Dean-number enhancement ─────────────────────────

    [Fact]
    public void Dean_StraightChannel_HasNoEnhancement()
    {
        // R_curv → ∞ (pure axial flow) and R_curv = 0 both give a unity multiplier.
        Assert.Equal(1.0, CoolantCorrelations.DeanNumberNuMultiplier(0.002, double.PositiveInfinity));
        Assert.Equal(1.0, CoolantCorrelations.DeanNumberNuMultiplier(0.002, 0.0));
    }

    [Fact]
    public void Dean_CurvedChannel_EnhancesHeatTransfer()
    {
        // A helix (finite curvature, coil wider than channel) lifts Nu above straight-tube.
        double mult = CoolantCorrelations.DeanNumberNuMultiplier(0.002, 0.05);
        Assert.True(mult > 1.0, $"curved channel should enhance Nu, got {mult}");
    }

    [Fact]
    public void Dean_KnownGeometry_MatchesDravidValue()
    {
        // D_h = 2 mm wrapped on a 50 mm-radius coil → D_curv = 100 mm,
        // ratio = 0.002/0.1 = 0.02, multiplier = 1 + 3.6·(1−0.02)·√0.02 ≈ 1.4989342.
        double mult = CoolantCorrelations.DeanNumberNuMultiplier(0.002, 0.05);
        AssertRelClose(1.4989342, mult, 1e-6);
    }

    [Fact]
    public void Dean_ChannelWiderThanCoil_DegeneratesToUnity()
    {
        // ratio ≥ 1 (channel wider than the coil it wraps) is degenerate → no enhancement.
        Assert.Equal(1.0, CoolantCorrelations.DeanNumberNuMultiplier(0.5, 0.1));
    }

    [Fact]
    public void Friction_IsDeterministic()
    {
        double a = CoolantCorrelations.FrictionFactor(1.23e5, 0.024);
        double b = CoolantCorrelations.FrictionFactor(1.23e5, 0.024);
        Assert.Equal(a, b);
    }
}
