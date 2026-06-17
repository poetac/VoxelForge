// RamjetObjectiveTests.cs — Sprint A5 acceptance for the IObjective
// adapter wrapping the ramjet cycle solver.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class RamjetObjectiveTests
{
    private static readonly FlightConditions Cond =
        new(Altitude_m: 12_000.0, MachNumber: 2.0, Fuel: AirbreathingFuel.H2);

    [Fact]
    public void DimensionCount_MatchesDefaultBoundsLength()
    {
        var obj = RamjetObjective.WithDefaultBounds(Cond);
        Assert.Equal(6, obj.DimensionCount);
        Assert.Equal(6, obj.Variables.Count);
        Assert.Equal(RamjetObjective.DefaultVariableNames.Length, obj.DimensionCount);
    }

    [Fact]
    public void PackUnpack_RoundTrips()
    {
        var design = new AirbreathingEngineDesign(
            Kind:                 AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.0848,
            NozzleExitArea_m2:    0.20,
            EquivalenceRatio:     0.40);

        var vector = RamjetObjective.Pack(design);
        var roundTripped = RamjetObjective.Unpack(vector);

        Assert.Equal(design, roundTripped);
    }

    [Fact]
    public void Evaluate_FeasibleDesign_ReturnsNegativeIspScore()
    {
        var obj = RamjetObjective.WithDefaultBounds(Cond);
        var design = new AirbreathingEngineDesign(
            Kind:                 AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.0848,
            NozzleExitArea_m2:    0.20,
            EquivalenceRatio:     0.40);
        var result = obj.Evaluate(RamjetObjective.Pack(design));
        Assert.True(result.Score < 0,
            $"Feasible ramjet score should be −Isp (negative). Got {result.Score}.");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Evaluate_InfeasibleDesign_ReturnsPositiveInfinity()
    {
        var obj = RamjetObjective.WithDefaultBounds(Cond);
        var design = new AirbreathingEngineDesign(
            Kind:                 AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.0848,
            NozzleExitArea_m2:    0.20,
            EquivalenceRatio:     0.10);    // below LeanBlowoutPhi
        var result = obj.Evaluate(RamjetObjective.Pack(design));
        Assert.Equal(double.PositiveInfinity, result.Score);
        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void Evaluate_WrongVectorLength_Throws()
    {
        var obj = RamjetObjective.WithDefaultBounds(Cond);
        Assert.Throws<System.ArgumentException>(
            () => obj.Evaluate(new double[] { 0.1, 0.2, 0.3 }));
    }

    [Fact]
    public void Variables_RespectsBoundsArrayProjection()
    {
        var obj = RamjetObjective.WithDefaultBounds(Cond);
        var bounds = DesignVariableInfo.ToBoundsArray(obj.Variables);
        Assert.Equal(obj.DimensionCount, bounds.Length);
        for (int i = 0; i < bounds.Length; i++)
        {
            Assert.True(bounds[i].Min < bounds[i].Max);
        }
    }

    [Fact]
    public void Evaluate_DeterministicAcrossCalls()
    {
        // Strict-determinism contract — load-bearing for SA's
        // bit-identical-across-runs guarantee.
        var obj = RamjetObjective.WithDefaultBounds(Cond);
        var design = new AirbreathingEngineDesign(
            Kind:                 AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.0848,
            NozzleExitArea_m2:    0.20,
            EquivalenceRatio:     0.40);
        var v = RamjetObjective.Pack(design);
        var r1 = obj.Evaluate(v);
        var r2 = obj.Evaluate(v);
        Assert.Equal(r1.Score, r2.Score, 12);
    }
}
