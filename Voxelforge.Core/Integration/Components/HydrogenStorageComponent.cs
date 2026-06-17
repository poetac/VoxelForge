// HydrogenStorageComponent.cs — Sprint SI.W2 adapter for H₂ tanks.

using System.Collections.Generic;
using Voxelforge.HydrogenStorage;

namespace Voxelforge.Integration.Components;

/// <summary>Compressed-gas / cryogenic / metal-hydride H₂ tank adapter.</summary>
internal sealed class HydrogenStorageComponent : SystemComponent
{
    private readonly HydrogenStorageDesign _design;

    public HydrogenStorageComponent(string name, HydrogenStorageDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    // No input ports — the tank is fully specified at construction.
    // (Future SI.W4 could expose HydrogenInflowRate_kgs / OutflowRate
    //  as a time-domain mass-balance integrator.)
    public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StoredHydrogenMass_kg", "StoredHydrogenEnergy_kWh",
            "GravimetricEfficiency", "BoilOffRate_kgs",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var r = HydrogenStorageSolver.Solve(_design);
        outputs["StoredHydrogenMass_kg"]    = r.StoredHydrogenMass_kg;
        outputs["StoredHydrogenEnergy_kWh"] = r.StoredHydrogenEnergy_kWh;
        outputs["GravimetricEfficiency"]    = r.GravimetricEfficiency;
        outputs["BoilOffRate_kgs"]          = r.BoilOffRate_kgs;
    }
}
