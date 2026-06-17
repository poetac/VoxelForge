// StateVectorBindingTests — Phase 1 of #557 item 1.
//
// Pins the binding's invariants: name→index map matches the order of
// StateVariables, duplicate names throw, dict↔array round-trips
// preserve values, and the bound component's state is opaque to the
// binding (read-only IStatefulComponent surface).

using System;
using System.Collections.Generic;
using Voxelforge.Integration;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class StateVectorBindingTests
{
    private sealed class StubStatefulComponent : SystemComponent, IStatefulComponent
    {
        public StubStatefulComponent(string name, params string[] stateVars) : base(name)
        {
            StateVariables = stateVars;
        }
        public override IReadOnlyList<string> InputPorts { get; } = Array.Empty<string>();
        public override IReadOnlyList<string> OutputPorts { get; } = Array.Empty<string>();
        public override void Evaluate(IReadOnlyDictionary<string, double> _, IDictionary<string, double> __) { }
        public IReadOnlyList<string> StateVariables { get; }
        public void ComputeDerivatives(
            ReadOnlySpan<double> _,
            IReadOnlyDictionary<string, double> __,
            IReadOnlyDictionary<string, double> ___,
            Span<double> ____) { }
        public void GetInitialState(Span<double> _) { }
        public void GetCurrentState(Span<double> _) { }
        public void SetState(ReadOnlySpan<double> _) { }
    }

    [Fact]
    public void Compute_SingleVariable_MapsToIndexZero()
    {
        var c = new StubStatefulComponent("acc", "Accumulated_total");
        var binding = StateVectorBinding.Compute("acc", c);
        Assert.Equal("acc", binding.ComponentName);
        Assert.Equal(1, binding.VariableCount);
        Assert.Equal(new[] { "Accumulated_total" }, binding.VariableNames);
        Assert.Equal(0, binding.NameToIndex["Accumulated_total"]);
    }

    [Fact]
    public void Compute_ManyVariables_PreservesOrder()
    {
        var c = new StubStatefulComponent("battery", "Soc", "T_cell_K", "DegradationFactor");
        var binding = StateVectorBinding.Compute("battery", c);
        Assert.Equal(3, binding.VariableCount);
        Assert.Equal(0, binding.NameToIndex["Soc"]);
        Assert.Equal(1, binding.NameToIndex["T_cell_K"]);
        Assert.Equal(2, binding.NameToIndex["DegradationFactor"]);
    }

    [Fact]
    public void Compute_DuplicateVariableName_Throws()
    {
        var c = new StubStatefulComponent("bad", "X", "Y", "X");
        var ex = Assert.Throws<InvalidOperationException>(
            () => StateVectorBinding.Compute("bad", c));
        Assert.Contains("duplicate state-variable", ex.Message);
        Assert.Contains("'X'", ex.Message);
    }

    [Fact]
    public void Compute_NullComponent_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => StateVectorBinding.Compute("any", null!));
    }

    [Fact]
    public void CopyDictToArray_FillsByName()
    {
        var c = new StubStatefulComponent("c", "A", "B", "C");
        var binding = StateVectorBinding.Compute("c", c);
        var src = new Dictionary<string, double> { ["A"] = 1.0, ["B"] = 2.0, ["C"] = 3.0 };
        Span<double> dst = stackalloc double[3];
        binding.CopyDictToArray(src, dst);
        Assert.Equal(1.0, dst[0]);
        Assert.Equal(2.0, dst[1]);
        Assert.Equal(3.0, dst[2]);
    }

    [Fact]
    public void CopyDictToArray_MissingKey_Throws()
    {
        var c = new StubStatefulComponent("c", "A", "B");
        var binding = StateVectorBinding.Compute("c", c);
        var src = new Dictionary<string, double> { ["A"] = 1.0 }; // missing "B"
        var dst = new double[2];
        var ex = Assert.Throws<InvalidOperationException>(
            () => binding.CopyDictToArray(src, dst));
        Assert.Contains("'B'", ex.Message);
    }

    [Fact]
    public void CopyArrayToDict_FillsByName()
    {
        var c = new StubStatefulComponent("c", "A", "B", "C");
        var binding = StateVectorBinding.Compute("c", c);
        ReadOnlySpan<double> src = stackalloc double[] { 10.0, 20.0, 30.0 };
        var dst = new Dictionary<string, double> { ["A"] = 0, ["B"] = 0, ["C"] = 0 };
        binding.CopyArrayToDict(src, dst);
        Assert.Equal(10.0, dst["A"]);
        Assert.Equal(20.0, dst["B"]);
        Assert.Equal(30.0, dst["C"]);
    }

    [Fact]
    public void CopyDictToArray_DestinationTooSmall_Throws()
    {
        var c = new StubStatefulComponent("c", "A", "B");
        var binding = StateVectorBinding.Compute("c", c);
        var src = new Dictionary<string, double> { ["A"] = 1, ["B"] = 2 };
        var dst = new double[1]; // too small
        Assert.Throws<ArgumentException>(
            () => binding.CopyDictToArray(src, dst));
    }

    [Fact]
    public void RoundTrip_DictToArrayToDict_PreservesValues()
    {
        var c = new StubStatefulComponent("rt", "X", "Y", "Z");
        var binding = StateVectorBinding.Compute("rt", c);
        var src  = new Dictionary<string, double> { ["X"] = 1.1, ["Y"] = 2.2, ["Z"] = 3.3 };
        Span<double> mid = stackalloc double[3];
        binding.CopyDictToArray(src, mid);

        var dst = new Dictionary<string, double> { ["X"] = 0, ["Y"] = 0, ["Z"] = 0 };
        binding.CopyArrayToDict(mid, dst);

        Assert.Equal(src["X"], dst["X"]);
        Assert.Equal(src["Y"], dst["Y"]);
        Assert.Equal(src["Z"], dst["Z"]);
    }
}
