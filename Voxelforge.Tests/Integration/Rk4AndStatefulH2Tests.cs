// Rk4AndStatefulH2Tests.cs — Sprint SI.W6 unit tests for the RK4
// integrator + the StatefulHydrogenStorageComponent.

using System;
using System.Collections.Generic;
using System.Linq;
using Voxelforge.HydrogenStorage;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class Rk4AndStatefulH2Tests
{
    // ── RK4 vs Euler accuracy on exponential decay ──────────────────────

    private sealed class ExponentialDecay : SystemComponent, IStatefulComponent
    {
        private readonly double _decayRate;
        private double _y;
        private readonly double _initialY;

        public ExponentialDecay(string name, double decayRate, double initialY)
            : base(name)
        {
            _decayRate = decayRate;
            _initialY  = initialY;
            _y         = initialY;
        }

        public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "y" };
        public override void Evaluate(
            IReadOnlyDictionary<string, double> _,
            IDictionary<string, double> outputs) => outputs["y"] = _y;

        public IReadOnlyList<string> StateVariables { get; } = new[] { "y" };
        public void ComputeDerivatives(
            ReadOnlySpan<double> state,
            IReadOnlyDictionary<string, double> _,
            IReadOnlyDictionary<string, double> __,
            Span<double> derivatives)
            => derivatives[0] = -_decayRate * state[0];
        public void GetInitialState(Span<double> destination) => destination[0] = _initialY;
        public void GetCurrentState(Span<double> destination) => destination[0] = _y;
        public void SetState(ReadOnlySpan<double> state) => _y = state[0];
    }

    [Fact]
    public void Rk4_BeatsEuler_OnExponentialDecay_LargeDt()
    {
        // Analytical: y(1) = 100 · e^(-1) = 36.7879.
        // dt = 0.1 is "large" for Euler (5 % per-step error compounds).
        var net = new ComponentNetwork();
        var decayEuler = new ExponentialDecay("d", 1.0, 100.0);
        net.Add(decayEuler);
        var euler = new TimeStepIntegrator(net);
        euler.RegisterStateful("d", decayEuler);
        var hEuler = euler.Run(0.0, 1.0, 0.1, method: IntegrationMethod.ExplicitEuler);

        var net2 = new ComponentNetwork();
        var decayRk4 = new ExponentialDecay("d", 1.0, 100.0);
        net2.Add(decayRk4);
        var rk4 = new TimeStepIntegrator(net2);
        rk4.RegisterStateful("d", decayRk4);
        var hRk4 = rk4.Run(0.0, 1.0, 0.1, method: IntegrationMethod.Rk4);

        double truth = 100.0 * Math.Exp(-1.0);
        double eulerErr = Math.Abs(hEuler[^1].PortValues["d"]["y"] - truth);
        double rk4Err   = Math.Abs(hRk4[^1].PortValues["d"]["y"]   - truth);

        // RK4 must be at least 100× more accurate at dt = 0.1.
        Assert.True(rk4Err < eulerErr / 100.0,
            $"RK4 error ({rk4Err:E3}) expected ≤ Euler error ({eulerErr:E3}) / 100.");
    }

    [Fact]
    public void Rk4_OnExponentialDecay_ConvergesToAnalytical_LargeDt()
    {
        // RK4 at dt = 0.1 (10 ticks) should already converge to ~ 4 decimals.
        var net = new ComponentNetwork();
        var decay = new ExponentialDecay("d", 1.0, 100.0);
        net.Add(decay);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("d", decay);
        var hist = integrator.Run(0.0, 1.0, 0.1, method: IntegrationMethod.Rk4);
        double truth = 100.0 * Math.Exp(-1.0);
        Assert.Equal(truth, hist[^1].PortValues["d"]["y"], precision: 3);
    }

    // ── Stateful H₂ tank — fill curve ───────────────────────────────────

    [Fact]
    public void StatefulH2Tank_FillsLinearly_AtConstantInflow()
    {
        // 1 kg/s inflow into a 0-kg-initial tank over 5 seconds → 5 kg.
        var net = new ComponentNetwork();
        var tank = new StatefulHydrogenStorageComponent("tank",
            new HydrogenStorageDesign(
                Kind: HydrogenStorageKind.CompressedGas,
                InternalVolume_m3: 0.122,
                OperatingPressure_bar: 700.0,
                OperatingTemperature_K: 298.15,
                DryMass_kg: 95.0),
            initialStoredMass_kg: 0.0);
        net.Add(tank);
        net.SetExternalInput("tank", "HydrogenInflowRate_kgs", 1.0);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("tank", tank);
        var hist = integrator.Run(0.0, 5.0, 0.1);

        // Final mass ≈ 5 kg. Under the #553 closed [t0, tEnd] N+1
        // contract the last snapshot lands at t = 5.0, so the integrated
        // mass should be very close to the analytical 5.0 kg.
        double finalMass = hist[^1].PortValues["tank"]["StoredHydrogenMass_kg"];
        Assert.InRange(finalMass, 4.8, 5.0);
    }

    [Fact]
    public void StatefulH2Tank_BoilOffDrainsCryoTank()
    {
        // No inflow, but a cryo tank with a 1 W heat leak: dm/dt = -1/446000
        // ≈ -2.24e-6 kg/s. Over 1000 s: ~ 2.24e-3 kg ≈ 2.24 g loss.
        var net = new ComponentNetwork();
        var tank = new StatefulHydrogenStorageComponent("tank",
            new HydrogenStorageDesign(
                Kind: HydrogenStorageKind.LiquidCryogenic,
                InternalVolume_m3: 0.122,
                OperatingPressure_bar: 1.0,
                OperatingTemperature_K: 20.3,
                DryMass_kg: 50.0,
                HeatLeakRate_W: 1.0),
            initialStoredMass_kg: 8.65);   // approx full LH₂ in 0.122 m³
        net.Add(tank);
        net.SetExternalInput("tank", "HydrogenInflowRate_kgs", 0.0);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("tank", tank);
        var hist = integrator.Run(0.0, 1000.0, 1.0);

        double finalMass = hist[^1].PortValues["tank"]["StoredHydrogenMass_kg"];
        double loss = 8.65 - finalMass;
        Assert.InRange(loss, 0.0018, 0.0028);
    }

    [Fact]
    public void StatefulH2Tank_NetMassFlowRate_ReflectsInflowMinusBoilOff()
    {
        // Cryo LH₂ tank with 2 W heat leak (boilOff ≈ 4.5e-6 kg/s) AND
        // 1e-5 kg/s inflow: net = inflow − boilOff ≈ +5.5e-6 kg/s.
        var net = new ComponentNetwork();
        var tank = new StatefulHydrogenStorageComponent("tank",
            new HydrogenStorageDesign(
                Kind: HydrogenStorageKind.LiquidCryogenic,
                InternalVolume_m3: 0.122,
                OperatingPressure_bar: 1.0,
                OperatingTemperature_K: 20.3,
                DryMass_kg: 50.0,
                HeatLeakRate_W: 2.0),
            initialStoredMass_kg: 1.0);
        net.Add(tank);
        net.SetExternalInput("tank", "HydrogenInflowRate_kgs", 1.0e-5);

        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("tank", tank);
        var hist = integrator.Run(0.0, 1.0, 0.1);

        // Net flow at first tick (state at initial mass = 1.0 kg):
        //   boilOff = 2.0 / 446000 = 4.484e-6 kg/s
        //   inflow = 1.0e-5
        //   net    = 1.0e-5 − 4.484e-6 = +5.516e-6 kg/s
        double net0 = hist[0].PortValues["tank"]["NetMassFlowRate_kgs"];
        Assert.InRange(net0, 5.0e-6, 6.0e-6);
    }

    [Fact]
    public void StatefulH2Tank_CompressedGas_NoBoilOff()
    {
        var net = new ComponentNetwork();
        var tank = new StatefulHydrogenStorageComponent("tank",
            new HydrogenStorageDesign(
                Kind: HydrogenStorageKind.CompressedGas,
                InternalVolume_m3: 0.122,
                OperatingPressure_bar: 700.0,
                OperatingTemperature_K: 298.15,
                DryMass_kg: 95.0),
            initialStoredMass_kg: 5.0);
        net.Add(tank);
        net.SetExternalInput("tank", "HydrogenInflowRate_kgs", 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("tank", tank);
        var hist = integrator.Run(0.0, 100.0, 1.0);
        // No inflow, no boil-off → mass stays at 5 kg exactly.
        Assert.Equal(5.0, hist[^1].PortValues["tank"]["StoredHydrogenMass_kg"], precision: 9);
    }

    [Fact]
    public void StatefulH2Tank_RejectsNegativeInitialMass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatefulHydrogenStorageComponent("t",
                new HydrogenStorageDesign(
                    Kind: HydrogenStorageKind.CompressedGas,
                    InternalVolume_m3: 0.122, OperatingPressure_bar: 700.0,
                    OperatingTemperature_K: 298.15, DryMass_kg: 95.0),
                initialStoredMass_kg: -1.0));
    }
}
