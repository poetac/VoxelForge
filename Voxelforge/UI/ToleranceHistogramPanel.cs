// ToleranceHistogramPanel.cs — Histogram + p10-p90 band for the
// Monte-Carlo tolerance sweep so the
// user can see the SHAPE of the distribution (unimodal / bimodal /
// skewed), not just the p10/p50/p90/p99 summary that `lblTolSummary`
// carries today.
//
// Four traces toggleable via a header ComboBox: peak wall T,
// min safety factor, coolant ΔP, coolant outlet T. Each trace comes
// straight from `ToleranceResult.Samples_*`; panel is inert when
// those arrays are null (legacy tolerance results or a sweep still
// in flight).
//
// Binning: fixed 30 bins over [min, max] of the selected array. A
// light-grey p10-p90 band is drawn across the plot, with a vertical
// p50 line on top of the bars. Y axis is sample count; x axis is
// labelled with the selected trace's engineering units.

using System.Drawing;
using System.Windows.Forms;
using Voxelforge.Analysis;

namespace Voxelforge.UI;

public sealed class ToleranceHistogramPanel : UserControl
{
    public enum Trace
    {
        PeakWallT_K,
        MinSafetyFactor,
        CoolantPressureDrop_Pa,
        CoolantOutletT_K,
    }

    private ToleranceResult? _result;
    private Trace _activeTrace = Trace.PeakWallT_K;

    private readonly ComboBox _traceCombo;
    private readonly Panel _plotArea;

    public ToleranceHistogramPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;

