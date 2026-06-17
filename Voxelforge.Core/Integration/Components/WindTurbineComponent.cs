// WindTurbineComponent.cs — Sprint SI.W2 adapter for HAWT/VAWT.

using System.Collections.Generic;
using Voxelforge.WindTurbine;

namespace Voxelforge.Integration.Components;

/// <summary>HAWT / VAWT wind turbine adapter.</summary>
internal sealed class WindTurbineComponent : SystemComponent
{
    private readonly HawtDesign _design;

    public WindTurbineComponent(string name, HawtDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "WindSpeed_ms" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ElectricalPower_W", "RotorPower_W", "RotorThrust_N",
            "PowerCoefficient", "RotationSpeed_rpm",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var r = HawtSolver.Solve(_design, windSpeed_ms: inputs["WindSpeed_ms"]);
        outputs["ElectricalPower_W"] = r.ElectricalPower_W;
        outputs["RotorPower_W"]      = r.RotorPower_W;
        outputs["RotorThrust_N"]     = r.RotorThrust_N;
        outputs["PowerCoefficient"]  = r.PowerCoefficient;
        outputs["RotationSpeed_rpm"] = r.RotationSpeed_rpm;
    }
}
