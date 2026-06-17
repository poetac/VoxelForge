// DcDcConverterComponent.cs — Sprint SI.W11 DC-DC converter adapter.
//
// Models an idealised buck / boost converter that decouples input
// voltage from output voltage and conserves power (minus efficiency):
//
//   P_out = η · P_in
//   I_in  = (V_out · I_out) / (η · V_in)
//
// Inputs:
//   InputVoltage_V    — DC bus voltage upstream (e.g. from battery).
//   OutputVoltage_V   — commanded DC bus voltage downstream.
//   OutputCurrent_A   — load current drawn by downstream component.
//
// Outputs:
//   InputCurrent_A      — actual current drawn from the upstream bus.
//   PowerLoss_W         — η-related loss.
//   ConversionEfficiency — echoes design η.
//
// Used in subsystems where the battery pack and motor operate at
// independent voltages — the converter handles the buck/boost.

using System;
using System.Collections.Generic;

namespace Voxelforge.Integration.Components;

/// <summary>Buck/boost DC-DC converter adapter (Sprint SI.W11).</summary>
internal sealed class DcDcConverterComponent : SystemComponent
{
    private readonly double _efficiency;

    /// <summary>Create a DC-DC converter.</summary>
    /// <param name="name">Network-unique component name.</param>
    /// <param name="conversionEfficiency">η ∈ (0, 1] [-]. Modern
    /// silicon-carbide automotive converters cluster 0.96-0.98; older
    /// silicon designs 0.90-0.94.</param>
    public DcDcConverterComponent(string name, double conversionEfficiency)
        : base(name)
    {
        if (conversionEfficiency <= 0 || conversionEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(conversionEfficiency),
                "η must be in (0, 1].");
        _efficiency = conversionEfficiency;
    }

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "InputVoltage_V", "OutputVoltage_V", "OutputCurrent_A" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[] { "InputCurrent_A", "PowerLoss_W", "ConversionEfficiency" };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        double V_in  = inputs["InputVoltage_V"];
        double V_out = inputs["OutputVoltage_V"];
        double I_out = inputs["OutputCurrent_A"];
        if (V_in <= 0)
            throw new InvalidOperationException(
                "DC-DC converter: InputVoltage_V must be > 0.");

        double P_out = V_out * I_out;
        double P_in  = P_out / _efficiency;
        double I_in  = P_in / V_in;
        double loss  = P_in - P_out;

        outputs["InputCurrent_A"]       = I_in;
        outputs["PowerLoss_W"]          = loss;
        outputs["ConversionEfficiency"] = _efficiency;
    }
}
