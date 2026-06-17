// ComponentNetwork.cs — Sprint SI.W1 sequential-evaluator container
// for component-graph subsystems.
//
// Holds a set of named SystemComponent instances + a set of directed
// ComponentConnection wires + a set of external inputs (values fed in
// from outside the network). On Solve(), the network:
//
//   1. Builds a dependency graph: component A depends on component B
//      if any of A's input ports are wired from B's output ports.
//   2. Performs a topological sort. Cycles raise InvalidOperationException
//      (cycle-iterative solving is deferred to SI.W2+).
//   3. For each component in topological order:
//      a. Gathers its input values from internal connections + external
//         input feeds.
//      b. Calls Evaluate(inputs, outputs).
//      c. Stores the resulting output values for downstream consumers.
//   4. Returns the full port-value map keyed by (component, port).
//
// The Sprint SI.W1 contract is purely causal + steady-state: the
// directed graph must be acyclic. Most multi-pillar studies fit this
// (PV → BP → EM; PG → grid; ST → STR → RAD); cycle-iterative
// (transient + closed-loop control) lands in SI.W2.
//
// Issue #491 (Tier 1 perf). Per-component dictionary pooling: the
// per-tick allocations from `new Dictionary<string, double>()` inside
// Solve() / SolveIterative() / GatherInputs() are eliminated by
// reusing per-component dicts owned by the network. The input pool
// also doubles as the canonical store behind LastResolvedInputs (the
// audit's secondary Tier-1 ask). A per-destination connection cache
// removes the per-tick LINQ `Where(c => c.ToComponent == name)` scan.
//
// Pooling contract — important. The dictionaries fed into Evaluate()
// (the `inputs` parameter) and the dicts surfaced via LastResolvedInputs
// ARE reused across Solve() / SolveIterative() calls. Callers that need
// a stable snapshot of inputs must clone. The DICTS RETURNED FROM
// Solve() / SolveIterative() (the per-component output dicts) remain
// freshly allocated per call so that TimeStepIntegrator's snapshot
// capture (which holds long-lived references to the returned map) stays
// correct without per-snapshot copy.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Integration;

/// <summary>
/// Causal steady-state sequential-evaluator network of
/// <see cref="SystemComponent"/> instances (Sprint SI.W1).
/// </summary>
internal sealed class ComponentNetwork
{
    private readonly Dictionary<string, SystemComponent> _components = new();
    private readonly List<ComponentConnection> _connections = new();
    // External inputs: (componentName → (portName → value)).
    private readonly Dictionary<string, Dictionary<string, double>> _externalInputs = new();
    // Sprint SI.W11. Time-varying external inputs: callback time_s → value.
    private readonly Dictionary<string, Dictionary<string, Func<double, double>>>
        _timeVaryingExternalInputs = new();
    // Issue #491 (Tier 1). Per-component input-port dict pool keyed by
    // component name. Cleared + repopulated by Solve()/SolveIterative()
    // each call; the dict instance for each component is reused across
    // calls so the 1 kHz transient hot path no longer pays the per-tick
    // Dictionary allocation cost. ALSO serves as the canonical backing
    // store behind LastResolvedInputs (the read-only view below).
    private readonly Dictionary<string, Dictionary<string, double>> _pooledInputs = new();
    // Sprint SI.W14 / issue #491. Read-only view over _pooledInputs.
    // Built once + reused across calls — Solve()/SolveIterative() rewrite
    // the underlying dicts in-place, the view sees the latest values
    // automatically. Saves the per-tick `new Dictionary<...>` + the LINQ
    // ToDictionary rebuild that the pre-pool implementation paid.
    private readonly Dictionary<string, IReadOnlyDictionary<string, double>>
        _lastResolvedInputsView = new();
    // Sprint SI.W17. Components marked as faulted have all of their
    // output ports forced to 0.0 on Solve()/SolveIterative(). Used for
    // reliability + safety analysis ("what happens if PV fails at
    // t=4 hr?").
    private readonly HashSet<string> _faultedComponents = new();
    // Sprint SI.W17. Scheduled fault injection: (time_s, name,
    // faulted) tuples applied at the start of each integration tick
    // whose time has crossed the schedule timestamp.
    private readonly List<(double Time_s, string Name, bool Faulted)>
        _faultSchedule = new();
    // Issue #557 item 4. Pre-sorted copy of _faultSchedule for the
    // per-tick ApplyScheduledFaultsAt hot path. The pre-existing
    // implementation called `.OrderBy(...)` per tick — for a 1000-tick
    // simulation with a 5-entry schedule that's 5000 redundant sort
    // iterations. Rebuilt lazily when _faultScheduleSortedDirty is
    // set (only after ScheduleFault() mutates the underlying list).
    private List<(double Time_s, string Name, bool Faulted)>?
        _faultScheduleSorted;
    private bool _faultScheduleSortedDirty = true;
    // Issue #491 (Tier 1). Per-destination connection cache. Maps
    // `c.ToComponent` to the (ordered, registration-order-preserved)
    // list of connections terminating at that component. Built lazily
    // from _connections on first Solve() / SolveIterative() call; the
    // _connectionsByDestDirty flag forces a rebuild the next time a
    // Connect() / Add() call alters the underlying topology. Eliminates
    // the per-tick `_connections.Where(c => c.ToComponent == name)` LINQ
    // scan that was O(connections × components) per Solve.
    private readonly Dictionary<string, List<ComponentConnection>>
        _connectionsByDest = new();
    private bool _connectionsByDestDirty = true;
    // Issue #491 (Tier 1). Topological-sort cache. The ordered name
    // sequence + the dependency map used for cycle detection are
    // invariant to anything except topology mutations (Add() /
    // Connect()); cache them across Solve() calls so the per-tick hot
    // path no longer rebuilds the dependency dict, the HashSet-per-
    // component, the Kahn's-algorithm scratch state, etc. Reset by
    // _connectionsByDestDirty (single flag covers both caches since
    // both are invalidated by the same set of mutations).
    private List<string>? _cachedTopologicalOrder;
    // Issue #491 (Tier 1). Cached component-name → SystemComponent list
    // walk for SolveIterative (which doesn't need topological order but
    // does need a stable per-call enumeration). Rebuilt under the same
    // dirty flag as the topo cache.
    private List<string>? _cachedRegistrationOrder;
    // Issue #491 (Tier 1). Shared empty input dict surfaced via
    // LastResolvedInputs for faulted components (whose Evaluate is
    // skipped). Saves the per-tick `new Dictionary<string, double>()`
    // that the pre-pool implementation paid for each faulted entry.
    private static readonly IReadOnlyDictionary<string, double>
        _emptyInputs = new Dictionary<string, double>(capacity: 0);

