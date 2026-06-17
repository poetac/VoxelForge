// ThrustTakeoutAdapterGeometry — voxel feature for a structural test-stand
// thrust-takeout adapter. Closes the Hot-fire readiness Item 6 gap (#260).
//
// Sprint B-2 (2026-04-30). The chamber's mounting flange (the engine-side
// interface) was already built by ChamberVoxelBuilder.AddMountingFlangeFull;
// this adds the aft adapter body that bolts through to the test-stand load
// cell. Mirroring the AddMountingFlangeFull conventions:
//
//   • Build into a single-disc-sized Voxels grid via DiscImplicit, then
//     BoolSubtract the exit bore + bolt circle + umbilical pass-throughs
//     in one kernel dispatch each.
//   • All implicit SDFs allocated as IImplicit instances; UnionImplicit
//     batches the per-bolt and per-pass-through cylinders so we voxelise
//     the union once instead of N times (matches Sprint 14 / P13 perf).
//   • shell.BoolAdd(adapterVox) at the end to union the adapter onto the
//     chamber so the result is one printable monolith.
//
// Geometry layout — adapter body is a cylinder of height H, OD D, with:
//
//        ┌─────── chamber TotalLength ────────┐
//        ┊                                    ┊
//        ┊   ╔════════════════════════════╗   ┊  mounting flange (existing)
//        ┊   ║                            ║   ┊
//        ┊   ╚════════════════════════════╝   ┊  ◄── xStart = TotalLength + flangeThk
//        ┊   ╔════════════════════════════╗   ┊  ◄── adapter body, H mm tall
//        ┊   ║   ●  ●  ●  ●  ●  ●  ●  ●   ║   ┊      bolt circle on aft face
//        ┊   ║                            ║   ┊      umbilical pass-throughs
//        ┊   ║          ▔▔▔▔▔             ║   ┊      drilled radially
//        ┊   ║         (clearance bore)   ║   ┊
//        ┊   ╚════════════════════════════╝   ┊
//
// The exit-clearance bore matches the chamber's nozzle ExitRadius plus a
// 0.5 mm DfM clearance so exhaust passes through the adapter without
// touching the adapter wall — same pattern as AddMountingFlangeFull's
// exit-bore subtraction.
//
// Tests: pure data tests live in Voxelforge.Tests; the voxel-build
// round-trip is verified subprocess-style via Voxelforge.StlExporter
// (CLAUDE.md pitfall #8 — xUnit + PicoGK).

using System;
using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// Specification for one thrust-takeout adapter. All dimensions in mm.
/// Pure data — voxel build is in
/// <see cref="ThrustTakeoutAdapterGeometry.AddAdapterFull"/>.
/// </summary>
public sealed record ThrustTakeoutAdapterSpec(
    /// <summary>Adapter outer diameter (mm). Caller supplies the resolved
    /// value; 0 / negative is treated as "match the chamber's mounting-
    /// flange OD."</summary>
    double OuterDiameter_mm,
    /// <summary>Adapter body height (mm) along the engine axis.</summary>
    double Height_mm,
    /// <summary>Test-stand-side bolt-pattern preset. Drives the bottom-
    /// face bolt circle. Independent of the chamber-side mounting-flange
    /// preset (the adapter top mates to the chamber, the adapter bottom
    /// to the load cell).</summary>
    MountingFlangeStandard MountStandard,
    /// <summary>Number of radial umbilical / instrumentation pass-
    /// throughs. 0 = none.</summary>
    int UmbilicalPassThroughCount,
    /// <summary>Diameter (mm) of each pass-through hole.</summary>
    double UmbilicalPassThroughDiameter_mm,
    /// <summary>Clearance radius (mm) added to the chamber exit radius
    /// to size the adapter's internal bore. 0.5 mm matches the existing
    /// AddMountingFlangeFull DfM clearance.</summary>
    double ExitClearanceRadius_mm = 0.5);

public static class ThrustTakeoutAdapterGeometry
{
    /// <summary>
    /// Build the thrust-takeout adapter onto <paramref name="shell"/>.
    /// Geometry is composed entirely from implicit SDFs (DiscImplicit +
    /// CylinderImplicit) so the BoolAdd / BoolSubtract path stays in
    /// the single-voxelization-per-feature regime.
    ///
    /// Caller responsibility: only call when both the mounting flange
    /// and the adapter are turned on. The mounting flange must already
    /// have been built so the adapter sits flush against its aft face.
    /// </summary>
    /// <param name="shell">Outer-solid Voxels accumulator. The adapter
    /// body is BoolAdded onto this; the caller's chamber + mounting
    /// flange must already be present so the bool union produces a
    /// continuous monolith.</param>
    /// <param name="bounds">Voxelization bounds — the build envelope
    /// must include the adapter's aft extent.</param>
    /// <param name="xStart_mm">Axial position of the adapter top face,
    /// in chamber coordinates. Typically <c>TotalLength + flangeThk</c>
    /// so the adapter top sits flush against the mounting-flange aft
    /// face.</param>
    /// <param name="exitRadius_mm">Chamber nozzle exit radius. The
    /// adapter's internal bore is this plus
    /// <see cref="ThrustTakeoutAdapterSpec.ExitClearanceRadius_mm"/>.</param>
    /// <param name="spec">Adapter geometry parameters.</param>
    public static void AddAdapterFull(
        Voxels                       shell,
        BBox3                        bounds,
        float                        xStart_mm,
        float                        exitRadius_mm,
        ThrustTakeoutAdapterSpec     spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Height_mm <= 0)
            throw new ArgumentException("adapter height must be positive", nameof(spec));
        if (spec.OuterDiameter_mm <= 0)
            throw new ArgumentException("adapter outer diameter must be positive", nameof(spec));

