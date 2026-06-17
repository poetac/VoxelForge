// HydroelectricComponent.cs — Sprint SI.W2 adapter for hydro turbines.

using System.Collections.Generic;
using Voxelforge.Hydroelectric;

namespace Voxelforge.Integration.Components;

/// <summary>Pelton / Francis / Kaplan hydro turbine adapter.</summary>
internal sealed class HydroelectricComponent : SystemComponent
{
    private readonly HydroTurbineDesign _design;

    public HydroelectricComponent(string name, HydroTurbineDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "Head_m", "VolumetricFlowRate_m3s" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ElectricalPower_W", "HydraulicPower_W", "ShaftPower_W",
            "OverallEfficiency",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            Head_m                 = inputs["Head_m"],
            VolumetricFlowRate_m3s = inputs["VolumetricFlowRate_m3s"],
        };
        var r = HydroTurbineSolver.Solve(d);
        outputs["ElectricalPower_W"] = r.ElectricalPower_W;
        outputs["HydraulicPower_W"]  = r.HydraulicPower_W;
        outputs["ShaftPower_W"]      = r.ShaftPower_W;
        outputs["OverallEfficiency"] = r.OverallEfficiency;
    }
}
