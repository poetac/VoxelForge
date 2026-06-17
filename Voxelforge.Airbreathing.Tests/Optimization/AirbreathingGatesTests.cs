// AirbreathingGatesTests.cs — coverage for the AirbreathingGates static
// registration entry point. Audit 05-test-gaps.md Section 2 High.
//
// PulsejetGatesTests already covers PULSEJET_BLOWOUT_LEAN +
// PULSEJET_ACOUSTIC_OVERPRESSURE behaviour end-to-end via the public
// AirbreathingFeasibility.Evaluate surface. This file targets the
// AirbreathingGates static class directly: confirms RegisterAll
// registered exactly four documented gate IDs with their declared
// severity / kind / applicability metadata.

using System.Linq;
using Voxelforge.Airbreathing.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests.Optimization;

public sealed class AirbreathingGatesTests
{
    private static FeasibilityGateDescriptor<AirbreathingGateInput> ById(string id) =>
        AirbreathingGateRegistry.Instance.All.Single(g => g.Id == id);

    [Fact]
    public void RegisterAll_RegistersExactlyFourGates()
    {
        // RegisterAll fires lazily on first registry read.
        var ids = AirbreathingGateRegistry.Instance.All.Select(g => g.Id).ToArray();
        Assert.Equal(4, ids.Length);
    }

    [Fact]
    public void RegisterAll_RegistersAllFourDocumentedGateIds()
    {
        var ids = AirbreathingGateRegistry.Instance.All.Select(g => g.Id).ToArray();
        Assert.Contains("PULSEJET_BLOWOUT_LEAN",                ids);
        Assert.Contains("PULSEJET_ACOUSTIC_OVERPRESSURE",       ids);
        Assert.Contains("AFTERBURNER_LINER_OVERTEMP",           ids);
        Assert.Contains("TURBOPROP_SHAFT_POWER_INSUFFICIENT",   ids);
    }

    [Fact]
    public void PulsejetBlowoutLean_IsHardPhysicsLimit()
    {
        var g = ById("PULSEJET_BLOWOUT_LEAN");
        Assert.Equal(GateSeverity.Hard,        g.Severity);
        Assert.Equal(GateKind.PhysicsLimit,    g.Kind);
        Assert.Equal(EngineFamilyMask.Airbreathing, g.Applicability);
    }

    [Fact]
    public void PulsejetAcousticOverpressure_IsAdvisoryEmpiricalBand()
    {
        var g = ById("PULSEJET_ACOUSTIC_OVERPRESSURE");
        Assert.Equal(GateSeverity.Advisory,    g.Severity);
        Assert.Equal(GateKind.EmpiricalBand,   g.Kind);
        Assert.Equal(EngineFamilyMask.Airbreathing, g.Applicability);
    }

    [Fact]
    public void AfterburnerLinerOvertemp_IsHardPhysicsLimit()
    {
        var g = ById("AFTERBURNER_LINER_OVERTEMP");
        Assert.Equal(GateSeverity.Hard,        g.Severity);
        Assert.Equal(GateKind.PhysicsLimit,    g.Kind);
        Assert.Equal(EngineFamilyMask.Airbreathing, g.Applicability);
    }

    [Fact]
    public void TurbopropShaftPowerInsufficient_IsHardPhysicsLimit()
    {
        var g = ById("TURBOPROP_SHAFT_POWER_INSUFFICIENT");
        Assert.Equal(GateSeverity.Hard,        g.Severity);
        Assert.Equal(GateKind.PhysicsLimit,    g.Kind);
        Assert.Equal(EngineFamilyMask.Airbreathing, g.Applicability);
    }

    [Fact]
    public void AllGates_CarryNonEmptyAdrRef()
    {
        foreach (var g in AirbreathingGateRegistry.Instance.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.AdrRef),
                $"Gate {g.Id} should reference its ADR / physics anchor in AdrRef.");
        }
    }

    [Fact]
    public void HydrocarbonLflMassFraction_MatchesGlassmanTable()
    {
        // Glassman 1996 §3 Table 3.1 LFL fuel-air mass fraction floor.
        Assert.Equal(0.030, AirbreathingGates.HydrocarbonLflMassFraction, precision: 6);
    }

    [Fact]
    public void AcousticOverpressureCeiling_MatchesFoaThreshold()
    {
        // Foa 1960 §11.4 + NACA RM E50A04 V-1 instrumented data.
        Assert.Equal(1.30, AirbreathingGates.AcousticOverpressureCeiling, precision: 6);
    }

    [Fact]
    public void TurbopropPowerExtractionMinimum_MatchesMattinglyFloor()
    {
        Assert.Equal(0.50, AirbreathingGates.TurbopropPowerExtractionMinimum, precision: 6);
    }
}
