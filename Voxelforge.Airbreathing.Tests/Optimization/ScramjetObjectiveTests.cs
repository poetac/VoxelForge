// ScramjetObjectiveTests.cs — Sprint A10 unit tests for the scramjet
// IObjective adapter.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class ScramjetObjectiveTests
{
    private static ScramjetObjective Objective()
        => ScramjetObjective.AtNominalConditions();

    private static AirbreathingEngineDesign NominalDesign()
        => new(
            Kind:                    AirbreathingEngineKind.Scramjet,
            InletThroatArea_m2:      0.20,
            CombustorArea_m2:        0.30,
            CombustorLength_m:       1.50,
            NozzleThroatArea_m2:     0.25,
            NozzleExitArea_m2:       1.00,
            EquivalenceRatio:        0.60,
            IsolatorLength_m:        0.80);

    [Fact]
    public void DimensionCount_IsSeven()
    {
        Assert.Equal(7, Objective().DimensionCount);
    }

    [Fact]
    public void PackUnpack_RoundTrip_BitIdentical()
    {
        var design  = NominalDesign();
        var packed  = ScramjetObjective.Pack(design);
        var rebuilt = ScramjetObjective.Unpack(packed);

        Assert.Equal(design.InletThroatArea_m2,  rebuilt.InletThroatArea_m2);
        Assert.Equal(design.CombustorArea_m2,     rebuilt.CombustorArea_m2);
        Assert.Equal(design.CombustorLength_m,    rebuilt.CombustorLength_m);
        Assert.Equal(design.NozzleThroatArea_m2,  rebuilt.NozzleThroatArea_m2);
        Assert.Equal(design.NozzleExitArea_m2,    rebuilt.NozzleExitArea_m2);
        Assert.Equal(design.EquivalenceRatio,     rebuilt.EquivalenceRatio);
        Assert.Equal(design.IsolatorLength_m,     rebuilt.IsolatorLength_m);
        Assert.Equal(AirbreathingEngineKind.Scramjet, rebuilt.Kind);
    }

    [Fact]
    public void Evaluate_FeasibleDesign_ReturnsNegativeScore()
    {
        var obj    = Objective();
        var vector = ScramjetObjective.Pack(NominalDesign());
        var result = obj.Evaluate(vector);
        Assert.True(result.Score < 0.0,
            $"Feasible design should have Score = −Isp < 0, got {result.Score:F2}");
    }

    [Fact]
    public void Evaluate_InfeasibleDesign_ReturnsPositiveInfinity()
    {
        var obj = Objective();
        // φ = 0.01 is below the lean blowout floor → infeasible
        var infeasibleDesign = NominalDesign() with { EquivalenceRatio = 0.01 };
        var vector = ScramjetObjective.Pack(infeasibleDesign);
        var result = obj.Evaluate(vector);
        Assert.Equal(double.PositiveInfinity, result.Score);
    }

    [Fact]
    public void Evaluate_WrongVectorLength_Throws()
    {
        var obj = Objective();
        Assert.Throws<ArgumentException>(() => obj.Evaluate(new double[5]));
    }

    [Fact]
    public void Evaluate_Deterministic()
    {
        var obj    = Objective();
        var vector = ScramjetObjective.Pack(NominalDesign());
        var a      = obj.Evaluate(vector);
        var b      = obj.Evaluate(vector);
        Assert.Equal(a.Score, b.Score);
    }
}
