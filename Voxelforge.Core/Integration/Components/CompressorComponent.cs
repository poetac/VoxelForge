// CompressorComponent.cs — Sprint SI.W2 adapter for centrifugal / axial compressors.

using System.Collections.Generic;
using Voxelforge.Compressor;

namespace Voxelforge.Integration.Components;

/// <summary>Centrifugal / axial-flow compressor adapter.</summary>
internal sealed class CompressorComponent : SystemComponent
{
    private readonly CentrifugalCompressorDesign _design;

    public CompressorComponent(string name, CentrifugalCompressorDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "MassFlow_kgs", "InletTotalTemperature_K", "InletTotalPressure_Pa", "PressureRatio" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ShaftPowerInput_W", "ActualExitTemperature_K", "ExitTotalPressure_Pa",
            "SpecificWork_J_kg",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            MassFlow_kgs             = inputs["MassFlow_kgs"],
            InletTotalTemperature_K  = inputs["InletTotalTemperature_K"],
            InletTotalPressure_Pa    = inputs["InletTotalPressure_Pa"],
            PressureRatio            = inputs["PressureRatio"],
        };
        var r = CentrifugalCompressorSolver.Solve(d);
        outputs["ShaftPowerInput_W"]        = r.ShaftPowerInput_W;
        outputs["ActualExitTemperature_K"]  = r.ActualExitTemperature_K;
        outputs["ExitTotalPressure_Pa"]     = r.ExitTotalPressure_Pa;
        outputs["SpecificWork_J_kg"]        = r.SpecificWork_J_kg;
    }
}
