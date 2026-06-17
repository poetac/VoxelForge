// AirbreathingGateInput.cs — gate-evaluator input shim for the
// air-breathing pillar (2026-05-05).
//
// AirbreathingFeasibility.Evaluate takes (design, cond, stations, compressor
// diagnostics, turbine diagnostics) — five inputs, building the violations
// list the gates produce. To fit the registry's single-input shape (per
// GenericGateRegistry.cs), gate predicates take this shim record
// bundling the five inputs.
//
// Shim is internal-only — does not appear in the pillar's public API.
// AirbreathingFeasibility.Evaluate keeps its existing signature and
// constructs an AirbreathingGateInput internally before looping the
// registry.

using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// Bundle of inputs a registered air-breathing gate predicate needs.
/// Internal — created by <see cref="AirbreathingFeasibility.Evaluate"/>
/// before looping <see cref="AirbreathingGateRegistry.Instance"/>.
/// </summary>
internal sealed record AirbreathingGateInput(
    AirbreathingEngineDesign Design,
    FlightConditions Conditions,
    StationMap Stations,
    MapInfo? CompressorDiagnostics,
    MapInfo? TurbineDiagnostics);
