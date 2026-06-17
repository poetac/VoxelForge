// PulsejetVoxelBuilderSubprocessTests — voxel-build round-trip for the
// Wave 1 PR-6 sub-step 1a.5 pulsejet PicoGK pipeline.
//
// Uses the same subprocess pattern as RamjetVoxelBuilderSubprocessTests
// because Voxelforge.Airbreathing.Tests deliberately targets net9.0 (not
// net9.0-windows) and does not reference Voxelforge.Airbreathing.Voxels —
// preserving the cross-platform-test architectural property.
//
// Marked [Trait("Category", "Subprocess")] — the test runs in the seconds
// range per case (one voxel build at 2 mm voxel on a small reference
// pulsejet). Default xUnit runs include them; a fast unit-only run can
// filter via `dotnet test --filter "Category!=Subprocess"`.

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelforge.Airbreathing.Tests.Helpers;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

[Trait("Category", "Subprocess")]
public class PulsejetVoxelBuilderSubprocessTests
{
    /// <summary>
    /// Healthy pulsejet — small reference geometry. STL builds cleanly,
    /// LPBF analysis runs, no overhang violation expected on a smoothly-
    /// tapered exit cone.
    /// </summary>
    [Fact]
    public void Build_ProducesPrintableStl_NoOverhangGate()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;   // clean skip when exe not yet built

        // Small reference geometry (V-1 scaled down by ~10× for fast voxel builds).
        var design = new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:  0.0008,
            CombustorArea_m2:    0.0020,
            CombustorLength_m:   0.080,
            NozzleThroatArea_m2: 0.0008,
            NozzleExitArea_m2:   0.0010,
            EquivalenceRatio:    0.95)
        {
            PulsejetTubeLength_m    = 0.34,
            PulsejetIntakeArea_m2   = 0.0008,
            PulsejetTailpipeArea_m2 = 0.0010,
        };

        var (triangles, stdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, lpbf: "inconel625");

        Assert.True(triangles > 1000,
            $"Expected > 1000 triangles, got {triangles}. Stdout:\n{stdout}");
        // The pulsejet geometry tapers exit gently (R_exit ≈ 1.12 × R_combustor,
        // slope ≈ 0.04 across the long tailpipe → β ≈ 88° well above IN625's 40° floor),
        // so OVERHANG_ANGLE_EXCEEDED should not fire.
        Assert.DoesNotContain("GATE OVERHANG_ANGLE_EXCEEDED", stdout);
    }

    /// <summary>
    /// Wall thickness flows through the pipeline — same design, two
    /// thicknesses; thicker shell must produce a higher triangle count.
    /// </summary>
    [Fact]
    public void Build_WallThickness_AffectsTriangleCount()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var design = new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:  0.0008,
            CombustorArea_m2:    0.0020,
            CombustorLength_m:   0.080,
            NozzleThroatArea_m2: 0.0008,
            NozzleExitArea_m2:   0.0010,
            EquivalenceRatio:    0.95)
        {
            PulsejetTubeLength_m    = 0.34,
            PulsejetIntakeArea_m2   = 0.0008,
            PulsejetTailpipeArea_m2 = 0.0010,
        };

        var (thinTri, thinStdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, wall_mm: 1.0);
        var (thickTri, thickStdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, wall_mm: 3.0);

        Assert.True(thickTri - thinTri >= 500,
            $"Expected thick-wall triangle count to exceed thin by ≥ 500. "
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
            $"pulsejet-test-{System.Guid.NewGuid():N}.json");
        string stlPath = Path.Combine(Path.GetTempPath(),
            $"pulsejet-test-{System.Guid.NewGuid():N}.stl");
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
