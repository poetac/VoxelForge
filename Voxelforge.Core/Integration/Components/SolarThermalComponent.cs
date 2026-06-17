// SolarThermalComponent.cs — Sprint SI.W2 adapter for solar collectors.

using System.Collections.Generic;
using Voxelforge.SolarThermal;

namespace Voxelforge.Integration.Components;

/// <summary>Flat-plate / parabolic-trough / evacuated-tube solar collector adapter.</summary>
internal sealed class SolarThermalComponent : SystemComponent
{
    private readonly SolarCollectorDesign _design;

    public SolarThermalComponent(string name, SolarCollectorDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "DirectNormalIrradiance_W_m2", "CollectorTemperature_C", "AmbientTemperature_C" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "UsefulHeatPower_W", "CollectorEfficiency",
            "ThermalLossPower_W", "AbsorbedSolarPower_W",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            DirectNormalIrradiance_W_m2 = inputs["DirectNormalIrradiance_W_m2"],
            CollectorTemperature_C      = inputs["CollectorTemperature_C"],
            AmbientTemperature_C        = inputs["AmbientTemperature_C"],
        };
        var r = SolarCollectorSolver.Solve(d);
        outputs["UsefulHeatPower_W"]    = r.UsefulHeatPower_W;
        outputs["CollectorEfficiency"]  = r.CollectorEfficiency;
        outputs["ThermalLossPower_W"]   = r.ThermalLossPower_W;
        outputs["AbsorbedSolarPower_W"] = r.AbsorbedSolarPower_W;
    }
}
