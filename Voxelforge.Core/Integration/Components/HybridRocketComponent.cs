// HybridRocketComponent.cs — Sprint SI.W2 adapter for hybrid rocket
// (LOX + HTPB / paraffin grain). Cross-pillar bridge: rocket-class
// thrust output feeds e.g. a payload-mass + Δv calculation that
// could be wired into a mission-analysis subsystem.

using System.Collections.Generic;
using Voxelforge.Hybrid;

namespace Voxelforge.Integration.Components;

/// <summary>LOX/HTPB hybrid rocket adapter.</summary>
internal sealed class HybridRocketComponent : SystemComponent
{
    private readonly HybridRocketDesign _design;

    public HybridRocketComponent(string name, HybridRocketDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "PortRadius_m" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "VacuumThrust_N", "VacuumIsp_s",
            "TotalMassFlow_kgs", "OxidiserFuelRatio",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var r = HybridRocketCycleSolver.Solve(_design, inputs["PortRadius_m"]);
        outputs["VacuumThrust_N"]    = r.VacuumThrust_N;
        outputs["VacuumIsp_s"]       = r.VacuumIsp_s;
        outputs["TotalMassFlow_kgs"] = r.TotalMassFlow_kgs;
        outputs["OxidiserFuelRatio"] = r.OxidiserFuelRatio;
    }
}
