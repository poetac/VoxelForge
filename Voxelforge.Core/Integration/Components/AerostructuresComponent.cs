// AerostructuresComponent.cs — Sprint SI.W2 adapter for wing spars.

using System.Collections.Generic;
using Voxelforge.Aerostructures;

namespace Voxelforge.Integration.Components;

/// <summary>Euler-Bernoulli wing-spar adapter.</summary>
internal sealed class AerostructuresComponent : SystemComponent
{
    private readonly WingSparDesign _design;

    public AerostructuresComponent(string name, WingSparDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "DistributedLift_Nm", "LoadFactor" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "MaximumBendingStress_Pa", "TipDeflection_m",
            "SafetyFactor", "SparMass_kg",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            DistributedLift_Nm = inputs["DistributedLift_Nm"],
            LoadFactor         = inputs["LoadFactor"],
        };
        var r = WingSparSolver.Solve(d);
        outputs["MaximumBendingStress_Pa"] = r.MaximumBendingStress_Pa;
        outputs["TipDeflection_m"]         = r.TipDeflection_m;
        outputs["SafetyFactor"]            = r.SafetyFactor;
        outputs["SparMass_kg"]             = r.SparMass_kg;
    }
}
