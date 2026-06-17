// SensorBossPresets.cs — TIER A.4 (2026-04-21):
//
// Instrumentation ports for cold-flow and hot-fire testing.
//
// Each boss is a radial hole drilled through the jacket at a
// user-specified (axialFraction, azimuth_deg) with a bore diameter +
// boss OD chosen to match a standard probe type. The voxel builder
// drills them through the outer jacket using the existing CylinderImplicit
// subtractive path.
//
// Scope (MVP):
//   • Radial holes only — no axial taps.
//   • The bore stops 0.5 mm short of the coolant channel so probes
//     don't protrude into the regen path (conservative). In practice
//     most users then countersink the boss manually in CAM.
//   • No automatic thread cutting inside the bore; the existing
//     threaded-port preset library should be used when threaded mating
//     is required.
//   • No clash detection with cooling channels — users place bosses
//     visually in the STL.

namespace Voxelforge.Geometry;

public enum SensorBossType
{
    /// <summary>1/8" NPT thermowell pocket, good for Type-K thermocouples.</summary>
    Thermocouple_1_8_NPT,
    /// <summary>M5 static-pressure tap. Compatible with sub-miniature pressure transducers.</summary>
    Pressure_M5,
    /// <summary>G 1/16 BSPP static tap for low-range hydrostatic probes.</summary>
    StaticTap_G_1_16,
}

/// <summary>
/// One instrumentation boss on the chamber. Radial bore at angular position
/// <see cref="AzimuthDeg"/> and axial position <see cref="AxialFraction"/>
/// (0 = injector face, 1 = nozzle exit).
/// </summary>
public readonly record struct SensorBoss(
    double AxialFraction,
    double AzimuthDeg,
    SensorBossType Type);

public readonly record struct SensorBossSpec(
    SensorBossType Type,
    double BoreDiameter_mm,
    double BossOuterDiameter_mm,
    double BossHeight_mm,
    string DisplayName);

public static class SensorBossPresets
{
    public static readonly Dictionary<SensorBossType, SensorBossSpec> All =
        new()
        {
            [SensorBossType.Thermocouple_1_8_NPT] = new SensorBossSpec(
                SensorBossType.Thermocouple_1_8_NPT,
                BoreDiameter_mm: 3.2,
                BossOuterDiameter_mm: 9.0,
                BossHeight_mm: 6.0,
                DisplayName: "Thermocouple (1/8 NPT)"),

            [SensorBossType.Pressure_M5] = new SensorBossSpec(
                SensorBossType.Pressure_M5,
                BoreDiameter_mm: 2.5,
                BossOuterDiameter_mm: 7.0,
                BossHeight_mm: 4.0,
                DisplayName: "Pressure tap (M5)"),

            [SensorBossType.StaticTap_G_1_16] = new SensorBossSpec(
                SensorBossType.StaticTap_G_1_16,
                BoreDiameter_mm: 2.0,
                BossOuterDiameter_mm: 8.0,
                BossHeight_mm: 5.0,
                DisplayName: "Static tap (G 1/16)"),
        };

    public static SensorBossSpec SpecFor(SensorBossType t) => All[t];
}
