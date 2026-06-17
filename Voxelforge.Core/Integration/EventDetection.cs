// EventDetection.cs — Sprint SI.W23 event-detection types for
// TimeStepIntegrator.
//
// Events are state-or-port-driven thresholds the simulation should
// either record (passing events) or stop on (terminal events). Each
// event is defined by a scalar predicate Func<ports, double>; the
// integrator checks for sign changes between consecutive ticks and
// records the crossing time via linear interpolation.
//
// Examples:
//   • "Battery SoC hits 0.20" — predicate: ports["battery"]["SoC"] - 0.20
//     direction Falling, terminal=false.
//   • "Tank empties" — predicate: ports["tank"]["StoredMass_kg"] - 0.0
//     direction Falling, terminal=true (stop the simulation).
//   • "Motor RPM exceeds redline" — predicate: ports["motor"]["RPM"] - 9000
//     direction Rising, terminal=true.
//
// Linear interpolation (not full bisection) for the crossing time is
// sufficient for the typical small-dt smooth-port-value case; full
// bisection would require re-running the network solve at intermediate
// dt values, which is expensive (network solve is the dominant cost per
// tick). When sub-dt accuracy is needed, the caller should lower dt or
// the adaptive controller's dtMax.
//
// Determinism: events fire deterministically because the predicate is a
// pure function of the port snapshot, and the snapshot is bit-identical
// across re-runs (when the integrator itself is deterministic per the
// SA strict-determinism contract).

using System;
using System.Collections.Generic;

namespace Voxelforge.Integration;

/// <summary>
/// Sign-change direction for an <see cref="EventDefinition"/>'s scalar
/// predicate. The integrator only fires the event when the predicate
/// crosses zero in the matching direction.
/// </summary>
internal enum EventDirection
{
    /// <summary>Fire on either rising or falling sign change.</summary>
    Either = 0,

    /// <summary>Fire only when the predicate goes from negative to positive.</summary>
    Rising = 1,

    /// <summary>Fire only when the predicate goes from positive to negative.</summary>
    Falling = 2,
}

/// <summary>
/// Definition of a scalar zero-crossing event the integrator should
/// watch for. Sprint SI.W23.
/// </summary>
/// <param name="Name">
/// Identifier the resulting <see cref="DetectedEvent"/> carries. Must
/// be unique within a single integrator run; registering two events
/// with the same name throws on the second registration.
/// </param>
/// <param name="Predicate">
/// Pure scalar function of the network port snapshot. The event fires
/// when the predicate crosses zero between consecutive ticks (in the
/// requested <paramref name="Direction"/>). The crossing time is
/// linear-interpolated from the two surrounding tick predicate values.
/// </param>
/// <param name="Direction">
/// Which sign-change direction triggers the event. Defaults to
/// <see cref="EventDirection.Either"/>.
/// </param>
/// <param name="Terminal">
/// When true, the integrator stops the simulation after recording the
/// event (the final history snapshot is captured at the crossing time).
/// When false (default), the event is recorded but integration
/// continues.
/// </param>
internal sealed record EventDefinition(
    string Name,
    Func<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>, double> Predicate,
    EventDirection Direction = EventDirection.Either,
    bool Terminal = false);

/// <summary>
/// A detected zero-crossing event. Sprint SI.W23.
/// </summary>
/// <param name="Name">Identifier matching the originating
/// <see cref="EventDefinition.Name"/>.</param>
/// <param name="Time_s">Linear-interpolated crossing time [s].</param>
/// <param name="Direction">Sign-change direction observed.</param>
/// <param name="PreviousValue">Predicate value at the previous tick.</param>
/// <param name="CurrentValue">Predicate value at the current tick.</param>
internal sealed record DetectedEvent(
    string         Name,
    double         Time_s,
    EventDirection Direction,
    double         PreviousValue,
    double         CurrentValue);
