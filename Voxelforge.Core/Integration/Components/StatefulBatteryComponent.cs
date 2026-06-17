// StatefulBatteryComponent.cs — Sprint SI.W7 stateful battery-pack
// adapter. Tracks state-of-charge over time given a per-tick
// LoadCurrent_A input (positive = discharge, negative = charge).
//
// SoC evolution (Coulomb-counting):
//   dSoC/dt = − I_pack / (C_pack_Ah · 3600)
//   C_pack_Ah = N_parallel · NominalCapacity_Ah_per_cell
//
// The static SI.W1 BatteryComponent uses the design's StateOfCharge
// field as a snapshot input; this stateful variant evolves SoC over
// time so a network can simulate full discharge curves.

using System;
using System.Collections.Generic;
using Voxelforge.Battery;

namespace Voxelforge.Integration.Components;

/// <summary>
/// Stateful adapter for the Battery pillar. Tracks SoC over time via
/// Coulomb counting.
/// </summary>
internal sealed class StatefulBatteryComponent
    : SystemComponent, IStatefulComponent
{
    private readonly BatteryPackDesign _design;
    private readonly double _initialSoC;
    private readonly double _packCapacity_Ah;
    private double _currentSoC;

    public StatefulBatteryComponent(
        string name,
        BatteryPackDesign design,
        double initialStateOfCharge)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (initialStateOfCharge < 0 || initialStateOfCharge > 1.0)
            throw new ArgumentOutOfRangeException(nameof(initialStateOfCharge),
                "Initial SoC must be in [0, 1].");
        _design          = design;
        _initialSoC      = initialStateOfCharge;
        _currentSoC      = initialStateOfCharge;
        // Cluster-anchored pack capacity: per-cell nominal × parallel strings.
        // (Pulled from the chemistry registry to match BP.W1's energy
        // integral.)
        var chem = BatteryChemistryRegistry.For(design.Chemistry);
        _packCapacity_Ah = design.ParallelStrings * chem.NominalCapacity_Ah;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "LoadCurrent_A" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StateOfCharge", "PackLoadedVoltage_V",
            "PackElectricalPower_W", "PackHeatGeneration_W",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            StateOfCharge  = _currentSoC,
            LoadCurrent_A  = inputs["LoadCurrent_A"],
        };
        var r = BatteryPackSolver.Solve(d);
        outputs["StateOfCharge"]          = _currentSoC;
        outputs["PackLoadedVoltage_V"]    = r.PackLoadedVoltage_V;
        outputs["PackElectricalPower_W"]  = r.PackElectricalPower_W;
        outputs["PackHeatGeneration_W"]   = r.PackHeatGeneration_W;
    }

    public IReadOnlyList<string> StateVariables { get; }
        = new[] { "StateOfCharge" };

    public void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives)
    {
        // dSoC/dt = − I_pack / (C_pack_Ah · 3600).
        double I_pack = portInputs["LoadCurrent_A"];
        derivatives[0] = -I_pack / (_packCapacity_Ah * 3600.0);
    }

    public void GetInitialState(Span<double> destination)
        => destination[0] = _initialSoC;

    public void GetCurrentState(Span<double> destination)
        => destination[0] = _currentSoC;

    public void SetState(ReadOnlySpan<double> state)
    {
        double soc = state[0];
        // Clamp to [0, 1] — SoC can't physically exceed these bounds.
        // (The integrator might overshoot near the rail due to stale
        // current values; clamping here keeps subsequent Evaluates valid.)
        if (soc < 0.0) soc = 0.0;
        if (soc > 1.0) soc = 1.0;
        _currentSoC = soc;
    }
}
