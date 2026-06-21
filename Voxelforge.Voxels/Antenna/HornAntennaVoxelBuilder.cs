// HornAntennaVoxelBuilder.cs — Sprint ANT.W5-voxel conical horn antenna
// geometry builder. Produces a hollow conical shell (the horn body) +
// a hollow cylindrical waveguide section from an AntennaLinkDesign
// (Horn topology).
//
//   ── Horn geometry ──
//   Conical frustum shell from z=0 (throat, radius R_throat) to
//   z=L_horn (aperture, radius R_aperture), wall thickness t_wall.
//   Aperture flare angle θ_flare = atan((R_aperture−R_throat)/L_horn).
//   Typical range: 10°–20° for low-sidelobe conical horns.
//
//   Cluster anchors (conical horn catalogue, Balanis 2016 §13.4):
//     λ_throat = 0.6λ radius (smallest resonant waveguide ≈ cutoff)
//     λ_aperture = 5λ radius (high-gain, 25 dBi cluster)
//     L_horn derived from flare: L = (R_ap − R_th) / tan(θ_flare)
//
//   ── Waveguide section ──
//   Hollow cylinder of radius R_throat at z ∈ [−L_wg, 0], length
//   L_wg = 0.5 × L_horn (standard backshort-to-throat spacing).
//
//   ── Construction (PicoGK booleans) ──
//   outer frustum (solid) → voxels_outer
//   inner frustum (solid, smaller by t_wall) → voxels_inner
//   BoolSubtract inner from outer → hollow horn shell
//   outer waveguide cylinder → voxels_wg_outer
//   inner waveguide cylinder → voxels_wg_inner
//   BoolSubtract inner_wg from outer_wg → hollow waveguide tube
//   BoolAdd waveguide to horn shell → complete assembly
//
//   ── Parameter derivation ──
//   At frequency f:
//     λ = c/f, R_throat = max(0.6λ, 4×voxel), R_aperture = 5λ
//     Flare 12.5° (cluster mid between 10° and 15°)
//     t_wall = max(2 mm, 4×voxel)
//     L_horn = (R_aperture − R_throat) / tan(12.5°)
//     L_wg   = 0.5 × L_horn
//
// References:
//   Balanis C. (2016). "Antenna Theory," 4th ed., §13.4 (conical horns).
//   Goldsmith P.F. (1998). "Quasioptical Systems," §3.2 (horn feed geometry).

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5-voxel — PicoGK voxel builder for a conical horn
/// antenna (hollow frustum shell + circular waveguide section).
/// </summary>
internal static class HornAntennaVoxelBuilder
{
    /// <summary>Throat radius as a fraction of wavelength.</summary>
    internal const double ThroatRadiusFactor   = 0.6;
    /// <summary>Aperture radius as a fraction of wavelength.</summary>
    internal const double ApertureRadiusFactor = 5.0;
    /// <summary>Horn flare half-angle [deg] (cluster mid-anchor).</summary>
    internal const double FlareAngle_deg       = 12.5;
    /// <summary>Waveguide-section length as fraction of horn length.</summary>
    internal const double WaveguideLengthFraction = 0.5;
    /// <summary>Smoothing feature fraction (PicoGK pitfall #1 cap).</summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the conical horn voxel body from a validated
    /// <see cref="AntennaLinkDesign"/> with
    /// <see cref="AntennaKind.Horn"/> Tx topology.
    /// </summary>
    internal static HornGeometryResult Build(
        AntennaLinkDesign design,
        double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm));

        double lambda_mm   = AntennaSolver.SpeedOfLight_ms / design.Frequency_Hz * 1000.0;
        double wallMin     = Math.Max(2.0, 4.0 * voxelSize_mm);
        double t_wall      = wallMin;
        double R_throat    = Math.Max(ThroatRadiusFactor   * lambda_mm,
                                      4.0 * voxelSize_mm);
        double R_aperture  = Math.Max(ApertureRadiusFactor * lambda_mm,
                                      R_throat + t_wall + 1.0);
        double flareRad    = FlareAngle_deg * Math.PI / 180.0;
        double L_horn      = (R_aperture - R_throat) / Math.Tan(flareRad);
        double L_wg        = WaveguideLengthFraction * L_horn;
        double overallLen  = L_horn + L_wg;

        // ── Bounding box ─────────────────────────────────────────────────
        float pad   = (float)Math.Max(2.0 * voxelSize_mm, 2.0);
        float xyMax = (float)(R_aperture + t_wall + pad);
        var bounds  = new BBox3(
            new Vector3(-xyMax, -xyMax, -(float)(L_wg + pad)),
            new Vector3( xyMax,  xyMax,  (float)(L_horn + pad)));

        // ── Horn shell: outer frustum − inner frustum ─────────────────────
        var outerHorn = new ConeFrustumImplicit(
            r0: (float)R_throat,
            r1: (float)R_aperture,
            height: (float)L_horn);
        var innerHorn = new ConeFrustumImplicit(
            r0: (float)Math.Max(0.0, R_throat - t_wall),
            r1: (float)Math.Max(0.0, R_aperture - t_wall),
            height: (float)L_horn);

        Voxels outerHornVox = LibraryScope.MakeVoxels(outerHorn, bounds);
        Voxels innerHornVox = LibraryScope.MakeVoxels(innerHorn, bounds);
        outerHornVox.BoolSubtract(innerHornVox);
        Voxels body = outerHornVox;

        // ── Waveguide shell: outer cylinder − inner cylinder ───────────────
        var outerWg = new CylinderImplicit(
            start:     new Vector3(0f, 0f, -(float)L_wg),
            direction: new Vector3(0f, 0f,  1f),
            radius:    (float)(R_throat + t_wall),
            length:    (float)L_wg);
        var innerWg = new CylinderImplicit(
            start:     new Vector3(0f, 0f, -(float)L_wg),
            direction: new Vector3(0f, 0f,  1f),
            radius:    (float)R_throat,
            length:    (float)L_wg);

        Voxels outerWgVox = LibraryScope.MakeVoxels(outerWg, bounds);
        Voxels innerWgVox = LibraryScope.MakeVoxels(innerWg, bounds);
        outerWgVox.BoolSubtract(innerWgVox);
        body.BoolAdd(outerWgVox);

        // ── Wall-safe smoothing (PicoGK pitfall #1) ───────────────────────
        double safeSmooth = SmoothingFeatureFraction * t_wall;
        if (safeSmooth >= 0.02)
            body.Smoothen((float)safeSmooth);

        return new HornGeometryResult(
            ThroatDiameter_mm:    2.0 * R_throat,
            ApertureDiameter_mm:  2.0 * R_aperture,
            HornLength_mm:        L_horn,
            WaveguideLength_mm:   L_wg,
            WallThickness_mm:     t_wall,
            FlareAngle_deg:       FlareAngle_deg,
            OverallAxialLength_mm: overallLen,
            Voxels:               new PicoGKVoxelHandle(body));
    }
}

