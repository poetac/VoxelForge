// StirlingComponent.cs — Sprint SI.W2 adapter for Stirling engines.

using System.Collections.Generic;
using Voxelforge.Stirling;

namespace Voxelforge.Integration.Components;

/// <summary>α / β / γ Stirling engine adapter.</summary>
/// <remarks>
/// Accuracy caveat: the power outputs surfaced here (<c>IndicatedPower_W</c>,
/// <c>HeatInputRate_W</c>, <c>HeatRejectionRate_W</c>) inherit the Wave-1
/// Stirling MEP fit's known 10–100× over-prediction of free-piston power —
/// order-of-magnitude only (LIMITATIONS.md, "Validated free-piston Stirling
/// output"). Downstream System-Integration / Economics consumers should treat
/// them as unvalidated screening estimates.
/// </remarks>
internal sealed class StirlingComponent : SystemComponent
{
    private readonly StirlingDesign _design;

    public StirlingComponent(string name, StirlingDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "HotSideTemperature_K", "ColdSideTemperature_K" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "IndicatedPower_W", "IndicatedEfficiency",
            "HeatInputRate_W", "HeatRejectionRate_W",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            HotSideTemperature_K  = inputs["HotSideTemperature_K"],
            ColdSideTemperature_K = inputs["ColdSideTemperature_K"],
        };
        var r = StirlingSolver.Solve(d);
        // Accuracy caveat (LIMITATIONS.md, "Validated free-piston Stirling
        // output"): the Wave-1 MEP fit over-predicts free-piston power by
        // 10–100×, so IndicatedPower_W and the two heat-rate outputs below
        // are order-of-magnitude screening values only, not validated numbers.
        outputs["IndicatedPower_W"]     = r.IndicatedPower_W;
        outputs["IndicatedEfficiency"]  = r.IndicatedEfficiency;
        outputs["HeatInputRate_W"]      = r.HeatInputRate_W;
        outputs["HeatRejectionRate_W"]  = r.HeatRejectionRate_W;
    }
}
