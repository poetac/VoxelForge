// IntegratorStageTimeForcingTests — pins that the multi-stage integrators
// (RK4, Crank-Nicolson, Cash-Karp) evaluate a time-varying external input at
// each stage's node time rather than freezing it at the tick start.
//
// Red-team finding: RefreshTimeVaryingInputsAt was called only once per tick
// (at the tick-start time), so every internal stage of RK4 / CN / Cash-Karp saw
// u(t_tickstart). For a system driven by a time-varying forcing this silently
// collapsed all higher-order methods to first-order (Euler) accuracy.
//
// These use a SINGLE large step where the analytic answer is exact and the
// frozen-forcing (Euler-equivalent) result diverges sharply — unlike the
// existing many-small-step ramp tests (Voxelforge.Tests, net9.0-windows), whose
// loose tolerances let the bug hide because Euler ≈ RK4 at small dt. Runs on the
// cross-platform Linux CI leg.

using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class IntegratorStageTimeForcingTests
{
    // dY/dt = u(t) = t, integrated from a zero initial state. The exact
    // integral over [0, T] is T²/2 (= 2.0 over [0, 2]).
    private static TimeStepIntegrator BuildRampDrivenAccumulator()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc", initial: 0.0);
        net.Add(acc);
        net.SetTimeVaryingExternalInput("acc", "Input_rate", t => t);   // u(t) = t
        var integ = new TimeStepIntegrator(net);
        integ.RegisterStateful("acc", acc);
        return integ;
    }

    private static double FinalTotal(System.Collections.Generic.IReadOnlyList<TimeHistorySnapshot> hist)
        => hist[^1].PortValues["acc"]["Accumulated_total"];

    [Fact]
    public void Rk4_TimeVaryingForcing_IntegratesExactlyOverOneStep()
    {
        // Single RK4 step over [0, 2]; RK4 is exact for this polynomial → 2.0.
        // Old code froze the forcing at u(0)=0 across all four stages → 0.0.
        var hist = BuildRampDrivenAccumulator().Run(0.0, 2.0, 2.0, method: IntegrationMethod.Rk4);
        Assert.Equal(2.0, FinalTotal(hist), precision: 9);
    }

    [Fact]
    public void Rk4_TimeVaryingForcing_DoesNotCollapseToEuler()
    {
        // Over [0, 2] with dt = 1, correct RK4 = 2.0 must differ from Explicit
        // Euler (= 1.0, left-Riemann). On the old code both returned 1.0, proving
        // the higher-order method had silently degraded to first order.
        double rk4 = FinalTotal(BuildRampDrivenAccumulator()
            .Run(0.0, 2.0, 1.0, method: IntegrationMethod.Rk4));
        double euler = FinalTotal(BuildRampDrivenAccumulator()
            .Run(0.0, 2.0, 1.0, method: IntegrationMethod.ExplicitEuler));

        Assert.Equal(2.0, rk4, precision: 9);
        Assert.NotEqual(euler, rk4, precision: 9);
    }

    [Fact]
    public void CrankNicolson_TimeVaryingForcing_IntegratesExactlyOverOneStep()
    {
        // CN trapezoid over [0, 2]: y = y0 + dt/2·(u(0) + u(2)) = 0 + 1·(0 + 2) =
        // 2.0. The implicit f(t+dt, ·) must use u at the END of the step; the old
        // code read u(0)=0 there → 0.0.
        var hist = BuildRampDrivenAccumulator()
            .Run(0.0, 2.0, 2.0, method: IntegrationMethod.CrankNicolson);
        Assert.Equal(2.0, FinalTotal(hist), precision: 6);
    }

    [Fact]
    public void CashKarp_TimeVaryingForcing_IntegratesToAnalyticArea()
    {
        // Adaptive Cash-Karp RK45 over [0, 2]; exact for this polynomial → 2.0.
        // With frozen forcing each step's six stages saw u(step-start), degrading
        // the embedded quadrature to a left-Riemann sum well below 2.0.
        var hist = BuildRampDrivenAccumulator()
            .RunAdaptiveCashKarp45(0.0, 2.0, dtInitial_s: 0.5, dtMin_s: 0.05, dtMax_s: 1.0);
        Assert.Equal(2.0, FinalTotal(hist), precision: 6);
    }
}
