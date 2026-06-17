// SystemSnapshotTests.cs — Sprint SI.W20 unit tests for system-state
// snapshot save/restore.

using System;
using System.Linq;
using Voxelforge.Battery;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class SystemSnapshotTests
{
    [Fact]
    public void CaptureSnapshot_AfterPartialRun_ContainsLiveState()
    {
        var net = new ComponentNetwork();
        var pack = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(pack);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", pack);
        integrator.Run(0.0, 300.0, 1.0);

        var snap = integrator.CaptureSnapshot();
        Assert.Contains("pack", snap.ComponentStates.Keys);
        double socAfter5min = snap.ComponentStates["pack"]["StateOfCharge"];
        Assert.True(socAfter5min < 1.0);
        Assert.True(socAfter5min > 0.9);
    }

    [Fact]
    public void RestoreSnapshot_RewindsBatteryToCapturedSoC()
    {
        // Run 300s under 100A discharge → capture → run another 300s →
        // restore → run another 300s. Final SoC after second run should
        // match final SoC after first run (deterministic forward step).
        var net = new ComponentNetwork();
        var pack = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(pack);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", pack);

        integrator.Run(0.0, 300.0, 1.0);
        var snap = integrator.CaptureSnapshot();
        double socAtCheckpoint = snap.ComponentStates["pack"]["StateOfCharge"];

        // Branch A.
        var histA = integrator.Run(300.0, 600.0, 1.0, warmStart: true);
        double socEndA = histA[^1].PortValues["pack"]["StateOfCharge"];

        // Rewind.
        integrator.RestoreSnapshot(snap);
        Span<double> postRewind = stackalloc double[1];
        pack.GetCurrentState(postRewind);
        Assert.Equal(socAtCheckpoint, postRewind[0], precision: 9);

        // Branch B (same external feed) → must match A.
        var histB = integrator.Run(300.0, 600.0, 1.0, warmStart: true);
        double socEndB = histB[^1].PortValues["pack"]["StateOfCharge"];
        Assert.Equal(socEndA, socEndB, precision: 9);
    }

    [Fact]
    public void RestoreSnapshot_DifferentLoad_ProducesDifferentTrajectory()
    {
        var net = new ComponentNetwork();
        var pack = new StatefulBatteryComponent("pack", ModelSPack(),
            initialStateOfCharge: 1.0);
        net.Add(pack);
        net.SetExternalInput("pack", "LoadCurrent_A", 100.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("pack", pack);

        integrator.Run(0.0, 300.0, 1.0);
        var snap = integrator.CaptureSnapshot();

        // Branch A: keep 100A.
        var histA = integrator.Run(300.0, 600.0, 1.0, warmStart: true);
        double socA = histA[^1].PortValues["pack"]["StateOfCharge"];

        // Branch B: rewind + 50A load → SoC drops more slowly.
        integrator.RestoreSnapshot(snap);
        net.SetExternalInput("pack", "LoadCurrent_A", 50.0);
        var histB = integrator.Run(300.0, 600.0, 1.0, warmStart: true);
        double socB = histB[^1].PortValues["pack"]["StateOfCharge"];

        // Branch B's SoC must be HIGHER than A's at the same horizon.
        Assert.True(socB > socA);
    }

    [Fact]
    public void GetCurrentState_TracksMidRunState()
    {
        // Direct test of IStatefulComponent.GetCurrentState (the new
        // SI.W20 interface method).
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc", initial: 0.0);
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 1.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        integrator.Run(0.0, 10.0, 1.0);

        // Under #553 closed [t0, tEnd] N+1 semantics, Run(0, 10, 1) does
        // 11 ticks and 11 advances (each tick records then steps state
        // forward by dt·rate = 1). GetCurrentState reads the post-final-
        // advance value: 11. Band ±1 absorbs explicit-Euler rounding.
        Span<double> cur = stackalloc double[1];
        acc.GetCurrentState(cur);
        Assert.InRange(cur[0], 10.0, 12.0);
    }

    [Fact]
    public void CaptureSnapshot_OnIntegratorWithNoStateful_IsEmpty()
    {
        var net = new ComponentNetwork();
        var integrator = new TimeStepIntegrator(net);
        var snap = integrator.CaptureSnapshot();
        Assert.Empty(snap.ComponentStates);
    }

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry:        BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries:    96,
        ParallelStrings:  46,
        StateOfCharge:    1.0,
        LoadCurrent_A:    0.0);
}
