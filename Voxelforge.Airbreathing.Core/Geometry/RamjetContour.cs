// RamjetContour.cs — axisymmetric profile for a ramjet duct.
//
// Pure data + derivation, no PicoGK. The actual voxel build sits in
// a future Voxelforge.Airbreathing.Voxels project — parallel to the
// rocket-side Voxelforge.Voxels — and consumes this contour the same
// way ChamberVoxelBuilder consumes ChamberContour. Spliting the data
// model into Core (this file) and the SDF rendering into Voxels
// keeps the rocket-side ADR-015 split intact: Core stays headless +
// PicoGK-free, Voxels owns geometry rendering.
//
// Why ship this in A6 even without the voxel builder
// --------------------------------------------------
//   - Cycle solver outputs (StationMap) are 0-D; the contour is the
//     first physically-shaped artefact of the design and is what
//     LPBF / CFD / inspection tooling will eventually consume.
//   - Tests can be run cross-platform (no PicoGK / WinForms dep).
//   - The voxel builder is a mechanical translation step from contour
//     → SDF that's straightforward once a real consumer surfaces.
//
// Sub-step layout (canonical ramjet "constant-area combustor" form):
//
//   freestream → diffuser (convergent) → combustor (constant area)
//              → CD nozzle (convergent → throat → divergent) → exit
//
// Axial stations (x = 0 at inlet face):
//
//                    R(x)
//                       ▲
//                       │
//   Inlet face ─────────┤R_inlet ─┐
//                       │         │ diffuser
//                       │         └────────┐R_combustor ────────┐
//                       │                                       │ combustor
//                       │                                       │
//                       │                  ──────┐R_throat      │
//                       │                       │  CD nozzle    │
//                       │                       │     ─────────┐│R_exit
//                       │                       │              ││
//                       └───────────────────────────────────────┴─→ x
//
// Stations sampled at: inlet face, diffuser exit, combustor inlet
// (= diffuser exit), combustor exit, throat, exit.

using System;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// One station on a ramjet axisymmetric contour. Axial position +
/// radius; area = π·R² is derived for convenience.
/// </summary>
public readonly record struct RamjetStation(
    double X_m,
    double R_m,
    RamjetSection Section)
{
    /// <summary>Cross-sectional area at this station, π·R² [m²].</summary>
    public double Area_m2 => Math.PI * R_m * R_m;
}

/// <summary>
/// Ramjet axial sections. Drives downstream colour-coded reporting +
/// LPBF wall-thickness scheduling (when the voxel builder lands).
/// </summary>
public enum RamjetSection
{
    Inlet = 0,
    Diffuser = 1,
    Combustor = 2,
    NozzleConvergent = 3,
    NozzleThroat = 4,
    NozzleDivergent = 5,
    Exit = 6,
}

/// <summary>
/// Axisymmetric ramjet contour derived from an
/// <see cref="AirbreathingEngineDesign"/>. Pure geometry — no SDF, no
/// voxel ops. The future <c>Voxelforge.Airbreathing.Voxels</c> project's
/// <c>RamjetVoxelBuilder</c> will consume this directly.
/// </summary>
/// <param name="Stations">Length-7 axial stations in monotone X order.</param>
/// <param name="TotalLength_m">x_exit − x_inlet [m].</param>
/// <param name="ThroatIndex">Index into <see cref="Stations"/> of the throat (smallest R) station.</param>
public sealed record RamjetContour(
    RamjetStation[] Stations,
    double TotalLength_m,
    int ThroatIndex)
{
    /// <summary>
    /// Convenience: throat station from the <see cref="ThroatIndex"/>.
    /// </summary>
    public RamjetStation ThroatStation => Stations[ThroatIndex];

    /// <summary>
    /// Convenience: exit (last) station.
    /// </summary>
    public RamjetStation ExitStation => Stations[Stations.Length - 1];
}

/// <summary>
/// Derive a <see cref="RamjetContour"/> from an
/// <see cref="AirbreathingEngineDesign"/>. The design carries the four
/// area knobs (inlet, combustor, throat, exit); this helper splits the
/// total length budget across diffuser / combustor / nozzle sections
/// using fixed proportions chosen for printability.
/// </summary>
public static class RamjetGeometry
{
    /// <summary>
    /// Diffuser length as a fraction of the design's combustor
    /// length. Values around 1.0 give printable, well-converging
    /// diffuser angles for the M=2-3 design regime.
    /// </summary>
    public const double DiffuserLengthOverCombustor = 1.0;

