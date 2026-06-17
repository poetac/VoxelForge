// PowerGenComponent.cs — Sprint SI.W2 adapter for PEM fuel cell.

using System.Collections.Generic;
using Voxelforge.PowerGen;

namespace Voxelforge.Integration.Components;

/// <summary>PEM fuel cell stack adapter.</summary>
internal sealed class PowerGenComponent : SystemComponent
{
    private readonly PemFuelCellDesign _design;

    public PowerGenComponent(string name, PemFuelCellDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "OperatingCurrentDensity_A_cm2" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[] { "StackElectricPower_W", "StackVoltage_V", "HeatRejectionPower_W", "LhvEfficiency" };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with { OperatingCurrentDensity_A_cm2 = inputs["OperatingCurrentDensity_A_cm2"] };
        var r = PemFuelCellSolver.Solve(d);
        outputs["StackElectricPower_W"] = r.StackElectricPower_W;
        outputs["StackVoltage_V"]       = r.StackVoltage_V;
        outputs["HeatRejectionPower_W"] = r.HeatRejectionPower_W;
        outputs["LhvEfficiency"]        = r.LhvEfficiency;
    }
}
