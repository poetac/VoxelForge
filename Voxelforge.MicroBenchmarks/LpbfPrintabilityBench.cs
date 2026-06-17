// BB-5 (2026-04-29): LpbfPrintabilityAnalysis.ForChamber microbench.
//
// 544 LOC of pure-math LPBF analysis (overhang + trapped-powder +
// drain-path + orientation-advisor) live in
// `Voxelforge.Core.Geometry.LpbfAnalysis`. The composite
// entry point `LpbfPrintabilityAnalysis.ForChamber` is the gate the
// SA hot path consults via `OVERHANG_ANGLE_EXCEEDED` /
// `TRAPPED_POWDER_REGION` / `DRAIN_PATH_MISSING`. BDN measures the
// microbench cost so a future routing-graph rewrite can be
// quantitatively defended.

using System.Numerics;
using BenchmarkDotNet.Attributes;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Geometry.LpbfAnalysis;
using Voxelforge.HeatTransfer;
using Voxelforge.MicroBenchmarks.Helpers;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class LpbfPrintabilityBench
{
    private ChamberContour _contour = null!;
    private ChannelSchedule _channels = null!;
    private LpbfMaterialProfile _material = null!;
    private Vector3 _buildAxis;

    [GlobalSetup]
    public void Setup()
    {
        var (cond, design, contour, inputs) = SolverInputsFactory.MakeSolverInputs(stationCount: 80);
        _contour = contour;
        _channels = inputs.Channels;
        _material = LpbfMaterialProfiles.GRCop42;
        _buildAxis = new Vector3(0, 0, 1);
    }

    [Benchmark]
    public LpbfPrintabilityResult ForChamber_GRCop42()
        => LpbfPrintabilityAnalysis.ForChamber(
            _contour, _channels, _material, _buildAxis,
            azimuthalSamples: 24);

    [Benchmark]
    public LpbfPrintabilityResult ForChamber_GRCop42_HighAzimuthal()
        => LpbfPrintabilityAnalysis.ForChamber(
            _contour, _channels, _material, _buildAxis,
            azimuthalSamples: 48);
}