    /// <summary>Number of components in the network.</summary>
    public int ComponentCount => _components.Count;

    /// <summary>
    /// Sprint SI.W14. Per-component snapshot of the input port values
    /// that were resolved on the most-recent Solve()/SolveIterative()
    /// call. Empty before the first solve.
    /// </summary>
    /// <remarks>
    /// Issue #491. The returned map AND its per-component sub-dicts are
    /// reused across Solve()/SolveIterative() calls — callers that need
    /// a stable snapshot of one tick's inputs must clone the values they
    /// care about. The TimeStepIntegrator's existing call sites consume
    /// LastResolvedInputs only inside ComputeDerivatives (immediately
    /// after the relevant Solve), so the reuse is invisible there.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>
        LastResolvedInputs => _lastResolvedInputsView;

    /// <summary>Number of internal port-to-port connections in the network.</summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// Sprint SI.W7. Names of all registered components in
    /// registration order.
    /// </summary>
    public IReadOnlyList<string> ComponentNames => _components.Keys.ToList();

    /// <summary>
    /// Sprint SI.W7. All registered connections in the order they were
    /// added.
    /// </summary>
    public IReadOnlyList<ComponentConnection> Connections => _connections.AsReadOnly();

    /// <summary>
    /// Sprint SI.W18. Look up a registered component's input ports.
    /// </summary>
    public IReadOnlyList<string> InputPortsOf(string componentName)
        => _components[componentName].InputPorts;

    /// <summary>
    /// Sprint B.8b / issue #493. Reports whether any registered
    /// component implements <see cref="IStatefulComponent"/>. Used by
    /// <see cref="Components.SubsystemComponent"/> to surface the
    /// algebraic-only Wave-1 contract foot-gun at construction time
    /// instead of letting state silently fail to evolve through the
    /// parent integrator.
    /// </summary>
    public bool HasStatefulComponents()
    {
        foreach (var c in _components.Values)
            if (c is IStatefulComponent) return true;
        return false;
    }

    /// <summary>
    /// Sprint SI.W18. Look up a registered component's output ports.
    /// </summary>
    public IReadOnlyList<string> OutputPortsOf(string componentName)
        => _components[componentName].OutputPorts;

    /// <summary>
    /// Sprint SI.W18. Set of <c>(component, port)</c> pairs that have
    /// a static external input value attached. Does not include
    /// time-varying callbacks.
    /// </summary>
    public IReadOnlyCollection<(string Component, string Port)> ExternalInputBindings
        => _externalInputs.SelectMany(
            kv => kv.Value.Keys.Select(p => (kv.Key, p))).ToList();

