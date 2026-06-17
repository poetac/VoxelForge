// AntennaLinkComponent.cs — Sprint SI.W2 adapter for RF link budget.

using System.Collections.Generic;
using Voxelforge.Antenna;

namespace Voxelforge.Integration.Components;

/// <summary>Friis-equation RF link adapter.</summary>
internal sealed class AntennaLinkComponent : SystemComponent
{
    private readonly AntennaLinkDesign _design;

    public AntennaLinkComponent(string name, AntennaLinkDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "TransmitPower_W", "LinkDistance_m" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ReceivedPower_W", "ReceivedPower_dBm",
            "FreeSpacePathLoss_dB", "EffectiveIsotropicRadiatedPower_dBW",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            TransmitPower_W = inputs["TransmitPower_W"],
            LinkDistance_m  = inputs["LinkDistance_m"],
        };
        var r = AntennaSolver.Solve(d);
        outputs["ReceivedPower_W"]                    = r.ReceivedPower_W;
        outputs["ReceivedPower_dBm"]                  = r.ReceivedPower_dBm;
        outputs["FreeSpacePathLoss_dB"]               = r.FreeSpacePathLoss_dB;
        outputs["EffectiveIsotropicRadiatedPower_dBW"] = r.EffectiveIsotropicRadiatedPower_dBW;
    }
}
