// CfdSmokeTests.cs — End-to-end smoke test requiring SU2_CFD binary.
//
// Skipped by default. Run with SU2_RUN=C:\SU2\bin to exercise the full
// mesh → config → SU2 subprocess → surface parser → calibration pipeline.

using Voxelforge.Cfd;
using Voxelforge.Cfd.Mesh;
using Voxelforge.Cfd.Su2;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;
using Xunit;

namespace Voxelforge.Cfd.Tests.Smoke;

public sealed class CfdSmokeTests
{
    [Fact(Skip = "Requires SU2_CFD binary (~10 min, Coarse mesh). " +
                 "Run with SU2_RUN=C:\\SU2\\bin set.")]
    [Trait("Category", "Smoke")]
    public void EndToEnd_CoarseMesh_Su2RunsAndProducesWallProfile()
    {
        if (Su2Locator.FindSu2Cfd() is null)
            return;

        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:       20,
            contractionRatio:      4,
            expansionRatio:        8,
            characteristicLength_m: 1.0);

        var gas = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.5, 5_000_000);

        var channels = new ChannelSchedule(
            ChannelCount:              40,
            RibThickness_mm:           0.5,
            GasSideWallThickness_mm:   0.8,
            ChannelHeightAtChamber_mm: 2.5,
            ChannelHeightAtThroat_mm:  1.2,
            ChannelHeightAtExit_mm:    2.0);

        var solverInputs = new RegenSolverInputs(
            Contour:                 contour,
            Gas:                     gas,
            Wall:                    WallMaterials.GRCop42,
            Channels:                channels,
            CoolantMassFlow_kgs:     0.8,
            CoolantInletTemp_K:      110,
            CoolantInletPressure_Pa: 6_000_000);

        var inputs = new CfdCalibrationInputs(
            Contour:            contour,
            Gas:                gas,
            SolverInputs:       solverInputs,
            ChamberPressure_Pa: 5_000_000,
            Density:            Su2MeshDensity.Coarse);

        var result = CfdCalibrationRunner.RunCalibration(
            inputs,
            inp => RegenCoolingSolver.Solve(inp));

        Assert.InRange(result.WallProfile.PeakAdiabaticWallTemp_K, 1000.0, 6000.0);
        Assert.True(result.WallProfile.NodeCount > 0);
        Assert.InRange(result.CalibrationResult.BartzScalingFactor.MapValue, 0.60, 1.40);
    }
}
