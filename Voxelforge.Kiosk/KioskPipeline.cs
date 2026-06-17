// KioskPipeline.cs — preset → perturb → FDM-floors → generate → cutaway,
// then optionally export STL + production render.
//
// Split into two phases:
//   • BuildPreview — produces a Voxels handle + design metadata. Cheap
//     to call repeatedly; the visitor "tries another" until they see a
//     design they like. Pushes the voxels to PicoGK's GLFW viewer for
//     real-time preview.
//   • Commit — takes a previously-built preview, writes STL into the
//     watch folder, and (if Blender is available) fires voxelforge-
//     render to produce a production-quality PNG alongside.
//
// The pipeline runs on the kiosk task thread (PicoGK voxel ops are
// thread-bound). It bypasses the SA optimizer entirely.
//
// Why kiosk-specific specs instead of CanonicalDesigns:
//   The bench-SA canonicals (merlin / aerospike / pintle) are 10-20 kN
//   engines tuned for SA-fingerprint stability. At 0.5 mm voxel they
//   take 3-4 minutes to voxelise and produce 600+ MB STLs — unworkable
//   for a trade-show kiosk. Kiosk specs target 1-2 kN: ~2-5 s build,
//   ~20-50 MB STL, ~25-35 mm OD, prints in ~30-45 min on PLA.

using System.Diagnostics;
using System.IO;
using PicoGK;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Kiosk.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.Kiosk;

/// <summary>
/// Result of one kiosk preview build. Held in task-thread state between
/// "Try Another" presses; consumed by <see cref="KioskPipeline.Commit"/>.
/// </summary>
public sealed record KioskPreview(
    string        PresetName,
    int           SequenceNumber,
    Voxels        Voxels,
    string        Description,
    double        BoundingLength_mm,
    double        BoundingDiameter_mm);

/// <summary>
/// Result of a successful commit: STL was written. The PNG render is
/// reported separately via the OnRenderComplete event because Blender
/// can take 20-60 s — the visitor doesn't wait for it.
/// </summary>
public sealed record KioskCommitResult(
    string PresetName,
    int    SequenceNumber,
    string StlPath,
    int    TriangleCount,
    long   StlBytes,
    string Description,
    bool   RenderPending);

public static class KioskPipeline
{
    /// <summary>
    /// Kiosk presets — small-thrust LOX/CH4 engines deliberately sized
    /// for FDM-print times (≤ 60 min) and Bambu X1C bed (256³ mm).
    /// </summary>
    public static readonly string[] FdmCanonicals = { "bell", "aerospike", "pintle" };

    public const double VoxelSize_mm = 0.5;

    private const double BambuMaxSide_mm = 240.0;

    /// <summary>
    /// Build the chamber + cutaway and return the live Voxels handle
    /// without writing STL or running render. The caller (kiosk task
    /// thread) is responsible for keeping the result alive between
    /// "Try Another" presses and disposing it when superseded.
    /// </summary>
    public static KioskPreview BuildPreview(
        string presetName, int sequenceNumber,
        bool   perturb       = true,
        double voxelSize_mm  = VoxelSize_mm)
    {
        var (spec, topologyOverride) = KioskSpec(presetName);
        var seed = AutoSeeder.Seed(spec);
        var baseline = seed.Design with
        {
            ChannelTopology   = topologyOverride ?? seed.Design.ChannelTopology,
            // Drop manifold + port plumbing for visual clarity on the
            // print. Injector + mounting flanges stay because they
            // make the silhouette read as an engine.
            IncludeManifolds  = false,
            IncludePorts      = false,
        };
        var design = perturb ? PerturbDesign(baseline, sequenceNumber) : baseline;
        design = ApplyFdmFloors(design);

        var result = RegenChamberOptimization.GenerateWith(
            seed.Conditions, design,
            voxelSize_mm:    voxelSize_mm,
            skipMfgAnalysis: true,
            voxelGenerator:  new Voxelforge.Geometry.ChamberVoxelBuilderAdapter());

        double L  = result.Geometry.BoundingLength_mm;
        double OD = result.Geometry.BoundingDiameter_mm;

        var vox = result.Geometry.Voxels.AsPicoGK();
        HalfSectionCutaway.ApplyAxialHalfSection(
            vox,
            boundingLength_mm:   L,
            boundingDiameter_mm: OD);

        return new KioskPreview(
            PresetName:          presetName,
            SequenceNumber:      sequenceNumber,
            Voxels:              vox,
            Description:         result.Geometry.Description,
            BoundingLength_mm:   L,
            BoundingDiameter_mm: OD);
    }

