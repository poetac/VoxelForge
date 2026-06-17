// AcousticDamperGeometry — voxel cavities for the OOB-6 (#200) acoustic
// damper module. Sprint B-3 (2026-04-30). Pairs with the closed-form
// physics in `Voxelforge.Core/Combustion/Stability/AcousticDamper.cs`.
//
// Two damper families ship in v1:
//
//   • Helmholtz array — N resonators distributed at evenly-spaced
//     azimuths around the chamber barrel. Each resonator is a buried
//     cavity (small cylinder embedded in the outer jacket) connected
//     to the chamber bore through a short narrow neck. The cavity
//     volume × neck-area product sets f₀; the chamber sees the neck
//     opening only.
//
//                 ▼ chamber bore
//      …═════════════════════════════════════…   inner liner
//      ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒    coolant channels
//      ◁ neck ▷                              ▷
//        ┌──┐                                       outer jacket
//        │  │   ← buried cavity (V_cavity)
//        └──┘
//      ─────────────────────────────────────       outer wall
//
//   • Quarter-wave array — N long thin radial cavities drilled outward
//     from the chamber bore through the jacket, terminating in the
//     jacket material (closed end). f₀ = c / (4·L) is the open-closed
//     pipe resonance.
//
// Both are implemented by reusing the sensor-boss CylinderImplicit
// pattern at ChamberVoxelBuilder.cs:930 — the voxel primitive is a
// simple radial bore drilled into the outer jacket, identical in
// implementation to the existing instrumentation-boss feature.

using System;
using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// Geometry inputs for placing the acoustic-damper voxel features on
/// the chamber. Distinct from the physics-side
/// <c>AcousticDamperConfig</c> in <c>Voxelforge.Combustion.Stability</c>;
/// this record adds the placement parameters (axial position, jacket
/// extent) that the voxel builder needs but the physics evaluator
/// doesn't care about.
/// </summary>
public sealed record AcousticDamperGeometrySpec(
    Combustion.Stability.AcousticDamperType Type,
    int    Count,                           // resonator count around chamber circumference
    double NeckDiameter_mm,                 // Helmholtz neck = inner cylinder
    double NeckLength_mm,
    double CavityDiameter_mm,               // Helmholtz cavity outer cylinder
    double CavityLength_mm,                 // Helmholtz cavity axial extent
    double QuarterWaveLength_mm,            // quarter-wave radial cavity length
    double QuarterWaveDiameter_mm,
    double AxialFraction,                   // 0 = injector face, 1 = exit
    double InnerRadius_mm,                  // chamber-bore radius at axial station
    double OuterJacketRadius_mm)            // outer jacket radius at the same station
{
    public bool IsActive => Type != Combustion.Stability.AcousticDamperType.None
                          && Count > 0;
}

