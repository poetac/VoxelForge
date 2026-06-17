// BuildProfile.cs — Per-stage wall-clock profile for ChamberVoxelBuilder.Build.
//
// Purpose: before any CUDA acceleration work, stamp the baseline into
// measurable, per-stage wall-clock numbers so the CUDA gate
// ("≥ 10× on channel kernel or stop") has a real floor to measure against.
//
// Every full Build() call populates ChamberGeometryResult.Profile; the
// physics-only BuildAnalytical path leaves it null. BuildProfiler is the
// zero-allocation helper Build() uses to tally stage ticks; a ref-struct
// scope handles the one-shot stages and an explicit AddTicks method is used
// inside the 40-200-iteration channel loop to avoid scope overhead in the
// hot path.
//
// Stage buckets correspond 1:1 to the numbered "§1-3 / §4 / …" comments in
// Build(), so the profile map and the source layout stay auditable.
//
// BENCH emission is done by EmitBench(), which writes structured
// "BENCH key=value" lines that the StlExporter subprocess pipes to stdout
// and the Benchmarks console app writes to stdout + a JSONL history file.

using System.Diagnostics;
using System.IO;
using PicoGK;

namespace Voxelforge.Geometry;

// BuildProfile record extracted to Voxelforge.Core/Geometry/
// as part of A1. BuildProfiler stays here because it tallies ticks during
// PicoGK voxelization.

/// <summary>
/// Tick-level profiler used by ChamberVoxelBuilder.Build to tally per-stage
/// wall-clock time into a BuildProfile. Zero allocation on the hot path —
/// Begin() returns a ref-struct scope, and the in-loop sub-stages use
/// AddTicks() with raw Stopwatch.GetTimestamp() deltas.
/// </summary>
internal sealed class BuildProfiler
{
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    private long _shellTicks;
    private long _channelsTicks;
    private long _chVoxTicks;
    private long _chBoolTicks;
    private long _manifoldsTicks;
    private long _radialPortsTicks;
    private long _smoothenTicks;
    private long _injFlangeTicks;
    private long _mountFlangeTicks;
    private long _injBoresTicks;
    private long _lateTicks;
    private long _finalTicks;
    private int  _channelCount;

    public enum Stage
    {
        Shell,
        ChannelsOuter,
        ChannelVoxelise,
        ChannelBoolSubtract,
        Manifolds,
        RadialPorts,
        Smoothen,
        InjectorFlange,
        MountingFlange,
        InjectorBores,
        LateFeatures,
        FinalMeasurements
    }

    public Scope Begin(Stage stage) => new Scope(this, stage);

    public void AddTicks(Stage stage, long ticks) => Accumulate(stage, ticks);

    public void NoteChannelCount(int n) => _channelCount = n;

    public BuildProfile Finalize(BBox3 bounds, double voxelSize_mm)
    {
        double totalMs = TicksToMs(Stopwatch.GetTimestamp() - _startTimestamp);
        float lx = bounds.vecMax.X - bounds.vecMin.X;
        float ly = bounds.vecMax.Y - bounds.vecMin.Y;
        float lz = bounds.vecMax.Z - bounds.vecMin.Z;
        long dense = voxelSize_mm > 0
            ? (long)((double)lx / voxelSize_mm)
              * (long)((double)ly / voxelSize_mm)
              * (long)((double)lz / voxelSize_mm)
            : 0;

        return new BuildProfile(
            Shell_ms:               TicksToMs(_shellTicks),
            Channels_ms:            TicksToMs(_channelsTicks),
            ChannelVoxelise_ms:     TicksToMs(_chVoxTicks),
            ChannelBoolSubtract_ms: TicksToMs(_chBoolTicks),
            ChannelCount:           _channelCount,
            Manifolds_ms:           TicksToMs(_manifoldsTicks),
            RadialPorts_ms:         TicksToMs(_radialPortsTicks),
            Smoothen_ms:            TicksToMs(_smoothenTicks),
            InjectorFlange_ms:      TicksToMs(_injFlangeTicks),
            MountingFlange_ms:      TicksToMs(_mountFlangeTicks),
            InjectorBores_ms:       TicksToMs(_injBoresTicks),
            LateFeatures_ms:        TicksToMs(_lateTicks),
            FinalMeasurements_ms:   TicksToMs(_finalTicks),
            Total_ms:               totalMs,
            VoxelSize_mm:           voxelSize_mm,
            BBoxLx_mm:              lx,
            BBoxLy_mm:               ly,
            BBoxLz_mm:               lz,
            DenseEquivalentVoxels:  dense);
    }

    internal static double TicksToMs(long ticks) =>
        (double)ticks / Stopwatch.Frequency * 1000.0;

    private void Accumulate(Stage s, long ticks)
    {
        switch (s)
        {
            case Stage.Shell:               _shellTicks       += ticks; break;
            case Stage.ChannelsOuter:       _channelsTicks    += ticks; break;
            case Stage.ChannelVoxelise:     _chVoxTicks       += ticks; break;
            case Stage.ChannelBoolSubtract: _chBoolTicks      += ticks; break;
            case Stage.Manifolds:           _manifoldsTicks   += ticks; break;
            case Stage.RadialPorts:         _radialPortsTicks += ticks; break;
            case Stage.Smoothen:            _smoothenTicks    += ticks; break;
            case Stage.InjectorFlange:      _injFlangeTicks   += ticks; break;
            case Stage.MountingFlange:      _mountFlangeTicks += ticks; break;
            case Stage.InjectorBores:       _injBoresTicks    += ticks; break;
            case Stage.LateFeatures:        _lateTicks        += ticks; break;
            case Stage.FinalMeasurements:   _finalTicks       += ticks; break;
        }
    }

    /// <summary>
    /// Ref-struct scope used with a `using` block to time a single stage.
    /// Zero heap allocation; Dispose() accumulates (now - t0) into the
    /// profiler's stage bucket.
    /// </summary>
    internal readonly ref struct Scope
    {
        private readonly BuildProfiler _p;
        private readonly Stage _s;
        private readonly long _t0;
        internal Scope(BuildProfiler p, Stage s)
        {
            _p = p; _s = s; _t0 = Stopwatch.GetTimestamp();
        }
        public void Dispose() => _p.Accumulate(_s, Stopwatch.GetTimestamp() - _t0);
    }

}

/// <summary>
/// Return value of ChamberVoxelBuilder.ExportStl when the caller wants the
/// meshing / write split. The legacy string-returning overload stays intact
/// so existing call sites (Program.cs fast-path export, 3MF export) keep
/// working without changes.
/// </summary>
public sealed record ExportStlResult(
    string Message,
    double Meshing_ms,
    double StlWrite_ms,
    int    TriangleCount,
    long   StlBytes);
