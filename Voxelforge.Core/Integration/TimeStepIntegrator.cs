// TimeStepIntegrator.cs — Sprint SI.W5 explicit-Euler time-domain
// integrator wrapping ComponentNetwork.
//
// At each tick:
//   1. Solve() (or SolveIterative()) the algebraic network.
//   2. For each IStatefulComponent, compute time derivatives.
//   3. Update state via y(t+dt) = y(t) + dt · dy/dt.
//   4. Push the new state back into the component.
//   5. Record per-tick port snapshots.
//
// Sprint SI.W5 ships explicit Euler. RK4 / adaptive-step / implicit
// methods deferred to SI.W6+. Tied to the existing ComponentNetwork
// — no API changes to existing pillars / adapters / non-stateful
// components.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Integration;

/// <summary>
/// Per-tick time-history snapshot from a
/// <see cref="TimeStepIntegrator"/> run (Sprint SI.W5).
/// </summary>
/// <param name="Time_s">Simulation time at the snapshot [s].</param>
/// <param name="PortValues">Component → port → value map.</param>
/// <param name="StateValues">Stateful-component → state-variable →
/// value map.</param>
internal sealed record TimeHistorySnapshot(
    double Time_s,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> PortValues,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> StateValues);

/// <summary>
/// Issue #628 — one record per Crank-Nicolson tick that exhausted
/// <see cref="TimeStepIntegrator.CrankNicolsonMaxIterations"/> without
/// the fixed-point residual dropping below tolerance. ADR-031's
/// silent-non-convergence failure mode becomes visible here.
/// </summary>
/// <param name="TickIndex">Zero-based outer-loop tick the hit occurred on.</param>
/// <param name="Dt_s">Step size in effect at the hit.</param>
/// <param name="MaxResidualAtExit">Largest scaled |Δy|/tol over all state vars at the final iteration. Values ≫ 1 mean the iteration was nowhere near converging.</param>
internal readonly record struct CrankNicolsonCeilingHit(
    int    TickIndex,
    double Dt_s,
    double MaxResidualAtExit);

/// <summary>
/// Explicit-Euler time-domain integrator wrapping a
/// <see cref="ComponentNetwork"/> (Sprint SI.W5).
/// </summary>
internal sealed class TimeStepIntegrator
{
    private readonly ComponentNetwork _network;
    private readonly Dictionary<string, IStatefulComponent> _statefulComponents = new();
    // Issue #736 Phase 2 — internal state flattened to per-component
    // `double[]`. Index map for each array is the
    // <see cref="StateVectorBinding"/> cached in <see cref="_bindings"/>.
    // Issue #738 Phase 3 — IStatefulComponent surface is now span-based,
    // so the integrator passes `_state[name]` directly to ComputeDerivatives /
    // SetState / GetInitialState / GetCurrentState. The Phase 2 temp-dict
    // boundary pool is gone — no per-tick dict allocation at the integrator-
    // component boundary. StateVectorBinding is still used for SnapshotData
    // dict<->array translation (SystemSnapshot public shape).
    private readonly Dictionary<string, double[]> _state = new();
    private readonly Dictionary<string, StateVectorBinding> _bindings = new();

    // ── #610 per-step reusable buffer pool ────────────────────────────
    // Hoisted from per-tick / per-iteration allocations in CN, RK4 and
    // Cash-Karp. Each buffer is pre-allocated with the registered
    // stateful-component set's structure (outer keyed by component name,
    // inner a flat double[] sized by the binding's VariableCount).
    // Subsequent uses overwrite values without allocating.
    // Lazy-allocated on first Advance call; rebuilt if the stateful-
    // component set changes after init. Issue #736 Phase 2 flips the
    // inner storage from Dictionary<string, double> → double[]; the
    // arrays are indexed by each component's StateVectorBinding.
    private int _bufferedComponentCount = -1;
    private Dictionary<string, double[]>? _yTBuf;
    private Dictionary<string, double[]>? _yPredBuf;
    private Dictionary<string, double[]>? _yNextBuf;
    private Dictionary<string, double[]>? _fTBuf;
    private Dictionary<string, double[]>? _fEndBuf;
    private Dictionary<string, double[]>? _kBuf1;
    private Dictionary<string, double[]>? _kBuf2;
    private Dictionary<string, double[]>? _kBuf3;
    private Dictionary<string, double[]>? _kBuf4;
    private Dictionary<string, double[]>? _kBuf5;
    private Dictionary<string, double[]>? _kBuf6;
    private Dictionary<string, double[]>? _origBuf;
    private Dictionary<string, double[]>? _y5Buf;
    // Cached `Dictionary[]` arg arrays for ApplyMultiPerturbation — see
    // #610. The Cash-Karp tableau evaluates each stage at a linear
    // combination of prior stage derivatives; these arrays hold stable
    // refs to the kN buffers so each step reuses the same array
    // instance instead of allocating `new[] { k1, k2 }` etc per call.
    private Dictionary<string, double[]>[]? _ksStage2;  // {k1}
    private Dictionary<string, double[]>[]? _ksStage3;  // {k1, k2}
    private Dictionary<string, double[]>[]? _ksStage4;  // {k1, k2, k3}
    private Dictionary<string, double[]>[]? _ksStage5;  // {k1, k2, k3, k4}
    private Dictionary<string, double[]>[]? _ksStage6;  // {k1, k2, k3, k4, k5}

    // CN-NEWTON flat state-ordering cache: (componentName, flatOffset, stateVarCount)[].
    // Rebuilt by EnsureBuffersAllocated whenever the component set changes.
    // Used by AdvanceCrankNicolson to map flat Jacobian indices ↔ per-component arrays.
    private (string Name, int Offset, int Count)[]? _cnStateOrder;
    private int _cnTotalStateDim;
    // Flat N-vector / N×N-matrix buffers for the Newton linear system.
    // Lazily sized to _cnTotalStateDim in AdvanceCrankNicolson; no per-tick alloc.
    private double[]? _cnResidualBuf;   // G(y^(k))
    private double[]? _cnNewtonStepBuf; // δy = J_G^{-1} · G
    private double[]? _cnJacobianBuf;   // dense N×N J_G (row-major), modified by LU

    public TimeStepIntegrator(ComponentNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        _network = network;
    }

    private void EnsureBuffersAllocated()
    {
        // Lazy-init / re-init when the stateful-component set has changed
        // (e.g. a caller registered another stateful component after the
        // first Run). The check is cheap (Count compare) on every Advance
        // entry; the rebuild only fires when registrations change.
        if (_bufferedComponentCount == _statefulComponents.Count) return;

        _yTBuf    = CreatePooledStateBuffer();
        _yPredBuf = CreatePooledStateBuffer();
        _yNextBuf = CreatePooledStateBuffer();
        _fTBuf    = CreatePooledStateBuffer();
        _fEndBuf  = CreatePooledStateBuffer();
        _kBuf1    = CreatePooledStateBuffer();
        _kBuf2    = CreatePooledStateBuffer();
        _kBuf3    = CreatePooledStateBuffer();
        _kBuf4    = CreatePooledStateBuffer();
        _kBuf5    = CreatePooledStateBuffer();
        _kBuf6    = CreatePooledStateBuffer();
        _origBuf  = CreatePooledStateBuffer();
        _y5Buf    = CreatePooledStateBuffer();

        // Cash-Karp tableau-stage ks-array refs.
        _ksStage2 = new[] { _kBuf1 };
        _ksStage3 = new[] { _kBuf1, _kBuf2 };
        _ksStage4 = new[] { _kBuf1, _kBuf2, _kBuf3 };
        _ksStage5 = new[] { _kBuf1, _kBuf2, _kBuf3, _kBuf4 };
        _ksStage6 = new[] { _kBuf1, _kBuf2, _kBuf3, _kBuf4, _kBuf5 };

        // CN-NEWTON: build flat state-variable ordering for Jacobian assembly.
        var order = new List<(string, int, int)>(_statefulComponents.Count);
        int flatOffset = 0;
        foreach (var (name, comp) in _statefulComponents)
        {
            int cnt = comp.StateVariables.Count;
            order.Add((name, flatOffset, cnt));
            flatOffset += cnt;
        }
        _cnStateOrder    = order.ToArray();
        _cnTotalStateDim = flatOffset;

        _bufferedComponentCount = _statefulComponents.Count;
    }

    private Dictionary<string, double[]> CreatePooledStateBuffer()
    {
        var outer = new Dictionary<string, double[]>(_statefulComponents.Count);
        foreach (var (name, comp) in _statefulComponents)
            outer[name] = new double[comp.StateVariables.Count];
        return outer;
    }

    // Copy values from `src` into `dst` in place, preserving dst's owned
    // array references. Both buffers must share the same component +
    // state-variable shape (guaranteed by EnsureBuffersAllocated).
    private void CopyStateInto(
        IReadOnlyDictionary<string, double[]> src,
        Dictionary<string, double[]> dst)
    {
        foreach (var (name, _) in _statefulComponents)
        {
            var s = src[name];
            var d = dst[name];
            // Array.Copy is well-vectorized for small double[] (typically
            // VariableCount == 1 today; future-proof for larger vectors).
            Array.Copy(s, d, s.Length);
        }
    }

