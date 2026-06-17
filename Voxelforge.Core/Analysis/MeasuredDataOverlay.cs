// MeasuredDataOverlay.cs — Ingest a CSV of cold-flow or hot-fire test
// data, average the steady-state segment, overlay predicted vs
// measured on each panel, and compute error metrics + a simple
// single-knob calibration delta on Bartz scaling / film β / friction.
//
// Expected CSV columns (header row required, case-insensitive):
//   time_s
//   chamber_p_pa         (chamber pressure, Pa)
//   coolant_p_in_pa      (coolant inlet pressure, Pa)
//   coolant_p_out_pa     (coolant outlet pressure, Pa)
//   coolant_t_in_k       (coolant inlet T, K)
//   coolant_t_out_k      (coolant outlet T, K)
//   thrust_n             (thrust, N) — optional
//   wall_t_k             (embedded thermocouple peak, K) — optional
//   wall_t_station_<n>_k (per-station thermocouple, K) — optional;
//                        <n> = 0-based station index into
//                        RegenSolverOutputs.Stations. When present,
//                        enables the χ² goodness-of-fit metric against
//                        the per-station predicted wall T.
//
// Empty / unknown columns are ignored. Rows with any NaN in the required
// columns are skipped.
//
// Steady-state extraction:
//   The default policy averages the middle 50 % of the time span. Users
//   with short bursts or custom steady windows should edit the CSV or
//   use the explicit Evaluate overload.
//
// Calibration:
//   Simple 1-D grid search over bartzScalingFactor in [0.6, 1.4] minimising
//   the sum of squared residuals across (peak wall T, coolant ΔT,
//   coolant ΔP). This is intentionally simplistic — a real calibration
//   would use a Levenberg-Marquardt on multiple coefficients and a
//   physical prior. See `CalibrationNotes` in the returned record.
//
// Per-station goodness-of-fit extensions:
//   • Per-station thermocouple CSV columns — `wall_t_station_<n>_k`
//     where <n> matches the station index in
//     `RegenSolverOutputs.Stations`. Parser picks up any number of
//     them (up to the underlying contour's station count).
//   • χ² goodness-of-fit metric (`GoodnessOfFit.ChiSquaredReduced`).
//     Classical reduced χ² / ν where ν = #observations − 1 (one
//     fit parameter, the Bartz factor). < 2 is a good fit; > 4 is a
//     model-structure problem not fixable by tuning Bartz alone.
//   • Persistence: the calibrated Bartz factor writes back to the
//     design JSON via `DesignPersistence.SaveBartzOverlay` (new helper)
//     so subsequent Generate() calls load it as the starting point.

using System.Globalization;

namespace Voxelforge.Analysis;

public enum TestDataColumn
{
    Time_s,
    ChamberP_Pa,
    CoolantP_In_Pa,
    CoolantP_Out_Pa,
    CoolantT_In_K,
    CoolantT_Out_K,
    Thrust_N,
    WallT_K,
    TotalMassFlow_kgs,   // optional: total propellant flow — enables CStarEff × CfEff calibration
}

public sealed record TestDataSample(
    double Time_s,
    double ChamberP_Pa,
    double CoolantP_In_Pa,
    double CoolantP_Out_Pa,
    double CoolantT_In_K,
    double CoolantT_Out_K,
    double Thrust_N,           // NaN when column absent
    double WallT_K,            // NaN when column absent
    // Optional per-station thermocouple readings keyed by 0-based
    // station index. NaN values are skipped downstream. Empty dictionary
    // when no wall_t_station_<n>_k columns present.
    IReadOnlyDictionary<int, double>? WallTByStation = null,
    double TotalMassFlow_kgs = double.NaN); // NaN when column absent

public sealed record MeasuredSummary(
    int SampleCount,
    double ChamberP_Pa,
    double CoolantDP_Pa,
    double CoolantDT_K,
    double CoolantT_In_K,
    double CoolantT_Out_K,
    double Thrust_N,
    double WallT_K,
    // Per-station steady-state average of wall T where the CSV supplied
    // a wall_t_station_<n>_k column. Null when no per-station readings
    // were available.
    IReadOnlyDictionary<int, double>? WallTByStation = null,
    // Total propellant mass flow (kg/s). NaN when the CSV did not carry
    // a total_mass_flow_kgs column. When present, enables joint
    // CStarEfficiency × NozzleCfEfficiency calibration in
    // CalibrationPosterior.Calibrate.
    double TotalMassFlow_kgs = double.NaN);

