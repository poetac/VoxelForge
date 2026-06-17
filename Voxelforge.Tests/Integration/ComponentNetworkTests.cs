// ComponentNetworkTests.cs — Sprint SI.W1 unit tests for the
// ComponentNetwork sequential evaluator + the Battery + Motor
// component adapters. The headline test wires a Tesla-Model-S-class
// battery pack into a Tesla-class drive-unit motor as a single-point
// EV-powertrain analysis — the first demonstration of cross-pillar
// integration in the architecture.

using System;
using System.Collections.Generic;
using Voxelforge.Battery;
using Voxelforge.ElectricMotor;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class ComponentNetworkTests
{
    // ── Validation surface ───────────────────────────────────────────────

    [Fact]
    public void Network_RejectsDuplicateComponentNames()
    {
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack", ModelSPack()));
        Assert.Throws<InvalidOperationException>(
            () => network.Add(new BatteryComponent("pack", ModelSPack())));
    }

    [Fact]
    public void Solve_FailsWhenInputPortHasNoFeed()
    {
        var network = new ComponentNetwork();
        network.Add(new MotorComponent("motor", ModelSMotor()));
        // No external feed and no connection — Solve must raise.
        Assert.Throws<InvalidOperationException>(() => network.Solve());
    }

    [Fact]
    public void Solve_FailsOnInvalidPortReference()
    {
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack", ModelSPack()));
        network.Add(new MotorComponent("motor", ModelSMotor()));
        // Bogus port name → must raise.
        network.Connect("pack", "PortDoesNotExist", "motor", "BusVoltage_V");
        Assert.Throws<InvalidOperationException>(() => network.Solve());
    }

    [Fact]
    public void Solve_FailsOnCyclicGraph()
    {
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack",  ModelSPack()));
        network.Add(new BatteryComponent("pack2", ModelSPack()));
        // Construct a self-referential cycle: pack -> pack2 -> pack.
        // (Bogus physically but ComponentConnection doesn't care; we
        // just want to exercise the cycle detector.)
        network.Connect("pack",  "PackLoadedVoltage_V", "pack2", "LoadCurrent_A");
        network.Connect("pack2", "PackLoadedVoltage_V", "pack",  "LoadCurrent_A");
        // ThrowsAny<> rather than Throws<>: per #490, the concrete exception is
        // CyclicComponentNetworkException : InvalidOperationException, and xUnit's
        // strict Throws<T> rejects subclasses.
        Assert.ThrowsAny<InvalidOperationException>(() => network.Solve());
    }

    [Fact]
    public void Solve_OnCyclicGraph_ThrowsTypedCyclicException()
    {
        // B.8a / issue #490 — pin the typed exception contract. The
        // base test above asserts InvalidOperationException for back-
        // compat; this test pins that the concrete type emitted is
        // CyclicComponentNetworkException so NetworkValidator can catch
        // it without string-matching the exception message.
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack",  ModelSPack()));
        network.Add(new BatteryComponent("pack2", ModelSPack()));
        network.Connect("pack",  "PackLoadedVoltage_V", "pack2", "LoadCurrent_A");
        network.Connect("pack2", "PackLoadedVoltage_V", "pack",  "LoadCurrent_A");
        var ex = Assert.Throws<CyclicComponentNetworkException>(() => network.Solve());
        // The exception is a kind of InvalidOperationException (so
        // existing callers that catch the base type still work).
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void GetTopologicalOrder_OnCyclicGraph_ThrowsTypedCyclicException()
    {
        // The cycle-detection exception is also thrown by the public
        // GetTopologicalOrder() helper — the path NetworkValidator
        // exercises directly.
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack",  ModelSPack()));
        network.Add(new BatteryComponent("pack2", ModelSPack()));
        network.Connect("pack",  "PackLoadedVoltage_V", "pack2", "LoadCurrent_A");
        network.Connect("pack2", "PackLoadedVoltage_V", "pack",  "LoadCurrent_A");
        Assert.Throws<CyclicComponentNetworkException>(() => network.GetTopologicalOrder());
    }

    // ── Single-component sanity ─────────────────────────────────────────

    [Fact]
    public void Solve_SingleBattery_WithExternalLoadCurrent()
    {
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack", ModelSPack()));
        network.SetExternalInput("pack", "LoadCurrent_A", 200.0);
        var result = network.Solve();
        Assert.Single(result);
        Assert.Contains("pack", result.Keys);
        // ~ 400 V pack at 200 A → ~ 80 kW power (cruise-class).
        double power = result["pack"]["PackElectricalPower_W"];
        Assert.InRange(power, 60_000.0, 85_000.0);
    }

    // ── EV-powertrain headline demo ─────────────────────────────────────

    [Fact]
    public void EvPowertrain_BatteryPlusMotor_ProducesCoherentTorqueAndSpeed()
    {
        // Tesla-Model-S-class battery + Tesla drive-unit-class motor.
        // Throttle command = ArmatureCurrent_A = 100 A (cruise).
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack",  ModelSPack()));
        network.Add(new MotorComponent("motor",  ModelSMotor()));

        // Wire: battery PackLoadedVoltage_V → motor BusVoltage_V.
        network.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");

        // External inputs: armature current goes to BOTH the motor (as
        // its operating I_a) AND the battery (as its discharge current),
        // because we don't have a DC-DC stage in this Wave-1 EV model.
        network.SetExternalInput("pack",  "LoadCurrent_A",      100.0);
        network.SetExternalInput("motor", "ArmatureCurrent_A",  100.0);

        var result = network.Solve();

        // Motor outputs at I_a = 100 A:
        //   τ = K_t · I = 0.5 · 100 = 50 N·m  (matches W1 cruise baseline)
        Assert.Equal(50.0, result["motor"]["ShaftTorque_Nm"], precision: 6);
        // P_mech ≈ 39 kW @ ω ≈ 790 rad/s.
        Assert.InRange(result["motor"]["MechanicalPower_W"],   30_000.0, 50_000.0);
        Assert.InRange(result["motor"]["RotationSpeed_rpm"],    5_000.0,  9_000.0);
        Assert.InRange(result["motor"]["MotorEfficiency"],          0.90,    0.99);

        // Battery outputs are consistent with the wired bus voltage.
        Assert.True(result["pack"]["PackLoadedVoltage_V"]   > 0);
        Assert.True(result["pack"]["PackElectricalPower_W"] > 0);
    }

    [Fact]
    public void EvPowertrain_MotorBusVoltage_MatchesBatteryPackVoltage()
    {
        // Verify the connection actually propagates the wired value.
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack", ModelSPack()));
        network.Add(new MotorComponent("motor", ModelSMotor()));
        network.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");
        network.SetExternalInput("pack",  "LoadCurrent_A",     50.0);
        network.SetExternalInput("motor", "ArmatureCurrent_A", 50.0);
        var result = network.Solve();
        double batteryV = result["pack"]["PackLoadedVoltage_V"];
        // Motor's operating point at this BusVoltage must reflect what
        // the battery supplies (not the standalone-motor 400 V default).
        // Motor MotorEfficiency uses the BusVoltage in its energy
        // balance; cross-check by re-solving the motor standalone with
        // the same V_bus.
        var standalone = MotorSolver.Solve(ModelSMotor() with
        {
            BusVoltage_V      = batteryV,
            ArmatureCurrent_A = 50.0,
        });
        Assert.Equal(standalone.RotationSpeed_rpm,
                     result["motor"]["RotationSpeed_rpm"], precision: 4);
    }

    [Fact]
    public void EvPowertrain_HigherThrottle_HigherTorque_LowerEfficiency()
    {
        // Sweep throttle current: torque should rise (τ ∝ I) and motor
        // efficiency should drop (copper-loss I²R grows faster than
        // linear).
        double cruiseT = TorqueAt(armatureCurrent_A: 100.0);
        double peakT   = TorqueAt(armatureCurrent_A: 400.0);
        double cruiseEta = EfficiencyAt(armatureCurrent_A: 100.0);
        double peakEta   = EfficiencyAt(armatureCurrent_A: 400.0);
        Assert.Equal(4.0, peakT / cruiseT, precision: 4);
        Assert.True(peakEta < cruiseEta);

        static double TorqueAt(double armatureCurrent_A)
        {
            var n = new ComponentNetwork();
            n.Add(new BatteryComponent("pack", ModelSPack()));
            n.Add(new MotorComponent("motor", ModelSMotor()));
            n.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");
            n.SetExternalInput("pack",  "LoadCurrent_A",     armatureCurrent_A);
            n.SetExternalInput("motor", "ArmatureCurrent_A", armatureCurrent_A);
            return n.Solve()["motor"]["ShaftTorque_Nm"];
        }

        static double EfficiencyAt(double armatureCurrent_A)
        {
            var n = new ComponentNetwork();
            n.Add(new BatteryComponent("pack", ModelSPack()));
            n.Add(new MotorComponent("motor", ModelSMotor()));
            n.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");
            n.SetExternalInput("pack",  "LoadCurrent_A",     armatureCurrent_A);
            n.SetExternalInput("motor", "ArmatureCurrent_A", armatureCurrent_A);
            return n.Solve()["motor"]["MotorEfficiency"];
        }
    }

    [Fact]
    public void Network_ComponentCount_AndConnectionCount_Accurate()
    {
        var network = new ComponentNetwork();
        Assert.Equal(0, network.ComponentCount);
        Assert.Equal(0, network.ConnectionCount);
        network.Add(new BatteryComponent("pack", ModelSPack()));
        network.Add(new MotorComponent("motor", ModelSMotor()));
        Assert.Equal(2, network.ComponentCount);
        network.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");
        Assert.Equal(1, network.ConnectionCount);
    }

    [Fact]
    public void ExternalInput_OverridesInternalConnection_WhenBothPresent()
    {
        // External input takes precedence over connection: handy for
        // overriding a wired-in value at the system level (e.g.
        // injecting a fault / setpoint override during analysis).
        var network = new ComponentNetwork();
        network.Add(new BatteryComponent("pack",  ModelSPack()));
        network.Add(new MotorComponent("motor", ModelSMotor()));
        network.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");
        network.SetExternalInput("pack",  "LoadCurrent_A",     100.0);
        network.SetExternalInput("motor", "ArmatureCurrent_A", 100.0);
        // Override the wired BusVoltage_V with 250 V (vs ~ 390 V from
        // the pack).
        network.SetExternalInput("motor", "BusVoltage_V", 250.0);
        var result = network.Solve();
        // Motor ω at V_bus = 250 V should be lower than at ~ 390 V.
        var baseline = MotorSolver.Solve(ModelSMotor() with
        {
            BusVoltage_V       = 250.0,
            ArmatureCurrent_A  = 100.0,
        });
        Assert.Equal(baseline.RotationSpeed_rpm,
                     result["motor"]["RotationSpeed_rpm"], precision: 4);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);

    private static MotorDesign ModelSMotor() => new(
        Kind:                      MotorKind.PermanentMagnetSynchronous,
        TorqueConstant_NmA:        0.5,
        ArmatureResistance_Ohm:    0.05,
        ConstantPowerLoss_W:       500.0,
        BusVoltage_V:              400.0,
        ArmatureCurrent_A:         100.0);
}
