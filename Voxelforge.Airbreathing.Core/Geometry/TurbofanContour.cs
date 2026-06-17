// TurbofanContour.cs — axisymmetric profile for a turbofan with separate
// bypass duct + core flow path. Pure data, no PicoGK; sibling to
// RamjetContour. Wave-2 follow-on for issue #441.
//
// Layout (separate-exhaust low-bypass turbofan, mirrors F404 / F100 class):
//
//                ┌──────── bypass duct (outer cold stream) ──────────┐
//   inlet ───────┤fan face                                           │bypass exit
//                │                                                   │
//                │     ┌── core flow ──┐                             │
//                ├─────┤  HPC  combustor  HPT  LPT                   │
//                │     └─── core nozzle ──── core exit ──────────────┤
//                └───────────────────────────────────────────────────┘
//
// Geometry split:
//   • Core-flow contour: 5 stations (inlet → fan face → core HPC face
//     → core nozzle throat → core exit), built like the ramjet contour
//     using the design's InletThroatArea / CombustorArea / NozzleThroatArea
//     / NozzleExitArea knobs.
//   • Bypass duct: a coaxial outer shell whose outer radius captures
//     bypass mass flow via BypassRatio: r_bypass_outer(x) = sqrt(r_core_outer(x)²
//     · (1 + BPR)). Inner radius = core outer + small structural gap;
//     bypass length = full core length so the bypass exit aligns with
//     the core nozzle exit (mixed-flow / separate-exhaust handled by the
//     cycle solver, not the geometry).
//
// This is a structural / printable-shell representation — fan blades,
// turbine stages, mixer / chevron geometry are out of scope. The voxel
// builder produces an annular core shell + an annular bypass-duct shell
// that share an inlet face.

using System;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// One axial station on a turbofan core contour. Mirrors
/// <see cref="RamjetStation"/> but uses a turbofan-specific section enum.
/// </summary>
public readonly record struct TurbofanCoreStation(
    double X_m,
    double R_m,
    TurbofanCoreSection Section)
{
    /// <summary>Cross-sectional area at this core station, π·R² [m²].</summary>
    public double Area_m2 => Math.PI * R_m * R_m;
}

/// <summary>
/// Axial sections on the turbofan core flow path. Drives downstream
/// colour-coded reporting + LPBF wall-thickness scheduling.
/// </summary>
public enum TurbofanCoreSection
{
    Inlet            = 0,
    FanFace          = 1,
    CompressorExit   = 2,
    CoreNozzleThroat = 3,
    CoreExit         = 4,
}

/// <summary>
/// Axisymmetric turbofan contour. Carries both the inner core flow path
/// (5 stations) and the outer bypass-duct radius profile (sampled at the
/// same X positions as the core stations).
/// </summary>
/// <param name="CoreStations">Core flow path, length-5, monotone in X.</param>
/// <param name="BypassOuterRadii_m">
/// Bypass-duct outer radius [m] sampled at the same X positions as
/// <see cref="CoreStations"/>. Length must match <see cref="CoreStations"/>.
/// </param>
/// <param name="TotalLength_m">x_exit − x_inlet [m].</param>
/// <param name="CoreThroatIndex">Index into <see cref="CoreStations"/> of the core nozzle throat.</param>
public sealed record TurbofanContour(
    TurbofanCoreStation[] CoreStations,
    double[] BypassOuterRadii_m,
    double TotalLength_m,
    int CoreThroatIndex)
{
    /// <summary>Convenience: core throat station.</summary>
    public TurbofanCoreStation CoreThroatStation => CoreStations[CoreThroatIndex];

    /// <summary>Convenience: core exit (last) station.</summary>
    public TurbofanCoreStation CoreExitStation => CoreStations[CoreStations.Length - 1];
}