/// <summary>
/// Exact SDF for a solid capped cone (frustum) with axis along +Z,
/// from z = 0 (radius r0) to z = height (radius r1). Formula adapted
/// from Inigo Quilez's <c>sdCappedCone</c> (iquilezles.org/articles/
/// distfunctions, 2020). Negative inside, positive outside.
/// </summary>
internal sealed class ConeFrustumImplicit : IImplicit
{
    private readonly float _r0;
    private readonly float _r1;
    private readonly float _h;

    internal ConeFrustumImplicit(float r0, float r1, float height)
    {
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _r0 = r0; _r1 = r1; _h = height;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float qr = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float qz = p.Z - _h * 0.5f;          // re-centre to IQ convention

        // IQ sdCappedCone: r1 = bottom radius, r2 = top radius, h = half-height.
        // IQ's k2 = (r2−r1, 2·h) with h = the half-height — so k2y must use halfH,
        // not the full height _h (which would double the slant vector and
        // mislocate the zero-isosurface along the flared flank).
        float halfH = _h * 0.5f;
        float k2x   = _r1 - _r0;
        float k2y   = 2.0f * halfH;

        float cax = qr - MathF.Min(qr, qz < 0 ? _r0 : _r1);
        float cay = MathF.Abs(qz) - halfH;

        // cb = q - k1 + k2 * clamp(dot(k1-q, k2)/dot(k2,k2), 0, 1)
        float k1q_x = _r1 - qr;  float k1q_y = halfH - qz;
        float k2dot = k2x * k2x + k2y * k2y;
        float t     = Math.Clamp((k1q_x * k2x + k1q_y * k2y) / k2dot, 0f, 1f);
        float cbx   = qr  - _r1   + k2x * t;
        float cby   = qz  - halfH + k2y * t;

        bool inside = cbx < 0f && cay < 0f;
        float dist2 = MathF.Min(cax * cax + cay * cay, cbx * cbx + cby * cby);
        return inside ? -MathF.Sqrt(dist2) : MathF.Sqrt(dist2);
    }
}