    // Issue #736 Phase 2 — ensure _state[name] holds a flat double[] of
    // the right size for the named component. Used by Run / RunStreaming
    // / RunAdaptive* during state initialisation. Reuses the existing
    // array when shape matches; allocates a fresh one only when the
    // component's StateVariables count changed (rare).
    private double[] EnsureStateArray(string name, int varCount)
    {
        if (_state.TryGetValue(name, out var existing) && existing.Length == varCount)
            return existing;
        var fresh = new double[varCount];
        _state[name] = fresh;
        return fresh;
    }

    // Issue #736 Phase 2 — shared state-init body used by Run() /
    // RunStreaming() / RunAdaptiveCrankNicolson() / RunAdaptiveCashKarp45().
    // Issue #738 Phase 3 — span-based interaction with the component (no
    // dict allocation at GetInitialState / GetCurrentState / SetState).
    private void InitialiseStateForRun(bool warmStart)
    {
        if (!warmStart)
        {
            // Cold start — initialise from each component's GetInitialState
            // (writes directly into the flat array) and push that back
            // into the component via SetState.
            foreach (var (name, stateful) in _statefulComponents)
            {
                var binding = _bindings[name];
                var arr = EnsureStateArray(name, binding.VariableCount);
                stateful.GetInitialState(arr);
                stateful.SetState(arr);
            }
        }
        else
        {
            // Warm start — pull current state from each component into
            // _state for the inner advance loops. SetState is not called
            // (the component already holds the canonical values).
            foreach (var (name, stateful) in _statefulComponents)
            {
                var binding = _bindings[name];
                var arr = EnsureStateArray(name, binding.VariableCount);
                stateful.GetCurrentState(arr);
            }
        }
    }

    /// <summary>
    /// Register an <see cref="IStatefulComponent"/> with the integrator.
    /// The named component must also have been added to the network
    /// (validated at integration start).
    /// </summary>
    public void RegisterStateful(string componentName, IStatefulComponent stateful)
    {
        ArgumentNullException.ThrowIfNull(componentName);
        ArgumentNullException.ThrowIfNull(stateful);
        if (_statefulComponents.ContainsKey(componentName))
            throw new InvalidOperationException(
                $"Component '{componentName}' already registered as stateful.");
        _statefulComponents[componentName] = stateful;
        // Issue #736 Phase 2 — compute the binding once at registration.
        // The buffer-pool rebuild on the next Advance picks up the new
        // component's StateVariables count from the binding.
        _bindings[componentName] = StateVectorBinding.Compute(componentName, stateful);
    }

    /// <summary>
    /// Sprint SI.W20. Capture a <see cref="SystemSnapshot"/> of every
    /// registered stateful component's current state. Useful for
    /// what-if branching: run forward, capture, run further, restore,
    /// run a different scenario from the checkpoint.
    /// </summary>
    public SystemSnapshot CaptureSnapshot()
    {
        // Issue #738 Phase 3 — pull state via the span-based getter, then
        // materialise the SystemSnapshot dict shape via the cached binding.
        var states = new Dictionary<string, IReadOnlyDictionary<string, double>>(
            _statefulComponents.Count);
        foreach (var (name, stateful) in _statefulComponents)
        {
            var binding = _bindings[name];
            var buf = new double[binding.VariableCount];
            stateful.GetCurrentState(buf);
            var dict = new Dictionary<string, double>(binding.VariableCount);
            for (int i = 0; i < buf.Length; i++)
                dict[binding.VariableNames[i]] = buf[i];
            states[name] = dict;
        }
        return new SystemSnapshot(states);
    }

