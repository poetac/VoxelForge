// StirlingConfiguration.cs — Sprint STR.W1 Stirling-engine configuration
// discriminator.
//
// Wave-1 ships the three canonical Stirling mechanical configurations:
//
//   Alpha — two pistons in separate cylinders, hot + cold, connected
//           by a regenerator. Phase angle ~ 90°. Highest specific power
//           but mechanically complex.
//   Beta  — single cylinder with both displacer + power piston. Phase
//           angle ~ 90°. Compact + the original Robert Stirling 1816
//           layout.
//   Gamma — displacer cylinder + separate power cylinder. Less efficient
//           than beta (dead volume in transfer port) but mechanically
//           simpler. Common in residential CHP (Whispergen, Sunpower).
//
// Wave-2+ will add free-piston (no mechanical linkage, used in NASA
// ASRG + cryocoolers) + Ringbom (variant of beta).

namespace Voxelforge.Stirling;

/// <summary>
/// Stirling-engine mechanical configuration (Sprint STR.W1).
/// </summary>
internal enum StirlingConfiguration
{
    /// <summary>Degenerate sentinel.</summary>
    None = 0,

    /// <summary>Alpha — two separate pistons + regenerator + connecting tube.</summary>
    Alpha = 1,

    /// <summary>Beta — single-cylinder displacer + power piston (Stirling 1816 original).</summary>
    Beta = 2,

    /// <summary>Gamma — separate displacer + power cylinders (CHP workhorse).</summary>
    Gamma = 3,
}