/// <summary>
/// Goodness-of-fit metrics for the per-station wall-temperature
/// comparison. Populated only when the CSV carried
/// wall_t_station_&lt;n&gt;_k columns and
/// <see cref="MeasuredDataOverlay.BuildOverlay"/> was given a
/// per-station predicted vector.
/// </summary>
public sealed record GoodnessOfFit(
    int    ObservationCount,            // # per-station wall-T measurements
    double ChiSquared,                  // Σ((pred − meas) / σ)²
    double ChiSquaredReduced,           // χ² / ν with ν = n − 1 (one free param, Bartz)
    double WorstResidual_K,             // max |pred − meas| across stations
    int    WorstStationIndex,           // station index of the worst residual
    double RootMeanSquareError_K);      // sqrt(Σ(pred − meas)² / n)

public sealed record CalibrationResult(
    double BartzScalingFactor,     // best factor ∈ [0.6, 1.4]
    double SumSquaredResidualAt1,  // SSR at factor = 1.0
    double SumSquaredResidualAtBest,
    string CalibrationNotes);

public sealed record MeasuredOverlayResult(
    MeasuredSummary Measured,
    double Predicted_PeakWallT_K,
    double Predicted_CoolantDT_K,
    double Predicted_CoolantDP_Pa,
    double PercentError_PeakWallT,   // (pred − meas) / meas × 100
    double PercentError_CoolantDT,
    double PercentError_CoolantDP,
    CalibrationResult? Calibration,  // null when not requested
    string[] Warnings,
    // Per-station fit quality, populated when the CSV supplied
    // wall_t_station_<n>_k columns AND the caller passed a non-null
    // predictedWallTByStation into BuildOverlay.
    GoodnessOfFit? Fit = null);

public static class MeasuredDataOverlay
{
    /// <summary>
    /// Parse a CSV file into samples. Returns (samples, warnings).
    /// Lines starting with '#' are treated as comments.
    /// </summary>
    public static (List<TestDataSample> samples, List<string> warnings) ParseCsv(string path)
    {
        var samples = new List<TestDataSample>();
        var warnings = new List<string>();
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) { warnings.Add("CSV has no data rows."); return (samples, warnings); }

        string headerLine = lines[0];
        // Skip leading comments.
        int startRow = 1;
        while (startRow < lines.Length && lines[startRow].TrimStart().StartsWith('#')) startRow++;

        var headers = headerLine.Split(',');
        var idx = new Dictionary<TestDataColumn, int>();
        // `wall_t_station_<n>_k` column indices keyed by the 0-based
        // station number. Any number of these can appear in the CSV;
        // the parser collects them all.
        var stationColumns = new Dictionary<int, int>();
        for (int i = 0; i < headers.Length; i++)
        {
            string h = headers[i].Trim().ToLowerInvariant().Replace(" ", "_");
            switch (h)
            {
                case "time_s":                idx[TestDataColumn.Time_s]           = i; break;
                case "chamber_p_pa":          idx[TestDataColumn.ChamberP_Pa]      = i; break;
                case "coolant_p_in_pa":       idx[TestDataColumn.CoolantP_In_Pa]   = i; break;
                case "coolant_p_out_pa":      idx[TestDataColumn.CoolantP_Out_Pa]  = i; break;
                case "coolant_t_in_k":        idx[TestDataColumn.CoolantT_In_K]    = i; break;
                case "coolant_t_out_k":       idx[TestDataColumn.CoolantT_Out_K]   = i; break;
                case "thrust_n":              idx[TestDataColumn.Thrust_N]         = i; break;
                case "wall_t_k":              idx[TestDataColumn.WallT_K]          = i; break;
                case "total_mass_flow_kgs":   idx[TestDataColumn.TotalMassFlow_kgs] = i; break;
                default:
                    // Recognise wall_t_station_<n>_k where <n> is a
                    // non-negative integer.
                    if (h.StartsWith("wall_t_station_", StringComparison.Ordinal)
                        && h.EndsWith("_k", StringComparison.Ordinal))
                    {
                        int numStart = "wall_t_station_".Length;
                        int numEnd   = h.Length - "_k".Length;
                        if (numEnd > numStart
                            && int.TryParse(h.AsSpan(numStart, numEnd - numStart),
                                NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out int stationIdx)
                            && stationIdx >= 0)
                        {
                            stationColumns[stationIdx] = i;
                        }
                    }
                    break;
            }
        }

