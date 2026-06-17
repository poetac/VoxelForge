// BatteryComponent.cs — Sprint SI.W1 SystemComponent adapter for the
// Battery pillar's BatteryPackSolver.
//
// Inputs:
//   LoadCurrent_A — pack-level discharge current [A]. The system
//                   passes this in via a connection or external feed.
//
// Outputs:
//   PackLoadedVoltage_V — pack terminal voltage under load [V].
//   PackElectricalPower_W — V_pack · I_pack [W].
//   PackHeatGeneration_W — I²·R Joule heat [W].

using System.Collections.Generic;
using Voxelforge.Battery;

namespace Voxelforge.Integration.Components;

/// <summary>
/// <see cref="SystemComponent"/> adapter wrapping
/// <c>BatteryPackSolver.Solve</c> (Sprint SI.W1).
/// </summary>
internal sealed class BatteryComponent : SystemComponent
{
    private readonly BatteryPackDesign _design;

    /// <summary>Create a battery component bound to a fixed design.</summary>
    /// <param name="name">Network-unique component name.</param>
    /// <param name="design">Battery pack design. LoadCurrent_A is overridden
    /// at each Evaluate by the value flowing in through the input port.</param>
    public BatteryComponent(string name, BatteryPackDesign design)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "LoadCurrent_A" };

    /// <inheritdoc />
    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "PackLoadedVoltage_V",
            "PackElectricalPower_W",
            "PackHeatGeneration_W",
        };

    /// <inheritdoc />
    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var designAtTick = _design with { LoadCurrent_A = inputs["LoadCurrent_A"] };
        var result = BatteryPackSolver.Solve(designAtTick);
        outputs["PackLoadedVoltage_V"]   = result.PackLoadedVoltage_V;
        outputs["PackElectricalPower_W"] = result.PackElectricalPower_W;
        outputs["PackHeatGeneration_W"]  = result.PackHeatGeneration_W;
    }
}
