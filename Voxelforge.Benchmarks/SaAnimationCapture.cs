// SaAnimationCapture — OA-1 (#287) animated-GIF capture for SA progress.
//
// Sprint B-1 (2026-04-30) — Visual elegance / kiosk track.
//
// Two-phase orchestrator that snapshots SA best-improvement events
// during a `--bench-sa` run, then composes an animated GIF from
// per-frame Blender renders after the SA loop completes.
//
//                    ┌──────────────────────┐
//   evaluator ───▶  │ OfferFrame(...)      │ ◀── thread-safe
//                    │  • is new best?      │
//                    │  • clone snapshot    │
//                    │  • stash in list     │
//                    └──────────────────────┘
//                            │
//             ─── after SA ──┴──────────────────────────────
//                            │
//                            ▼
//                    ┌──────────────────────┐
//                    │ Compose(...)         │
//                    │  • per-frame:        │
//                    │     - write design   │
//                    │       JSON           │
//                    │     - StlExporter    │
//                    │     - voxelforge-    │
//                    │       render         │
//                    │  • Magick.NET → GIF  │
//                    └──────────────────────┘
//
// Deferred-render design rationale:
//   The SA loop offers ~10–50 best-improvement events for a typical
//   300-iter run. If we rendered each one synchronously inside the
//   evaluator, every Blender call (~30 s) would block the SA chain
//   that triggered it — adding 5–25 min to wall-clock. By stashing
//   only (Conditions, Design, Score, Iter) snapshots during SA
//   (~kB of memory) and rendering after the loop, the SA itself
//   stays clean and the per-frame cost becomes a sequential post-
//   processing step the user accepts up front (`~30–120 s` per the
//   issue, scaled by frame count).
//
// Subprocess seam:
//   Frame rendering routes through ISaFrameRenderer so tests can
//   substitute a stub that writes deterministic solid-colour PNGs
//   (no Blender required for the orchestrator's unit tests). The
//   SubprocessFrameRenderer impl invokes Voxelforge.StlExporter
//   for the STL build and voxelforge-render for the Blender pass.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using ImageMagick;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

/// <summary>
/// One captured SA best-improvement frame. Held in memory between SA
/// iterations and rendered post-loop. Conditions + Design come direct
/// from the Unpacked SA candidate, so each frame is a complete,
/// self-contained design that round-trips through DesignPersistence.
/// </summary>
public sealed record SaFrameSnapshot(
    int                 Iteration,
    double              Score,
    OperatingConditions Conditions,
    RegenChamberDesign  Design);

/// <summary>
/// Result of <see cref="SaAnimationCapture.Compose"/>. Reports how many
/// frames were captured, how many actually rendered (some may have
/// failed mid-run; we keep the GIF and skip the broken ones), and the
/// final GIF path.
/// </summary>
public sealed record SaAnimationResult(
    string GifPath,
    int    FramesCaptured,
    int    FramesRendered,
    long   GifBytes,
    long   ElapsedMilliseconds);

/// <summary>
/// Subprocess seam used by <see cref="SaAnimationCapture"/>. Mocked in
/// tests; production impl is <see cref="SubprocessFrameRenderer"/>.
/// </summary>
public interface ISaFrameRenderer
{
    /// <summary>
    /// Build the STL + render the PNG for one frame. Returns the path
    /// to the rendered PNG on success, null on failure (caller logs +
    /// skips). Implementations write to <paramref name="frameDir"/>.
    /// </summary>
    string? RenderFrame(SaFrameSnapshot frame, string frameDir);
}

/// <summary>
/// Two-phase capture orchestrator.
///
/// Capture phase (during SA): <see cref="OfferFrame"/> is called from
/// the SA evaluator; thread-safe across multi-chain workers via an
/// internal lock. Each new best-feasible score is cloned + stashed.
///
/// Render phase (after SA): <see cref="Compose"/> walks the captured
/// snapshots, fires the renderer per frame, and assembles the result
/// PNG sequence into an animated GIF via Magick.NET.
/// </summary>
public sealed class SaAnimationCapture : IDisposable
{
    private readonly ISaFrameRenderer _renderer;
    private readonly string           _outputGifPath;
    private readonly string           _tempDir;
    private readonly bool             _ownsTempDir;

