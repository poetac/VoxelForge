// RefrigerationComponent.cs — Sprint SI.W2 adapter for HVAC / heat pumps.

using System.Collections.Generic;
using Voxelforge.Refrigeration;

namespace Voxelforge.Integration.Components;

/// <summary>Vapor-compression refrigeration / heat-pump adapter.</summary>
internal sealed class RefrigerationComponent : SystemComponent
{
    private readonly RefrigerationDesign _design;

    public RefrigerationComponent(string name, RefrigerationDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "ColdReservoirTemperature_K", "HotReservoirTemperature_K", "CompressorPowerInput_W" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ColdSideHeatRemoval_W", "HotSideHeatDelivery_W",
            "CoolingCop", "HeatingCop",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            ColdReservoirTemperature_K = inputs["ColdReservoirTemperature_K"],
            HotReservoirTemperature_K  = inputs["HotReservoirTemperature_K"],
            CompressorPowerInput_W     = inputs["CompressorPowerInput_W"],
        };
        var r = RefrigerationSolver.Solve(d);
        outputs["ColdSideHeatRemoval_W"] = r.ColdSideHeatRemoval_W;
        outputs["HotSideHeatDelivery_W"] = r.HotSideHeatDelivery_W;
        outputs["CoolingCop"]            = r.CoolingCop;
        outputs["HeatingCop"]            = r.HeatingCop;
    }
}