/// <summary>
/// Derive a <see cref="TurbofanContour"/> from an
/// <see cref="AirbreathingEngineDesign"/>. Reuses the ramjet section-length
/// proportions for the core path; sets the bypass-duct outer radius via
/// area scaling: A_bypass(x) = BPR · A_core(x), so r_bypass_outer(x) =
/// √(r_core(x)² · (1 + BPR)).
/// </summary>
public static class TurbofanGeometry
{
    /// <summary>Build the contour from the design knobs.</summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s
    /// <see cref="AirbreathingEngineDesign.Kind"/> is not
    /// <see cref="AirbreathingEngineKind.Turbofan"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any of the turbofan core areas
    /// (<see cref="AirbreathingEngineDesign.InletThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.CombustorArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleExitArea_m2"/>) or
    /// <see cref="AirbreathingEngineDesign.CombustorLength_m"/> is NaN
    /// or non-positive, or when
    /// <see cref="AirbreathingEngineDesign.BypassRatio"/> is NaN or
    /// negative.
    /// </exception>
    public static TurbofanContour From(AirbreathingEngineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (design.Kind != AirbreathingEngineKind.Turbofan)
            throw new ArgumentException(
                $"TurbofanGeometry.From requires Kind == Turbofan; got {design.Kind}.",
                nameof(design));
        if (double.IsNaN(design.InletThroatArea_m2) || design.InletThroatArea_m2 <= 0
            || double.IsNaN(design.CombustorArea_m2) || design.CombustorArea_m2 <= 0
            || double.IsNaN(design.NozzleThroatArea_m2) || design.NozzleThroatArea_m2 <= 0
            || double.IsNaN(design.NozzleExitArea_m2) || design.NozzleExitArea_m2 <= 0
            || double.IsNaN(design.CombustorLength_m) || design.CombustorLength_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"All turbofan core areas + combustor length must be positive; got "
              + $"InletThroatArea_m2={design.InletThroatArea_m2:F6} m^2, "
              + $"CombustorArea_m2={design.CombustorArea_m2:F6} m^2, "
              + $"NozzleThroatArea_m2={design.NozzleThroatArea_m2:F6} m^2, "
              + $"NozzleExitArea_m2={design.NozzleExitArea_m2:F6} m^2, "
              + $"CombustorLength_m={design.CombustorLength_m:F4} m.");
        if (double.IsNaN(design.BypassRatio) || design.BypassRatio < 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"BypassRatio must be >= 0; got {design.BypassRatio:F3}.");

        double rInlet     = AreaToRadius(design.InletThroatArea_m2);
        double rCombustor = AreaToRadius(design.CombustorArea_m2);
        double rThroat    = AreaToRadius(design.NozzleThroatArea_m2);
        double rExit      = AreaToRadius(design.NozzleExitArea_m2);

        // Reuse the ramjet section-length proportions for the core flow
        // path (printable + well-converging diffuser angles in the M ≤ 0.8
        // typical-fan-face regime).
        double lDiff = RamjetGeometry.DiffuserLengthOverCombustor * design.CombustorLength_m;
        double lComb = design.CombustorLength_m;
        double lConv = RamjetGeometry.ConvergentLengthOverCombustor * design.CombustorLength_m;
        double lDiv  = RamjetGeometry.DivergentLengthOverCombustor  * design.CombustorLength_m;

        double xInlet    = 0.0;
        double xFanFace  = xInlet    + lDiff;
        double xCompExit = xFanFace  + lComb;
        double xThroat   = xCompExit + lConv;
        double xExit     = xThroat   + lDiv;

        var core = new[]
        {
            new TurbofanCoreStation(xInlet,    rInlet,     TurbofanCoreSection.Inlet),
            new TurbofanCoreStation(xFanFace,  rCombustor, TurbofanCoreSection.FanFace),
            new TurbofanCoreStation(xCompExit, rCombustor, TurbofanCoreSection.CompressorExit),
            new TurbofanCoreStation(xThroat,   rThroat,    TurbofanCoreSection.CoreNozzleThroat),
            new TurbofanCoreStation(xExit,     rExit,      TurbofanCoreSection.CoreExit),
        };

        // Bypass-duct outer radius via area scaling:
        //   A_bypass + A_core = A_total = π · r_outer²
        //   ⇒ r_outer(x) = √(r_core(x)² · (1 + BPR))
        // BPR == 0 degenerates to r_outer = r_core (turbojet limit).
        double bpr = design.BypassRatio;
        var bypassOuter = new double[core.Length];
        for (int i = 0; i < core.Length; i++)
        {
            double r = core[i].R_m;
            bypassOuter[i] = Math.Sqrt(r * r * (1.0 + bpr));
        }

        // Throat = smallest-R core station.
        int throatIndex = 0;
        double minR = core[0].R_m;
        for (int i = 1; i < core.Length; i++)
        {
            if (core[i].R_m < minR)
            {
                minR = core[i].R_m;
                throatIndex = i;
            }
        }

        return new TurbofanContour(
            CoreStations:       core,
            BypassOuterRadii_m: bypassOuter,
            TotalLength_m:      xExit - xInlet,
            CoreThroatIndex:    throatIndex);
    }

    private static double AreaToRadius(double area_m2)
        => Math.Sqrt(area_m2 / Math.PI);
}
