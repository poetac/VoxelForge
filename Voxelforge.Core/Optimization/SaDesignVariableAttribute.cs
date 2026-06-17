// SaDesignVariableAttribute.cs — Sprint 5 Dev A (2026-04-22):
// Core metadata for the ADR-010 single-source-of-truth design-variable
// registry. Marks properties on RegenChamberDesign / InjectorPattern
// (or any other SA-visible record) with the SA-vector index, (min, max)
// bounds, and a conditional-application gate, so DesignVariableRegistry
// can emit the Bounds array + drive Unpack without callers duplicating
// the literal values in RegenChamberOptimization.Bounds.
//
// Next-sprint iteration (2026-04-22) — added the Gate discriminator so
// conditionally-applied dims (injector pattern, TPMS topology, aerospike
// topology) can be expressed declaratively. Dims 0..22 are all tagged;
// RegenChamberOptimization.Bounds now reads from the registry. Pack /
// Unpack still delegate to typed field access; converting them to
// full registry iteration is a follow-on refactor.

namespace Voxelforge.Optimization;

/// <summary>
/// Conditional-application predicate for an SA design variable. When
/// Unpack decides whether to write a sampled value back into the
/// baseline, it consults this gate — only writing the value when the
/// baseline's categorical state (channel topology, engine cycle,
/// injector-pattern presence) matches the gate.
/// <para>
/// This exists because a packed vector is length-stable across
/// topologies (see ADR-003) — every Unpack receives every
/// dim, whether or not the baseline actually uses it. Without the
/// gate, a TPMS cell-edge value would silently overwrite an axial
/// baseline's ignored field, and re-emerge as a spurious perturbation
/// the next time the user flipped topology back to TPMS.
/// </para>
/// </summary>
public enum SaGate
{
    /// <summary>Always applied in Unpack — the default for plain dims.</summary>
    None = 0,

    /// <summary>
    /// Applied only when <c>baseline.InjectorElementPattern</c> is
    /// non-null. Covers the five injector-pattern SA knobs (element
    /// count, ΔP_inj / Pc, outer-row film fraction, CdOx, CdFuel) —
    /// when no pattern is set, the dim is inert but still packed so
    /// the vector stays the same length.
    /// </summary>
    InjectorPatternPresent,

    /// <summary>
    /// Applied only when <c>baseline.ChannelTopology</c> is a TPMS
    /// family (TpmsGyroid / TpmsSchwarzP / TpmsSchwarzD). Covers
    /// TPMS cell-edge + solid-fraction dims.
    /// </summary>
    TpmsTopology,

    /// <summary>
    /// Applied only when <c>baseline.ChannelTopology</c> is Aerospike.
    /// Covers the aerospike plug-truncation dim.
    /// </summary>
    AerospikeTopology,
}

/// <summary>
/// Marks a property (or field) on a record as one dimension of the
/// SA search vector. <see cref="DesignVariableRegistry"/> scans types
/// at static-init time and materialises a Bounds array keyed by
/// <see cref="Index"/>.
/// <para>
/// See ADR-010 for the architectural debt this attribute pays down.
/// See <see cref="RegenChamberOptimization.Bounds"/> — now derived
/// from this registry (2026-04-22).
/// </para>
/// </summary>
[System.AttributeUsage(
    System.AttributeTargets.Property | System.AttributeTargets.Field,
    AllowMultiple = false, Inherited = true)]
public sealed class SaDesignVariableAttribute : System.Attribute
{
    /// <summary>Position in the SA search vector (0-based).</summary>
    public int Index { get; }

    /// <summary>Lower bound for SA sampling.</summary>
    public double Min { get; }

    /// <summary>Upper bound for SA sampling.</summary>
    public double Max { get; }

    /// <summary>
    /// Conditional-application predicate. Default <see cref="SaGate.None"/>
    /// matches pre-Gate-enum attribute usage bit-identically.
    /// </summary>
    public SaGate Gate { get; }

    public SaDesignVariableAttribute(
        int index, double min, double max,
        SaGate gate = SaGate.None)
    {
        if (index < 0)
            throw new System.ArgumentOutOfRangeException(nameof(index),
                "SA design-variable index must be non-negative");
        if (min >= max)
            throw new System.ArgumentException(
                $"SA design-variable bounds must satisfy min < max; got [{min}, {max}]",
                nameof(min));
        Index = index;
        Min = min;
        Max = max;
        Gate = gate;
    }
}
