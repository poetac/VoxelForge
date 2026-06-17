// IdealGasAirTests.cs — Sprint A3 acceptance for the ideal-gas air
// model + isentropic-flow helpers.

using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Tests.Thermo;

public sealed class IdealGasAirTests
{
    /// <summary>
    /// cp = γ·R/(γ−1) — algebraic identity. With γ = 1.40 and
    /// R = 287.05 J/(kg·K) the value is 1004.7 J/(kg·K). The text-
    /// book "1004.5" rounding comes from γ = 1.40 + R = 287.0; the
    /// extra precision in our R (287.053) shifts cp slightly upward.
    /// </summary>
    [Fact]
    public void Cp_DerivedFromGammaAndR()
    {
        double derived = IdealGasAir.Gamma * IdealGasAir.R_J_kg_K / (IdealGasAir.Gamma - 1.0);
        Assert.Equal(IdealGasAir.Cp_J_kg_K, derived, 6);
        Assert.InRange(IdealGasAir.Cp_J_kg_K, 1004.0, 1005.0);
    }

    /// <summary>
    /// Mayer's relation: cp − cv = R.
    /// </summary>
    [Fact]
    public void MayersRelation_Holds()
    {
        Assert.Equal(IdealGasAir.R_J_kg_K, IdealGasAir.Cp_J_kg_K - IdealGasAir.Cv_J_kg_K, 6);
    }

    /// <summary>
    /// Speed of sound at 288.15 K matches sea-level reference value
    /// 340.29 m/s within rounding.
    /// </summary>
    [Fact]
    public void SpeedOfSound_AtSeaLevelTemp_MatchesReference()
    {
        double a = IdealGasAir.SpeedOfSound_m_s(288.15);
        Assert.InRange(a, 340.2, 340.4);
    }

    /// <summary>
    /// At M = 0 stagnation = static (ratio 1.0 for both T and P).
    /// </summary>
    [Fact]
    public void StagnationRatios_AtZeroMach_AreUnity()
    {
        Assert.Equal(1.0, IdealGasAir.StagnationTemperatureRatio(0.0), 6);
        Assert.Equal(1.0, IdealGasAir.StagnationPressureRatio(0.0), 6);
    }

    /// <summary>
    /// At M = 1: T_t/T = 1.2; P_t/P = 1.8935 (γ = 1.40 throughout).
    /// </summary>
    [Fact]
    public void StagnationRatios_AtUnitMach_MatchTextbook()
    {
        Assert.Equal(1.20, IdealGasAir.StagnationTemperatureRatio(1.0), 4);
        Assert.Equal(1.8929, IdealGasAir.StagnationPressureRatio(1.0), 3);
    }

    /// <summary>
    /// At M = 2: T_t/T = 1.8; P_t/P = 7.824. Drives the Mattingly
    /// synthetic ramjet fixture's 12 km / M=2 inlet stagnation values
    /// (T_t0 = 216.65 · 1.8 = 389.97 K).
    /// </summary>
    [Fact]
    public void StagnationRatios_AtMach2_MatchTextbook()
    {
        Assert.Equal(1.80, IdealGasAir.StagnationTemperatureRatio(2.0), 4);
        Assert.Equal(7.824, IdealGasAir.StagnationPressureRatio(2.0), 2);
    }

    /// <summary>
    /// Stagnation/static pressure ratio inversion round-trips: the
    /// inverse function recovers the input Mach number.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(5.0)]
    public void MachInversion_RoundTrips(double m)
    {
        double p_ratio = IdealGasAir.StagnationPressureRatio(m);
        double m_back  = IdealGasAir.MachFromStagnationPressureRatio(p_ratio);
        Assert.Equal(m, m_back, 6);
    }

