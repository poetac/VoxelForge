// HexArrayGeometry.cs — Sprint NU.W2 hex-array fuel-pin geometry helper.
//
// NERVA-class fuel elements pack circular fuel pins in a hexagonal close-
// packed array within a graphite-composite or UO₂-cermet matrix. This
// module computes per-element + per-pin geometric quantities (pin pitch,
// coolant-channel hydraulic diameter, fuel volume fraction, pin count
// from triangular-number packing) that the
// <see cref="Voxelforge.Nuclear.FuelPin.FuelPinHeatModel"/> consumes.
//
// Reference: Bennett R.G. (1972). "NERVA Program Summary." AIAA-72-1161
// (per-element geometry: hex 1.91 cm flat-to-flat, 19 coolant channels in
// a 3-ring hex array, 0.254 cm channel diameter, ~30 % UO₂ loading).
//
// Stateless, allocation-free.

using System;

namespace Voxelforge.Nuclear.FuelPin;

/// <summary>
/// Derived geometric quantities for a single NERVA-class hexagonal fuel
/// element packed with circular fuel pins in a hex-close-packed array.
/// </summary>
/// <param name="PinCount">Total pins in a <paramref name="HexRings"/>-ring array.</param>
/// <param name="HexRings">Number of concentric pin rings (excluding the centre).</param>
/// <param name="ElementOuterFlat_mm">Hex element flat-to-flat outer dimension [mm].</param>
/// <param name="PinPitch_mm">Centre-to-centre distance between adjacent pins [mm].</param>
/// <param name="PinDiameter_mm">Fuel-pin outer diameter [mm].</param>
/// <param name="ChannelHydraulicDiameter_mm">
/// Equivalent-channel hydraulic diameter [mm] = 4·A_flow/P_wetted for the
/// triangular sub-channel between three adjacent pins.
/// </param>
/// <param name="FuelVolumeFraction">Per-element UO₂ volume / total element volume [-].</param>
/// <param name="CoolantVolumeFraction">Per-element coolant void volume / total element volume [-].</param>
public sealed record HexArrayGeometryResult(
    int    PinCount,
    int    HexRings,
    double ElementOuterFlat_mm,
    double PinPitch_mm,
    double PinDiameter_mm,
    double ChannelHydraulicDiameter_mm,
    double FuelVolumeFraction,
    double CoolantVolumeFraction);

/// <summary>
/// Hex-array fuel-pin geometry helper. Closed-form; no iterative solve.
/// </summary>
public static class HexArrayGeometry
{
    /// <summary>
    /// Total pin count for a hex-close-packed array of
    /// <paramref name="rings"/> concentric rings (excluding the centre
    /// pin). 1+6+12+18+24… = 3·n·(n+1)+1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="rings"/> is negative.
    /// </exception>
    public static int PinCountForRings(int rings)
    {
        if (rings < 0) throw new ArgumentOutOfRangeException(nameof(rings),
            $"HexArray rings {rings} must be non-negative.");
        return 3 * rings * (rings + 1) + 1;
    }

    /// <summary>
    /// Element flat-to-flat for the given ring count + pin pitch. Approximated
    /// as W = 2·(n+0.5)·pitch (n rings of pins + half-pitch border).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="rings"/> is negative, or
    /// <paramref name="pinPitch_mm"/> is NaN or non-positive.
    /// </exception>
    public static double ElementOuterFlatMm(int rings, double pinPitch_mm)
    {
        if (rings < 0)
            throw new ArgumentOutOfRangeException(nameof(rings),
                $"HexArray rings {rings} must be non-negative.");
        if (double.IsNaN(pinPitch_mm) || pinPitch_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(pinPitch_mm),
                $"PinPitch_mm {pinPitch_mm:F3} must be > 0 mm.");
        return 2.0 * (rings + 0.5) * pinPitch_mm;
    }

