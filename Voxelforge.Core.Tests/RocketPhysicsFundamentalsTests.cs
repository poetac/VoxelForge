// RocketPhysicsFundamentalsTests.cs — cross-platform regression coverage for
// the headless rocket-physics primitives in Voxelforge.Core.
//
// Each test exercises a closed-form physics relation against either a textbook
// reference point (NIST saturation pressures, Huzel & Huang orifice flow,
// Newton's law of cooling) or a physically necessary invariant (sign,
// monotonicity, sqrt / linear scaling, round-trip consistency, determinism).
// They are PicoGK-free, so they run on the GitHub-hosted Linux CI and give the
// flagship rocket pillar a runtime green signal independent of the self-hosted
// Windows voxel suite.

using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;

namespace Voxelforge.Core.Tests;

public sealed class RocketPhysicsFundamentalsTests
{
    private const double OneAtm_Pa = 101_325.0;

    private static void AssertRelClose(double expected, double actual, double relTol = 1e-9)
    {
        double tol = Math.Max(Math.Abs(expected) * relTol, 1e-12);
        Assert.True(Math.Abs(expected - actual) <= tol,
            $"expected {expected} (±{tol}), got {actual}");
    }

    // ───────────────────────── Antoine vapor pressure ─────────────────────────

    [Fact]
    public void Antoine_Lox_At90K_IsApproximatelyOneAtmosphere()
    {
        // NIST O2: saturated LOX at ~90.18 K sits at ~1 atm.
        double p = Antoine.VaporPressure_Pa(Antoine.LOX, 90.18);
        Assert.InRange(p, 0.85 * OneAtm_Pa, 1.15 * OneAtm_Pa);
    }

    [Fact]
    public void Antoine_VaporPressure_RisesMonotonicallyWithTemperature()
    {
        double p80 = Antoine.VaporPressure_Pa(Antoine.LOX, 80.0);
        double p90 = Antoine.VaporPressure_Pa(Antoine.LOX, 90.0);
        double p100 = Antoine.VaporPressure_Pa(Antoine.LOX, 100.0);
        Assert.True(p80 < p90 && p90 < p100,
            $"expected a monotonic rise, got {p80}, {p90}, {p100}");
    }

    [Fact]
    public void Antoine_ClampsBelowFittedRange()
    {
        // Inputs below T_min substitute the endpoint (documented clamp).
        double below = Antoine.VaporPressure_Pa(Antoine.LOX, 40.0);
        double atMin = Antoine.VaporPressure_Pa(Antoine.LOX, Antoine.LOX.T_min_K);
        Assert.Equal(atMin, below);
    }

    [Fact]
    public void Antoine_ClampsAboveFittedRange()
    {
        double above = Antoine.VaporPressure_Pa(Antoine.LOX, 200.0);
        double atMax = Antoine.VaporPressure_Pa(Antoine.LOX, Antoine.LOX.T_max_K);
        Assert.Equal(atMax, above);
    }

    [Fact]
    public void Antoine_Methane_PressureRoughlyTenfoldFromBoilingTo150K()
    {
        // NIST CH4 reference: ~1 atm at 111.7 K, ~10 atm at 150 K.
        double pBoil = Antoine.VaporPressure_Pa(Antoine.LCH4, 111.7);
        double p150 = Antoine.VaporPressure_Pa(Antoine.LCH4, 150.0);
        Assert.InRange(pBoil, 0.85 * OneAtm_Pa, 1.15 * OneAtm_Pa);
        Assert.InRange(p150 / pBoil, 8.0, 13.0);
    }

    [Fact]
    public void Antoine_ForFluid_KnownKey_MatchesDirectCoefficients()
    {
        double? viaKey = Antoine.VaporPressureForFluid_Pa("LOX", 95.0);
        double direct = Antoine.VaporPressure_Pa(Antoine.LOX, 95.0);
        Assert.True(viaKey.HasValue);
        AssertRelClose(direct, viaKey.GetValueOrDefault());
    }

    [Fact]
    public void Antoine_ForFluid_UnknownKey_ReturnsNull()
    {
        double? result = Antoine.VaporPressureForFluid_Pa("UNOBTANIUM", 300.0);
        Assert.False(result.HasValue);
    }

    [Fact]
    public void Antoine_IsDeterministic()
    {
        double a = Antoine.VaporPressure_Pa(Antoine.LCH4, 130.0);
        double b = Antoine.VaporPressure_Pa(Antoine.LCH4, 130.0);
        Assert.Equal(a, b); // bit-identical
    }

