// SolarCollectorKind.cs — Sprint ST.W1 solar-thermal collector
// discriminator.
//
// Wave-1 ships two cluster topologies:
//
//   FlatPlate — non-concentrating, glass-cover-over-absorber-plate
//               design. T_collector ≤ ~ 100 °C (atmospheric pressure
//               working fluid). Domestic hot-water + low-T process
//               heat market. U_L ~ 5 W/(m²·K), τα ~ 0.75.
//
//   ParabolicTrough — line-focus concentrator with evacuated-tube
//                     receiver. T_collector ∈ [300, 450] °C. Drives
//                     CSP (concentrated solar power) plants (Andasol,
//                     Mojave Solar, Solana). U_L ~ 0.5 W/(m²·K)
//                     (evacuated tube), τα ~ 0.85 (selective coating).
//
// Wave-2+ will add evacuated-tube (flat-plate cousin), Fresnel-lens
// linear, parabolic-dish (point-focus, > 700 °C), and central-receiver
// (heliostat field) topologies.

namespace Voxelforge.SolarThermal;

/// <summary>
/// Sub-classification of solar-thermal collector (Sprint ST.W1).
/// </summary>
internal enum SolarCollectorKind
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>
    /// Flat-plate non-concentrating collector — domestic hot water +
    /// low-T process heat cluster.
    /// </summary>
    FlatPlate = 1,

    /// <summary>
    /// Parabolic-trough line-focus concentrator with evacuated-tube
    /// receiver — utility-scale CSP cluster (Andasol-class).
    /// </summary>
    ParabolicTrough = 2,

    /// <summary>
    /// Evacuated-tube collector (Sprint ST.W2) — non-concentrating but
    /// with vacuum-insulated absorber tubes. Higher F_R · τα than
    /// flat-plate (vacuum cuts convective loss), enabling mid-T
    /// applications (60-200 °C). Commercial domestic / commercial
    /// hot-water cluster + low-T process heat.
    /// </summary>
    EvacuatedTube = 3,
}
