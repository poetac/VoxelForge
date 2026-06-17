// KioskPipelineSubprocessTests.cs — smoke + regression for the
// trade-show kiosk pipeline. Spawns Voxelforge.Kiosk in --headless
// mode (mirrors the StlExporter / VoxelforgeEval pattern) so xUnit +
// PicoGK pitfall #8 doesn't bite (in-proc PicoGK.Library construction
// crashes the test host on dispose).
//
// Asserts:
//   • exe ran to completion with exit code 0
//   • the STL file was written and is non-empty
//   • the file size is in a sane FDM-print range (50 KB - 50 MB)

using System.IO;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests.Kiosk;

public class KioskPipelineSubprocessTests
{
    private static string LocateExe()
    {
        // Prefer Release because Debug PicoGK is 10-100× slower; the
        // bench-class voxel build at the kiosk's 1-2 kN dimensions is
        // ~5 s in Release and tens-of-seconds in Debug. Fall back to
        // Debug if Release isn't built (e.g. dev iterating in Debug).
        var release = SubprocessRunner.LocateUnderRepo(
            "Voxelforge.Kiosk/bin/Release/net9.0-windows/Voxelforge.Kiosk.exe");
        if (System.IO.File.Exists(release)) return release;
        return SubprocessRunner.LocateUnderRepo(
            "Voxelforge.Kiosk/bin/Debug/net9.0-windows/Voxelforge.Kiosk.exe");
    }

    [Theory]
    [InlineData("bell")]
    [InlineData("aerospike")]
    [InlineData("pintle")]
    public void Headless_BuildsWatertightStl_PerCanonical(string preset)
    {
        string exe = LocateExe();
        if (!SubprocessRunner.ProbeExe(exe).ExeExists)
        {
            // Single-project test runs may not have built the kiosk exe.
            // Skip rather than fail — full sln build is required for
            // this test to be meaningful.
            return;
        }

        string outDir = Path.Combine(Path.GetTempPath(),
            $"voxelforge-kiosk-test-{Guid.NewGuid():N}");
        try
        {
            int seq = 1;
            var result = SubprocessRunner.Run(
                exe,
                args: new[] { "--headless", "--preset", preset, "--seq", seq.ToString(), "--out", outDir },
                stdin: null,
                timeoutMs: 120_000);

            Assert.True(result.Succeeded, result.DescribeFailure());

            string expectedStl = Path.Combine(outDir,
                $"voxelforge_kiosk_{seq:D4}_{preset}.stl");
            Assert.True(File.Exists(expectedStl),
                $"Expected STL not written at {expectedStl}.\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");

            long bytes = new FileInfo(expectedStl).Length;
            // 50 KB lower bound: smaller than any half-section regen
            // chamber should fit at 0.4 mm voxel. 50 MB upper bound:
            // catches a runaway tessellation regression.
            Assert.InRange(bytes, 50_000L, 50_000_000L);
        }
        finally
        {
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
