// HybridFuel.cs — Sprint R.W2 hybrid-rocket fuel discriminator.
//
// Hybrid rockets store the fuel as a solid grain and feed liquid (or
// gaseous) oxidiser through the port. Wave-1 covers LOX/HTPB — the
// classroom hybrid baseline (Stanford SPIRIT and similar). Wave-2+ will
// add paraffin (Karabeyoglu's high-regression-rate fuel) and the green-
// propellant N₂O variants.
//
// The fuel choice drives:
//   - density ρ_fuel
//   - the Marxman pyrolysis-regression-rate constants a + n in
//         r_dot = a · G_ox^n  [m/s]
//
// Both constants ride on HybridFuelProperties, queried via
// HybridFuelProperties.For(HybridFuel).

namespace Voxelforge.Hybrid;

/// <summary>
/// Solid-grain fuel choice for a hybrid rocket motor (Sprint R.W2).
/// </summary>
internal enum HybridFuel
{
    /// <summary>
    /// Hydroxyl-terminated polybutadiene — the classical hybrid fuel.
    /// Density ~920 kg/m³. Marxman regression-rate fit (Karabeyoglu et
    /// al. 2003 for LOX/HTPB): a = 1.37e-4 m/s · (kg/(m²·s))^-n,
    /// n = 0.681. Stanford SPIRIT classroom baseline.
    /// </summary>
    HTPB = 0,

    /// <summary>
    /// Paraffin (n-alkane wax). Density ~920 kg/m³. Karabeyoglu's high-
    /// regression-rate fuel (entrainment mechanism); regression rate is
    /// ~3× HTPB at the same G_ox. Wave-2 candidate.
    /// </summary>
    Paraffin = 1,
}
