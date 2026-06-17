// OOB-7 (issue #343): rotating detonation engine combustion-chamber topology.
namespace Voxelforge.Optimization;

/// <summary>
/// OOB-7 (issue #343): rotating detonation engine combustion-chamber topology selector.
/// Distinct from <see cref="ChannelTopology"/>, which governs coolant routing — this
/// enum governs the combustion mode at the chamber inlet.
/// </summary>
public enum RdeTopology
{
    /// <summary>Conventional deflagration combustion (default). No RDE physics applied.</summary>
    None = 0,

    /// <summary>
    /// Single annular detonation channel with a solid centre-body.
    /// Detonation waves propagate azimuthally around the annulus.
    /// </summary>
    Annular = 1,

    /// <summary>
    /// Annular detonation channel with a central nozzle insert that
    /// recovers additional stagnation pressure from the detonation products.
    /// </summary>
    AnnularWithCentralNozzle = 2,
}
