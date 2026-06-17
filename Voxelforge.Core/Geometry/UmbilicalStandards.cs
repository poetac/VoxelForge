// UmbilicalStandards.cs — Ground-side quick-disconnect / umbilical
// interface standards.
//
// Parallels `PortStandards`, `SensorBossPresets`, `MountingFlangePresets`
// — an enum of named presets + a spec record + a lookup table. The
// spec carries face OD, bolt pattern, seal-groove geometry, and a
// nominal ΔP at rated flow so the pressure stackup can charge a
// representative loss for each interface.
//
// MVP scope:
//   • Enum entries cover one example from each of the three common
//     families: MS33656 (AN flare), generic cryo QD, GSE pressurant.
//     Real flight hardware picks specific part numbers — the generator
//     only needs the face geometry + nominal ΔP.
//   • Voxel geometry is deferred: the voxel builder currently does not
//     draw the seal groove or bolt circle. The spec is loaded + shown
//     in the report and used in the pressure stackup. A follow-on PR
//     will add `UmbilicalInterfaceImplicit` and wire it into the voxel
//     flange step.
//
// Adding a preset: drop a new entry into the `All` dictionary below.
// No other file needs to change.

namespace Voxelforge.Geometry;

public enum UmbilicalStandard
{
    /// <summary>No umbilical — the generator does not budget any ΔP for it.</summary>
    None = 0,
    /// <summary>AN fitting / MS33656 -06 (3/8 tube). Small-flow instrumentation or GSE pressurant.</summary>
    AN_MS33656_06,
    /// <summary>AN fitting / MS33656 -08 (1/2 tube). Common propellant-line fitting on sub-kN hardware.</summary>
    AN_MS33656_08,
    /// <summary>Cryogenic quick-disconnect, 1/2" nominal. Higher ΔP than AN but dis/reconnect without tools.</summary>
    Cryo_QD_Half_Inch,
    /// <summary>Cryogenic quick-disconnect, 3/4" nominal. Main propellant QD for larger hardware.</summary>
    Cryo_QD_Three_Quarter,
    /// <summary>AN-04 GSE pressurant fitting. Low flow, very low ΔP.</summary>
    Pressurant_MS33649_04,
}

/// <summary>
/// Specification for one umbilical interface preset. ΔP coefficient K
/// feeds the pressure stackup: ΔP = K · ½·ρ·v² at the throat area.
/// </summary>
public readonly record struct UmbilicalSpec(
    UmbilicalStandard Id,
    string DisplayName,
    double FaceOuterDiameter_mm,
    double BoreInnerDiameter_mm,
    double SealGrooveDepth_mm,
    double LossCoefficientK);

public static class UmbilicalStandards
{
    public static readonly System.Collections.Generic.Dictionary<UmbilicalStandard, UmbilicalSpec> All =
        new()
        {
            [UmbilicalStandard.None] = new(
                UmbilicalStandard.None, "(none)",
                FaceOuterDiameter_mm: 0, BoreInnerDiameter_mm: 0,
                SealGrooveDepth_mm: 0, LossCoefficientK: 0.0),

            [UmbilicalStandard.AN_MS33656_06] = new(
                UmbilicalStandard.AN_MS33656_06, "AN MS33656-06 (3/8 tube)",
                FaceOuterDiameter_mm: 22.0, BoreInnerDiameter_mm: 8.0,
                SealGrooveDepth_mm: 1.5, LossCoefficientK: 1.2),

            [UmbilicalStandard.AN_MS33656_08] = new(
                UmbilicalStandard.AN_MS33656_08, "AN MS33656-08 (1/2 tube)",
                FaceOuterDiameter_mm: 28.0, BoreInnerDiameter_mm: 10.9,
                SealGrooveDepth_mm: 1.5, LossCoefficientK: 1.0),

            [UmbilicalStandard.Cryo_QD_Half_Inch] = new(
                UmbilicalStandard.Cryo_QD_Half_Inch, "Cryo QD 1/2\"",
                FaceOuterDiameter_mm: 45.0, BoreInnerDiameter_mm: 12.0,
                SealGrooveDepth_mm: 2.0, LossCoefficientK: 2.5),

            [UmbilicalStandard.Cryo_QD_Three_Quarter] = new(
                UmbilicalStandard.Cryo_QD_Three_Quarter, "Cryo QD 3/4\"",
                FaceOuterDiameter_mm: 55.0, BoreInnerDiameter_mm: 18.0,
                SealGrooveDepth_mm: 2.0, LossCoefficientK: 2.0),

            [UmbilicalStandard.Pressurant_MS33649_04] = new(
                UmbilicalStandard.Pressurant_MS33649_04, "Pressurant MS33649-04",
                FaceOuterDiameter_mm: 14.0, BoreInnerDiameter_mm: 5.0,
                SealGrooveDepth_mm: 1.0, LossCoefficientK: 1.5),
        };

    public static UmbilicalSpec SpecFor(UmbilicalStandard s) => All[s];

    /// <summary>
    /// Return the nominal pressure drop across this umbilical at the
    /// given mass-flow and density. K · ½·ρ·v² on the bore throat.
    /// None → 0.
    /// </summary>
    public static double NominalDeltaP_Pa(
        UmbilicalStandard standard, double massFlow_kgs, double density_kgm3)
    {
        var spec = All[standard];
        if (spec.LossCoefficientK <= 0 || spec.BoreInnerDiameter_mm <= 0) return 0;
        double dia_m = spec.BoreInnerDiameter_mm * 1e-3;
        double A = System.Math.PI * dia_m * dia_m / 4.0;
        double rho = System.Math.Max(density_kgm3, 1e-3);
        double v = massFlow_kgs / (rho * A);
        return spec.LossCoefficientK * 0.5 * rho * v * v;
    }
}
