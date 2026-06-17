// HeatPipeComponent.cs — Sprint SI.W2 adapter for capillary heat pipes.

using System.Collections.Generic;
using Voxelforge.HeatPipe;

namespace Voxelforge.Integration.Components;

/// <summary>Capillary-driven heat pipe adapter.</summary>
internal sealed class HeatPipeComponent : SystemComponent
{
    private readonly HeatPipeDesign _design;

    public HeatPipeComponent(string name, HeatPipeDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "HeatThroughput_W", "OperatingTemperature_K" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "EndToEndDeltaT_K", "ThermalResistance_K_W",
            "CapillaryMargin", "GoverningMargin",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            HeatThroughput_W      = inputs["HeatThroughput_W"],
            OperatingTemperature_K = inputs["OperatingTemperature_K"],
        };
        var r = HeatPipeSolver.Solve(d);
        outputs["EndToEndDeltaT_K"]       = r.EndToEndDeltaT_K;
        outputs["ThermalResistance_K_W"]  = r.ThermalResistance_K_W;
        outputs["CapillaryMargin"]        = r.CapillaryMargin;
        outputs["GoverningMargin"]        = r.GoverningMargin;
    }
}
