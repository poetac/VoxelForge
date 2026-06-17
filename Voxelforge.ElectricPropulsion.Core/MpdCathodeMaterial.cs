// MpdCathodeMaterial.cs — cathode-tip material discriminator for the MPD
// variant.
//
// Sister to AnodeMaterial / CathodeType / ArcjetElectrodeMaterial in the
// EP pillar. Drives the Hard gate MPD_CATHODE_OVERHEAT (max sustained
// surface temperature).

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Cathode-tip material for the MPD variant. Drives the maximum sustained
/// cathode-tip temperature the structural gate <c>MPD_CATHODE_OVERHEAT</c>
/// enforces.
/// </summary>
public enum MpdCathodeMaterial
{
    /// <summary>
    /// Sentinel — non-MPD kinds default here. The gate falls back to the
    /// most-conservative (lowest-T) limit so unconfigured MPD designs surface
    /// the gate rather than passing silently.
    /// </summary>
    None = 0,

    /// <summary>
    /// Pure tungsten (W). T_max ≈ 3700 K sustained. Standard MPD cathode.
    /// </summary>
    Tungsten = 1,

    /// <summary>
    /// Thoriated tungsten (W-2%ThO₂). T_max ≈ 3200 K sustained — lower than
    /// pure W because thorium oxide grain inclusions limit the operating
    /// envelope, but the work-function reduction (3.6 eV → 2.6 eV) lets the
    /// cathode emit usefully at lower temperatures. The LiLFA cluster
    /// material of choice.
    /// </summary>
    ThoriatedTungsten = 2,

    /// <summary>
    /// Lanthanum hexaboride (LaB₆). T_max ≈ 2200 K sustained. Lower
    /// operating temperature, lower erosion than W; preferred for long-
    /// duration ground-test hardware.
    /// </summary>
    LanthanumHexaboride = 3,
}
