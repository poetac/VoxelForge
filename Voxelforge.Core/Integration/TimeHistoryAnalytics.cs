// TimeHistoryAnalytics.cs — Sprint SI.W16 post-processing helpers for
// TimeStepIntegrator histories.
//
// Three helpers, all stateless:
//
//   - IntegrateOverTime    — trapezoidal integral of any (comp, port)
//                            value across the run. Useful for total
//                            energy [J] from a Power_W column or total
//                            charge [C] from a current column.
//   - MaxOf / MinOf        — bounds of any (comp, port) over the run.
//                            Useful for ripple analysis / SoC bounds.
//   - PowerBalance         — sweeps every port whose name ends in
//                            "_W" and reports net power [W] per tick.
//                            Positive contributors (sources) vs.
//                            negative (sinks); identifies the
//                            instantaneous balance error.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Integration;

/// <summary>
/// Post-processing helpers for <see cref="TimeStepIntegrator"/>
/// histories (Sprint SI.W16).
/// </summary>
internal static class TimeHistoryAnalytics
{
    /// <summary>
    /// Trapezoidal integral of <paramref name="portName"/> on
    /// <paramref name="componentName"/> across the full history.
    /// </summary>
    /// <returns>∫_{t_0}^{t_end} value(t) dt — units depend on port:
    /// integrating W gives J, integrating A gives C, integrating kg/s
    /// gives kg.</returns>
    public static double IntegrateOverTime(
        IReadOnlyList<TimeHistorySnapshot> history,
        string componentName,
        string portName)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count < 2) return 0.0;
        double total = 0.0;
        for (int k = 1; k < history.Count; k++)
        {
            double dt = history[k].Time_s - history[k - 1].Time_s;
            double y0 = history[k - 1].PortValues[componentName][portName];
            double y1 = history[k].PortValues[componentName][portName];
            total += 0.5 * (y0 + y1) * dt;
        }
        return total;
    }

    /// <summary>Maximum value of (component, port) across the run.</summary>
    public static double MaxOf(
        IReadOnlyList<TimeHistorySnapshot> history,
        string componentName, string portName)
        => history.Select(s => s.PortValues[componentName][portName]).Max();

    /// <summary>Minimum value of (component, port) across the run.</summary>
    public static double MinOf(
        IReadOnlyList<TimeHistorySnapshot> history,
        string componentName, string portName)
        => history.Select(s => s.PortValues[componentName][portName]).Min();

    /// <summary>
    /// Sprint SI.W16. Compute per-tick aggregated power [W] across all
    /// ports whose name ends in <c>"_W"</c>. Positive contributors are
    /// summed into <see cref="PowerBalanceTick.TotalSourcePower_W"/>;
    /// negative contributors into
    /// <see cref="PowerBalanceTick.TotalSinkPower_W"/>. The net is the
    /// instantaneous mismatch (interpretable as a heat-leak or solver
    /// residual depending on the network topology).
    /// </summary>
    public static IReadOnlyList<PowerBalanceTick> PowerBalance(
        IReadOnlyList<TimeHistorySnapshot> history)
        => BalanceByPortSuffix<PowerBalanceTick>(
            history,
            suffix:        "_W",
            constructor:   (t, src, sink) => new PowerBalanceTick(
                Time_s:               t,
                TotalSourcePower_W:   src,
                TotalSinkPower_W:     sink,
                NetPowerImbalance_W:  src + sink));

    /// <summary>
    /// Sprint SI.W25. Compute per-tick aggregated mass flow [kg/s] across
    /// all ports whose name ends in <c>"_kgs"</c>. Positive contributors
    /// are summed into <see cref="MassFlowBalanceTick.TotalInflow_kgs"/>;
    /// negative contributors into
    /// <see cref="MassFlowBalanceTick.TotalOutflow_kgs"/>. The net is the
    /// instantaneous mass-flow mismatch — useful for diagnosing storage-
    /// component conservation invariants (tank mass should integrate to
    /// the running net inflow).
    /// </summary>
    public static IReadOnlyList<MassFlowBalanceTick> MassFlowBalance(
        IReadOnlyList<TimeHistorySnapshot> history)
        => BalanceByPortSuffix<MassFlowBalanceTick>(
            history,
            suffix:      "_kgs",
            constructor: (t, src, sink) => new MassFlowBalanceTick(
                Time_s:              t,
                TotalInflow_kgs:     src,
                TotalOutflow_kgs:    sink,
                NetMassFlow_kgs:     src + sink));

    /// <summary>
    /// Sprint SI.W25. Compute per-tick aggregated current [A] across all
    /// ports whose name ends in <c>"_A"</c>. Positive contributors are
    /// summed into <see cref="CurrentBalanceTick.TotalSourceCurrent_A"/>;
    /// negative contributors into
    /// <see cref="CurrentBalanceTick.TotalSinkCurrent_A"/>. The net is
    /// the instantaneous Kirchhoff-current-law residual at the network
    /// boundary.
    /// </summary>
    public static IReadOnlyList<CurrentBalanceTick> CurrentBalance(
        IReadOnlyList<TimeHistorySnapshot> history)
        => BalanceByPortSuffix<CurrentBalanceTick>(
            history,
            suffix:      "_A",
            constructor: (t, src, sink) => new CurrentBalanceTick(
                Time_s:                t,
                TotalSourceCurrent_A:  src,
                TotalSinkCurrent_A:    sink,
                NetCurrentImbalance_A: src + sink));

    /// <summary>
    /// Sprint SI.W25 shared helper. Walks every port across the history,
    /// matches the suffix exactly, and routes positive / negative
    /// contributions into source / sink buckets respectively. Sprint
    /// SI.W27 adds the optional <paramref name="componentFilter"/>
    /// predicate so callers can scope the balance to a subsystem (e.g.
    /// only thermal-management components, only EV-powertrain
    /// components).
    /// </summary>
    private static List<TBalance> BalanceByPortSuffix<TBalance>(
        IReadOnlyList<TimeHistorySnapshot> history,
        string suffix,
        Func<double, double, double, TBalance> constructor,
        Func<string, bool>? componentFilter = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        var ticks = new List<TBalance>(history.Count);
        foreach (var snap in history)
        {
            double sources = 0.0;
            double sinks   = 0.0;
            foreach (var (componentName, ports) in snap.PortValues)
            {
                if (componentFilter is not null && !componentFilter(componentName)) continue;
                foreach (var (portName, value) in ports)
                {
                    if (!portName.EndsWith(suffix, StringComparison.Ordinal)) continue;
                    if (value >= 0) sources += value;
                    else            sinks   += value;
                }
            }
            ticks.Add(constructor(snap.Time_s, sources, sinks));
        }
        return ticks;
    }

    // ── Sprint SI.W27 — Component-filtered subsystem balance ──────────

    /// <summary>
    /// Sprint SI.W27. <see cref="PowerBalance"/> variant scoped to a
    /// caller-supplied subset of components via name predicate. Useful
    /// for subsystem-level diagnostics:
    /// "What's the power balance across just the thermal-management
    /// subsystem?", "How much electrical power flows through the
    /// EV-powertrain group?".
    /// </summary>
    /// <param name="history">Per-tick snapshot history.</param>
    /// <param name="componentFilter">
    /// Predicate on component name. Returns true for components that
    /// should contribute to the balance; false for components ignored
    /// in this subsystem cut.
    /// </param>
    public static IReadOnlyList<PowerBalanceTick> PowerBalanceFor(
        IReadOnlyList<TimeHistorySnapshot> history,
        Func<string, bool> componentFilter)
    {
        if (componentFilter is null) throw new ArgumentNullException(nameof(componentFilter));
        return BalanceByPortSuffix<PowerBalanceTick>(
            history,
            suffix:        "_W",
            constructor:   (t, src, sink) => new PowerBalanceTick(
                Time_s:               t,
                TotalSourcePower_W:   src,
                TotalSinkPower_W:     sink,
                NetPowerImbalance_W:  src + sink),
            componentFilter: componentFilter);
    }

    /// <summary>
    /// Sprint SI.W27. <see cref="MassFlowBalance"/> variant scoped to a
    /// caller-supplied subset of components via name predicate.
    /// </summary>
    public static IReadOnlyList<MassFlowBalanceTick> MassFlowBalanceFor(
        IReadOnlyList<TimeHistorySnapshot> history,
        Func<string, bool> componentFilter)
    {
        if (componentFilter is null) throw new ArgumentNullException(nameof(componentFilter));
        return BalanceByPortSuffix<MassFlowBalanceTick>(
            history,
            suffix:      "_kgs",
            constructor: (t, src, sink) => new MassFlowBalanceTick(
                Time_s:              t,
                TotalInflow_kgs:     src,
                TotalOutflow_kgs:    sink,
                NetMassFlow_kgs:     src + sink),
            componentFilter: componentFilter);
    }

    /// <summary>
    /// Sprint SI.W27. <see cref="CurrentBalance"/> variant scoped to a
    /// caller-supplied subset of components via name predicate.
    /// </summary>
    public static IReadOnlyList<CurrentBalanceTick> CurrentBalanceFor(
        IReadOnlyList<TimeHistorySnapshot> history,
        Func<string, bool> componentFilter)
    {
        if (componentFilter is null) throw new ArgumentNullException(nameof(componentFilter));
        return BalanceByPortSuffix<CurrentBalanceTick>(
            history,
            suffix:      "_A",
            constructor: (t, src, sink) => new CurrentBalanceTick(
                Time_s:                t,
                TotalSourceCurrent_A:  src,
                TotalSinkCurrent_A:    sink,
                NetCurrentImbalance_A: src + sink),
            componentFilter: componentFilter);
    }

    // ── Sprint SI.W26 — Cumulative network-wide aggregators ────────────

    /// <summary>
    /// Sprint SI.W26. Cumulative network-wide energy ∫P_net(t) dt [J]
    /// from the per-tick <see cref="PowerBalance"/> series. Trapezoidal
    /// rule on the per-tick <c>NetPowerImbalance_W</c>. Useful for total
    /// energy delivered / consumed by the network across the run; in a
    /// conservative system the cumulative residual should stay near zero
    /// (within numerical / solver-residual noise).
    /// </summary>
    /// <param name="history">Per-tick integrator snapshot history.</param>
    /// <returns>Time-indexed cumulative energy [J] starting at 0.</returns>
    public static IReadOnlyList<CumulativeEnergyTick> CumulativeEnergy_J(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var balance = PowerBalance(history);
        var result = new List<CumulativeEnergyTick>(balance.Count);
        double cumulative = 0.0;
        for (int k = 0; k < balance.Count; k++)
        {
            if (k > 0)
            {
                double dt = balance[k].Time_s - balance[k - 1].Time_s;
                cumulative += 0.5 * dt
                    * (balance[k - 1].NetPowerImbalance_W + balance[k].NetPowerImbalance_W);
            }
            result.Add(new CumulativeEnergyTick(balance[k].Time_s, cumulative));
        }
        return result;
    }

    /// <summary>
    /// Sprint SI.W26. Cumulative network-wide mass transfer
    /// ∫ṁ_net(t) dt [kg] from the per-tick
    /// <see cref="MassFlowBalance"/> series. Useful for total mass
    /// transferred (e.g. propellant consumed, hydrogen produced) across
    /// the run.
    /// </summary>
    public static IReadOnlyList<CumulativeMassTick> CumulativeMass_kg(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var balance = MassFlowBalance(history);
        var result = new List<CumulativeMassTick>(balance.Count);
        double cumulative = 0.0;
        for (int k = 0; k < balance.Count; k++)
        {
            if (k > 0)
            {
                double dt = balance[k].Time_s - balance[k - 1].Time_s;
                cumulative += 0.5 * dt
                    * (balance[k - 1].NetMassFlow_kgs + balance[k].NetMassFlow_kgs);
            }
            result.Add(new CumulativeMassTick(balance[k].Time_s, cumulative));
        }
        return result;
    }

    // ── Sprint SI.W29 — Peak finders + conservation residuals ──────────

    /// <summary>
    /// Sprint SI.W29. Find the time + value of the maximum
    /// <c>NetPowerImbalance_W</c> across the run. Useful for diagnosing
    /// the worst-case instantaneous power deficit (when a load exceeded
    /// supply) or surplus (when sources exceeded sinks).
    /// </summary>
    /// <returns>A <see cref="PowerExtremum"/> record carrying the
    /// time-of-peak, peak value, and whether the peak is positive
    /// (source surplus) or negative (sink surplus).</returns>
    public static PowerExtremum PeakPowerImbalance(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var balance = PowerBalance(history);
        if (balance.Count == 0)
            return new PowerExtremum(Time_s: 0.0, PeakValue_W: 0.0, IsSurplus: true);

        var peak = balance[0];
        foreach (var t in balance)
            if (Math.Abs(t.NetPowerImbalance_W) > Math.Abs(peak.NetPowerImbalance_W))
                peak = t;
        return new PowerExtremum(
            Time_s:      peak.Time_s,
            PeakValue_W: peak.NetPowerImbalance_W,
            IsSurplus:   peak.NetPowerImbalance_W >= 0);
    }

    /// <summary>
    /// Sprint SI.W29. Conservation residual — absolute cumulative
    /// energy / mass / charge drift at the final tick. For a closed
    /// (conservative) network the residual should be near zero
    /// (numerical / solver-residual noise only). Large residuals
    /// indicate either (a) an unmodeled external feed missing from the
    /// network, (b) a unit-suffix mismatch (SI.W24 would flag the
    /// connection-level cause), or (c) a solver convergence failure.
    /// </summary>
    /// <returns>Cumulative energy drift [J] at t_end. Use
    /// <see cref="Math.Abs"/> on the result for the magnitude regardless
    /// of direction.</returns>
    public static double ConservationResidualEnergy_J(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        var cum = CumulativeEnergy_J(history);
        return cum.Count == 0 ? 0.0 : cum[^1].CumulativeEnergy_J;
    }

    /// <summary>Sprint SI.W29. Mass-conservation residual at t_end [kg].</summary>
    public static double ConservationResidualMass_kg(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        var cum = CumulativeMass_kg(history);
        return cum.Count == 0 ? 0.0 : cum[^1].CumulativeMass_kg;
    }

    /// <summary>Sprint SI.W29. Charge-conservation residual at t_end [C].</summary>
    public static double ConservationResidualCharge_C(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        var cum = CumulativeCharge_C(history);
        return cum.Count == 0 ? 0.0 : cum[^1].CumulativeCharge_C;
    }

    /// <summary>
    /// Sprint SI.W29. Energy delivered between two timestamps via
    /// trapezoidal integration of the network's per-tick net power.
    /// Useful for mission-phase energy accounting:
    /// <c>EnergyDelivered_J(history, t_cruise_start, t_cruise_end)</c>.
    /// </summary>
    /// <param name="history">Snapshot history.</param>
    /// <param name="t0_s">Start time [s] (inclusive).</param>
    /// <param name="tEnd_s">End time [s] (inclusive).</param>
    /// <returns>Trapezoidal-rule integral of NetPowerImbalance_W over
    /// the window. Sign convention: positive = net source delivery
    /// during the window.</returns>
    public static double EnergyDelivered_J(
        IReadOnlyList<TimeHistorySnapshot> history,
        double t0_s,
        double tEnd_s)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (tEnd_s <= t0_s)
            throw new ArgumentOutOfRangeException(nameof(tEnd_s),
                $"tEnd_s ({tEnd_s}) must be > t0_s ({t0_s}).");

        var balance = PowerBalance(history);
        double total = 0.0;
        for (int k = 1; k < balance.Count; k++)
        {
            double t1 = balance[k].Time_s;
            double t0 = balance[k - 1].Time_s;
            // Skip intervals fully outside the window.
            if (t1 <= t0_s) continue;
            if (t0 >= tEnd_s) break;
            // Clip to window.
            double clipStart = Math.Max(t0, t0_s);
            double clipEnd   = Math.Min(t1, tEnd_s);
            if (clipEnd <= clipStart) continue;
            double dt = clipEnd - clipStart;
            // Interpolate the piecewise-linear power to the clipped endpoints,
            // then apply the trapezoid over the clipped width. A window
            // boundary that lands mid-interval then integrates exactly. (The
            // earlier form applied the FULL-interval endpoint average over the
            // clipped width — correct only when the clip is the whole interval
            // or symmetric about its midpoint, and 2× off for an edge-aligned
            // partial window.)
            double y0 = balance[k - 1].NetPowerImbalance_W;
            double y1 = balance[k].NetPowerImbalance_W;
            double fullInterval = t1 - t0;
            double yA = fullInterval > 0.0 ? y0 + (y1 - y0) * (clipStart - t0) / fullInterval : y0;
            double yB = fullInterval > 0.0 ? y0 + (y1 - y0) * (clipEnd   - t0) / fullInterval : y1;
            total += 0.5 * (yA + yB) * dt;
        }
        return total;
    }

    /// <summary>
    /// Sprint SI.W26. Cumulative network-wide charge transfer
    /// ∫I_net(t) dt [C] from the per-tick <see cref="CurrentBalance"/>
    /// series. Useful for total ampere-hours delivered (battery capacity
    /// drawn / charged) across the run.
    /// </summary>
    public static IReadOnlyList<CumulativeChargeTick> CumulativeCharge_C(
        IReadOnlyList<TimeHistorySnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var balance = CurrentBalance(history);
        var result = new List<CumulativeChargeTick>(balance.Count);
        double cumulative = 0.0;
        for (int k = 0; k < balance.Count; k++)
        {
            if (k > 0)
            {
                double dt = balance[k].Time_s - balance[k - 1].Time_s;
                cumulative += 0.5 * dt
                    * (balance[k - 1].NetCurrentImbalance_A + balance[k].NetCurrentImbalance_A);
            }
            result.Add(new CumulativeChargeTick(balance[k].Time_s, cumulative));
        }
        return result;
    }
}