    /// <summary>
    /// Triangular-sub-channel hydraulic diameter D_h = 4·A_flow / P_wetted
    /// for three pins of <paramref name="pinDiameter_mm"/> at
    /// <paramref name="pinPitch_mm"/> spacing.
    ///
    ///   A_flow = (√3/4) · pitch² − (π/8) · d_pin²   (triangle area minus 3·(π/6)·(d/2)² )
    ///   P_wet  = π · d_pin / 2                       (3 × 60° arc of one pin)
    ///   D_h    = 4 A / P_wet
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pinPitch_mm"/> or
    /// <paramref name="pinDiameter_mm"/> is NaN, non-positive, or the
    /// diameter is not less than the pitch.
    /// </exception>
    public static double TriangularSubChannelDh_mm(double pinPitch_mm, double pinDiameter_mm)
    {
        if (double.IsNaN(pinPitch_mm) || pinPitch_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(pinPitch_mm),
                $"PinPitch_mm {pinPitch_mm:F3} must be > 0 mm.");
        if (double.IsNaN(pinDiameter_mm) || pinDiameter_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(pinDiameter_mm),
                $"PinDiameter_mm {pinDiameter_mm:F3} must be > 0 mm.");
        if (pinDiameter_mm >= pinPitch_mm)
            throw new ArgumentOutOfRangeException(nameof(pinDiameter_mm),
                $"PinDiameter_mm {pinDiameter_mm:F3} mm must be less than "
              + $"PinPitch_mm {pinPitch_mm:F3} mm for valid hex packing.");

        double sqrt3 = Math.Sqrt(3.0);
        double aFlow = (sqrt3 / 4.0) * pinPitch_mm * pinPitch_mm
                     - (Math.PI / 8.0) * pinDiameter_mm * pinDiameter_mm;
        double pWet  = Math.PI * pinDiameter_mm / 2.0;
        return 4.0 * aFlow / pWet;
    }

    /// <summary>
    /// Per-element fuel volume fraction. F_fuel = N_pin·π·(d/2)² / A_hex
    /// where A_hex = (3√3/2)·s² with s = flat / √3 (hex side length).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pinCount"/> is &lt; 1, or
    /// <paramref name="pinDiameter_mm"/> or
    /// <paramref name="elementOuterFlat_mm"/> is NaN or non-positive.
    /// </exception>
    public static double FuelVolumeFractionFor(
        int    pinCount,
        double pinDiameter_mm,
        double elementOuterFlat_mm)
    {
        if (pinCount < 1)
            throw new ArgumentOutOfRangeException(nameof(pinCount),
                $"PinCount {pinCount} must be ≥ 1.");
        if (double.IsNaN(pinDiameter_mm) || pinDiameter_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(pinDiameter_mm),
                $"PinDiameter_mm {pinDiameter_mm:F3} must be > 0 mm.");
        if (double.IsNaN(elementOuterFlat_mm) || elementOuterFlat_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(elementOuterFlat_mm),
                $"ElementOuterFlat_mm {elementOuterFlat_mm:F3} must be > 0 mm.");

        // Hex outer flat-to-flat W relates to side length s as W = s·√3,
        // so s = W/√3. Hex area = (3√3/2)·s² = (3√3/2)·(W/√3)² = (√3/2)·W².
        double aHex   = (Math.Sqrt(3.0) / 2.0) * elementOuterFlat_mm * elementOuterFlat_mm;
        double aFuel  = pinCount * Math.PI * 0.25 * pinDiameter_mm * pinDiameter_mm;
        return aFuel / aHex;
    }

    /// <summary>
    /// Resolve the full geometric state for the given pin geometry +
    /// ring count. Uses the closed-form helpers above.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="hexRings"/> is negative or any of the
    /// pin-geometry preconditions enforced by the helpers fail (see
    /// <see cref="PinCountForRings"/>, <see cref="ElementOuterFlatMm"/>,
    /// <see cref="TriangularSubChannelDh_mm"/>).
    /// </exception>
    public static HexArrayGeometryResult Resolve(
        int    hexRings,
        double pinDiameter_mm,
        double pinPitch_mm)
    {
        int    pinCount    = PinCountForRings(hexRings);
        double elementFlat = ElementOuterFlatMm(hexRings, pinPitch_mm);
        double dh          = TriangularSubChannelDh_mm(pinPitch_mm, pinDiameter_mm);
        double f_fuel      = FuelVolumeFractionFor(pinCount, pinDiameter_mm, elementFlat);
        double f_coolant   = 1.0 - f_fuel;

        return new HexArrayGeometryResult(
            PinCount:                    pinCount,
            HexRings:                    hexRings,
            ElementOuterFlat_mm:         elementFlat,
            PinPitch_mm:                 pinPitch_mm,
            PinDiameter_mm:              pinDiameter_mm,
            ChannelHydraulicDiameter_mm: dh,
            FuelVolumeFraction:          f_fuel,
            CoolantVolumeFraction:       f_coolant);
    }
}