    /// <summary>
    /// Convergent-nozzle length as a fraction of combustor length.
    /// Short — convergent section is geometrically less demanding.
    /// </summary>
    public const double ConvergentLengthOverCombustor = 0.30;

    /// <summary>
    /// Divergent-nozzle length as a fraction of combustor length.
    /// Longer — large area-ratio bell needs axial room to expand.
    /// </summary>
    public const double DivergentLengthOverCombustor = 0.60;

    /// <summary>
    /// Build the contour from the design knobs.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s
    /// <see cref="AirbreathingEngineDesign.Kind"/> is not
    /// <see cref="AirbreathingEngineKind.Ramjet"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any of the ramjet contour areas
    /// (<see cref="AirbreathingEngineDesign.InletThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.CombustorArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleThroatArea_m2"/>,
    /// <see cref="AirbreathingEngineDesign.NozzleExitArea_m2"/>) or
    /// <see cref="AirbreathingEngineDesign.CombustorLength_m"/> is NaN
    /// or non-positive.
    /// </exception>
    public static RamjetContour From(AirbreathingEngineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (design.Kind != AirbreathingEngineKind.Ramjet)
            throw new ArgumentException(
                $"RamjetGeometry.From requires Kind == Ramjet; got {design.Kind}.",
                nameof(design));
        if (double.IsNaN(design.InletThroatArea_m2) || design.InletThroatArea_m2 <= 0
            || double.IsNaN(design.CombustorArea_m2) || design.CombustorArea_m2 <= 0
            || double.IsNaN(design.NozzleThroatArea_m2) || design.NozzleThroatArea_m2 <= 0
            || double.IsNaN(design.NozzleExitArea_m2) || design.NozzleExitArea_m2 <= 0
            || double.IsNaN(design.CombustorLength_m) || design.CombustorLength_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"All ramjet contour areas + combustor length must be positive; got "
              + $"InletThroatArea_m2={design.InletThroatArea_m2:F6} m^2, "
              + $"CombustorArea_m2={design.CombustorArea_m2:F6} m^2, "
              + $"NozzleThroatArea_m2={design.NozzleThroatArea_m2:F6} m^2, "
              + $"NozzleExitArea_m2={design.NozzleExitArea_m2:F6} m^2, "
              + $"CombustorLength_m={design.CombustorLength_m:F4} m.");

        double rInlet     = AreaToRadius(design.InletThroatArea_m2);
        double rCombustor = AreaToRadius(design.CombustorArea_m2);
        double rThroat    = AreaToRadius(design.NozzleThroatArea_m2);
        double rExit      = AreaToRadius(design.NozzleExitArea_m2);

        double lDiff   = DiffuserLengthOverCombustor * design.CombustorLength_m;
        double lComb   = design.CombustorLength_m;
        double lConv   = ConvergentLengthOverCombustor * design.CombustorLength_m;
        double lDiv    = DivergentLengthOverCombustor * design.CombustorLength_m;

        double xInlet         = 0.0;
        double xDiffExit      = xInlet     + lDiff;
        double xCombExit      = xDiffExit  + lComb;
        double xThroat        = xCombExit  + lConv;
        double xExit          = xThroat    + lDiv;

        var stations = new[]
        {
            new RamjetStation(xInlet,    rInlet,     RamjetSection.Inlet),
            new RamjetStation(xDiffExit, rCombustor, RamjetSection.Diffuser),
            new RamjetStation(xCombExit, rCombustor, RamjetSection.Combustor),
            new RamjetStation(xThroat,   rThroat,    RamjetSection.NozzleThroat),
            new RamjetStation(xExit,     rExit,      RamjetSection.Exit),
        };

        // Throat is the smallest-R station. For the canonical layout
        // it's index 3, but compute it from the data so a future
        // re-ordering (e.g. inserting an isolator) doesn't drift the
        // accessor.
        int throatIndex = 0;
        double minR = stations[0].R_m;
        for (int i = 1; i < stations.Length; i++)
        {
            if (stations[i].R_m < minR)
            {
                minR = stations[i].R_m;
                throatIndex = i;
            }
        }

        return new RamjetContour(
            Stations:       stations,
            TotalLength_m:  xExit - xInlet,
            ThroatIndex:    throatIndex);
    }

    private static double AreaToRadius(double area_m2)
        => Math.Sqrt(area_m2 / Math.PI);
}
