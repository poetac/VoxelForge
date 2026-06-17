// SaAnimationCaptureTests — OA-1 (#287) orchestrator coverage.
//
// Sprint B-1 (2026-04-30). Pure-orchestrator tests that exercise the
// (Capture → Compose) seam without spinning Blender. The stub renderer
// writes a tiny deterministic solid-colour PNG per frame using
// Magick.NET; the resulting GIF is read back via Magick.NET and
// validated for frame count + per-frame delay.
//
// Why this is split out:
//   • Real subprocess invocation needs Voxelforge.StlExporter +
//     voxelforge-render + Blender on disk → not portable to CI / CI-
//     less runs (cf. CLAUDE.md pitfall #8 — xUnit + PicoGK).
//   • The orchestrator's logic (best-improvement gating, lock
//     serialisation under multi-chain, GIF compose, hold-last-frame
//     extra delay) is independent of whether the per-frame PNG came
//     from Blender or from a stub. Mocking the renderer is the right
//     abstraction.
//
// Tests cover:
//   1. OfferFrame: only finite improvements pass the gate.
//   2. OfferFrame: thread-safe under concurrent callers (multi-chain).
//   3. Compose: empty capture writes no GIF.
//   4. Compose: produces a valid animated GIF whose frame count matches
//      the captured snapshots.
//   5. Compose: final frame's animation delay is bumped by hold-last-ms.
//   6. WriteFrameDesignJson round-trips via DesignPersistence.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;
using Voxelforge.Benchmarks;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class SaAnimationCaptureTests
{
    /// <summary>
    /// Test stub. Writes a 32×32 solid-colour PNG whose RGB is derived
    /// from the iteration so the post-compose check can verify the
    /// frames landed in capture order. Returns the PNG path.
    /// </summary>
    private sealed class StubRenderer : ISaFrameRenderer
    {
        public int Calls { get; private set; }
        public bool FailEverything { get; init; }
        public bool FailEveryOther  { get; init; }

        public string? RenderFrame(SaFrameSnapshot frame, string frameDir)
        {
            Calls++;
            if (FailEverything) return null;
            if (FailEveryOther && (Calls % 2 == 0)) return null;

            // Iteration → 24-bit colour. Lower 8 bits in B, etc. Lets
            // us verify ordering without parsing GIF metadata.
            byte r = (byte)((frame.Iteration >> 16) & 0xFF);
            byte g = (byte)((frame.Iteration >>  8) & 0xFF);
            byte b = (byte)( frame.Iteration        & 0xFF);

            string pngPath = Path.Combine(frameDir, $"stub-iter-{frame.Iteration}.png");
            using var img = new MagickImage(
                new MagickColor(r, g, b),
                width:  32,
                height: 32);
            img.Format = MagickFormat.Png;
            img.Write(pngPath);
            return pngPath;
        }
    }

    [Fact]
    public void OfferFrame_NonImprovingScores_AreRejected()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using var cap = new SaAnimationCapture(new StubRenderer(), temp.Path);

        var (cond, design) = SmallCanonicalPair();

        Assert.True (cap.OfferFrame(iteration: 10, score: 100.0, cond, design));
        Assert.False(cap.OfferFrame(iteration: 11, score: 100.0, cond, design));   // not strictly better
        Assert.False(cap.OfferFrame(iteration: 12, score: 150.0, cond, design));   // worse
        Assert.True (cap.OfferFrame(iteration: 13, score:  90.0, cond, design));
        Assert.True (cap.OfferFrame(iteration: 14, score:  10.0, cond, design));
        Assert.False(cap.OfferFrame(iteration: 15, score:  10.0, cond, design));

        Assert.Equal(new[] { 10, 13, 14 }, cap.CapturedIterations);
    }

    [Fact]
    public void OfferFrame_NonFiniteScores_AreRejected()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using var cap = new SaAnimationCapture(new StubRenderer(), temp.Path);

        var (cond, design) = SmallCanonicalPair();

        Assert.False(cap.OfferFrame(1, double.PositiveInfinity, cond, design));
        Assert.False(cap.OfferFrame(2, double.NaN,              cond, design));
        Assert.False(cap.OfferFrame(3, double.NegativeInfinity, cond, design));   // not finite either
        Assert.True (cap.OfferFrame(4, 0.0,                     cond, design));
        Assert.True (cap.OfferFrame(5, -1.0,                    cond, design));

        Assert.Equal(new[] { 4, 5 }, cap.CapturedIterations);
    }

    [Fact]
    public async Task OfferFrame_IsThreadSafeAcrossConcurrentChains()
    {
        // Eight workers, each offers descending scores. Total of 8×100 =
        // 800 OfferFrame calls. Without the lock, the captured-list
        // would race — the result iteration count must match the lock-
        // serialised invariant: each captured frame's score is strictly
        // less than every prior captured frame's score.
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using var cap = new SaAnimationCapture(new StubRenderer(), temp.Path);

        var (cond, design) = SmallCanonicalPair();

        var tasks = new Task[8];
        for (int chain = 0; chain < tasks.Length; chain++)
        {
            int chainCopy = chain;
            tasks[chain] = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    // chain-local scores: chainCopy * 100 + (100 - i).
                    // Across all chains, scores span 1..800 with overlap.
                    double score = chainCopy * 100 + (100 - i);
                    cap.OfferFrame(chainCopy * 1000 + i, score, cond, design);
                }
            });
        }
        await Task.WhenAll(tasks);

        // Invariant: captured scores are strictly monotonically decreasing.
        var frames = cap.Frames;
        Assert.NotEmpty(frames);
        for (int i = 1; i < frames.Count; i++)
            Assert.True(frames[i].Score < frames[i - 1].Score,
                $"frame {i} score {frames[i].Score} not strictly less than predecessor {frames[i - 1].Score}");
    }

    [Fact]
    public void Compose_WithZeroFrames_WritesNoGifAndReportsZero()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using var cap = new SaAnimationCapture(new StubRenderer(), temp.Path);

        var result = cap.Compose();

        Assert.Equal(0, result.FramesCaptured);
        Assert.Equal(0, result.FramesRendered);
        Assert.Equal(0, result.GifBytes);
        Assert.False(File.Exists(result.GifPath));
    }

    [Fact]
    public void Compose_ProducesValidGifWithCapturedFrameCount()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        var stub = new StubRenderer();
        using (var cap = new SaAnimationCapture(stub, temp.Path))
        {
            var (cond, design) = SmallCanonicalPair();

            // Six strict improvements.
            double s = 1000.0;
            for (int i = 0; i < 6; i++)
                cap.OfferFrame(iteration: 1 + i, score: s -= 50.0, cond, design);

            var result = cap.Compose(frameDelayMs: 200, holdLastFrameMs: 1000);
            Assert.Equal(6, result.FramesCaptured);
            Assert.Equal(6, result.FramesRendered);
            Assert.True (result.GifBytes > 0);
            Assert.True (File.Exists(result.GifPath));
            Assert.Equal(6, stub.Calls);
        }

        // Validate the GIF round-trips via Magick.NET.
        using var collection = new MagickImageCollection(temp.Path);
        Assert.Equal(6, collection.Count);
    }

    [Fact]
    public void Compose_FinalFrame_GetsHoldLastDelay()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using (var cap = new SaAnimationCapture(new StubRenderer(), temp.Path))
        {
            var (cond, design) = SmallCanonicalPair();
            for (int i = 0; i < 4; i++)
                cap.OfferFrame(iteration: i + 1, score: 100.0 - i * 10, cond, design);

            cap.Compose(frameDelayMs: 200, holdLastFrameMs: 1500);
        }

        // Magick.NET reports AnimationDelay in centiseconds.
        // (200 + 1500) / 10 = 170 cs on the last; 200 / 10 = 20 cs elsewhere.
        using var collection = new MagickImageCollection(temp.Path);
        Assert.Equal(4, collection.Count);
        for (int i = 0; i < collection.Count - 1; i++)
            Assert.Equal(20u, collection[i].AnimationDelay);
        Assert.Equal(170u, collection[^1].AnimationDelay);
    }

    [Fact]
    public void Compose_LoopsForever_AnimationIterationsZero()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using (var cap = new SaAnimationCapture(new StubRenderer(), temp.Path))
        {
            var (cond, design) = SmallCanonicalPair();
            cap.OfferFrame(1, 100.0, cond, design);
            cap.OfferFrame(2,  50.0, cond, design);

            cap.Compose();
        }

        using var collection = new MagickImageCollection(temp.Path);
        Assert.Equal(0u, collection[0].AnimationIterations);
    }

    [Fact]
    public void Compose_PartialRenderFailures_ProducesShorterGifAndReports()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        var stub = new StubRenderer { FailEveryOther = true };
        using var cap = new SaAnimationCapture(stub, temp.Path);

        var (cond, design) = SmallCanonicalPair();
        for (int i = 0; i < 6; i++)
            cap.OfferFrame(iteration: i + 1, score: 1000.0 - i * 100, cond, design);

        var result = cap.Compose();
        Assert.Equal(6, result.FramesCaptured);
        // Stub fails every-other call; expect 3 rendered.
        Assert.Equal(3, result.FramesRendered);
        Assert.True(result.GifBytes > 0);
        using var collection = new MagickImageCollection(temp.Path);
        Assert.Equal(3, collection.Count);
    }

    [Fact]
    public void Compose_AllRenderFailures_WritesNoGif()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using var cap = new SaAnimationCapture(new StubRenderer { FailEverything = true }, temp.Path);

        var (cond, design) = SmallCanonicalPair();
        for (int i = 0; i < 3; i++)
            cap.OfferFrame(iteration: i + 1, score: 100.0 - i * 10, cond, design);

        var result = cap.Compose();
        Assert.Equal(3, result.FramesCaptured);
        Assert.Equal(0, result.FramesRendered);
        Assert.Equal(0, result.GifBytes);
        Assert.False(File.Exists(result.GifPath));
    }

    [Fact]
    public void WriteFrameDesignJson_RoundTripsViaDesignPersistence()
    {
        using var tempDir = new TempDir();
        var (cond, design) = SmallCanonicalPair();
        var snap = new SaFrameSnapshot(
            Iteration:  42,
            Score:      123.4,
            Conditions: cond,
            Design:     design);

        string designPath = SaAnimationCapture.WriteFrameDesignJson(snap, tempDir.Path);
        Assert.True(File.Exists(designPath));

        var loaded = DesignPersistence.Load(designPath);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Conditions);
        Assert.NotNull(loaded.Design);
        // Spot-check a couple of fields to confirm the round-trip is real,
        // not a serializer no-op. Thrust + propellant pair are required
        // and uniquely identify the canonical preset.
        Assert.Equal(cond.Thrust_N, loaded.Conditions!.Thrust_N);
        Assert.Equal(cond.PropellantPair, loaded.Conditions.PropellantPair);
        Assert.Equal(design.ContractionRatio,        loaded.Design!.ContractionRatio);
        Assert.Equal(design.ExpansionRatio,          loaded.Design.ExpansionRatio);
        Assert.Equal(design.CharacteristicLength_m,  loaded.Design.CharacteristicLength_m);
    }

    [Fact]
    public void Capture_AfterDispose_Throws()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        var cap = new SaAnimationCapture(new StubRenderer(), temp.Path);
        var (cond, design) = SmallCanonicalPair();

        cap.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => cap.OfferFrame(1, 1.0, cond, design));
        Assert.Throws<ObjectDisposedException>(() => cap.Compose());
    }

    [Fact]
    public void Compose_RejectsBadDelays()
    {
        using var temp = Helpers.TestTempFile.WithUniqueName("voxelforge-sa-anim", "gif");
        using var cap = new SaAnimationCapture(new StubRenderer(), temp.Path);

        Assert.Throws<ArgumentOutOfRangeException>(() => cap.Compose(frameDelayMs: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => cap.Compose(holdLastFrameMs: -1));
    }

    /// <summary>
    /// Pull a canonical (Conditions, Design) pair from the merlin
    /// preset. We don't need realistic SA states for these orchestrator
    /// tests — we just need a record-shaped pair that round-trips
    /// through <see cref="DesignPersistence"/>.
    /// </summary>
    private static (OperatingConditions cond, RegenChamberDesign design) SmallCanonicalPair()
    {
        var preset = CanonicalDesigns.Get("merlin");
        return (preset.Seed.Conditions, preset.Seed.Design);
    }

    /// <summary>
    /// Tiny RAII temp-directory wrapper. Tests use this for the
    /// per-frame scratch dir; on Dispose the directory is removed
    /// recursively. Modelled on Helpers.TestTempFile.
    /// </summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "voxelforge-sa-anim-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
        }
    }
}
