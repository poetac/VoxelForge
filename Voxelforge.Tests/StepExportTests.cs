// StepExportTests.cs — unit tests for Voxelforge.IO.StepExport (OOB-8).
//
// All tests are pure Core-only: no PicoGK, no subprocess.
// A synthetic ChamberContour is built via ChamberContourGenerator.Generate()
// with fixed parameters so tests reproduce bit-identically across machines.

using System.IO;
using Voxelforge.Chamber;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class StepExportTests
{
    // -----------------------------------------------------------------------
    // Test fixture helpers
    // -----------------------------------------------------------------------

    private static ChamberContour MakeTestContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm:        15.0,
            contractionRatio:       5.0,
            expansionRatio:         8.0,
            characteristicLength_m: 0.9,
            thetaN_deg:             30.0,
            thetaE_deg:             10.0,
            bellLengthFraction:     0.8,
            stationCount:           60);   // fewer stations → fast test, still >2

    private static RegenChamberDesign MakeTestDesign() => new()
    {
        GasSideWallThickness_mm  = 0.8,
        ChannelHeightChamber_mm  = 2.5,
        ChannelHeightThroat_mm   = 1.5,
        ChannelHeightExit_mm     = 2.0,
        OuterJacketThickness_mm  = 2.0,
    };

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void SaveFromContour_CreatesFileWithValidStepHeader()
    {
        using var tmp = TestTempFile.WithUniqueName("step_header", ".step");
        var contour = MakeTestContour();
        var design  = MakeTestDesign();

        StepExport.SaveFromContour(tmp.Path, contour, design);

        string content = File.ReadAllText(tmp.Path);
        Assert.StartsWith("ISO-10303-21;", content);
        Assert.EndsWith("END-ISO-10303-21;", content.TrimEnd());
    }

    [Fact]
    public void SaveFromContour_ContainsManifoldSolidBrepEntity()
    {
        using var tmp = TestTempFile.WithUniqueName("step_brep", ".step");
        StepExport.SaveFromContour(tmp.Path, MakeTestContour(), MakeTestDesign());

        string content = File.ReadAllText(tmp.Path);
        Assert.Contains("MANIFOLD_SOLID_BREP", content);
    }

    [Fact]
    public void SaveFromContour_ContainsSurfaceOfRevolutionEntity()
    {
        using var tmp = TestTempFile.WithUniqueName("step_sor", ".step");
        StepExport.SaveFromContour(tmp.Path, MakeTestContour(), MakeTestDesign());

        string content = File.ReadAllText(tmp.Path);
        Assert.Contains("SURFACE_OF_REVOLUTION", content);
    }

    [Fact]
    public void SaveFromContour_FileHasReasonableSize()
    {
        using var tmp = TestTempFile.WithUniqueName("step_size", ".step");
        StepExport.SaveFromContour(tmp.Path, MakeTestContour(), MakeTestDesign());

        long bytes = new FileInfo(tmp.Path).Length;
        Assert.True(bytes > 10_000, $"Expected STEP file > 10 KB, got {bytes} bytes");
    }

    [Fact]
    public void SaveFromContour_IsDeterministic()
    {
        using var tmp1 = TestTempFile.WithUniqueName("step_det1", ".step");
        using var tmp2 = TestTempFile.WithUniqueName("step_det2", ".step");

        var contour = MakeTestContour();
        var design  = MakeTestDesign();

        // No gitSha or gateManifest → output contains no timestamp-variable data.
        StepExport.SaveFromContour(tmp1.Path, contour, design);
        StepExport.SaveFromContour(tmp2.Path, contour, design);

        byte[] bytes1 = File.ReadAllBytes(tmp1.Path);
        byte[] bytes2 = File.ReadAllBytes(tmp2.Path);
        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public void SaveFromContour_Stats_ReflectProfileStationCount()
    {
        using var tmp = TestTempFile.WithUniqueName("step_stats", ".step");
        var contour = MakeTestContour();
        var design  = MakeTestDesign();

        StepExportStats stats = StepExport.SaveFromContour(tmp.Path, contour, design);

        Assert.Equal(contour.Stations.Length, stats.ProfileStationCount);
        Assert.True(stats.EntityCount > 100,
            $"Expected > 100 STEP entities, got {stats.EntityCount}");
        Assert.True(stats.FileBytes > 0);
        Assert.True(stats.ElapsedMs >= 0);
    }

    [Fact]
    public void SaveFromContour_WithMetadata_EmbedsShaInFileDescription()
    {
        using var tmp = TestTempFile.WithUniqueName("step_meta", ".step");
        const string testSha = "abcdef1234567890abcdef1234567890abcdef12";

        StepExport.SaveFromContour(tmp.Path, MakeTestContour(), MakeTestDesign(),
            gitSha: testSha, gateManifest: "PASS");

        string content = File.ReadAllText(tmp.Path);
        // The first 7 chars of the SHA should appear in the PRODUCT description.
        Assert.Contains("git=abcdef1", content);
        Assert.Contains("gates=PASS", content);
    }
}
