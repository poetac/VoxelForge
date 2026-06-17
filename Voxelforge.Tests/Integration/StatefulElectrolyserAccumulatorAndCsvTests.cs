// StatefulElectrolyserAccumulatorAndCsvTests.cs — Sprint SI.W13 + W14
// + W15 unit tests for: stateful PEM electrolyser, generic
// AccumulatorComponent, and CsvTimeSeriesExporter.

using System;
using System.Linq;
using Voxelforge.Electrolyser;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class StatefulElectrolyserAccumulatorAndCsvTests
{
    // ── StatefulElectrolyserComponent (Sprint SI.W13) ────────────────────

    [Fact]
    public void StatefulElectrolyser_AtZeroProduction_MassUnchanged()
    {
        // Drive current density at 0 → zero production → cumulative
        // mass stays at the initial value.
        var net = new ComponentNetwork();
        var stack = new StatefulElectrolyserComponent("el", NelPemDesign(),
            initialCumulativeMass_kg: 0.5);
        net.Add(stack);
        net.SetExternalInput("el", "OperatingCurrentDensity_A_cm2", 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("el", stack);
        var hist = integrator.Run(0.0, 60.0, 1.0);
        Assert.Equal(0.5, hist[^1].PortValues["el"]["CumulativeHydrogenMass_kg"],
            precision: 6);
    }

    [Fact]
    public void StatefulElectrolyser_AccumulatesMonotonically_AtPositiveCurrent()
    {
        // Drive at 1.5 A/cm² for 600 s → cumulative mass must rise.
        var net = new ComponentNetwork();
        var stack = new StatefulElectrolyserComponent("el", NelPemDesign(),
            initialCumulativeMass_kg: 0.0);
        net.Add(stack);
        net.SetExternalInput("el", "OperatingCurrentDensity_A_cm2", 1.5);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("el", stack);
        var hist = integrator.Run(0.0, 600.0, 10.0);
        for (int k = 1; k < hist.Count; k++)
            Assert.True(hist[k].PortValues["el"]["CumulativeHydrogenMass_kg"]
                      > hist[k - 1].PortValues["el"]["CumulativeHydrogenMass_kg"]);
    }

    [Fact]
    public void StatefulElectrolyser_RejectsNegativeInitialMass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatefulElectrolyserComponent("el", NelPemDesign(),
                initialCumulativeMass_kg: -0.01));
    }

    // ── AccumulatorComponent (Sprint SI.W14) ─────────────────────────────

    [Fact]
    public void Accumulator_IntegratesConstantRate_LinearlyOverTime()
    {
        // Input_rate = 10 [units/s] for 100 s → total = 1000.
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc", initial: 0.0);
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 10.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 100.0, 1.0);
        // After 99 ticks of +10 step → ~990 (last snapshot at t=99).
        double final = hist[^1].PortValues["acc"]["Accumulated_total"];
        Assert.InRange(final, 980.0, 1010.0);
    }

    [Fact]
    public void Accumulator_RespectsInitialValue()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc", initial: 500.0);
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 0.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 10.0, 1.0);
        Assert.Equal(500.0, hist[^1].PortValues["acc"]["Accumulated_total"],
            precision: 6);
    }

    [Fact]
    public void Accumulator_TimeVaryingRamp_IntegratesToTriangleArea()
    {
        // Ramp rate 0 → 100 over 100 s. Integral = 0.5 · 100 · 100 = 5000.
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetTimeVaryingExternalInput("acc", "Input_rate", t => t);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 100.0, 0.1, useIterativeSolve: false,
            method: IntegrationMethod.Rk4);
        double final = hist[^1].PortValues["acc"]["Accumulated_total"];
        // RK4 with dt=0.1 should be very close to the exact 5000.
        Assert.InRange(final, 4990.0, 5010.0);
    }

    // ── CsvTimeSeriesExporter (Sprint SI.W15) ────────────────────────────

    [Fact]
    public void CsvExporter_EmitsHeaderAndDataRows()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc", initial: 0.0);
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 1.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 3.0, 1.0);
        string csv = CsvTimeSeriesExporter.ToCsv(hist);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // 1 header + 4 ticks. Under #553 closed [t0, tEnd] N+1 semantics
        // Run(0, 3, 1) emits round(3/1)+1 = 4 snapshots at t = 0, 1, 2, 3.
        Assert.Equal(5, lines.Length);
        // Header includes Time_s + the port + the state.
        Assert.Contains("Time_s", lines[0]);
        Assert.Contains("acc.Accumulated_total", lines[0]);
        Assert.Contains("state.acc.Accumulated_total", lines[0]);
    }

    [Fact]
    public void CsvExporter_OnEmptyHistory_RendersHeaderOnly()
    {
        string csv = CsvTimeSeriesExporter.ToCsv(Array.Empty<TimeHistorySnapshot>());
        Assert.Equal("Time_s\n", csv);
    }

    [Fact]
    public void CsvExporter_TimeColumn_ParsesAsDouble()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 1.0);
        var integrator = new TimeStepIntegrator(net);
        integrator.RegisterStateful("acc", acc);
        var hist = integrator.Run(0.0, 5.0, 1.0);
        string csv = CsvTimeSeriesExporter.ToCsv(hist);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Skip header; first data row's first column == 0.0.
        var firstCell = lines[1].Split(',')[0];
        Assert.True(double.TryParse(firstCell,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var t0));
        Assert.Equal(0.0, t0, precision: 6);
    }

    // ── Cross-cutting: SI.W14 LastResolvedInputs ─────────────────────────

    [Fact]
    public void Network_LastResolvedInputs_PopulatedAfterSolve()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 42.0);
        net.Solve();
        Assert.True(net.LastResolvedInputs.ContainsKey("acc"));
        Assert.Equal(42.0, net.LastResolvedInputs["acc"]["Input_rate"]);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static PemElectrolyserDesign NelPemDesign() => new(
        Kind:                           ElectrolyserKind.Pem,
        CellCount:                      100,
        ActiveAreaPerCell_cm2:          750.0,
        OperatingCurrentDensity_A_cm2:  1.0,
        OperatingTemperature_C:         80.0,
        OperatingPressure_bar:          30.0);
}
