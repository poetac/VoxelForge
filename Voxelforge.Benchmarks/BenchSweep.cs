// --sweep — 1D parameter sweep over any registered SA design variable or
// supported operating condition. Evaluates N evenly-spaced sample points
// in pure-physics mode (no voxels) and emits a CSV + PNG artifact.
//
// CLI:
//   --sweep --preset <name> --variable <var> --range <lo,hi>
//           [--samples <N=20>]
//           [--objective score|peak_wall_t|coolant_dp|mass|min_sf|coolant_t_out|isp]
//           [--out <path.csv>]   (defaults to current/sweep-{preset}-{var}-{date}.csv)
//
// Supported variables:
//   Any MemberName from DesignVariableRegistry (SA design variables).
//   Condition shorthands: p_c (ChamberPressure_Pa), thrust (Thrust_N).
//
// Example:
//   dotnet run --project Voxelforge.Benchmarks -- --sweep \
//     --preset merlin --variable p_c --range 2e6,8e6 --samples 30 \
//     --objective isp
//
// The PNG is written to the same directory with .png extension;
// workflow artifact upload should glob the containing directory.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static class BenchSweep
{
    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --sweep "
      + "--preset <merlin|rl10|pressure-fed-small|aerospike|pintle> "
      + "--variable <member-name|p_c|thrust> "
      + "--range <lo,hi> "
      + "[--samples <N=20>] "
      + "[--objective score|peak_wall_t|coolant_dp|mass|min_sf|coolant_t_out|isp] "
      + "[--out <path.csv>]";

    // Valid --objective values.
    private static readonly string[] KnownObjectives =
        { "score", "peak_wall_t", "coolant_dp", "mass", "min_sf", "coolant_t_out", "isp" };

    // Condition variables supported as shorthands (not in SA registry).
    private static readonly Dictionary<string, Func<OperatingConditions, double, OperatingConditions>>
        ConditionSetters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["p_c"]    = static (c, v) => c with { ChamberPressure_Pa = v },
            ["thrust"] = static (c, v) => c with { Thrust_N = v },
        };

    public static int Run(string[] args)
    {
        string? presetName    = null;
        string? variableName  = null;
        string? rangeStr      = null;
        int     samples       = 20;
        string  objective     = "score";
        string? outPath       = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset":
                    if (i + 1 >= args.Length) { Error("--preset missing value"); return 3; }
                    presetName = args[++i];
                    break;
                case "--variable":
                    if (i + 1 >= args.Length) { Error("--variable missing value"); return 3; }
                    variableName = args[++i];
                    break;
                case "--range":
                    if (i + 1 >= args.Length) { Error("--range missing value"); return 3; }
                    rangeStr = args[++i];
                    break;
                case "--samples":
                    if (i + 1 >= args.Length) { Error("--samples missing value"); return 3; }
                    if (!int.TryParse(args[++i], out samples) || samples < 2 || samples > 1000)
                    { Error($"--samples must be 2..1000, got '{args[i]}'"); return 3; }
                    break;
                case "--objective":
                    if (i + 1 >= args.Length) { Error("--objective missing value"); return 3; }
                    objective = args[++i];
                    if (!Array.Exists(KnownObjectives, o => o.Equals(objective, StringComparison.OrdinalIgnoreCase)))
                    { Error($"--objective must be one of: {string.Join(", ", KnownObjectives)}"); return 3; }
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Error("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                default:
                    Error($"Unknown argument '{args[i]}'. {UsageLine}"); return 3;
            }
        }

        if (presetName is null)   { Error("--preset required"); return 3; }
        if (variableName is null) { Error("--variable required"); return 3; }
        if (rangeStr is null)     { Error("--range required"); return 3; }

        // Parse range
        var c = CultureInfo.InvariantCulture;
        var rangeParts = rangeStr.Split(',');
        if (rangeParts.Length != 2
            || !double.TryParse(rangeParts[0], NumberStyles.Float, c, out double lo)
            || !double.TryParse(rangeParts[1], NumberStyles.Float, c, out double hi)
            || lo >= hi)
        {
            Error($"--range must be 'lo,hi' with lo < hi (InvariantCulture), got '{rangeStr}'");
            return 3;
        }

        // Resolve preset
        CanonicalDesigns.Preset preset;
        try { preset = CanonicalDesigns.Get(presetName); }
        catch (ArgumentException ex) { Error(ex.Message); return 3; }

        // Resolve variable — SA registry first, then condition shorthands
        var allDescriptors = DesignVariableRegistry.DescriptorsForMany(
            typeof(RegenChamberDesign), typeof(InjectorPattern));

        SaDesignVariableDescriptor? desc = allDescriptors.FirstOrDefault(
            d => d.MemberName.Equals(variableName, StringComparison.OrdinalIgnoreCase));

        Func<OperatingConditions, double, OperatingConditions>? conditionSetter = null;
        if (desc is null && !ConditionSetters.TryGetValue(variableName, out conditionSetter))
        {
            var saNames = string.Join(", ", allDescriptors.Select(d => d.MemberName));
            Error($"Unknown variable '{variableName}'.\n"
                + $"  SA design variables: {saNames}\n"
                + $"  Condition shorthands: {string.Join(", ", ConditionSetters.Keys)}");
            return 3;
        }

        // Default output path
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", c);
        outPath ??= $"current/sweep-{preset.Name}-{variableName}-{date}.csv";

        string? outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (outDir is not null) Directory.CreateDirectory(outDir);

        // Generate evenly-spaced sample values
        double[] values = Enumerable.Range(0, samples)
            .Select(i => lo + (hi - lo) * i / (samples - 1))
            .ToArray();

        // Pack baseline params once (only used for SA variable sweeps)
        double[]? baseParams = desc is not null
            ? RegenChamberOptimization.Pack(preset.Seed.Design)
            : null;

        // Pre-flight: reject a design variable that the selected preset's
        // baseline gates off. Some descriptors only apply when a categorical
        // baseline field is set (injector pattern present, TPMS / aerospike
        // topology); for a preset whose baseline doesn't satisfy the gate,
        // RegenChamberOptimization.Unpack silently drops the swept value and
        // the run would emit a flat, misleading "no-effect" curve with exit 0
        // (#852). Fail fast instead of producing a corrupt experiment.
        if (desc is not null
            && !DesignVariableBinder.IsGateSatisfied(desc.Gate, preset.Seed.Design))
        {
            Error($"Design variable '{variableName}' is gated off for preset '{preset.Name}': "
                + $"its gate ({desc.Gate}) is not satisfied by the preset's baseline design, so "
                + $"RegenChamberOptimization.Unpack would silently ignore the swept value and "
                + $"produce a flat, misleading curve. Choose a preset whose baseline enables this "
                + $"variable, or a different --variable.");
            return 3;
        }

        Console.WriteLine($"# Sweep: preset={preset.Name}  variable={variableName}"
                        + $"  range=[{lo:G4},{hi:G4}]  samples={samples}  objective={objective}");
        Console.WriteLine($"# Output CSV: {outPath}");

        var results = new List<SweepPoint>(samples);

        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            try
            {
                OperatingConditions conditions = preset.Seed.Conditions;
                RegenChamberDesign  design     = preset.Seed.Design;

                if (desc is not null && baseParams is not null)
                {
                    // SA design variable: clone params, set index, unpack
                    double[] p = (double[])baseParams.Clone();
                    p[desc.Index] = v;
                    design = RegenChamberOptimization.Unpack(p, preset.Seed.Design);
                }
                else if (conditionSetter is not null)
                {
                    // Operating condition: modify conditions record
                    conditions = conditionSetter(preset.Seed.Conditions, v);
                }

                var gen   = RegenChamberOptimization.GenerateWith(
                    conditions, design,
                    skipVoxelGeometry: true, skipMfgAnalysis: true);
                var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

                double objValue  = ExtractObjective(objective, score, gen, conditions);
                bool   feasible  = !double.IsPositiveInfinity(score.TotalScore);
                int    violations = score.FeasibilityViolations.Length;

                results.Add(new SweepPoint(v, objValue, feasible, violations, null));
                Console.WriteLine($"# [{i + 1,3}/{samples}] {variableName}={v,12:G6}  "
                                + $"{objective}={objValue,10:G4}  feasible={feasible}  violations={violations}");
            }
            catch (Exception ex)
            {
                results.Add(new SweepPoint(v, double.NaN, false, -1, ex.Message));
                Console.WriteLine($"# [{i + 1,3}/{samples}] {variableName}={v,12:G6}  ERROR: {ex.Message}");
            }
        }

        WriteCsv(outPath, variableName, objective, results, c);

        string pngPath = Path.ChangeExtension(outPath, ".png");
        WritePng(pngPath, preset.Name, variableName, objective, results, c);

        Console.WriteLine($"# PNG: {pngPath}");
        return 0;
    }

    // ── Objective extraction ──────────────────────────────────────────────────

    private static double ExtractObjective(string objective, RegenScoreResult score,
                                           RegenGenerationResult gen, OperatingConditions conditions)
        => objective.ToLowerInvariant() switch
        {
            "score"        => score.TotalScore,
            "peak_wall_t"  => score.PeakWallT_K,
            "coolant_dp"   => score.CoolantDP_Pa,
            "mass"         => score.Mass_g,
            "min_sf"       => score.MinSafetyFactor,
            "coolant_t_out"=> score.CoolantTOut_K,
            "isp"          => ComputeIsp(gen, conditions),
            _              => throw new ArgumentException($"Unknown objective '{objective}'")
        };

    private static double ComputeIsp(RegenGenerationResult gen, OperatingConditions conditions)
    {
        double totalMdot = gen.Derived.FuelMassFlow_kgs + gen.Derived.OxidizerMassFlow_kgs;
        if (totalMdot <= 0) return double.NaN;
        return conditions.Thrust_N / (totalMdot * 9.80665);
    }

    // ── CSV output ────────────────────────────────────────────────────────────

    private static void WriteCsv(string path, string variableName, string objective,
                                  IReadOnlyList<SweepPoint> results, CultureInfo c)
    {
        using var w = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
        w.WriteLine($"variable_value,objective_{objective},feasible,feasibility_violation_count");
        foreach (var pt in results)
        {
            w.WriteLine(
                $"{pt.Value.ToString("G6", c)},"
              + $"{pt.ObjValue.ToString("G6", c)},"
              + $"{(pt.Feasible ? 1 : 0)},"
              + $"{pt.Violations}");
        }
    }

    // ── PNG plot ──────────────────────────────────────────────────────────────

    private static void WritePng(string pngPath, string preset, string variableName,
                                  string objective, IReadOnlyList<SweepPoint> results,
                                  CultureInfo c)
    {
        const int W = 800, H = 500;
        const int ML = 90, MR = 30, MT = 50, MB = 65;
        int plotW = W - ML - MR;
        int plotH = H - MT - MB;

        // Axis ranges
        double xMin = results.Min(r => r.Value);
        double xMax = results.Max(r => r.Value);
        if (Math.Abs(xMax - xMin) < 1e-15) xMax = xMin + 1;

        var finite = results.Where(r => double.IsFinite(r.ObjValue)).ToList();
        double yMin = finite.Count > 0 ? finite.Min(r => r.ObjValue) : 0;
        double yMax = finite.Count > 0 ? finite.Max(r => r.ObjValue) : 1;
        if (Math.Abs(yMax - yMin) < 1e-15) { yMin -= 0.5; yMax += 0.5; }
        double yPad = (yMax - yMin) * 0.08;
        yMin -= yPad;
        yMax += yPad;

        float ToX(double v) => ML + (float)((v - xMin) / (xMax - xMin) * plotW);
        float ToY(double v) => MT + plotH - (float)((v - yMin) / (yMax - yMin) * plotH);

        using var bmp = new Bitmap(W, H);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.White);

        using var gridPen      = new Pen(Color.FromArgb(210, 210, 210), 1f);
        using var axisPen      = new Pen(Color.Black, 1.5f);
        using var feasiblePen  = new Pen(Color.RoyalBlue, 2f);
        using var labelFont    = new Font("Arial", 8.5f);
        using var titleFont    = new Font("Arial", 10.5f, FontStyle.Bold);
        using var axisFont     = new Font("Arial", 9f);
        using var blackBrush   = new SolidBrush(Color.Black);
        using var feasBrush    = new SolidBrush(Color.RoyalBlue);
        using var infeasBrush  = new SolidBrush(Color.Tomato);

        // Title
        string title = $"Sweep: {preset} — {variableName} vs {objective}";
        using var titleSf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(title, titleFont, blackBrush, W / 2f, 12, titleSf);

        // Y grid + ticks (6 divisions)
        for (int t = 0; t <= 6; t++)
        {
            double yv = yMin + (yMax - yMin) * t / 6;
            float  yp = ToY(yv);
            g.DrawLine(gridPen, ML, yp, ML + plotW, yp);
            string label = FormatAxisValue(yv);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(label, labelFont, blackBrush, ML - 4, yp - 7, sf);
        }

        // X grid + ticks (5 divisions)
        for (int t = 0; t <= 5; t++)
        {
            double xv = xMin + (xMax - xMin) * t / 5;
            float  xp = ToX(xv);
            g.DrawLine(gridPen, xp, MT, xp, MT + plotH);
            string label = FormatAxisValue(xv);
            using var sf = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(label, labelFont, blackBrush, xp, MT + plotH + 4, sf);
        }

        // Axes
        g.DrawLine(axisPen, ML, MT, ML, MT + plotH);
        g.DrawLine(axisPen, ML, MT + plotH, ML + plotW, MT + plotH);

        // Connecting line through feasible points
        var feasPts = results
            .Where(r => r.Feasible && double.IsFinite(r.ObjValue))
            .OrderBy(r => r.Value)
            .Select(r => new PointF(ToX(r.Value), ToY(r.ObjValue)))
            .ToArray();
        if (feasPts.Length >= 2) g.DrawLines(feasiblePen, feasPts);

        // Individual points
        const float R = 4f;
        foreach (var pt in results)
        {
            if (!double.IsFinite(pt.ObjValue)) continue;
            float px = ToX(pt.Value), py = ToY(pt.ObjValue);
            var b = pt.Feasible ? feasBrush : infeasBrush;
            g.FillEllipse(b, px - R, py - R, 2 * R, 2 * R);
        }

        // X-axis label (bottom centre)
        using var xSf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(variableName, axisFont, blackBrush, ML + plotW / 2f, H - 18, xSf);

        // Y-axis label (rotated, left side)
        g.TranslateTransform(14, MT + plotH / 2f);
        g.RotateTransform(-90);
        using var ySf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(objective, axisFont, blackBrush, 0, 0, ySf);
        g.ResetTransform();

        // Legend (top right)
        int lx = ML + plotW - 120, ly = MT + 8;
        g.FillEllipse(feasBrush,  lx, ly,      8, 8); g.DrawString("feasible",   labelFont, blackBrush, lx + 12, ly - 1);
        g.FillEllipse(infeasBrush, lx, ly + 16, 8, 8); g.DrawString("infeasible", labelFont, blackBrush, lx + 12, ly + 15);

        bmp.Save(pngPath, ImageFormat.Png);
    }

    // Format an axis tick value concisely.
    private static string FormatAxisValue(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "∞";
        double abs = Math.Abs(v);
        if (abs == 0) return "0";
        if (abs >= 1e6 || abs < 1e-3) return v.ToString("G3", CultureInfo.InvariantCulture);
        return v.ToString("G4",  CultureInfo.InvariantCulture);
    }

    private static void Error(string msg) => Console.Error.WriteLine(msg);

    private sealed record SweepPoint(
        double  Value,
        double  ObjValue,
        bool    Feasible,
        int     Violations,
        string? ErrorMessage);
}