        // Create the plot surface FIRST so the ComboBox change handler
        // (which calls _plotArea.Invalidate) can't fire against a still-
        // null field when SelectedIndex is initialised below.
        _plotArea = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.White,
        };
        _plotArea.Paint += (_, e) => PaintHistogram(e.Graphics);

        _traceCombo = new ComboBox
        {
            Dock = DockStyle.Top, Height = 24,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 8.25f),
        };
        _traceCombo.Items.AddRange(new object[]
        {
            "Peak wall T (K)",
            "Min safety factor",
            "Coolant ΔP (MPa)",
            "Coolant outlet T (K)",
        });
        _traceCombo.SelectedIndex = 0;
        _traceCombo.SelectedIndexChanged += (_, _) =>
        {
            _activeTrace = (Trace)_traceCombo.SelectedIndex;
            _plotArea.Invalidate();
        };

        // Same double-buffer hack AxialProfileChartPanel uses — Panel
        // doesn't expose DoubleBuffered publicly.
        typeof(Panel).GetMethod("SetStyle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(_plotArea,
                     new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });

        Controls.Add(_plotArea);
        Controls.Add(_traceCombo);
    }

    /// <summary>Replace the displayed sweep. Null clears.</summary>
    public void SetResult(ToleranceResult? result)
    {
        _result = result;
        _plotArea.Invalidate();
    }

    /// <summary>Exposed for tests — resolve which trace the combo currently selects.</summary>
    internal Trace ActiveTrace => _activeTrace;

    /// <summary>Exposed for tests — pull the active per-sample array off the stored result.</summary>
    internal double[]? ActiveSamples()
    {
        if (_result is null) return null;
        return _activeTrace switch
        {
            Trace.PeakWallT_K              => _result.Samples_PeakWallT_K,
            Trace.MinSafetyFactor          => _result.Samples_MinSafetyFactor,
            Trace.CoolantPressureDrop_Pa   => _result.Samples_CoolantPressureDrop_Pa,
            Trace.CoolantOutletT_K         => _result.Samples_CoolantOutletT_K,
            _ => null,
        };
    }

    /// <summary>Exposed for tests — resolve the active quantile summary.</summary>
    internal ToleranceQuantile? ActiveQuantile()
    {
        if (_result is null) return null;
        return _activeTrace switch
        {
            Trace.PeakWallT_K              => _result.PeakWallT_K,
            Trace.MinSafetyFactor          => _result.MinSafetyFactor,
            Trace.CoolantPressureDrop_Pa   => _result.CoolantPressureDrop_Pa,
            Trace.CoolantOutletT_K         => _result.CoolantOutletT_K,
            _ => null,
        };
    }

    /// <summary>
    /// Build the histogram counts for a sample array. Exposed internal
    /// so Phase8UiInfraTests can assert on the binning logic without
    /// driving the WinForms paint path.
    /// </summary>
    internal static int[] Bin(double[] samples, int binCount, double min, double max)
    {
        var counts = new int[binCount];
        if (samples.Length == 0 || binCount < 1) return counts;
        double span = max - min;
        if (span <= 0) { counts[0] = samples.Length; return counts; }
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i];
            int idx = (int)((v - min) / span * binCount);
            if (idx < 0) idx = 0;
            else if (idx >= binCount) idx = binCount - 1;
            counts[idx]++;
        }
        return counts;
    }

    private void PaintHistogram(Graphics g)
    {
        int w = _plotArea.ClientSize.Width, h = _plotArea.ClientSize.Height;
        if (w < 40 || h < 30) return;
        int plotX = 44, plotY = 10, plotW = w - plotX - 10, plotH = h - plotY - 24;

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var pen = new Pen(Color.Gray, 1))
            g.DrawRectangle(pen, plotX, plotY, plotW, plotH);

        var samples = ActiveSamples();
        var quantile = ActiveQuantile();
        if (samples is null || samples.Length == 0 || quantile is null)
        {
            using var brush = new SolidBrush(Color.Gray);
            using var font = new Font("Segoe UI", 8.5f);
            g.DrawString("Run a tolerance sweep to populate the histogram.",
                         font, brush, plotX + 6, plotY + plotH / 2 - 8);
            return;
        }

        // Scale factor for the selected trace's display units.
        double scale = _activeTrace == Trace.CoolantPressureDrop_Pa ? 1e-6 : 1.0;
        string unit = _activeTrace switch
        {
            Trace.PeakWallT_K            => "K",
            Trace.MinSafetyFactor        => "",
            Trace.CoolantPressureDrop_Pa => "MPa",
            Trace.CoolantOutletT_K       => "K",
            _ => "",
        };

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in samples) { if (v < min) min = v; if (v > max) max = v; }
        if (max - min < 1e-9) max = min + 1;

        const int binCount = 30;
        var counts = Bin(samples, binCount, min, max);
        int cMax = 1;
        for (int i = 0; i < binCount; i++) if (counts[i] > cMax) cMax = counts[i];

        // p10-p90 shaded band (drawn behind bars).
        double bandFrac_lo = (quantile.P10 - min) / (max - min);
        double bandFrac_hi = (quantile.P90 - min) / (max - min);
        int bandX0 = plotX + (int)(System.Math.Clamp(bandFrac_lo, 0, 1) * plotW);
        int bandX1 = plotX + (int)(System.Math.Clamp(bandFrac_hi, 0, 1) * plotW);
        using (var bandBrush = new SolidBrush(Color.FromArgb(40, 100, 140, 220)))
            g.FillRectangle(bandBrush, bandX0, plotY, System.Math.Max(bandX1 - bandX0, 1), plotH);

        // Histogram bars.
        double barStep = plotW / (double)binCount;
        using (var barBrush = new SolidBrush(Color.FromArgb(200, 70, 110, 170)))
        {
            for (int i = 0; i < binCount; i++)
            {
                double hFrac = counts[i] / (double)cMax;
                int barH = (int)(hFrac * (plotH - 4));
                int bx = plotX + (int)(i * barStep);
                int bw = System.Math.Max(1, (int)barStep - 1);
                g.FillRectangle(barBrush, bx, plotY + plotH - barH, bw, barH);
            }
        }

        // p50 vertical line.
        double p50Frac = (quantile.P50 - min) / (max - min);
        int p50X = plotX + (int)(System.Math.Clamp(p50Frac, 0, 1) * plotW);
        using (var p50Pen = new Pen(Color.Firebrick, 1.5f))
            g.DrawLine(p50Pen, p50X, plotY, p50X, plotY + plotH);

        // Axis labels — corner values of the sample range + N samples.
        using (var font = new Font("Segoe UI", 7.5f))
        using (var brush = new SolidBrush(Color.Black))
        {
            g.DrawString($"{cMax}", font, brush, 4, plotY);
            g.DrawString("0", font, brush, 4, plotY + plotH - 10);
            g.DrawString($"{(min * scale):F2} {unit}", font, brush, plotX, plotY + plotH + 6);
            g.DrawString($"{(max * scale):F2} {unit}", font, brush, plotX + plotW - 52, plotY + plotH + 6);
            g.DrawString($"N={samples.Length}, p10 {(quantile.P10 * scale):F2} · p50 {(quantile.P50 * scale):F2} · p90 {(quantile.P90 * scale):F2}",
                         font, new SolidBrush(Color.Gray), plotX + 6, plotY + 4);
        }
    }
}
