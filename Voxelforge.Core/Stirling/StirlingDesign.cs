// StirlingDesign.cs — Sprint STR.W1 Stirling-engine design record.

using System;

namespace Voxelforge.Stirling;

/// <summary>
/// Design parameters for a Stirling engine (Sprint STR.W1 scaffold).
/// </summary>
/// <param name="Configuration">Mechanical layout — Alpha / Beta / Gamma.</param>
/// <param name="HotSideTemperature_K">T_hot [K] — heater-head temp.</param>
/// <param name="ColdSideTemperature_K">T_cold [K] — cooler-head temp.</param>
/// <param name="MeanPressure_Pa">P_mean [Pa] — charge-gas mean pressure.</param>
/// <param name="SweptVolume_m3">V_swept [m³] — power-piston swept volume per cycle.</param>
/// <param name="OperatingFrequency_Hz">f [Hz] = N/60 — cycles per second.</param>
/// <param name="SecondLawEfficiency">η_2nd ∈ (0, 1] [-] — Schmidt
/// derating vs Carnot. Real Stirling engines cluster 0.40-0.65
/// (regenerator losses, mechanical friction, gas-leakage past seals).</param>
internal sealed record StirlingDesign(
    StirlingConfiguration Configuration,
    double HotSideTemperature_K,
    double ColdSideTemperature_K,
    double MeanPressure_Pa,
    double SweptVolume_m3,
    double OperatingFrequency_Hz,
    double SecondLawEfficiency)
{
    /// <summary>
    /// Sprint STR.W2. Working-fluid choice. Defaults to Helium for
    /// backwards-compat with STR.W1 (which hard-coded He behaviour).
    /// </summary>
    public StirlingWorkingFluid WorkingFluid { get; init; } = StirlingWorkingFluid.Helium;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Configuration == StirlingConfiguration.None)
            throw new ArgumentException(
                "Configuration must be set (None sentinel is reserved).",
                nameof(Configuration));
        if (HotSideTemperature_K <= 0)
            throw new ArgumentException("HotSideTemperature_K must be > 0.",
                nameof(HotSideTemperature_K));
        if (ColdSideTemperature_K <= 0)
            throw new ArgumentException("ColdSideTemperature_K must be > 0.",
                nameof(ColdSideTemperature_K));
        if (HotSideTemperature_K <= ColdSideTemperature_K)
            throw new ArgumentException(
                $"HotSideTemperature_K ({HotSideTemperature_K:F1}) must exceed "
              + $"ColdSideTemperature_K ({ColdSideTemperature_K:F1}); Stirling cycle "
              + "requires a thermal gradient.",
                nameof(HotSideTemperature_K));
        if (MeanPressure_Pa <= 0)
            throw new ArgumentException("MeanPressure_Pa must be > 0.",
                nameof(MeanPressure_Pa));
        if (SweptVolume_m3 <= 0)
            throw new ArgumentException("SweptVolume_m3 must be > 0.",
                nameof(SweptVolume_m3));
        if (OperatingFrequency_Hz <= 0)
            throw new ArgumentException("OperatingFrequency_Hz must be > 0.",
                nameof(OperatingFrequency_Hz));
        if (SecondLawEfficiency <= 0 || SecondLawEfficiency > 1.0)
            throw new ArgumentException(
                "SecondLawEfficiency must be in (0, 1].",
                nameof(SecondLawEfficiency));
    }
}
