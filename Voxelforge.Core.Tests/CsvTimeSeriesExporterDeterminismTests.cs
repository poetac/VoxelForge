// CsvTimeSeriesExporterDeterminismTests.cs — regression guard for the CSV
// column-order determinism bug (red-team finding). PicoGK-free → runs on the
// Linux CI 'core' leg.
//
// CsvTimeSeriesExporter.ToCsv derived its column layout by enumerating the
// snapshot's IReadOnlyDictionary maps directly. Dictionary enumeration order is
// not a guaranteed contract (it tracks insertion order in the current runtime,
// which itself depends on upstream component/port registration order), so the
// emitted column order — and therefore every exported file's schema — was not
// guaranteed stable across runs/hosts. The exporter now sorts component and
// port/var names ordinally. This test feeds deliberately scrambled insertion
// order and pins the fully-sorted header; it fails on the old code.

using System.Collections.Generic;
using Voxelforge.Integration;

namespace Voxelforge.Core.Tests;

public sealed class CsvTimeSeriesExporterDeterminismTests
{
    // Insertion order is deliberately NON-sorted (zeta before alpha; q before a;
    // p before b; s2 before s1) so a pass requires the exporter to sort.
    private static TimeHistorySnapshot Snapshot(double t)
    {
        var ports = new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["zeta"]  = new Dictionary<string, double> { ["q"] = 1.0, ["a"] = 2.0 },
            ["alpha"] = new Dictionary<string, double> { ["p"] = 3.0, ["b"] = 4.0 },
        };
        var states = new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["yankee"] = new Dictionary<string, double> { ["s2"] = 5.0, ["s1"] = 6.0 },
        };
        return new TimeHistorySnapshot(t, ports, states);
    }

    [Fact]
    public void ToCsv_HeaderColumns_AreOrdinallySorted_NotDictionaryOrder()
    {
        var hist = new[] { Snapshot(0.0), Snapshot(1.0) };
        string csv = CsvTimeSeriesExporter.ToCsv(hist);
        string header = csv.Split('\n')[0];

        // Components alpha < zeta; within each, ports sorted (b<p, a<q); state
        // vars s1 < s2. None of this matches the scrambled insertion order.
        Assert.Equal(
            "Time_s,alpha.b,alpha.p,zeta.a,zeta.q,state.yankee.s1,state.yankee.s2",
            header);
    }

    [Fact]
    public void ToCsv_IsByteIdenticalAcrossCalls()
    {
        var hist = new[] { Snapshot(0.0), Snapshot(1.0), Snapshot(2.0) };
        Assert.Equal(CsvTimeSeriesExporter.ToCsv(hist), CsvTimeSeriesExporter.ToCsv(hist));
    }
}
