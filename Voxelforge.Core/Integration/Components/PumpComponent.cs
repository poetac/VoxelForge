// PumpComponent.cs — Sprint SI.W2 adapter for centrifugal pumps.

using System.Collections.Generic;
using Voxelforge.Pump;

namespace Voxelforge.Integration.Components;

/// <summary>Centrifugal pump adapter.</summary>
internal sealed class PumpComponent : SystemComponent
{
    private readonly CentrifugalPumpDesign _design;

    public PumpComponent(string name, CentrifugalPumpDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "VolumetricFlowRate_m3s", "HeadRise_m" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "HydraulicPower_W", "ShaftPowerInput_W",
            "SpecificSpeedSI", "CavitationMargin_m",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            VolumetricFlowRate_m3s = inputs["VolumetricFlowRate_m3s"],
            HeadRise_m             = inputs["HeadRise_m"],
        };
        var r = CentrifugalPumpSolver.Solve(d);
        outputs["HydraulicPower_W"]    = r.HydraulicPower_W;
        outputs["ShaftPowerInput_W"]   = r.ShaftPowerInput_W;
        outputs["SpecificSpeedSI"]     = r.SpecificSpeedSI;
        outputs["CavitationMargin_m"]  = r.CavitationMargin_m;
    }
}
