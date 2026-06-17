// PhotovoltaicCellType.cs — Sprint PV.W1 PV cell technology discriminator.
//
// Wave-1 ships the two dominant commercial silicon technologies. Wave-2+
// will add multi-junction III-V (GaAs, GaInP/GaInAs/Ge) for space + CPV,
// thin-film (CdTe, CIGS) for utility-scale, and perovskite-Si tandem
// (the emerging high-efficiency cluster).

namespace Voxelforge.Photovoltaic;

/// <summary>
/// Photovoltaic cell technology for the PV pillar (Sprint PV.W1).
/// </summary>
internal enum PhotovoltaicCellType
{
    /// <summary>Degenerate sentinel — not a valid design choice.</summary>
    None = 0,

    /// <summary>
    /// Monocrystalline silicon (mono-Si). High-efficiency commercial
    /// rooftop / utility cluster: 20-22 % module efficiency. SunPower
    /// Maxeon X-class baseline.
    /// </summary>
    Monocrystalline = 1,

    /// <summary>
    /// Polycrystalline silicon (poly-Si). Lower-cost commercial cluster:
    /// 15-18 % module efficiency. Older utility-class anchor.
    /// </summary>
    Polycrystalline = 2,
}
