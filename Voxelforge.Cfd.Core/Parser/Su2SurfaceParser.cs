// Su2SurfaceParser.cs — Parses SU2 surface output CSV to extract per-station
// adiabatic wall temperatures for CalibrationPosterior wiring.
//
// SU2 v8 writes surface quantities to "surface_flow_0.csv" (preferred) with
// a fallback to "surface_flow.csv". The file contains per-node values at the
// named MARKER_MONITORING boundary (the "wall" marker).
//
// With an adiabatic BC (MARKER_HEATFLUX= wall, 0.0), SU2's "Temperature" column
// at wall nodes equals T_aw — the adiabatic (recovery) wall temperature. Axis
// nodes (y < 1e-9 m) are filtered out.
//
// Station mapping: node x-coordinate (metres) → x_mm → ChamberContour.StationAt().
// When multiple nodes map to the same station, the maximum T is kept.

using System.Globalization;
using Voxelforge.Chamber;

namespace Voxelforge.Cfd.Parser;

/// <summary>
/// Per-station adiabatic wall temperature profile parsed from a SU2 surface output file.
/// </summary>
public sealed record Su2WallProfile(
    /// <summary>True when the runner reported convergence for this run.</summary>
    bool Converged,
    /// <summary>Maximum T_aw across all non-axis wall nodes (K). NaN when no nodes found.</summary>
    double PeakAdiabaticWallTemp_K,
    /// <summary>Per-station maximum T_aw keyed by <see cref="ChamberContour.StationAt"/> index.</summary>
    IReadOnlyDictionary<int, double> AdiabaticWallTempByStation,
    /// <summary>Total non-axis wall nodes parsed (0 means the surface file had no wall data).</summary>
    int NodeCount,
    /// <summary>Non-fatal parse warnings (empty string when all is well).</summary>
    string ParseWarnings);

/// <summary>
/// Parses SU2 surface output files to extract per-station adiabatic wall temperatures.
/// </summary>
public static class Su2SurfaceParser
{
    // y < AxisThreshold_m is treated as an axis (symmetry) node and excluded.
    private const double AxisThreshold_m = 1e-9;

    /// <summary>
    /// Locates and parses the SU2 surface output file from <paramref name="outputDirectory"/>.
    /// Tries <c>surface_flow_0.csv</c> first, then <c>surface_flow.csv</c>.
    /// </summary>
    /// <param name="outputDirectory">Directory where SU2 wrote its output files.</param>
    /// <param name="contour">Nozzle contour used for x → station-index mapping.</param>
    /// <param name="converged">Convergence flag from the SU2 run result.</param>
    /// <exception cref="FileNotFoundException">Neither surface CSV candidate was found.</exception>
    public static Su2WallProfile Parse(
        string outputDirectory,
        ChamberContour contour,
        bool converged)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(contour);

        string primary  = Path.Combine(outputDirectory, "surface_flow_0.csv");
        string fallback = Path.Combine(outputDirectory, "surface_flow.csv");

        string csvPath = File.Exists(primary)  ? primary  :
                         File.Exists(fallback) ? fallback :
                         throw new FileNotFoundException(
                             $"SU2 surface output not found in '{outputDirectory}'. " +
                             "Expected 'surface_flow_0.csv' or 'surface_flow.csv'.");

        return ParseFile(csvPath, contour, converged);
    }

    /// <summary>
    /// Parses a specific SU2 surface CSV file.
    /// </summary>
    /// <param name="csvPath">Path to the surface output CSV.</param>
    /// <param name="contour">Nozzle contour used for x → station-index mapping.</param>
    /// <param name="converged">Convergence flag from the SU2 run result.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the CSV does not contain a recognisable Temperature column.
    /// </exception>
    public static Su2WallProfile ParseFile(
        string csvPath,
        ChamberContour contour,
        bool converged)
    {
        ArgumentNullException.ThrowIfNull(csvPath);
        ArgumentNullException.ThrowIfNull(contour);

        var ci = CultureInfo.InvariantCulture;
        var stationMap = new Dictionary<int, double>();
        int nodeCount  = 0;
        var warnings   = new List<string>();

        string[] lines = File.ReadAllLines(csvPath);

        // Find first non-comment, non-empty line as header
        int headerIdx = -1;
        for (int li = 0; li < lines.Length; li++)
        {
            string trimmed = lines[li].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('%') || trimmed.StartsWith('#'))
                continue;
            headerIdx = li;
            break;
        }

        if (headerIdx < 0)
            throw new InvalidOperationException($"Surface CSV '{csvPath}' contains no data rows.");

        // Parse header: normalize tokens to lowercase with underscores
        string[] headers = lines[headerIdx].Split(',');
        string[] normHeaders = headers.Select(NormalizeHeader).ToArray();

        int xCol = FindColumn(normHeaders, "x_coordinate", "x");
        int yCol = FindColumn(normHeaders, "y_coordinate", "y");
        int tCol = FindColumn(normHeaders, "temperature");

        if (tCol < 0)
            throw new InvalidOperationException(
                $"Surface CSV '{csvPath}' does not contain a 'Temperature' column. " +
                $"Found headers: {string.Join(", ", headers.Select(h => h.Trim()))}");

        if (xCol < 0) warnings.Add("x-coordinate column not found; using column 0 as x fallback.");
        if (yCol < 0) warnings.Add("y-coordinate column not found; axis filtering disabled.");

        // Parse data rows
        for (int li = headerIdx + 1; li < lines.Length; li++)
        {
            string line = lines[li].Trim();
            if (line.Length == 0 || line.StartsWith('%') || line.StartsWith('#'))
                continue;

            string[] fields = line.Split(',');
            if (fields.Length <= tCol) continue;

            double xM = xCol >= 0 && xCol < fields.Length
                && double.TryParse(fields[xCol].Trim(), NumberStyles.Float, ci, out double xv) ? xv : double.NaN;
            double yM = yCol >= 0 && yCol < fields.Length
                && double.TryParse(fields[yCol].Trim(), NumberStyles.Float, ci, out double yv) ? yv : double.NaN;

            if (!double.TryParse(fields[tCol].Trim(), NumberStyles.Float, ci, out double tK))
                continue;

            // Filter axis nodes (symmetry line at r=0)
            if (!double.IsNaN(yM) && yM < AxisThreshold_m)
                continue;

            // Map x-coordinate to station index
            if (!double.IsNaN(xM))
            {
                double xMm = xM * 1000.0;
                int stIdx = contour.StationAt(xMm);

                if (!stationMap.TryGetValue(stIdx, out double existing) || tK > existing)
                    stationMap[stIdx] = tK;
            }

            nodeCount++;
        }

        if (nodeCount == 0)
            warnings.Add("No non-axis wall nodes found in surface CSV.");

        double peak = stationMap.Count > 0
            ? stationMap.Values.Max()
            : double.NaN;

        return new Su2WallProfile(
            Converged:                  converged,
            PeakAdiabaticWallTemp_K:    peak,
            AdiabaticWallTempByStation: stationMap,
            NodeCount:                  nodeCount,
            ParseWarnings:              string.Join("; ", warnings));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string NormalizeHeader(string raw)
        => raw.Trim().Trim('"').ToLowerInvariant()
               .Replace('-', '_').Replace(' ', '_');

    private static int FindColumn(string[] normalized, params string[] candidates)
    {
        foreach (string candidate in candidates)
            for (int i = 0; i < normalized.Length; i++)
                if (normalized[i] == candidate) return i;
        return -1;
    }
}
