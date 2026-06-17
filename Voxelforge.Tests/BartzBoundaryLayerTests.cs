// BartzBoundaryLayerTests.cs — Ensure the Mayer acceleration correction
// and the barrel injector-mixing enhancement stay consistent with their
// documented asymptotes.

using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class BartzBoundaryLayerTests
{
    private static PropellantState TestGas() =>
        PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 7.0e6);

    [Fact]
    public void ClassicBartz_ReducesToOriginal_WhenBLCorrectionsOff()
    {
        var gas = TestGas();
        double h1 = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 0.5, 0.30, 1800, 1.0,
            accelerationParameterK: 0.0,
            injectorMixingDecay: 1e9);
        double h2 = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 0.5, 0.30, 1800, 1.0);
        Assert.Equal(h1, h2, precision: 3);
    }

    [Fact]
    public void MayerCorrection_ReducesH_AtLargeK()
    {
        var gas = TestGas();
        double hNoAccel = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 0.0, 1e9);
        double hStrongAccel = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 1e-5, 1e9);
        Assert.True(hStrongAccel < hNoAccel,
            $"Mayer correction should lower h at K=1e-5 (got {hNoAccel:F0} → {hStrongAccel:F0})");
        double ratio = hStrongAccel / hNoAccel;
        Assert.InRange(ratio, 0.35, 0.55);   // exp(-80000·1e-5) = 0.449
    }

    [Fact]
    public void MayerCorrection_HasNoEffect_AtNegativeOrZeroK()
    {
        var gas = TestGas();
        double hRef = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 0.0, 1e9);
        double hNeg = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, -1e-4, 1e9);
        Assert.Equal(hRef, hNeg, precision: 2);
    }

    [Fact]
    public void BarrelEnhancement_AddsHeatTransfer_NearInjector()
    {
        var gas = TestGas();
        double hInjector = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 0.2, 0.1, 1500, 1.0, 0.0, 0.0);   // at injector
        double hFar = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 0.2, 0.1, 1500, 1.0, 0.0, 5.0);   // well downstream
        Assert.True(hInjector > hFar,
            $"Injector mixing should add ≈ 30 % near x=0: {hFar:F0} → {hInjector:F0}");
        double ratio = hInjector / hFar;
        Assert.InRange(ratio, 1.25, 1.35);   // 1 + 0.30 · exp(0) = 1.30
    }

    [Fact]
    public void AccelerationParameter_PositiveForAcceleratingFlow()
    {
        var gas = TestGas();
        double K = BartzHeatFlux.AccelerationParameter(
            gas, localMach: 0.9, T_static_K: 3200, velocityGradient_1ps: 5000);
        Assert.True(K > 0, $"K should be positive for dU/dx > 0 (got {K:E2})");
        Assert.InRange(K, 1e-8, 1e-3);
    }

    [Fact]
    public void BothCorrectionsStack_Multiplicatively()
    {
        var gas = TestGas();
        double hRef  = BartzHeatFlux.HeatTransferCoefficient(gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 0.0,  1e9);
        double hAcc  = BartzHeatFlux.HeatTransferCoefficient(gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 5e-6, 1e9);
        double hBar  = BartzHeatFlux.HeatTransferCoefficient(gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 0.0,  0.0);
        double hBoth = BartzHeatFlux.HeatTransferCoefficient(gas, 0.010, 0.015, 1.0, 1.0, 2500, 1.0, 5e-6, 0.0);
        double fAcc = hAcc / hRef;
        double fBar = hBar / hRef;
        double expected = hRef * fAcc * fBar;
        Assert.InRange(hBoth / expected, 0.98, 1.02);
    }

    // ══════════════════════ PH-44 (2026-04-29) ══════════════════════
    // Bartz σ wall-T floor lowered 400 → 200 K so the chilldown / start-
    // transient solvers (ChilldownTransient.Run, StartTransientSim.Run,
    // ShutdownBlowdownSim.Run shipped 2026-04-28) can call Bartz at cold
    // wall T < 400 K without the σ correction silently flooring out.

    [Fact]
    public void PH44_BartzAtCryogenicWallT_ProducesPositiveH()
    {
        // Pre-PH-44 Twg < 400 K was clamped to 400 K — silently biased
        // σ low. Post-PH-44 the floor is 200 K, so cryogen wall states
        // (LH2 ≈ 20 K, LCH4 ≈ 112 K) get a meaningful σ contribution.
        var gas = TestGas();
        double h_at_250K = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, wallTempGas_K: 250, 1.0);
        double h_at_400K = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, wallTempGas_K: 400, 1.0);
        double h_at_1800K = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, wallTempGas_K: 1800, 1.0);
        Assert.True(h_at_250K > 0);
        // σ ∝ 1 / [(0.5·(T_wg/Tc)·machTerm + 0.5)^0.68 · machTerm^0.12]
        // Lower T_wg → smaller bracket → higher σ → higher h.
        // Pre-PH-44 the 400 K floor would give h_at_250K == h_at_400K.
        Assert.True(h_at_250K > h_at_400K,
            $"Bartz h at T_wg=250 K ({h_at_250K:F0}) should exceed h at T_wg=400 K "
          + $"({h_at_400K:F0}) once the 400 K floor is removed.");
        // h falls monotonically with rising T_wg (well-known property of σ).
        Assert.True(h_at_400K > h_at_1800K);
    }

    [Fact]
    public void PH44_BartzFloor_StillGuards_ZeroAndNegativeWallT()
    {
        // The 200 K floor still prevents pathological inputs from
        // producing NaN / divide-by-zero behavior.
        var gas = TestGas();
        double h_at_50K = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, wallTempGas_K: 50, 1.0);
        double h_at_200K = BartzHeatFlux.HeatTransferCoefficient(
            gas, 0.010, 0.015, 1.0, 1.0, wallTempGas_K: 200, 1.0);
        Assert.Equal(h_at_50K, h_at_200K, precision: 3);
        Assert.False(double.IsNaN(h_at_50K));
        Assert.True(h_at_50K > 0);
    }
}
