// PhotovoltaicComponent.cs — Sprint SI.W2 adapter for PV panel.

using System.Collections.Generic;
using Voxelforge.Photovoltaic;

namespace Voxelforge.Integration.Components;

/// <summary>PV panel adapter.</summary>
internal sealed class PhotovoltaicComponent : SystemComponent
{
    private readonly PvPanelDesign _design;

    public PhotovoltaicComponent(string name, PvPanelDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "Irradiance_W_m2", "CellTemperature_C" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "MaxPower_W", "MaxPowerPointVoltage_V", "MaxPowerPointCurrent_A",
            "ConversionEfficiency",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            Irradiance_W_m2    = inputs["Irradiance_W_m2"],
            CellTemperature_C  = inputs["CellTemperature_C"],
        };
        var r = PvPanelSolver.Solve(d);
        outputs["MaxPower_W"]              = r.MaxPower_W;
        outputs["MaxPowerPointVoltage_V"]  = r.MaxPowerPointVoltage_V;
        outputs["MaxPowerPointCurrent_A"]  = r.MaxPowerPointCurrent_A;
        outputs["ConversionEfficiency"]    = r.ConversionEfficiency;
    }
}
