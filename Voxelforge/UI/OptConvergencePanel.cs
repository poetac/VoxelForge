// OptConvergencePanel.cs — Score-vs-iteration trace panel so the user
// can see WHEN the SA search converged vs is still finding new best
// scores.
//
// Today `lblOptProgress` shows "iter N / 300 score=X best=Y" as text.
// That catches the current iteration but hides the trajectory — "your
// SA stalled at iter 80, save your time" is a missed signal. This
// panel plots best-so-far as a line against iteration count so the
// shape of convergence (steep drop → long flat tail → restart bump)
// is visible at a glance.
//
// Paint-only, mirrors the ParetoScatterPanel / AxialProfileChartPanel
// pattern — no NuGet deps. Points are accumulated from each progress
// poll (via `AppendPoint`), one trace: best-so-far.
// Reset cleared when a new optimization starts.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Voxelforge.UI;

public sealed class OptConvergencePanel : Panel
{
    private readonly List<(int Iteration, double BestScore)> _history = new();
    private int _restartMarker = -1;
    private int _lastRestartCount;

    public OptConvergencePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    /// <summary>
    /// Append the most-recent best-so-far reading from an SA poll.
    /// Deduplicates: ignores a call with the same iteration as the
    /// previous one (no UI value in double-plotting the same point).
    /// Non-finite scores are skipped so the autoscale isn't blown
    /// out by a +∞ infeasible-placeholder.
    /// </summary>
    public void AppendPoint(int iteration, double bestScore, int restartCount = 0)
    {
        if (!double.IsFinite(bestScore)) return;
        if (_history.Count > 0 && _history[^1].Iteration == iteration)
        {
            // Update in place if the score dropped (rare — only if a
            // better candidate landed within the same iter slot).
            if (bestScore < _history[^1].BestScore)
                _history[^1] = (iteration, bestScore);
            return;
        }
        _history.Add((iteration, bestScore));
        if (restartCount > _lastRestartCount)
        {
            _restartMarker = iteration;
            _lastRestartCount = restartCount;
        }
        Invalidate();
    }

    /// <summary>Clear accumulated history. Call when starting a new optimization.</summary>
    public void Reset()
    {
        _history.Clear();
        _restartMarker = -1;
        _lastRestartCount = 0;
        Invalidate();
    }

    /// <summary>Snapshot for tests — returns a copy.</summary>
    internal IReadOnlyList<(int Iteration, double BestScore)> SnapshotForTests()
        => _history.ToArray();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = ClientSize.Width, h = ClientSize.Height;
        if (w < 40 || h < 30) return;

        int plotX = 44, plotY = 10, plotW = w - plotX - 10, plotH = h - plotY - 24;

        using (var pen = new Pen(Color.Gray, 1))
            g.DrawRectangle(pen, plotX, plotY, plotW, plotH);

        if (_history.Count < 2)
        {
            using var brush = new SolidBrush(Color.Gray);
            using var font = new Font("Segoe UI", 8.5f);
            g.DrawString("Start an optimization to plot convergence.",
                         font, brush, plotX + 6, plotY + plotH / 2 - 8);
            return;
        }

        int iMin = _history[0].Iteration;
        int iMax = _history[^1].Iteration;
        if (iMax == iMin) iMax = iMin + 1;

        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var p in _history)
        {
            if (p.BestScore < yMin) yMin = p.BestScore;
            if (p.BestScore > yMax) yMax = p.BestScore;
        }
        if (yMax - yMin < 1e-9) yMax = yMin + 1;
        double yPad = (yMax - yMin) * 0.05;
        yMin -= yPad; yMax += yPad;

        Point ToPx(int iter, double score)
        {
            double xN = (iter - iMin) / (double)(iMax - iMin);
            double yN = System.Math.Clamp((score - yMin) / (yMax - yMin), 0, 1);
            return new Point(plotX + (int)(xN * plotW),
                             plotY + plotH - (int)(yN * plotH));
        }

        // Restart marker — vertical dashed line at the iteration where
        // the SA most recently restarted (if any).
        if (_restartMarker > 0)
        {
            var markerPt = ToPx(_restartMarker, yMin);
            using var pen = new Pen(Color.LightCoral, 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, markerPt.X, plotY, markerPt.X, plotY + plotH);
        }

        // Best-so-far trace.
        using (var pen = new Pen(Color.DarkGreen, 1.8f))
        {
            for (int i = 1; i < _history.Count; i++)
            {
                var p1 = ToPx(_history[i - 1].Iteration, _history[i - 1].BestScore);
                var p2 = ToPx(_history[i    ].Iteration, _history[i    ].BestScore);
                g.DrawLine(pen, p1, p2);
            }
        }

        // Corner labels.
        using (var font = new Font("Segoe UI", 7.5f))
        using (var brush = new SolidBrush(Color.Black))
        {
            g.DrawString($"{yMax:F2}", font, brush, 4, plotY);
            g.DrawString($"{yMin:F2}", font, brush, 4, plotY + plotH - 10);
            g.DrawString($"iter {iMin}", font, brush, plotX, plotY + plotH + 6);
            g.DrawString($"{iMax}", font, brush, plotX + plotW - 32, plotY + plotH + 6);
            g.DrawString("best score", font, brush, plotX + 6, plotY + 4);
        }
    }
}
