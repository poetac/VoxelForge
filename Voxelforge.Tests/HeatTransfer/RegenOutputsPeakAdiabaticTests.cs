// RegenOutputsPeakAdiabaticTests.cs — Sprint C.2 (2026-05-06).
//
// Pins RegenSolverOutputs.PeakAdiabaticWallTemp_K — the new aggregate field
// the CFD calibration loop now uses for direct T_aw vs T_aw comparison
// against SU2's adiabatic-wall surface output (MARKER_HEATFLUX=0).

using System.Linq;
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class RegenOutputsPeakAdiabaticTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 5_000,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        PropellantPair          = PropellantPair.LOX_CH4,
        WallMaterialIndex       = 1,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
    };

    [Fact]
    public void PeakAdiabaticWallTemp_MatchesMaxOverStations()
    {
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 12.0,
            ContractionRatio = 4.0,
        };

        var gen = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:      0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        var thermal = gen.Thermal;
        double expected = thermal.Stations.Max(s => s.AdiabaticWallTemp_K);
        Assert.Equal(expected, thermal.PeakAdiabaticWallTemp_K, precision: 6);
    }

    [Fact]
    public void PeakAdiabaticWallTemp_ExceedsAnyStationStaticTemp()
    {
        // T_aw = T_static · (1 + r·(γ-1)/2·M²) ≥ T_static for any non-trivial flow.
        // Therefore peak T_aw must exceed every station's static T.
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 12.0,
            ContractionRatio = 4.0,
        };

        var gen = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:      0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        var thermal = gen.Thermal;
        double maxStatic = thermal.Stations.Max(s => s.StaticTemp_K);
        Assert.True(thermal.PeakAdiabaticWallTemp_K >= maxStatic,
            $"peak T_aw {thermal.PeakAdiabaticWallTemp_K:F0} K should be ≥ peak T_static {maxStatic:F0} K");
    }

    [Fact]
    public void PeakAdiabaticWallTemp_PlausibleForLoxCh4Chamber()
    {
        // For LOX/CH4 at 6.9 MPa with chamber T ≈ 3500 K, peak T_aw at the
        // throat (M=1) should be a few hundred K below T_chamber — typically
        // 3200-3400 K. Pin the value into the published-engine sanity range.
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 12.0,
            ContractionRatio = 4.0,
        };

        var gen = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:      0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        Assert.InRange(gen.Thermal.PeakAdiabaticWallTemp_K, 2500.0, 4000.0);
    }
}
