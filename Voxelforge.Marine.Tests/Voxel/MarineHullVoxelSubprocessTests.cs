// MarineHullVoxelSubprocessTests.cs — subprocess round-trip tests for the
// marine hull voxel pipeline (Marine Wave-2 M3).
//
// Runs the MarineStlExporter exe as a subprocess (same rationale as
// Voxelforge.Airbreathing.Tests.RamjetVoxelBuilderSubprocessTests):
// Voxelforge.Marine.Tests targets net9.0 (not net9.0-windows) and does
// not reference Voxelforge.Marine.Voxels, preserving cross-platform
// test execution. The subprocess pattern is correct even under PicoGK
// 2.0.0 for this reason — not because of the old pitfall #8.
//
// Marked [Trait("Category", "Subprocess")]; runs ~15-30 s at 0.4 mm voxel.
// Default xUnit runs include them; fast unit-only runs can filter via
// `dotnet test --filter "Category!=Subprocess"`.

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelforge.Marine;
using Voxelforge.Marine.Tests.Helpers;
using Xunit;
using static Voxelforge.Marine.Tests.ScaffoldingSmokeTests;

namespace Voxelforge.Marine.Tests.Voxel;

[Trait("Category", "Subprocess")]
public sealed class MarineHullVoxelSubprocessTests
{
    /// <summary>
    /// REMUS-100 at 0.4 mm voxel must produce a non-trivial STL with at
    /// least 50 000 triangles, validating the full SDF → voxelise → mesh
    /// pipeline is wired end-to-end.
    /// </summary>
    [Fact]
    public void Build_Remus100_At0p4mmVoxel_ProducesAtLeast50kTriangles()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;   // clean skip when exe not yet built

        var design  = MakeRemus100Design();
        var (triangles, stdout, stderr) = RunBuild(exePath, design, voxelMm: 0.4, wallMm: 5.0);

        Assert.True(triangles >= 50_000,
            $"Expected ≥ 50 000 triangles at 0.4 mm voxel, got {triangles}.\n"
          + $"Stdout:\n{stdout}\nStderr:\n{stderr}");
    }

    /// <summary>
    /// CylindricalHemi hull at 0.8 mm voxel must produce a non-trivial STL.
    /// Validates M2 CylindricalHemi branch in the voxel builder.
    /// </summary>
    [Fact]
    public void Build_CylHemi_At0p8mmVoxel_ProducesNonTrivialStl()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var design = new MarineDesign(
            Kind:                MarineKind.AuvMidBody,
            Length_m:            1.595,
            Diameter_m:          0.190,
            NoseFairingFraction: 0.1,   // ignored for CylindricalHemi
            TailFairingFraction: 0.1,   // ignored for CylindricalHemi
            WallThickness_m:     0.005,
            MaterialIndex:       1,
            DepthRating_m:       100.0,
            HullFamily:          HullFamily.CylindricalHemi);

        var (triangles, stdout, stderr) = RunBuild(exePath, design, voxelMm: 0.8, wallMm: 5.0);

        Assert.True(triangles > 5_000,
            $"Expected > 5 000 triangles for CylHemi at 0.8 mm voxel, got {triangles}.\n"
          + $"Stdout:\n{stdout}\nStderr:\n{stderr}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string LocateExporter()
    {
        string exePath = SubprocessRunner.LocateUnderRepo(
            "Voxelforge.Marine.Voxels/bin/Debug/net9.0-windows/Voxelforge.Marine.StlExporter.exe");
        if (!File.Exists(exePath))
        {
            exePath = SubprocessRunner.LocateUnderRepo(
                "Voxelforge.Marine.Voxels/bin/Release/net9.0-windows/Voxelforge.Marine.StlExporter.exe");
        }
        return exePath;
    }

    private static (long Triangles, string Stdout, string Stderr) RunBuild(
        string exePath,
        MarineDesign design,
        double voxelMm,
        double wallMm)
    {
        string designJson = JsonSerializer.Serialize(design, JsonOpts);
        string designPath = Path.Combine(Path.GetTempPath(),
            $"marine-test-{Guid.NewGuid():N}.json");
        string stlPath = Path.Combine(Path.GetTempPath(),
            $"marine-test-{Guid.NewGuid():N}.stl");
        File.WriteAllText(designPath, designJson);

        try
        {
            var args = new List<string>
            {
                "--design", designPath,
                "--voxel",  voxelMm.ToString("F3", CultureInfo.InvariantCulture),
                "--out",    stlPath,
                "--wall",   wallMm.ToString("F3", CultureInfo.InvariantCulture),
            };

            var result = SubprocessRunner.Run(exePath, args, timeoutMs: 120_000);
            Assert.True(result.ExeExists, "MarineStlExporter exe not found.");
            Assert.False(result.WaitTimedOut,
                $"MarineStlExporter timed out after 120 s.\nStderr:\n{result.Stderr}");
            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(stlPath),
                $"STL file not written.\nStderr:\n{result.Stderr}");

            long triangles = ParseBenchTriangleCount(result.Stdout);
            return (triangles, result.Stdout, result.Stderr);
        }
        finally
        {
            try { if (File.Exists(designPath)) File.Delete(designPath); } catch { /* best-effort */ }
            try { if (File.Exists(stlPath)) File.Delete(stlPath); } catch { /* best-effort */ }
        }
    }

    private static long ParseBenchTriangleCount(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            int idx = line.IndexOf("BENCH triangle_count=", StringComparison.Ordinal);
            if (idx < 0) continue;
            string tail = line[(idx + "BENCH triangle_count=".Length)..].Trim();
            if (long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                return n;
        }
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}
