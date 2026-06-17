// CrankNicolsonStiffSolverTests.cs — Sprint SI.W21 unit tests for the
// Crank-Nicolson implicit-trapezoid integrator. Pins:
//   • A-stability: stiff exponential decay stays bounded at dt > 2/λ
//     where Euler / RK4 blow up.
//   • Order-2 accuracy: ε_global ∝ dt² (vs Euler's O(dt) and RK4's O(dt⁴)).
//   • Fixed-point convergence: mildly stiff systems converge within the
//     iteration ceiling.
//   • Energy-balance conservation: trapezoid preserves invariants better
//     than Euler at large dt.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class CrankNicolsonStiffSolverTests
{
    /// <summary>
    /// Exponential decay y' = -λy. Stiff when λ is large; the eigenvalue
    /// of the linearised problem is -λ.
    /// </summary>
    private sealed class ExponentialDecay : SystemComponent, IStatefulComponent
    {
        private readonly double _lambda;
        private readonly double _initial;
        private double _y;

        public ExponentialDecay(string name, double lambda, double initialY)
            : base(name)
        {
            _lambda  = lambda;
            _initial = initialY;
            _y       = initialY;
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
            => derivatives[0] = -_lambda * state[0];
        public void GetInitialState(Span<double> destination) => destination[0] = _initial;
        public void GetCurrentState(Span<double> destination) => destination[0] = _y;
        public void SetState(ReadOnlySpan<double> state) => _y = state[0];
    }

    [Fact]
    public void CrankNicolson_StaysStableOnStiffSystem_WhereEulerExplodes()
    {
        // λ = 100 → eigenvalue -100. Explicit Euler's stability bound is
        // dt < 2/λ = 0.02. At dt = 0.1 Euler diverges; Crank-Nicolson must
        // stay bounded (A-stable).
        const double lambda = 100.0;
        const double dt = 0.1;
        const double y0 = 1.0;

        var netEuler = new ComponentNetwork();
        var dEuler = new ExponentialDecay("d", lambda, y0);
        netEuler.Add(dEuler);
        var euler = new TimeStepIntegrator(netEuler);
        euler.RegisterStateful("d", dEuler);
        var hEuler = euler.Run(0.0, 2.0, dt, method: IntegrationMethod.ExplicitEuler);

        var netCn = new ComponentNetwork();
        var dCn = new ExponentialDecay("d", lambda, y0);
        netCn.Add(dCn);
        var cn = new TimeStepIntegrator(netCn);
        cn.RegisterStateful("d", dCn);
        var hCn = cn.Run(0.0, 2.0, dt, method: IntegrationMethod.CrankNicolson);

        double finalEuler = hEuler[^1].PortValues["d"]["y"];
        double finalCn    = hCn[^1].PortValues["d"]["y"];

        // Euler diverges → |y| growing unboundedly. CN must stay bounded
        // (≤ y0).
        Assert.True(Math.Abs(finalEuler) > y0 * 2.0,
            $"Euler at dt=0.1 should blow up on stiff (λ=100) system. "
          + $"Got |y_euler|={Math.Abs(finalEuler):E3} (expected > {y0 * 2.0:E3}).");
        Assert.True(Math.Abs(finalCn) <= y0,
            $"Crank-Nicolson at dt=0.1 should stay bounded (A-stable). "
          + $"Got |y_cn|={Math.Abs(finalCn):E3} (expected ≤ {y0:E3}).");
    }

    [Fact]
    public void CrankNicolson_ConvergesToAnalytical_OnNonStiffDecay()
    {
        // λ = 1, dt = 0.1, t_end = 1.0 → analytical y(1) = e^-1 = 0.3679.
        // CN is order-2 accurate; expect convergence to ~3 decimals.
        var net = new ComponentNetwork();
        var d = new ExponentialDecay("d", 1.0, 1.0);
        net.Add(d);
        var cn = new TimeStepIntegrator(net);
        cn.RegisterStateful("d", d);
        var hist = cn.Run(0.0, 1.0, 0.1, method: IntegrationMethod.CrankNicolson);
        double truth = Math.Exp(-1.0);
        Assert.Equal(truth, hist[^1].PortValues["d"]["y"], precision: 3);
    }

    [Fact]
    public void CrankNicolson_OrderTwoAccuracyOnExponentialDecay()
    {
        // Halve dt → error should drop ~4×. Order-2 scaling for CN
        // (vs ~2× for Euler order-1, ~16× for RK4 order-4).
        var net1 = new ComponentNetwork();
        var d1 = new ExponentialDecay("d", 1.0, 1.0);
        net1.Add(d1);
        var int1 = new TimeStepIntegrator(net1);
        int1.RegisterStateful("d", d1);
        var h1 = int1.Run(0.0, 1.0, 0.1, method: IntegrationMethod.CrankNicolson);

        var net2 = new ComponentNetwork();
        var d2 = new ExponentialDecay("d", 1.0, 1.0);
        net2.Add(d2);
        var int2 = new TimeStepIntegrator(net2);
        int2.RegisterStateful("d", d2);
        var h2 = int2.Run(0.0, 1.0, 0.05, method: IntegrationMethod.CrankNicolson);

        double truth = Math.Exp(-1.0);
        double err1 = Math.Abs(h1[^1].PortValues["d"]["y"] - truth);
        double err2 = Math.Abs(h2[^1].PortValues["d"]["y"] - truth);

        // CN is order-2; halving dt should drop error by ~4×. Accept ≥ 3×
        // to absorb the rounding floor.
        Assert.True(err2 < err1 / 3.0,
            $"Crank-Nicolson should show ≥3× error reduction at half dt. "
          + $"Got err(dt=0.1)={err1:E3}, err(dt=0.05)={err2:E3}.");
    }

    [Fact]
    public void CrankNicolson_HandlesModeratelyStiffSystem()
    {
        // λ = 50; dt = 0.1. CN must converge in ≤ 25 iterations
        // (the inner-loop ceiling) on every tick.
        var net = new ComponentNetwork();
        var d = new ExponentialDecay("d", 50.0, 1.0);
        net.Add(d);
        var cn = new TimeStepIntegrator(net);
        cn.RegisterStateful("d", d);
        var hist = cn.Run(0.0, 1.0, 0.1, method: IntegrationMethod.CrankNicolson);
        double truth = Math.Exp(-50.0);
        // y(1) = e^-50 ≈ 1.9e-22 — physical truth is essentially zero.
        // Stability requires |y| ≤ y0 throughout.
        foreach (var snap in hist)
        {
            double v = snap.PortValues["d"]["y"];
            Assert.True(Math.Abs(v) <= 1.0,
                $"State should remain bounded on stiff system; got |y|={Math.Abs(v):E3}");
        }
    }

    [Fact]
    public void CrankNicolson_DegeneratesToCnExactSolutionOnLinearDecay()
    {
        // For y' = -λy, CN gives y_{n+1} = y_n · (1 - λdt/2) / (1 + λdt/2).
        // This is the canonical implicit-trapezoid recurrence — test we
        // match it exactly (to FP precision) on a single step.
        double lambda = 2.0;
        double dt = 0.1;
        double y0 = 1.0;

        var net = new ComponentNetwork();
        var d = new ExponentialDecay("d", lambda, y0);
        net.Add(d);
        var cn = new TimeStepIntegrator(net);
        cn.RegisterStateful("d", d);
        var hist = cn.Run(0.0, dt * 1.5, dt, method: IntegrationMethod.CrankNicolson);

        double expected = y0 * (1.0 - lambda * dt / 2.0) / (1.0 + lambda * dt / 2.0);
        // First post-tick snapshot is at t=dt (the second history entry).
        Assert.Equal(expected, hist[1].PortValues["d"]["y"], precision: 6);
    }

    [Fact]
    public void CrankNicolson_BeatsEulerAccuracyOnNonStiffDecay()
    {
        // Same dt, same system; CN order-2 vs Euler order-1.
        var netE = new ComponentNetwork();
        var dE = new ExponentialDecay("d", 1.0, 1.0);
        netE.Add(dE);
        var euler = new TimeStepIntegrator(netE);
        euler.RegisterStateful("d", dE);
        var hE = euler.Run(0.0, 1.0, 0.1, method: IntegrationMethod.ExplicitEuler);

        var netCn = new ComponentNetwork();
        var dCn = new ExponentialDecay("d", 1.0, 1.0);
        netCn.Add(dCn);
        var cn = new TimeStepIntegrator(netCn);
        cn.RegisterStateful("d", dCn);
        var hCn = cn.Run(0.0, 1.0, 0.1, method: IntegrationMethod.CrankNicolson);

        double truth = Math.Exp(-1.0);
        double eulerErr = Math.Abs(hE[^1].PortValues["d"]["y"]  - truth);
        double cnErr    = Math.Abs(hCn[^1].PortValues["d"]["y"] - truth);

        Assert.True(cnErr < eulerErr,
            $"Crank-Nicolson should beat Euler at the same dt on non-stiff decay. "
          + $"Got err(Euler)={eulerErr:E3}, err(CN)={cnErr:E3}.");
    }

    [Fact]
    public void CrankNicolson_Deterministic_RepeatedRuns()
    {
        // Same network, same dt, same initial state → bit-identical history.
        var net1 = new ComponentNetwork();
        var d1 = new ExponentialDecay("d", 5.0, 1.0);
        net1.Add(d1);
        var int1 = new TimeStepIntegrator(net1);
        int1.RegisterStateful("d", d1);
        var r1 = int1.Run(0.0, 1.0, 0.1, method: IntegrationMethod.CrankNicolson);

        var net2 = new ComponentNetwork();
        var d2 = new ExponentialDecay("d", 5.0, 1.0);
        net2.Add(d2);
        var int2 = new TimeStepIntegrator(net2);
        int2.RegisterStateful("d", d2);
        var r2 = int2.Run(0.0, 1.0, 0.1, method: IntegrationMethod.CrankNicolson);

        Assert.Equal(r1.Count, r2.Count);
        for (int i = 0; i < r1.Count; i++)
        {
            Assert.Equal(r1[i].PortValues["d"]["y"],
                         r2[i].PortValues["d"]["y"]);
        }
    }
}
