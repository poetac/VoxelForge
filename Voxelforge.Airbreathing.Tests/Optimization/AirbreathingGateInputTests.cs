// AirbreathingGateInputTests.cs — coverage for the internal gate-input
// shim record. Audit 05-test-gaps.md Section 2 High.
//
// AirbreathingGateInput is the internal bundle AirbreathingFeasibility.
// Evaluate constructs before looping the registry. The shim is not
// exposed publicly, but Voxelforge.Airbreathing.Core's csproj grants
// InternalsVisibleTo to the test project, so we can construct + assert
// directly here.

using System.Collections.Generic;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class AirbreathingGateInputTests
{
    private static AirbreathingEngineDesign V1LikeDesign() =>
        new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:  0.030,
            CombustorArea_m2:    0.075,
            CombustorLength_m:   0.80,
            NozzleThroatArea_m2: 0.025,
            NozzleExitArea_m2:   0.040,
            EquivalenceRatio:    0.95);

    private static FlightConditions SeaLevelJp8() =>
        new(Altitude_m: 0.0, MachNumber: 0.0, Fuel: AirbreathingFuel.Jp8);

    private static StationMap EmptyStations() =>
        new(Stations:           new List<StationState>(),
            ThrustNet_N:         0.0,
            SpecificImpulse_s:   0.0,
            FuelMassFlow_kg_s:   0.0);

    [Fact]
    public void Ctor_StoresAllFiveFields()
    {
        var design = V1LikeDesign();
        var cond   = SeaLevelJp8();
        var smap   = EmptyStations();
        var cd     = new MapInfo(SurgeMargin: 0.20, CorrectedMassFlow_kg_s: 12.0, ChokeMarginRel: 0.90);
        var td     = new MapInfo(SurgeMargin: 0.15, CorrectedMassFlow_kg_s: 13.0, ChokeMarginRel: 0.95);

        var input = new AirbreathingGateInput(design, cond, smap, cd, td);

        Assert.Same(design, input.Design);
        Assert.Same(cond,   input.Conditions);
        Assert.Same(smap,   input.Stations);
        Assert.Equal(cd,    input.CompressorDiagnostics);
        Assert.Equal(td,    input.TurbineDiagnostics);
    }

    [Fact]
    public void Ctor_DiagnosticsAreNullableAndDefaultable()
    {
        // Most gates self-guard on diagnostics being null (ramjet / pulsejet
        // / scramjet have no compressor or turbine to diagnose).
        var input = new AirbreathingGateInput(
            V1LikeDesign(), SeaLevelJp8(), EmptyStations(),
            CompressorDiagnostics: null,
            TurbineDiagnostics:    null);

        Assert.Null(input.CompressorDiagnostics);
        Assert.Null(input.TurbineDiagnostics);
    }

    [Fact]
    public void Record_EqualityHonoursAllFields()
    {
        // The shim is a `record`, so structural equality fires across all
        // five constructor params.
        var d = V1LikeDesign();
        var c = SeaLevelJp8();
        var s = EmptyStations();

        var a = new AirbreathingGateInput(d, c, s, null, null);
        var b = new AirbreathingGateInput(d, c, s, null, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Record_EqualityDistinguishesDiagnostics()
    {
        var d = V1LikeDesign();
        var c = SeaLevelJp8();
        var s = EmptyStations();

        var a = new AirbreathingGateInput(d, c, s,
            new MapInfo(0.20, 12.0, 0.90), null);
        var b = new AirbreathingGateInput(d, c, s,
            new MapInfo(0.10, 12.0, 0.90), null);  // different SurgeMargin
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_CopiesAndOverridesField()
    {
        var input = new AirbreathingGateInput(V1LikeDesign(), SeaLevelJp8(),
            EmptyStations(), null, null);
        var ramjetDesign = V1LikeDesign() with { Kind = AirbreathingEngineKind.Ramjet };
        var copy = input with { Design = ramjetDesign };
        Assert.Equal(AirbreathingEngineKind.Pulsejet, input.Design.Kind);
        Assert.Equal(AirbreathingEngineKind.Ramjet,   copy.Design.Kind);
    }
}
