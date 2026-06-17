// BB-3 (2026-04-29): CfdFieldExport.Write / WriteAerospike microbenchmarks.
//
// Pre-cascade reference for the CFD handoff path. Output goes to
// `%TEMP%/regen-microbenchmarks/cfd-export-bench-*.vti` so the working
// tree stays clean; cleanup runs in [GlobalCleanup].

using BenchmarkDotNet.Attributes;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.MicroBenchmarks.Helpers;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class CfdExportBench
{
    private ChamberContour _bellContour = null!;
    private RegenSolverOutputs _bellSolver = null!;
    private ChannelSchedule _bellChannels = null!;
    private string _tempDir = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (cond, design, contour, inputs) = SolverInputsFactory.MakeSolverInputs(stationCount: 80);
        _bellContour = contour;
        _bellChannels = inputs.Channels;
        _bellSolver = RegenCoolingSolver.Solve(inputs);

        _tempDir = Path.Combine(Path.GetTempPath(), "regen-microbenchmarks");
        Directory.CreateDirectory(_tempDir);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Benchmark]
    public CfdFieldStats WriteBell_96cubed()
    {
        string outPath = Path.Combine(_tempDir, $"bell_96_{Guid.NewGuid():N}.vti");
        try
        {
            var grid = new CfdFieldGrid(Nx: 96, Ny: 96, Nz: 96,
                TransverseHalfWidth_mm: 1.10 * Math.Max(_bellContour.ChamberRadius_mm, _bellContour.ExitRadius_mm));
            return CfdFieldExport.Write(outPath, _bellContour, _bellChannels, _bellSolver,
                outerJacketThickness_mm: 2.5, grid: grid);
        }
        finally { TryDelete(outPath); }
    }

    [Benchmark]
    public CfdFieldStats WriteBell_192cubed()
    {
        string outPath = Path.Combine(_tempDir, $"bell_192_{Guid.NewGuid():N}.vti");
        try
        {
            var grid = new CfdFieldGrid(Nx: 192, Ny: 96, Nz: 96,
                TransverseHalfWidth_mm: 1.10 * Math.Max(_bellContour.ChamberRadius_mm, _bellContour.ExitRadius_mm));
            return CfdFieldExport.Write(outPath, _bellContour, _bellChannels, _bellSolver,
                outerJacketThickness_mm: 2.5, grid: grid);
        }
        finally { TryDelete(outPath); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
