// RadiatorComponent.cs — Sprint SI.W2 adapter for spacecraft radiator.

using System.Collections.Generic;
using Voxelforge.Radiator;

namespace Voxelforge.Integration.Components;

/// <summary>Spacecraft flat-panel / two-sided deployable radiator adapter.</summary>
internal sealed class RadiatorComponent : SystemComponent
{
    private readonly SpacecraftRadiatorDesign _design;

    public RadiatorComponent(string name, SpacecraftRadiatorDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "OperatingTemperature_K" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "NetHeatRejectionRate_W", "GrossRadiatedHeat_W",
            "HeatRejectionDensity_W_m2",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with { OperatingTemperature_K = inputs["OperatingTemperature_K"] };
        var r = SpacecraftRadiatorSolver.Solve(d);
        outputs["NetHeatRejectionRate_W"]   = r.NetHeatRejectionRate_W;
        outputs["GrossRadiatedHeat_W"]      = r.GrossRadiatedHeat_W;
        outputs["HeatRejectionDensity_W_m2"] = r.HeatRejectionDensity_W_m2;
    }
}
