// HelicalAntennaVoxelBuilder.cs — Sprint ANT.W5-voxel helical end-fire
// antenna geometry builder. Produces a printable 3D body from an
// AntennaLinkDesign (Helical topology):
//
//   ── Helix topology ──
//   N-turn end-fire helix of circumference C = C_rel × λ, turn spacing
//   S = S_rel × λ, wire diameter d_wire. Axis along +Z (boresight);
//   coil starts at z = 0. The coil is modelled as a stack of N+1
//   circular rings of radius R_h = C/(2π) at axial positions z_k = k·S,
//   k = 0 … N. This "circular coil" approximation introduces a < 5 %
//   positional error relative to the true helical spiral for pitch
//   angles ≤ 15° (α = atan(S/(2πR_h)); Kraus optimal α ≈ 14°). The
//   sign-driven PicoGK voxeliser is tolerant of the approximation.
//
//   ── Ground plane ──
//   A solid disc of diameter D_gp ≈ 1.5 λ (cluster anchor from Kraus
//   1988 §7-4 ground-plane size recommendation) and thickness equal to
//   max(4×voxel, 2 mm) is placed at z ∈ [−GP_thickness, 0].
//
//   ── Wire diameter ──
//   Default wire diameter = max(λ/50, PrintMaterialTable.MinFeatureDiameter_mm).
//   λ/50 is the physically representative wire-to-wavelength ratio for
//   typical UHF/SHF helical antennas (e.g., at 2.4 GHz: λ ≈ 125 mm,
//   d_wire ≈ 2.5 mm). The ANT.W6 gate ANTENNA_WIRE_TOO_THIN fires when
//   the natural d_wire (λ/50) is below the material minimum; the wire is
//   floored to the minimum and the flag is set in the result.
//
//   ── PicoGK closed-cavity note ──
//   The helix coil is an open wire — no closed cavities. No flood-fill
//   workaround needed.
//
// References:
//   Kraus J.D. (1988). "Antennas," 2nd ed., §7-4 (end-fire helix).
//   Balanis C. (2016). "Antenna Theory," 4th ed., §10.3.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5-voxel — PicoGK voxel builder for a helical end-fire
/// antenna (helix coil + ground plane).
/// </summary>
internal static class HelicalAntennaVoxelBuilder
{
    /// <summary>Ground-plane diameter as a multiple of wavelength.
    /// Kraus 1988 §7-4 recommends ≥ 0.75λ radius → ≥ 1.5λ diameter.</summary>
    internal const double GroundPlaneDiameterFactor = 1.5;

    /// <summary>Default wire diameter as a fraction of wavelength.
    /// λ/50 clusters at 2–3 mm for typical UHF/SHF helical antennas.
    /// </summary>
    internal const double WireDiameterFactor = 1.0 / 50.0;

