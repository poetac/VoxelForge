// SubsystemComponent.cs — Sprint SI.W19 hierarchical encapsulation.
//
// Wraps a ComponentNetwork as a single SystemComponent in a parent
// network. The parent network sees the subsystem as one black-box
// component with externally-named input/output ports; each parent
// port is bound to a (child component, child port) pair inside the
// subnet.
//
// On Evaluate:
//   1. Copy parent inputs into the subnet's external-input map.
//   2. Solve the subnet algebraically.
//   3. Pluck outputs from the subnet's solve result and surface them
//      under the parent-port names.
//
// Stateful subnets are out of scope for Wave-1. The inner network is
// treated as a stateless transfer function — no per-tick state
// evolution inside the subsystem. (Use registered IStatefulComponent
// instances in the parent network if you need state.)
//
// Sprint B.8b (issue #493 defensive): the constructor now throws when
// the inner network contains an IStatefulComponent unless the caller
// explicitly passes allowStatefulInner: true. The algebraic-only
// contract is a real foot-gun — inner stateful components' state never
// evolves through the parent integrator, silently producing wrong
// transient results — and the opt-in flag forces the caller to
// acknowledge the limitation.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Integration.Components;

/// <summary>
/// Algebraic-only hierarchical encapsulation of a
/// <see cref="ComponentNetwork"/> as a single <see cref="SystemComponent"/>
/// (Sprint SI.W19).
/// </summary>
internal sealed class SubsystemComponent : SystemComponent
{
    private readonly ComponentNetwork _subnet;
    private readonly Dictionary<string, (string Component, string Port)> _inputBindings;
    private readonly Dictionary<string, (string Component, string Port)> _outputBindings;
    private readonly bool _useIterativeSolve;

    /// <summary>Create a hierarchical subsystem.</summary>
    /// <param name="name">Parent-network-unique component name.</param>
    /// <param name="subnet">The inner network this subsystem wraps.</param>
    /// <param name="inputBindings">For each external (parent) input
    /// port, a (child component, child port) pair to route the value
    /// into.</param>
    /// <param name="outputBindings">For each external (parent) output
    /// port, a (child component, child port) pair to read the value
    /// from.</param>
    /// <param name="useIterativeSolve">When the inner network contains
    /// a cycle, set true to use <see cref="ComponentNetwork.SolveIterative"/>
    /// internally.</param>
    /// <param name="allowStatefulInner">Sprint B.8b foot-gun guard. When
    /// false (the default), the constructor throws if any inner
    /// component implements <see cref="IStatefulComponent"/>. Wave-1
    /// SubsystemComponent treats the inner network as a stateless
    /// transfer function — inner stateful components' state never
    /// evolves through the parent <see cref="TimeStepIntegrator"/>, so
    /// transient simulations of subnets containing batteries / flywheels
    /// / electrolysers silently produce wrong state trajectories. Pass
    /// <c>true</c> only when the caller is sure the subnet is solved
    /// inside one parent tick (e.g. SI.W1 algebraic-only studies that
    /// never see a TimeStepIntegrator), or when migrating prior code
    /// that relied on the algebraic-only behaviour. Wave-2 stateful-
    /// subsystem support is tracked separately.</param>
    public SubsystemComponent(
        string name,
        ComponentNetwork subnet,
        IEnumerable<(string ParentPort, string SubComponent, string SubPort)> inputBindings,
        IEnumerable<(string ParentPort, string SubComponent, string SubPort)> outputBindings,
        bool useIterativeSolve = false,
        bool allowStatefulInner = false)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(subnet);
        if (!allowStatefulInner && subnet.HasStatefulComponents())
            throw new InvalidOperationException(
                $"SubsystemComponent '{name}' wraps an inner network that "
              + "contains an IStatefulComponent. Wave-1 SubsystemComponent "
              + "treats the inner network as a stateless transfer function "
              + "— inner stateful components' state never evolves through "
              + "the parent TimeStepIntegrator and transient simulations "
              + "silently produce wrong state trajectories. Either pass "
              + "allowStatefulInner: true to acknowledge the algebraic-only "
              + "contract, or register the IStatefulComponent directly in "
              + "the parent network instead.");
        _subnet           = subnet;
        _useIterativeSolve = useIterativeSolve;
        _inputBindings    = inputBindings.ToDictionary(
            t => t.ParentPort, t => (t.SubComponent, t.SubPort));
        _outputBindings   = outputBindings.ToDictionary(
            t => t.ParentPort, t => (t.SubComponent, t.SubPort));
        InputPorts  = _inputBindings.Keys.ToList();
        OutputPorts = _outputBindings.Keys.ToList();
    }

    public override IReadOnlyList<string> InputPorts { get; }
    public override IReadOnlyList<string> OutputPorts { get; }

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        // 1. Push parent inputs onto the subnet's external-input map.
        foreach (var (parentPort, target) in _inputBindings)
            _subnet.SetExternalInput(target.Component, target.Port,
                inputs[parentPort]);

        // 2. Solve the subnet.
        var r = _useIterativeSolve
            ? _subnet.SolveIterative()
            : _subnet.Solve();

        // 3. Hoist subnet outputs to parent port names.
        foreach (var (parentPort, source) in _outputBindings)
            outputs[parentPort] = r[source.Component][source.Port];
    }
}
