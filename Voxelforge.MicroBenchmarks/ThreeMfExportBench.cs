// BB-5 (2026-04-29): ThreeMFExport.SaveFromStl microbench. Closes the
// 3MF coverage gap from the BB-5 audit brief. Synthetic 4-triangle
// binary STL is materialised in [GlobalSetup] so the bench avoids
// touching PicoGK (`MicroBenchmarks` is a PicoGK-free project).

using System.IO;
using BenchmarkDotNet.Attributes;
using Voxelforge.IO;
using Voxelforge.MicroBenchmarks.Helpers;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class ThreeMfExportBench
{
    private string _tempDir = null!;
    private string _syntheticStlPath = null!;
    private RegenGenerationResult _generationResult = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "regen-microbenchmarks-3mf");
        Directory.CreateDirectory(_tempDir);
        _syntheticStlPath = Path.Combine(_tempDir, "tetrahedron.stl");
        WriteSyntheticTetrahedronStl(_syntheticStlPath);

        var (cond, design) = CanonicalDesignFixtures.MerlinSeed();
        _generationResult = RegenChamberOptimization.GenerateWith(
            cond, design,
            skipVoxelGeometry: true, skipMfgAnalysis: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Benchmark]
    public void Export_DefaultChamber_3MF()
    {
        string outPath = Path.Combine(_tempDir, $"out_{System.Guid.NewGuid():N}.3mf");
        try { ThreeMFExport.SaveFromStl(_syntheticStlPath, outPath, _generationResult); }
        finally { try { File.Delete(outPath); } catch { } }
    }

    // Writes a 4-triangle (tetrahedron) binary STL — 80-byte header +
    // uint32 triangle count + 50 bytes per triangle (12-byte normal +
    // 36-byte vertices + 2-byte attribute). Total 284 bytes. Format:
    // https://en.wikipedia.org/wiki/STL_(file_format)#Binary_STL
    private static void WriteSyntheticTetrahedronStl(string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(new byte[80]);                    // header
        bw.Write((uint)4);                         // triangle count

        // Tetrahedron with vertices A=(0,0,0), B=(10,0,0), C=(5,10,0), D=(5,5,10).
        var A = (X: 0f,  Y: 0f,  Z: 0f);
        var B = (X: 10f, Y: 0f,  Z: 0f);
        var C = (X: 5f,  Y: 10f, Z: 0f);
        var D = (X: 5f,  Y: 5f,  Z: 10f);
        var triangles = new[]
        {
            (A, B, C), // base
            (A, B, D), // side 1
            (B, C, D), // side 2
            (C, A, D), // side 3
        };
        foreach (var (v0, v1, v2) in triangles)
        {
            // Normal placeholder — readers (including ThreeMFExport) discard.
            bw.Write(0f); bw.Write(0f); bw.Write(0f);
            bw.Write(v0.X); bw.Write(v0.Y); bw.Write(v0.Z);
            bw.Write(v1.X); bw.Write(v1.Y); bw.Write(v1.Z);
            bw.Write(v2.X); bw.Write(v2.Y); bw.Write(v2.Z);
            bw.Write((ushort)0);                   // attribute byte count
        }
    }
}
