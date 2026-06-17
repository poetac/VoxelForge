// RefrigerationDesign.cs — Sprint RFG.W1 design record.

using System;

namespace Voxelforge.Refrigeration;

/// <summary>
/// Design parameters for a vapor-compression refrigeration / heat-pump
/// cycle (Sprint RFG.W1 scaffold). Standalone — does not integrate
/// with the <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Mode">Cycle direction — Cooling or Heating.</param>
/// <param name="Refrigerant">Working fluid.</param>
/// <param name="ColdReservoirTemperature_K">T_cold [K] — the evaporator-
/// side reservoir (the room being cooled / the outdoor air in heating
/// mode).</param>
/// <param name="HotReservoirTemperature_K">T_hot [K] — the condenser-
/// side reservoir (outdoor air in cooling / the room being heated).</param>
/// <param name="CompressorPowerInput_W">W_compressor [W] — electrical
/// input at the compressor shaft. Drives Q_cold and Q_hot via the COP.</param>
internal sealed record RefrigerationDesign(
    RefrigerationMode Mode,
    Refrigerant Refrigerant,
    double ColdReservoirTemperature_K,
    double HotReservoirTemperature_K,
    double CompressorPowerInput_W)
{
    /// <summary>
    /// Sprint RFG.W2. Subcooling depth at the condenser outlet [K].
    /// ΔT_sub &gt; 0 → liquid is cooled below saturation, which boosts
    /// COP by 2-4 % per 5 K of subcooling (vapor-compression cycle
    /// state-point analysis). Defaults to 0 → bit-identical RFG.W1.
    /// </summary>
    public double SubcoolingDepth_K { get; init; } = 0.0;

    /// <summary>
    /// Sprint RFG.W2. Superheat at the evaporator outlet [K]. Required
    /// to prevent liquid carryover into the compressor. Adds compressor
    /// work without boosting cooling capacity → reduces COP by ~ 1 %
    /// per 5 K of superheat. Defaults to 0 → bit-identical RFG.W1.
    /// </summary>
    public double SuperheatDepth_K { get; init; } = 0.0;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Mode == RefrigerationMode.None)
            throw new ArgumentException(
                "Mode must be set (None sentinel is reserved).", nameof(Mode));
        if (Refrigerant == Refrigerant.None)
            throw new ArgumentException(
                "Refrigerant must be set (None sentinel is reserved).", nameof(Refrigerant));
        if (ColdReservoirTemperature_K <= 0)
            throw new ArgumentException("ColdReservoirTemperature_K must be > 0.",
                nameof(ColdReservoirTemperature_K));
        if (HotReservoirTemperature_K <= 0)
            throw new ArgumentException("HotReservoirTemperature_K must be > 0.",
                nameof(HotReservoirTemperature_K));
        if (HotReservoirTemperature_K <= ColdReservoirTemperature_K)
            throw new ArgumentException(
                $"HotReservoirTemperature_K ({HotReservoirTemperature_K:F1}) must exceed "
              + $"ColdReservoirTemperature_K ({ColdReservoirTemperature_K:F1}) — refrigeration "
              + "requires a thermal gradient to pump heat against.",
                nameof(HotReservoirTemperature_K));
        if (CompressorPowerInput_W <= 0)
            throw new ArgumentException("CompressorPowerInput_W must be > 0.",
                nameof(CompressorPowerInput_W));
        if (SubcoolingDepth_K < 0)
            throw new ArgumentException("SubcoolingDepth_K must be ≥ 0.",
                nameof(SubcoolingDepth_K));
        if (SuperheatDepth_K < 0)
            throw new ArgumentException("SuperheatDepth_K must be ≥ 0.",
                nameof(SuperheatDepth_K));
    }
}
