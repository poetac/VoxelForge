// UraniumEnrichment.cs — Sprint NU.W5 fuel enrichment tier discriminator.
//
// NTR designs span three published enrichment tiers, each with distinct
// regulatory + non-proliferation constraints and distinct achievable
// fission-power-density limits:
//
//   LEU (Low-Enriched Uranium) — < 5 % U-235. Commercial LWR fuel grade.
//                                Achievable NTR power density is low
//                                because the critical mass scales with
//                                enrichment; LEU-fuelled NTRs need very
//                                large cores. Max practical core power
//                                density ~50 MW/m³. NASA-LEU NTR concept
//                                (Patel et al. 2020).
//
//   HALEU (High-Assay LEU) — 5–19.75 % U-235. The "between" tier shipped
//                            by Centrus + others under modern reactor
//                            programs (advanced SMRs, microreactors).
//                            More compact NTR cores feasible; max
//                            ~500 MW/m³. NASA's preferred NTR tier as of
//                            2024 (per NTP-Technology-Development plans).
//
//   HEU (Highly Enriched Uranium) — ≥ 19.75 % U-235 (typically 90 %+ for
//                                  the NERVA-class historical baseline).
//                                  Highest power density (~5 GW/m³ for
//                                  NERVA NRX-A6) but requires
//                                  international-safeguard waivers for
//                                  modern commercial use.
//
// Per-tier limit data lives on <see cref="UraniumEnrichmentTiers"/>; the
// design record carries only the enum discriminator. Wave-1/Wave-2/Wave-3
// designs that leave the field at <see cref="None"/> default to HEU
// behaviour to preserve NERVA-baseline bit-identical outputs.

namespace Voxelforge.Nuclear;

/// <summary>
/// Uranium enrichment tier for the per-pin power-density gate (Sprint
/// NU.W5). Drives the maximum allowable volumetric heat flux that
/// <see cref="Optimization.NuclearGates.Evaluate"/> tolerates before
/// firing the existing <c>NTR_THERMAL_FLUX_EXCEEDED</c> gate.
/// </summary>
public enum UraniumEnrichment
{
    /// <summary>
    /// Sentinel — pre-Sprint-NU.W5 designs default here. Maps to HEU
    /// behaviour (4000 MW/m³ ceiling — matches the prior Wave-1
    /// hard-coded constant). Preserves Wave-1/W2/W3/W4 backwards compat
    /// bit-identically.
    /// </summary>
    None = 0,

    /// <summary>
    /// LEU (Low-Enriched Uranium) — &lt; 5 % U-235. Max practical NTR
    /// core power density ~50 MW/m³. Commercial-fuel-grade.
    /// </summary>
    LEU = 1,

    /// <summary>
    /// HALEU (High-Assay LEU) — 5 % – 19.75 % U-235. Max practical NTR
    /// power density ~500 MW/m³. NASA's preferred modern NTR tier.
    /// </summary>
    HALEU = 2,

    /// <summary>
    /// HEU (Highly Enriched Uranium) — ≥ 19.75 % U-235; historical NERVA
    /// designs were 90 %+. Max NTR power density ~4000 MW/m³ (NERVA
    /// NRX-A6 historical envelope). Requires international-safeguard
    /// waivers for modern commercial use.
    /// </summary>
    HEU = 3,
}

/// <summary>
/// Per-tier maximum-power-density data.
/// </summary>
/// <param name="MaxVolumetricHeatFlux_MWm3">Max sustained reactor power density [MW/m³].</param>
/// <param name="MinU235Fraction">Lower bound of the tier's U-235 mass fraction band.</param>
/// <param name="MaxU235Fraction">Upper bound of the tier's U-235 mass fraction band.</param>
public sealed record UraniumEnrichmentData(
    double MaxVolumetricHeatFlux_MWm3,
    double MinU235Fraction,
    double MaxU235Fraction);

/// <summary>
/// Static registry of per-tier enrichment data.
/// </summary>
public static class UraniumEnrichmentTiers
{
    /// <summary>LEU: U-235 ∈ [0, 5 %], max ~50 MW/m³.</summary>
    public static readonly UraniumEnrichmentData LEU =
        new(MaxVolumetricHeatFlux_MWm3:   50.0,
            MinU235Fraction:               0.0,
            MaxU235Fraction:               0.05);

    /// <summary>HALEU: U-235 ∈ [5 %, 19.75 %], max ~500 MW/m³.</summary>
    public static readonly UraniumEnrichmentData HALEU =
        new(MaxVolumetricHeatFlux_MWm3:  500.0,
            MinU235Fraction:               0.05,
            MaxU235Fraction:               0.1975);

    /// <summary>HEU: U-235 ≥ 19.75 %, max ~4000 MW/m³ (NERVA envelope).</summary>
    public static readonly UraniumEnrichmentData HEU =
        new(MaxVolumetricHeatFlux_MWm3: 4000.0,
            MinU235Fraction:               0.1975,
            MaxU235Fraction:               1.0);

    /// <summary>
    /// Resolve enrichment data, with <see cref="UraniumEnrichment.None"/>
    /// mapping to HEU for backwards compat with Wave-1/W2/W3/W4 designs.
    /// </summary>
    public static UraniumEnrichmentData For(UraniumEnrichment tier) => tier switch
    {
        UraniumEnrichment.LEU   => LEU,
        UraniumEnrichment.HALEU => HALEU,
        UraniumEnrichment.HEU   => HEU,
        _                       => HEU,
    };
}
