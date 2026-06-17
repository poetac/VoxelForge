// RamjetVoxelBuilderSubprocessTests — voxel-build round-trip + LPBF
// gate-firing for the Step-1 sub-step 1c ramjet PicoGK pipeline.
//
// 2026-05-04 (post-PR-#374): pitfall #8 (xUnit + PicoGK Library disposal)
// is RESOLVED under PicoGK 2.0.0, and the rocket-side voxel tests have
// migrated in-process. This project keeps the subprocess pattern for a
// DIFFERENT reason: Voxelforge.Airbreathing.Tests deliberately targets
// `net9.0` (not net9.0-windows) and does not reference Voxelforge.Airbreathing.Voxels,
// preserving cross-platform test execution. Migrating these tests in-process
// would (a) flip the TFM to net9.0-windows, (b) add a PicoGK reference. Both
// are reasonable but a deliberate trade — tracked as a follow-on cleanup
// after the user confirms the cross-platform-test property can be relaxed.
//
// Marked [Trait("Category", "Subprocess")] — the test runs ~5-25 s per
// case (one or two voxel builds at 1 mm voxel on a compact ramjet).
// Default xUnit runs include them; a fast unit-only run can filter via
// `dotnet test --filter "Category!=Subprocess"`.

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelforge.Airbreathing.Tests.Helpers;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

[Trait("Category", "Subprocess")]
public class RamjetVoxelBuilderSubprocessTests
{
    /// <summary>
    /// Healthy ramjet — gentle divergent (R_exit ≈ R_combustor, slope ≈ 0.3),
    /// no LPBF overhang violation expected.
    /// </summary>
    [Fact]
    public void Build_ProducesPrintableStl_NoOverhangGate()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;   // clean skip when exe not yet built

        var design = new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:  0.0005,
            CombustorArea_m2:    0.0010,
            CombustorLength_m:   0.030,
            NozzleThroatArea_m2: 0.0005,
            NozzleExitArea_m2:   0.0010,    // R_exit ≈ R_combustor → gentle divergent
            EquivalenceRatio:    0.85);
        var (triangles, stdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, lpbf: "inconel625");

        Assert.True(triangles > 1000,
            $"Expected > 1000 triangles, got {triangles}. Stdout:\n{stdout}");
        Assert.DoesNotContain("GATE OVERHANG_ANGLE_EXCEEDED", stdout);
        Assert.Contains("BENCH lpbf_violation_count=0", stdout);
    }

    /// <summary>
    /// Bad ramjet — steep divergent (R_exit / R_throat ≈ 3, slope ≈ 1.5
    /// → β ≈ 33° &lt; IN625's 40° floor), LPBF overhang gate must fire.
    /// </summary>
    [Fact]
    public void Build_LpbfOverhang_FiresGate()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var design = new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:  0.0005,
            CombustorArea_m2:    0.0010,
            CombustorLength_m:   0.030,
            NozzleThroatArea_m2: 0.0005,
            NozzleExitArea_m2:   0.0050,    // R_exit ≈ 3.16 × R_throat → steep slope
            EquivalenceRatio:    0.85);
        var (triangles, stdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, lpbf: "inconel625");

        Assert.True(triangles > 0, $"Expected > 0 triangles, got {triangles}. Stdout:\n{stdout}");
        Assert.Contains("GATE OVERHANG_ANGLE_EXCEEDED", stdout);
    }

    /// <summary>
    /// Wall thickness flows through the pipeline — same design, two
    /// thicknesses (1 mm vs 3 mm); thicker shell must produce a higher
    /// triangle count by ≥ 1000 (sanity check that --wall is wired).
    /// </summary>
    [Fact]
    public void Build_WallThickness_AffectsTriangleCount()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var design = new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:  0.0005,
            CombustorArea_m2:    0.0010,
            CombustorLength_m:   0.030,
            NozzleThroatArea_m2: 0.0005,
            NozzleExitArea_m2:   0.0010,
            EquivalenceRatio:    0.85);

        var (thinTri, thinStdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, wall_mm: 1.0);
        var (thickTri, thickStdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, wall_mm: 3.0);

        Assert.True(thickTri - thinTri >= 1000,
            $"Expected thick-wall triangle count to exceed thin by ≥ 1000. "
          + $"Thin: {thinTri}, Thick: {thickTri}, Δ: {thickTri - thinTri}.\n"
          + $"Thin stdout:\n{thinStdout}\nThick stdout:\n{thickStdout}");
    }

    private static string LocateExporter()
    {
        string exporterPath = SubprocessRunner.LocateUnderRepo(
            "Voxelforge.Airbreathing.Voxels/bin/Debug/net9.0-windows/Voxelforge.Airbreathing.StlExporter.exe");
        if (!File.Exists(exporterPath))
        {
            exporterPath = SubprocessRunner.LocateUnderRepo(
                "Voxelforge.Airbreathing.Voxels/bin/Release/net9.0-windows/Voxelforge.Airbreathing.StlExporter.exe");
        }
        return exporterPath;
    }

    private static (long Triangles, string Stdout, string Stderr) RunBuild(
        string exePath,
        AirbreathingEngineDesign design,
        double voxel_mm,
        double? wall_mm = null,
        string? lpbf = null)
    {
        string designJson = JsonSerializer.Serialize(design, JsonOpts);
        string designPath = Path.Combine(Path.GetTempPath(),
            $"ramjet-test-{System.Guid.NewGuid():N}.json");
        string stlPath = Path.Combine(Path.GetTempPath(),
            $"ramjet-test-{System.Guid.NewGuid():N}.stl");
        File.WriteAllText(designPath, designJson);

        try
        {
            var args = new System.Collections.Generic.List<string>
            {
                "--design", designPath,
                "--voxel",  voxel_mm.ToString("F3", CultureInfo.InvariantCulture),
                "--out",    stlPath,
            };
            if (wall_mm is { } w)
                args.AddRange(new[] { "--wall", w.ToString("F3", CultureInfo.InvariantCulture) });
            if (lpbf is not null)
                args.AddRange(new[] { "--lpbf", lpbf });

            var result = SubprocessRunner.Run(exePath, args, timeoutMs: 90_000);
            Assert.True(result.ExeExists, "exporter exe missing");
            Assert.False(result.WaitTimedOut, $"exporter timed out\nstderr:\n{result.Stderr}");
            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(stlPath), $"STL not written\nstderr:\n{result.Stderr}");

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
            int idx = line.IndexOf("BENCH triangle_count=", System.StringComparison.Ordinal);
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
