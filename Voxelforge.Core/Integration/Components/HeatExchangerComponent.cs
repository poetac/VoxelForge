// HeatExchangerComponent.cs — Sprint SI.W2 adapter for plate-fin HX.

using System.Collections.Generic;
using Voxelforge.HeatExchanger;

namespace Voxelforge.Integration.Components;

/// <summary>Plate-fin counterflow heat exchanger adapter.</summary>
internal sealed class HeatExchangerComponent : SystemComponent
{
    private readonly PlateFinDesign _design;

    public HeatExchangerComponent(string name, PlateFinDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[]
        {
            "HotMassFlow_kgs", "ColdMassFlow_kgs",
            "HotInletTemperature_K", "ColdInletTemperature_K",
        };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "HeatDuty_W", "HotOutletTemperature_K", "ColdOutletTemperature_K",
            "Effectiveness", "NumberOfTransferUnits",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            HotMassFlow_kgs        = inputs["HotMassFlow_kgs"],
            ColdMassFlow_kgs       = inputs["ColdMassFlow_kgs"],
            HotInletTemperature_K  = inputs["HotInletTemperature_K"],
            ColdInletTemperature_K = inputs["ColdInletTemperature_K"],
        };
        var r = EpsilonNtuSolver.Solve(d);
        outputs["HeatDuty_W"]              = r.HeatDuty_W;
        outputs["HotOutletTemperature_K"]  = r.HotOutletTemperature_K;
        outputs["ColdOutletTemperature_K"] = r.ColdOutletTemperature_K;
        outputs["Effectiveness"]           = r.Effectiveness;
        outputs["NumberOfTransferUnits"]   = r.NumberOfTransferUnits;
    }
}
