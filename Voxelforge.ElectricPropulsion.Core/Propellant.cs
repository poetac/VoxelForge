// Propellant.cs — propellant choice for electric-propulsion engines.
//
// Distinct enum from the rocket-side `PropellantPair` because EP has only
// a single propellant stream (no oxidiser — the "fuel" is the working
// fluid heated by electrical input). Wave-1 covers four propellants
// commonly used in flown resistojets:
//
//   NH3              — ammonia (NASA-TP-2382 NASA Lewis ammonia resistojet).
//   N2H4Decomposed   — hydrazine catalyst products (MR-501-series Aerojet).
//   H2               — gaseous hydrogen (high Isp, low density; NASA Lewis
//                      hydrogen resistojet test articles).
//   H2O              — water vapor (low Isp / high density; CubeSat-class
//                      green-propellant resistojet research).
//
// Wave-2 (HET) adds:
//   Xenon            — Xe, monatomic noble gas; canonical HET propellant
//                      (BPT-4000, SPT-100, PPS-1350). MW = 131.293 g/mol;
//                      γ = 5/3 (monatomic ideal, T-independent in resistojet
//                      regime). HET physics consumes MW only on the hot
//                      path (ion velocity v_i = √(2·e·V_d/m_xe)); γ / cp
//                      are parked for future arcjet electrothermal use.

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Propellant choice for an electric-propulsion engine. Wave-1 covers
/// the four propellants exercised by flown resistojets in open-literature
/// validation fixtures.
/// </summary>
/// <remarks>
/// The pre-catalyst hydrazine cracking step (Shell-405 catalyst bed) is
/// not modeled in this pillar — see pillar spec §3 + §5.5. The
/// <see cref="N2H4Decomposed"/> entry represents the post-catalyst
/// product gas (NH₃ / N₂ / H₂ mixture) ready for electrothermal
/// superheat.
/// </remarks>
public enum Propellant
{
    /// <summary>
    /// Ammonia (gaseous NH₃). MW ≈ 17.03 g/mol; storable;
    /// dissociation onset ≈ 1100 K (gate
    /// <c>RESISTOJET_PROPELLANT_DECOMPOSITION</c>).
    /// </summary>
    NH3 = 0,

    /// <summary>
    /// Hydrazine catalyst products: 2 N₂H₄ → 2 NH₃ + N₂ + H₂ (Shell-405).
    /// Effective MW ≈ 13.0 g/mol post-catalyst at 900 K inlet.
    /// Decomposition limit ≈ 1400 K (further NH₃ cracking).
    /// </summary>
    N2H4Decomposed = 1,

    /// <summary>
    /// Hydrogen (gaseous H₂). MW = 2.016 g/mol; high Isp regime;
    /// dissociation onset ≈ 3500 K.
    /// </summary>
    H2 = 2,

    /// <summary>
    /// Water vapor (H₂O gas). MW = 18.015 g/mol; low Isp / high density;
    /// dissociation onset ≈ 2700 K.
    /// </summary>
    H2O = 3,

    /// <summary>
    /// Xenon (gaseous Xe; monatomic noble gas). MW = 131.293 g/mol;
    /// γ = 5/3 (T-independent). Wave-2 HET propellant — canonical
    /// choice for BPT-4000 / SPT-100 / PPS-1350. First ionisation
    /// energy 12.13 eV (relevant for HET Bohm-sheath physics, not
    /// resistojet).
    /// </summary>
    Xenon = 4,
}
