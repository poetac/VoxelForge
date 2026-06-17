// NetworkValidatorTests.cs — Sprint SI.W18 unit tests for the static-
// analysis NetworkValidator.

using System;
using System.Linq;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Voxelforge.Photovoltaic;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class NetworkValidatorTests
{
    [Fact]
    public void Validator_OnEmptyNetwork_NoIssues()
    {
        var net = new ComponentNetwork();
        var rep = NetworkValidator.Validate(net);
        Assert.True(rep.IsValid);
        Assert.Empty(rep.Issues);
    }

    [Fact]
    public void Validator_FlagsUnfedInput_AsError()
    {
        // PV component needs Irradiance + CellTemperature; neither
        // is fed.
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        var rep = NetworkValidator.Validate(net);
        // 2 unfed inputs → 2 errors.
        Assert.Equal(2, rep.ErrorCount);
        Assert.All(rep.Issues.Where(i => i.Severity == ValidationSeverity.Error),
            i => Assert.Equal("UnfedInput", i.Category));
        Assert.False(rep.IsValid);
    }

    [Fact]
    public void Validator_PassesValidPvNetwork()
    {
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        net.SetExternalInput("pv", "Irradiance_W_m2",   1000.0);
        net.SetExternalInput("pv", "CellTemperature_C", 25.0);
        var rep = NetworkValidator.Validate(net);
        Assert.True(rep.IsValid);
        Assert.Equal(0, rep.ErrorCount);
        // 4 unconnected outputs (MaxPower_W etc.) → 4 infos.
        Assert.Equal(4, rep.InfoCount);
    }

    [Fact]
    public void Validator_FlagsUnconnectedOutput_AsInfo()
    {
        var net = new ComponentNetwork();
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("acc", "Input_rate", 1.0);
        var rep = NetworkValidator.Validate(net);
        Assert.Equal(1, rep.InfoCount);   // Accumulated_total unconnected
        Assert.Equal("UnconnectedOutput", rep.Issues[0].Category);
        Assert.True(rep.IsValid);
    }

    [Fact]
    public void Validator_FlagsOverdeterminedInput_AsWarning()
    {
        // Connect PV.MaxPower_W → acc.Input_rate AND set ext Input_rate.
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv", DefaultPanelDesign()));
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("pv", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv", "CellTemperature_C", 25.0);
        net.Connect("pv", "MaxPower_W", "acc", "Input_rate");
        net.SetExternalInput("acc", "Input_rate", 999.0);   // overdetermined
        var rep = NetworkValidator.Validate(net);
        Assert.Equal(1, rep.WarningCount);
        Assert.Equal("OverDeterminedInput",
            rep.Issues.First(i => i.Severity == ValidationSeverity.Warning).Category);
    }

    [Fact]
    public void Validator_FlagsMultipleSourcesForOneInput_AsError()
    {
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv1", DefaultPanelDesign()));
        net.Add(new PhotovoltaicComponent("pv2", DefaultPanelDesign()));
        var acc = new AccumulatorComponent("acc");
        net.Add(acc);
        net.SetExternalInput("pv1", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv1", "CellTemperature_C", 25.0);
        net.SetExternalInput("pv2", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv2", "CellTemperature_C", 25.0);
        // Two sources feeding the same input — error.
        net.Connect("pv1", "MaxPower_W", "acc", "Input_rate");
        net.Connect("pv2", "MaxPower_W", "acc", "Input_rate");
        var rep = NetworkValidator.Validate(net);
        Assert.Contains(rep.Issues,
            i => i.Category == "MultipleSourcesForInput"
              && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validator_FlagsCycle_ViaTypedException()
    {
        // B.8a / issue #490 — validator catches the typed
        // CyclicComponentNetworkException without relying on string-
        // matching the exception message. Construct a 2-node cycle
        // and verify the ContainsCycle warning fires.
        var net = new ComponentNetwork();
        net.Add(new PhotovoltaicComponent("pv1", DefaultPanelDesign()));
        net.Add(new PhotovoltaicComponent("pv2", DefaultPanelDesign()));
        net.SetExternalInput("pv1", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv1", "CellTemperature_C", 25.0);
        net.SetExternalInput("pv2", "Irradiance_W_m2", 1000.0);
        net.SetExternalInput("pv2", "CellTemperature_C", 25.0);
        // Cycle: pv1 -> pv2 -> pv1 on the irradiance/temperature ports.
        // (Bogus physically; only exercises the topology detector.)
        net.Connect("pv1", "MaxPower_W", "pv2", "Irradiance_W_m2");
        net.Connect("pv2", "MaxPower_W", "pv1", "Irradiance_W_m2");
        var rep = NetworkValidator.Validate(net);
        Assert.Contains(rep.Issues,
            i => i.Category == "ContainsCycle"
              && i.Severity == ValidationSeverity.Warning);
    }

    private static PvPanelDesign DefaultPanelDesign() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      60,
        StringsInParallel:  1,
        CellArea_cm2:       243.0,
        Irradiance_W_m2:    1000.0,
        CellTemperature_C:  25.0);
}
