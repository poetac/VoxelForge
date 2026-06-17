// TankageComponent.cs — Sprint SI.W2 adapter for cylindrical pressure vessels.

using System.Collections.Generic;
using Voxelforge.Tankage;

namespace Voxelforge.Integration.Components;

/// <summary>Cylindrical pressure-vessel adapter.</summary>
internal sealed class TankageComponent : SystemComponent
{
    private readonly PressureVesselDesign _design;

    public TankageComponent(string name, PressureVesselDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "OperatingPressure_Pa" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "HoopStress_Pa", "VonMisesStress_Pa", "SafetyFactor",
            "ShellMass_kg", "GravimetricEfficiency",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with { OperatingPressure_Pa = inputs["OperatingPressure_Pa"] };
        var r = PressureVesselSolver.Solve(d);
        outputs["HoopStress_Pa"]          = r.HoopStress_Pa;
        outputs["VonMisesStress_Pa"]      = r.VonMisesStress_Pa;
        outputs["SafetyFactor"]           = r.SafetyFactor;
        outputs["ShellMass_kg"]           = r.ShellMass_kg;
        outputs["GravimetricEfficiency"]  = r.GravimetricEfficiency;
    }
}
