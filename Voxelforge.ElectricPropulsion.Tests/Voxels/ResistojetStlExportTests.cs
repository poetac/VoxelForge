// ResistojetStlExportTests — voxel-build round-trip for the
// resistojet PicoGK pipeline.
//
// Mirrors RamjetVoxelBuilderSubprocessTests on the airbreathing side.
// Pitfall #8 (xUnit + PicoGK Library disposal) is RESOLVED under PicoGK
// 2.0.0 (PR #374, 2026-05-04), but Voxelforge.ElectricPropulsion.Tests
// targets `net9.0` (not net9.0-windows) to preserve cross-platform
// execution — the StlExporter exe is the safe bridge to the windows-
// only PicoGK voxel pipeline.
//
// Marked [Trait("Category", "Subprocess")]: each test runs ~5-25 s
// (one voxel build at 0.10 mm voxel for a ~25 mm chamber + nozzle).

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelforge.ElectricPropulsion.Tests.Helpers;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Voxels;

[Trait("Category", "Subprocess")]
public class ResistojetStlExportTests
{
    [Fact]
    public void Build_OnMr501bClassDesign_ProducesNonEmptyStl()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;  // clean skip when exe not yet built

        var design = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:         100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:  6.0);

        var (triangles, stdout, stderr) = RunBuild(exePath, design, voxel_mm: 0.10);

        Assert.True(triangles > 1000,
            $"Expected > 1000 triangles, got {triangles}.\nStdout:\n{stdout}\nStderr:\n{stderr}");
        Assert.Contains("BENCH triangle_count=", stdout);
        Assert.Contains("BENCH area_ratio=", stdout);
    }

    [Fact]
    public void Build_HighAreaRatio_ProducesLongerNozzle()
    {
        // ε=150 should produce a longer bounding-box than ε=50 (more
        // diverging-cone length to reach larger exit radius).
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var lo = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.Resistojet,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:         50.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:  6.0);
        var hi = lo with { NozzleAreaRatio = 150.0 };

        var (triLo, _, _) = RunBuild(exePath, lo, voxel_mm: 0.10);
        var (triHi, _, _) = RunBuild(exePath, hi, voxel_mm: 0.10);

        Assert.True(triLo > 0 && triHi > 0);
        // Higher ε → larger surface area → more triangles.
        Assert.True(triHi > triLo,
            $"ε=150 should produce more triangles than ε=50; got hi={triHi}, lo={triLo}.");
    }

    [Fact]
    public void Build_RejectsNonResistojetKind()
    {
        string exePath = LocateExporter();
        var probe = SubprocessRunner.ProbeExe(exePath);
        if (!probe.ExeExists) return;

        var bad = new ElectricPropulsionEngineDesign(
            Kind:                    ElectricPropulsionEngineKind.HallEffect,
            HeaterPower_W:           870.0,
            PropellantMassFlow_kgs:  1.2e-4,
            NozzleThroatRadius_mm:   0.20,
            NozzleAreaRatio:         100.0,
            HeaterChamberLength_mm:  25.0,
            HeaterChamberRadius_mm:  6.0);

        string designJsonPath = Path.GetTempFileName();
        File.WriteAllText(designJsonPath, JsonSerializer.Serialize(bad, JsonOpts));
        string outPath = Path.GetTempFileName();

        try
        {
            var result = SubprocessRunner.Run(exePath, new[]
            {
                "--design", designJsonPath,
                "--voxel",  "0.10",
                "--out",    outPath,
            });
            Assert.True(result.ExeExists);
            // Rejects with exit code 2 (malformed JSON / unsupported kind).
            Assert.Equal(2, result.ExitCode);
            Assert.Contains("Resistojet", result.Stderr);
        }
        finally
        {
            if (File.Exists(designJsonPath)) File.Delete(designJsonPath);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    // ---- helpers -------------------------------------------------------

    private static string LocateExporter()
    {
#if DEBUG
        const string cfg = "Debug";
#else
        const string cfg = "Release";
#endif
        return SubprocessRunner.LocateUnderRepo(
            $"Voxelforge.ElectricPropulsion.Voxels/bin/{cfg}/net9.0-windows/Voxelforge.ElectricPropulsion.StlExporter.exe");
    }

    private static (int triangles, string stdout, string stderr) RunBuild(
        string exePath, ElectricPropulsionEngineDesign design, double voxel_mm)
    {
        string designJsonPath = Path.GetTempFileName();
        File.WriteAllText(designJsonPath, JsonSerializer.Serialize(design, JsonOpts));
        string outPath = Path.ChangeExtension(Path.GetTempFileName(), ".stl");

        try
        {
            var res = SubprocessRunner.Run(exePath, new[]
            {
                "--design", designJsonPath,
                "--voxel",  voxel_mm.ToString("F3", CultureInfo.InvariantCulture),
                "--out",    outPath,
            });

            if (!res.Succeeded)
            {
                return (0, res.Stdout, res.Stderr);
            }

            int triangles = 0;
            foreach (var line in res.Stdout.Split('\n'))
            {
                const string prefix = "BENCH triangle_count=";
                int idx = line.IndexOf(prefix, System.StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int.TryParse(line[(idx + prefix.Length)..].Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out triangles);
                    break;
                }
            }
            return (triangles, res.Stdout, res.Stderr);
        }
        finally
        {
            if (File.Exists(designJsonPath)) File.Delete(designJsonPath);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}