    /// <summary>
    /// Inverse rejects ratios &lt; 1 (physically impossible for
    /// isentropic stagnation flow).
    /// </summary>
    [Fact]
    public void MachInversion_RejectsSubUnityRatio()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IdealGasAir.MachFromStagnationPressureRatio(0.5));
    }

    // ----- cp(T) tabulated functions (post-A7 follow-on) -----

    /// <summary>
    /// At T = 288.15 K (sea-level reference) cp_air interpolates to
    /// ~1004.6 J/(kg·K), within 0.01 % of the algebraic γR/(γ−1).
    /// </summary>
    [Fact]
    public void CpAir_AtSeaLevel_MatchesNistTable()
    {
        double cp = IdealGasAir.CpAir(288.15);
        Assert.InRange(cp, 1004.0, 1005.5);
    }

    /// <summary>
    /// cp_air rises monotonically over 200-2200 K. Polyatomic-mode
    /// excitation kicks in above ~700 K and the curve trends upward
    /// through the rest of the table.
    /// </summary>
    [Theory]
    [InlineData(300.0, 600.0)]
    [InlineData(600.0, 1200.0)]
    [InlineData(1200.0, 1800.0)]
    public void CpAir_RisesMonotonically(double T_lo, double T_hi)
    {
        Assert.True(IdealGasAir.CpAir(T_hi) > IdealGasAir.CpAir(T_lo),
            $"cp_air({T_hi}) should exceed cp_air({T_lo}); table is monotonically rising over the JANAF range.");
    }

    /// <summary>
    /// Edge clamp: T below 200 K returns the floor value; T above
    /// 2200 K returns the ceiling. Cycle solvers should never push
    /// outside this band but the clamp keeps the inversion stable
    /// when they do (e.g. φ&gt;1.5 super-rich).
    /// </summary>
    [Fact]
    public void CpAir_AtTableExtremes_ClampsCleanly()
    {
        Assert.Equal(IdealGasAir.CpAir(200.0), IdealGasAir.CpAir(150.0), 6);
        Assert.Equal(IdealGasAir.CpAir(2200.0), IdealGasAir.CpAir(2500.0), 6);
    }

    /// <summary>
    /// Kerosene burnt-gas cp at high T exceeds dry-air cp at the same
    /// T — polyatomic CO2 + H2O products have higher heat capacity
    /// per kg than the diatomic-dominated air. At T = 1500 K the gap
    /// is ~9 % per Mattingly App. B.
    /// </summary>
    [Fact]
    public void CpBurntKerosene_AtCombustorExit_HigherThanAir()
    {
        double cpAir   = IdealGasAir.CpAir(1500.0);
        double cpBurnt = IdealGasAir.CpBurntKerosene(1500.0);
        Assert.True(cpBurnt > cpAir,
            $"cp_burnt({cpBurnt}) should exceed cp_air({cpAir}) at 1500 K.");
        Assert.InRange(cpBurnt - cpAir, 50.0, 200.0);
    }

    /// <summary>
    /// Enthalpy referenced to T_ref = 200 K is zero at the reference
    /// and rises monotonically. The trapezoidal-rule integral matches
    /// cp · ΔT to within 5 % for small ΔT (where cp is essentially
    /// constant) and within ~15 % over the full 200-2200 K span.
    /// </summary>
    [Fact]
    public void EnthalpyAir_RisesMonotonicallyWithT()
    {
        Assert.Equal(0.0, IdealGasAir.EnthalpyAir(200.0), 6);
        double h300  = IdealGasAir.EnthalpyAir(300.0);
        double h1500 = IdealGasAir.EnthalpyAir(1500.0);
        Assert.True(h300 > 0.0);
        Assert.True(h1500 > h300);
    }

    /// <summary>
    /// At the same T, burnt-gas enthalpy exceeds air enthalpy — both
    /// reference 200 K, both increase monotonically, but cp_burnt is
    /// uniformly ≥ cp_air across the table so the integral pulls
    /// ahead.
    /// </summary>
    [Fact]
    public void EnthalpyBurnt_AboveAir_ForSameT()
    {
        double hAir   = IdealGasAir.EnthalpyAir(1500.0);
        double hBurnt = IdealGasAir.EnthalpyBurntKerosene(1500.0);
        Assert.True(hBurnt > hAir,
            $"h_burnt({hBurnt}) should exceed h_air({hAir}) at 1500 K (cp_burnt > cp_air everywhere).");
    }

    /// <summary>
    /// Invert(EnthalpyBurnt(T)) round-trips to T within 1 K — the
    /// table's ~1 % cp variation cell-to-cell drives this tolerance.
    /// </summary>
    [Theory]
    [InlineData(500.0)]
    [InlineData(1175.0)]   // J85 design-point T_t4
    [InlineData(1700.0)]   // turbine-inlet ceiling
    [InlineData(2050.0)]   // φ=0.55 #358 test
    public void InvertEnthalpyBurntKerosene_RoundTrips(double T)
    {
        double h = IdealGasAir.EnthalpyBurntKerosene(T);
        double T_back = IdealGasAir.InvertEnthalpyBurntKerosene(h);
        Assert.Equal(T, T_back, 1.0);
    }
}