    /// <summary>
    /// Commit a preview: write STL into <paramref name="watchFolder"/>,
    /// then asynchronously fire voxelforge-render to produce a PNG
    /// alongside (if Blender is available). The STL write is
    /// synchronous; the render fires in the background and reports
    /// completion via <paramref name="onRenderComplete"/>.
    /// </summary>
    public static KioskCommitResult Commit(
        KioskPreview preview, string watchFolder,
        Action<string>?           onRenderComplete = null,
        Action<string, string>?   onRenderFailed   = null)
    {
        Directory.CreateDirectory(watchFolder);

        if (preview.BoundingLength_mm > BambuMaxSide_mm
            || preview.BoundingDiameter_mm > BambuMaxSide_mm)
        {
            KioskLog.Write(watchFolder,
                $"WARN preset={preview.PresetName} seq={preview.SequenceNumber:D4} envelope " +
                $"L={preview.BoundingLength_mm:F0}mm OD={preview.BoundingDiameter_mm:F0}mm " +
                $"exceeds Bambu X1C {BambuMaxSide_mm:F0}mm. Slicer must scale-to-fit.");
        }

        var stlName = $"voxelforge_kiosk_{preview.SequenceNumber:D4}_{preview.PresetName}.stl";
        var stlPath = Path.Combine(watchFolder, stlName);
        var export  = ChamberVoxelBuilder.ExportStlProfiled(preview.Voxels, stlPath);

        // Fire production render asynchronously so the visitor doesn't
        // wait the ~30 s Blender takes.
        var renderPath = Path.ChangeExtension(stlPath, ".png");
        bool renderPending = TryFireRender(stlPath, renderPath, onRenderComplete, onRenderFailed);

        return new KioskCommitResult(
            PresetName:     preview.PresetName,
            SequenceNumber: preview.SequenceNumber,
            StlPath:        stlPath,
            TriangleCount:  export.TriangleCount,
            StlBytes:       export.StlBytes,
            Description:    $"L={preview.BoundingLength_mm:F0}mm, OD={preview.BoundingDiameter_mm:F0}mm",
            RenderPending:  renderPending);
    }

