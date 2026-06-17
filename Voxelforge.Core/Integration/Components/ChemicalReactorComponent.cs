// ChemicalReactorComponent.cs — Sprint SI.W2 adapter for CSTR / PFR / Batch.

using System.Collections.Generic;
using Voxelforge.Chemical;

namespace Voxelforge.Integration.Components;

/// <summary>Ideal first-/second-order chemical reactor adapter.</summary>
internal sealed class ChemicalReactorComponent : SystemComponent
{
    private readonly ReactorDesign _design;

    public ChemicalReactorComponent(string name, ReactorDesign design) : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "OperatingTemperature_K", "VolumetricFlowRate_m3s", "InletConcentration_mol_m3" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "Conversion", "OutletConcentration_mol_m3",
            "ProductFormationRate_mol_s", "DamkohlerNumber",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var d = _design with
        {
            OperatingTemperature_K     = inputs["OperatingTemperature_K"],
            VolumetricFlowRate_m3s     = inputs["VolumetricFlowRate_m3s"],
            InletConcentration_mol_m3  = inputs["InletConcentration_mol_m3"],
        };
        var r = ReactorSolver.Solve(d);
        outputs["Conversion"]                 = r.Conversion;
        outputs["OutletConcentration_mol_m3"] = r.OutletConcentration_mol_m3;
        outputs["ProductFormationRate_mol_s"] = r.ProductFormationRate_mol_s;
        outputs["DamkohlerNumber"]            = r.DamkohlerNumber;
    }
}
