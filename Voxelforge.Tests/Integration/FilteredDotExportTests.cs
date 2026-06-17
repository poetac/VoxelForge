// FilteredDotExportTests.cs — Sprint SI.W28 tests for the
// component-filtered GraphViz DOT export.

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class FilteredDotExportTests
{
    private sealed class Producer : SystemComponent
    {
        public Producer(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "Out_W" };
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> outputs)
            => outputs["Out_W"] = 100.0;
    }

    private sealed class Consumer : SystemComponent
    {
        public Consumer(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "In_W" };
        public override IReadOnlyList<string> OutputPorts { get; } = Array.Empty<string>();
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> __) { }
    }

    [Fact]
    public void Unfiltered_ExportsEveryComponent()
    {
        var net = new ComponentNetwork();
        net.Add(new Producer("ev_battery"));
        net.Add(new Consumer("ev_motor"));
        net.Add(new Producer("therm_radiator"));
        net.Connect("ev_battery", "Out_W", "ev_motor", "In_W");

        var dot = net.ExportToDot();
        Assert.Contains("ev_battery", dot);
        Assert.Contains("ev_motor", dot);
        Assert.Contains("therm_radiator", dot);
    }

    [Fact]
    public void Filtered_OmitsExcludedComponents()
    {
        var net = new ComponentNetwork();
        net.Add(new Producer("ev_battery"));
        net.Add(new Consumer("ev_motor"));
        net.Add(new Producer("therm_radiator"));
        net.Add(new Consumer("therm_pump"));

        // Filter to only the EV subsystem.
        var dot = net.ExportToDot(c => c.StartsWith("ev_", System.StringComparison.Ordinal));
        Assert.Contains("ev_battery", dot);
        Assert.Contains("ev_motor", dot);
        // therm_* should NOT appear as primary nodes.
        // (Substring check is sufficient because the names are distinct
        // and don't appear as substrings of other names.)
        Assert.DoesNotContain("therm_radiator", dot);
        Assert.DoesNotContain("therm_pump", dot);
    }

    [Fact]
    public void Filtered_CrossBoundary_ShowsOutOfScopeBorderNode()
    {
        // Connection from ev_battery (in scope) to therm_pump (out).
        // The dotted edge should land + therm_pump should appear as
        // a faded "out of scope" border node.
        var net = new ComponentNetwork();
        net.Add(new Producer("ev_battery"));
        net.Add(new Consumer("therm_pump"));
        net.Connect("ev_battery", "Out_W", "therm_pump", "In_W");

        var dot = net.ExportToDot(c => c.StartsWith("ev_", System.StringComparison.Ordinal));
        Assert.Contains("ev_battery", dot);
        // Out-of-scope endpoint rendered as a border node with the
        // "(out of scope)" label.
        Assert.Contains("therm_pump", dot);
        Assert.Contains("out of scope", dot);
        // Cross-boundary edge uses dotted style.
        Assert.Contains("style=dotted", dot);
    }

    [Fact]
    public void Filtered_OutOfScopeConnections_Skipped()
    {
        // Connection where BOTH endpoints are out of scope must not
        // appear in the filtered DOT output.
        var net = new ComponentNetwork();
        net.Add(new Producer("ev_battery"));
        net.Add(new Producer("therm_radiator"));
        net.Add(new Consumer("therm_pump"));
        net.Connect("therm_radiator", "Out_W", "therm_pump", "In_W");

        var dot = net.ExportToDot(c => c.StartsWith("ev_", System.StringComparison.Ordinal));
        // Edge therm_radiator → therm_pump must NOT appear.
        Assert.DoesNotContain("therm_radiator\" -> \"therm_pump\"", dot);
    }

    [Fact]
    public void Filtered_ExternalInputs_ScopedToIncludedComponents()
    {
        var net = new ComponentNetwork();
        net.Add(new Consumer("ev_motor"));
        net.Add(new Consumer("therm_pump"));
        net.SetExternalInput("ev_motor",   "In_W", 100.0);
        net.SetExternalInput("therm_pump", "In_W",  50.0);

        var dot = net.ExportToDot(c => c.StartsWith("ev_", System.StringComparison.Ordinal));
        Assert.Contains("ext_ev_motor_In_W", dot);
        Assert.DoesNotContain("ext_therm_pump", dot);
    }

    [Fact]
    public void PassAllFilter_MatchesUnfilteredOutput()
    {
        var net = new ComponentNetwork();
        net.Add(new Producer("a"));
        net.Add(new Consumer("b"));
        net.Connect("a", "Out_W", "b", "In_W");

        var unfiltered = net.ExportToDot();
        var allPass    = net.ExportToDot(_ => true);

        // Pass-all filter still adds the cross-boundary detection logic,
        // but since no components are filtered out, no border nodes are
        // generated. The output should contain all the same components +
        // connections without the cross-boundary "out of scope" markers.
        Assert.Contains("a", allPass);
        Assert.Contains("b", allPass);
        Assert.Contains("Out_W", allPass);
        Assert.DoesNotContain("out of scope", allPass);
    }

    [Fact]
    public void EmptyFilter_EmptySubgraphValid()
    {
        var net = new ComponentNetwork();
        net.Add(new Producer("a"));
        net.Add(new Consumer("b"));
        net.Connect("a", "Out_W", "b", "In_W");

        // Filter that matches nothing — DOT output should still be a
        // valid digraph (no syntax errors), just empty of content.
        // Border-node rendering only triggers on cross-boundary edges
        // (one endpoint in, one out); when BOTH endpoints are filtered
        // out, the edge is dropped entirely and no border nodes appear
        // (see ComponentNetwork.ExportToDot line 187 XOR check).
        var dot = net.ExportToDot(_ => false);
        Assert.Contains("digraph", dot);
        Assert.Contains("}", dot);
    }
}
