// IntegrationMethod.cs — Sprint SI.W6 + SI.W21 ODE-stepper discriminator
// for TimeStepIntegrator. Explicit Euler (SI.W5 default) is order-1
// accurate; RK4 (Runge-Kutta-4, SI.W6) is order-4 but conditionally
// stable. CrankNicolson (SI.W21) is order-2 A-stable for stiff systems
// (battery thermal runaway, LH₂ boil-off, large-RC electrical loads)
// where Euler / RK4 require sub-millisecond steps to stay stable.

namespace Voxelforge.Integration;

/// <summary>ODE-stepper choice for <see cref="TimeStepIntegrator"/>.</summary>
internal enum IntegrationMethod
{
    /// <summary>
    /// Explicit Euler (SI.W5 baseline). y(t+dt) = y(t) + dt · f(t, y).
    /// Order-1 accurate. Cheapest per step but ε_global ∝ dt — large
    /// dt produces visible drift on long horizons. Conditionally stable
    /// — fails on stiff systems unless dt is below the smallest
    /// time-scale.
    /// </summary>
    ExplicitEuler = 0,

    /// <summary>
    /// Classical 4-stage Runge-Kutta (SI.W6). Order-4 accurate; ε_global
    /// ∝ dt⁴. Costs 4 derivative evaluations per step but is dramatically
    /// more accurate than Euler for the same dt — typically allows
    /// 10×-100× larger dt at the same accuracy. Conditionally stable
    /// (slightly better than Euler).
    /// </summary>
    Rk4 = 1,

    /// <summary>
    /// Crank-Nicolson implicit trapezoid (SI.W21). y(t+dt) = y(t)
    /// + (dt/2) · [f(t, y) + f(t+dt, y(t+dt))]. Order-2 accurate and
    /// A-stable — handles stiff systems (battery thermal runaway,
    /// LH₂ boil-off, RC-dominated electrical loads, large heat-capacity
    /// thermal masses) without sub-tick dt. Solved by fixed-point
    /// iteration starting from an explicit-Euler predictor; converges
    /// for mildly-to-moderately stiff systems. Severely-stiff problems
    /// (Jacobian eigenvalues spanning &gt; 10⁶) need full Newton —
    /// deferred until a real such system surfaces.
    /// </summary>
    CrankNicolson = 2,

    /// <summary>
    /// Cash-Karp embedded Runge-Kutta 4(5) with PI step controller
    /// (Sprint B.8c / issue #492). Cash &amp; Karp (1990) 6-stage scheme
    /// yields both a 5th-order solution and an embedded 4th-order
    /// solution; the per-state error <c>|y5 − y4|</c> drives a per-step
    /// dt adaptation against a caller-supplied <c>atol</c> + <c>rtol</c>
    /// weighted-RMS error norm. Order-5 accurate when steps are
    /// accepted; rejected steps retry with a smaller dt. The adaptive
    /// path lives on
    /// <see cref="TimeStepIntegrator.RunAdaptiveCashKarp45"/> — this
    /// enum value is the discriminator the caller passes there. The
    /// fixed-step <see cref="TimeStepIntegrator.Run"/> path does NOT
    /// recognise this method (the step-size logic is inseparable from
    /// the integrator); calling Run with CashKarpRk45Adaptive raises.
    /// </summary>
    CashKarpRk45Adaptive = 3,
}
