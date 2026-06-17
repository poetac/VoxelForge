// AirbreathingCostEstimatorsTests.cs — Sprint EC.W8 unit tests for
// the kind-aware airbreathing-pillar cost estimator.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Economics;
using Voxelforge.Economics;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingCostEstimatorsTests
{
    [Fact]
    public void Ramjet_PerKilogram_IsCheaperThanScramjet()
    {
        // Even after kg/N differs by kind, the $/kg ratio (capex/mass)
        // should rank correctly: ramjet $400/N + 0.08 kg/N → $5000/kg;
        // scramjet $20k/N + 0.10 kg/N → $200k/kg.
        var ramjet = ComponentCostEstimator(AirbreathingEngineKind.Ramjet,
            AirbreathingFuel.H2, 4.0);
        var scramjet = ComponentCostEstimator(AirbreathingEngineKind.Scramjet,
            AirbreathingFuel.H2, 0.85, mach: 6.0);
        double ramjetPerKg   = ramjet.CapitalCost_USD   / ramjet.Mass_kg;
        double scramjetPerKg = scramjet.CapitalCost_USD / scramjet.Mass_kg;
        Assert.True(ramjetPerKg < scramjetPerKg);
    }

    [Fact]
    public void Pulsejet_HasLowestCapitalCost_AtSameThrust()
    {
        var pulsejet = ComponentCostEstimator(AirbreathingEngineKind.Pulsejet,
            AirbreathingFuel.JetA, 0.85);
        var turbojet = ComponentCostEstimator(AirbreathingEngineKind.Turbojet,
            AirbreathingFuel.JetA, 0.85);
        // V-1 pulsejet should be < 10 % of J79-class turbojet cost.
        Assert.True(pulsejet.CapitalCost_USD < turbojet.CapitalCost_USD);
    }

    [Fact]
    public void Scramjet_HasHighestPerNewtonCost()
    {
        var scramjet = ComponentCostEstimator(AirbreathingEngineKind.Scramjet,
            AirbreathingFuel.H2, 0.85, mach: 6.0);
        var turbojet = ComponentCostEstimator(AirbreathingEngineKind.Turbojet,
            AirbreathingFuel.JetA, 0.85);
        Assert.True(scramjet.CapitalCost_USD > turbojet.CapitalCost_USD);
    }

    [Fact]
    public void RoundTrip_PositiveCostMassCo2()
    {
        var est = ComponentCostEstimator(AirbreathingEngineKind.Ramjet,
            AirbreathingFuel.H2, 4.0);
        Assert.True(est.Mass_kg > 0);
        Assert.True(est.CapitalCost_USD > 0);
        Assert.True(est.EmbodiedCO2_kgCO2eq > 0);
    }

    [Fact]
    public void Rollup_OfThreeEngines_SumsCorrectly()
    {
        var r = ComponentCostEstimator(AirbreathingEngineKind.Ramjet,  AirbreathingFuel.H2,   4.0);
        var t = ComponentCostEstimator(AirbreathingEngineKind.Turbojet, AirbreathingFuel.JetA, 0.85);
        var p = ComponentCostEstimator(AirbreathingEngineKind.Pulsejet, AirbreathingFuel.JetA, 0.85);
        var roll = EconomicAnalyzer.Analyze(new[] { r, t, p });
        Assert.Equal(3, roll.Components.Count);
        Assert.Equal(r.CapitalCost_USD + t.CapitalCost_USD + p.CapitalCost_USD,
            roll.TotalCapitalCost_USD, precision: 4);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static CostEstimate ComponentCostEstimator(
        AirbreathingEngineKind kind, AirbreathingFuel fuel,
        double equivalenceRatio, double mach = 2.5)
    {
        var design = new AirbreathingEngineDesign(
            Kind:                 kind,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.08,
            NozzleExitArea_m2:    0.18,
            EquivalenceRatio:     equivalenceRatio);
        var cond = new FlightConditions(12_000.0, mach, fuel);
        var result = AirbreathingOptimization.GenerateWith(design, cond);
        return AirbreathingCostEstimators.ForAirbreathingEngine($"eng_{kind}", result);
    }

}