    private readonly object                _lock   = new();
    private readonly List<SaFrameSnapshot> _frames = new();
    private double                         _bestSoFar;
    private bool                           _disposed;

    /// <summary>Frames captured so far. Read-only snapshot.</summary>
    public IReadOnlyList<SaFrameSnapshot> Frames
    {
        get
        {
            lock (_lock) return _frames.ToArray();
        }
    }

    /// <summary>Iteration counts of all captured frames in capture order.</summary>
    public IReadOnlyList<int> CapturedIterations
    {
        get
        {
            lock (_lock) return _frames.Select(f => f.Iteration).ToArray();
        }
    }

    /// <param name="renderer">Per-frame STL + render seam. Production
    /// uses <see cref="SubprocessFrameRenderer"/>; tests use a stub.</param>
    /// <param name="outputGifPath">Final GIF output path. Parent
    /// directory is created if missing.</param>
    /// <param name="tempDir">Optional working directory for the
    /// per-frame STL/PNG intermediates. When null, a fresh dir is
    /// created under %TEMP%/voxelforge-sa-anim-<guid> and cleaned up
    /// on Dispose.</param>
    public SaAnimationCapture(
        ISaFrameRenderer renderer,
        string           outputGifPath,
        string?          tempDir = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (string.IsNullOrWhiteSpace(outputGifPath))
            throw new ArgumentException("output GIF path required", nameof(outputGifPath));

        _renderer      = renderer;
        _outputGifPath = outputGifPath;
        _bestSoFar     = double.PositiveInfinity;

        if (tempDir is null)
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "voxelforge-sa-anim-" + Guid.NewGuid().ToString("N"));
            _ownsTempDir = true;
        }
        else
        {
            _tempDir     = tempDir;
            _ownsTempDir = false;
        }
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Offer a candidate to the capture pipeline. Returns true if the
    /// frame was captured (new feasible best), false otherwise.
    ///
    /// Thread-safe: lock-serialised so multi-chain SA workers can call
    /// concurrently. The lock contention is bounded — best-improvements
    /// are rare (10–50 per 300-iter run), so the lock is uncontested
    /// 95 %+ of the time.
    /// </summary>
    public bool OfferFrame(
        int                 iteration,
        double              score,
        OperatingConditions conditions,
        RegenChamberDesign  design)
    {
        // Reject infeasible (PositiveInfinity) and NaN — only finite
        // improvements count as a "best" event.
        if (!double.IsFinite(score)) return false;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (score >= _bestSoFar) return false;

            _bestSoFar = score;
            _frames.Add(new SaFrameSnapshot(
                Iteration:  iteration,
                Score:      score,
                Conditions: conditions,
                Design:     design));
            return true;
        }
    }

    /// <summary>
    /// Render every captured frame, then compose the GIF. Frames that
    /// fail to render are skipped (the GIF still emits with what's
    /// available) — caller can read the result's
    /// <see cref="SaAnimationResult.FramesRendered"/> vs
    /// <see cref="SaAnimationResult.FramesCaptured"/> to detect
    /// partial-render runs.
    ///
    /// If zero frames were captured (SA never found a feasible
    /// improvement) <see cref="SaAnimationResult.GifBytes"/> is 0 and
    /// no GIF file is written.
    /// </summary>
    /// <param name="frameDelayMs">Inter-frame delay in milliseconds.
    /// Default 500 ms = 2 fps, readable on a TV.</param>
    /// <param name="holdLastFrameMs">Extra delay on the final frame so
    /// the converged design lingers before the loop restarts. Default
    /// 2500 ms.</param>
    public SaAnimationResult Compose(
        int frameDelayMs    = 500,
        int holdLastFrameMs = 2500)
    {
        if (frameDelayMs    < 20)  throw new ArgumentOutOfRangeException(nameof(frameDelayMs));
        if (holdLastFrameMs < 0)   throw new ArgumentOutOfRangeException(nameof(holdLastFrameMs));

        SaFrameSnapshot[] snap;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            snap = _frames.ToArray();
        }

        long swStart = System.Diagnostics.Stopwatch.GetTimestamp();

        if (snap.Length == 0)
        {
            return new SaAnimationResult(
                GifPath:             _outputGifPath,
                FramesCaptured:      0,
                FramesRendered:      0,
                GifBytes:            0,
                ElapsedMilliseconds: 0);
        }

        // Render each frame to PNG. Files land in <tempDir>/frame-NNNN/
        // so the per-frame STL + design JSON + PNG live together for
        // diagnosis if a run misbehaves.
        var renderedPngs = new List<string>(snap.Length);
        for (int i = 0; i < snap.Length; i++)
        {
            string frameDir = Path.Combine(_tempDir, $"frame-{i:D4}");
            Directory.CreateDirectory(frameDir);
            string? pngPath = _renderer.RenderFrame(snap[i], frameDir);
            if (pngPath is not null && File.Exists(pngPath))
                renderedPngs.Add(pngPath);
        }

        long bytes = 0;
        if (renderedPngs.Count > 0)
        {
            // Ensure parent dir of GIF exists.
            string? gifParent = Path.GetDirectoryName(Path.GetFullPath(_outputGifPath));
            if (!string.IsNullOrEmpty(gifParent))
                Directory.CreateDirectory(gifParent);

            using var collection = new MagickImageCollection();
            for (int i = 0; i < renderedPngs.Count; i++)
            {
                var img = new MagickImage(renderedPngs[i]);
                int delayCs = Math.Max(1, frameDelayMs / 10);
                if (i == renderedPngs.Count - 1)
                    delayCs = Math.Max(delayCs, (frameDelayMs + holdLastFrameMs) / 10);
                // GIF AnimationDelay is in centiseconds (1/100 s) per the
                // GIF89a spec; Magick.NET preserves that unit on the
                // MagickImage when writing to GIF.
                img.AnimationDelay = (uint)delayCs;
                collection.Add(img);
            }

            // 0 = infinite loop. Suits the kiosk TV-display use case
            // (issue #287: "Trade-show workflow … play the resulting
            // GIF on a TV via VLC loop mode" — VLC loops itself, but a
            // GIF that loops natively is friendlier for previews and
            // for the wider documentation surface like a README image).
            collection.Coalesce();
            collection[0].AnimationIterations = 0;
            collection.Write(_outputGifPath);

            bytes = new FileInfo(_outputGifPath).Length;
        }

        long swEnd = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsedMs = (swEnd - swStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        return new SaAnimationResult(
            GifPath:             _outputGifPath,
            FramesCaptured:      snap.Length,
            FramesRendered:      renderedPngs.Count,
            GifBytes:            bytes,
            ElapsedMilliseconds: (long)elapsedMs);
    }

    /// <summary>
    /// Persist a frame's design + conditions to a JSON file the
    /// StlExporter can consume. Used by the production
    /// <see cref="SubprocessFrameRenderer"/>; exposed on
    /// <see cref="SaAnimationCapture"/> as a static helper so tests
    /// (or alternative renderers like a CFD-export pipeline) can reuse
    /// it without re-deriving the file-naming convention.
    /// </summary>
    public static string WriteFrameDesignJson(SaFrameSnapshot frame, string frameDir)
    {
        Directory.CreateDirectory(frameDir);
        string designPath = Path.Combine(
            frameDir,
            string.Create(CultureInfo.InvariantCulture,
                $"frame-iter-{frame.Iteration:D5}.rcd.json"));
        DesignPersistence.Save(designPath, frame.Conditions, frame.Design, r: null);
        return designPath;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (_ownsTempDir)
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort — temp dirs cleared on reboot anyway */ }
        }
    }
}
