// AntennaLinkDesignBinder.cs — Sprint ANT.W3 SA Pack/Unpack helper for
// the Antenna pillar's categorical ModulationScheme dim.
//
// The Antenna pillar does not (yet) ship an IObjective adapter, so this
// binder is the minimal seam the optimizer needs to vary modulation
// jointly with antenna geometry once that adapter lands. The shape
// mirrors HetObjective.Pack / HetObjective.Unpack — one method each,
// hand-coded and reflection-free.
//
// Layout (Wave-1 set, 1 dim):
//   0  ModulationSchemeIndex   0 .. ModulationSchemeTable.Count - 1
//
// Subsequent ANT.* sprints can grow the vector by appending new
// [SaDesignVariable] attributes to AntennaLinkDesign and extending
// this binder by one slot each.

using System;
using Voxelforge.Optimization;

namespace Voxelforge.Antenna;

/// <summary>
/// Pack/Unpack helper for the SA-visible portion of
/// <see cref="AntennaLinkDesign"/> (Sprint ANT.W3).
/// </summary>
internal static class AntennaLinkDesignBinder
{
    /// <summary>
    /// Names of the SA design-vector slots. Order is load-bearing.
    /// </summary>
    internal static readonly string[] DefaultVariableNames =
    {
        nameof(AntennaLinkDesign.ModulationSchemeIndex),
    };

    /// <summary>
    /// Default bounds aligned to the <see cref="SaDesignVariableAttribute"/>
    /// metadata on each tagged property. Sourced from the
    /// <see cref="DesignVariableRegistry"/> reflection pass so any drift
    /// between the attribute and the binder-bounds is impossible by
    /// construction.
    /// </summary>
    internal static DesignVariableInfo[] DefaultBounds()
    {
        var descriptors = DesignVariableRegistry.For(typeof(AntennaLinkDesign));
        var infos = new DesignVariableInfo[descriptors.Count];
        for (int i = 0; i < descriptors.Count; i++)
            infos[i] = new DesignVariableInfo(
                Name: descriptors[i].MemberName,
                Min:  descriptors[i].Min,
                Max:  descriptors[i].Max);
        return infos;
    }

    /// <summary>
    /// Project an antenna-link design into the SA vector. Inverse of
    /// <see cref="Unpack"/>.
    /// </summary>
    internal static double[] Pack(AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return new[]
        {
            (double)design.ModulationSchemeIndex,
        };
    }

    /// <summary>
    /// Inflate an SA vector + baseline design into a concrete
    /// AntennaLinkDesign. Categorical state and continuous numeric
    /// state not represented in the SA vector are preserved from
    /// <paramref name="baseline"/> per CLAUDE.md PicoGK pitfall #7.
    /// </summary>
    internal static AntennaLinkDesign Unpack(
        double[] vector, AntennaLinkDesign baseline)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(baseline);
        if (vector.Length != DefaultVariableNames.Length)
            throw new ArgumentException(
                $"Antenna SA vector requires {DefaultVariableNames.Length} elements; "
              + $"got {vector.Length}.",
                nameof(vector));

        // ModulationSchemeIndex — round to int, clamp into the enum's
        // valid index range, then mirror onto the categorical
        // Modulation field via the helper. Same shape as the rocket
        // binder's int-typed dim path (ConvertForProperty → Math.Round
        // then cast).
        int rawIndex = (int)Math.Round(vector[0]);
        int clamped  = Math.Clamp(rawIndex, 0, ModulationSchemeTable.Count - 1);
        return baseline.WithModulationIndex(clamped);
    }
}
