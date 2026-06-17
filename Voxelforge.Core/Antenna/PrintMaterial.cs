// PrintMaterial.cs — Sprint ANT.W6 print-material enum + property
// lookup table for the antenna voxel builders. Each material entry
// carries three properties that drive geometry-feasibility checks:
//
//   MinFeatureDiameter_mm — smallest resolvable detail for the process
//     (wire diameter floor for LPBF/FDM, substrate rib floor for SLA).
//   MaxOverhangAngle_deg  — self-supporting overhang limit (45° for
//     LPBF metal / FDM without supports; 90° for SLA with liquid-support).
//   RelativePermittivity  — ε_r of the base material, used by the
//     ANT.W7 patch-resonance coupling formula. Metal (LPBF-316L) is
//     listed as 1.0 — its substrate is air/vacuum, not applicable for
//     the Bahl-Trivedi formula; the voxel builder uses it only for
//     non-patch topologies.
//
// References:
//   EOS M 290 LPBF process guide — 316L minimum wall 0.3 mm.
//   Markforged Onyx FDM process guide — 0.4 mm minimum wall.
//   Formlabs SLA process guide — 0.1 mm minimum wall (with support).
//   Rogers Corp RT/Duroid 4003C datasheet — ε_r = 3.55 at 10 GHz.

using System;

namespace Voxelforge.Antenna;

/// <summary>Sprint ANT.W6 — 3D-printing material taxonomy for antenna
/// voxel builders.</summary>
internal enum PrintMaterial
{
    /// <summary>LPBF stainless steel 316L. Conductive; no substrate role.
    /// Min feature ≈ 0.3 mm; max overhang 45° (self-supporting melt
    /// pool).</summary>
    Lpbf316L = 0,

    /// <summary>Conductive FDM PLA (e.g., Protopasta Carbon Black).
    /// σ ≈ 100 S/m (adequate for HF/VHF wire antennas). Min layer
    /// ≈ 0.4 mm; max overhang 45°.</summary>
    ConductiveFdmPla = 1,

    /// <summary>SLA standard photopolymer resin (e.g., Formlabs Standard
    /// V4). Low loss at microwave frequencies. Min feature ≈ 0.1 mm
    /// with supports; ε_r ≈ 3.2 at 10 GHz.</summary>
    SlaResinStandard = 2,

    /// <summary>SLA high-frequency substrate resin equivalent to Rogers
    /// 4003C. ε_r = 3.55 ± 0.05 at 10 GHz; tan δ ≈ 0.0027.
    /// Min feature ≈ 0.1 mm; no overhang limit (liquid support).</summary>
    SlaResinRogers = 3,
}

/// <summary>
/// Tabulated physical properties for each <see cref="PrintMaterial"/>
/// used by the ANT.W6/W7 geometry builders and constraint checks.
/// </summary>
internal static class PrintMaterialTable
{
    /// <summary>
    /// Minimum resolvable feature (wire diameter / wall thickness) [mm]
    /// for the selected print material.
    /// </summary>
    internal static double MinFeatureDiameter_mm(PrintMaterial m)
        => m switch
        {
            PrintMaterial.Lpbf316L         => 0.3,
            PrintMaterial.ConductiveFdmPla => 0.4,
            PrintMaterial.SlaResinStandard => 0.1,
            PrintMaterial.SlaResinRogers   => 0.1,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m,
                     "Unknown PrintMaterial.")
        };

    /// <summary>
    /// Maximum self-supporting overhang angle [°] for the material.
    /// SLA values are listed as 90° because the liquid resin acts as
    /// a support medium — there is no practical angular limit.
    /// </summary>
    internal static double MaxOverhangAngle_deg(PrintMaterial m)
        => m switch
        {
            PrintMaterial.Lpbf316L         => 45.0,
            PrintMaterial.ConductiveFdmPla => 45.0,
            PrintMaterial.SlaResinStandard => 90.0,
            PrintMaterial.SlaResinRogers   => 90.0,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m,
                     "Unknown PrintMaterial.")
        };

    /// <summary>
    /// Relative permittivity ε_r [-] of the base material at microwave
    /// frequencies (10 GHz reference). Metal (LPBF-316L) returns 1.0 as
    /// a placeholder — it is only applicable when used as a substrate,
    /// which is not the case for LPBF wire/aperture topologies.
    /// </summary>
    internal static double RelativePermittivity(PrintMaterial m)
        => m switch
        {
            PrintMaterial.Lpbf316L         => 1.0,
            PrintMaterial.ConductiveFdmPla => 5.0,
            PrintMaterial.SlaResinStandard => 3.2,
            PrintMaterial.SlaResinRogers   => 3.55,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m,
                     "Unknown PrintMaterial.")
        };
}
