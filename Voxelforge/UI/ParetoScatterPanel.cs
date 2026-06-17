// ParetoScatterPanel.cs — Minimal 2D scatter of the Pareto-front set
// (peak wall T vs coolant ΔP), coloured by mass OR Rizk-Lefebvre SMD.
// Click a dot to raise <see cref="PointSelected"/> so the host form
// can apply that candidate back into the design UI.
//
// Intentional MVP scope:
//   • No zoom / pan, no axis labels — the Panel paints raw on every
//     Invalidate. At 64 points this is fast enough to feel responsive.
//   • Mass is shown via a warm-cool colour ramp: blue = light, red = heavy.
//   • SMD uses the same ramp: blue = small drops (good atomisation),
//     red = large drops (poor atomisation). Points with NaN SMD are
//     drawn grey so the user can see they weren't characterised.
//   • Hit-test tolerance is 8 px. Clicking outside a dot does nothing.

using System.Drawing;
using System.Windows.Forms;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

/// <summary>
/// Colour-ramp mode for the Pareto scatter.
/// </summary>
public enum ParetoColorBy
{
    /// <summary>Legacy: colour by mass (blue=light, red=heavy).</summary>
    Mass = 0,
    /// <summary>Colour by Rizk-Lefebvre SMD (blue=small/good, red=large/poor).</summary>
    SMD  = 1,
}

public sealed class ParetoScatterPanel : Panel
{
    private IReadOnlyList<ParetoPoint>? _points;
    private readonly List<(Rectangle rect, ParetoPoint pt)> _hit = new();
    private ParetoColorBy _colorBy = ParetoColorBy.Mass;

    public ParetoScatterPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    /// <summary>Replace the displayed Pareto points. Call with null to clear.</summary>
    public void SetPoints(IReadOnlyList<ParetoPoint>? pts)
    {
        _points = pts;
        Invalidate();
    }

    /// <summary>
    /// Switch the colour-ramp data source. Setting the same value
    /// twice is a no-op; repaint fires only on actual changes.
    /// </summary>
    public void SetColorBy(ParetoColorBy mode)
    {
        if (_colorBy == mode) return;
        _colorBy = mode;
        Invalidate();
    }

    /// <summary>Current colour-ramp data source.</summary>
    public ParetoColorBy ColorBy => _colorBy;

