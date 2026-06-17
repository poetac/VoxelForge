// PulsejetContour.cs — axisymmetric profile for a valveless pulsejet
// (Wave 1 PR-5, sub-step 1a.5).
//
// Pure data + derivation, no PicoGK. The voxel build sits in
// Voxelforge.Airbreathing.Voxels. Mirrors the rocket-side ChamberContour
// + air-breathing sibling RamjetContour.
//
// Sub-step layout (canonical valveless / Argus-Lockwood-Hiller form):
//
//   intake horn → diffuser (convergent) → combustor (constant area)
//                → tailpipe (constant area, long)
//                → exit (tapered)
//
// Axial stations (x = 0 at intake face):
//
//                 R(x)
//                    ▲
//                    │
//   Intake face      │R_intake ─┐
//                    │          │ horn (slight flare)
//                    │          │
//                    │          └────┐R_combustor ──────────────┐
//                    │                                          │ combustor
//                    │                                          │
//                    │                          ────────────────┴── (tailpipe, long)
//                    │                                            ─┐R_exit
//                    │                                             │
//                    └────────────────────────────────────────────┴─→ x
//
// Stations sampled at: intake face, diffuser exit (= combustor inlet),
// combustor exit (= tailpipe inlet), tailpipe exit (= exit).

using System;

namespace Voxelforge.Airbreathing.Geometry;

/// <summary>
/// One station on a pulsejet axisymmetric contour. Axial position +
/// radius; area = π·R² is derived for convenience.
/// </summary>
public readonly record struct PulsejetStation(
    double X_m,
    double R_m,
    PulsejetSection Section)
{
    /// <summary>Cross-sectional area at this station, π·R² [m²].</summary>
    public double Area_m2 => Math.PI * R_m * R_m;
}

/// <summary>
/// Pulsejet axial sections. Drives downstream colour-coded reporting +
/// LPBF wall-thickness scheduling.
/// </summary>
public enum PulsejetSection
{
    IntakeHorn = 0,
    Diffuser = 1,
    Combustor = 2,
    Tailpipe = 3,
    Exit = 4,
}

/// <summary>
/// Axisymmetric pulsejet contour derived from an
/// <see cref="AirbreathingEngineDesign"/>. Pure geometry — no SDF, no
/// voxel ops. The <c>PulsejetVoxelBuilder</c> in
/// <c>Voxelforge.Airbreathing.Voxels</c> consumes this directly.
/// </summary>
/// <param name="Stations">Axial stations in monotone X order.</param>
/// <param name="TotalLength_m">x_exit − x_inlet [m].</param>
/// <param name="CombustorIndex">Index of the combustor exit station (largest radius interior point).</param>
public sealed record PulsejetContour(
    PulsejetStation[] Stations,
    double TotalLength_m,
    int CombustorIndex)
{
    /// <summary>Convenience: combustor exit station from the index.</summary>
    public PulsejetStation CombustorStation => Stations[CombustorIndex];

    /// <summary>Convenience: exit (last) station.</summary>
    public PulsejetStation ExitStation => Stations[Stations.Length - 1];
}

/// <summary>
/// Derive a <see cref="PulsejetContour"/> from an
/// <see cref="AirbreathingEngineDesign"/>. The design carries the
/// pulsejet geometry knobs (intake area, combustor, tailpipe, total tube
/// length); this helper splits the total length budget across horn /
/// diffuser / combustor / tailpipe sections using fixed proportions
/// chosen for printability + acoustic-resonance fit.
/// </summary>
public static class PulsejetGeometry
{
    /// <summary>
    /// Intake-horn length as a fraction of total tube length. Short flare
    /// for forward-firing diffuser geometry per Foa §11.3.
    /// </summary>
    public const double IntakeHornFractionOfTotal = 0.05;

    /// <summary>
    /// Diffuser length as a fraction of total tube length. Convergent
    /// section transitioning from intake horn into combustor area.
    /// </summary>
    public const double DiffuserFractionOfTotal = 0.10;

    /// <summary>
    /// Combustor length as a fraction of total tube length. Constant-area
    /// section where the cyclic Humphrey combustion happens.
    /// </summary>
    public const double CombustorFractionOfTotal = 0.20;

