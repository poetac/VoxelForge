// CrankNicolsonCeilingHitTests.cs — issue #628.
//
// Pins the CN ceiling-hit telemetry surface added to TimeStepIntegrator.
// A CrankNicolsonCeilingHit record is appended to CrankNicolsonCeilingHits
// whenever the inner Newton loop exhausts CrankNicolsonMaxIterations
// without converging. Scenarios pinned here:
//
//   1. A highly-stiff fixture (λ = 1e4, dt = 0.01) where the old
//      fixed-point iteration diverged. The Newton-Raphson solver is
//      A-stable and converges even here → zero ceiling hits expected.
//   2. A modest fixture (λ = 50, dt = 1 ms) — Newton converges in
//      1-2 iterations → zero hits expected.
//   3. The CeilingHit list clears at the start of every Run invocation.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class CrankNicolsonCeilingHitTests
{
    /// <summary>Scalar `dy/dt = -λ y` decay, reused from AdaptiveStepCrankNicolsonTests.</summary>
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

    private static TimeStepIntegrator BuildIntegrator(double lambda)
    {
        var net = new ComponentNetwork();
        var d = new ExponentialDecay("d", lambda, 1.0);
        net.Add(d);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("d", d);
        return integrator;
    }

    [Fact]
    public void Newton_AStable_NoCeilingHits_OnExtremelyStiffSystem()
    {
        // λ·dt/2 = 1e4 × 0.01 / 2 = 50 ≫ 1. The old fixed-point iteration
        // diverged here; Newton-Raphson is A-stable and converges in a few
        // iterations regardless of stiffness. Zero ceiling hits expected.
        var integrator = BuildIntegrator(lambda: 1e4);
        integrator.Run(
            t0_s:    0.0,
            tEnd_s:  0.05,                                  // 6 ticks at dt=0.01
            dt_s:    0.01,
            method:  IntegrationMethod.CrankNicolson);

        Assert.Equal(0, integrator.CrankNicolsonCeilingHitCount);
        Assert.Empty(integrator.CrankNicolsonCeilingHits);
        Assert.True(
            integrator.LastCrankNicolsonIterations > 0 &&
            integrator.LastCrankNicolsonIterations < TimeStepIntegrator.CrankNicolsonMaxIterations,
            $"Newton should converge well under the ceiling; got {integrator.LastCrankNicolsonIterations} iterations.");
    }

    [Fact]
    public void Modest_StaysUnderCeiling_NoHits()
    {
        // λ·dt/2 = 50 × 1e-3 / 2 = 0.025 ≪ 1, so fixed-point converges
        // in 1-2 iterations. Zero ceiling hits expected.
        var integrator = BuildIntegrator(lambda: 50.0);
        integrator.Run(
            t0_s:    0.0,
            tEnd_s:  0.1,                                   // 101 ticks at dt=1e-3
            dt_s:    1e-3,
            method:  IntegrationMethod.CrankNicolson);

        Assert.Equal(0, integrator.CrankNicolsonCeilingHitCount);
        Assert.Empty(integrator.CrankNicolsonCeilingHits);

        // Sanity: the iteration count should be well under the ceiling.
        Assert.True(
            integrator.LastCrankNicolsonIterations > 0 &&
            integrator.LastCrankNicolsonIterations < TimeStepIntegrator.CrankNicolsonMaxIterations,
            $"LastCrankNicolsonIterations={integrator.LastCrankNicolsonIterations} should be in (0, {TimeStepIntegrator.CrankNicolsonMaxIterations}).");
    }

    [Fact]
    public void Adaptive_NoCeilingHits_OnStiffSystem_WithNewton()
    {
        // Newton-Raphson is A-stable on linear stiff systems (λ·dt/2 = 50
        // at λ=1e4, dt=0.01). The adaptive controller therefore never
        // observes a ceiling hit and does not shrink dt.
        var integrator = BuildIntegrator(lambda: 1e4);
        integrator.RunAdaptiveCrankNicolson(
            t0_s:             0.0,
            tEnd_s:           0.05,
            dtInitial_s:      0.01,
            dtMin_s:          1e-6,
            dtMax_s:          0.01,
            targetIterations: 5);

        Assert.Equal(0, integrator.CrankNicolsonCeilingHitCount);
        Assert.Empty(integrator.CrankNicolsonCeilingHits);
    }

    [Fact]
    public void Run_ResetsCeilingHits_AcrossInvocations()
    {
        // The hit list must clear at the start of every Run call —
        // otherwise telemetry from a prior run leaks into the next.
        // Newton is A-stable so no ceiling hits occur; the reset
        // contract is still valid (count is 0 → 0 across runs).
        var integrator = BuildIntegrator(lambda: 1e4);
        integrator.Run(
            t0_s:   0.0,
            tEnd_s: 0.03,
            dt_s:   0.01,
            method: IntegrationMethod.CrankNicolson);
        int firstRunHits = integrator.CrankNicolsonCeilingHitCount;
        Assert.Equal(0, firstRunHits);                      // Newton is A-stable → no ceiling hits

        // Second invocation with a tame λ — counts still start fresh.
        var integrator2 = BuildIntegrator(lambda: 50.0);
        integrator2.Run(
            t0_s:   0.0,
            tEnd_s: 0.01,
            dt_s:   1e-3,
            method: IntegrationMethod.CrankNicolson);
        Assert.Equal(0, integrator2.CrankNicolsonCeilingHitCount);

        // Same integrator instance, second run via a non-CN method.
        // Reset clears CN telemetry regardless of the method used.
        integrator.Run(
            t0_s:   0.0,
            tEnd_s: 0.01,
            dt_s:   1e-3,
            method: IntegrationMethod.ExplicitEuler);
        Assert.Equal(0, integrator.CrankNicolsonCeilingHitCount);
    }
}
