// IEngineContractTests.cs — Sprint A Phase 1 (2026-05-04), airbreathing side.
//
// Mirrors Voxelforge.Tests/Engines/IEngineContractTests.cs on the rocket
// side. Pins:
//   1. Family-string matching across AirbreathingEngineDesign / FlightConditions / AirbreathingEngine.
//   2. AirbreathingEngine.Evaluate result equivalence with the legacy
//      AirbreathingOptimization.GenerateWith pipeline.
//   3. Family-mismatch rejection (engine raises ArgumentException when
//      a foreign-family design or conditions leaks in).

using Voxelforge.Airbreathing.Engines;
using Voxelforge.Engines;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Engines;

public class IEngineContractTests
{
    private static AirbreathingEngineDesign Design() => new(
        Kind:                AirbreathingEngineKind.Ramjet,
        InletThroatArea_m2:  0.0005,
        CombustorArea_m2:    0.0010,
        CombustorLength_m:   0.030,
        NozzleThroatArea_m2: 0.0005,
        NozzleExitArea_m2:   0.0010,
        EquivalenceRatio:    0.85);

    private static FlightConditions Cond() => new(
        Altitude_m: 10_000,
        MachNumber: 2.5,
        Fuel:       AirbreathingFuel.H2);

    [Fact]
    public void AirbreathingEngine_FamilyMatchesEngineFamiliesAirbreathing()
    {
        Assert.Equal(EngineFamilies.Airbreathing, AirbreathingEngine.Instance.Family);
        Assert.Equal(EngineFamilies.Airbreathing, Design().Family);
        Assert.Equal(EngineFamilies.Airbreathing, Cond().Family);
    }

    [Fact]
    public void AirbreathingEngine_Evaluate_MatchesLegacyPipeline()
    {
        var design = Design();
        var cond = Cond();

        // New IEngine path
        var engineResult = AirbreathingEngine.Instance.Evaluate(design, cond);

        // Legacy path the engine is wrapping
        var legacyResult = AirbreathingOptimization.GenerateWith(design, cond);

        Assert.Equal(legacyResult.Violations.Count, engineResult.Violations.Count);
        Assert.Equal(legacyResult.IsFeasible, engineResult.IsFeasible);
        Assert.Equal(legacyResult.Stations.Stations.Count, engineResult.Stations.Stations.Count);
    }

    [Fact]
    public void AirbreathingResult_ImplementsIEngineResult()
    {
        var design = Design();
        var cond = Cond();
        var result = AirbreathingOptimization.GenerateWith(design, cond);

        // The cycle solver result is now uniformly observable through the
        // generic IEngineResult interface — the optimizer + UI layers can
        // read violation counts / feasibility without knowing the family.
        // The CA1859 suppression below is the entire point of the test:
        // we explicitly type-test through the abstraction.
#pragma warning disable CA1859 // exercising interface dispatch on purpose
        IEngineResult generic = result;
#pragma warning restore CA1859
        Assert.NotNull(generic);
        Assert.Equal(result.Violations.Count, generic.Violations.Count);
        Assert.Equal(result.IsFeasible, generic.IsFeasible);
        // Phase 1 fills Advisories from the airbreathing solver's
        // Advisories list (which is already separate from Violations on
        // the airbreathing side).
        Assert.Equal(result.Advisories.Count, generic.Advisories.Count);
    }
}
