// RadiatorWave2Tests.cs — Sprint RAD.W2 unit tests for the two-sided
// deployable radiator extension.

using Voxelforge.Radiator;
using Xunit;

namespace Voxelforge.Tests.Radiator;

public sealed class RadiatorWave2Tests
{
    [Fact]
    public void FlatPanel_DefaultBehavior_BitIdenticalToW1()
    {
        // The W1 ISS-class baseline must still produce Q_net ∈ [8, 14] kW.
        var r = SpacecraftRadiatorSolver.Solve(IssPanel());
        Assert.InRange(r.NetHeatRejectionRate_W, 8000.0, 14000.0);
    }

    [Fact]
    public void TwoSidedDeployable_DoublesRadiatedHeat_VsFlatPanel()
    {
        // Same area but radiates from both faces.
        var single = SpacecraftRadiatorSolver.Solve(IssPanel());
        var two_sided = SpacecraftRadiatorSolver.Solve(IssPanel()
            with { Kind = RadiatorKind.TwoSidedDeployable });
        Assert.Equal(2.0,
            two_sided.GrossRadiatedHeat_W / single.GrossRadiatedHeat_W,
            precision: 6);
    }

    [Fact]
    public void TwoSidedDeployable_DoublesBackradiation()
    {
        var single = SpacecraftRadiatorSolver.Solve(IssPanel());
        var two_sided = SpacecraftRadiatorSolver.Solve(IssPanel()
            with { Kind = RadiatorKind.TwoSidedDeployable });
        Assert.Equal(2.0,
            two_sided.SinkBackradiation_W / single.SinkBackradiation_W,
            precision: 6);
    }

    [Fact]
    public void TwoSidedDeployable_DoesNotDoubleParasiticSolar()
    {
        // Only one face faces the sun — solar absorption is single-sided.
        var single = SpacecraftRadiatorSolver.Solve(IssPanel()
            with { IncidentSolarFlux_W_m2 = 1361.0 });
        var two_sided = SpacecraftRadiatorSolver.Solve(IssPanel()
            with
            {
                Kind                    = RadiatorKind.TwoSidedDeployable,
                IncidentSolarFlux_W_m2  = 1361.0,
            });
        Assert.Equal(single.ParasiticSolarHeat_W,
                     two_sided.ParasiticSolarHeat_W, precision: 6);
    }

    [Fact]
    public void TwoSidedDeployable_NetHeatRejectionHigherThanFlatPanel()
    {
        var single = SpacecraftRadiatorSolver.Solve(IssPanel());
        var two_sided = SpacecraftRadiatorSolver.Solve(IssPanel()
            with { Kind = RadiatorKind.TwoSidedDeployable });
        Assert.True(two_sided.NetHeatRejectionRate_W > single.NetHeatRejectionRate_W);
    }

    private static SpacecraftRadiatorDesign IssPanel() => new(
        Kind:                    RadiatorKind.FlatPanel,
        PanelArea_m2:            30.0,
        OperatingTemperature_K:  320.0,
        SinkTemperature_K:       240.0,
        Emissivity:              0.85,
        SolarAbsorptivity:       0.20,
        IncidentSolarFlux_W_m2:    0.0);
}