    /// <summary>
    /// Sprint SI.W20. Restore every stateful component to the state
    /// recorded in <paramref name="snapshot"/>. The snapshot must
    /// contain an entry for every currently-registered stateful
    /// component (else the missing component's state is left
    /// untouched).
    /// </summary>
    public void RestoreSnapshot(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var (name, stateful) in _statefulComponents)
            if (snapshot.ComponentStates.TryGetValue(name, out var captured))
            {
                // Issue #736 Phase 2 — write captured values into the
                // flat array via the cached binding.
                // Issue #738 Phase 3 — SetState takes ReadOnlySpan<double>.
                var binding = _bindings[name];
                var arr = EnsureStateArray(name, binding.VariableCount);
                binding.CopyDictToArray(captured, arr);
                stateful.SetState(arr);
            }
    }

    /// <summary>
    /// Run the explicit-Euler time-domain integration over the closed
    /// interval [t_0, t_end] with fixed step Δt. The tick count is
    /// <c>(int)Math.Round((tEnd - t0) / dt) + 1</c>, deterministic
    /// across FP-rounding regimes (issue #553 / audit C3; closes #547).
    /// </summary>
    /// <param name="t0_s">Start time [s].</param>
    /// <param name="tEnd_s">End time [s].</param>
    /// <param name="dt_s">Time step [s].</param>
    /// <param name="useIterativeSolve">When true, the algebraic network
    /// solve at each tick uses <see cref="ComponentNetwork.SolveIterative"/>;
    /// when false (default), uses the acyclic <see cref="ComponentNetwork.Solve"/>.</param>
    /// <returns>Per-tick port + state snapshot history.</returns>
    public IReadOnlyList<TimeHistorySnapshot> Run(
        double t0_s, double tEnd_s, double dt_s,
        bool useIterativeSolve = false,
        IntegrationMethod method = IntegrationMethod.ExplicitEuler,
        bool warmStart = false)
    {
        if (dt_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(dt_s),
                "dt_s must be > 0.");
        if (tEnd_s <= t0_s)
            throw new ArgumentOutOfRangeException(nameof(tEnd_s),
                $"tEnd_s ({tEnd_s:F3}) must be > t0_s ({t0_s:F3}).");

        // 1. Initialise state vectors.
        //    Sprint SI.W20 — warmStart=true preserves the existing
        //    component state (e.g. after RestoreSnapshot or a previous
        //    Run). When false (default), each Run starts fresh from
        //    GetInitialState().
        //    Issue #736 Phase 2 — _state holds flat double[] per component;
        //    StateVectorBinding maps StateVariables names → indices.
        InitialiseStateForRun(warmStart);

        // 2. Walk time, recording per-tick snapshots.
        ResetEventState();
        var history = new List<TimeHistorySnapshot>();
        double previousTickTime = t0_s;
        // Issue #553 / audit C3 (generalises #547). The previous
        // FP-accumulated form `for (double t = t0_s; t < tEnd_s; t += dt_s)`
        // produced a tick count that varied with host FP rounding
        // (e.g. 10·0.1 = 0.9999… undershot tEnd, 20·0.05 = 1.0000007
        // overshot). The integer-tick form below pins the count to the
        // closed interval [t0_s, tEnd_s] with N+1 samples, so identical
        // (t0, tEnd, dt) inputs always yield the same number of snapshots
        // regardless of FP error in the running sum.
        int nTicks = (int)Math.Round((tEnd_s - t0_s) / dt_s) + 1;
        for (int tickIdx = 0; tickIdx < nTicks; tickIdx++)
        {
            double t = t0_s + tickIdx * dt_s;
            _cnCurrentTickIndex = tickIdx; // Issue #628 — surfaced on CN ceiling hits.
            // Sprint SI.W11 — refresh time-varying external inputs first
            // so the solve uses current-tick values.
            _network.RefreshTimeVaryingInputsAt(t);
            // Sprint SI.W17 — apply any scheduled fault transitions
            // whose timestamp is now ≤ t.
            _network.ApplyScheduledFaultsAt(t);

            // a. Algebraic network solve at current time + state.
            var ports = useIterativeSolve
                ? _network.SolveIterative()
                : _network.Solve();

            // b. Capture snapshot.
            history.Add(new TimeHistorySnapshot(
                Time_s:      t,
                PortValues:  ports,
                StateValues: CloneStateMap(_state)));

            // b.1. Sprint SI.W23 — event-detection check. Stop the loop
            //      when a terminal event fires.
            if (CheckEvents(ports, t, previousTickTime))
            {
                return history;
            }
            previousTickTime = t;

            // c. Compute derivatives for each stateful component +
            //    advance state. Sprint SI.W6 dispatches by method.
            switch (method)
            {
                case IntegrationMethod.ExplicitEuler:
                    AdvanceExplicitEuler(ports, dt_s);
                    break;
                case IntegrationMethod.Rk4:
                    AdvanceRk4(useIterativeSolve, dt_s);
                    break;
                case IntegrationMethod.CrankNicolson:
                    AdvanceCrankNicolson(ports, useIterativeSolve, dt_s);
                    break;
                case IntegrationMethod.CashKarpRk45Adaptive:
                    throw new ArgumentOutOfRangeException(nameof(method),
                        "CashKarpRk45Adaptive is only supported by "
                      + "RunAdaptiveCashKarp45 — the fixed-step Run path "
                      + "cannot honour a variable-dt scheme. See issue #492.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(method),
                        $"Unknown integration method {method}.");
            }
        }
        return history;
    }

    // ── Sprint SI.W30 — Streaming history (callback per tick) ──────────

    /// <summary>
    /// Sprint SI.W30. Streaming variant of <see cref="Run"/> that
    /// invokes <paramref name="onSnapshot"/> per tick instead of
    /// accumulating into a history list. The callback receives the
    /// freshly-built <see cref="TimeHistorySnapshot"/> + decides what
    /// to do with it (CSV stream-write, log, drop after a window,
    /// pipe to an analytic).
    /// </summary>
    /// <param name="t0_s">Start time [s].</param>
    /// <param name="tEnd_s">End time [s].</param>
    /// <param name="dt_s">Time step [s].</param>
    /// <param name="onSnapshot">
    /// Callback invoked per tick. Must not throw — exceptions surface
    /// to the caller via the integrator stack but corrupt the run.
    /// </param>
    /// <param name="useIterativeSolve">When true, the algebraic network
    /// solve at each tick uses <see cref="ComponentNetwork.SolveIterative"/>;
    /// when false (default), uses the acyclic <see cref="ComponentNetwork.Solve"/>.</param>
    /// <param name="method">ODE-stepper choice (Sprint SI.W6 dispatcher).</param>
    /// <param name="warmStart">When true, preserves existing stateful-
    /// component state across runs (Sprint SI.W20 what-if branching).</param>
    /// <returns>The number of snapshots emitted via the callback. Doubles
    /// as a tick count.</returns>
    /// <remarks>
    /// Use case: simulations whose horizon × tick-rate produces a history
    /// too large for memory (e.g. 1-hour transient at 1 kHz → 3.6 M
    /// snapshots × ~1 KB each = ~3.6 GB if accumulated). The streaming
    /// callback lets a caller emit a CSV row + drop the snapshot on
    /// every tick, keeping memory constant. Companion to the SI.W15
    /// `CsvTimeSeriesExporter` (which works on a complete history); a
    /// future <c>CsvTimeSeriesExporter.WriteSnapshot</c> single-row
    /// writer pairs cleanly with this method.
    ///
    /// Determinism: same network + same dt + same initial state +
    /// same callback → bit-identical sequence of callback invocations.
    /// The callback's own side effects are the caller's responsibility.
    /// </remarks>
    public int RunStreaming(
        double t0_s, double tEnd_s, double dt_s,
        Action<TimeHistorySnapshot> onSnapshot,
        bool useIterativeSolve = false,
        IntegrationMethod method = IntegrationMethod.ExplicitEuler,
        bool warmStart = false)
    {
        if (onSnapshot is null) throw new ArgumentNullException(nameof(onSnapshot));
        if (dt_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(dt_s),
                "dt_s must be > 0.");
        if (tEnd_s <= t0_s)
            throw new ArgumentOutOfRangeException(nameof(tEnd_s),
                $"tEnd_s ({tEnd_s:F3}) must be > t0_s ({t0_s:F3}).");

        // Mirror Run()'s state-init path. Issue #736 Phase 2 — flat-array
        // state init shared with Run() / RunAdaptiveCrankNicolson() /
        // RunAdaptiveCashKarp45() in one helper.
        InitialiseStateForRun(warmStart);

        ResetEventState();
        int emittedCount = 0;
        double previousTickTime = t0_s;

        // Issue #553 / audit C3 (generalises #547). Integer-tick form
        // matches Run() above: closed interval [t0_s, tEnd_s] with N+1
        // samples, deterministic regardless of host FP rounding in the
        // running sum.
        int nTicks = (int)Math.Round((tEnd_s - t0_s) / dt_s) + 1;
        for (int tickIdx = 0; tickIdx < nTicks; tickIdx++)
        {
            double t = t0_s + tickIdx * dt_s;
            _cnCurrentTickIndex = tickIdx; // Issue #628 — surfaced on CN ceiling hits.
            _network.RefreshTimeVaryingInputsAt(t);
            _network.ApplyScheduledFaultsAt(t);

            var ports = useIterativeSolve
                ? _network.SolveIterative()
                : _network.Solve();

            var snap = new TimeHistorySnapshot(
                Time_s:      t,
                PortValues:  ports,
                StateValues: CloneStateMap(_state));
            onSnapshot(snap);
            emittedCount++;

            // Event-detection check (Sprint SI.W23). Terminal event still
            // stops the streaming loop.
            if (CheckEvents(ports, t, previousTickTime))
            {
                return emittedCount;
            }
            previousTickTime = t;

            switch (method)
            {
                case IntegrationMethod.ExplicitEuler:
                    AdvanceExplicitEuler(ports, dt_s);
                    break;
                case IntegrationMethod.Rk4:
                    AdvanceRk4(useIterativeSolve, dt_s);
                    break;
                case IntegrationMethod.CrankNicolson:
                    AdvanceCrankNicolson(ports, useIterativeSolve, dt_s);
                    break;
                case IntegrationMethod.CashKarpRk45Adaptive:
                    throw new ArgumentOutOfRangeException(nameof(method),
                        "CashKarpRk45Adaptive is only supported by "
                      + "RunAdaptiveCashKarp45 — the fixed-step Run path "
                      + "cannot honour a variable-dt scheme. See issue #492.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(method),
                        $"Unknown integration method {method}.");
            }
        }
        return emittedCount;
    }

    // ── Sprint SI.W21 Crank-Nicolson implicit-trapezoid configuration ──

    /// <summary>
    /// Maximum fixed-point iterations per Crank-Nicolson tick. The
    /// trapezoid rule converges in O(1) iterations for mild-to-moderate
    /// stiffness; 25 is a generous ceiling that catches divergent fits
    /// without locking the simulation.
    /// </summary>
    public const int CrankNicolsonMaxIterations = 25;

    /// <summary>
    /// L∞ convergence tolerance for the Crank-Nicolson fixed-point
    /// inner loop, expressed in absolute state units. Tight enough to
    /// preserve the order-2 accuracy guarantee; loose enough to avoid
    /// quadratic-zone chasing in stiff systems.
    /// </summary>
    public const double CrankNicolsonAbsoluteTolerance = 1.0e-9;

    /// <summary>
    /// Relative-tolerance companion to
    /// <see cref="CrankNicolsonAbsoluteTolerance"/>. Convergence requires
    /// |Δy| ≤ atol + rtol · |y| per state variable. Standard ODE-solver
    /// formulation; mirrors CVODE / DASSL defaults.
    /// </summary>
    public const double CrankNicolsonRelativeTolerance = 1.0e-7;

    private void AdvanceExplicitEuler(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ports,
        double dt_s)
    {
        // Issue #738 Phase 3 — span-based boundary; no temp-dict pool.
        // Per-component derivative buffer is stack-allocated (1-var typical;
        // bound by VariableCount which is pinned at registration time).
        EnsureBuffersAllocated();
        Span<double> deriv = stackalloc double[8]; // sized for the largest
                                                    // current StateVariables.Count;
                                                    // sliced below to fit.
        foreach (var (name, stateful) in _statefulComponents)
        {
            // Faulted components don't accumulate state — their Evaluate
            // is skipped in ComponentNetwork.Solve, leaving LastResolvedInputs
            // empty for this component (so ComputeDerivatives would throw
            // on any port lookup). Freeze state at the current value instead.
            if (_network.IsComponentFaulted(name)) continue;

            var stateArr = _state[name];
            var dSlice   = deriv[..stateArr.Length];
            var portInputs  = ExtractInputs(name, ports);
            var portOutputs = ports[name];
            stateful.ComputeDerivatives(stateArr, portInputs, portOutputs, dSlice);

            // y(t+dt) = y(t) + dt · dy/dt, written in-place into the
            // flat state array.
            for (int i = 0; i < stateArr.Length; i++)
                stateArr[i] += dt_s * dSlice[i];

            stateful.SetState(stateArr);
        }
    }

    private void AdvanceRk4(bool useIterativeSolve, double dt_s)
    {
        // Sprint SI.W6 — RK4 advance.
        //   k1 = f(t,         y)
        //   k2 = f(t + dt/2,  y + dt/2 · k1)
        //   k3 = f(t + dt/2,  y + dt/2 · k2)
        //   k4 = f(t + dt,    y + dt · k3)
        //   y(t+dt) = y(t) + dt/6 · (k1 + 2·k2 + 2·k3 + k4)
        //
        // To evaluate f(t, y) at intermediate state values we need to
        // perturb each stateful component's state, re-solve the network,
        // and read derivatives. We snapshot the original state per
        // component, then sweep through k1..k4 evaluations.
        EnsureBuffersAllocated();
        SnapshotStateInto(_origBuf!);

        // k1 — at original state.
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf1!);

        // k2 — at y + dt/2 · k1.
        ApplyPerturbationInPlace(_origBuf!, _kBuf1!, dt_s / 2.0);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf2!);

        // k3 — at y + dt/2 · k2.
        ApplyPerturbationInPlace(_origBuf!, _kBuf2!, dt_s / 2.0);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf3!);

        // k4 — at y + dt · k3.
        ApplyPerturbationInPlace(_origBuf!, _kBuf3!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf4!);

        // Combine: y(t+dt) = y(t) + dt/6 · (k1 + 2k2 + 2k3 + k4).
        // Issue #736 Phase 2 — write into _state's owned flat arrays
        // walking raw indices.
        // Issue #738 Phase 3 — SetState takes ReadOnlySpan<double> directly.
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dst  = _state[name];
            var orig = _origBuf![name];
            var k1 = _kBuf1![name];
            var k2 = _kBuf2![name];
            var k3 = _kBuf3![name];
            var k4 = _kBuf4![name];
            int n  = dst.Length;
            for (int i = 0; i < n; i++)
            {
                double dy = dt_s / 6.0 * (k1[i] + 2.0 * k2[i] + 2.0 * k3[i] + k4[i]);
                dst[i] = orig[i] + dy;
            }
            stateful.SetState(dst);
        }
    }

    /// <summary>
    /// Sprint SI.W22 adaptive-step diagnostic — number of Newton iterations
    /// the most-recent <see cref="AdvanceCrankNicolson"/> call took.
    /// Set by the inner loop; consumed by
    /// <see cref="RunAdaptiveCrankNicolson"/> to tune <c>dt</c>. -1
    /// when no CN step has run yet. Typically 1–2 on smooth/linear systems.
    /// </summary>
    public int LastCrankNicolsonIterations { get; private set; } = -1;

    // Issue #628 — Crank-Nicolson ceiling-hit telemetry. Records every
    // tick where the fixed-point inner loop exhausted CrankNicolsonMaxIterations
    // without the residual dropping below tolerance. Cleared at the
    // start of each Run / RunAdaptive* invocation.
    private readonly List<CrankNicolsonCeilingHit> _cnCeilingHits = new();
    private int _cnCurrentTickIndex = -1;

    /// <summary>
    /// Issue #628 — structured records of every Crank-Nicolson tick
    /// where the fixed-point inner loop exhausted
    /// <see cref="CrankNicolsonMaxIterations"/> without converging.
    /// Empty when no ceiling-hit has occurred since the last Run /
    /// RunAdaptive* invocation. Diagnostic surface for the
    /// silent-non-convergence failure mode documented in ADR-031.
    /// </summary>
    public IReadOnlyList<CrankNicolsonCeilingHit> CrankNicolsonCeilingHits => _cnCeilingHits;

    /// <summary>
    /// Issue #628 — convenience count over
    /// <see cref="CrankNicolsonCeilingHits"/>. Zero in the healthy case.
    /// Adaptive solvers (<see cref="RunAdaptiveCrankNicolson"/>) shrink
    /// <c>dt</c> on a ceiling hit, so a non-zero count there says
    /// "the controller had to back off"; in fixed-step <see cref="Run"/>
    /// a non-zero count says "the integration result is unreliable."
    /// </summary>
    public int CrankNicolsonCeilingHitCount => _cnCeilingHits.Count;

    // ── Sprint SI.W23 — Event detection ─────────────────────────────────

    private readonly List<EventDefinition> _events = new();
    private readonly Dictionary<string, double> _previousPredicate = new();
    private readonly List<DetectedEvent> _detectedEvents = new();
    private bool _terminalEventFired;

    /// <summary>
    /// All events detected during the most recent Run / RunAdaptive*
    /// invocation. Sprint SI.W23. Cleared at the start of each new run.
    /// </summary>
    public IReadOnlyList<DetectedEvent> LastDetectedEvents => _detectedEvents;

    /// <summary>
    /// Register a zero-crossing event the integrator should watch for
    /// on every tick. The event fires when its predicate changes sign
    /// between consecutive ticks (in the requested direction); the
    /// crossing time is linear-interpolated from the predicate values
    /// at the two surrounding ticks. Sprint SI.W23.
    /// </summary>
    public void RegisterEvent(EventDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        foreach (var existing in _events)
        {
            if (existing.Name == definition.Name)
                throw new InvalidOperationException(
                    $"Event '{definition.Name}' already registered. "
                  + "Event names must be unique within a single integrator.");
        }
        _events.Add(definition);
    }

    /// <summary>
    /// Reset event state at the start of every Run / RunAdaptive*
    /// invocation. Clears the detected-events list + the per-event
    /// previous-predicate map. Called by Run + RunAdaptiveCrankNicolson.
    /// </summary>
    private void ResetEventState()
    {
        _previousPredicate.Clear();
        _detectedEvents.Clear();
        _terminalEventFired = false;
        // Issue #628 — also clear CN ceiling-hit telemetry per Run.
        _cnCeilingHits.Clear();
        _cnCurrentTickIndex = -1;
    }

    /// <summary>
    /// Per-tick event check. Compares each registered event's predicate
    /// against its previous-tick value; fires the event when a sign-
    /// change in the requested direction occurs. Returns true iff the
    /// run loop should stop (a terminal event fired).
    /// </summary>
    private bool CheckEvents(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ports,
        double currentTime,
        double previousTime)
    {
        if (_events.Count == 0) return false;

        foreach (var ev in _events)
        {
            double current = ev.Predicate(ports);
            if (!_previousPredicate.TryGetValue(ev.Name, out var previous))
            {
                _previousPredicate[ev.Name] = current;
                continue;  // no previous value → no crossing on first tick
            }

            bool crossing = (previous < 0 && current >= 0)
                         || (previous > 0 && current <= 0)
                         || (previous == 0 && current != 0);
            if (crossing)
            {
                EventDirection observed = (current > previous)
                    ? EventDirection.Rising
                    : EventDirection.Falling;

                bool matches = ev.Direction == EventDirection.Either
                            || ev.Direction == observed;
                if (matches)
                {
                    // Linear interpolation for the crossing time.
                    // y(t*) = 0  →  t* = t_prev + (t_curr - t_prev) · (-prev) / (curr - prev).
                    double tCross = previousTime;
                    double denom = current - previous;
                    if (denom != 0)
                        tCross = previousTime + (currentTime - previousTime) * (-previous / denom);

                    _detectedEvents.Add(new DetectedEvent(
                        Name:          ev.Name,
                        Time_s:        tCross,
                        Direction:     observed,
                        PreviousValue: previous,
                        CurrentValue:  current));

                    if (ev.Terminal) _terminalEventFired = true;
                }
            }
            _previousPredicate[ev.Name] = current;
        }
        return _terminalEventFired;
    }

    private void AdvanceCrankNicolson(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> portsAtTn,
        bool useIterativeSolve,
        double dt_s)
    {
        // Crank-Nicolson implicit trapezoid solved by Newton-Raphson (CN-NEWTON):
        //   G(y) = y − y_n − (dt/2)·[f(t,y_n) + f(t+dt,y)] = 0
        //   J_G  = I − (dt/2)·∂f/∂y  (estimated column-wise by finite differences)
        //   y^(k+1) = y^(k) − J_G^{−1}·G(y^(k))
        //
        // For linear ODEs (y′ = −λy), Newton converges in 1 iteration to the
        // A-stable recurrence y_{n+1} = y_n·(1−λdt/2)/(1+λdt/2), irrespective
        // of λ·dt. The old fixed-point iteration had the stability bound
        // |λdt/2| < 1 (same as explicit Euler); Newton removes it entirely.
        //
        // Cost per iteration: N+1 network solves (1 base + N finite-difference
        // columns), vs 1 for fixed-point. Newton compensates by converging in
        // ≤ 2 iterations on smooth systems; fixed-point needed up to 25 on
        // moderately stiff ones and diverged on highly stiff ones.
        //
        // f(t, y_n) is computed once before the loop; it does not change.
        // _kBuf1 is reused as the finite-difference scratch buffer (RK4 / CK45
        // never overlap with CN in the same tick).

        EnsureBuffersAllocated();
        SnapshotStateInto(_yTBuf!);
        ComputeDerivativesAtCurrentStateInto(portsAtTn, _fTBuf!);

        var yT    = _yTBuf!;
        var fT    = _fTBuf!;
        var yPred = _yPredBuf!;
        var yNext = _yNextBuf!;
        var fEnd  = _fEndBuf!;

        // Euler predictor: y^(0) = y_n + dt·f(t, y_n).
        foreach (var (name, _) in _statefulComponents)
        {
            var pred = yPred[name]; var yTn = yT[name]; var fTn = fT[name];
            int n = pred.Length;
            for (int i = 0; i < n; i++) pred[i] = yTn[i] + dt_s * fTn[i];
        }

        // Ensure Newton flat-buffer sizes match the total state dimension N.
        int N = _cnTotalStateDim;
        if (_cnResidualBuf == null || _cnResidualBuf.Length < N)
            _cnResidualBuf = new double[N];
        if (_cnNewtonStepBuf == null || _cnNewtonStepBuf.Length < N)
            _cnNewtonStepBuf = new double[N];
        if (_cnJacobianBuf == null || _cnJacobianBuf.Length < N * N)
            _cnJacobianBuf = new double[System.Math.Max(N * N, 1)];

        var order   = _cnStateOrder!;
        double halfDt = 0.5 * dt_s;
        // sqrt(double.Epsilon) — standard forward finite-difference step size.
        const double sqrtEps = 1.4901161193847656e-8;

        int    convergedAt    = CrankNicolsonMaxIterations;
        double lastMaxResidual = double.NaN;

        for (int iter = 0; iter < CrankNicolsonMaxIterations; iter++)
        {
            // Push y^(k) (= yPred after ping-pong) into _state and each component.
            foreach (var (name, stateful) in _statefulComponents)
            {
                var dst = _state[name]; var src = yPred[name];
                Array.Copy(src, dst, src.Length);
                stateful.SetState(dst);
            }

            // f(t+dt, y^(k)).
            ComputeAllDerivativesInto(useIterativeSolve, fEnd);

            // Residual: G_i = y^(k)_i − y_n_i − (dt/2)·(f_n_i + f(y^(k))_i).
            foreach (var (name, ofs, cnt) in order)
            {
                var yk = yPred[name]; var yn = yT[name];
                var fn = fT[name];   var fe = fEnd[name];
                for (int i = 0; i < cnt; i++)
                    _cnResidualBuf![ofs + i] = yk[i] - yn[i] - halfDt * (fn[i] + fe[i]);
            }

            // Finite-difference Jacobian of f at y^(k), assembled column by column.
            // _state currently holds y^(k) (set above); we perturb one entry,
            // re-solve, observe Δf, then restore.
            for (int j = 0; j < N; j++)
            {
                var (cName, cOfs, _) = FindCnStateEntry(order, j);
                int localJ = j - cOfs;

                double yj = _state[cName][localJ];
                double hj = sqrtEps * System.Math.Max(1.0, System.Math.Abs(yj));

                _state[cName][localJ] = yj + hj;
                _statefulComponents[cName].SetState(_state[cName]);

                ComputeAllDerivativesInto(useIterativeSolve, _kBuf1!);

                _state[cName][localJ] = yj;
                _statefulComponents[cName].SetState(_state[cName]);

                // Column j of J_G = I − (dt/2)·J_f.
                foreach (var (nm, nmOfs, nmCnt) in order)
                {
                    var fBase = fEnd[nm]; var fPert = _kBuf1![nm];
                    for (int i = 0; i < nmCnt; i++)
                    {
                        int row = nmOfs + i;
                        double jfij = (fPert[i] - fBase[i]) / hj;
                        _cnJacobianBuf![row * N + j] =
                            (row == j ? 1.0 : 0.0) - halfDt * jfij;
                    }
                }
            }

            // Solve J_G · δy = G  (Gaussian elimination, partial pivoting).
            // Both _cnJacobianBuf and _cnResidualBuf are modified in-place;
            // both are recomputed at the start of the next iteration.
            SolveDenseLinear(N, _cnJacobianBuf!, _cnResidualBuf!, _cnNewtonStepBuf!);

            // y^(k+1) = y^(k) − δy; compute convergence criterion.
            double maxResidual = 0.0;
            foreach (var (name, ofs, cnt) in order)
            {
                var nxt = yNext[name]; var pred = yPred[name];
                for (int i = 0; i < cnt; i++)
                {
                    double newValue = pred[i] - _cnNewtonStepBuf![ofs + i];
                    nxt[i] = newValue;
                    double absDelta = System.Math.Abs(newValue - pred[i]);
                    double tol = CrankNicolsonAbsoluteTolerance
                               + CrankNicolsonRelativeTolerance * System.Math.Abs(newValue);
                    double scaled = tol > 0 ? absDelta / tol : absDelta;
                    if (scaled > maxResidual) maxResidual = scaled;
                }
            }

            // Ping-pong: swap local refs so the next iteration starts from y^(k+1).
            (yPred, yNext) = (yNext, yPred);
            lastMaxResidual = maxResidual;
            if (maxResidual <= 1.0)
            {
                convergedAt = iter + 1;
                break;
            }
        }
        LastCrankNicolsonIterations = convergedAt;

        // Issue #628 — record a ceiling hit when the loop exhausted
        // CrankNicolsonMaxIterations without converging.
        if (convergedAt == CrankNicolsonMaxIterations)
        {
            _cnCeilingHits.Add(new CrankNicolsonCeilingHit(
                TickIndex:         _cnCurrentTickIndex,
                Dt_s:              dt_s,
                MaxResidualAtExit: lastMaxResidual));
        }

        // Commit y(t+dt) (held in yPred after the final swap) to every stateful
        // component and the integrator's own _state cache.
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dst = _state[name]; var src = yPred[name];
            Array.Copy(src, dst, src.Length);
            stateful.SetState(dst);
        }
    }

    // Find the ordering entry that owns flat state-vector index j.
    private static (string Name, int Offset, int Count) FindCnStateEntry(
        (string Name, int Offset, int Count)[] order, int j)
    {
        foreach (var entry in order)
            if (j < entry.Offset + entry.Count) return entry;
        throw new System.InvalidOperationException(
            $"CN flat state index {j} is out of range.");
    }

    // Gaussian elimination with partial pivoting: solves A·x = b.
    // A (n×n, row-major) and b (n-vector) are modified in place.
    // Solution written to x.
    private static void SolveDenseLinear(int n, double[] A, double[] b, double[] x)
    {
        if (n == 0) return;
        if (n == 1)
        {
            x[0] = System.Math.Abs(A[0]) > 1e-300 ? b[0] / A[0] : 0.0;
            return;
        }

        // Forward elimination.
        for (int col = 0; col < n; col++)
        {
            // Partial pivot: find row with largest |A[row, col]| at or below diagonal.
            int pivotRow = col;
            double maxAbs = System.Math.Abs(A[col * n + col]);
            for (int row = col + 1; row < n; row++)
            {
                double abs = System.Math.Abs(A[row * n + col]);
                if (abs > maxAbs) { maxAbs = abs; pivotRow = row; }
            }

            if (pivotRow != col)
            {
                for (int c = 0; c < n; c++)
                    (A[col * n + c], A[pivotRow * n + c]) =
                        (A[pivotRow * n + c], A[col * n + c]);
                (b[col], b[pivotRow]) = (b[pivotRow], b[col]);
            }

            double pivot = A[col * n + col];
            if (System.Math.Abs(pivot) < 1e-300) continue; // near-singular column

            for (int row = col + 1; row < n; row++)
            {
                double factor = A[row * n + col] / pivot;
                A[row * n + col] = 0.0;
                for (int c = col + 1; c < n; c++)
                    A[row * n + c] -= factor * A[col * n + c];
                b[row] -= factor * b[col];
            }
        }

        // Back-substitution.
        for (int row = n - 1; row >= 0; row--)
        {
            double sum = b[row];
            for (int c = row + 1; c < n; c++) sum -= A[row * n + c] * x[c];
            double diag = A[row * n + row];
            x[row] = System.Math.Abs(diag) > 1e-300 ? sum / diag : 0.0;
        }
    }

    // ── Sprint SI.W22 — Adaptive step-size for Crank-Nicolson ──────────

    /// <summary>
    /// Adaptive step-size controller for the Crank-Nicolson integrator.
    /// Uses the fixed-point iteration count as a local-stiffness proxy:
    /// few iterations → step too small, grow <c>dt</c>; many iterations
    /// or non-convergence → step too big, shrink <c>dt</c>.
    /// </summary>
    /// <param name="t0_s">Start time [s].</param>
    /// <param name="tEnd_s">End time [s].</param>
    /// <param name="dtInitial_s">Initial step [s] — also the first attempt.</param>
    /// <param name="dtMin_s">Floor for <c>dt</c>. The controller refuses
    /// to shrink below this; if the step at <c>dt_min</c> still doesn't
    /// converge, the iteration count is accepted anyway (the simulation
    /// continues with a warning-grade non-convergent step).</param>
    /// <param name="dtMax_s">Ceiling for <c>dt</c>. The controller refuses
    /// to grow above this; protects against runaway dt on long quiescent
    /// stretches where the network barely changes.</param>
    /// <param name="targetIterations">Sweet-spot iteration count — the
    /// controller tries to keep CN iterations near this value. Default
    /// 5 (most CN ticks converge in 2-4; 5 is the "no work to do" floor).</param>
    /// <param name="useIterativeSolve">When true, the inner algebraic
    /// network solve uses Gauss-Seidel <see cref="ComponentNetwork.SolveIterative"/>;
    /// when false (default), the acyclic <see cref="ComponentNetwork.Solve"/>.</param>
    /// <param name="warmStart">When true, preserves existing stateful-
    /// component state (e.g. after RestoreSnapshot); when false (default),
    /// each call reinitialises from GetInitialState().</param>
    /// <returns>Per-tick port + state snapshot history. Ticks land at
    /// non-uniform times because <c>dt</c> varies.</returns>
    public IReadOnlyList<TimeHistorySnapshot> RunAdaptiveCrankNicolson(
        double t0_s, double tEnd_s,
        double dtInitial_s,
        double dtMin_s,
        double dtMax_s,
        int targetIterations = 5,
        bool useIterativeSolve = false,
        bool warmStart = false)
    {
        if (dtInitial_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtInitial_s),
                "dtInitial_s must be > 0.");
        if (dtMin_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtMin_s),
                "dtMin_s must be > 0.");
        if (dtMax_s < dtMin_s)
            throw new ArgumentOutOfRangeException(nameof(dtMax_s),
                $"dtMax_s ({dtMax_s:F6}) must be ≥ dtMin_s ({dtMin_s:F6}).");
        if (tEnd_s <= t0_s)
            throw new ArgumentOutOfRangeException(nameof(tEnd_s),
                $"tEnd_s ({tEnd_s:F3}) must be > t0_s ({t0_s:F3}).");
        if (targetIterations < 1 || targetIterations >= CrankNicolsonMaxIterations)
            throw new ArgumentOutOfRangeException(nameof(targetIterations),
                $"targetIterations must be in [1, {CrankNicolsonMaxIterations - 1}].");

        // 1. Initialise state (warmStart parity with Run()). Issue #736
        //    Phase 2 — flat-array state init via shared helper.
        InitialiseStateForRun(warmStart);

        // 2. Walk time with adaptive dt.
        ResetEventState();
        var history = new List<TimeHistorySnapshot>();
        double t = t0_s;
        double previousTickTime = t0_s;
        double dt = Math.Clamp(dtInitial_s, dtMin_s, dtMax_s);
        int adaptiveCnTickIdx = 0;

        while (t < tEnd_s)
        {
            // Don't overshoot the end-time horizon.
            double dtThisStep = Math.Min(dt, tEnd_s - t);
            if (dtThisStep <= 0) break;

            _cnCurrentTickIndex = adaptiveCnTickIdx++; // Issue #628 — surfaced on CN ceiling hits.
            _network.RefreshTimeVaryingInputsAt(t);
            _network.ApplyScheduledFaultsAt(t);

            var ports = useIterativeSolve
                ? _network.SolveIterative()
                : _network.Solve();

            history.Add(new TimeHistorySnapshot(
                Time_s:      t,
                PortValues:  ports,
                StateValues: CloneStateMap(_state)));

            // Sprint SI.W23 — event-detection check. Stop the run when
            // a terminal event fires (history already includes this tick).
            if (CheckEvents(ports, t, previousTickTime))
            {
                return history;
            }
            previousTickTime = t;

            AdvanceCrankNicolson(ports, useIterativeSolve, dtThisStep);
            int iters = LastCrankNicolsonIterations;

            // Issue #553 / audit C3 — snap the final step to land exactly
            // at tEnd_s. Without this, FP accumulation may stop one step
            // short (leaving tEnd_s - t = epsilon > 0) or take a near-
            // zero final step (t + dtThisStep barely past tEnd_s), neither
            // bit-deterministic across runs. The Math.Min above already
            // shrinks an overshooting dt; this assignment removes any
            // residual FP roundoff so t lands bit-exactly on tEnd_s.
            if (t + dtThisStep >= tEnd_s)
            {
                t = tEnd_s;
            }
            else
            {
                t += dtThisStep;
            }

            // 3. Tune dt for next tick based on iteration count.
            //    iters ≤ target/2     → grow dt by ×2 (capped at dtMax)
            //    iters ≤ target       → grow dt by ×1.2
            //    iters ≤ target × 2   → keep dt unchanged
            //    iters >  target × 2  → shrink dt by ×0.5 (floored at dtMin)
            //    Non-convergence (iters == CrankNicolsonMaxIterations)
            //                          → shrink dt by ×0.5 unconditionally
            if (iters >= CrankNicolsonMaxIterations)
            {
                dt = Math.Max(dt * 0.5, dtMin_s);
            }
            else if (iters <= targetIterations / 2)
            {
                dt = Math.Min(dt * 2.0, dtMax_s);
            }
            else if (iters <= targetIterations)
            {
                dt = Math.Min(dt * 1.2, dtMax_s);
            }
            else if (iters > targetIterations * 2)
            {
                dt = Math.Max(dt * 0.5, dtMin_s);
            }
            // else: keep dt unchanged.
        }

        // Issue #553 / audit C3 — defensive endpoint snap. The clamp +
        // ternary assignment inside the loop should land here exactly
        // under IEEE 754 for any reasonable (t0, tEnd, dt) inputs, but
        // this assignment removes any residual roundoff so the final
        // snapshot's Time_s field carries the exact endpoint value
        // callers asked for.
        t = tEnd_s;

        // Refresh time-varying external inputs + scheduled faults at the exact
        // end time so the final snapshot's port values are consistent with its
        // Time_s = tEnd_s. The in-loop ticks refresh before solving; the final
        // solve must too, otherwise it echoes the previous tick's inputs.
        _network.RefreshTimeVaryingInputsAt(t);
        _network.ApplyScheduledFaultsAt(t);

        // Capture final snapshot at t_end.
        var finalPorts = useIterativeSolve
            ? _network.SolveIterative()
            : _network.Solve();
        history.Add(new TimeHistorySnapshot(
            Time_s:      t,
            PortValues:  finalPorts,
            StateValues: CloneStateMap(_state)));

        return history;
    }

    // ── Buffered helpers (#610) ─────────────────────────────────────
    // All four helpers write into a caller-provided pre-allocated
    // buffer instead of allocating a fresh `Dictionary<string,
    // Dictionary<string, double>>`. Each buffer's outer + inner dicts
    // are owned by the integrator (see CreatePooledStateBuffer) so
    // value overwrites do not realloc.

    private void ComputeDerivativesAtCurrentStateInto(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ports,
        Dictionary<string, double[]> destBuf)
    {
        // f(t, y(t)) at the start-of-tick state — we already have the
        // ports map from the calling Run loop, no re-solve needed.
        // Faulted components: derivatives are zero (state frozen) — their
        // Evaluate is skipped so LastResolvedInputs is empty for them.
        // Issue #738 Phase 3 — span-based ComputeDerivatives; the
        // destBuf entry is the derivative span itself.
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dArr = destBuf[name];
            if (_network.IsComponentFaulted(name))
            {
                Array.Clear(dArr, 0, dArr.Length);
                continue;
            }
            var stateArr = _state[name];
            var portInputs  = ExtractInputs(name, ports);
            var portOutputs = ports[name];
            stateful.ComputeDerivatives(stateArr, portInputs, portOutputs, dArr);
        }
    }

    private void SnapshotStateInto(Dictionary<string, double[]> destBuf)
    {
        // Issue #736 Phase 2 — flat-array Array.Copy. The per-component
        // arrays are sized identically (binding pins shape at registration).
        foreach (var (name, _) in _statefulComponents)
        {
            var src = _state[name];
            var dst = destBuf[name];
            Array.Copy(src, dst, src.Length);
        }
    }

    private void ApplyPerturbationInPlace(
        Dictionary<string, double[]> baseState,
        Dictionary<string, double[]> k,
        double step)
    {
        // _state's flat arrays are owned + reused (see Run / RunStreaming
        // state-init), so we overwrite values in place instead of
        // allocating a fresh perturbation buffer per component per call.
        // Issue #738 Phase 3 — SetState takes ReadOnlySpan<double> directly.
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dst = _state[name];
            var bs  = baseState[name];
            var ks  = k[name];
            int n = dst.Length;
            for (int i = 0; i < n; i++)
                dst[i] = bs[i] + step * ks[i];
            stateful.SetState(dst);
        }
    }

    private void ComputeAllDerivativesInto(
        bool useIterativeSolve,
        Dictionary<string, double[]> destBuf)
    {
        // Re-solve the algebraic network at the current state, then
        // gather derivatives for every stateful component into destBuf.
        // Faulted components: derivatives are zero (state frozen).
        // Issue #738 Phase 3 — span-based ComputeDerivatives writes into
        // destBuf[name] directly.
        var ports = useIterativeSolve
            ? _network.SolveIterative()
            : _network.Solve();
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dArr = destBuf[name];
            if (_network.IsComponentFaulted(name))
            {
                Array.Clear(dArr, 0, dArr.Length);
                continue;
            }
            var stateArr = _state[name];
            var portInputs  = ExtractInputs(name, ports);
            var portOutputs = ports[name];
            stateful.ComputeDerivatives(stateArr, portInputs, portOutputs, dArr);
        }
    }

    // ── Internals ──────────────────────────────────────────────────────

    // Cached empty input map for the ExtractInputs fallback path —
    // the previous code allocated a fresh `new Dictionary<>()` per
    // missed lookup, which fired once per stateful component per tick
    // when LastResolvedInputs lagged. Reusing one read-only shared
    // instance saves an allocation per fallback hit (#610).
    private static readonly IReadOnlyDictionary<string, double> EmptyInputs
        = new Dictionary<string, double>(0);

    private IReadOnlyDictionary<string, double> ExtractInputs(
        string componentName,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ports)
    {
        // Sprint SI.W14 — read actual input port snapshot from the
        // network's LastResolvedInputs map. This is the deferred fix
        // from SI.W5's ExtractInputs comment: stateful components now
        // get their REAL input port values (resolved from external
        // feeds + upstream connections), not their own outputs.
        return _network.LastResolvedInputs.TryGetValue(componentName, out var p)
            ? p
            : EmptyInputs;
    }

    // Issue #736 Phase 2 — clone _state's flat arrays into the dict-shape
    // public TimeHistorySnapshot expects. The integrator's internal storage
    // is per-component double[]; the snapshot's StateValues field still
    // promises (componentName → (variableName → value)), so we materialise
    // the dicts at snapshot time using each component's cached binding.
    // No longer static (needs access to _bindings).
    private Dictionary<string, IReadOnlyDictionary<string, double>>
        CloneStateMap(Dictionary<string, double[]> source)
    {
        var clone = new Dictionary<string, IReadOnlyDictionary<string, double>>(source.Count);
        foreach (var (name, arr) in source)
        {
            var binding = _bindings[name];
            var dict = new Dictionary<string, double>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                dict[binding.VariableNames[i]] = arr[i];
            clone[name] = dict;
        }
        return clone;
    }

    // ── Sprint B.8c / issue #492 — Cash-Karp RK45 adaptive integrator ─────
    //
    // Cash & Karp (1990) 6-stage embedded Runge-Kutta 4(5) tableau.
    // Stages 1..6 each evaluate f at y + dt · (Σ a_{i,j} · k_j); the
    // 5th-order solution and the embedded 4th-order solution share all
    // 6 stages, so the per-step error |y5 − y4| comes free. Reference:
    // Numerical Recipes 16.2 + Cash & Karp 1990 AIAA-90-1290.

    private const double CK_A21 = 1.0 / 5.0;
    private const double CK_A31 = 3.0 / 40.0;
    private const double CK_A32 = 9.0 / 40.0;
    private const double CK_A41 = 3.0 / 10.0;
    private const double CK_A42 = -9.0 / 10.0;
    private const double CK_A43 = 6.0 / 5.0;
    private const double CK_A51 = -11.0 / 54.0;
    private const double CK_A52 = 5.0 / 2.0;
    private const double CK_A53 = -70.0 / 27.0;
    private const double CK_A54 = 35.0 / 27.0;
    private const double CK_A61 = 1631.0 / 55296.0;
    private const double CK_A62 = 175.0 / 512.0;
    private const double CK_A63 = 575.0 / 13824.0;
    private const double CK_A64 = 44275.0 / 110592.0;
    private const double CK_A65 = 253.0 / 4096.0;

    // 5th-order combination weights (b_2 and b_5 are zero in Cash-Karp).
    private const double CK_B5_1 = 37.0 / 378.0;
    private const double CK_B5_3 = 250.0 / 621.0;
    private const double CK_B5_4 = 125.0 / 594.0;
    private const double CK_B5_6 = 512.0 / 1771.0;

    // 4th-order combination weights (the "embedded" solution).
    private const double CK_B4_1 = 2825.0 / 27648.0;
    private const double CK_B4_3 = 18575.0 / 48384.0;
    private const double CK_B4_4 = 13525.0 / 55296.0;
    private const double CK_B4_5 = 277.0 / 14336.0;
    private const double CK_B4_6 = 1.0 / 4.0;

    // Pre-built per-stage weight arrays (#610). Each stage of the
    // Cash-Karp tableau passes its row of A_ij coefficients to
    // ApplyMultiPerturbationInPlace; before, these arrays were allocated
    // afresh on every call (`new[] { CK_A21 }`, …). Static-readonly
    // hoist makes them shared singletons.
    private static readonly double[] CkWeightsStage2 = { CK_A21 };
    private static readonly double[] CkWeightsStage3 = { CK_A31, CK_A32 };
    private static readonly double[] CkWeightsStage4 = { CK_A41, CK_A42, CK_A43 };
    private static readonly double[] CkWeightsStage5 = { CK_A51, CK_A52, CK_A53, CK_A54 };
    private static readonly double[] CkWeightsStage6 = { CK_A61, CK_A62, CK_A63, CK_A64, CK_A65 };

    /// <summary>
    /// Sprint B.8c default safety factor for the Cash-Karp PI controller.
    /// </summary>
    public const double CashKarpSafetyFactor = 0.9;

    /// <summary>
    /// Sprint B.8c minimum dt growth/shrink factor (rejected-step floor).
    /// </summary>
    public const double CashKarpMinFactor = 0.1;

    /// <summary>
    /// Sprint B.8c maximum dt growth factor (accepted-step ceiling).
    /// </summary>
    public const double CashKarpMaxFactor = 5.0;

    /// <summary>
    /// Sprint B.8c diagnostic — number of rejected steps in the most-
    /// recent <see cref="RunAdaptiveCashKarp45"/> call. -1 before any
    /// adaptive RK45 run has executed.
    /// </summary>
    public int LastCashKarpRejectedSteps { get; private set; } = -1;

    /// <summary>
    /// Sprint B.8c (issue #492) — adaptive-step Cash-Karp RK4(5) run.
    /// Integrates from <paramref name="t0_s"/> to <paramref name="tEnd_s"/>
    /// using the embedded 4th/5th-order tableau; each accepted step
    /// emits a <see cref="TimeHistorySnapshot"/> at the variable-density
    /// tick times. The PI step controller targets the caller-supplied
    /// (atol, rtol) weighted-RMS error norm: <c>err_norm = sqrt(Σ
    /// (|y5−y4| / (atol + rtol · max(|y0|, |y5|)))² / N)</c>. Accept on
    /// <c>err_norm ≤ 1</c>; reject and retry with dt × safety ·
    /// err_norm^(−1/5).
    /// </summary>
    /// <param name="t0_s">Start time (seconds).</param>
    /// <param name="tEnd_s">End time (seconds). Must be &gt; t0_s.</param>
    /// <param name="dtInitial_s">Initial step size guess. Adjusted on
    /// every accepted/rejected step. Clamped to <c>[dtMin_s, dtMax_s]</c>.</param>
    /// <param name="dtMin_s">Step-size floor (seconds). Steps rejected
    /// even at <c>dtMin_s</c> still advance the simulation (the
    /// integrator commits the unrejected 5th-order estimate) to avoid
    /// pathological non-termination on a hard-stiff sub-region.</param>
    /// <param name="dtMax_s">Step-size ceiling (seconds). Must be ≥
    /// <c>dtMin_s</c>.</param>
    /// <param name="atol">Absolute tolerance (per state variable).</param>
    /// <param name="rtol">Relative tolerance (per state variable).</param>
    /// <param name="useIterativeSolve">When true, inner network solves
    /// use <see cref="ComponentNetwork.SolveIterative"/> (cycle-bound
    /// network); when false (default), the acyclic
    /// <see cref="ComponentNetwork.Solve"/>.</param>
    /// <param name="warmStart">When true, preserves existing stateful-
    /// component state (e.g. after RestoreSnapshot); when false (default),
    /// each call reinitialises from GetInitialState().</param>
    /// <returns>Per-accepted-tick port + state snapshot history. Ticks
    /// land at non-uniform times because <c>dt</c> varies.</returns>
    public IReadOnlyList<TimeHistorySnapshot> RunAdaptiveCashKarp45(
        double t0_s, double tEnd_s,
        double dtInitial_s,
        double dtMin_s,
        double dtMax_s,
        double atol = 1.0e-6,
        double rtol = 1.0e-3,
        bool useIterativeSolve = false,
        bool warmStart = false)
    {
        if (dtInitial_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtInitial_s),
                "dtInitial_s must be > 0.");
        if (dtMin_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtMin_s),
                "dtMin_s must be > 0.");
        if (dtMax_s < dtMin_s)
            throw new ArgumentOutOfRangeException(nameof(dtMax_s),
                $"dtMax_s ({dtMax_s:F6}) must be ≥ dtMin_s ({dtMin_s:F6}).");
        if (tEnd_s <= t0_s)
            throw new ArgumentOutOfRangeException(nameof(tEnd_s),
                $"tEnd_s ({tEnd_s:F3}) must be > t0_s ({t0_s:F3}).");
        if (atol <= 0)
            throw new ArgumentOutOfRangeException(nameof(atol),
                "atol must be > 0.");
        if (rtol < 0)
            throw new ArgumentOutOfRangeException(nameof(rtol),
                "rtol must be ≥ 0.");

        // 1. Initialise state (warmStart parity with Run()). Issue #736
        //    Phase 2 — flat-array state init via shared helper.
        InitialiseStateForRun(warmStart);

        // 2. Walk time with adaptive dt.
        ResetEventState();
        LastCashKarpRejectedSteps = 0;
        var history = new List<TimeHistorySnapshot>();
        double t = t0_s;
        double previousTickTime = t0_s;
        double dt = Math.Clamp(dtInitial_s, dtMin_s, dtMax_s);

        while (t < tEnd_s)
        {
            // Don't overshoot the end-time horizon.
            double dtThisStep = Math.Min(dt, tEnd_s - t);
            if (dtThisStep <= 0) break;

            _network.RefreshTimeVaryingInputsAt(t);
            _network.ApplyScheduledFaultsAt(t);

            var ports = useIterativeSolve
                ? _network.SolveIterative()
                : _network.Solve();

            history.Add(new TimeHistorySnapshot(
                Time_s:      t,
                PortValues:  ports,
                StateValues: CloneStateMap(_state)));

            if (CheckEvents(ports, t, previousTickTime))
            {
                return history;
            }
            previousTickTime = t;

            // Trial step. Returns the per-step error norm and the y5
            // estimate; commits y5 to _state when accepted.
            bool accepted = TryCashKarpStep(
                useIterativeSolve, dtThisStep, atol, rtol,
                out double errorNorm);

            if (accepted)
            {
                // Issue #553 / audit C3 — snap the final step to land
                // exactly at tEnd_s. Without this, FP accumulation may
                // stop one step short or take a near-zero final step,
                // neither bit-deterministic across runs. The Math.Min
                // above already shrinks an overshooting dt; this snap
                // removes any residual FP roundoff so t lands bit-
                // exactly on tEnd_s.
                if (t + dtThisStep >= tEnd_s)
                {
                    t = tEnd_s;
                }
                else
                {
                    t += dtThisStep;
                }
                // Grow dt for the next step based on the error norm.
                double factor = errorNorm > 0
                    ? CashKarpSafetyFactor * Math.Pow(errorNorm, -1.0 / 5.0)
                    : CashKarpMaxFactor;
                factor = Math.Clamp(factor, CashKarpMinFactor, CashKarpMaxFactor);
                dt = Math.Clamp(dt * factor, dtMin_s, dtMax_s);
            }
            else
            {
                LastCashKarpRejectedSteps++;
                // Shrink dt and retry. If we're already at dtMin_s,
                // commit the 5th-order estimate anyway (the trial was
                // un-committed on rejection) — non-termination on a
                // hard-stiff sub-region is worse than a single
                // tolerance-exceeding tick at the floor.
                double factor = errorNorm > 0
                    ? CashKarpSafetyFactor * Math.Pow(errorNorm, -1.0 / 5.0)
                    : CashKarpMinFactor;
                factor = Math.Clamp(factor, CashKarpMinFactor, 1.0);
                double dtShrunk = Math.Max(dt * factor, dtMin_s);
                if (dtShrunk >= dt && dt <= dtMin_s)
                {
                    // Already at floor and the shrink can't shrink
                    // further. Commit the unrejected y5 to make
                    // forward progress; flag via the rejected-step
                    // counter.
                    CommitCashKarpFloorStep(useIterativeSolve, dtThisStep);
                    // Issue #553 / audit C3 — endpoint snap (see above).
                    if (t + dtThisStep >= tEnd_s)
                    {
                        t = tEnd_s;
                    }
                    else
                    {
                        t += dtThisStep;
                    }
                    dt = dtMin_s;
                }
                else
                {
                    dt = dtShrunk;
                }
            }
        }

        // Issue #553 / audit C3 — defensive endpoint snap. The clamp +
        // ternary assignment inside the loop should land here exactly
        // under IEEE 754 for any reasonable (t0, tEnd, dt) inputs, but
        // this assignment removes any residual roundoff so the final
        // snapshot's Time_s field carries the exact endpoint value
        // callers asked for.
        t = tEnd_s;

        // Refresh time-varying external inputs + scheduled faults at the exact
        // end time so the final snapshot's port values are consistent with its
        // Time_s = tEnd_s. The in-loop ticks refresh before solving; the final
        // solve must too, otherwise it echoes the previous tick's inputs.
        _network.RefreshTimeVaryingInputsAt(t);
        _network.ApplyScheduledFaultsAt(t);

        // Capture final snapshot at t_end.
        var finalPorts = useIterativeSolve
            ? _network.SolveIterative()
            : _network.Solve();
        history.Add(new TimeHistorySnapshot(
            Time_s:      t,
            PortValues:  finalPorts,
            StateValues: CloneStateMap(_state)));

        return history;
    }

    // Try one Cash-Karp step. On accept: commits y5 to _state, returns
    // true. On reject: rolls _state back to its pre-step value, returns
    // false. errorNorm is set in both cases (caller uses it to size the
    // next dt). State is mutated through the 6 perturbations regardless,
    // so we always restore to originalState on the way out.
    private bool TryCashKarpStep(
        bool useIterativeSolve,
        double dt_s,
        double atol,
        double rtol,
        out double errorNorm)
    {
        EnsureBuffersAllocated();
        SnapshotStateInto(_origBuf!);
        var origState = _origBuf!;

        ComputeAllDerivativesInto(useIterativeSolve, _kBuf1!);

        ApplyMultiPerturbationInPlace(origState, CkWeightsStage2, _ksStage2!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf2!);

        ApplyMultiPerturbationInPlace(origState, CkWeightsStage3, _ksStage3!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf3!);

        ApplyMultiPerturbationInPlace(origState, CkWeightsStage4, _ksStage4!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf4!);

        ApplyMultiPerturbationInPlace(origState, CkWeightsStage5, _ksStage5!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf5!);

        ApplyMultiPerturbationInPlace(origState, CkWeightsStage6, _ksStage6!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf6!);

        // Compute y5 + per-var weighted error |y5 − y4| / (atol + rtol · |y|).
        // Issue #736 Phase 2 — index-based walk through the per-component
        // k-buffers; no inner-dict hash lookups in the inner loop.
        double sumSquaredErr = 0.0;
        int totalVars = 0;
        var y5 = _y5Buf!;

        foreach (var (name, _) in _statefulComponents)
        {
            var y5n  = y5[name];
            var orig = origState[name];
            var k1 = _kBuf1![name];
            var k3 = _kBuf3![name];
            var k4 = _kBuf4![name];
            var k5 = _kBuf5![name];
            var k6 = _kBuf6![name];
            int n = orig.Length;
            for (int i = 0; i < n; i++)
            {
                double y0  = orig[i];
                double k1v = k1[i];
                double k3v = k3[i];
                double k4v = k4[i];
                double k5v = k5[i];
                double k6v = k6[i];

                double y5val = y0 + dt_s * (CK_B5_1 * k1v + CK_B5_3 * k3v
                                          + CK_B5_4 * k4v + CK_B5_6 * k6v);
                double y4val = y0 + dt_s * (CK_B4_1 * k1v + CK_B4_3 * k3v
                                          + CK_B4_4 * k4v + CK_B4_5 * k5v
                                          + CK_B4_6 * k6v);

                double diff  = y5val - y4val;
                double scale = atol + rtol * Math.Max(Math.Abs(y0), Math.Abs(y5val));
                double ratio = diff / scale;
                sumSquaredErr += ratio * ratio;
                totalVars++;

                y5n[i] = y5val;
            }
        }

        errorNorm = totalVars > 0
            ? Math.Sqrt(sumSquaredErr / totalVars)
            : 0.0;

        if (errorNorm <= 1.0)
        {
            // Accept — commit y5 to _state (in-place into owned flat
            // arrays). Issue #738 Phase 3 — SetState is span-based.
            foreach (var (name, stateful) in _statefulComponents)
            {
                var dst = _state[name];
                var src = y5[name];
                Array.Copy(src, dst, src.Length);
                stateful.SetState(dst);
            }
            return true;
        }
        else
        {
            // Reject — restore the pre-step state from origState.
            foreach (var (name, stateful) in _statefulComponents)
            {
                var dst = _state[name];
                var src = origState[name];
                Array.Copy(src, dst, src.Length);
                stateful.SetState(dst);
            }
            return false;
        }
    }

    // dt-floor fallback used when the controller can't shrink further.
    // Re-runs the step and commits the 5th-order estimate unconditionally.
    private void CommitCashKarpFloorStep(bool useIterativeSolve, double dt_s)
    {
        EnsureBuffersAllocated();
        SnapshotStateInto(_origBuf!);
        var origState = _origBuf!;

        ComputeAllDerivativesInto(useIterativeSolve, _kBuf1!);
        ApplyMultiPerturbationInPlace(origState, CkWeightsStage2, _ksStage2!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf2!);
        ApplyMultiPerturbationInPlace(origState, CkWeightsStage3, _ksStage3!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf3!);
        ApplyMultiPerturbationInPlace(origState, CkWeightsStage4, _ksStage4!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf4!);
        ApplyMultiPerturbationInPlace(origState, CkWeightsStage5, _ksStage5!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf5!);
        ApplyMultiPerturbationInPlace(origState, CkWeightsStage6, _ksStage6!, dt_s);
        ComputeAllDerivativesInto(useIterativeSolve, _kBuf6!);

        // Write y5 directly into _state's owned flat arrays.
        // Issue #738 Phase 3 — SetState is span-based.
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dst  = _state[name];
            var orig = origState[name];
            var k1 = _kBuf1![name];
            var k3 = _kBuf3![name];
            var k4 = _kBuf4![name];
            var k6 = _kBuf6![name];
            int n = dst.Length;
            for (int i = 0; i < n; i++)
            {
                dst[i] = orig[i] + dt_s * (
                    CK_B5_1 * k1[i]
                  + CK_B5_3 * k3[i]
                  + CK_B5_4 * k4[i]
                  + CK_B5_6 * k6[i]);
            }
            stateful.SetState(dst);
        }
    }

    // Variable-stage perturbation helper: _state := baseState + step ·
    // Σ_i (weights[i] · ks[i]). Used by the Cash-Karp tableau's stages
    // 2..6, which each evaluate f at a linear combination of all prior
    // stage derivatives. Per #610: writes directly into _state's owned
    // flat arrays; the `weights` and `ks` arrays are also caller-owned
    // and pre-allocated (static readonly + pooled fields respectively).
    // Issue #738 Phase 3 — inner loop walks raw indices; SetState is
    // span-based.
    private void ApplyMultiPerturbationInPlace(
        Dictionary<string, double[]> baseState,
        double[] weights,
        Dictionary<string, double[]>[] ks,
        double step)
    {
        foreach (var (name, stateful) in _statefulComponents)
        {
            var dst = _state[name];
            var bs  = baseState[name];
            int n = dst.Length;
            for (int j = 0; j < n; j++)
            {
                double weightedSum = 0.0;
                for (int i = 0; i < weights.Length; i++)
                {
                    double w = weights[i];
                    if (w == 0.0) continue;
                    weightedSum += w * ks[i][name][j];
                }
                dst[j] = bs[j] + step * weightedSum;
            }
            stateful.SetState(dst);
        }
    }
}
