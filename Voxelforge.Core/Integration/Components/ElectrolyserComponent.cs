// ElectrolyserComponent.cs — Sprint SI.W2 adapter for PEM electrolyser.

using System.Collections.Generic;
using Voxelforge.Electrolyser;

namespace Voxelforge.Integration.Components;

/// <summary>PEM electrolyser stack adapter.</summary>
internal sealed class ElectrolyserComponent : SystemComponent
{
    private readonly PemElectrolyserDesign _design;

    public ElectrolyserComponent(string name, PemElectrolyserDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "OperatingCurrentDensity_A_cm2" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StackElectricPower_W", "StackVoltage_V",
            "HydrogenProductionRate_kgs", "HydrogenProductionRate_Nm3_h",
            "HhvEfficiency",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with { OperatingCurrentDensity_A_cm2 = inputs["OperatingCurrentDensity_A_cm2"] };
        var r = PemElectrolyserSolver.Solve(d);
        outputs["StackElectricPower_W"]        = r.StackElectricPower_W;
        outputs["StackVoltage_V"]              = r.StackVoltage_V;
        outputs["HydrogenProductionRate_kgs"]  = r.HydrogenProductionRate_kgs;
        outputs["HydrogenProductionRate_Nm3_h"] = r.HydrogenProductionRate_Nm3_h;
        outputs["HhvEfficiency"]               = r.HhvEfficiency;
    }
}
