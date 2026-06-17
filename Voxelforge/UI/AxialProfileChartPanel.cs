// AxialProfileChartPanel.cs — Per-station thermal-profile chart for
// the regen-cooling solver.
//
// The thermal solver produces per-station data
// (`RegenSolverOutputs.Stations[i]`) but the form has historically
// surfaced only summary scalars (PeakWallT_K, CoolantOutletT_K, …).
// This panel overlays four traces against axial position so the
// user can SEE the physics they configured:
//
//   • T_wg(x)        — gas-side wall temperature  [K]
//   • T_wc(x)        — coolant-side wall temperature [K]
//   • T_coolant(x)   — coolant bulk temperature [K]
//   • q(x)           — radial heat flux [W/m²], drawn against a
//                       secondary right-hand y-axis
//
// Dual-axis layout: the three temperature traces share a left-hand
// auto-scaled axis [K]; the heat-flux trace uses a right-hand
// auto-scaled axis [W/m²] — drawn in MW/m² for readability.
//
// Trace selection is toggleable via four CheckBoxes docked across
// the top; hover tooltip shows the numeric values at the cursor x
// (snapped to the nearest station).
//
// Mirrors the existing `ParetoScatterPanel` / `StartTransientChartPanel`
// pattern: paint-only, no NuGet dep, fast enough at 80-200 stations
// for an interactive feel.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Voxelforge.HeatTransfer;

namespace Voxelforge.UI;

public sealed class AxialProfileChartPanel : UserControl
{
    private RegenSolverOutputs? _outputs;

    private readonly CheckBox _chkTwg, _chkTwc, _chkTcoolant, _chkQ;
    private readonly Panel _plotArea;
    private readonly System.Windows.Forms.ToolTip _hoverTip;

    private int _hoverStationIndex = -1;          // -1 = no hover

    public AxialProfileChartPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;

