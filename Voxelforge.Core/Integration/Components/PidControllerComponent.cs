// PidControllerComponent.cs — Sprint SI.W9 stateful PID controller.
//
// Classical proportional-integral-derivative controller wrapped as a
// SystemComponent + IStatefulComponent. Only the IntegralError is
// integrated by the TimeStepIntegrator; the derivative-term tracking
// is purely internal to the component (no need to expose it as a
// state variable).
//
// Inputs:
//   Setpoint        — target value.
//   ProcessVariable — measured plant output.
//
// Output:
//   ControlOutput — u(t) = K_p · e + K_i · ∫e dt + K_d · de/dt
//   where e = setpoint − processVariable.
//
// In a closed-loop subsystem, the ControlOutput typically wires back
// to the plant's actuation input — creating a cycle. Use
// ComponentNetwork.SolveIterative() to converge.

using System;
using System.Collections.Generic;

namespace Voxelforge.Integration.Components;

/// <summary>Classical PID controller (Sprint SI.W9).</summary>
internal sealed class PidControllerComponent
    : SystemComponent, IStatefulComponent
{
    private readonly double _kP, _kI, _kD;
    private double _integralError;
    private double _previousError;

    /// <summary>Create a PID controller with the given gains.</summary>
    /// <param name="name">Network-unique component name.</param>
    /// <param name="proportionalGain">K_p [-] (output per error unit).</param>
    /// <param name="integralGain">K_i [1/s] (output per error·second).</param>
    /// <param name="derivativeGain">K_d [s] (output per (error/s)).</param>
    public PidControllerComponent(
        string name,
        double proportionalGain,
        double integralGain    = 0.0,
        double derivativeGain  = 0.0)
        : base(name)
    {
        _kP = proportionalGain;
        _kI = integralGain;
        _kD = derivativeGain;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "Setpoint", "ProcessVariable" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[] { "ControlOutput", "Error" };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        double error = inputs["Setpoint"] - inputs["ProcessVariable"];
        // Derivative term — approximate from last Evaluate's error.
        double dEdt = error - _previousError;     // unit-less; treat dt = 1 sample
        double u = _kP * error + _kI * _integralError + _kD * dEdt;
        outputs["ControlOutput"] = u;
        outputs["Error"]         = error;
        _previousError = error;
    }

    // ── IStatefulComponent: integrate the error term over time ─────────

    public IReadOnlyList<string> StateVariables { get; }
        = new[] { "IntegralError" };

    public void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives)
    {
        // d(IntegralError)/dt = current error.
        derivatives[0] = portOutputs["Error"];
    }

    public void GetInitialState(Span<double> destination)
        => destination[0] = 0.0;

    public void SetState(ReadOnlySpan<double> state)
        => _integralError = state[0];

    public void GetCurrentState(Span<double> destination)
        => destination[0] = _integralError;
}