/// <summary>Per-tick power balance summary (Sprint SI.W16).</summary>
internal sealed record PowerBalanceTick(
    double Time_s,
    double TotalSourcePower_W,
    double TotalSinkPower_W,
    double NetPowerImbalance_W);

/// <summary>Per-tick mass-flow balance summary (Sprint SI.W25).</summary>
/// <param name="Time_s">Snapshot time [s].</param>
/// <param name="TotalInflow_kgs">Σ positive mass-flow ports [kg/s].</param>
/// <param name="TotalOutflow_kgs">Σ negative mass-flow ports [kg/s] (≤ 0).</param>
/// <param name="NetMassFlow_kgs">Inflow + Outflow — should integrate to
/// stored-mass change across the network for conservative systems.</param>
internal sealed record MassFlowBalanceTick(
    double Time_s,
    double TotalInflow_kgs,
    double TotalOutflow_kgs,
    double NetMassFlow_kgs);

/// <summary>Per-tick current balance summary (Sprint SI.W25).</summary>
/// <param name="Time_s">Snapshot time [s].</param>
/// <param name="TotalSourceCurrent_A">Σ positive current ports [A].</param>
/// <param name="TotalSinkCurrent_A">Σ negative current ports [A] (≤ 0).</param>
/// <param name="NetCurrentImbalance_A">Source + Sink — should be ≈ 0
/// for conservative networks (Kirchhoff current law).</param>
internal sealed record CurrentBalanceTick(
    double Time_s,
    double TotalSourceCurrent_A,
    double TotalSinkCurrent_A,
    double NetCurrentImbalance_A);

