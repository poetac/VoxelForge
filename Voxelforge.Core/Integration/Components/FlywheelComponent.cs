// FlywheelComponent.cs — Sprint SI.W2 adapter for flywheel energy storage.

using System.Collections.Generic;
using Voxelforge.Flywheel;

namespace Voxelforge.Integration.Components;

/// <summary>Flywheel kinetic-energy-storage adapter.</summary>
internal sealed class FlywheelComponent : SystemComponent
{
    private readonly FlywheelDesign _design;

    public FlywheelComponent(string name, FlywheelDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "StateOfCharge" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StoredEnergy_J", "StoredEnergy_kWh",
            "ParasiticPowerLoss_W", "AutoDischargeTimeConstant_s",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with { StateOfCharge = inputs["StateOfCharge"] };
        var r = FlywheelSolver.Solve(d);
        outputs["StoredEnergy_J"]               = r.StoredEnergy_J;
        outputs["StoredEnergy_kWh"]             = r.StoredEnergy_kWh;
        outputs["ParasiticPowerLoss_W"]         = r.ParasiticPowerLoss_W;
        outputs["AutoDischargeTimeConstant_s"]  = r.AutoDischargeTimeConstant_s;
    }
}
