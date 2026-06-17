// RbccObjectiveTests.cs — Sprint A11 unit tests for RbccObjective.
//
// Covers: dimension count, Pack/Unpack round-trip, Evaluate score sign
// for a feasible design, and factory construction for all three modes.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class RbccObjectiveTests
{
    private static FlightConditions RamjetCond()
        => new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2);

    [Fact]
    public void DimensionCount_IsEight()
    {
        var obj = RbccObjective.WithDefaultBounds(RamjetCond());
        Assert.Equal(8, obj.DimensionCount);
        Assert.Equal(8, RbccObjective.DefaultVariableNames.Length);
        Assert.Equal(8, RbccObjective.DefaultBounds.Length);
    }

    [Fact]
    public void Pack_Unpack_RoundTrip_RamjetMode()
    {
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Rbcc,
            InletThroatArea_m2:      0.12,
            CombustorArea_m2:        0.35,
            CombustorLength_m:       0.60,
            NozzleThroatArea_m2:     0.09,
            NozzleExitArea_m2:       0.22,
            EquivalenceRatio:        0.55,
            IsolatorLength_m:        0.70,
            RbccMode:                RbccOperatingMode.Ramjet,
            EjectorEntrainmentRatio: 1.8);

        double[] packed = RbccObjective.Pack(design);
        var unpacked = RbccObjective.Unpack(packed, RbccOperatingMode.Ramjet);

        Assert.Equal(design.InletThroatArea_m2,      unpacked.InletThroatArea_m2,      precision: 10);
        Assert.Equal(design.CombustorArea_m2,         unpacked.CombustorArea_m2,         precision: 10);
        Assert.Equal(design.CombustorLength_m,        unpacked.CombustorLength_m,        precision: 10);
        Assert.Equal(design.NozzleThroatArea_m2,      unpacked.NozzleThroatArea_m2,      precision: 10);
        Assert.Equal(design.NozzleExitArea_m2,        unpacked.NozzleExitArea_m2,        precision: 10);
        Assert.Equal(design.EquivalenceRatio,         unpacked.EquivalenceRatio,         precision: 10);
        Assert.Equal(design.IsolatorLength_m,         unpacked.IsolatorLength_m,         precision: 10);
        Assert.Equal(design.EjectorEntrainmentRatio,  unpacked.EjectorEntrainmentRatio,  precision: 10);
        Assert.Equal(AirbreathingEngineKind.Rbcc, unpacked.Kind);
        Assert.Equal(RbccOperatingMode.Ramjet, unpacked.RbccMode);
    }

    [Fact]
    public void Evaluate_FeasibleRamjetModeDesign_ReturnsNegativeScore()
    {
        var obj = RbccObjective.WithDefaultBounds(
            RamjetCond(), RbccOperatingMode.Ramjet);

        // Design parameters that produce a feasible in-envelope ramjet result.
        double[] vector = RbccObjective.Pack(new AirbreathingEngineDesign(
            Kind:             AirbreathingEngineKind.Rbcc,
            InletThroatArea_m2: 0.10,
            CombustorArea_m2:   0.30,
            CombustorLength_m:  0.50,
            NozzleThroatArea_m2: 0.085,
            NozzleExitArea_m2:  0.20,
            EquivalenceRatio:   0.50,
            IsolatorLength_m:   0.50,
            RbccMode:           RbccOperatingMode.Ramjet,
            EjectorEntrainmentRatio: 1.0));

        var result = obj.Evaluate(vector);

        // A feasible, positive-Isp design should score negative (score = -Isp).
        Assert.True(result.Score < 0,
            $"Expected negative score (−Isp) for feasible RBCC design; got {result.Score:F2}.");
    }

    [Fact]
    public void WithDefaultBounds_ReturnsValidObjective_AllModesConstruct()
    {
        // All three factory calls should succeed without throwing.
        var ramjet = RbccObjective.WithDefaultBounds(
            new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2),
            RbccOperatingMode.Ramjet);
        var scramjet = RbccObjective.WithDefaultBounds(
            new FlightConditions(25_000.0, 7.0, AirbreathingFuel.H2),
            RbccOperatingMode.Scramjet);
        var ducted = RbccObjective.WithDefaultBounds(
            new FlightConditions(0.0, 0.5, AirbreathingFuel.H2),
            RbccOperatingMode.DuctedRocket);

        Assert.Equal(8, ramjet.DimensionCount);
        Assert.Equal(8, scramjet.DimensionCount);
        Assert.Equal(8, ducted.DimensionCount);
    }
}
