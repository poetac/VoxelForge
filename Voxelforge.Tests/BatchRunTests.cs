// BatchRunTests.cs — Contract tests for the 2026-04-22 batch-run mode.
//
// Verifies the data-model pieces of the batch workflow without spinning
// the full WinForms host. The UI-side plumbing (button, folder dialog,
// progress wiring) is exercised manually per DEMO_SCRIPT.md; here we
// cover the bits that are easy to regression-lock in xUnit:
//   • BatchRunSettings defaults match documented behaviour.
//   • Design round-trips cleanly through DesignPersistence.Save → Load,
//     which is what batch runs use for the .rcd.json artefact.
//   • The report builder accepts the batch-time signatures used by
//     Program.WriteBatchOutputs (bestSoFarIteration = 0 + non-null
//     Pareto snapshot).

using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Voxelforge.UI;

namespace Voxelforge.Tests;

public class BatchRunTests
{
    [Fact]
    public void BatchRunSettings_Defaults_AreSensible()
    {
        var b = new BatchRunSettings();
        Assert.True(b.SaveDesignJson);
        Assert.True(b.SaveStl);
        Assert.True(b.SaveReport);
        Assert.False(b.SaveParetoCsv);  // opt-in since it's niche
        Assert.Equal(0.4f, b.StlVoxelMM);
        Assert.Equal("", b.OutputFolder);
    }

    [Fact]
    public void BatchRunSettings_CarriesInitValues()
    {
        var b = new BatchRunSettings
        {
            OutputFolder   = @"C:\tmp\batch",
            SaveDesignJson = false,
            SaveStl        = false,
            SaveReport     = true,
            SaveParetoCsv  = true,
            StlVoxelMM     = 0.15f,
        };
        Assert.Equal(@"C:\tmp\batch", b.OutputFolder);
        Assert.False(b.SaveDesignJson);
        Assert.True(b.SaveParetoCsv);
        Assert.Equal(0.15f, b.StlVoxelMM);
    }

    [Fact]
    public void DesignPersistence_RoundTrip_IsStable_ForBatchSavedDesigns()
    {
        // Program.WriteBatchOutputs calls DesignPersistence.Save with a
        // null result when no geometry yet, and with a populated result
        // after optimization. Lock the null-result case (safe form).
        using var tmp = TestTempFile.WithUniqueName("batch_probe", "rcd.json");
        var cond = new OperatingConditions { Thrust_N = 2000, ChamberPressure_Pa = 6.9e6 };
        var design = new RegenChamberDesign { ChannelCount = 96 };
        DesignPersistence.Save(tmp.Path, cond, design, r: null);
        Assert.True(File.Exists(tmp.Path));
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.Equal(2000,  loaded.Conditions!.Thrust_N);
        Assert.Equal(96,    loaded.Design!.ChannelCount);
    }

    [Fact]
    public void ReportExport_AcceptsBatchSignature_WithParetoFront()
    {
        // Simulate the path WriteBatchOutputs takes: bestSoFarIteration = 0
        // (batch writes happen AFTER FinalizeOpt clears the flag) + a
        // populated Pareto list. Build a minimal gen via the physics-only
        // path so we don't need a voxel-capable host.
        var cond = new OperatingConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var pareto = new[]
        {
            new ParetoPoint(900,  1e6,  200, Array.Empty<double>(), 1),
            new ParetoPoint(1100, 0.7e6, 150, Array.Empty<double>(), 2),
        };

        using var tmp = TestTempFile.WithUniqueName("batch_report", "txt");
        ReportExport.SaveToFile(gen, tmp.Path, bestSoFarIteration: 0, pareto);
        var text = File.ReadAllText(tmp.Path);
        Assert.Contains("REGENERATIVELY-COOLED THRUST CHAMBER DESIGN REPORT", text);
        Assert.Contains("PARETO FRONT", text);
        // BEST-SO-FAR banner must NOT appear — batch writes are final.
        Assert.DoesNotContain("BEST-SO-FAR", text);
    }

    [Fact]
    public void OptSettings_Defaults_MatchDocumentedBehaviour()
    {
        var s = new OptSettings();
        Assert.Equal(300, s.MaxIterations);
        Assert.True(s.WarmStart);
        Assert.Equal(1, s.ParallelBatchSize);
        // Track A (2026-04-27): multi-chain default flipped on after
        // benchmark validation across the canonical preset set.
        Assert.True(s.UseMultiChain);
        Assert.Equal(0, s.MultiChainCount);   // 0 = auto-scale
    }

    [Fact]
    public void OptSettings_MultiChainCount_AcceptsExplicitOverride()
    {
        var s = new OptSettings { MultiChainCount = 4 };
        Assert.Equal(4, s.MultiChainCount);
    }

    [Fact]
    public void OptSettings_LegacySingleChain_StillSelectable()
    {
        // Users who want the pre-Track-A single-chain + ParallelBatchSize
        // path can still select it explicitly via the chkMultiChainSa
        // toggle. Confirms the flag is settable, not forced-on.
        var s = new OptSettings { UseMultiChain = false };
        Assert.False(s.UseMultiChain);
    }
}
