// DesignVariableInfo.cs — engine-family-agnostic descriptor for one
// dimension of an IObjective's search vector.
//
// Distinct from SaDesignVariableDescriptor (which carries SA-attribute
// metadata like DeclaringType + MemberName) so IObjective remains free
// of reflection-attribute coupling. Future engine families that don't
// drive their search vector via [SaDesignVariable] reflection (e.g. an
// air-breathing engine using a hand-rolled vector layout) can produce
// DesignVariableInfo arrays without touching the SA registry.
//
// Design rationale (the IObjective decoupling):
// each engine family ships an IObjective whose Variables list describes
// its native search-space shape; the optimizer never sees an
// engine-specific record.

namespace Voxelforge.Optimization;

/// <summary>
/// One dimension of an <see cref="IObjective"/> search vector. Pairs a
/// human-readable name with the inclusive sampling bounds that the
/// optimizer must respect.
/// </summary>
/// <param name="Name">
/// Human-readable label for diagnostics, plot axes, and gate-violation
/// messages. Conventionally the property name on the underlying record
/// for SA-attribute-driven objectives (e.g. <c>"ContractionRatio"</c>),
/// but free-form for hand-rolled objectives.
/// </param>
/// <param name="Min">Inclusive lower bound for sampling. Must be strictly less than <paramref name="Max"/>.</param>
/// <param name="Max">Inclusive upper bound for sampling. Must be strictly greater than <paramref name="Min"/>.</param>
public readonly record struct DesignVariableInfo(
    string Name,
    double Min,
    double Max)
{
    /// <summary>
    /// Project the descriptor list to the <c>(double Min, double Max)[]</c>
    /// bounds-array shape that <see cref="MultiChainOptimizer"/> and
    /// <see cref="SimulatedAnnealingOptimizer"/> consume directly.
    /// </summary>
    public static (double Min, double Max)[] ToBoundsArray(
        System.Collections.Generic.IReadOnlyList<DesignVariableInfo> variables)
    {
        if (variables is null) throw new System.ArgumentNullException(nameof(variables));
        var bounds = new (double Min, double Max)[variables.Count];
        for (int i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (v.Min >= v.Max)
                throw new System.ArgumentException(
                    $"DesignVariableInfo[{i}] '{v.Name}' has invalid bounds [{v.Min}, {v.Max}] — Min must be strictly less than Max.",
                    nameof(variables));
            bounds[i] = (v.Min, v.Max);
        }
        return bounds;
    }

    /// <summary>
    /// Return a copy of this descriptor with bounds replaced. Convenience
    /// for bind-time clipping in per-pillar objectives — e.g.
    /// <c>MpdObjective.ApplyBusPowerClip</c> narrowing the arc-current dim
    /// based on <c>BusPower_W_avail</c>. Equivalent to
    /// <c>this with { Min = min, Max = max }</c> but easier to chain when
    /// only one bound changes:
    /// <code>
    ///   clamped[0] = defaults[0].WithMax(maxJ_busLimit);
    /// </code>
    /// </summary>
    /// <param name="min">New lower bound. Must be strictly less than <paramref name="max"/>.</param>
    /// <param name="max">New upper bound. Must be strictly greater than <paramref name="min"/>.</param>
    public DesignVariableInfo WithBounds(double min, double max)
    {
        if (min >= max)
            throw new System.ArgumentException(
                $"WithBounds for '{Name}': min ({min}) must be strictly less than max ({max}).");
        return this with { Min = min, Max = max };
    }

    /// <summary>
    /// Return a copy of this descriptor with only <see cref="Min"/>
    /// replaced. Preserves the existing <see cref="Max"/>; validates the
    /// new Min stays strictly below it.
    /// </summary>
    public DesignVariableInfo WithMin(double min)
    {
        if (min >= Max)
            throw new System.ArgumentException(
                $"WithMin for '{Name}': new Min ({min}) must be strictly less than existing Max ({Max}).");
        return this with { Min = min };
    }

    /// <summary>
    /// Return a copy of this descriptor with only <see cref="Max"/>
    /// replaced. Preserves the existing <see cref="Min"/>; validates the
    /// new Max stays strictly above it.
    /// </summary>
    public DesignVariableInfo WithMax(double max)
    {
        if (max <= Min)
            throw new System.ArgumentException(
                $"WithMax for '{Name}': new Max ({max}) must be strictly greater than existing Min ({Min}).");
        return this with { Max = max };
    }
}