    /// <summary>
    /// Tailpipe length as a fraction of total tube length. The dominant
    /// section — long open tube driving the half-wave acoustic mode that
    /// pairs with combustor Helmholtz resonance.
    /// </summary>
    public const double TailpipeFractionOfTotal = 0.65;

    // 0.05 + 0.10 + 0.20 + 0.65 = 1.00.

    /// <summary>
    /// Build the contour from the design knobs.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="design"/>'s
    /// <see cref="AirbreathingEngineDesign.Kind"/> is not
    /// <see cref="AirbreathingEngineKind.Pulsejet"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the effective intake area, combustor area, tailpipe
    /// area, or total tube length is NaN or non-positive (after the
    /// v5-compatibility fallbacks from
    /// <see cref="AirbreathingEngineDesign.PulsejetIntakeArea_m2"/> to
    /// <see cref="AirbreathingEngineDesign.InletThroatArea_m2"/> etc.).
    /// </exception>
    public static PulsejetContour From(AirbreathingEngineDesign design)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (design.Kind != AirbreathingEngineKind.Pulsejet)
            throw new ArgumentException(
                $"PulsejetGeometry.From requires Kind == Pulsejet; got {design.Kind}.",
                nameof(design));

        // Aliases: pulsejet-specific fields fall back to legacy fields when 0
        // (matches the cycle solver's v5-compatibility behaviour).
        double intakeArea_m2 = design.PulsejetIntakeArea_m2 > 0
            ? design.PulsejetIntakeArea_m2
            : design.InletThroatArea_m2;
        double tailpipeArea_m2 = design.PulsejetTailpipeArea_m2 > 0
            ? design.PulsejetTailpipeArea_m2
            : design.NozzleExitArea_m2;
        double totalLength_m = design.PulsejetTubeLength_m > 0
            ? design.PulsejetTubeLength_m
            : (design.CombustorLength_m > 0 ? design.CombustorLength_m / CombustorFractionOfTotal : 0);

        if (double.IsNaN(intakeArea_m2) || intakeArea_m2 <= 0
            || double.IsNaN(design.CombustorArea_m2) || design.CombustorArea_m2 <= 0
            || double.IsNaN(tailpipeArea_m2) || tailpipeArea_m2 <= 0
            || double.IsNaN(totalLength_m) || totalLength_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(design),
                $"Pulsejet contour requires positive intake / combustor / tailpipe areas and tube length; got "
              + $"intakeArea_m2={intakeArea_m2:F6} m^2, "
              + $"CombustorArea_m2={design.CombustorArea_m2:F6} m^2, "
              + $"tailpipeArea_m2={tailpipeArea_m2:F6} m^2, "
              + $"totalLength_m={totalLength_m:F4} m.");

        double rIntake    = AreaToRadius(intakeArea_m2);
        double rCombustor = AreaToRadius(design.CombustorArea_m2);
        double rExit      = AreaToRadius(tailpipeArea_m2);

        double xIntake     = 0.0;
        double xHornExit   = xIntake     + IntakeHornFractionOfTotal   * totalLength_m;
        double xDiffExit   = xHornExit   + DiffuserFractionOfTotal     * totalLength_m;
        double xCombExit   = xDiffExit   + CombustorFractionOfTotal    * totalLength_m;
        double xExit       = xCombExit   + TailpipeFractionOfTotal     * totalLength_m;

        // Tailpipe is constant-area → both ends share the combustor radius;
        // the exit is tapered to the design's exit area at the very end.
        // Model the tailpipe with two stations: combustor exit → tailpipe
        // exit (taper happens at the last station).
        var stations = new[]
        {
            new PulsejetStation(xIntake,    rIntake,    PulsejetSection.IntakeHorn),
            new PulsejetStation(xHornExit,  rCombustor, PulsejetSection.Diffuser),
            new PulsejetStation(xDiffExit,  rCombustor, PulsejetSection.Combustor),
            new PulsejetStation(xCombExit,  rCombustor, PulsejetSection.Tailpipe),
            new PulsejetStation(xExit,      rExit,      PulsejetSection.Exit),
        };

        // Combustor index = first station marked Combustor (here index 2).
        int combustorIndex = 2;

        return new PulsejetContour(
            Stations:       stations,
            TotalLength_m:  xExit - xIntake,
            CombustorIndex: combustorIndex);
    }

    private static double AreaToRadius(double area_m2)
        => Math.Sqrt(area_m2 / Math.PI);
}
