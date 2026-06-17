// DesignPersistence Save/Load round-trip microbench.
// Timing shifts when schema bumps occur because the migration chain runs Pre-Load.

using System.IO;
using BenchmarkDotNet.Attributes;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.MicroBenchmarks.Helpers;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class JsonRoundTripBench
{
    private OperatingConditions _cond = null!;
    private RegenChamberDesign _design = null!;
    private string _tempDir = null!;
    private string _savedPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        var (cond, design) = CanonicalDesignFixtures.MerlinSeed();
        _cond = cond;
        _design = design;

        _tempDir = Path.Combine(Path.GetTempPath(), "regen-microbenchmarks-json");
        Directory.CreateDirectory(_tempDir);
        _savedPath = Path.Combine(_tempDir, "merlin.json");
        DesignPersistence.Save(_savedPath, _cond, _design, r: null);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Benchmark]
    public void Save_DefaultDesign()
    {
        string path = Path.Combine(_tempDir, $"save_{System.Guid.NewGuid():N}.json");
        try { DesignPersistence.Save(path, _cond, _design, r: null); }
        finally { try { File.Delete(path); } catch { } }
    }

    [Benchmark]
    public SavedDesign? Load_DefaultDesign()
        => DesignPersistence.Load(_savedPath);
}
