// AccumulatorComponent.cs — Sprint SI.W14 generic time-integrator.
//
// A pillar-agnostic stateful component that integrates a single input
// port over time:
//
//   dY/dt = Input_rate(t)
//
// Use cases:
//   • Total energy delivered: Input = power [W], output = energy [J]
//   • Total mass throughput: Input = mass-flow [kg/s], output = mass [kg]
//   • Total charge: Input = current [A], output = coulombs [C]
//
// Pair with Connect() to wire any source component's "rate" output into
// the accumulator's input. The accumulator's output is the running
// integral, refreshed every tick.

using System;
using System.Collections.Generic;

namespace Voxelforge.Integration.Components;

/// <summary>
/// Generic time-integrator (Sprint SI.W14). Accumulates the
/// time-integral of <c>Input_rate</c> into <c>Accumulated_total</c>
/// state. Pillar-agnostic.
/// </summary>
internal sealed class AccumulatorComponent
    : SystemComponent, IStatefulComponent
{
    private readonly double _initial;
    private double _current;

    public AccumulatorComponent(string name, double initial = 0.0) : base(name)
    {
        _initial = initial;
        _current = initial;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "Input_rate" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[] { "Accumulated_total" };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        // Output the running total. State is mutated by the integrator
        // via SetState() between ticks.
        outputs["Accumulated_total"] = _current;
    }

    public IReadOnlyList<string> StateVariables { get; }
        = new[] { "Accumulated_total" };

    public void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives)
    {
        derivatives[0] = portInputs["Input_rate"];
    }

    public void GetInitialState(Span<double> destination)
        => destination[0] = _initial;

    public void SetState(ReadOnlySpan<double> state)
        => _current = state[0];

    public void GetCurrentState(Span<double> destination)
        => destination[0] = _current;
}
