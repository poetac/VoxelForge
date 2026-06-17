// SystemSnapshot.cs — Sprint SI.W20 save/restore of stateful-component
// state across a ComponentNetwork.
//
// Enables what-if branching:
//
//   var integrator = new TimeStepIntegrator(net);
//   integrator.RegisterStateful("pack", pack);
//   integrator.Run(0.0, 1800.0, 1.0);          // half-hour
//   var snap = integrator.CaptureSnapshot();    // checkpoint
//
//   // Branch A: full discharge to end-of-life.
//   integrator.Run(1800.0, 3600.0, 1.0);
//
//   // Branch B: rewind and run a different load profile.
//   integrator.RestoreSnapshot(snap);
//   net.SetExternalInput("pack", "LoadCurrent_A", 50.0);
//   integrator.Run(1800.0, 3600.0, 1.0);
//
// The snapshot captures only state-variable values. Network topology
// (wires + components + external feeds) is not part of the snapshot;
// those are mutated independently on the live network instance.

using System.Collections.Generic;

namespace Voxelforge.Integration;

/// <summary>
/// Immutable snapshot of every stateful component's current state
/// vector (Sprint SI.W20).
/// </summary>
/// <param name="ComponentStates">
/// (componentName → (variableName → value)) full state map at capture
/// time.
/// </param>
internal sealed record SystemSnapshot(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ComponentStates);