        float thickness = (float)spec.Height_mm;
        float adapterR  = (float)(spec.OuterDiameter_mm * 0.5);
        var   mount     = MountingFlangePresets.SpecFor(spec.MountStandard);

        // 1. Solid disc body. DiscImplicit takes (xStart, thickness, radius)
        //    and yields a finite cylinder spanning x ∈ [xStart, xStart+H]
        //    with r ≤ adapterR.
        var bodyDisc = new DiscImplicit(xStart_mm, thickness, adapterR);
        var adapterVox = LibraryScope.MakeVoxels(bodyDisc, bounds);

        // 2. Internal bore so the nozzle exhaust passes through cleanly.
        //    Extend ±1 mm beyond the disc faces so the boolean
        //    subtraction doesn't leave a sliver from voxel quantisation
        //    at the disc edges (same trick AddMountingFlangeFull uses).
        float boreR = exitRadius_mm + (float)spec.ExitClearanceRadius_mm;
        var exitBore = new CylinderImplicit(
            new Vector3(xStart_mm - 1f, 0, 0),
            new Vector3(1, 0, 0),
            boreR,
            thickness + 2f);
        adapterVox.BoolSubtractTemp(exitBore, bounds);

        // 3. Bottom-face bolt circle for the test-stand load-cell
        //    interface. Same pattern as the mounting flange but on
        //    the adapter's aft face and using the adapter's own preset
        //    — the chamber-side and stand-side bolt circles can differ.
        float boltRad     = (float)(mount.BoltDiameter_mm * 0.5);
        float boltCircleR = adapterR - (float)mount.BoltCircleInset_mm;
        if (boltCircleR > boreR + 3f)
        {
            int n = Math.Max(mount.BoltCount, 2);
            var bolts = new IImplicit[n];
            for (int i = 0; i < n; i++)
            {
                double theta = mount.StartAngle_rad + 2.0 * Math.PI * i / n;
                float yB = boltCircleR * MathF.Cos((float)theta);
                float zB = boltCircleR * MathF.Sin((float)theta);
                // Each bolt hole spans the full adapter height, drilled
                // axially through both faces. Same disposition as the
                // mounting flange's bolt holes (full-thickness clearance).
                bolts[i] = new CylinderImplicit(
                    new Vector3(xStart_mm - 1f, yB, zB),
                    new Vector3(1, 0, 0),
                    boltRad,
                    thickness + 2f);
            }
            adapterVox.BoolSubtractTemp(new UnionImplicit(bolts), bounds);
        }

        // 4. Optional radial umbilical pass-throughs. Distributed at
        //    evenly-spaced azimuths and centred at the mid-height of
        //    the adapter so the holes pass clear of both bolt circles.
        //    Cylinder axis is radial (along Y) since the SDF is
        //    rotationally symmetric and CylinderImplicit's distance
        //    formula is direction-aware.
        if (spec.UmbilicalPassThroughCount > 0
            && spec.UmbilicalPassThroughDiameter_mm > 0
            && adapterR > 1.0f)
        {
            int n = spec.UmbilicalPassThroughCount;
            float passR = (float)(spec.UmbilicalPassThroughDiameter_mm * 0.5);
            // Phase the pass-throughs by half a step relative to the
            // bolt circle so the holes don't intersect bolt material in
            // a typical N=8 + N=4 layout. The half-step also reads
            // visually as deliberate clocking on the rendered STL.
            double phase = mount.StartAngle_rad + Math.PI / Math.Max(n, 1);
            float xMid   = xStart_mm + thickness * 0.5f;
            float passLen = adapterR * 2.5f;   // span clear across the body
            var passes = new IImplicit[n];
            for (int i = 0; i < n; i++)
            {
                double theta = phase + 2.0 * Math.PI * i / n;
                // CylinderImplicit's "start" is the centre of one face
                // of the cylinder; we offset by half the length along
                // the axis direction so the cylinder is centred on the
                // adapter's geometric centre line.
                var axis = new Vector3(0, MathF.Cos((float)theta), MathF.Sin((float)theta));
                var start = new Vector3(xMid, 0, 0) - axis * (passLen * 0.5f);
                passes[i] = new CylinderImplicit(start, axis, passR, passLen);
            }
            adapterVox.BoolSubtractTemp(new UnionImplicit(passes), bounds);
        }

        shell.BoolAdd(adapterVox);
    }

    /// <summary>
    /// Resolve <see cref="ThrustTakeoutAdapterSpec.OuterDiameter_mm"/>'s
    /// "0 = match flange OD" sentinel into the actual diameter the
    /// builder should use. Centralised here so tests can pin the
    /// fallback semantics independent of the voxel build.
    /// </summary>
    public static double ResolveOuterDiameter(
        double designOuterDiameter_mm,
        double mountingFlangeOuterDiameter_mm)
    {
        return designOuterDiameter_mm > 0
            ? designOuterDiameter_mm
            : mountingFlangeOuterDiameter_mm;
    }
}
