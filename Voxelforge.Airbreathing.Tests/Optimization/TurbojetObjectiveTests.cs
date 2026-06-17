// TurbojetObjectiveTests.cs — Sprint A7 acceptance for the IObjective
// adapter wrapping the turbojet cycle solver.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class TurbojetObjectiveTests
{
    private static readonly FlightConditions Cond =
        new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    private static AirbreathingEngineDesign Design(double phi = 0.22, double piC = 8.0)
        => new(
            Kind:                       AirbreathingEngineKind.Turbojet,
            InletThroatArea_m2:         0.115,
            CombustorArea_m2:           0.10,
            CombustorLength_m:          0.30,
            NozzleThroatArea_m2:        0.060,
            NozzleExitArea_m2:          0.078,
            EquivalenceRatio:           phi,
            CompressorPressureRatio:    piC);

    [Fact]
    public void DimensionCount_IsSeven()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        Assert.Equal(7, obj.DimensionCount);
        Assert.Equal(7, obj.Variables.Count);
        Assert.Equal(TurbojetObjective.DefaultVariableNames.Length, obj.DimensionCount);
    }

    [Fact]
    public void DefaultBounds_LastSlotIsCompressorPressureRatio()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        Assert.Equal("CompressorPressureRatio", obj.Variables[6].Name);
        Assert.True(obj.Variables[6].Min >= 1.0);
    }

    [Fact]
    public void PackUnpack_RoundTrips()
    {
        var design = Design();
        var vector = TurbojetObjective.Pack(design);
        var roundTripped = TurbojetObjective.Unpack(vector);
        Assert.Equal(design, roundTripped);
    }

    [Fact]
    public void Evaluate_FeasibleDesign_ReturnsNegativeIspScore()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        var vector = TurbojetObjective.Pack(Design());
        var result = obj.Evaluate(vector);
        Assert.True(result.Score < 0,
            $"Feasible turbojet score should be −Isp (negative). Got {result.Score}.");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Evaluate_InfeasibleDesign_ReturnsPositiveInfinity()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        // φ above rich blowout fires COMBUSTOR_BLOWOUT_RICH gate.
        var vector = TurbojetObjective.Pack(Design(phi: 2.0));
        var result = obj.Evaluate(vector);
        Assert.Equal(double.PositiveInfinity, result.Score);
        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void Evaluate_WrongVectorLength_Throws()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        Assert.Throws<System.ArgumentException>(() => obj.Evaluate(new double[] { 0.1, 0.2, 0.3 }));
    }

    [Fact]
    public void Evaluate_DeterministicAcrossCalls()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        var v = TurbojetObjective.Pack(Design());
        var r1 = obj.Evaluate(v);
        var r2 = obj.Evaluate(v);
        Assert.Equal(r1.Score, r2.Score, 12);
    }

    [Fact]
    public void Variables_ProjectToBoundsArray()
    {
        var obj = TurbojetObjective.WithDefaultBounds(Cond);
        var bounds = DesignVariableInfo.ToBoundsArray(obj.Variables);
        Assert.Equal(obj.DimensionCount, bounds.Length);
        for (int i = 0; i < bounds.Length; i++)
            Assert.True(bounds[i].Min < bounds[i].Max);
    }
}
