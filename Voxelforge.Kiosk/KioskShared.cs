// KioskShared.cs — request queue + completion events between the
// WinForms STA thread and the PicoGK task thread.
//
// Two request kinds:
//   • TryAnother (preview): build a fresh perturbed design at the
//     given preset + seq, push to viewer. Voxels remain in
//     task-thread state for a subsequent Commit.
//   • Commit: take the most recent preview, write STL + fire the
//     production render asynchronously.
//
// Completion events:
//   • OnPreviewReady — preview built, viewer updated. Form re-enables
//     Try Another / Save This.
//   • OnCommitReady  — STL written. Form shows file path; render is
//     still running in background.
//   • OnRenderReady  — Blender render finished, PNG written.
//   • OnError        — any failure path; form shows the error.

using System.Collections.Concurrent;

namespace Voxelforge.Kiosk;

public abstract record KioskRequest;
public sealed record KioskTryAnotherRequest(string PresetName, int SequenceNumber) : KioskRequest;
public sealed record KioskCommitRequest(string WatchFolder)                        : KioskRequest;

public sealed record KioskPreviewReady(
    string PresetName,
    int    SequenceNumber,
    string Description,
    double BoundingLength_mm,
    double BoundingDiameter_mm);

internal static class KioskShared
{
    private static readonly ConcurrentQueue<KioskRequest> _queue = new();

    public static event Action<KioskPreviewReady>? OnPreviewReady;
    public static event Action<KioskCommitResult>? OnCommitReady;
    public static event Action<string>?            OnRenderReady;     // png path
    public static event Action<Exception, string>? OnError;           // (ex, ctx)

    public static void Enqueue(KioskRequest req)             => _queue.Enqueue(req);
    public static bool TryDequeue(out KioskRequest? req)     => _queue.TryDequeue(out req);

    public static void RaisePreviewReady(KioskPreviewReady r) => OnPreviewReady?.Invoke(r);
    public static void RaiseCommitReady(KioskCommitResult r)  => OnCommitReady?.Invoke(r);
    public static void RaiseRenderReady(string pngPath)        => OnRenderReady?.Invoke(pngPath);
    public static void RaiseError(Exception ex, string ctx)   => OnError?.Invoke(ex, ctx);
}
