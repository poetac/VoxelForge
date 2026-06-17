// StirlingWorkingFluid.cs — Sprint STR.W2 working-fluid discriminator.
//
// Wave-2 adds explicit working-fluid choice. Wave-1 hard-coded helium
// (the modern high-performance default). Different fluids change the
// achievable specific-power + indicated-work fraction:
//
//   Helium   — modern default. Highest thermal conductivity → fastest
//              regenerator response → highest η. Used in NASA ASRG +
//              Sunpower designs.
//   Hydrogen — slightly higher specific heat → marginally more work
//              per cycle, but flammability + diffusion through metals
//              makes it niche.
//   Air      — original 1816 Robert Stirling configuration. Lower
//              conductivity → slower regenerator response → lower η.
//              Used in early industrial + present-day low-cost units.

namespace Voxelforge.Stirling;

/// <summary>
/// Stirling-engine working-fluid choice (Sprint STR.W2).
/// </summary>
internal enum StirlingWorkingFluid
{
    /// <summary>
    /// Helium — modern high-η default. Sprint STR.W1 baseline.
    /// </summary>
    Helium = 0,

    /// <summary>
    /// Hydrogen — highest cp; ~ 2 % indicated-work bonus vs He but
    /// flammability + permeation limits commercial use.
    /// </summary>
    Hydrogen = 1,

    /// <summary>
    /// Air — original 1816 Stirling configuration. Lower thermal
    /// conductivity → reduced indicated-work fraction. ~ 15 % efficiency
    /// penalty vs He.
    /// </summary>
    Air = 2,
}