public static class AcousticDamperGeometry
{
    /// <summary>
    /// Add the damper-array cavities to <paramref name="shell"/>.
    /// Helmholtz: N (cavity − neck) pairs subtracted from the jacket
    /// at evenly-spaced azimuths. Quarter-wave: N long radial bores
    /// drilled into the jacket from the chamber-bore wall.
    ///
    /// Caller responsibility: validate <see cref="AcousticDamperGeometrySpec.IsActive"/>
    /// before calling (the geometry builder short-circuits silently
    /// when inactive). The chamber inner liner remains intact: only
    /// the neck (Helmholtz) or open end (quarter-wave) opens onto
    /// the chamber gas; the rest of the cavity sits in jacket
    /// material between the inner liner and outer jacket.
    /// </summary>
    public static void AddDamperArray(
        Voxels                          shell,
        BBox3                           bounds,
        AcousticDamperGeometrySpec      spec,
        double                          chamberLength_mm)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!spec.IsActive) return;
        if (chamberLength_mm <= 0)
            throw new ArgumentException("chamber length must be positive", nameof(chamberLength_mm));

        float xStation = (float)(spec.AxialFraction * chamberLength_mm);

        switch (spec.Type)
        {
            case Combustion.Stability.AcousticDamperType.Helmholtz:
                AddHelmholtzArray(shell, bounds, spec, xStation);
                break;
            case Combustion.Stability.AcousticDamperType.QuarterWave:
                AddQuarterWaveArray(shell, bounds, spec, xStation);
                break;
            default:
                // Unknown type — silently skip rather than crash a
                // legitimate build pipeline on a future enum addition.
                break;
        }
    }

    /// <summary>
    /// Helmholtz array: per-resonator (cavity ∪ neck) subtracted from
    /// the chamber outer solid. The cavity is a radial cylinder buried
    /// in the jacket; the neck is a smaller-diameter radial cylinder
    /// connecting the chamber bore to the cavity. Both share the same
    /// radial axis at the resonator's azimuth.
    /// </summary>
    private static void AddHelmholtzArray(
        Voxels                       shell,
        BBox3                        bounds,
        AcousticDamperGeometrySpec   spec,
        float                        xStation)
    {
        // Sanity: zero-length neck or zero-diameter cavity → nothing
        // to subtract. The physics-side IsActive check is by-the-book,
        // but the voxel builder gets called from external paths too.
        if (spec.NeckDiameter_mm <= 0 || spec.CavityDiameter_mm <= 0
            || spec.CavityLength_mm <= 0)
            return;

        float rInner = (float)spec.InnerRadius_mm;
        float rOuter = (float)spec.OuterJacketRadius_mm;
        if (rOuter <= rInner + 0.1f) return;   // jacket too thin

        float neckRadius   = (float)(spec.NeckDiameter_mm   * 0.5);
        float cavityRadius = (float)(spec.CavityDiameter_mm * 0.5);

        // Cavity inner radial position: starts just past the inner
        // liner + neck length. Cap at outer jacket so the cavity
        // never punches through the outer wall.
        float neckOuterR  = rInner + (float)spec.NeckLength_mm;
        // Cavity span: NeckOuterR + (epsilon) to (NeckOuterR + CavityLength).
        // Clamp to jacket extent.
        float cavityNearR = neckOuterR + 0.1f;
        float cavityFarR  = MathF.Min(cavityNearR + (float)spec.CavityLength_mm, rOuter - 0.1f);
        if (cavityFarR <= cavityNearR) return;
        float cavityLen   = cavityFarR - cavityNearR;

        var ops = new System.Collections.Generic.List<IImplicit>(spec.Count * 2);
        for (int i = 0; i < spec.Count; i++)
        {
            double theta = 2.0 * Math.PI * i / spec.Count;
            float dirY = MathF.Cos((float)theta);
            float dirZ = MathF.Sin((float)theta);

            // Neck: cylinder centred on the chamber axis, axis = (dirY, dirZ),
            // running from rInner outward to rInner + NeckLength.
            // CylinderImplicit takes (start, direction, radius, length);
            // start = inner liner intersection at this azimuth. The neck
            // extends 1 mm into the bore for a clean BoolSubtract through
            // the inner liner (mirrors sensor-boss pattern at
            // ChamberVoxelBuilder.cs:930 where the bore extends 0.5 mm into
            // the chamber to ensure the subtraction punches through).
            float neckStartR = rInner - 1f;
            float neckStartY = dirY * neckStartR;
            float neckStartZ = dirZ * neckStartR;
            float neckLen    = (float)spec.NeckLength_mm + 1f;
            ops.Add(new CylinderImplicit(
                start:     new Vector3(xStation, neckStartY, neckStartZ),
                direction: new Vector3(0, dirY, dirZ),
                radius:    neckRadius,
                length:    neckLen));

            // Cavity: wider cylinder buried in the jacket. Start one
            // step past the neck's end to avoid double-counting in the
            // union envelope; the union takes care of merging the small
            // overlap into one connected void.
            float cavityStartY = dirY * cavityNearR;
            float cavityStartZ = dirZ * cavityNearR;
            ops.Add(new CylinderImplicit(
                start:     new Vector3(xStation, cavityStartY, cavityStartZ),
                direction: new Vector3(0, dirY, dirZ),
                radius:    cavityRadius,
                length:    cavityLen));
        }

        // BoolSubtract the unioned implicit set from the chamber outer
        // solid. One voxelize per call instead of N — same Sprint 14 / P13
        // optimisation the existing AddMountingFlangeFull / sensor-boss
        // paths use for bolt circles.
        if (ops.Count > 0)
            shell.BoolSubtractTemp(new UnionImplicit(ops.ToArray()), bounds);
    }

    /// <summary>
    /// Quarter-wave array: per-resonator long radial cylinder drilled
    /// from the chamber bore into the jacket, length = QuarterWaveLength_mm.
    /// One end open (chamber-side), one end closed (terminates inside
    /// the jacket material).
    /// </summary>
    private static void AddQuarterWaveArray(
        Voxels                       shell,
        BBox3                        bounds,
        AcousticDamperGeometrySpec   spec,
        float                        xStation)
    {
        if (spec.QuarterWaveLength_mm <= 0 || spec.QuarterWaveDiameter_mm <= 0)
            return;

        float rInner = (float)spec.InnerRadius_mm;
        float rOuter = (float)spec.OuterJacketRadius_mm;
        if (rOuter <= rInner + 0.1f) return;

        float qwRadius = (float)(spec.QuarterWaveDiameter_mm * 0.5);
        // Clamp the cavity length so it never punches through the
        // outer jacket. A truncated cavity is still a quarter-wave
        // resonator at the tuned f₀ as long as the closed end
        // remains inside jacket material.
        float qwLen = (float)Math.Min(spec.QuarterWaveLength_mm, rOuter - rInner - 0.5);
        if (qwLen <= 1f) return;

        var ops = new System.Collections.Generic.List<IImplicit>(spec.Count);
        for (int i = 0; i < spec.Count; i++)
        {
            double theta = 2.0 * Math.PI * i / spec.Count;
            float dirY = MathF.Cos((float)theta);
            float dirZ = MathF.Sin((float)theta);

            float startR = rInner - 1f;   // 1 mm penetration into bore
            float startY = dirY * startR;
            float startZ = dirZ * startR;
            ops.Add(new CylinderImplicit(
                start:     new Vector3(xStation, startY, startZ),
                direction: new Vector3(0, dirY, dirZ),
                radius:    qwRadius,
                length:    qwLen + 1f));
        }

        if (ops.Count > 0)
            shell.BoolSubtractTemp(new UnionImplicit(ops.ToArray()), bounds);
    }
}
