// UnitSuffixConsistencyTests.cs — Sprint SI.W24 tests for the
// NetworkValidator unit-suffix consistency check.
//
// Pins:
//   • Matching suffix (_W → _W) clean — no UnitMismatch issue.
//   • Mismatched recognized suffixes (_W → _A) produce a Warning.
//   • Unrecognized suffixes (descriptor-shaped like _total, _frac) skip
//     the check — no false positives.
//   • Ports without underscores skip the check.
//   • The check runs alongside the existing SI.W18 arms (unfed inputs,
//     overdetermined inputs, cycle detection) — doesn't suppress them.

using System;
using System.Collections.Generic;
using System.Linq;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class UnitSuffixConsistencyTests
{
    private sealed class WattSource : SystemComponent
    {
        public WattSource(string name, double value) : base(name) { _value = value; }
        private readonly double _value;
        public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();
        public override IReadOnlyList<string> OutputPorts { get; } = new[] { "Power_W" };
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> outputs)
            => outputs["Power_W"] = _value;
    }

    private sealed class WattSink : SystemComponent
    {
        public WattSink(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "Load_W" };
        public override IReadOnlyList<string> OutputPorts { get; } = Array.Empty<string>();
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> __) { }
    }

    private sealed class AmpSink : SystemComponent
    {
        public AmpSink(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "DrawCurrent_A" };
        public override IReadOnlyList<string> OutputPorts { get; } = Array.Empty<string>();
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> __) { }
    }

    private sealed class DescriptorSink : SystemComponent
    {
        public DescriptorSink(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "Some_total" };
        public override IReadOnlyList<string> OutputPorts { get; } = Array.Empty<string>();
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> __) { }
    }

    private sealed class NoSuffixSink : SystemComponent
    {
        public NoSuffixSink(string name) : base(name) { }
        public override IReadOnlyList<string> InputPorts { get; } = new[] { "BareInput" };
        public override IReadOnlyList<string> OutputPorts { get; } = Array.Empty<string>();
        public override void Evaluate(IReadOnlyDictionary<string, double> _,
                                       IDictionary<string, double> __) { }
    }

    [Fact]
    public void MatchingSuffix_ProducesNoUnitMismatchIssue()
    {
        var net = new ComponentNetwork();
        net.Add(new WattSource("src", 100.0));
        net.Add(new WattSink("sink"));
        net.Connect("src", "Power_W", "sink", "Load_W");

        var report = NetworkValidator.Validate(net);
        Assert.DoesNotContain(report.Issues,
            i => i.Category == "UnitMismatch");
    }

    [Fact]
    public void MismatchedSuffix_ProducesWarning()
    {
        // Power_W → DrawCurrent_A — recognized suffixes that don't match.
        var net = new ComponentNetwork();
        net.Add(new WattSource("src", 100.0));
        net.Add(new AmpSink("sink"));
        net.Connect("src", "Power_W", "sink", "DrawCurrent_A");

        var report = NetworkValidator.Validate(net);
        var mismatches = report.Issues
            .Where(i => i.Category == "UnitMismatch")
            .ToList();
        Assert.Single(mismatches);
        Assert.Equal(ValidationSeverity.Warning, mismatches[0].Severity);
        Assert.Contains("W", mismatches[0].Message);
        Assert.Contains("A", mismatches[0].Message);
    }

    [Fact]
    public void DescriptorSuffix_SkipsCheck()
    {
        // _total is a descriptor, not a recognized unit — connection
        // should NOT trip the unit-mismatch warning even though the
        // upstream side has _W.
        var net = new ComponentNetwork();
        net.Add(new WattSource("src", 100.0));
        net.Add(new DescriptorSink("sink"));
        net.Connect("src", "Power_W", "sink", "Some_total");

        var report = NetworkValidator.Validate(net);
        Assert.DoesNotContain(report.Issues,
            i => i.Category == "UnitMismatch");
    }

    [Fact]
    public void NoSuffix_SkipsCheck()
    {
        // BareInput has no underscore-suffix at all — the check must
        // skip the connection silently.
        var net = new ComponentNetwork();
        net.Add(new WattSource("src", 100.0));
        net.Add(new NoSuffixSink("sink"));
        net.Connect("src", "Power_W", "sink", "BareInput");

        var report = NetworkValidator.Validate(net);
        Assert.DoesNotContain(report.Issues,
            i => i.Category == "UnitMismatch");
    }

    [Fact]
    public void UnitCheck_DoesNotSuppressOtherArms()
    {
        // Combine a unit mismatch with an unfed-input error — both should
        // surface in the same report.
        var net = new ComponentNetwork();
        net.Add(new WattSource("src", 100.0));
        net.Add(new AmpSink("sink_with_mismatch"));
        net.Add(new WattSink("sink_unfed"));
        net.Connect("src", "Power_W", "sink_with_mismatch", "DrawCurrent_A");
        // sink_unfed.Load_W has neither external input nor connection.

        var report = NetworkValidator.Validate(net);
        Assert.Contains(report.Issues,
            i => i.Category == "UnitMismatch"
              && i.Component == "src");
        Assert.Contains(report.Issues,
            i => i.Category == "UnfedInput"
              && i.Component == "sink_unfed");
    }

    [Fact]
    public void MultipleMismatches_AllSurface()
    {
        var net = new ComponentNetwork();
        net.Add(new WattSource("src1", 100.0));
        net.Add(new WattSource("src2", 200.0));
        net.Add(new AmpSink("sink1"));
        net.Add(new AmpSink("sink2"));
        net.Connect("src1", "Power_W", "sink1", "DrawCurrent_A");
        net.Connect("src2", "Power_W", "sink2", "DrawCurrent_A");

        var report = NetworkValidator.Validate(net);
        Assert.Equal(2, report.Issues.Count(i => i.Category == "UnitMismatch"));
    }
}
