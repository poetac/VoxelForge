// MountingFlangePresets.cs — Parent audit §4 (2026-04-22):
//
// Mounting-flange standards library for the nozzle-exit mount surface.
// Parallels `PortStandards` in structure: enum of named presets + spec
// record carrying bolt count, bolt diameter, circle-radius formula, and
// optional start-angle offset (clocked patterns).
//
// Presets:
//   • Generic8Bolt          — today's default (8 bolts, Ø5 mm shank).
//   • MilStd_4Bolt_Small    — 4-bolt pattern, Ø6 mm, wider flange for
//                             simple test-stand adapters.
//   • MilStd_6Bolt_Clocked  — 6-bolt clocked (30° offset) — common for
//                             SSME-style thrust mount adapters.
//   • AsmeB165_Small        — ASME B16.5 Class-150 NPS 1 equivalent (4
//                             bolts Ø8 mm on a ~108 mm bolt circle).
//
// Bolt circle and overall flange radius are computed as functions of the
// nozzle exit radius + jacket thickness so the preset scales sensibly
// across thrust classes. The voxel builder reads the spec and replaces
// its hardcoded (8 bolts, Ø5 mm, flange − 5 mm) block.

namespace Voxelforge.Geometry;

public enum MountingFlangeStandard
{
    Generic8Bolt,
    MilStd_4Bolt_Small,
    MilStd_6Bolt_Clocked,
    AsmeB165_Small,
}

public readonly record struct MountingFlangeSpec(
    MountingFlangeStandard Id,
    string DisplayName,
    int BoltCount,
    double BoltDiameter_mm,
    /// <summary>Minimum extra radial material outside the jacket OD (mm).</summary>
    double FlangeMarginRadius_mm,
    /// <summary>Inset from flange OD to bolt circle (mm).</summary>
    double BoltCircleInset_mm,
    /// <summary>Start-angle offset for clocked patterns (rad). 0 = bolt at +Y.</summary>
    double StartAngle_rad);

public static class MountingFlangePresets
{
    public static readonly Dictionary<MountingFlangeStandard, MountingFlangeSpec> All =
        new()
        {
            [MountingFlangeStandard.Generic8Bolt] = new(
                Id:                   MountingFlangeStandard.Generic8Bolt,
                DisplayName:          "Generic 8-bolt",
                BoltCount:            8,
                BoltDiameter_mm:      5.0,
                FlangeMarginRadius_mm: 8.0,
                BoltCircleInset_mm:    5.0,
                StartAngle_rad:        0.0),

            [MountingFlangeStandard.MilStd_4Bolt_Small] = new(
                Id:                   MountingFlangeStandard.MilStd_4Bolt_Small,
                DisplayName:          "MIL-STD 4-bolt (small)",
                BoltCount:            4,
                BoltDiameter_mm:      6.0,
                FlangeMarginRadius_mm: 12.0,
                BoltCircleInset_mm:    7.0,
                StartAngle_rad:        System.Math.PI / 4),   // bolt at 45°

            [MountingFlangeStandard.MilStd_6Bolt_Clocked] = new(
                Id:                   MountingFlangeStandard.MilStd_6Bolt_Clocked,
                DisplayName:          "MIL-STD 6-bolt (clocked)",
                BoltCount:            6,
                BoltDiameter_mm:      5.0,
                FlangeMarginRadius_mm: 10.0,
                BoltCircleInset_mm:    6.0,
                StartAngle_rad:        System.Math.PI / 6),   // 30° offset

            [MountingFlangeStandard.AsmeB165_Small] = new(
                Id:                   MountingFlangeStandard.AsmeB165_Small,
                DisplayName:          "ASME B16.5 Class-150 (small)",
                BoltCount:            4,
                BoltDiameter_mm:      8.0,
                FlangeMarginRadius_mm: 18.0,
                BoltCircleInset_mm:    9.0,
                StartAngle_rad:        System.Math.PI / 4),
        };

    public static MountingFlangeSpec SpecFor(MountingFlangeStandard s) => All[s];
}
