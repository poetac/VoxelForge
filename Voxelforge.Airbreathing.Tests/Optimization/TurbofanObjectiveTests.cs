// TurbofanObjectiveTests.cs — Sprint A8 acceptance for the IObjective
// adapter wrapping the turbofan cycle solver.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class TurbofanObjectiveTests
{
    private static readonly FlightConditions Cond =
        new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    private static AirbreathingEngineDesign Design(
        double phi = 0.30,
        double piC = 25.0,
        double bpr = 0.34)
        => new(
            Kind:                       AirbreathingEngineKind.Turbofan,
            InletThroatArea_m2:         0.37,
            CombustorArea_m2:           0.15,
            CombustorLength_m:          0.40,
            NozzleThroatArea_m2:        0.12,
            NozzleExitArea_m2:          0.18,
            EquivalenceRatio:           phi,
            CompressorPressureRatio:    piC,
            BypassRatio:                bpr);

    [Fact]
    public void DimensionCount_IsEight()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        Assert.Equal(8, obj.DimensionCount);
        Assert.Equal(8, obj.Variables.Count);
        Assert.Equal(TurbofanObjective.DefaultVariableNames.Length, obj.DimensionCount);
    }

    [Fact]
    public void DefaultBounds_LastSlotIsBypassRatio()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        Assert.Equal("BypassRatio", obj.Variables[7].Name);
        Assert.Equal(0.10, obj.Variables[7].Min);
        Assert.Equal(2.00, obj.Variables[7].Max);
    }

    [Fact]
    public void DefaultBounds_Slot6IsCompressorPressureRatio_Unchanged()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        Assert.Equal("CompressorPressureRatio", obj.Variables[6].Name);
        Assert.True(obj.Variables[6].Min >= 1.0);
    }

    [Fact]
    public void PackUnpack_RoundTrips()
    {
        var design = Design();
        var vector = TurbofanObjective.Pack(design);
        var roundTripped = TurbofanObjective.Unpack(vector);
        Assert.Equal(design, roundTripped);
    }

    [Fact]
    public void Pack_HasBypassRatioInLastSlot()
    {
        var design = Design(bpr: 0.85);
        var vector = TurbofanObjective.Pack(design);
        Assert.Equal(0.85, vector[7], 12);
    }

    [Fact]
    public void Unpack_ForcesKindTurbofan()
    {
        var design = Design();
        var vector = TurbofanObjective.Pack(design);
        var roundTripped = TurbofanObjective.Unpack(vector);
        Assert.Equal(AirbreathingEngineKind.Turbofan, roundTripped.Kind);
    }

    [Fact]
    public void Evaluate_FeasibleDesign_ReturnsNegativeIspScore()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        var vector = TurbofanObjective.Pack(Design());
        var result = obj.Evaluate(vector);
        Assert.True(result.Score < 0,
            $"Feasible turbofan score should be −Isp (negative). Got {result.Score}.");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Evaluate_InfeasibleDesign_ReturnsPositiveInfinity()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        // BPR above the [0.10, 2.00] band fires BYPASS_RATIO_OUT_OF_BAND.
        var vector = TurbofanObjective.Pack(Design(bpr: 5.0));
        var result = obj.Evaluate(vector);
        Assert.Equal(double.PositiveInfinity, result.Score);
        Assert.Contains(result.Violations, v => v.ConstraintId == "BYPASS_RATIO_OUT_OF_BAND");
    }

    [Fact]
    public void Evaluate_WrongVectorLength_Throws()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        Assert.Throws<System.ArgumentException>(() => obj.Evaluate(new double[] { 0.1, 0.2, 0.3 }));
    }

    [Fact]
    public void Evaluate_DeterministicAcrossCalls()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        var v = TurbofanObjective.Pack(Design());
        var r1 = obj.Evaluate(v);
        var r2 = obj.Evaluate(v);
        Assert.Equal(r1.Score, r2.Score, 12);
    }

    [Fact]
    public void Variables_ProjectToBoundsArray()
    {
        var obj = TurbofanObjective.WithDefaultBounds(Cond);
        var bounds = DesignVariableInfo.ToBoundsArray(obj.Variables);
        Assert.Equal(obj.DimensionCount, bounds.Length);
        for (int i = 0; i < bounds.Length; i++)
            Assert.True(bounds[i].Min < bounds[i].Max);
    }
}
