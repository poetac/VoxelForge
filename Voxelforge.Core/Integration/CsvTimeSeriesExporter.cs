// CsvTimeSeriesExporter.cs — Sprint SI.W15 export helper.
//
// Flattens a List<TimeHistorySnapshot> from TimeStepIntegrator.Run
// into a wide-format CSV. Column layout:
//
//   Time_s, comp1.port1, comp1.port2, ..., comp2.port1, ..., comp1.state1, ...
//
// Suitable for ingestion by Excel, pandas, gnuplot, R. The column set
// is fixed once at the first snapshot; subsequent snapshots are
// indexed by (component, port). Missing values render as empty string.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Voxelforge.Integration;

/// <summary>
/// CSV time-series exporter (Sprint SI.W15) for time-history snapshots
/// returned by <see cref="TimeStepIntegrator.Run"/>.
/// </summary>
internal static class CsvTimeSeriesExporter
{
    /// <summary>
    /// Render the history as a CSV string. The first row is the
    /// header; one data row per snapshot.
    /// </summary>
    public static string ToCsv(IReadOnlyList<TimeHistorySnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count == 0) return "Time_s\n";

        // 1. Determine column ordering from the first snapshot. Component and
        //    port/var names are sorted ordinally so the column layout is
        //    deterministic and culture-invariant regardless of the
        //    IReadOnlyDictionary iteration order (which the runtime does not
        //    guarantee and which can vary with upstream insertion order).
        var portColumns  = new List<(string Component, string Port)>();
        foreach (var componentName in history[0].PortValues.Keys.OrderBy(k => k, StringComparer.Ordinal))
            foreach (var portName in history[0].PortValues[componentName].Keys.OrderBy(k => k, StringComparer.Ordinal))
                portColumns.Add((componentName, portName));
        var stateColumns = new List<(string Component, string Var)>();
        foreach (var componentName in history[0].StateValues.Keys.OrderBy(k => k, StringComparer.Ordinal))
            foreach (var varName in history[0].StateValues[componentName].Keys.OrderBy(k => k, StringComparer.Ordinal))
                stateColumns.Add((componentName, varName));

        var sb = new StringBuilder();
        // Header.
        sb.Append("Time_s");
        foreach (var (c, p) in portColumns)
            sb.Append(',').Append(c).Append('.').Append(p);
        foreach (var (c, v) in stateColumns)
            sb.Append(',').Append("state.").Append(c).Append('.').Append(v);
        sb.Append('\n');

        // Rows.
        foreach (var snap in history)
        {
            sb.Append(snap.Time_s.ToString("G17", CultureInfo.InvariantCulture));
            foreach (var (componentName, portName) in portColumns)
            {
                sb.Append(',');
                if (snap.PortValues.TryGetValue(componentName, out var ports)
                 && ports.TryGetValue(portName, out var v))
                    sb.Append(v.ToString("G17", CultureInfo.InvariantCulture));
            }
            foreach (var (componentName, varName) in stateColumns)
            {
                sb.Append(',');
                if (snap.StateValues.TryGetValue(componentName, out var vars)
                 && vars.TryGetValue(varName, out var v))
                    sb.Append(v.ToString("G17", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Convenience: write the rendered CSV to a file on disk.
    /// </summary>
    public static void WriteToFile(
        IReadOnlyList<TimeHistorySnapshot> history, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        System.IO.File.WriteAllText(filePath, ToCsv(history));
    }
}
