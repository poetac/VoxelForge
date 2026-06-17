// IStatefulComponent.cs — Sprint SI.W5 optional interface for
// components that carry continuous state across time steps.
//
// Stateful components (e.g. a Hydrogen-storage tank with mass-flow in
// + boil-off out, a Battery pack with cumulative state-of-charge
// discharge, a Reactor with conversion vs time) implement this
// interface in ADDITION to the SystemComponent base. The TimeStepIntegrator
// detects the interface and integrates the declared state derivatives
// across the simulation horizon.
//
// Non-stateful components (most of the existing 21 adapters)
// continue to work unchanged — they're "memoryless" and only respond
// to the current-instant external inputs.
//
// Issue #738 Phase 3 — the state-vector surface is span-based.
// ComputeDerivatives / SetState / GetInitialState / GetCurrentState all
// move state values through `ReadOnlySpan<double>` / `Span<double>`.
// Implementations read state[i] / write derivatives[i] using their
// own knowledge of StateVariables ordering (the same ordering Phase 1
// pinned in StateVectorBinding). The port-value surface (portInputs /
// portOutputs) is still dict-shaped — Phase 4 (#739) flattens that.

using System;
using System.Collections.Generic;

namespace Voxelforge.Integration;

/// <summary>
/// Optional interface implemented by components that carry continuous
/// state across time steps. Sprint SI.W5.
/// </summary>
internal interface IStatefulComponent
{
    /// <summary>
    /// Names of the state variables this component carries. The order
    /// of this list is the canonical index ordering used by every
    /// span-based method below — implementations may read
    /// <c>state[i]</c> / write <c>derivatives[i]</c> using the same
    /// indices they would use to look up
    /// <c>StateVariables[i]</c>.
    /// </summary>
    IReadOnlyList<string> StateVariables { get; }

    /// <summary>
    /// Compute the time-derivative of each state variable at the
    /// current operating point. Called once per integration tick.
    /// </summary>
    /// <param name="state">Current values of all state variables, in
    /// <see cref="StateVariables"/> order.</param>
    /// <param name="portInputs">Current input port values (same as the
    /// dictionary passed to <see cref="SystemComponent.Evaluate"/>).</param>
    /// <param name="portOutputs">Current output port values (computed
    /// by the most-recent Evaluate within this tick).</param>
    /// <param name="derivatives">Span the implementation populates with
    /// dy/dt entries — one per state variable, in
    /// <see cref="StateVariables"/> order. Length is guaranteed
    /// ≥ <c>StateVariables.Count</c>.</param>
    void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives);

    /// <summary>
    /// Write the initial state-vector values at t = 0 into
    /// <paramref name="destination"/>. Called once at the start of an
    /// integration run.
    /// </summary>
    /// <param name="destination">Caller-provided span sized to
    /// <see cref="StateVariables"/>.Count.</param>
    void GetInitialState(Span<double> destination);

    /// <summary>
    /// Apply the integrator-updated state values BACK to the component
    /// so subsequent Evaluates see the new state.
    /// </summary>
    void SetState(ReadOnlySpan<double> state);

    /// <summary>
    /// Sprint SI.W20. Write the CURRENT state-vector values (mid-run,
    /// as opposed to <see cref="GetInitialState"/>'s pinned t=0 values)
    /// into <paramref name="destination"/>. Used by snapshot save/
    /// restore for what-if branching.
    /// </summary>
    /// <param name="destination">Caller-provided span sized to
    /// <see cref="StateVariables"/>.Count.</param>
    void GetCurrentState(Span<double> destination);
}
