// TurbofanVoxelBuilderSubprocessTests — voxel-build round-trip for the
// Wave-2 follow-on turbofan PicoGK pipeline (issue #441 follow-on slice).
//
// Same subprocess pattern + cross-platform-test rationale as
// RamjetVoxelBuilderSubprocessTests — Voxelforge.Airbreathing.Tests
// targets net9.0 (not net9.0-windows) and does not reference the Voxels
// project, so PicoGK lives in the StlExporter exe.

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelforge.Airbreathing.Tests.Helpers;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

[Trait("Category", "Subprocess")]
public class TurbofanVoxelBuilderSubprocessTests
{
    /// <summary>
    /// F404-class turbofan (BPR=0.34) builds at 1 mm voxel and emits a
    /// non-trivial mesh.
    /// </summary>
    [Fact]
    public void Build_F404Class_ProducesNonTrivialMesh()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;   // clean skip when exe not yet built

        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.0030,
            CombustorArea_m2:        0.0050,
            CombustorLength_m:       0.040,
            NozzleThroatArea_m2:     0.0020,
            NozzleExitArea_m2:       0.0030,
            EquivalenceRatio:        0.85,
            CompressorPressureRatio: 8.0,
            BypassRatio:             0.34);
        var (triangles, stdout, _) = RunBuild(exePath, design, voxel_mm: 1.0);

        Assert.True(triangles > 1000,
            $"Expected > 1000 triangles, got {triangles}. Stdout:\n{stdout}");
        // The combined-shell description must mention the BPR.
        Assert.Contains("BPR=", stdout);
    }

    /// <summary>
    /// Bypass-duct wall thickness flows through the pipeline: thicker
    /// bypass shell ⇒ more triangles than thinner.
    /// </summary>
    [Fact]
    public void Build_BypassDuctWallThickness_AffectsTriangleCount()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var thinDesign = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.0030,
            CombustorArea_m2:        0.0050,
            CombustorLength_m:       0.040,
            NozzleThroatArea_m2:     0.0020,
            NozzleExitArea_m2:       0.0030,
            EquivalenceRatio:        0.85,
            CompressorPressureRatio: 8.0,
            BypassRatio:             0.34)
        { BypassDuctWallThickness_mm = 1.5 };

        var thickDesign = thinDesign with { BypassDuctWallThickness_mm = 4.0 };

        var (thinTri,  thinStdout,  _) = RunBuild(exePath, thinDesign,  voxel_mm: 1.0);
        var (thickTri, thickStdout, _) = RunBuild(exePath, thickDesign, voxel_mm: 1.0);

        Assert.True(thickTri > thinTri,
            $"Expected thick bypass-duct triangle count to exceed thin. "
          + $"Thin: {thinTri}, Thick: {thickTri}.\n"
          + $"Thin stdout:\n{thinStdout}\nThick stdout:\n{thickStdout}");
    }

    /// <summary>
    /// LPBF analysis runs end-to-end on the turbofan two-shell geometry.
    /// A gentle-divergent design (R_exit ≈ R_combustor) should produce
    /// zero overhang violations.
    /// </summary>
    [Fact]
    public void Build_LpbfAnalysis_GentleDivergent_NoOverhangGate()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        // R_exit ≈ R_combustor means the divergent slope is ~0 — well
        // inside any LPBF overhang floor (40° for IN625).
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.0030,
            CombustorArea_m2:        0.0050,
            CombustorLength_m:       0.040,
            NozzleThroatArea_m2:     0.0020,
            NozzleExitArea_m2:       0.0050,    // ≈ combustor area → gentle divergent
            EquivalenceRatio:        0.85,
            CompressorPressureRatio: 8.0,
            BypassRatio:             0.34);
        var (_, stdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, lpbf: "inconel625");

        Assert.DoesNotContain("GATE OVERHANG_ANGLE_EXCEEDED", stdout);
        Assert.Contains("BENCH lpbf_violation_count=", stdout);
    }

    /// <summary>
    /// Steep core divergent ⇒ LPBF overhang gate fires. Verifies the
    /// turbofan-aware surface sampler emits patches that reach the
    /// printability analysis.
    /// </summary>
    [Fact]
    public void Build_LpbfAnalysis_SteepDivergent_FiresOverhangGate()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        // R_exit ≈ 1.6× R_throat with a short divergent length ⇒ slope
        // > tan(50°) at the IN625 40° overhang floor.
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.0030,
            CombustorArea_m2:        0.0050,
            CombustorLength_m:       0.040,
            NozzleThroatArea_m2:     0.0010,
            NozzleExitArea_m2:       0.0080,    // 8× throat ⇒ steep slope
            EquivalenceRatio:        0.85,
            CompressorPressureRatio: 8.0,
            BypassRatio:             0.34);
        var (_, stdout, _) = RunBuild(exePath, design, voxel_mm: 1.0, lpbf: "inconel625");

        Assert.Contains("GATE OVERHANG_ANGLE_EXCEEDED", stdout);
    }

    /// <summary>
    /// Higher BPR ⇒ larger overall bounding diameter (bypass duct grows
    /// outward), which in turn ⇒ more triangles than a lower-BPR design
    /// at the same core areas.
    /// </summary>
    [Fact]
    public void Build_HigherBpr_LargerOuterDiameter()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var lowBpr = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:      0.0030,
            CombustorArea_m2:        0.0050,
            CombustorLength_m:       0.040,
            NozzleThroatArea_m2:     0.0020,
            NozzleExitArea_m2:       0.0030,
            EquivalenceRatio:        0.85,
            CompressorPressureRatio: 8.0,
            BypassRatio:             0.20);
        var highBpr = lowBpr with { BypassRatio = 1.50 };

        var (_, lowStdout,  _) = RunBuild(exePath, lowBpr,  voxel_mm: 1.0);
        var (_, highStdout, _) = RunBuild(exePath, highBpr, voxel_mm: 1.0);

        double lowDiameter  = ParseBenchDouble(lowStdout,  "bounding_diameter_mm");
        double highDiameter = ParseBenchDouble(highStdout, "bounding_diameter_mm");

        Assert.True(highDiameter > lowDiameter,
            $"Expected higher-BPR turbofan to have larger bounding diameter. "
          + $"BPR=0.20: {lowDiameter:F1} mm, BPR=1.50: {highDiameter:F1} mm.");
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
        string? lpbf = null)
    {
        string designJson = JsonSerializer.Serialize(design, JsonOpts);
        string designPath = Path.Combine(Path.GetTempPath(),
            $"turbofan-test-{System.Guid.NewGuid():N}.json");
        string stlPath = Path.Combine(Path.GetTempPath(),
            $"turbofan-test-{System.Guid.NewGuid():N}.stl");
        File.WriteAllText(designPath, designJson);

        try
        {
            var args = new System.Collections.Generic.List<string>
            {
                "--design", designPath,
                "--voxel",  voxel_mm.ToString("F3", CultureInfo.InvariantCulture),
                "--out",    stlPath,
            };
            if (lpbf is not null)
                args.AddRange(new[] { "--lpbf", lpbf });

            var result = SubprocessRunner.Run(exePath, args, timeoutMs: 120_000);
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

    private static double ParseBenchDouble(string stdout, string key)
    {
        string token = "BENCH " + key + "=";
        foreach (var line in stdout.Split('\n'))
        {
            int idx = line.IndexOf(token, System.StringComparison.Ordinal);
            if (idx < 0) continue;
            string tail = line[(idx + token.Length)..].Trim();
            if (double.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
        }
        return double.NaN;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}
