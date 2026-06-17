// FlywheelDesign.cs — Sprint FW.W1 flywheel-rotor design record.

using System;

namespace Voxelforge.Flywheel;

/// <summary>
/// Design parameters for a flywheel energy-storage rotor (Sprint FW.W1
/// scaffold).
/// </summary>
/// <param name="Shape">Rotor topology.</param>
/// <param name="Material">Rotor material.</param>
/// <param name="OuterRadius_m">R [m] — outer radius of the rotor.</param>
/// <param name="Mass_kg">m [kg] — total rotor mass.</param>
/// <param name="RotationSpeed_rpm">N [rpm] — design operating speed.</param>
internal sealed record FlywheelDesign(
    FlywheelShape Shape,
    FlywheelMaterial Material,
    double OuterRadius_m,
    double Mass_kg,
    double RotationSpeed_rpm)
{
    // ── Wave-2 fields (Sprint FW.W2) ────────────────────────────────────

    /// <summary>
    /// Sprint FW.W2. Bearing-system technology. Drives the self-discharge
    /// rate via the parasitic-drag torque. Defaults to Mechanical for
    /// backwards-compat with FW.W1 (which assumed Mechanical without
    /// reporting it).
    /// </summary>
    public BearingType Bearing { get; init; } = BearingType.Mechanical;

    /// <summary>
    /// Sprint FW.W2. State-of-charge ∈ [0, 1] [-]. Drives the
    /// instantaneous-stored-energy fraction E/E_max via the textbook
    /// E(SoC) = E_max · SoC² scaling (a SoC of 0.5 stores 25 % of E_max
    /// because E ∝ ω² and ω ∝ √SoC at constant mass + geometry).
    /// Defaults to 1.0 (fully charged) for FW.W1 bit-identity.
    /// </summary>
    public double StateOfCharge { get; init; } = 1.0;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">
    /// When <see cref="Shape"/> or <see cref="Material"/> is the reserved
    /// <c>None</c> sentinel (categorical failure).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When any numeric field is NaN, non-positive (radius, mass, speed),
    /// or out of range (<see cref="StateOfCharge"/> must be in [0, 1]).
    /// </exception>
    public void ValidateSelf()
    {
        if (Shape == FlywheelShape.None)
            throw new ArgumentException(
                "Shape must be set (None sentinel is reserved).", nameof(Shape));
        if (Material == FlywheelMaterial.None)
            throw new ArgumentException(
                "Material must be set (None sentinel is reserved).", nameof(Material));
        if (double.IsNaN(OuterRadius_m) || OuterRadius_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(OuterRadius_m),
                $"OuterRadius_m={OuterRadius_m:F4} must be > 0.");
        if (double.IsNaN(Mass_kg) || Mass_kg <= 0)
            throw new ArgumentOutOfRangeException(nameof(Mass_kg),
                $"Mass_kg={Mass_kg:F3} must be > 0.");
        if (double.IsNaN(RotationSpeed_rpm) || RotationSpeed_rpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(RotationSpeed_rpm),
                $"RotationSpeed_rpm={RotationSpeed_rpm:F1} must be > 0.");
        if (double.IsNaN(StateOfCharge) || StateOfCharge < 0.0 || StateOfCharge > 1.0)
            throw new ArgumentOutOfRangeException(nameof(StateOfCharge),
                $"StateOfCharge={StateOfCharge:F3} must be in [0, 1].");
    }
}
