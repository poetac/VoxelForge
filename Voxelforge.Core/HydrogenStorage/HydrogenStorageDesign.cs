// HydrogenStorageDesign.cs — Sprint H2T.W1 hydrogen-storage tank
// design record.
//
// Sized to bracket Toyota Mirai 700 bar Type-IV tanks (~ 122 L total
// capacity, ~ 5 kg H₂ stored) and a cryo LH₂ comparison.

using System;

namespace Voxelforge.HydrogenStorage;

/// <summary>
/// Design parameters for a hydrogen-storage tank (Sprint H2T.W1
/// scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Storage mode (compressed gas vs cryogenic liquid).</param>
/// <param name="InternalVolume_m3">Tank internal volume V [m³].</param>
/// <param name="OperatingPressure_bar">Storage pressure [bar]. Drives
/// gas density via real-gas Z(P, T) — used only for CompressedGas.
/// Cryogenic tanks operate near 1 atm and ignore this field.</param>
/// <param name="OperatingTemperature_K">Storage temperature [K]. 298.15
/// (25 °C) for compressed; 20.3 K for LH₂.</param>
/// <param name="DryMass_kg">Tank dry mass (composite + liner + valves +
/// MLI for cryo) [kg]. Drives gravimetric efficiency m_H₂ / (m_H₂ + m_dry).</param>
/// <param name="HeatLeakRate_W">Steady-state heat leak through MLI [W].
/// Drives continuous boil-off in LiquidCryogenic mode. Ignored by
/// CompressedGas mode.</param>
internal sealed record HydrogenStorageDesign(
    HydrogenStorageKind Kind,
    double InternalVolume_m3,
    double OperatingPressure_bar,
    double OperatingTemperature_K,
    double DryMass_kg,
    double HeatLeakRate_W = 0.0)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == HydrogenStorageKind.None)
            throw new ArgumentException(
                "Kind must be set (None sentinel is reserved).", nameof(Kind));
        if (InternalVolume_m3 <= 0)
            throw new ArgumentException("InternalVolume_m3 must be > 0.",
                nameof(InternalVolume_m3));
        if (OperatingTemperature_K <= 0)
            throw new ArgumentException("OperatingTemperature_K must be > 0.",
                nameof(OperatingTemperature_K));
        if (DryMass_kg < 0)
            throw new ArgumentException("DryMass_kg must be ≥ 0.", nameof(DryMass_kg));
        if (HeatLeakRate_W < 0)
            throw new ArgumentException("HeatLeakRate_W must be ≥ 0.", nameof(HeatLeakRate_W));
        if (Kind == HydrogenStorageKind.CompressedGas && OperatingPressure_bar <= 0)
            throw new ArgumentException(
                "OperatingPressure_bar must be > 0 for CompressedGas storage.",
                nameof(OperatingPressure_bar));
    }
}
