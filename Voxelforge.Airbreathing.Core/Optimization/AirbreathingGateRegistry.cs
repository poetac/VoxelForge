// AirbreathingGateRegistry.cs — air-breathing pillar's gate registry
// instance (2026-05-05).
//
// Static-singleton wrapper around `GateRegistry<AirbreathingGateInput>`.
// First read of `Instance.All` triggers `AirbreathingGates.RegisterAll()`
// via the bootstrap delegate.
//
// Pulsejet Wave 1 PR-2 (this commit): registry exists but
// AirbreathingGates.RegisterAll() is a stub — no gates registered yet.
// Pulsejet Wave 1 PR-4 will add `PULSEJET_BLOWOUT_LEAN` +
// `PULSEJET_ACOUSTIC_OVERPRESSURE` to AirbreathingGates.RegisterAll().
//
// The 22 inline air-breathing gates in AirbreathingFeasibility.cs:34-179
// are NOT lifted in Wave 1 — that's a separate Stream B refactor sprint.
// AirbreathingFeasibility.Evaluate runs both paths during the transition:
// the existing inline if/else cascade, then a registry loop for gates that
// have migrated. Pulsejet gates flow through the registry path.

using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// Air-breathing pillar's static-singleton gate registry. Mirrors the
/// generic <see cref="GateRegistry{T}"/> additive-overlay shape but typed
/// to the pillar's own <see cref="AirbreathingGateInput"/> shim.
/// </summary>
/// <remarks>
/// First read of <see cref="Instance"/>'s <c>All</c>/<c>ById</c>/<c>TryGetById</c>
/// triggers <see cref="AirbreathingGates.RegisterAll"/> via the bootstrap
/// delegate. Lock-free reads thereafter.
/// </remarks>
internal static class AirbreathingGateRegistry
{
    /// <summary>
    /// The pillar's gate registry instance. Predicates registered here run
    /// during <see cref="AirbreathingFeasibility.Evaluate"/> after the
    /// existing inline gate cascade. Internal because
    /// <see cref="AirbreathingGateInput"/> is internal — the shim is a
    /// pillar-private detail, not part of the public surface.
    /// </summary>
    internal static readonly GateRegistry<AirbreathingGateInput> Instance =
        new(AirbreathingGates.RegisterAll);
}
