// ThermoelectricComponent.cs — Sprint SI.W2 adapter for TEGs.

using System.Collections.Generic;
using Voxelforge.Thermoelectric;

namespace Voxelforge.Integration.Components;

/// <summary>Thermoelectric generator adapter.</summary>
internal sealed class ThermoelectricComponent : SystemComponent
{
    private readonly ThermoelectricGeneratorDesign _design;

    public ThermoelectricComponent(string name, ThermoelectricGeneratorDesign design)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "HotSideTemperature_K", "ColdSideTemperature_K", "HotSideHeatInput_W" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ElectricPowerOutput_W", "HeatRejectedToColdSide_W",
            "ConversionEfficiency", "CarnotEfficiency",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            HotSideTemperature_K  = inputs["HotSideTemperature_K"],
            ColdSideTemperature_K = inputs["ColdSideTemperature_K"],
            HotSideHeatInput_W    = inputs["HotSideHeatInput_W"],
        };
        var r = ThermoelectricGeneratorSolver.Solve(d);
        outputs["ElectricPowerOutput_W"]    = r.ElectricPowerOutput_W;
        outputs["HeatRejectedToColdSide_W"] = r.HeatRejectedToColdSide_W;
        outputs["ConversionEfficiency"]    = r.ConversionEfficiency;
        outputs["CarnotEfficiency"]        = r.CarnotEfficiency;
    }
}
