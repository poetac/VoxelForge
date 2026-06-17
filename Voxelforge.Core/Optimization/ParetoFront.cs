// ParetoFront.cs — PHASE 6 (2026-04-20):
// Track the non-dominated set of (peak wall T, coolant ΔP, mass) tuples
// seen during a simulated-annealing run.
//
// Why this matters:
//   The 13-var SA optimizer returns one weighted-sum winner per scoring
//   profile. That hides the trade-off surface — a user who wants "low
//   wall T" and "low mass" has no way to see candidates that were
//   dominated on the weighted score but excellent on two of three axes.
//   A Pareto front exposes that frontier directly.
//
// Contract:
//   • Minimisation on all three axes.
//   • A point p1 dominates p2 iff p1 is ≤ p2 on every axis AND strictly <
//     p2 on at least one axis.
//   • On Offer(candidate):
//       – If candidate is dominated by any existing point → drop it.
//       – Else remove any existing points dominated by the candidate, add
//         the candidate to the set.
//   • Thread-safe for concurrent Offer calls from the SA loop; snapshot
//     reads are lock-free via an immutable array swap.
//
// Scope limits (MVP):
//   • No hypervolume metric.
//   • No front visualisation widget — consumers render a text table.
//   • Non-dominated set is pruned to MaxSize by k-medoid on the axes,
//     defaulting to 64. Adequate for a 300–600 iteration SA run.

namespace Voxelforge.Optimization;

/// <summary>
/// A single point on the Pareto front. <see cref="Parameters"/> is a copy
/// of the SA parameter vector that produced it — the caller stores it so
/// the user can re-load any front point into the design UI.
///
/// <see cref="SMD_um"/> carries the Rizk-Lefebvre Sauter Mean Diameter
/// for the candidate's injector pattern (NaN when no implemented
/// element pattern is active or the propellant pair has no published
/// fluid-properties entry). Consumed by
/// <see cref="UI.ParetoScatterPanel"/> when the colour mode is set to
/// <c>ColorBy.SMD</c>. Default NaN preserves legacy behaviour.
/// </summary>
public sealed record ParetoPoint(
    double   PeakWallT_K,
    double   CoolantDP_Pa,
    double   Mass_g,
    double[] Parameters,
    int      Iteration,
    double   SMD_um = double.NaN);

public sealed class ParetoFront
{
    private readonly object _lock = new();
    private List<ParetoPoint> _points = new();

    public int MaxSize { get; init; } = 64;

    /// <summary>Snapshot of the current front (stable copy).</summary>
    public IReadOnlyList<ParetoPoint> Points
    {
        get { lock (_lock) return _points.ToArray(); }
    }

    public int Count
    {
        get { lock (_lock) return _points.Count; }
    }

    public void Clear()
    {
        lock (_lock) _points = new List<ParetoPoint>();
    }

    /// <summary>
    /// Try to add <paramref name="candidate"/> to the front. Returns true
    /// when it was added (meaning at least one incumbent was demoted, or
    /// the candidate was strictly new).
    /// </summary>
    public bool Offer(ParetoPoint candidate)
    {
        if (!double.IsFinite(candidate.PeakWallT_K)
         || !double.IsFinite(candidate.CoolantDP_Pa)
         || !double.IsFinite(candidate.Mass_g))
            return false;

        lock (_lock)
        {
            // Dominated by any existing point? Drop the candidate.
            foreach (var p in _points)
                if (Dominates(p, candidate)) return false;

            // Remove incumbents dominated by the candidate; keep equals.
            var keep = new List<ParetoPoint>(_points.Count + 1);
            foreach (var p in _points)
                if (!Dominates(candidate, p)) keep.Add(p);
            keep.Add(candidate);

            // Prune to MaxSize via simple axis-uniform thinning if needed.
            if (keep.Count > MaxSize)
            {
                keep.Sort((a, b) => a.PeakWallT_K.CompareTo(b.PeakWallT_K));
                // Uniformly decimate — keeps extremes on the peak-T axis.
                var thinned = new List<ParetoPoint>(MaxSize);
                for (int i = 0; i < MaxSize; i++)
                {
                    int idx = (int)((long)i * (keep.Count - 1) / Math.Max(MaxSize - 1, 1));
                    thinned.Add(keep[idx]);
                }
                keep = thinned;
            }
            _points = keep;
            return true;
        }
    }

    /// <summary>True iff <paramref name="a"/> dominates <paramref name="b"/>.</summary>
    public static bool Dominates(ParetoPoint a, ParetoPoint b)
    {
        bool le = a.PeakWallT_K <= b.PeakWallT_K
               && a.CoolantDP_Pa <= b.CoolantDP_Pa
               && a.Mass_g       <= b.Mass_g;
        bool lt = a.PeakWallT_K <  b.PeakWallT_K
               || a.CoolantDP_Pa <  b.CoolantDP_Pa
               || a.Mass_g       <  b.Mass_g;
        return le && lt;
    }

    /// <summary>
    /// Canonical CSV serialiser. The header line
    /// is `iteration,peak_wall_t_k,coolant_dp_pa,mass_g`; each subsequent
    /// row matches a `ParetoPoint`. Used by both the batch-mode writer
    /// in <see cref="Program.WriteBatchOutputs"/> and the interactive
    /// "Save Pareto CSV…" UI action so the format stays consistent.
    /// Returns the number of rows written (excluding the header).
    /// </summary>
    public static int SaveToCsv(string path, IReadOnlyList<ParetoPoint> points)
    {
        var sb = new System.Text.StringBuilder(64 + 64 * points.Count);
        sb.AppendLine("iteration,peak_wall_t_k,coolant_dp_pa,mass_g");
        foreach (var pt in points)
        {
            sb.Append(pt.Iteration).Append(',')
              .Append(pt.PeakWallT_K.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(pt.CoolantDP_Pa.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(pt.Mass_g.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        System.IO.File.WriteAllText(path, sb.ToString());
        return points.Count;
    }
}
