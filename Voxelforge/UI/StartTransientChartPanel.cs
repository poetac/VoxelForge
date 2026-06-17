// StartTransientChartPanel.cs — Minimal time-history chart for the
// lumped 0-D start-transient
// simulator (`Combustion.StartTransientSim`). Mirrors the
// ParetoScatterPanel pattern — paint-only, no NuGet plotting
// dependency, fast enough for the few-thousand-sample histories
// the simulator returns at default 1 ms time step.
//
// Three traces overlaid in the same axis:
//   • Blue solid  — chamber pressure Pc(t), normalised to target Pc.
//   • Gray dashed — valve position (0 → 1 ramp).
//   • Orange      — dome fill fraction (0 → 1, plateaus at 1).
//
// X axis: time (ms). Y axis: 0 → 1.1 (normalised).
//
// Intentional MVP scope:
//   • No zoom / pan, no legend overlay (axis label corner only).
//   • Re-paints on every `SetSamples` call.
//   • Hit-testing not implemented — the chart is read-only.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Voxelforge.Combustion;

namespace Voxelforge.UI;

public sealed class StartTransientChartPanel : Panel
{
    private StartTransientSample[]? _samples;
    private double _targetPc_Pa = 6.9e6;

    public StartTransientChartPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    /// <summary>Replace the displayed samples + target Pc reference. Null clears.</summary>
    public void SetSamples(StartTransientSample[]? samples, double targetPc_Pa)
    {
        _samples = samples;
        _targetPc_Pa = System.Math.Max(targetPc_Pa, 1.0);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 40 || h < 40) return;

        // Frame.
        int plotX = 36, plotY = 12, plotW = w - plotX - 8, plotH = h - plotY - 24;
        using (var pen = new Pen(Color.Gray, 1))
            g.DrawRectangle(pen, plotX, plotY, plotW, plotH);

        if (_samples is null || _samples.Length < 2)
        {
            using var brush = new SolidBrush(Color.Gray);
            using var font = new Font("Segoe UI", 8.5f);
            g.DrawString("Enable Start Transient and click Generate to populate the chart.",
                         font, brush, plotX + 6, plotY + plotH / 2 - 8);
            return;
        }

        double tMax_s = _samples[^1].Time_s;
        if (tMax_s < 1e-6) tMax_s = 1.0;

        Point ToPx(double t_s, double yNorm)
        {
            double xN = t_s / tMax_s;
            // y axis: 0 (bottom) → 1.1 (top); 1.0 = target Pc / fully open.
            double yN = System.Math.Clamp(yNorm / 1.1, 0.0, 1.0);
            return new Point(plotX + (int)(xN * plotW), plotY + plotH - (int)(yN * plotH));
        }

        // Reference line at y = 1.0 (= target Pc).
        using (var refPen = new Pen(Color.LightGray, 1) { DashStyle = DashStyle.Dash })
        {
            int yRef = ToPx(0, 1.0).Y;
            g.DrawLine(refPen, plotX, yRef, plotX + plotW, yRef);
        }

        // Pc(t) — blue solid.
        DrawTrace(g, _samples,
            select: s => s.ChamberPressure_Pa / _targetPc_Pa,
            ToPx, color: Color.RoyalBlue, dashed: false);

        // Valve(t) — gray dashed.
        DrawTrace(g, _samples,
            select: s => s.ValvePosition,
            ToPx, color: Color.DimGray, dashed: true);

        // Dome fill(t) — orange.
        DrawTrace(g, _samples,
            select: s => s.DomeFillFraction,
            ToPx, color: Color.DarkOrange, dashed: false);

        using (var font = new Font("Segoe UI", 7.5f))
        using (var lblBrush = new SolidBrush(Color.Black))
        {
            g.DrawString("0", font, lblBrush, 4, plotY + plotH - 10);
            g.DrawString("1.0×Pc", font, lblBrush, 2, plotY);
            g.DrawString($"t {tMax_s * 1000:F0} ms", font, lblBrush, plotX + plotW - 50, plotY + plotH + 4);
            g.DrawString("0", font, lblBrush, plotX - 4, plotY + plotH + 4);
        }

        using (var font = new Font("Segoe UI", 7.5f))
        using (var lblBrush = new SolidBrush(Color.DimGray))
        {
            g.DrawString("blue=Pc/Pc_target  ·  gray=valve  ·  orange=dome fill",
                         font, lblBrush, plotX + 6, plotY + 2);
        }
    }

    private static void DrawTrace(
        Graphics g, StartTransientSample[] samples,
        System.Func<StartTransientSample, double> select,
        System.Func<double, double, Point> ToPx,
        Color color, bool dashed)
    {
        using var pen = new Pen(color, dashed ? 1.4f : 1.6f);
        if (dashed) pen.DashStyle = DashStyle.Dot;
        for (int i = 1; i < samples.Length; i++)
        {
            var p1 = ToPx(samples[i - 1].Time_s, select(samples[i - 1]));
            var p2 = ToPx(samples[i    ].Time_s, select(samples[i    ]));
            g.DrawLine(pen, p1, p2);
        }
    }
}
