// ThermoelectricGeneratorDesign.cs — Sprint TEG.W1 design record.
//
// Sized to bracket the GPHS-RTG (Cassini / Galileo / New Horizons),
// MMRTG (Curiosity / Perseverance), and ground-based Bi₂Te₃ waste-
// heat-recovery clusters. Real RTGs use Pu-238 oxide as the heat
// source; this scaffold treats the heat input as a free parameter
// (Q_hot) so the same solver covers RTG / waste-heat / solar-thermal
// concentrator-fed TEGs equally.

using System;

namespace Voxelforge.Thermoelectric;

/// <summary>
/// Design parameters for a thermoelectric generator (Sprint TEG.W1).
/// Standalone — does not integrate with the <c>IEngine&lt;,,&gt;</c>
/// stack yet.
/// </summary>
/// <param name="Material">TEG material — drives ZT.</param>
/// <param name="HotSideTemperature_K">T_hot [K].</param>
/// <param name="ColdSideTemperature_K">T_cold [K] — the radiator-side
/// rejection temperature.</param>
/// <param name="HotSideHeatInput_W">Q_hot [W] — heat flux into the hot
/// face. For an RTG this is the Pu-238 thermal power.</param>
internal sealed record ThermoelectricGeneratorDesign(
    ThermoelectricMaterial Material,
    double HotSideTemperature_K,
    double ColdSideTemperature_K,
    double HotSideHeatInput_W)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Material == ThermoelectricMaterial.None)
            throw new ArgumentException(
                "Material must be set (None sentinel is reserved).", nameof(Material));
        if (HotSideTemperature_K <= 0)
            throw new ArgumentException("HotSideTemperature_K must be > 0.",
                nameof(HotSideTemperature_K));
        if (ColdSideTemperature_K <= 0)
            throw new ArgumentException("ColdSideTemperature_K must be > 0.",
                nameof(ColdSideTemperature_K));
        if (HotSideTemperature_K <= ColdSideTemperature_K)
            throw new ArgumentException(
                $"HotSideTemperature_K ({HotSideTemperature_K:F1}) must exceed "
              + $"ColdSideTemperature_K ({ColdSideTemperature_K:F1}); otherwise no "
              + "thermal gradient drives the Seebeck EMF.",
                nameof(HotSideTemperature_K));
        if (HotSideHeatInput_W <= 0)
            throw new ArgumentException("HotSideHeatInput_W must be > 0.",
                nameof(HotSideHeatInput_W));
    }
}
