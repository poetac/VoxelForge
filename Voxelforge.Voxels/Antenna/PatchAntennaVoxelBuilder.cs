// PatchAntennaVoxelBuilder.cs — Sprint ANT.W6 microstrip patch antenna
// voxel builder. Produces a three-layer rectangular solid from an
// AntennaLinkDesign (Patch topology):
//
//   ── Layer stack (z-axis is stack normal, +Z = air side) ──
//   Ground plane:  z ∈ [−h_sub − t_conductor, −h_sub]  (copper layer)
//   Substrate:     z ∈ [−h_sub, 0]                      (dielectric slab)
//   Patch:         z ∈ [0, t_conductor]                 (copper layer)
//
//   Assembly centred at XY = (0, 0); patch width along X, length along Y.
//
//   ── Patch dimensions ──
//   If PatchWidth_mm = 0 and PatchLength_mm = 0 (auto-compute):
//     Width  W = c/(2f) · sqrt(2/(ε_r+1))    [Bahl-Trivedi 1977, eq. 3]
//     Length L = c/(2f·sqrt(ε_r_eff)) − 2ΔL  [resonant length]
//     ΔL = 0.412h·(ε_eff+0.3)(W/h+0.264)/((ε_eff−0.258)(W/h+0.8))
//   If either dimension is supplied → use directly.
//
//   ── Print-material checks (ANT.W6) ──
//   ANTENNA_SUBSTRATE_TOO_THIN: SubstrateThickness_mm < MinFeatureDiameter
//   ANTENNA_GEOMETRY_RF_MISMATCH: user-supplied dimensions give f_r
//     deviating >5 % from the design frequency (ANT.W7 coupling check).
//
// References:
//   Bahl I.J., Trivedi D.K. (1977). "A designer's guide to microstrip
//     line." Microwaves 16 (5).
//   Balanis C. (2016). "Antenna Theory," 4th ed., §14.2.

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Antenna;

/// <summary>
/// Sprint ANT.W6 — PicoGK voxel builder for a rectangular microstrip
/// patch antenna (ground plane + dielectric substrate + patch conductor).
/// </summary>
internal static class PatchAntennaVoxelBuilder
{
    /// <summary>
    /// Conductor layer thickness [mm]. Represents a printed-circuit
    /// copper layer (35 µm typical PCB) scaled up to the minimum printable
    /// feature size. Floored to the material minimum but never below 0.1 mm.
    /// </summary>
    internal const double ConductorThickness_mm = 0.3;

    internal static PatchGeometryResult Build(
        AntennaLinkDesign design,
        double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm));

        PrintMaterial mat     = design.PrintMaterialKind;
        double minFeature     = PrintMaterialTable.MinFeatureDiameter_mm(mat);
        double eps_r          = PrintMaterialTable.RelativePermittivity(mat);
        double h_sub          = Math.Max(design.SubstrateThickness_mm, minFeature);
        bool substrateTooThin = design.SubstrateThickness_mm > 0
                              && design.SubstrateThickness_mm < minFeature;
        double t_cond         = Math.Max(ConductorThickness_mm, Math.Max(minFeature, 2.0 * voxelSize_mm));

        // Patch dimensions (auto or user-supplied).
        double W_mm = design.PatchWidth_mm  > 0
            ? design.PatchWidth_mm
            : AntennaSolver.ComputePatchWidth_mm(design.Frequency_Hz, eps_r);
        double L_mm = design.PatchLength_mm > 0
            ? design.PatchLength_mm
            : AntennaSolver.ComputePatchLength_mm(design.Frequency_Hz, eps_r, W_mm, h_sub);

        // Floor dimensions to minimum feature size.
        W_mm = Math.Max(W_mm, minFeature);
        L_mm = Math.Max(L_mm, minFeature);

        // Resonant frequency and mismatch check.
        double f_r        = AntennaSolver.ComputePatchResonantFrequency_Hz(design);
        bool rfMismatch   = AntennaSolver.CheckPatchGeometryRfMismatch(design);

        // ── Bounding box ─────────────────────────────────────────────────
        float pad   = (float)Math.Max(2.0 * voxelSize_mm, 1.0);
        float halfW = (float)(0.5 * W_mm);
        float halfL = (float)(0.5 * L_mm);
        float zMin  = -(float)(h_sub + t_cond) - pad;
        float zMax  =  (float)t_cond            + pad;
        var bounds  = new BBox3(
            new Vector3(-halfW - pad, -halfL - pad, zMin),
            new Vector3( halfW + pad,  halfL + pad, zMax));

        // ── Ground plane (bottom copper layer) ───────────────────────────
        var groundImpl = new BoxImplicit(
            new Vector3(-(float)(0.5*W_mm), -(float)(0.5*L_mm), -(float)(h_sub + t_cond)),
            new Vector3( (float)(0.5*W_mm),  (float)(0.5*L_mm), -(float)h_sub));
        Voxels body = LibraryScope.MakeVoxels(groundImpl, bounds);

        // ── Dielectric substrate ─────────────────────────────────────────
        var substrateImpl = new BoxImplicit(
            new Vector3(-(float)(0.5*W_mm), -(float)(0.5*L_mm), -(float)h_sub),
            new Vector3( (float)(0.5*W_mm),  (float)(0.5*L_mm),  0f));
        Voxels subVox = LibraryScope.MakeVoxels(substrateImpl, bounds);
        body.BoolAdd(subVox);

        // ── Patch conductor (top copper layer) ───────────────────────────
        var patchImpl = new BoxImplicit(
            new Vector3(-(float)(0.5*W_mm), -(float)(0.5*L_mm),  0f),
            new Vector3( (float)(0.5*W_mm),  (float)(0.5*L_mm),  (float)t_cond));
        Voxels patchVox = LibraryScope.MakeVoxels(patchImpl, bounds);
        body.BoolAdd(patchVox);

        return new PatchGeometryResult(
            PatchWidth_mm:         W_mm,
            PatchLength_mm:        L_mm,
            SubstrateThickness_mm: h_sub,
            ResonantFrequency_Hz:  f_r,
            Material:              mat,
            SubstrateTooThin:      substrateTooThin,
            GeometryRfMismatch:    rfMismatch,
            Voxels:                new PicoGKVoxelHandle(body));
    }
}
