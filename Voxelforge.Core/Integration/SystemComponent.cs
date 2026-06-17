// SystemComponent.cs — Sprint SI.W1 abstract base for a component that
// participates in a ComponentNetwork.
//
// Each component declares its input + output ports (as named doubles)
// and implements an Evaluate method that reads the inputs and writes
// the outputs. The ComponentNetwork class topologically sorts components
// by their port dependencies and evaluates them in order.
//
// Each pillar gets its own concrete subclass under
// Voxelforge.Core/Integration/Components/ that wraps the pillar's
// closed-form Solver. The pillar's Solver remains usable standalone;
// the component wrapper is purely additive.
//
// Sprint SI.W1 keeps the value type to plain double — sufficient for
// most domain quantities (voltage, current, temperature, mass flow,
// torque, etc.). Future SI.W2 may admit complex / vector / phase types.

using System.Collections.Generic;

namespace Voxelforge.Integration;

/// <summary>
/// Abstract base for a component that participates in a
/// <see cref="ComponentNetwork"/> (Sprint SI.W1).
/// </summary>
internal abstract class SystemComponent
{
    /// <summary>Unique component name within a network.</summary>
    public string Name { get; }

    protected SystemComponent(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Component name must be non-empty.", nameof(name));
        Name = name;
    }

    /// <summary>
    /// Names of the ports this component reads on each Evaluate. The
    /// caller is responsible for supplying values for each name in the
    /// inputs dictionary passed to <see cref="Evaluate"/>.
    /// </summary>
    public abstract IReadOnlyList<string> InputPorts { get; }

    /// <summary>
    /// Names of the ports this component writes on each Evaluate. The
    /// caller can consume these via the outputs dictionary that
    /// <see cref="Evaluate"/> populates.
    /// </summary>
    public abstract IReadOnlyList<string> OutputPorts { get; }

    /// <summary>
    /// Compute output port values from input port values. Each
    /// implementation reads its inputs from the supplied dictionary
    /// (keyed by the names in <see cref="InputPorts"/>) and writes its
    /// outputs into the supplied dictionary (keyed by the names in
    /// <see cref="OutputPorts"/>).
    /// </summary>
    /// <param name="inputs">Dictionary keyed by each <see cref="InputPorts"/>
    /// name. The component may assume all input names are present.</param>
    /// <param name="outputs">Dictionary that the component populates
    /// with one entry per <see cref="OutputPorts"/> name.</param>
    public abstract void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs);
}