        // Top toolbar: trace toggle checkboxes (default all ON).
        _chkTwg      = MakeToggle("T_wg (gas-side wall)",   Color.Firebrick);
        _chkTwc      = MakeToggle("T_wc (coolant-side wall)", Color.OrangeRed);
        _chkTcoolant = MakeToggle("T coolant",              Color.RoyalBlue);
        _chkQ        = MakeToggle("q (right axis)",         Color.DarkGreen);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 26,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = SystemColors.ControlLight,
            Padding = new Padding(4, 2, 4, 2),
        };
        toolbar.Controls.AddRange(new Control[]
        { _chkTwg, _chkTwc, _chkTcoolant, _chkQ });

        // Plot area: a paint-only Panel docked below the toolbar.
        _plotArea = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.White,
        };
        _plotArea.Paint += (_, e) => PaintChart(e.Graphics);
        _plotArea.MouseMove += OnPlotMouseMove;
        _plotArea.MouseLeave += (_, _) => { _hoverStationIndex = -1; _plotArea.Invalidate(); };

        // Use double-buffering on the inner panel via the SetStyle hack
        // (Panel doesn't expose DoubleBuffered as public).
        typeof(Panel).GetMethod("SetStyle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(_plotArea, new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });

        Controls.Add(_plotArea);   // Add Fill control FIRST so it docks under the Top toolbar
        Controls.Add(toolbar);

        _hoverTip = new System.Windows.Forms.ToolTip
        {
            AutoPopDelay = 10000, InitialDelay = 100, ReshowDelay = 50,
        };
    }

    private CheckBox MakeToggle(string text, Color swatch)
    {
        var cb = new CheckBox
        {
            Text = text, Checked = true, AutoSize = true,
            Padding = new Padding(0, 2, 8, 0),
            ForeColor = swatch, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        };
        cb.CheckedChanged += (_, _) => _plotArea.Invalidate();
        return cb;
    }

    /// <summary>
    /// Replace the displayed station data. Null clears.
    /// Caller passes the entire <see cref="RegenSolverOutputs"/> so the
    /// chart can read the per-station arrays directly.
    /// </summary>
    public void SetOutputs(RegenSolverOutputs? outputs)
    {
        // Treat a zero-station output as "no data" so the chart falls
        // back to the placeholder prompt rather than rendering a
        // degenerate frame. The hover and auto-scale code below assume
        // Stations.Length >= 2; guard at the boundary so a caller that
        // passes in an in-flight / uninitialised RegenSolverOutputs
        // can't push us through them.
        _outputs = (outputs is null || outputs.Stations is null || outputs.Stations.Length < 2)
            ? null
            : outputs;
        _hoverStationIndex = -1;
        _plotArea.Invalidate();
    }

    private void OnPlotMouseMove(object? sender, MouseEventArgs e)
    {
        if (_outputs is null || _outputs.Stations.Length < 2) return;

        // Snap to nearest station by x-coordinate.
        var bounds = ComputePlotBounds();
        if (e.X < bounds.PlotX || e.X > bounds.PlotX + bounds.PlotW) return;

        double xFrac = (e.X - bounds.PlotX) / (double)bounds.PlotW;
        double xMm   = xFrac * (bounds.XMax - bounds.XMin) + bounds.XMin;

        // Linear search — N ≤ 200 stations, fine for interactive hover.
        int bestIdx = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _outputs.Stations.Length; i++)
        {
            double d = System.Math.Abs(_outputs.Stations[i].X_mm - xMm);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }
        if (bestIdx == _hoverStationIndex) return;
        _hoverStationIndex = bestIdx;
        _plotArea.Invalidate();

        // Hover tooltip with the per-station numerics.
        var s = _outputs.Stations[bestIdx];
        string txt = $"x = {s.X_mm:F1} mm  (station {bestIdx})\n"
                   + $"T_wg = {s.GasSideWallTemp_K:F0} K\n"
                   + $"T_wc = {s.CoolantSideWallTemp_K:F0} K\n"
                   + $"T_coolant = {s.CoolantBulkTemp_K:F0} K\n"
                   + $"q = {s.HeatFlux_Wm2 / 1e6:F2} MW/m²";
        _hoverTip.SetToolTip(_plotArea, txt);
    }

    // ─── Painting ──────────────────────────────────────────────────────

    private record struct PlotBounds(int PlotX, int PlotY, int PlotW, int PlotH,
                                     double XMin, double XMax,
                                     double TMin, double TMax,
                                     double QMin, double QMax);

    private PlotBounds ComputePlotBounds()
    {
        int w = _plotArea.ClientSize.Width;
        int h = _plotArea.ClientSize.Height;
        int plotX = 44, plotY = 12, plotW = w - plotX - 48, plotH = h - plotY - 24;

        if (_outputs is null || _outputs.Stations.Length < 2)
            return new PlotBounds(plotX, plotY, plotW, plotH, 0, 1, 0, 1, 0, 1);

        double xMin = double.MaxValue, xMax = double.MinValue;
        double tMin = double.MaxValue, tMax = double.MinValue;
        double qMin = double.MaxValue, qMax = double.MinValue;
        foreach (var s in _outputs.Stations)
        {
            if (s.X_mm < xMin) xMin = s.X_mm;
            if (s.X_mm > xMax) xMax = s.X_mm;
            // Temperatures: include all three traces in the same axis.
            tMin = System.Math.Min(tMin, System.Math.Min(System.Math.Min(s.GasSideWallTemp_K, s.CoolantSideWallTemp_K), s.CoolantBulkTemp_K));
            tMax = System.Math.Max(tMax, System.Math.Max(System.Math.Max(s.GasSideWallTemp_K, s.CoolantSideWallTemp_K), s.CoolantBulkTemp_K));
            if (s.HeatFlux_Wm2 < qMin) qMin = s.HeatFlux_Wm2;
            if (s.HeatFlux_Wm2 > qMax) qMax = s.HeatFlux_Wm2;
        }
        if (xMax - xMin < 1e-3) xMax = xMin + 1;
        if (tMax - tMin < 1e-3) tMax = tMin + 1;
        if (qMax - qMin < 1e-3) qMax = qMin + 1;

        // Pad the temperature axis by 5 % so the trace doesn't sit on
        // the frame; same for q.
        double tPad = (tMax - tMin) * 0.05;
        double qPad = (qMax - qMin) * 0.05;
        return new PlotBounds(plotX, plotY, plotW, plotH,
                              xMin, xMax,
                              tMin - tPad, tMax + tPad,
                              qMin - qPad, qMax + qPad);
    }

    private void PaintChart(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var b = ComputePlotBounds();

        // Frame.
        using (var pen = new Pen(Color.Gray, 1))
            g.DrawRectangle(pen, b.PlotX, b.PlotY, b.PlotW, b.PlotH);

        if (_outputs is null || _outputs.Stations.Length < 2)
        {
            using var brush = new SolidBrush(Color.Gray);
            using var font = new Font("Segoe UI", 8.5f);
            g.DrawString("Click Generate to populate the axial-profile chart.",
                         font, brush, b.PlotX + 6, b.PlotY + b.PlotH / 2 - 8);
            return;
        }

        Point ToPx(double xMm, double y, double yMin, double yMax)
        {
            double xN = (xMm - b.XMin) / (b.XMax - b.XMin);
            double yN = System.Math.Clamp((y - yMin) / (yMax - yMin), 0, 1);
            return new Point(b.PlotX + (int)(xN * b.PlotW),
                             b.PlotY + b.PlotH - (int)(yN * b.PlotH));
        }

        // Throat marker — vertical dashed line at the throat station x.
        if (_outputs.Stations.Length > 0)
        {
            int throatIdx = _outputs.PeakStationIndex;   // proxy: peak T usually within a few stations of throat
            // Use the actual throat: find the station with the smallest area-ratio (= 1.0).
            double bestAr = double.MaxValue;
            for (int i = 0; i < _outputs.Stations.Length; i++)
            {
                double ar = System.Math.Abs(_outputs.Stations[i].AreaRatioToThroat - 1.0);
                if (ar < bestAr) { bestAr = ar; throatIdx = i; }
            }
            double xThroat = _outputs.Stations[throatIdx].X_mm;
            int xThroatPx = (int)(b.PlotX + ((xThroat - b.XMin) / (b.XMax - b.XMin)) * b.PlotW);
            using var pen = new Pen(Color.LightGray, 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(pen, xThroatPx, b.PlotY, xThroatPx, b.PlotY + b.PlotH);
            using var font = new Font("Segoe UI", 7f);
            using var brush = new SolidBrush(Color.Gray);
            g.DrawString("throat", font, brush, xThroatPx - 18, b.PlotY + 2);
        }

        // Trace draw helper (left-axis K).
        void DrawT(System.Func<StationResult, double> sel, Color color, bool show)
        {
            if (!show) return;
            using var pen = new Pen(color, 1.6f);
            for (int i = 1; i < _outputs.Stations.Length; i++)
            {
                var p1 = ToPx(_outputs.Stations[i - 1].X_mm, sel(_outputs.Stations[i - 1]), b.TMin, b.TMax);
                var p2 = ToPx(_outputs.Stations[i    ].X_mm, sel(_outputs.Stations[i    ]), b.TMin, b.TMax);
                g.DrawLine(pen, p1, p2);
            }
        }
        // Trace for q (right-axis W/m²).
        void DrawQ(Color color, bool show)
        {
            if (!show) return;
            using var pen = new Pen(color, 1.6f);
            for (int i = 1; i < _outputs.Stations.Length; i++)
            {
                var p1 = ToPx(_outputs.Stations[i - 1].X_mm, _outputs.Stations[i - 1].HeatFlux_Wm2, b.QMin, b.QMax);
                var p2 = ToPx(_outputs.Stations[i    ].X_mm, _outputs.Stations[i    ].HeatFlux_Wm2, b.QMin, b.QMax);
                g.DrawLine(pen, p1, p2);
            }
        }

        DrawT(s => s.GasSideWallTemp_K,     Color.Firebrick, _chkTwg.Checked);
        DrawT(s => s.CoolantSideWallTemp_K, Color.OrangeRed, _chkTwc.Checked);
        DrawT(s => s.CoolantBulkTemp_K,     Color.RoyalBlue, _chkTcoolant.Checked);
        DrawQ(Color.DarkGreen, _chkQ.Checked);

        // Hover crosshair + station marker.
        if (_hoverStationIndex >= 0 && _hoverStationIndex < _outputs.Stations.Length)
        {
            var s = _outputs.Stations[_hoverStationIndex];
            double xN = (s.X_mm - b.XMin) / (b.XMax - b.XMin);
            int xPx = b.PlotX + (int)(xN * b.PlotW);
            using var pen = new Pen(Color.LightGray, 1) { DashStyle = DashStyle.Dot };
            g.DrawLine(pen, xPx, b.PlotY, xPx, b.PlotY + b.PlotH);
        }

        // Axis labels — left K, right MW/m², bottom mm.
        using (var font = new Font("Segoe UI", 7.5f))
        using (var brush = new SolidBrush(Color.Black))
        {
            g.DrawString($"{b.TMax:F0} K", font, brush, 4, b.PlotY);
            g.DrawString($"{b.TMin:F0} K", font, brush, 4, b.PlotY + b.PlotH - 10);
            g.DrawString($"{b.QMax / 1e6:F1} MW/m²", font, brush, b.PlotX + b.PlotW + 4, b.PlotY);
            g.DrawString($"{b.QMin / 1e6:F1} MW/m²", font, brush, b.PlotX + b.PlotW + 4, b.PlotY + b.PlotH - 10);
            g.DrawString($"x {b.XMin:F0} mm", font, brush, b.PlotX, b.PlotY + b.PlotH + 6);
            g.DrawString($"{b.XMax:F0} mm", font, brush, b.PlotX + b.PlotW - 38, b.PlotY + b.PlotH + 6);
        }

        // Axis titles — center-aligned on each axis, italic to
        // distinguish from the min/max value labels.
        using (var titleFont = new Font("Segoe UI", 7.5f, FontStyle.Italic))
        using (var titleBrush = new SolidBrush(Color.DimGray))
        {
            var xTitle = "axial position x [mm]";
            var xSize = g.MeasureString(xTitle, titleFont);
            g.DrawString(xTitle, titleFont, titleBrush,
                         b.PlotX + (b.PlotW - xSize.Width) / 2f,
                         b.PlotY + b.PlotH + 6);

            var yLeftTitle = "T [K]";
            var state = g.Save();
            g.TranslateTransform(14, b.PlotY + b.PlotH / 2f + g.MeasureString(yLeftTitle, titleFont).Width / 2f);
            g.RotateTransform(-90);
            g.DrawString(yLeftTitle, titleFont, titleBrush, 0, 0);
            g.Restore(state);

            var yRightTitle = "q [MW/m²]";
            state = g.Save();
            g.TranslateTransform(b.PlotX + b.PlotW + 30,
                                 b.PlotY + b.PlotH / 2f + g.MeasureString(yRightTitle, titleFont).Width / 2f);
            g.RotateTransform(-90);
            g.DrawString(yRightTitle, titleFont, titleBrush, 0, 0);
            g.Restore(state);
        }
    }
}
