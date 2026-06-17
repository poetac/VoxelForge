// YagiUdaAntennaVoxelBuilder.cs — Sprint ANT.W5-voxel Yagi-Uda end-fire
// array antenna voxel builder. Produces a 3D-printable assembly from
// an AntennaLinkDesign (YagiUda topology):
//
//   ── Array layout ──
//   Axis: +Z (end-fire boresight). All elements are thin cylinders
//   oriented along +X (perpendicular to boresight), centred on the boom.
//
//   For N_dir directors (default 3, giving 7-element total):
//     Reflector  at z = 0, length = 0.525λ (5.25 % longer than λ/2)
//     Driven     at z = d_ref  = 0.2λ (typical reflector–driven spacing)
//     Director k at z = d_ref + d_drv + k × d_dir, k = 0 … N_dir-1
//     Director spacing d_dir = 0.3λ
//     Director length = 0.45λ (10 % shorter than λ/2)
//
//   ── Element diameter ──
//   Default: max(0.01λ, PrintMaterial min feature). 0.01λ is the
//   electrically thin wire assumption used in Yagi design tables
//   (Yagi 1928, Uda 1926). Typically 2–3 mm at UHF.
//
//   ── Boom ──
//   Centre cylinder from z_reflector to z_lastDirector + 0.05λ each
//   side. Diameter = element diameter (self-supporting monolith).
//
//   ── Overhang gate ──
//   Yagi elements are perpendicular to +Z. Overhang angle relative to
//   horizontal print bed = 90°. For FDM/LPBF (max overhang 45°) the
//   elements cannot self-support without orientation or supports.
//   ANT.W6 gate ANTENNA_ELEMENT_OVERHANG_UNSUPPORTED fires when the
//   maximum print-material overhang is < 90°.
//
// References:
//   Yagi H. (1928). "Beam transmission of ultra short waves." IRE 16 (6).
//   Balanis C. (2016). "Antenna Theory," 4th ed., §10.3 (Yagi-Uda arrays).

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W5-voxel — PicoGK voxel builder for a Yagi-Uda end-fire
/// array (boom + reflector + driven + N directors).
/// </summary>
internal static class YagiUdaAntennaVoxelBuilder
{
    /// <summary>Reflector element length as fraction of λ.</summary>
    internal const double ReflectorLengthFactor = 0.525;
    /// <summary>Driven element length as fraction of λ (half-wave).</summary>
    internal const double DrivenLengthFactor    = 0.500;
    /// <summary>Director element length as fraction of λ.</summary>
    internal const double DirectorLengthFactor  = 0.450;
    /// <summary>Reflector-to-driven spacing as fraction of λ.</summary>
    internal const double RefToDriverSpacing    = 0.200;
    /// <summary>Director-to-director spacing as fraction of λ.</summary>
    internal const double DirectorSpacing       = 0.300;
    /// <summary>Boom end extension beyond last element as fraction of λ.</summary>
    internal const double BoomEndExtension      = 0.050;
    /// <summary>Element diameter as fraction of λ (electrically thin wire).</summary>
    internal const double ElementDiameterFactor = 0.010;
    /// <summary>Default number of director elements (7-element Yagi).</summary>
    internal const int    DefaultDirectorCount  = 3;

    internal static YagiUdaGeometryResult Build(
        AntennaLinkDesign design,
        double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm));

        double lambda_mm     = AntennaSolver.SpeedOfLight_ms / design.Frequency_Hz * 1000.0;
        double minFeature    = PrintMaterialTable.MinFeatureDiameter_mm(design.PrintMaterialKind);
        double elemDia_mm    = Math.Max(ElementDiameterFactor * lambda_mm, minFeature);
        double elemRad_mm    = 0.5 * elemDia_mm;

        // Element lengths [mm].
        double L_reflector   = ReflectorLengthFactor * lambda_mm;
        double L_driven      = DrivenLengthFactor    * lambda_mm;
        double L_director    = DirectorLengthFactor  * lambda_mm;

        // Z-positions of element centres [mm]. Reflector at z=0.
        double z_reflector   = 0.0;
        double z_driven      = RefToDriverSpacing * lambda_mm;
        int    nDir          = DefaultDirectorCount;
        double d_dir         = DirectorSpacing * lambda_mm;

        // Boom extent.
        double z_lastDir     = z_driven + nDir * d_dir;  // past last director
        double boomStart     = z_reflector - BoomEndExtension * lambda_mm;
        double boomEnd       = z_lastDir   + BoomEndExtension * lambda_mm;
        double boomLen_mm    = boomEnd - boomStart;

        // Overhang check: Yagi elements are horizontal (perpendicular to +Z).
        double maxOverhang   = PrintMaterialTable.MaxOverhangAngle_deg(design.PrintMaterialKind);
        bool overhangFailed  = maxOverhang < 89.0;  // elements are at 90° overhang

        // ── Bounding box ─────────────────────────────────────────────────
        float halfElemMax = (float)(0.5 * L_reflector + elemRad_mm);
        float pad         = (float)Math.Max(2.0 * voxelSize_mm, 2.0);
        var bounds = new BBox3(
            new Vector3(-halfElemMax - pad, -(float)elemRad_mm - pad, (float)boomStart - pad),
            new Vector3( halfElemMax + pad,  (float)elemRad_mm + pad, (float)boomEnd   + pad));

        // ── Boom ─────────────────────────────────────────────────────────
        var boomImpl = new CylinderImplicit(
            start:     new Vector3(0f, 0f, (float)boomStart),
            direction: new Vector3(0f, 0f, 1f),
            radius:    (float)elemRad_mm,
            length:    (float)boomLen_mm);
        Voxels body = LibraryScope.MakeVoxels(boomImpl, bounds);

        // ── Helper: add a rod element along X at the given z-position ────
        void AddElement(double zPos, double length)
        {
            double halfLen = 0.5 * length;
            var impl = new CylinderImplicit(
                start:     new Vector3(-(float)halfLen, 0f, (float)zPos),
                direction: new Vector3(1f, 0f, 0f),
                radius:    (float)elemRad_mm,
                length:    (float)length);
            Voxels vox = LibraryScope.MakeVoxels(impl, bounds);
            body.BoolAdd(vox);
        }

        AddElement(z_reflector, L_reflector);
        AddElement(z_driven,    L_driven);
        for (int k = 0; k < nDir; k++)
            AddElement(z_driven + (k + 1) * d_dir, L_director);

        // ── Wall-safe smoothing ───────────────────────────────────────────
        double safeSmooth = 0.25 * elemRad_mm;
        if (safeSmooth >= 0.02)
            body.Smoothen((float)safeSmooth);

        return new YagiUdaGeometryResult(
            DrivenElementLength_mm:   L_driven,
            ReflectorLength_mm:       L_reflector,
            DirectorLength_mm:        L_director,
            DirectorCount:            nDir,
            ElementDiameter_mm:       elemDia_mm,
            BoomLength_mm:            boomLen_mm,
            BoomDiameter_mm:          elemDia_mm,
            ElementOverhangViolated:  overhangFailed,
            Voxels:                   new PicoGKVoxelHandle(body));
    }
}