    public event Action<ParetoPoint>? PointSelected;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _hit.Clear();
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 20 || h < 20) return;

        // Axes frame.
        using (var framePen = new Pen(Color.Gray, 1))
        {
            g.DrawRectangle(framePen, 40, 10, w - 50, h - 40);
        }

        // Axis titles — bottom X (peak wall T), left Y (coolant ΔP).
        // Rendered regardless of data state so an empty chart still
        // tells the user what the axes represent.
        using (var titleFont = new Font("Segoe UI", 8f, FontStyle.Bold))
        using (var titleBrush = new SolidBrush(Color.Black))
        {
            var xTitle = "Peak wall T [K] \u2192";
            var xSize = g.MeasureString(xTitle, titleFont);
            g.DrawString(xTitle, titleFont, titleBrush,
                         40 + ((w - 50) - xSize.Width) / 2f,
                         h - 16);

            var yTitle = "Coolant \u0394P [MPa] \u2192";
            var state = g.Save();
            g.TranslateTransform(12, 10 + (h - 40) / 2f + g.MeasureString(yTitle, titleFont).Width / 2f);
            g.RotateTransform(-90);
            g.DrawString(yTitle, titleFont, titleBrush, 0, 0);
            g.Restore(state);
        }

        if (_points is null || _points.Count == 0)
        {
            using var brush = new SolidBrush(Color.Gray);
            using var font = new Font("Segoe UI", 8f);
            g.DrawString("Run an optimization to populate the Pareto front.",
                         font, brush, 50, h / 2 - 8);
            return;
        }

        // Compute scatter bounds across x = peak T and y = ΔP.
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        double mMin = double.MaxValue, mMax = double.MinValue;
        // SMD bounds (ignore NaN points).
        double smdMin = double.MaxValue, smdMax = double.MinValue;
        foreach (var p in _points)
        {
            if (p.PeakWallT_K < xMin) xMin = p.PeakWallT_K;
            if (p.PeakWallT_K > xMax) xMax = p.PeakWallT_K;
            if (p.CoolantDP_Pa < yMin) yMin = p.CoolantDP_Pa;
            if (p.CoolantDP_Pa > yMax) yMax = p.CoolantDP_Pa;
            if (p.Mass_g < mMin) mMin = p.Mass_g;
            if (p.Mass_g > mMax) mMax = p.Mass_g;
            if (!double.IsNaN(p.SMD_um))
            {
                if (p.SMD_um < smdMin) smdMin = p.SMD_um;
                if (p.SMD_um > smdMax) smdMax = p.SMD_um;
            }
        }
        if (xMax - xMin < 1e-3) xMax = xMin + 1;
        if (yMax - yMin < 1e-3) yMax = yMin + 1;
        double mRange   = System.Math.Max(mMax   - mMin,   1e-3);
        double smdRange = System.Math.Max(smdMax - smdMin, 1e-3);

        int plotX = 40, plotY = 10, plotW = w - 50, plotH = h - 40;

        // Axis labels — corners only, compact.
        using (var font = new Font("Segoe UI", 7.5f))
        using (var textBrush = new SolidBrush(Color.Black))
        {
            g.DrawString($"T {xMin:F0}K", font, textBrush, plotX - 25, plotY + plotH + 2);
            g.DrawString($"T {xMax:F0}K", font, textBrush, plotX + plotW - 40, plotY + plotH + 2);
            g.DrawString($"ΔP {yMin / 1e6:F1} MPa", font, textBrush, 2, plotY + plotH - 10);
            g.DrawString($"ΔP {yMax / 1e6:F1} MPa", font, textBrush, 2, plotY);
        }

        foreach (var p in _points)
        {
            double xn = (p.PeakWallT_K - xMin) / (xMax - xMin);
            double yn = (p.CoolantDP_Pa - yMin) / (yMax - yMin);
            int px = plotX + (int)(xn * plotW);
            int py = plotY + plotH - (int)(yn * plotH);

            // Colour source selected by _colorBy. Fall back to grey
            // for points missing SMD data so the user can see
            // characterisation coverage at a glance.
            int r, b;
            int alpha = 220;
            if (_colorBy == ParetoColorBy.SMD)
            {
                if (double.IsNaN(p.SMD_um))
                {
                    r = 130; b = 130; alpha = 140;   // grey-out
                }
                else
                {
                    double sn = (p.SMD_um - smdMin) / smdRange;
                    r = (int)(255 * sn);
                    b = 255 - r;
                }
            }
            else
            {
                double mn = (p.Mass_g - mMin) / mRange;
                r = (int)(255 * mn);
                b = 255 - r;
            }
            using (var dot = new SolidBrush(Color.FromArgb(alpha, r, 60, b)))
            {
                var rect = new Rectangle(px - 4, py - 4, 8, 8);
                g.FillEllipse(dot, rect);
                _hit.Add((new Rectangle(px - 6, py - 6, 12, 12), p));
            }
        }

        using (var font = new Font("Segoe UI", 7.5f))
        using (var textBrush = new SolidBrush(Color.Gray))
        {
            string legendTail = _colorBy == ParetoColorBy.SMD
                ? "colour = SMD (blue=fine, red=coarse)"
                : "colour = mass (blue=light, red=heavy)";
            g.DrawString($"{_points.Count} points · {legendTail}",
                         font, textBrush, plotX + 4, plotY + 4);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        foreach (var (rect, pt) in _hit)
            if (rect.Contains(e.Location))
            { PointSelected?.Invoke(pt); return; }
    }
}