    /// <summary>
    /// Returns true on render fire-and-forget OK; false if voxelforge-
    /// render isn't on disk or Blender isn't installed (in which case
    /// the kiosk silently skips the PNG step — STL alone is enough).
    /// </summary>
    private static bool TryFireRender(
        string stlPath, string renderPath,
        Action<string>?         onComplete,
        Action<string, string>? onFailed)
    {
        var exe = LocateRenderExe();
        if (exe is null) return false;

        // Async fire — task thread doesn't wait. The render runs on a
        // threadpool worker; both stdout/stderr are captured in case
        // we need to surface the error.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName              = exe,
                    UseShellExecute       = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                psi.ArgumentList.Add("--in");        psi.ArgumentList.Add(stlPath);
                psi.ArgumentList.Add("--out");       psi.ArgumentList.Add(renderPath);
                psi.ArgumentList.Add("--mode");      psi.ArgumentList.Add("still");
                psi.ArgumentList.Add("--material");  psi.ArgumentList.Add("copper");
                psi.ArgumentList.Add("--resolution"); psi.ArgumentList.Add("high");

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Process.Start returned null for voxelforge-render");
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0 && File.Exists(renderPath))
                {
                    onComplete?.Invoke(renderPath);
                }
                else
                {
                    onFailed?.Invoke(renderPath,
                        $"voxelforge-render exit {proc.ExitCode}: " +
                        $"{(stderr.Length > 400 ? stderr[..400] + "…" : stderr)}");
                }
            }
            catch (Exception ex)
            {
                onFailed?.Invoke(renderPath, $"{ex.GetType().Name}: {ex.Message}");
            }
        });
        return true;
    }

    /// <summary>
    /// Find voxelforge-render.exe. The Voxelforge.Renderer csproj's
    /// OutDir is `..\Voxelforge\bin\<config>\net9.0-windows\` (relative
    /// to the renderer project), which resolves to the
    /// AssemblyName-mapped folder <c>&lt;repo&gt;/Voxelforge/bin/...</c>
    /// — NOT <c>Voxelforge/bin/...</c> despite the source
    /// folder being named that way (folder + assembly-name asymmetry
    /// from Sprint 0 PR-2). The kiosk's BaseDirectory is at
    /// <c>&lt;repo&gt;/Voxelforge.Kiosk/bin/&lt;config&gt;/net9.0-windows/</c>;
    /// going up 4 levels lands us at the repo root, then we descend
    /// into <c>Voxelforge/bin/&lt;config&gt;/net9.0-windows/</c>.
    /// </summary>
    private static string? LocateRenderExe()
    {
        var baseDir = AppContext.BaseDirectory;
        // Determine our config (Debug vs Release) from BaseDirectory's
        // grandparent: …/bin/<config>/net9.0-windows/
        var config = Path.GetFileName(
            Path.GetDirectoryName(Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar))));

        var probes = new[]
        {
            // Same folder (if user staged the renderer alongside).
            Path.Combine(baseDir, "voxelforge-render.exe"),
            // Sibling under the AssemblyName-mapped Voxelforge bin.
            Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..",
                "Voxelforge", "bin", config ?? "Release", "net9.0-windows",
                "voxelforge-render.exe")),
        };
        foreach (var p in probes)
            if (File.Exists(p)) return p;
        return null;
    }

    private static (EngineSpec spec, ChannelTopology? topology) KioskSpec(string name) =>
        name?.ToLowerInvariant() switch
        {
            "bell" => (new EngineSpec(
                PropellantPair:      PropellantPair.LOX_CH4,
                Thrust_N:            1_500.0,
                ChamberPressure_Pa:  3e6,
                ExpansionRatio:      6.0,
                EngineCycleOverride: EngineCycle.GasGenerator),
                null),

            "aerospike" => (new EngineSpec(
                PropellantPair:           PropellantPair.LOX_CH4,
                Thrust_N:                 1_500.0,
                ChamberPressure_Pa:       4e6,
                ExpansionRatio:           8.0,
                ChannelTopologyOverride:  ChannelTopology.Aerospike),
                ChannelTopology.Aerospike),

            "pintle" => (new EngineSpec(
                PropellantPair:        PropellantPair.LOX_CH4,
                Thrust_N:              1_000.0,
                ChamberPressure_Pa:    4e6,
                ExpansionRatio:        6.0,
                ElementTypeOverride:   "Pintle"),
                null),

            _ => throw new ArgumentException(
                $"Unknown kiosk preset '{name}'. Valid: {string.Join(", ", FdmCanonicals)}."),
        };

    private static RegenChamberDesign PerturbDesign(RegenChamberDesign baseline, int sequenceNumber)
    {
        var rng = new Random(sequenceNumber);
        double Wiggle(double v, double pct, double lo, double hi)
            => Math.Clamp(v * (1.0 + (rng.NextDouble() - 0.5) * 2.0 * pct), lo, hi);

        int channelCountWiggled = (int)Math.Round(
            baseline.ChannelCount * (1.0 + (rng.NextDouble() - 0.5) * 0.20));

        return baseline with
        {
            ContractionRatio        = Wiggle(baseline.ContractionRatio,       0.10, 3.0, 7.0),
            ExpansionRatio          = Wiggle(baseline.ExpansionRatio,         0.10, 4.0, 10.0),
            CharacteristicLength_m  = Wiggle(baseline.CharacteristicLength_m, 0.08, 0.7, 1.3),
            BellEntranceAngle_deg   = Wiggle(baseline.BellEntranceAngle_deg,  0.08, 20.0, 32.0),
            BellExitAngle_deg       = Wiggle(baseline.BellExitAngle_deg,      0.08,  6.0, 16.0),
            ChannelCount            = Math.Clamp(channelCountWiggled, 18, 42),
        };
    }

    private static RegenChamberDesign ApplyFdmFloors(RegenChamberDesign d) => d with
    {
        RibThickness_mm         = Math.Max(d.RibThickness_mm,         1.5),
        GasSideWallThickness_mm = Math.Max(d.GasSideWallThickness_mm, 1.2),
        OuterJacketThickness_mm = Math.Max(d.OuterJacketThickness_mm, 1.5),
        ChannelHeightChamber_mm = Math.Max(d.ChannelHeightChamber_mm, 2.0),
        ChannelHeightThroat_mm  = Math.Max(d.ChannelHeightThroat_mm,  1.5),
        ChannelHeightExit_mm    = Math.Max(d.ChannelHeightExit_mm,    2.0),
        ChannelCount            = Math.Min(d.ChannelCount,            42),
    };

    // ───────────────────────────────────────────────────────────────
    //  Backwards-compatible single-shot path used by:
    //    • the headless --headless CLI mode (Voxelforge.Tests subprocess)
    //    • any caller that wants build + commit in one step (no preview
    //      iteration)
    // ───────────────────────────────────────────────────────────────

    public sealed record KioskResult(
        string PresetName,
        int    SequenceNumber,
        string StlPath,
        int    TriangleCount,
        long   StlBytes,
        string Description);

    public static KioskResult Generate(
        string presetName, int sequenceNumber, string watchFolder,
        bool   perturb       = true,
        double voxelSize_mm  = VoxelSize_mm)
    {
        var preview = BuildPreview(presetName, sequenceNumber, perturb, voxelSize_mm);
        var commit  = Commit(preview, watchFolder);
        return new KioskResult(
            PresetName:     commit.PresetName,
            SequenceNumber: commit.SequenceNumber,
            StlPath:        commit.StlPath,
            TriangleCount:  commit.TriangleCount,
            StlBytes:       commit.StlBytes,
            Description:    commit.Description);
    }
}
