// RegenCoolingSolverTempFloorTests.cs — P10 (2026-05-06).
//
// Pins the Math.Max floor guard added to RegenCoolingSolver for per-station
// T_wg and T_wc. At low-flux stations (nozzle-exit region of a large
// expansion-ratio chamber, or thick-wall designs where the coolant bulk
// temperature is close to the recovery temperature), floating-point arithmetic
// can yield slightly sub-zero or very-small wall temperatures that then
// propagate as NaN into downstream Math.Sqrt / Math.Log calls inside k(T)
// and CoolantCorrelations helpers.
//
// The test constructs a high-expansion-ratio chamber that stresses the
// exit-region stations — those have the weakest Bartz flux and are the
// most likely to trip the pre-fix NaN path.

using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class RegenCoolingSolverTempFloorTests
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

    /// <summary>
    /// High expansion ratio → large nozzle exit stations with very weak
    /// gas-side heat flux. These stations are the historical source of
    /// sub-100 K T_wg / T_wc that could propagate as NaN into k(T) calls.
    /// After the Math.Max floor guard (P10), every station must return a
    /// finite wall temperature ≥ MinPhysicalWallTemp_K (100 K).
    /// </summary>
    [Fact]
    public void RegenSolver_AllStations_WallTempFiniteAndAboveFloor()
    {
        const double MinPhysicalWallTemp_K = 100.0;

        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ExpansionRatio   = 20.0,  // large exit cone → weak-flux exit stations
            ContractionRatio = 4.0,
        };

        // skipVoxelGeometry=true, skipMfgAnalysis=true: physics-only path,
        // same as the SA hot path that originally triggered the NaN issue.
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design,
            voxelSize_mm:      0.0,
            skipVoxelGeometry: true,
            skipMfgAnalysis:   true);

        var thermal = gen.Thermal;
        Assert.NotNull(thermal.Stations);

        for (int i = 0; i < thermal.Stations.Length; i++)
        {
            var st = thermal.Stations[i];
            Assert.False(double.IsNaN(st.GasSideWallTemp_K),
                $"Station {i} (x={st.X_mm:F1} mm): GasSideWallTemp_K is NaN");
            Assert.False(double.IsNaN(st.CoolantSideWallTemp_K),
                $"Station {i} (x={st.X_mm:F1} mm): CoolantSideWallTemp_K is NaN");
            Assert.True(st.GasSideWallTemp_K >= MinPhysicalWallTemp_K,
                $"Station {i} (x={st.X_mm:F1} mm): GasSideWallTemp_K={st.GasSideWallTemp_K:F1} K < floor {MinPhysicalWallTemp_K} K");
            Assert.True(st.CoolantSideWallTemp_K >= MinPhysicalWallTemp_K,
                $"Station {i} (x={st.X_mm:F1} mm): CoolantSideWallTemp_K={st.CoolantSideWallTemp_K:F1} K < floor {MinPhysicalWallTemp_K} K");
        }
    }
}
