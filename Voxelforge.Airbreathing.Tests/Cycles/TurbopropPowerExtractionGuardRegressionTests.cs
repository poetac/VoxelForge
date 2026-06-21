// TurbopropPowerExtractionGuardRegressionTests.cs — regression guard for the
// turboprop over-unity power-extraction bug (red-team finding).
//
// PropellerPowerExtraction_frac is a fraction ∈ [0, 1], but the solver never
// validated it. Over-unity values over-extract the available enthalpy:
//   • at low pressure ratios the residual-nozzle thrust is silently inflated
//     (cruise fpe=3.0 read ~57 kN vs ~18 kN at the design fpe=0.89);
//   • at high pressure ratios (high altitude) the power-turbine exit
//     temperature T_t6,s goes negative, so Math.Pow(T_t6,s/T_t5, γ/(γ−1))
//     returns NaN and corrupts station 6 / 9 pressures — which then slip
//     through NaN-vs-limit gate comparisons unflagged.
// Solve now rejects fpe ∉ [0, 1]. These cases throw on the new code and
// returned inflated/NaN results on the old code.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class TurbopropPowerExtractionGuardRegressionTests
{
    private static AirbreathingEngineDesign Design(double fpe) => new(
        Kind:                    AirbreathingEngineKind.Turboprop,
        InletThroatArea_m2:      0.115,
        CombustorArea_m2:        0.10,
        CombustorLength_m:       0.35,
        NozzleThroatArea_m2:     0.055,
        NozzleExitArea_m2:       0.070,
        EquivalenceRatio:        0.30,
        CompressorPressureRatio: 9.25)
    {
        PropellerPowerExtraction_frac = fpe,
    };

    [Theory]
    [InlineData(5182.0,  0.58, 1.5)]   // cruise: over-unity → inflated thrust on old code
    [InlineData(12000.0, 0.55, 3.0)]   // high altitude: over-unity → NaN station pressures on old code
    [InlineData(5182.0,  0.58, -0.1)]  // negative fraction
    public void OutOfRangePowerExtraction_ThrowsClearly(double alt, double mach, double fpe)
    {
        var solver = new TurbopropCycleSolver();
        var cond = new FlightConditions(alt, mach, AirbreathingFuel.Jp8);
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Solve(Design(fpe), cond));
    }

    [Fact]
    public void FullExtraction_AtUnity_StillSolvesFinite()
    {
        // fpe = 1.0 is the inclusive boundary (full expansion to ambient —
        // the value the turboshaft path uses). It must still solve, not throw.
        var solver = new TurbopropCycleSolver();
        var cond = new FlightConditions(5182.0, 0.58, AirbreathingFuel.Jp8);
        var r = solver.Solve(Design(1.0), cond);
        double p6 = r.Stations.Station(6).StagnationP_Pa;
        Assert.False(double.IsNaN(p6), "station 6 pressure should be finite at fpe = 1.0");
        Assert.True(p6 > 0.0, $"station 6 pressure should be positive; got {p6}");
    }
}
