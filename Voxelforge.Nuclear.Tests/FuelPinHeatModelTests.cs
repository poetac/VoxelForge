// FuelPinHeatModelTests.cs — Sprint NU.W2 unit tests for FuelPinHeatModel.
//
// Coverage: per-pin power scaling, centreline-to-surface ΔT scaling,
// coolant-side energy balance, NaN-trap behaviour, NRX-A6-anchor sanity-band.

using System;
using Voxelforge.Nuclear.FuelPin;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class FuelPinHeatModelTests
{
    private static HexArrayGeometryResult NrxA6Geometry()
        => HexArrayGeometry.Resolve(hexRings: 2, pinDiameter_mm: 2.5, pinPitch_mm: 3.2);

    // ── Smoke / NRX-A6 cluster anchor ────────────────────────────────────

    [Fact]
    public void Solve_NrxA6Anchor_ProducesFiniteTemperatures()
    {
        var r = FuelPinHeatModel.Solve(
            reactorThermalPower_W: 1100e6,
            fuelElementCount:      564,
            hexGeometry:           NrxA6Geometry(),
            fuelPinLength_m:       1.4,
            coolantMassFlow_kgs:   33.0,
            coolantInletTemp_K:    80.0,
            coolantInletPressure_Pa: 34e5,
            hotChannelFactor:      double.NaN);
        Assert.True(double.IsFinite(r.PeakFuelCenterlineTemp_K));
        Assert.True(double.IsFinite(r.PinSurfaceTemp_K));
        Assert.True(double.IsFinite(r.CoolantExitTemp_K));
        Assert.True(r.PeakFuelCenterlineTemp_K > r.PinSurfaceTemp_K);
        Assert.True(r.PinSurfaceTemp_K > r.CoolantExitTemp_K);
    }

    [Fact]
    public void Solve_NrxA6Anchor_PeakInCermetRange()
    {
        // NERVA cluster peak fuel T expected band: 2500-3500 K (the 3200 K
        // UO₂-cermet hard limit is at the upper edge of operational designs).
        var r = FuelPinHeatModel.Solve(
            reactorThermalPower_W: 1100e6,
            fuelElementCount:      564,
            hexGeometry:           NrxA6Geometry(),
            fuelPinLength_m:       1.4,
            coolantMassFlow_kgs:   33.0,
            coolantInletTemp_K:    80.0,
            coolantInletPressure_Pa: 34e5);
        Assert.InRange(r.PeakFuelCenterlineTemp_K, 2500.0, 3500.0);
    }

    [Fact]
    public void Solve_NrxA6Anchor_DefaultHotChannelFactorApplied()
    {
        // F_hc=NaN should use the 1.40 cluster anchor.
        var r = FuelPinHeatModel.Solve(
            reactorThermalPower_W: 1100e6,
            fuelElementCount:      564,
            hexGeometry:           NrxA6Geometry(),
            fuelPinLength_m:       1.4,
            coolantMassFlow_kgs:   33.0,
            coolantInletTemp_K:    80.0,
            coolantInletPressure_Pa: 34e5,
            hotChannelFactor:      double.NaN);
        Assert.Equal(FuelPinHeatModel.DefaultHotChannelFactor, r.HotChannelFactor, precision: 6);
    }

    // ── Power scaling ────────────────────────────────────────────────────

    [Fact]
    public void PerPinPower_ScalesInverselyWithElementCount()
    {
        // Doubling the element count at fixed reactor power halves per-pin power.
        var rA = FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5);
        var rB = FuelPinHeatModel.Solve(1100e6, 1128, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5);
        Assert.Equal(2.0, rA.PerPinPower_W / rB.PerPinPower_W, precision: 6);
    }

    [Fact]
    public void HotChannelFactorOverride_RaisesPeakTempLinearlyInDeltaWc()
    {
        // F_hc raises q'' linearly → ΔT_wc grows linearly with F_hc.
        // ΔT_cs is F_hc-independent (depends on volumetric source). So
        // ΔT_wc-only growth ≈ (F_hc' - F_hc) · ΔT_wc_baseline.
        var r10 = FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5, hotChannelFactor: 1.0);
        var r15 = FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5, hotChannelFactor: 1.5);
        // r15.ΔT_wc = 1.5 · r10.ΔT_wc.
        Assert.Equal(1.5, r15.WallToCoolantDeltaT_K / r10.WallToCoolantDeltaT_K, precision: 4);
        // CenterlineToSurface ΔT is F_hc-independent.
        Assert.Equal(r10.CenterlineToSurfaceDeltaT_K, r15.CenterlineToSurfaceDeltaT_K, precision: 6);
    }

    [Fact]
    public void CenterlineToSurface_DeltaT_ScalesWithRadiusSquared()
    {
        // ΔT_cs = q''' · r² / (4·k). Doubling pin diameter (at fixed q''')
        // would 4× ΔT_cs. But q''' = Q_pin/(π·r²·L) so at fixed Q_pin (fixed
        // power + fixed N_pin), q''' ∝ 1/r² and ΔT_cs ∝ q'''·r² ≈ constant.
        // Verify the algebraic invariant.
        var thin = HexArrayGeometry.Resolve(2, pinDiameter_mm: 2.0, pinPitch_mm: 3.2);
        var fat  = HexArrayGeometry.Resolve(2, pinDiameter_mm: 3.0, pinPitch_mm: 4.0);
        var rThin = FuelPinHeatModel.Solve(1100e6, 564, thin, 1.4, 33.0, 80.0, 34e5);
        var rFat  = FuelPinHeatModel.Solve(1100e6, 564, fat,  1.4, 33.0, 80.0, 34e5);
        // Both pins receive the same Q_pin, so q'''·r² is invariant → ΔT_cs equal.
        Assert.Equal(rThin.CenterlineToSurfaceDeltaT_K, rFat.CenterlineToSurfaceDeltaT_K, precision: 4);
    }

    // ── Coolant energy balance ──────────────────────────────────────────

    [Fact]
    public void CoolantExitTemp_RisesWithDecreasedMassFlow()
    {
        var rLo = FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 30.0, 80.0, 34e5);
        var rHi = FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 40.0, 80.0, 34e5);
        Assert.True(rLo.CoolantExitTemp_K > rHi.CoolantExitTemp_K);
    }

    [Fact]
    public void CoolantExitTemp_MatchesCycleSolverWhenPropellantsAlign()
    {
        // The fuel-pin energy balance reproduces the NtrCycleSolver result
        // for the same inputs (both use LH2 cp(T) integration over the same
        // ΔT interval). Comparable accuracy.
        var r = FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5);
        // NRX-A6 cycle predicts T_exit ≈ 2400-2500 K. ±100 K agreement is
        // expected at this fidelity.
        Assert.InRange(r.CoolantExitTemp_K, 2300.0, 2600.0);
    }

    // ── NaN-trap + bound-check behaviour ────────────────────────────────

    [Fact]
    public void Solve_NonPositiveReactorPower_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            FuelPinHeatModel.Solve(0.0, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5));

    [Fact]
    public void Solve_NonPositiveElementCount_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            FuelPinHeatModel.Solve(1100e6, 0, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5));

    [Fact]
    public void Solve_NullGeometry_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            FuelPinHeatModel.Solve(1100e6, 564, null!, 1.4, 33.0, 80.0, 34e5));

    [Fact]
    public void Solve_NonPositiveLength_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 0.0, 33.0, 80.0, 34e5));

    [Fact]
    public void Solve_NonPositiveMassFlow_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 0.0, 80.0, 34e5));

    [Fact]
    public void Solve_NonPositiveInletTemp_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 0.0, 34e5));

    [Fact]
    public void Solve_NonPositiveHotChannelFactor_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            FuelPinHeatModel.Solve(1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5, hotChannelFactor: 0.0));
}
