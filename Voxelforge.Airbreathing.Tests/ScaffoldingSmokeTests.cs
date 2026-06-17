// ScaffoldingSmokeTests.cs — Sprint A1 acceptance.
//
// Confirms the air-breathing project compiles, the Cycles registry
// behaves as documented (empty in A1), and the dispatch surface
// throws cleanly on unsupported kinds.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests;

public sealed class ScaffoldingSmokeTests
{
    [Fact]
    public void EngineDesign_RoundTripsThroughRecordWith()
    {
        var seed = new AirbreathingEngineDesign(
            Kind: AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2: 0.10,
            CombustorArea_m2: 0.30,
            CombustorLength_m: 0.50,
            NozzleThroatArea_m2: 0.08,
            NozzleExitArea_m2: 0.18,
            EquivalenceRatio: 0.85);

        var modified = seed with { EquivalenceRatio = 1.0 };

        Assert.Equal(0.85, seed.EquivalenceRatio);
        Assert.Equal(1.0, modified.EquivalenceRatio);
        Assert.NotEqual(seed, modified);
    }

    [Fact]
    public void FlightConditions_HoldsAltitudeAndMach()
    {
        var fc = new FlightConditions(
            Altitude_m: 12_000.0,
            MachNumber: 2.5,
            Fuel: AirbreathingFuel.H2);

        Assert.Equal(12_000.0, fc.Altitude_m);
        Assert.Equal(2.5, fc.MachNumber);
        Assert.Equal(AirbreathingFuel.H2, fc.Fuel);
    }

    [Fact]
    public void CycleSolverRegistry_HasAllFiveKindsPostA11()
    {
        // Sprint A4 wired Ramjet; A7 Turbojet; A8 Turbofan; A10 Scramjet;
        // A11 Rbcc (sub-step 1e capstone). All five are now registered.
        Assert.Contains(AirbreathingEngineKind.Ramjet,   AirbreathingCycleSolvers.SupportedKinds);
        Assert.Contains(AirbreathingEngineKind.Turbojet, AirbreathingCycleSolvers.SupportedKinds);
        Assert.Contains(AirbreathingEngineKind.Turbofan, AirbreathingCycleSolvers.SupportedKinds);
        Assert.Contains(AirbreathingEngineKind.Scramjet, AirbreathingCycleSolvers.SupportedKinds);
        Assert.Contains(AirbreathingEngineKind.Rbcc,     AirbreathingCycleSolvers.SupportedKinds);
        Assert.True(AirbreathingCycleSolvers.IsSupported(AirbreathingEngineKind.Ramjet));
        Assert.True(AirbreathingCycleSolvers.IsSupported(AirbreathingEngineKind.Turbojet));
        Assert.True(AirbreathingCycleSolvers.IsSupported(AirbreathingEngineKind.Turbofan));
        Assert.True(AirbreathingCycleSolvers.IsSupported(AirbreathingEngineKind.Scramjet));
        Assert.True(AirbreathingCycleSolvers.IsSupported(AirbreathingEngineKind.Rbcc));
    }

    [Fact]
    public void GenerateWith_Rbcc_RamjetMode_Succeeds()
    {
        // Post-A11: RbccCycleSolver is registered. GenerateWith should
        // dispatch cleanly and return a non-null result (not throw).
        var design = new AirbreathingEngineDesign(
            Kind: AirbreathingEngineKind.Rbcc,
            InletThroatArea_m2: 0.10,
            CombustorArea_m2: 0.30,
            CombustorLength_m: 0.50,
            NozzleThroatArea_m2: 0.08,
            NozzleExitArea_m2: 0.18,
            EquivalenceRatio: 0.55,
            RbccMode: RbccOperatingMode.Ramjet);
        var cond = new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2);

        var result = AirbreathingOptimization.GenerateWith(design, cond);

        Assert.NotNull(result);
    }
}
