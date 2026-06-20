// IsentropicFlowTests.cs — cross-platform coverage for the isentropic
// gas-dynamics relations in Voxelforge.Core (nozzle station thermodynamics):
// the area↔Mach Newton inversion, static-from-total temperature and pressure,
// the turbulent recovery factor, and adiabatic wall temperature. Anchored to
// standard isentropic-table values (γ=1.4, A/A*=2 → M ≈ 2.197 / 0.306; the
// γ=1.4 choked-flow pressure ratio P*/P0 = 0.5283) and the invariants each
// relation must satisfy. PicoGK-free → runs on the Linux CI 'core' leg.

using Voxelforge.Combustion;

namespace Voxelforge.Core.Tests;

public sealed class IsentropicFlowTests
{
    private static void AssertRelClose(double expected, double actual, double relTol = 1e-9)
    {
        double tol = Math.Max(Math.Abs(expected) * relTol, 1e-12);
        Assert.True(Math.Abs(expected - actual) <= tol,
            $"expected {expected} (±{tol}), got {actual}");
    }

    // Standard area-Mach relation A/A* for a given M and γ (Anderson, Modern
    // Compressible Flow) — used to round-trip the Newton inversion below.
    private static double AreaRatioFromMach(double m, double gamma)
    {
        double gp1 = gamma + 1.0, gm1 = gamma - 1.0;
        double term = (2.0 / gp1) * (1.0 + 0.5 * gm1 * m * m);
        return (1.0 / m) * Math.Pow(term, gp1 / (2.0 * gm1));
    }

    // ───────────────────────── Area ↔ Mach inversion ─────────────────────────

    [Fact]
    public void Mach_AreaRatioTwo_Gamma14_MatchesIsentropicTable()
    {
        // Standard table (γ=1.4, A/A*=2): M_super ≈ 2.197, M_sub ≈ 0.306.
        double mSup = PropellantTables.MachFromAreaRatio(2.0, 1.4, supersonic: true);
        double mSub = PropellantTables.MachFromAreaRatio(2.0, 1.4, supersonic: false);
        Assert.InRange(mSup, 2.18, 2.22);
        Assert.InRange(mSub, 0.30, 0.31);
    }

    [Fact]
    public void Mach_SupersonicRootAboveOne_SubsonicRootBelowOne()
    {
        Assert.True(PropellantTables.MachFromAreaRatio(3.0, 1.2, supersonic: true) > 1.0);
        double sub = PropellantTables.MachFromAreaRatio(3.0, 1.2, supersonic: false);
        Assert.True(sub > 0.0 && sub < 1.0, $"subsonic root out of band: {sub}");
    }

    [Fact]
    public void Mach_SupersonicRoot_RisesWithAreaRatio()
    {
        double m2 = PropellantTables.MachFromAreaRatio(2.0, 1.4, supersonic: true);
        double m4 = PropellantTables.MachFromAreaRatio(4.0, 1.4, supersonic: true);
        Assert.True(m4 > m2, $"supersonic M should rise with A/A*: {m4} !> {m2}");
    }

    [Fact]
    public void Mach_InvertsTheAreaRelation_RoundTrip()
    {
        const double areaRatio = 6.0, gamma = 1.22;
        double m = PropellantTables.MachFromAreaRatio(areaRatio, gamma, supersonic: true);
        AssertRelClose(areaRatio, AreaRatioFromMach(m, gamma), 1e-4);
    }

    // ───────────────────────── Static-from-total T & P ─────────────────────────

    [Fact]
    public void StaticTemp_AtZeroMach_EqualsStagnation()
    {
        Assert.Equal(3000.0, PropellantTables.StaticTemp(3000.0, 0.0, 1.2));
    }

    [Fact]
    public void StaticTemp_AtMachOne_MatchesClosedForm()
    {
        // T*/T0 = 1/(1 + (γ−1)/2); γ=1.2 → 3000/1.1 = 2727.27 K.
        double t = PropellantTables.StaticTemp(3000.0, 1.0, 1.2);
        AssertRelClose(3000.0 / 1.1, t);
    }

    [Fact]
    public void StaticTemp_FallsAsMachRises()
    {
        double t1 = PropellantTables.StaticTemp(3000.0, 1.0, 1.2);
        double t3 = PropellantTables.StaticTemp(3000.0, 3.0, 1.2);
        Assert.True(t3 < t1, $"static T should fall with M: {t3} !< {t1}");
    }

    [Fact]
    public void StaticPressure_AtMachOne_Gamma14_IsChokedRatio()
    {
        // The classic choked-flow ratio P*/P0 = 0.5283 for γ=1.4.
        double p = PropellantTables.StaticPressure(1.0e6, 1.0, 1.4);
        AssertRelClose(0.528282 * 1.0e6, p, 1e-4);
    }

    [Fact]
    public void StaticPressure_AtZeroMach_EqualsStagnation()
    {
        Assert.Equal(1.0e6, PropellantTables.StaticPressure(1.0e6, 0.0, 1.4));
    }

    // ───────────────────────── Recovery & adiabatic wall T ─────────────────────────

    [Fact]
    public void RecoveryFactor_IsCubeRootOfPrandtl()
    {
        AssertRelClose(0.887904, PropellantTables.RecoveryFactor(0.7), 1e-5);
        Assert.Equal(1.0, PropellantTables.RecoveryFactor(1.0));
    }

    [Fact]
    public void RecoveryFactor_RisesWithPrandtl()
    {
        Assert.True(PropellantTables.RecoveryFactor(0.9) > PropellantTables.RecoveryFactor(0.6));
    }

    [Fact]
    public void AdiabaticWallTemp_AtZeroMach_EqualsStatic()
    {
        Assert.Equal(2200.0, PropellantTables.AdiabaticWallTemp(2200.0, 0.0, 1.2, 0.7));
    }

    [Fact]
    public void AdiabaticWallTemp_ExceedsStaticInFlow_AndMatchesClosedForm()
    {
        // T_aw = T_static·(1 + r·(γ−1)/2·M²), r = Pr^(1/3).
        double tAw = PropellantTables.AdiabaticWallTemp(2000.0, 2.0, 1.2, 0.7);
        double r = Math.Cbrt(0.7);
        double expected = 2000.0 * (1.0 + r * 0.5 * 0.2 * 4.0);
        Assert.True(tAw > 2000.0, $"recovery should raise wall T above static: {tAw}");
        AssertRelClose(expected, tAw);
    }
}