/// <summary>
/// Time-indexed cumulative energy [J] (Sprint SI.W26). Trapezoidal
/// integral of <see cref="PowerBalanceTick.NetPowerImbalance_W"/>.
/// </summary>
internal sealed record CumulativeEnergyTick(double Time_s, double CumulativeEnergy_J);

/// <summary>
/// Time-indexed cumulative mass transfer [kg] (Sprint SI.W26).
/// Trapezoidal integral of <see cref="MassFlowBalanceTick.NetMassFlow_kgs"/>.
/// </summary>
internal sealed record CumulativeMassTick(double Time_s, double CumulativeMass_kg);

/// <summary>
/// Time-indexed cumulative charge transfer [C] (Sprint SI.W26).
/// Trapezoidal integral of <see cref="CurrentBalanceTick.NetCurrentImbalance_A"/>.
/// </summary>
internal sealed record CumulativeChargeTick(double Time_s, double CumulativeCharge_C);

/// <summary>
/// Worst-case instantaneous power imbalance (Sprint SI.W29).
/// Result of <see cref="TimeHistoryAnalytics.PeakPowerImbalance"/>.
/// </summary>
/// <param name="Time_s">Time at which the peak occurred [s].</param>
/// <param name="PeakValue_W">Signed peak power [W]. Positive = source
/// surplus, negative = sink surplus.</param>
/// <param name="IsSurplus">True if the peak was a source surplus
/// (PeakValue_W &gt; 0), false for a sink surplus.</param>
internal sealed record PowerExtremum(
    double Time_s,
    double PeakValue_W,
    bool   IsSurplus);
