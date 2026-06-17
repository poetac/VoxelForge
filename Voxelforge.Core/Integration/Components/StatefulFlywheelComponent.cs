// StatefulFlywheelComponent.cs — Sprint SI.W10 stateful flywheel
// adapter. Tracks state-of-charge over time given the per-tick
// bearing parasitic power loss + an optional external charge/discharge
// power input.
//
// SoC evolution:
//   dE/dt = P_charge − P_drag
//   dSoC/dt = dE/dt / E_max
// where E_max = ½·I·ω_design² is the maximum-stored energy at the
// design rotation speed.

using System;
using System.Collections.Generic;
using Voxelforge.Flywheel;

namespace Voxelforge.Integration.Components;

/// <summary>Stateful flywheel adapter — tracks SoC over time.</summary>
internal sealed class StatefulFlywheelComponent
    : SystemComponent, IStatefulComponent
{
    private readonly FlywheelDesign _design;
    private readonly double _initialSoC;
    private readonly double _maxStoredEnergy_J;
    private double _currentSoC;

    public StatefulFlywheelComponent(
        string name,
        FlywheelDesign design,
        double initialStateOfCharge)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (initialStateOfCharge < 0 || initialStateOfCharge > 1.0)
            throw new ArgumentOutOfRangeException(nameof(initialStateOfCharge),
                "Initial SoC must be in [0, 1].");
        _design     = design;
        _initialSoC = initialStateOfCharge;
        _currentSoC = initialStateOfCharge;
        // Compute E_max once: ½·I·ω_design².
        var d_full = design with { StateOfCharge = 1.0 };
        var r_full = FlywheelSolver.Solve(d_full);
        _maxStoredEnergy_J = r_full.StoredEnergy_J;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "ChargePower_W" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StateOfCharge", "StoredEnergy_J",
            "ParasiticPowerLoss_W", "AngularVelocity_rads",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with { StateOfCharge = _currentSoC };
        var r = FlywheelSolver.Solve(d);
        outputs["StateOfCharge"]         = _currentSoC;
        outputs["StoredEnergy_J"]        = r.StoredEnergy_J;
        outputs["ParasiticPowerLoss_W"]  = r.ParasiticPowerLoss_W;
        outputs["AngularVelocity_rads"]  = r.AngularVelocity_rads;
    }

    public IReadOnlyList<string> StateVariables { get; }
        = new[] { "StateOfCharge" };

    public void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives)
    {
        double P_charge = portInputs["ChargePower_W"];
        double P_drag = portOutputs["ParasiticPowerLoss_W"];
        // dE/dt = P_charge − P_drag → dSoC/dt = (P_charge − P_drag) / E_max.
        derivatives[0] = (P_charge - P_drag) / _maxStoredEnergy_J;
    }

    public void GetInitialState(Span<double> destination)
        => destination[0] = _initialSoC;

    public void GetCurrentState(Span<double> destination)
        => destination[0] = _currentSoC;

    public void SetState(ReadOnlySpan<double> state)
    {
        double soc = state[0];
        if (soc < 0.0) soc = 0.0;
        if (soc > 1.0) soc = 1.0;
        _currentSoC = soc;
    }
}
