// PressureVesselDesign.cs — Sprint TANK.W1 cylindrical pressure-vessel
// design record.
//
// Sized to bracket Falcon 9 stage-1 LOX tank (3.66 m OD, 26 m long,
// 4130 stainless monocoque, MEOP 3 bar) and the Toyota Mirai 700-bar
// CF-composite H₂ tank (0.30 m OD, 0.85 m long).

using System;

namespace Voxelforge.Tankage;

/// <summary>
/// Design parameters for a cylindrical pressure vessel
/// (Sprint TANK.W1 scaffold). Thin-wall theory (R/t > 10); thick-wall
/// Lamé physics deferred to TANK.W2.
/// </summary>
/// <param name="ShellType">Shell-material construction.</param>
/// <param name="InternalRadius_m">R [m] — inner radius.</param>
/// <param name="ShellLength_m">L [m] — cylindrical-section length.</param>
/// <param name="WallThickness_m">t [m] — uniform shell thickness.</param>
/// <param name="OperatingPressure_Pa">MEOP [Pa] — maximum expected
/// operating pressure (the regulatory design point).</param>
/// <param name="HasHemisphericalEndCaps">If true, accounts for two
/// hemispherical end caps of the same shell material + thickness in the
/// total mass calculation; if false, the design is cylinder-only.</param>
internal sealed record PressureVesselDesign(
    TankShellType ShellType,
    double InternalRadius_m,
    double ShellLength_m,
    double WallThickness_m,
    double OperatingPressure_Pa,
    bool   HasHemisphericalEndCaps = true)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (ShellType == TankShellType.None)
            throw new ArgumentException(
                "ShellType must be set (None sentinel is reserved).", nameof(ShellType));
        if (InternalRadius_m <= 0)
            throw new ArgumentException("InternalRadius_m must be > 0.",
                nameof(InternalRadius_m));
        if (ShellLength_m <= 0)
            throw new ArgumentException("ShellLength_m must be > 0.",
                nameof(ShellLength_m));
        if (WallThickness_m <= 0)
            throw new ArgumentException("WallThickness_m must be > 0.",
                nameof(WallThickness_m));
        if (OperatingPressure_Pa <= 0)
            throw new ArgumentException("OperatingPressure_Pa must be > 0.",
                nameof(OperatingPressure_Pa));
        if (InternalRadius_m / WallThickness_m < 10.0)
            throw new ArgumentException(
                $"R/t = {InternalRadius_m / WallThickness_m:F2} is below the thin-wall "
              + "validity envelope R/t ≥ 10. Use thick-wall Lamé physics (TANK.W2).",
                nameof(WallThickness_m));
    }
}
