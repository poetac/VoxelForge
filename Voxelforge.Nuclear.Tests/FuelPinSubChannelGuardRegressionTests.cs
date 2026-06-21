// FuelPinSubChannelGuardRegressionTests.cs — regression guard for the fuel-pin
// negative sub-channel-area bug (red-team finding).
//
// FuelPinHeatModel.Solve validated every scalar input but not the hex
// geometry's pin overlap. When PinDiameter ≥ PinPitch (pins overlapping), the
// triangular sub-channel flow area A = (√3/4)·p² − (π/8)·d² goes ≤ 0; the code
// then floored it (Math.Max(A, 1e-12)) so the sub-channel mass flux G = ṁ/A
// became an enormous garbage value, driving the wall-to-coolant ΔT toward zero
// and silently over-cooling the pin — an impossible geometry produced a finite,
// over-optimistic result that could slip past the centreline-overtemp gate.
//
// The two normal entry points already guard pitch > diameter (NuclearThermalDesign
// for the optimization path, HexArrayGeometry.Resolve via TriangularSubChannelDh_mm
// for the factory path). The remaining gap is the public HexArrayGeometryResult
// record, which is constructible directly with no validation — so this is the
// defense-in-depth guard on the model itself. Solve now rejects pitch ≤ diameter.

using Voxelforge.Nuclear.FuelPin;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class FuelPinSubChannelGuardRegressionTests
{
    [Fact]
    public void OverlappingPins_ThrowsClearly_RatherThanGarbageResult()
    {
        // d = 3.5 mm > p = 3.0 mm → (√3/4)p² − (π/8)d² < 0. Built via the record
        // constructor directly: HexArrayGeometry.Resolve would itself reject this,
        // so this exercises the model's own guard on a hand-built geometry.
        var overlapping = new HexArrayGeometryResult(
            PinCount:                    19,
            HexRings:                    2,
            ElementOuterFlat_mm:         15.0,
            PinPitch_mm:                 3.0,
            PinDiameter_mm:              3.5,
            ChannelHydraulicDiameter_mm: 1.0,
            FuelVolumeFraction:          0.5,
            CoolantVolumeFraction:       0.5);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => FuelPinHeatModel.Solve(
            reactorThermalPower_W:   1100e6,
            fuelElementCount:        564,
            hexGeometry:             overlapping,
            fuelPinLength_m:         1.4,
            coolantMassFlow_kgs:     33.0,
            coolantInletTemp_K:      80.0,
            coolantInletPressure_Pa: 34e5));
    }

    [Fact]
    public void TightButValidPitch_StillSolvesFinite()
    {
        // d = 3.0 mm < p = 3.05 mm: pins do not overlap (A_sub > 0). The guard
        // must not over-reject a tight-but-valid lattice.
        var tight = HexArrayGeometry.Resolve(hexRings: 2, pinDiameter_mm: 3.0, pinPitch_mm: 3.05);
        var r = FuelPinHeatModel.Solve(
            reactorThermalPower_W:   1100e6,
            fuelElementCount:        564,
            hexGeometry:             tight,
            fuelPinLength_m:         1.4,
            coolantMassFlow_kgs:     33.0,
            coolantInletTemp_K:      80.0,
            coolantInletPressure_Pa: 34e5);
        Assert.True(double.IsFinite(r.PeakFuelCenterlineTemp_K)
                 && r.PeakFuelCenterlineTemp_K > r.PinSurfaceTemp_K,
            $"tight-but-valid lattice should solve; peak={r.PeakFuelCenterlineTemp_K}");
    }
}