    /// <summary>
    /// Sprint SI.W7. Render the network topology as a multi-line string
    /// for debugging / log output.
    /// </summary>
    public string DescribeTopology()
    {
        var lines = new List<string>();
        lines.Add($"ComponentNetwork: {ComponentCount} component(s), {ConnectionCount} connection(s)");
        lines.Add("Components:");
        foreach (var (name, component) in _components)
        {
            lines.Add($"  - {name}");
            lines.Add($"      inputs:  [{string.Join(", ", component.InputPorts)}]");
            lines.Add($"      outputs: [{string.Join(", ", component.OutputPorts)}]");
        }
        if (_connections.Count > 0)
        {
            lines.Add("Connections:");
            foreach (var c in _connections)
                lines.Add($"  - {c.FromComponent}.{c.FromPort} -> {c.ToComponent}.{c.ToPort}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Sprint SI.W7. Compute the topological-sort order that Solve()
    /// will walk. Raises <see cref="InvalidOperationException"/> on
    /// cycles.
    /// </summary>
    public IReadOnlyList<string> GetTopologicalOrder()
    {
        ValidateConnectionsAndExternalInputs();
        return TopologicalSort(BuildDependencyMap());
    }

    /// <summary>
    /// Sprint SI.W8. Render the network as a GraphViz DOT-language
    /// digraph. Pipe through `dot -Tpng` (or any GraphViz frontend)
    /// to visualise the subsystem topology.
    /// </summary>
    /// <param name="componentFilter">
    /// Optional component-name predicate (Sprint SI.W28). When non-null,
    /// the DOT output is scoped to just the matching components + their
    /// inter-component connections + their external feeds. Useful for
    /// rendering one subsystem's topology in isolation. Connections
    /// that cross the filter boundary (one endpoint in, one out) are
    /// rendered as dotted edges with the out-of-scope endpoint shown
    /// as a faded box — preserves the "border" of the filtered
    /// subgraph for visual clarity.
    /// </param>
    public string ExportToDot(Func<string, bool>? componentFilter = null)
    {
        bool Included(string name) => componentFilter is null || componentFilter(name);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("digraph ComponentNetwork {");
        sb.AppendLine("    rankdir=LR;");
        sb.AppendLine("    node [shape=box, fontname=\"Helvetica\"];");
        sb.AppendLine("    edge [fontname=\"Helvetica\", fontsize=10];");

        // Component nodes.
        foreach (var (name, component) in _components)
        {
            if (!Included(name)) continue;
            int ip = component.InputPorts.Count;
            int op = component.OutputPorts.Count;
            sb.AppendLine($"    \"{name}\" [label=\"{name}\\n[{ip} in, {op} out]\"];");
        }

        // Border nodes — components OUTSIDE the filter that are touched
        // by an included connection (one endpoint in, one out). Render
        // faded so the subsystem "border" is visible.
        if (componentFilter is not null)
        {
            var borderNodes = new HashSet<string>();
            foreach (var c in _connections)
            {
                bool fromIn = Included(c.FromComponent);
                bool toIn   = Included(c.ToComponent);
                if (fromIn ^ toIn)
                {
                    borderNodes.Add(fromIn ? c.ToComponent : c.FromComponent);
                }
            }
            foreach (var name in borderNodes)
            {
                sb.AppendLine($"    \"{name}\" [label=\"{name}\\n(out of scope)\", "
                            + "style=dashed, color=\"#999999\", fontcolor=\"#999999\"];");
            }
        }

        // External-input feeds (dashed-border ellipses) — restricted to
        // included components.
        foreach (var (componentName, ports) in _externalInputs)
        {
            if (!Included(componentName)) continue;
            foreach (var portName in ports.Keys)
            {
                string extId = $"ext_{componentName}_{portName}";
                sb.AppendLine(
                    $"    \"{extId}\" [shape=ellipse, style=dashed, label=\"ext: {portName}\"];");
                sb.AppendLine(
                    $"    \"{extId}\" -> \"{componentName}\" [label=\"{portName}\", style=dashed];");
            }
        }

        // Internal connection edges. Cross-boundary edges (one endpoint
        // in / one out) get dotted style for visual distinction.
        foreach (var c in _connections)
        {
            bool fromIn = Included(c.FromComponent);
            bool toIn   = Included(c.ToComponent);
            if (!fromIn && !toIn) continue;   // both out → skip
            string style = (fromIn && toIn)
                ? string.Empty
                : ", style=dotted, color=\"#999999\"";
            sb.AppendLine(
                $"    \"{c.FromComponent}\" -> \"{c.ToComponent}\" "
              + $"[label=\"{c.FromPort} → {c.ToPort}\"{style}];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Add a component to the network. Component names must be unique.
    /// </summary>
    public void Add(SystemComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (_components.ContainsKey(component.Name))
            throw new InvalidOperationException(
                $"Component name '{component.Name}' is already registered.");
        _components[component.Name] = component;
        // Issue #491. Reset the per-destination connection cache; the
        // new component may be the target of subsequent Connect() calls
        // (and even before that, an empty entry needs to exist so the
        // pooled gather path doesn't NRE on lookup).
        _connectionsByDestDirty = true;
    }

    /// <summary>
    /// Connect an output port of one component to an input port of
    /// another. The two ports must exist on their respective components
    /// (validated at <see cref="Solve"/> time).
    /// </summary>
    public void Connect(string fromComponent, string fromPort,
                        string toComponent, string toPort)
    {
        _connections.Add(new ComponentConnection(
            fromComponent, fromPort, toComponent, toPort));
        // Issue #491. Invalidate the per-destination cache; the new
        // wire alters at least one component's incoming-connection list.
        _connectionsByDestDirty = true;
    }

    /// <summary>
    /// Set an external input value that will be fed to a specific
    /// component's input port on Solve. External inputs override
    /// internal connections (use this for system-level boundary
    /// conditions like setpoints / commands).
    /// </summary>
    public void SetExternalInput(string componentName, string portName, double value)
    {
        if (!_externalInputs.TryGetValue(componentName, out var portMap))
        {
            portMap = new Dictionary<string, double>();
            _externalInputs[componentName] = portMap;
        }
        portMap[portName] = value;
    }

    /// <summary>
    /// Sprint SI.W11. Set a time-varying external input. The callback
    /// is invoked at the start of each integration tick to refresh the
    /// value. Use this for time-varying boundary conditions like
    /// diurnal solar irradiance, throttle profiles, fault injections.
    /// </summary>
    public void SetTimeVaryingExternalInput(
        string componentName, string portName, Func<double, double> valueAtTime)
    {
        ArgumentNullException.ThrowIfNull(valueAtTime);
        if (!_timeVaryingExternalInputs.TryGetValue(componentName, out var portMap))
        {
            portMap = new Dictionary<string, Func<double, double>>();
            _timeVaryingExternalInputs[componentName] = portMap;
        }
        portMap[portName] = valueAtTime;
    }

    /// <summary>
    /// Sprint SI.W11. Refresh time-varying external inputs at the
    /// given simulation time. Called by the TimeStepIntegrator at the
    /// start of each tick. Updates the constant-external-input
    /// dictionary so Solve()/SolveIterative() see the new values.
    /// </summary>
    internal void RefreshTimeVaryingInputsAt(double time_s)
    {
        foreach (var (componentName, ports) in _timeVaryingExternalInputs)
            foreach (var (portName, callback) in ports)
                SetExternalInput(componentName, portName, callback(time_s));
    }

    /// <summary>
    /// Sprint SI.W17. Mark a component as faulted. All of its output
    /// ports are forced to 0.0 on subsequent solves. Downstream
    /// consumers see zeros on the wires that originate from this
    /// component.
    /// </summary>
    public void SetComponentFaulted(string componentName, bool faulted)
    {
        if (!_components.ContainsKey(componentName))
            throw new InvalidOperationException(
                $"Unknown component '{componentName}'.");
        if (faulted) _faultedComponents.Add(componentName);
        else         _faultedComponents.Remove(componentName);
    }

    /// <summary>
    /// Sprint SI.W17. Is the given component currently marked faulted?
    /// </summary>
    public bool IsComponentFaulted(string componentName)
        => _faultedComponents.Contains(componentName);

    /// <summary>
    /// Sprint SI.W17. Schedule a fault state-change at the given time.
    /// The integrator applies the schedule at the start of each tick;
    /// at the first tick whose Time_s ≥ schedule.Time_s the component
    /// transitions to the scheduled Faulted state.
    /// </summary>
    public void ScheduleFault(double time_s, string componentName, bool faulted)
    {
        if (!_components.ContainsKey(componentName))
            throw new InvalidOperationException(
                $"Unknown component '{componentName}'.");
        _faultSchedule.Add((time_s, componentName, faulted));
        _faultScheduleSortedDirty = true;
    }

    /// <summary>
    /// Sprint SI.W17. Apply all scheduled fault transitions whose
    /// timestamp is ≤ <paramref name="time_s"/>. Called by the
    /// TimeStepIntegrator at the start of each tick.
    /// </summary>
    internal void ApplyScheduledFaultsAt(double time_s)
    {
        // Sort by timestamp so out-of-order ScheduleFault() calls still
        // apply transitions in chronological order. Otherwise a user
        // who schedules an OFF-at-t=300 BEFORE an ON-at-t=120 would
        // see the wrong final state once both have elapsed.
        // Idempotent: SetComponentFaulted with the same flag is a no-op,
        // so re-applying a long-elapsed schedule entry each tick is
        // harmless.
        // Issue #557 item 4 — cache the sorted view so the OrderBy fires
        // only after ScheduleFault() mutates the underlying list, not on
        // every tick.
        if (_faultScheduleSortedDirty || _faultScheduleSorted is null)
        {
            _faultScheduleSorted = new List<(double, string, bool)>(_faultSchedule.Count);
            _faultScheduleSorted.AddRange(_faultSchedule);
            _faultScheduleSorted.Sort(static (a, b) => a.Time_s.CompareTo(b.Time_s));
            _faultScheduleSortedDirty = false;
        }
        foreach (var (t, name, faulted) in _faultScheduleSorted)
            if (t <= time_s)
                SetComponentFaulted(name, faulted);
    }

    /// <summary>
    /// Solve the network: topologically sort components, evaluate each
    /// in order, propagate output values through connections, return
    /// the full (component, port) → value map.
    /// </summary>
    /// <returns>
    /// (componentName → (portName → value)) map of all output port
    /// values across all components. Each per-component sub-dict is a
    /// freshly-allocated <see cref="Dictionary{TKey, TValue}"/> safe to
    /// hold long-term (e.g. for <c>TimeHistorySnapshot</c> capture).
    /// </returns>
    /// <exception cref="InvalidOperationException">When the connection
    /// graph contains a cycle, or when a connection references a
    /// component / port that doesn't exist, or when a component's input
    /// has neither an external feed nor an internal connection.</exception>
    /// <remarks>
    /// Issue #491 (Tier 1 perf). The dicts surfaced via
    /// <see cref="LastResolvedInputs"/> after this call ARE reused across
    /// subsequent Solve() / SolveIterative() invocations (input pool);
    /// the per-component OUTPUT dicts in the returned map remain
    /// freshly-allocated per call.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Solve()
    {
        ValidateConnectionsAndExternalInputs();

        // Issue #491. Lazily rebuild the per-destination connection +
        // topological-order caches after any topology change. The
        // per-tick hot path then just reads the cached structures.
        EnsureTopologyCachesBuilt();

        // 1+2. Topological sort (cached). Raises on cycle on first
        //      Solve after a topology change; on subsequent calls just
        //      returns the cached order.
        var orderedNames = GetOrBuildTopologicalOrder();

        // 3. Evaluate each component in order — Sprint SI.W1 behaviour.
        //    Issue #491: the per-component OUTPUT dicts in `results` are
        //    fresh allocations sized to OutputPorts.Count; the
        //    `mutableOutputs` view used by GatherInputsInto reads the
        //    same dict instances. The per-component INPUT dicts are
        //    drawn from the pool (_pooledInputs) and reused across
        //    Solve() calls.
        var results = new Dictionary<string, IReadOnlyDictionary<string, double>>(
            _components.Count);
        var mutableOutputs = new Dictionary<string, Dictionary<string, double>>(
            _components.Count);
        foreach (var name in orderedNames)
        {
            var component = _components[name];
            var outputs = new Dictionary<string, double>(component.OutputPorts.Count);
            if (_faultedComponents.Contains(name))
            {
                // Sprint SI.W17 — faulted: zero outputs, skip Evaluate.
                foreach (var portName in component.OutputPorts)
                    outputs[portName] = 0.0;
                _lastResolvedInputsView[name] = _emptyInputs;
            }
            else
            {
                var inputs = GetOrCreatePooledInputs(component);
                GatherInputsInto(component, mutableOutputs, inputs);
                component.Evaluate(inputs, outputs);
                _lastResolvedInputsView[name] = inputs;
            }
            mutableOutputs[name] = outputs;
            results[name] = outputs;
        }
        return results;
    }

    /// <summary>
    /// Sprint SI.W3. Solve a network that may contain cycles via
    /// Gauss-Seidel iteration. Unlike <see cref="Solve"/>, this method
    /// does not require an acyclic graph — closed-loop control and
    /// thermo-coupled subsystems work directly.
    ///
    /// Algorithm: each iteration walks components in registration
    /// order; gathers inputs from external feeds + the previous
    /// iteration's outputs for any port that isn't yet computed in
    /// the current iteration; checks max-abs-delta in port values;
    /// halts at <paramref name="tolerance"/> or
    /// <paramref name="maxIterations"/>.
    /// </summary>
    /// <param name="maxIterations">Hard ceiling on iteration count.
    /// Raises <see cref="InvalidOperationException"/> if convergence
    /// isn't reached.</param>
    /// <param name="tolerance">Max-abs-delta in any port value across
    /// one iteration for the solver to consider the system converged.
    /// Default 1e-6.</param>
    /// <param name="missingInputDefault">Initial-iteration value for
    /// any input port that doesn't have an external feed AND isn't
    /// computed by an upstream-already-evaluated component within the
    /// same iteration. Default 0.0.</param>
    /// <returns>(component → port → value) map of converged outputs.
    /// Each per-component sub-dict is a freshly-allocated
    /// <see cref="Dictionary{TKey, TValue}"/> safe to hold long-term.</returns>
    /// <exception cref="InvalidOperationException">When the iteration
    /// limit is reached without converging.</exception>
    /// <remarks>
    /// Issue #491 (Tier 1 perf). The dicts surfaced via
    /// <see cref="LastResolvedInputs"/> after this call ARE reused across
    /// subsequent Solve() / SolveIterative() invocations (input pool);
    /// the per-component OUTPUT dicts in the returned map remain
    /// freshly-allocated per call.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>
        SolveIterative(
            int maxIterations = 100,
            double tolerance  = 1e-6,
            double missingInputDefault = 0.0)
    {
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations),
                "maxIterations must be > 0.");
        if (tolerance <= 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance),
                "tolerance must be > 0.");

        ValidateConnectionsAndExternalInputs();
        EnsureTopologyCachesBuilt();

        // Seed each output port at the default — needed for cycle-bound
        // ports whose first-iteration value is read by another component
        // before this component has computed it.
        //
        // Issue #491. We still allocate per-component output dicts here
        // (and on each iteration below) because the returned map is held
        // long-term by TimeStepIntegrator's snapshot capture. The hot
        // input-side allocations are eliminated by _pooledInputs.
        var outputs = new Dictionary<string, Dictionary<string, double>>(
            _components.Count);
        foreach (var (name, component) in _components)
        {
            var ports = new Dictionary<string, double>(component.OutputPorts.Count);
            foreach (var portName in component.OutputPorts)
                ports[portName] = missingInputDefault;
            outputs[name] = ports;
        }
        var previousOutputs = CloneOutputs(outputs);

        // Issue #491. Cached registration order — same instance reused
        // across solves until topology mutates. The list is read-only
        // inside the iteration loop so sharing the instance is safe.
        var orderedNames = _cachedRegistrationOrder!;

        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            foreach (var name in orderedNames)
            {
                var component = _components[name];
                var newOutputs = new Dictionary<string, double>(
                    component.OutputPorts.Count);
                if (_faultedComponents.Contains(name))
                {
                    // Sprint SI.W17 — faulted: zero outputs, skip Evaluate.
                    foreach (var portName in component.OutputPorts)
                        newOutputs[portName] = 0.0;
                    _lastResolvedInputsView[name] = _emptyInputs;
                }
                else
                {
                    var inputs = GetOrCreatePooledInputs(component);
                    GatherInputsIterativeInto(
                        component, outputs, missingInputDefault, inputs);
                    component.Evaluate(inputs, newOutputs);
                    _lastResolvedInputsView[name] = inputs;
                }
                outputs[name] = newOutputs;
            }

            double maxDelta = ComputeMaxAbsoluteDelta(outputs, previousOutputs);
            if (maxDelta < tolerance)
            {
                return BuildReadOnlyResultMap(outputs);
            }

            previousOutputs = CloneOutputs(outputs);
        }

        throw new InvalidOperationException(
            $"Gauss-Seidel iteration failed to converge to tolerance {tolerance:E} "
          + $"after {maxIterations} iterations.");
    }

    // ── Internals ──────────────────────────────────────────────────────

    private void ValidateConnectionsAndExternalInputs()
    {
        foreach (var c in _connections)
        {
            if (!_components.TryGetValue(c.FromComponent, out var fromComp))
                throw new InvalidOperationException(
                    $"Connection refers to unknown component '{c.FromComponent}'.");
            if (!_components.TryGetValue(c.ToComponent, out var toComp))
                throw new InvalidOperationException(
                    $"Connection refers to unknown component '{c.ToComponent}'.");
            if (!fromComp.OutputPorts.Contains(c.FromPort))
                throw new InvalidOperationException(
                    $"Component '{c.FromComponent}' has no output port '{c.FromPort}'.");
            if (!toComp.InputPorts.Contains(c.ToPort))
                throw new InvalidOperationException(
                    $"Component '{c.ToComponent}' has no input port '{c.ToPort}'.");
        }
        foreach (var (componentName, ports) in _externalInputs)
        {
            if (!_components.TryGetValue(componentName, out var component))
                throw new InvalidOperationException(
                    $"External input refers to unknown component '{componentName}'.");
            foreach (var portName in ports.Keys)
                if (!component.InputPorts.Contains(portName))
                    throw new InvalidOperationException(
                        $"Component '{componentName}' has no input port '{portName}'.");
        }
    }

    private Dictionary<string, HashSet<string>> BuildDependencyMap()
    {
        var deps = new Dictionary<string, HashSet<string>>();
        foreach (var name in _components.Keys)
            deps[name] = new HashSet<string>();
        foreach (var c in _connections)
            deps[c.ToComponent].Add(c.FromComponent);
        return deps;
    }

    private static List<string> TopologicalSort(Dictionary<string, HashSet<string>> deps)
    {
        var sorted = new List<string>();
        var ready = new Queue<string>(deps.Where(kv => kv.Value.Count == 0)
                                          .Select(kv => kv.Key));
        var remainingDeps = deps.ToDictionary(
            kv => kv.Key, kv => new HashSet<string>(kv.Value));
        while (ready.Count > 0)
        {
            var n = ready.Dequeue();
            sorted.Add(n);
            foreach (var kv in remainingDeps)
            {
                if (kv.Value.Remove(n) && kv.Value.Count == 0)
                    ready.Enqueue(kv.Key);
            }
        }
        if (sorted.Count != deps.Count)
            throw new CyclicComponentNetworkException(
                "Component connection graph contains a cycle — Solve() only "
              + "supports acyclic networks. Use SolveIterative() for "
              + "Gauss-Seidel cycle iteration (Sprint SI.W3+).");
        return sorted;
    }

    // Issue #491. Lazy fetch / create the pooled input dict for a
    // component. The dict instance is reused across Solve() calls;
    // GatherInputsInto / GatherInputsIterativeInto clear + repopulate
    // its contents.
    private Dictionary<string, double> GetOrCreatePooledInputs(SystemComponent component)
    {
        if (!_pooledInputs.TryGetValue(component.Name, out var pooled))
        {
            pooled = new Dictionary<string, double>(component.InputPorts.Count);
            _pooledInputs[component.Name] = pooled;
        }
        return pooled;
    }

    // Issue #491. Build per-destination connection cache lazily. The
    // cache is invalidated by Add() / Connect() via the dirty flag. The
    // single dirty flag also invalidates the topological-order cache +
    // the registration-order cache below, since all three are
    // invariants of the same topology graph.
    private void EnsureTopologyCachesBuilt()
    {
        if (!_connectionsByDestDirty) return;
        // Per-destination connection cache.
        _connectionsByDest.Clear();
        foreach (var componentName in _components.Keys)
            _connectionsByDest[componentName] = new List<ComponentConnection>();
        foreach (var c in _connections)
        {
            // Connections referencing unknown components are caught by
            // ValidateConnectionsAndExternalInputs (which runs ahead of
            // this in both Solve / SolveIterative). Defensive check keeps
            // the pre-validation surface (e.g. ad-hoc unit tests calling
            // EnsureTopologyCachesBuilt indirectly) NRE-safe.
            if (!_connectionsByDest.TryGetValue(c.ToComponent, out var list))
            {
                list = new List<ComponentConnection>();
                _connectionsByDest[c.ToComponent] = list;
            }
            list.Add(c);
        }
        // Registration-order cache (used by SolveIterative + the seed
        // pass in same).
        _cachedRegistrationOrder = new List<string>(_components.Count);
        foreach (var name in _components.Keys)
            _cachedRegistrationOrder.Add(name);
        // Topological-order cache (used by Solve). Invalidated here so a
        // subsequent Solve sees the dirty cache + rebuilds. We do NOT
        // call TopologicalSort here because cycle errors should surface
        // at Solve time, not Add/Connect time (preserves the prior
        // contract — adding a cycle then never calling Solve was a
        // no-op before).
        _cachedTopologicalOrder = null;
        _connectionsByDestDirty = false;
    }

    // Issue #491. Lazy topological-order cache. Built on first Solve()
    // call after any topology change; reused across subsequent Solves.
    // Raises CyclicComponentNetworkException on cycle.
    private List<string> GetOrBuildTopologicalOrder()
    {
        if (_cachedTopologicalOrder is not null) return _cachedTopologicalOrder;
        var order = TopologicalSort(BuildDependencyMap());
        _cachedTopologicalOrder = order;
        return order;
    }

    // Issue #491. Refactor of GatherInputs to fill a caller-supplied
    // dict (drawn from _pooledInputs) instead of allocating a fresh
    // Dictionary per call. The destination dict is cleared first so
    // stale entries from a previous Solve() can't leak through; the
    // per-destination connection cache replaces the per-tick LINQ
    // Where() scan.
    private void GatherInputsInto(
        SystemComponent component,
        Dictionary<string, Dictionary<string, double>> outputs,
        Dictionary<string, double> destination)
    {
        destination.Clear();
        // External inputs first (they take precedence over connections).
        if (_externalInputs.TryGetValue(component.Name, out var external))
            foreach (var (port, value) in external)
                destination[port] = value;
        // Then internal connections. The per-destination cache replaces
        // the per-tick `_connections.Where(c => c.ToComponent == name)`
        // LINQ scan that the pre-pool implementation paid.
        if (_connectionsByDest.TryGetValue(component.Name, out var incoming))
        {
            foreach (var c in incoming)
            {
                if (destination.ContainsKey(c.ToPort)) continue;   // external override wins
                if (!outputs.TryGetValue(c.FromComponent, out var fromOutputs)
                 || !fromOutputs.TryGetValue(c.FromPort, out var v))
                    throw new InvalidOperationException(
                        $"Internal: missing upstream value for {c.FromComponent}.{c.FromPort} "
                      + $"needed by {c.ToComponent}.{c.ToPort}.");
                destination[c.ToPort] = v;
            }
        }
        // Verify every declared input port has a value.
        foreach (var portName in component.InputPorts)
            if (!destination.ContainsKey(portName))
                throw new InvalidOperationException(
                    $"Component '{component.Name}' input port '{portName}' has neither "
                  + "an external feed nor an internal connection.");
    }

    // ── Sprint SI.W3 — iterative-solve helpers ─────────────────────────

    // Issue #491. Refactor of GatherInputsIterative to fill a caller-
    // supplied dict (drawn from _pooledInputs).
    private void GatherInputsIterativeInto(
        SystemComponent component,
        Dictionary<string, Dictionary<string, double>> outputs,
        double missingDefault,
        Dictionary<string, double> destination)
    {
        destination.Clear();
        if (_externalInputs.TryGetValue(component.Name, out var external))
            foreach (var (port, value) in external)
                destination[port] = value;
        if (_connectionsByDest.TryGetValue(component.Name, out var incoming))
        {
            foreach (var c in incoming)
            {
                if (destination.ContainsKey(c.ToPort)) continue;
                // Iterative variant: ALWAYS read from outputs (this iteration
                // or the seed from the previous iteration). Cycles work
                // because we always have a previous-iteration value.
                destination[c.ToPort] = outputs[c.FromComponent].TryGetValue(c.FromPort, out var v)
                    ? v
                    : missingDefault;
            }
        }
        // Each declared input port must end up with a value.
        foreach (var portName in component.InputPorts)
            if (!destination.ContainsKey(portName))
                throw new InvalidOperationException(
                    $"Component '{component.Name}' input port '{portName}' has neither "
                  + "an external feed nor an internal connection.");
    }

    private static Dictionary<string, Dictionary<string, double>> CloneOutputs(
        Dictionary<string, Dictionary<string, double>> source)
    {
        var clone = new Dictionary<string, Dictionary<string, double>>(source.Count);
        foreach (var (k, v) in source)
            clone[k] = new Dictionary<string, double>(v);
        return clone;
    }

    private static double ComputeMaxAbsoluteDelta(
        Dictionary<string, Dictionary<string, double>> a,
        Dictionary<string, Dictionary<string, double>> b)
    {
        double max = 0.0;
        foreach (var (componentName, portsA) in a)
        {
            if (!b.TryGetValue(componentName, out var portsB)) continue;
            foreach (var (portName, valA) in portsA)
            {
                if (!portsB.TryGetValue(portName, out var valB)) continue;
                double delta = Math.Abs(valA - valB);
                if (delta > max) max = delta;
            }
        }
        return max;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, double>>
        BuildReadOnlyResultMap(Dictionary<string, Dictionary<string, double>> source)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, double>>(source.Count);
        foreach (var (k, v) in source)
            result[k] = v;
        return result;
    }
}
