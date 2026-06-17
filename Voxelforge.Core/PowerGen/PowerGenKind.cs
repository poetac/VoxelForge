// PowerGenKind.cs — Sprint PG.W1 power-generation discriminator.
//
// Wave-1 ships the PEM (proton-exchange-membrane) fuel cell stack — the
// classroom-baseline electrochemical generator. Wave-2+ will add SOFC
// (solid-oxide), reciprocating ICE (Diesel/Otto/HCCI), and lithium
// battery packs. The enum mirrors NuclearKind / MarineKind discrimination
// so the future PowerGen IEngine dispatcher keys on this same surface.
//
// PEM cluster:
//   - Toyota Mirai 2nd-gen single-stack ~ 128 kW continuous, 330 cells.
//   - Active area ~ 200 cm² / cell.
//   - Operational current density ~ 1.0 A/cm² at V_cell ~ 0.7 V.
//   - Stack T ~ 80 °C; H₂ + air at ~ 2.5 bar.

namespace Voxelforge.PowerGen;

/// <summary>
/// Sub-classification of power generator within the power-generation
/// pillar (Sprint PG.W1 scaffold). Wave-1 ships PemFuelCell only;
/// SolidOxideFuelCell + ReciprocatingIce + LithiumPack reserved for
/// future waves.
/// </summary>
internal enum PowerGenKind
{
    /// <summary>Degenerate sentinel — not a valid design kind.</summary>
    None = 0,

    /// <summary>
    /// Proton-exchange-membrane (PEM) hydrogen fuel cell stack.
    /// Wave-1 baseline — Toyota Mirai / Ballard MK-class cluster.
    /// Operates at ~ 80 °C; ~ 0.7 V/cell at ~ 1.0 A/cm² nominal.
    /// </summary>
    PemFuelCell = 1,
}
