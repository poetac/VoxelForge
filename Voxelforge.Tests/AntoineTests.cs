// AntoineTests.cs — Issue #158 (A6 — Antoine P_vap-by-tank-T).
//
// Pin Antoine-equation outputs against published NIST values for the
// four supported propellant fluids. Tolerances at ±5 % are tight
// enough to catch coefficient-set regressions, loose enough to
// accommodate the various NIST/Yaws fits being slightly different
// from each other on the same fluid (the fits agree on shape but
// differ on absolute constants by ~1-3 %).

using Voxelforge.Coolant;
using Xunit;

namespace Voxelforge.Tests;

public class AntoineTests
{
    /// <summary>±5 % band around the NIST-published value.</summary>
    private static void AssertCloseFrac(double expected, double actual, double frac, string label)
    {
        double low  = expected * (1.0 - frac);
        double high = expected * (1.0 + frac);
        Assert.True(
            actual >= low && actual <= high,
            $"{label}: expected ≈ {expected:G4}, got {actual:G4}, "
          + $"band [{low:G4}, {high:G4}]");
    }

    // ── LOX (NIST WebBook) ──────────────────────────────────────────
    [Fact]
    public void LOX_AtSaturation_90K_About100kPa()
    {
        // NIST: LOX saturation at T = 90.18 K is 1 atm = 101_325 Pa.
        // Within ± 5 %.
        double p = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: 90.18);
        AssertCloseFrac(101_325.0, p, 0.05, "LOX @ 90.18 K");
    }

    [Fact]
    public void LOX_AtSubcooled_85K_DropsSignificantly()
    {
        // NIST formula at T=85 K gives ≈ 56 kPa (well below 1 atm sat).
        // The point of this test is direction-of-effect (subcooled →
        // lower P_vap → larger NPSHA margin), not the exact value.
        double p = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: 85.0);
        Assert.InRange(p, 40_000.0, 75_000.0);
    }

    [Fact]
    public void LOX_AtWarmedTank_100K_RisesToAbout250kPa()
    {
        // NIST: LOX P_sat at 100 K is ~250 kPa.
        double p = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: 100.0);
        AssertCloseFrac(254_000.0, p, 0.05, "LOX @ 100 K (warmed)");
    }

    // ── LH2 (NIST WebBook) ──────────────────────────────────────────
    [Fact]
    public void LH2_AtSaturation_20K_About100kPa()
    {
        // NIST: LH2 saturation at 20.4 K is 1 atm = 101_325 Pa.
        double p = Antoine.VaporPressure_Pa(Antoine.LH2, T_K: 20.4);
        AssertCloseFrac(101_325.0, p, 0.10, "LH2 @ 20.4 K");
    }

    // ── LCH4 (NIST WebBook) ─────────────────────────────────────────
    [Fact]
    public void LCH4_AtSaturation_111K_About100kPa()
    {
        // NIST: LCH4 saturation at 111.7 K is 1 atm.
        double p = Antoine.VaporPressure_Pa(Antoine.LCH4, T_K: 111.7);
        AssertCloseFrac(101_325.0, p, 0.10, "LCH4 @ 111.7 K");
    }

    // ── RP-1 (n-dodecane proxy) ─────────────────────────────────────
    [Fact]
    public void RP1_AtRoomTemp_298K_VeryLow()
    {
        // n-Dodecane P_vap at 298 K (room T) is ~14 Pa per Yaws table.
        // Pre-A6 callers used 100 Pa flat — the Antoine form gives a
        // physics-correct lower number that better reflects RP-1's
        // very-low-volatility character at storage temperatures.
        double p = Antoine.VaporPressure_Pa(Antoine.RP1, T_K: 298.0);
        Assert.InRange(p, 1.0, 200.0);
    }

    // ── Out-of-range clamping ───────────────────────────────────────
    [Fact]
    public void LOX_BelowFitRange_ClampsToTmin()
    {
        // LOX coefficients fit 54-150 K. Input 30 K should clamp to 54 K.
        double pAt30 = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: 30.0);
        double pAt54 = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: 54.0);
        Assert.Equal(pAt54, pAt30, precision: 3);
    }

    [Fact]
    public void LOX_AboveFitRange_ClampsToTmax()
    {
        double pAt200    = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: 200.0);
        double pAtTmax   = Antoine.VaporPressure_Pa(Antoine.LOX, T_K: Antoine.LOX.T_max_K);
        Assert.Equal(pAtTmax, pAt200, precision: 3);
    }

    // ── VaporPressureForFluid_Pa key dispatch ───────────────────────
    [Theory]
    [InlineData("LOX",  90.18, 101_325.0, 0.05)]
    [InlineData("H2",   20.4,  101_325.0, 0.10)]
    [InlineData("CH4",  111.7, 101_325.0, 0.10)]
    public void VaporPressureForFluid_DispatchByKey(string key, double T, double expected, double frac)
    {
        double? p = Antoine.VaporPressureForFluid_Pa(key, T);
        Assert.NotNull(p);
        AssertCloseFrac(expected, p!.Value, frac, $"{key} @ {T} K");
    }

    [Fact]
    public void VaporPressureForFluid_UnknownKey_ReturnsNull()
    {
        // N2O4, MMH, etc. aren't in the table today. Caller falls back
        // to legacy constant table.
        Assert.Null(Antoine.VaporPressureForFluid_Pa("N2O4", 298.0));
        Assert.Null(Antoine.VaporPressureForFluid_Pa("",      298.0));
    }

    // ── Schema migration v19 → … → v25 ──────────────────────────────
    [Fact]
    public void Schema_v20IsCurrent()
    {
        // Schema bumped to v31 by OOB-7 #343 (RdeTopology fields).
        // Test name retained for git-history continuity;
        // assertion tracks DesignPersistence.CurrentSchemaVersion.
        Assert.Equal("v31", IO.DesignPersistence.CurrentSchemaVersion);
    }

    [Fact]
    public void OperatingConditions_OxidizerInletTemp_K_DefaultZero()
    {
        // Sentinel default = 0 means "use legacy P_vap table".
        var cond = new Voxelforge.Optimization.OperatingConditions();
        Assert.Equal(0.0, cond.OxidizerInletTemp_K);
    }
}