    // ───────────────────────── Orifice flow model ─────────────────────────

    [Fact]
    public void Orifice_JetVelocity_MatchesClosedForm()
    {
        // V = Cd·√(2·ΔP/ρ); Cd=1, ΔP=2000 Pa, ρ=1000 kg/m³ → √4 = 2 m/s.
        double v = OrificeModel.JetVelocity_ms(2_000.0, 1_000.0, cd: 1.0);
        AssertRelClose(2.0, v);
    }

    [Fact]
    public void Orifice_JetVelocity_ScalesWithSqrtOfDeltaP()
    {
        double v1 = OrificeModel.JetVelocity_ms(2_000.0, 1_000.0, cd: 1.0);
        double v4 = OrificeModel.JetVelocity_ms(8_000.0, 1_000.0, cd: 1.0);
        AssertRelClose(2.0 * v1, v4); // 4× ΔP → 2× velocity
    }

    [Fact]
    public void Orifice_JetVelocity_ScalesLinearlyWithDischargeCoefficient()
    {
        double full = OrificeModel.JetVelocity_ms(3.0e5, 1_140.0, cd: 1.0);
        double real = OrificeModel.JetVelocity_ms(3.0e5, 1_140.0, cd: 0.70);
        AssertRelClose(0.70 * full, real);
    }

    [Fact]
    public void Orifice_Area_RoundTripsToMassFlow()
    {
        const double mdot = 0.5, dP = 3.0e5, rho = 1_140.0, cd = 0.70;
        double area = OrificeModel.OrificeArea_m2(mdot, dP, rho, cd);
        double mdotBack = cd * area * Math.Sqrt(2.0 * rho * dP);
        AssertRelClose(mdot, mdotBack);
    }

    [Fact]
    public void Orifice_Area_ShrinksAsDeltaPRises()
    {
        double aLow = OrificeModel.OrificeArea_m2(0.5, 2.0e5, 1_140.0);
        double aHigh = OrificeModel.OrificeArea_m2(0.5, 8.0e5, 1_140.0);
        Assert.True(aHigh < aLow, $"area should fall with ΔP: {aHigh} !< {aLow}");
    }

    [Fact]
    public void Orifice_Diameter_IsConsistentWithArea()
    {
        const double mdot = 0.25, dP = 2.5e5, rho = 820.0, cd = 0.65;
        double dMm = OrificeModel.OrificeDiameter_mm(mdot, dP, rho, cd);
        double areaMm2 = OrificeModel.OrificeArea_mm2(mdot, dP, rho, cd);
        // area = π·(d/2)² for a circular orifice.
        AssertRelClose(areaMm2, Math.PI * dMm * dMm / 4.0);
    }

    [Fact]
    public void Orifice_DefaultDischargeCoefficient_IsSeventyPercent()
    {
        Assert.Equal(0.70, OrificeModel.DefaultCd);
    }

    [Fact]
    public void Orifice_ReferenceDensity_LoxMatchesSaturatedAnchor()
    {
        Assert.Equal(1_140.0, OrificeModel.ReferenceDensity_kgm3.LOX);
    }

    // ───────────────────────── Bartz gas-side heat flux ─────────────────────────

    [Fact]
    public void Bartz_HeatFlux_FollowsNewtonsLawOfCooling()
    {
        // q" = h_g·(T_aw − T_wg) = 1000·(3000 − 800) = 2.2 MW/m².
        double q = BartzHeatFlux.HeatFlux(1_000.0, 3_000.0, 800.0);
        AssertRelClose(2_200_000.0, q);
    }

    [Fact]
    public void Bartz_HeatFlux_IsZeroWhenWallAtAdiabaticTemperature()
    {
        Assert.Equal(0.0, BartzHeatFlux.HeatFlux(1_500.0, 2_750.0, 2_750.0));
    }

    [Fact]
    public void Bartz_HeatFlux_GoesNegativeWhenWallHotterThanGas()
    {
        double q = BartzHeatFlux.HeatFlux(1_000.0, 500.0, 900.0);
        Assert.True(q < 0.0, $"expected reverse flux, got {q}");
    }

    [Fact]
    public void Bartz_HeatFlux_ScalesLinearlyWithCoefficient()
    {
        double q1 = BartzHeatFlux.HeatFlux(1_000.0, 3_000.0, 800.0);
        double q2 = BartzHeatFlux.HeatFlux(2_000.0, 3_000.0, 800.0);
        AssertRelClose(2.0 * q1, q2);
    }
}