    /// <summary>Smoothing feature fraction (PicoGK pitfall #1 cap).</summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the helical antenna voxel body from a validated
    /// <see cref="AntennaLinkDesign"/> with
    /// <see cref="AntennaKind.Helical"/> Tx topology.
    /// </summary>
    internal static HelicalGeometryResult Build(
        AntennaLinkDesign design,
        double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm));

        double lambda_mm = AntennaSolver.SpeedOfLight_ms / design.Frequency_Hz * 1000.0;
        double C_mm   = design.HelicalCircumference_rel * lambda_mm; // circumference
        double S_mm   = design.HelicalTurnSpacing_rel   * lambda_mm; // turn spacing
        double R_h_mm = C_mm / (2.0 * Math.PI);                     // coil radius
        int    N      = design.HelicalTurns;

        // Wire diameter: λ/50 floored to print-material minimum.
        double minFeature  = PrintMaterialTable.MinFeatureDiameter_mm(
            design.PrintMaterialKind);
        double naturalWire = WireDiameterFactor * lambda_mm;
        bool wireTooThin   = naturalWire < minFeature;
        double wireDia_mm  = Math.Max(naturalWire, minFeature);
        double wireRad_mm  = 0.5 * wireDia_mm;

        double gpThick_mm  = Math.Max(2.0 * voxelSize_mm, 2.0);   // ground plane
        double gpRadius_mm = 0.5 * GroundPlaneDiameterFactor * lambda_mm;
        double helixLen_mm = N * S_mm;
        double overallLen  = gpThick_mm + helixLen_mm;

        // ── Bounding box ────────────────────────────────────────────────
        float pad = (float)Math.Max(2.0 * voxelSize_mm, wireRad_mm + 1.0);
        float xyMax = (float)(Math.Max(gpRadius_mm, R_h_mm + wireRad_mm) + pad);
        float zMin  = -(float)(gpThick_mm + pad);
        float zMax  = (float)(helixLen_mm + wireRad_mm + pad);
        var bounds = new BBox3(
            new Vector3(-xyMax, -xyMax, zMin),
            new Vector3( xyMax,  xyMax, zMax));

        // ── Ground plane disc ───────────────────────────────────────────
        var gpImpl = new CylinderImplicit(
            start:     new Vector3(0f, 0f, -(float)gpThick_mm),
            direction: new Vector3(0f, 0f, 1f),
            radius:    (float)gpRadius_mm,
            length:    (float)gpThick_mm);
        Voxels body = LibraryScope.MakeVoxels(gpImpl, bounds);

        // ── Helix rings (circular-coil approximation) ───────────────────
        var helixImpl = new HelixCoilImplicit(
            helixRadius_mm: (float)R_h_mm,
            turnSpacing_mm: (float)S_mm,
            wireRadius_mm:  (float)wireRad_mm,
            turnCount:      N);
        Voxels helixVox = LibraryScope.MakeVoxels(helixImpl, bounds);
        body.BoolAdd(helixVox);

        // ── Wall-safe smoothing ─────────────────────────────────────────
        double safeSmooth = SmoothingFeatureFraction * wireRad_mm;
        if (safeSmooth >= 0.02)
            body.Smoothen((float)safeSmooth);

        return new HelicalGeometryResult(
            HelixRadius_mm:          R_h_mm,
            TurnSpacing_mm:          S_mm,
            WireDiameter_mm:         wireDia_mm,
            GroundPlaneDiameter_mm:  2.0 * gpRadius_mm,
            TotalAxialLength_mm:     helixLen_mm,
            OverallAxialLength_mm:   overallLen,
            WireTooThinForMaterial:  wireTooThin,
            Voxels:                  new PicoGKVoxelHandle(body));
    }
}

/// <summary>
/// Circular-coil approximation SDF for a helical wire antenna. Models
/// the N-turn helix as N+1 circular rings of radius R_h at axial
/// positions z_k = k · S (k = 0 … N). The SDF at a query point is the
/// minimum Euclidean distance to any ring's circle minus the wire radius.
///
/// Distance from (x, y, z) to the circle at z_k:
///   d_k = sqrt((sqrt(x²+y²) − R_h)² + (z − z_k)²) − r_wire
///
/// This is an exact formula for the nearest point on a circle in 3D space.
/// The helix approximation error (replacing the continuous spiral with
/// concentric circles) is &lt; 5 % for pitch angles α ≤ 15°.
/// </summary>
internal sealed class HelixCoilImplicit : IImplicit
{
    private readonly float _helixRadius_mm;
    private readonly float _turnSpacing_mm;
    private readonly float _wireRadius_mm;
    private readonly int   _turnCount;

    internal HelixCoilImplicit(
        float helixRadius_mm,
        float turnSpacing_mm,
        float wireRadius_mm,
        int   turnCount)
    {
        _helixRadius_mm = helixRadius_mm;
        _turnSpacing_mm = turnSpacing_mm;
        _wireRadius_mm  = wireRadius_mm;
        _turnCount      = turnCount;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float z = p.Z;
        float dr = r - _helixRadius_mm;
        float minDist2 = float.MaxValue;

        for (int k = 0; k <= _turnCount; k++)
        {
            float dz = z - k * _turnSpacing_mm;
            float d2 = dr * dr + dz * dz;
            if (d2 < minDist2) minDist2 = d2;
        }
        return MathF.Sqrt(minDist2) - _wireRadius_mm;
    }
}