        if (!idx.ContainsKey(TestDataColumn.Time_s))
            warnings.Add("CSV missing time_s column — rows can't be ordered.");

        double Get(string[] cols, TestDataColumn c)
        {
            if (!idx.TryGetValue(c, out int i) || i >= cols.Length) return double.NaN;
            return double.TryParse(cols[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : double.NaN;
        }

        for (int r = startRow; r < lines.Length; r++)
        {
            var line = lines[r].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            var cols = line.Split(',');
            double pc = Get(cols, TestDataColumn.ChamberP_Pa);
            double tIn = Get(cols, TestDataColumn.CoolantT_In_K);
            double tOut = Get(cols, TestDataColumn.CoolantT_Out_K);
            if (double.IsNaN(pc) || double.IsNaN(tIn) || double.IsNaN(tOut))
                continue;   // skip rows without the minimum required fields

            Dictionary<int, double>? perStation = null;
            if (stationColumns.Count > 0)
            {
                perStation = new Dictionary<int, double>(stationColumns.Count);
                foreach (var (stationIdx, colIdx) in stationColumns)
                {
                    if (colIdx >= cols.Length) continue;
                    if (double.TryParse(cols[colIdx],
                            NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                        && !double.IsNaN(v))
                    {
                        perStation[stationIdx] = v;
                    }
                }
            }

            samples.Add(new TestDataSample(
                Time_s:             Get(cols, TestDataColumn.Time_s),
                ChamberP_Pa:        pc,
                CoolantP_In_Pa:     Get(cols, TestDataColumn.CoolantP_In_Pa),
                CoolantP_Out_Pa:    Get(cols, TestDataColumn.CoolantP_Out_Pa),
                CoolantT_In_K:      tIn,
                CoolantT_Out_K:     tOut,
                Thrust_N:           Get(cols, TestDataColumn.Thrust_N),
                WallT_K:            Get(cols, TestDataColumn.WallT_K),
                WallTByStation:     perStation,
                TotalMassFlow_kgs:  Get(cols, TestDataColumn.TotalMassFlow_kgs)));
        }

        if (samples.Count == 0)
            warnings.Add("No parseable rows found — check the header names match the CSV spec.");
        return (samples, warnings);
    }

    /// <summary>
    /// Summarise a set of samples over the middle 50 % of the time range.
    /// When <paramref name="samples"/> lack a time column the full span is used.
    /// </summary>
    public static MeasuredSummary Summarise(List<TestDataSample> samples)
    {
        if (samples.Count == 0)
            return new MeasuredSummary(0, 0, 0, 0, 0, 0, double.NaN, double.NaN);

        // Sort by time when present, otherwise preserve input order.
        bool hasTime = samples.Exists(s => !double.IsNaN(s.Time_s));
        var sorted = hasTime
            ? samples.OrderBy(s => s.Time_s).ToList()
            : samples;

        int lo = (int)(sorted.Count * 0.25);
        int hi = (int)(sorted.Count * 0.75);
        if (hi <= lo) { lo = 0; hi = sorted.Count; }

        double pc = 0, pIn = 0, pOut = 0, tIn = 0, tOut = 0, thrust = 0, wallT = 0, mDot = 0;
        int thrustN = 0, wallN = 0, mDotN = 0;
        // Per-station steady-state accumulator.
        var stationSum   = new Dictionary<int, double>();
        var stationCount = new Dictionary<int, int>();
        for (int i = lo; i < hi; i++)
        {
            var s = sorted[i];
            pc += s.ChamberP_Pa;
            pIn += s.CoolantP_In_Pa;
            pOut += s.CoolantP_Out_Pa;
            tIn += s.CoolantT_In_K;
            tOut += s.CoolantT_Out_K;
            if (!double.IsNaN(s.Thrust_N))           { thrust += s.Thrust_N;           thrustN++; }
            if (!double.IsNaN(s.WallT_K))             { wallT  += s.WallT_K;             wallN++;   }
            if (!double.IsNaN(s.TotalMassFlow_kgs))   { mDot   += s.TotalMassFlow_kgs;   mDotN++;   }
            if (s.WallTByStation is { Count: > 0 } perStation)
            {
                foreach (var (stationIdx, value) in perStation)
                {
                    if (double.IsNaN(value)) continue;
                    stationSum.TryGetValue(stationIdx, out double cur);
                    stationSum[stationIdx]   = cur + value;
                    stationCount.TryGetValue(stationIdx, out int n2);
                    stationCount[stationIdx] = n2 + 1;
                }
            }
        }
        int n = hi - lo;

        IReadOnlyDictionary<int, double>? stationAvg = null;
        if (stationSum.Count > 0)
        {
            var avg = new Dictionary<int, double>(stationSum.Count);
            foreach (var (stationIdx, sum) in stationSum)
            {
                if (stationCount[stationIdx] > 0)
                    avg[stationIdx] = sum / stationCount[stationIdx];
            }
            stationAvg = avg;
        }

        return new MeasuredSummary(
            SampleCount:      n,
            ChamberP_Pa:      pc  / n,
            CoolantDP_Pa:     (pIn - pOut) / n,
            CoolantDT_K:      (tOut - tIn) / n,
            CoolantT_In_K:    tIn  / n,
            CoolantT_Out_K:   tOut / n,
            Thrust_N:         thrustN > 0 ? thrust / thrustN : double.NaN,
            WallT_K:          wallN   > 0 ? wallT  / wallN   : double.NaN,
            WallTByStation:   stationAvg,
            TotalMassFlow_kgs: mDotN > 0 ? mDot / mDotN : double.NaN);
    }

    /// <summary>
    /// Compute a reduced-χ² goodness-of-fit against per-station wall-T
    /// readings. Uses an assumed measurement uncertainty σ per station;
    /// default 20 K covers typical type-K thermocouple error plus
    /// thermal-mass-response drift. Returns null when no overlapping
    /// (measured, predicted) pairs are found.
    /// </summary>
    public static GoodnessOfFit? ComputeGoodnessOfFit(
        IReadOnlyDictionary<int, double>       measuredByStation,
        IReadOnlyDictionary<int, double>       predictedByStation,
        double sigma_K = 20.0)
    {
        if (measuredByStation  is null || measuredByStation.Count  == 0) return null;
        if (predictedByStation is null || predictedByStation.Count == 0) return null;
        double sigma = Math.Max(sigma_K, 1e-6);
        double chi2 = 0, sse = 0;
        double worst = 0;
        int worstIdx = -1;
        int obs = 0;
        foreach (var (stationIdx, measured) in measuredByStation)
        {
            if (double.IsNaN(measured)) continue;
            if (!predictedByStation.TryGetValue(stationIdx, out double predicted)) continue;
            if (double.IsNaN(predicted)) continue;
            double resid = predicted - measured;
            chi2 += (resid * resid) / (sigma * sigma);
            sse  += resid * resid;
            double absResid = Math.Abs(resid);
            if (absResid > worst) { worst = absResid; worstIdx = stationIdx; }
            obs++;
        }
        if (obs == 0) return null;
        // Reduced χ² with ν = n − 1 (one fit parameter, the Bartz
        // scaling factor). Guard against n = 1 by reporting raw χ².
        double reduced = obs > 1 ? chi2 / (obs - 1) : chi2;
        return new GoodnessOfFit(
            ObservationCount:      obs,
            ChiSquared:            chi2,
            ChiSquaredReduced:     reduced,
            WorstResidual_K:       worst,
            WorstStationIndex:     worstIdx,
            RootMeanSquareError_K: Math.Sqrt(sse / obs));
    }

    /// <summary>
    /// Build the overlay record given a summary and a generated result.
    /// When <paramref name="runCalibration"/> is true, a simple grid search
    /// over the Bartz scaling factor is run to minimise combined L² error.
    /// Calibration requires a runner callback that re-solves the design
    /// at a given Bartz factor and returns the key scalars.
    /// </summary>
    public static MeasuredOverlayResult BuildOverlay(
        MeasuredSummary measured,
        double predicted_PeakWallT_K,
        double predicted_CoolantDT_K,
        double predicted_CoolantDP_Pa,
        Func<double, (double wallT, double dT, double dP)>? calibrationRunner = null,
        // Optional per-station predicted wall temperatures. When both
        // sides are populated and non-empty,
        // <see cref="MeasuredOverlayResult.Fit"/> is computed via
        // <see cref="ComputeGoodnessOfFit"/>. Null (default) preserves
        // legacy behaviour bit-identical.
        IReadOnlyDictionary<int, double>? predictedWallTByStation = null,
        double perStationSigma_K = 20.0)
    {
        var warnings = new List<string>();
        double ePeak = Percent(predicted_PeakWallT_K, measured.WallT_K);
        double eDT   = Percent(predicted_CoolantDT_K, measured.CoolantDT_K);
        double eDP   = Percent(predicted_CoolantDP_Pa, measured.CoolantDP_Pa);

        if (double.IsNaN(measured.WallT_K))
            warnings.Add("No wall_t_k column in CSV — peak-wall-T overlay omitted.");

        CalibrationResult? cal = null;
        if (calibrationRunner is not null)
            cal = Calibrate(measured, calibrationRunner);

        GoodnessOfFit? fit = null;
        if (measured.WallTByStation is { Count: > 0 } measuredStations
            && predictedWallTByStation is { Count: > 0 } predictedStations)
        {
            fit = ComputeGoodnessOfFit(measuredStations, predictedStations, perStationSigma_K);
            if (fit is not null)
            {
                if (fit.ChiSquaredReduced > 4.0)
                    warnings.Add($"Reduced χ² = {fit.ChiSquaredReduced:F2} > 4 — model "
                               + $"structure may be missing a mechanism; tuning Bartz alone "
                               + $"won't close the gap. Worst residual {fit.WorstResidual_K:F0} K "
                               + $"at station {fit.WorstStationIndex}.");
                else if (fit.ChiSquaredReduced > 2.0)
                    warnings.Add($"Reduced χ² = {fit.ChiSquaredReduced:F2} — "
                               + $"marginal fit; calibrated Bartz factor may help.");
            }
        }

        return new MeasuredOverlayResult(
            Measured: measured,
            Predicted_PeakWallT_K:   predicted_PeakWallT_K,
            Predicted_CoolantDT_K:   predicted_CoolantDT_K,
            Predicted_CoolantDP_Pa:  predicted_CoolantDP_Pa,
            PercentError_PeakWallT:  ePeak,
            PercentError_CoolantDT:  eDT,
            PercentError_CoolantDP:  eDP,
            Calibration: cal,
            Warnings: warnings.ToArray(),
            Fit: fit);
    }

    /// <summary>
    /// 21-point grid search of Bartz factor over [0.6, 1.4]. Returns the
    /// factor that minimises a normalised sum-squared residual across
    /// whichever of peak T / ΔT / ΔP the measurement carries. Fast: relies
    /// on the caller having a cheap re-eval runner (no voxel build).
    /// </summary>
    private static CalibrationResult Calibrate(
        MeasuredSummary measured,
        Func<double, (double wallT, double dT, double dP)> runner)
    {
        double Ssr(double factor)
        {
            var (wallT, dT, dP) = runner(factor);
            double ssr = 0;
            if (!double.IsNaN(measured.WallT_K) && measured.WallT_K > 0)
            {
                double r = (wallT - measured.WallT_K) / measured.WallT_K;
                ssr += r * r;
            }
            if (measured.CoolantDT_K > 0)
            {
                double r = (dT - measured.CoolantDT_K) / measured.CoolantDT_K;
                ssr += r * r;
            }
            if (measured.CoolantDP_Pa > 0)
            {
                double r = (dP - measured.CoolantDP_Pa) / measured.CoolantDP_Pa;
                ssr += r * r;
            }
            return ssr;
        }

        double ssrAt1 = Ssr(1.0);
        double bestFactor = 1.0;
        double bestSsr = ssrAt1;
        for (int i = 0; i <= 20; i++)
        {
            double f = 0.6 + 0.04 * i;   // 0.60 … 1.40 step 0.04
            double s = Ssr(f);
            if (s < bestSsr) { bestSsr = s; bestFactor = f; }
        }

        string notes = bestFactor > 1.0
            ? $"Model under-predicts heat load by {(bestFactor - 1.0) * 100:F0} % — boost BartzScalingFactor."
            : bestFactor < 1.0
                ? $"Model over-predicts heat load by {(1.0 - bestFactor) * 100:F0} % — damp BartzScalingFactor."
                : "Model matches measurement within the grid resolution (±2 %).";

        return new CalibrationResult(
            BartzScalingFactor:         bestFactor,
            SumSquaredResidualAt1:      ssrAt1,
            SumSquaredResidualAtBest:   bestSsr,
            CalibrationNotes:           notes);
    }

    private static double Percent(double predicted, double measured)
    {
        if (double.IsNaN(measured) || measured == 0) return double.NaN;
        return (predicted - measured) / measured * 100.0;
    }
}
