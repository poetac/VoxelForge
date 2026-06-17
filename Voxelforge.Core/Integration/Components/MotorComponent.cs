// MotorComponent.cs — Sprint SI.W1 SystemComponent adapter for the
// Electric Motor pillar's MotorSolver.
//
// Inputs:
//   BusVoltage_V       — DC link voltage [V] (typically wired from the
//                        battery pack's PackLoadedVoltage_V).
//   ArmatureCurrent_A  — operating current [A] (external system command).
//
// Outputs:
//   ShaftTorque_Nm
//   AngularVelocity_rads
//   RotationSpeed_rpm
//   MechanicalPower_W
//   MotorEfficiency

using System.Collections.Generic;
using Voxelforge.ElectricMotor;

namespace Voxelforge.Integration.Components;

/// <summary>
/// <see cref="SystemComponent"/> adapter wrapping
/// <c>MotorSolver.Solve</c> (Sprint SI.W1).
/// </summary>
internal sealed class MotorComponent : SystemComponent
{
    private readonly MotorDesign _design;

    /// <summary>Create a motor component bound to a fixed design.</summary>
    /// <param name="name">Network-unique component name.</param>
    /// <param name="design">Motor design. BusVoltage_V + ArmatureCurrent_A
    /// are overridden at each Evaluate by the values flowing in through
    /// the input ports.</param>
    public MotorComponent(string name, MotorDesign design)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        _design = design;
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "BusVoltage_V", "ArmatureCurrent_A" };

    /// <inheritdoc />
    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "ShaftTorque_Nm",
            "AngularVelocity_rads",
            "RotationSpeed_rpm",
            "MechanicalPower_W",
            "MotorEfficiency",
        };

    /// <inheritdoc />
    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        var designAtTick = _design with
        {
            BusVoltage_V       = inputs["BusVoltage_V"],
            ArmatureCurrent_A  = inputs["ArmatureCurrent_A"],
        };
        var result = MotorSolver.Solve(designAtTick);
        outputs["ShaftTorque_Nm"]        = result.ShaftTorque_Nm;
        outputs["AngularVelocity_rads"]  = result.AngularVelocity_rads;
        outputs["RotationSpeed_rpm"]     = result.RotationSpeed_rpm;
        outputs["MechanicalPower_W"]     = result.MechanicalPower_W;
        outputs["MotorEfficiency"]       = result.MotorEfficiency;
    }
}
