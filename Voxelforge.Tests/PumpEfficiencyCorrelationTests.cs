// Sprint 34c (2026-04-25) / PH-8 companion — Stepanoff η correlation tests.

using Voxelforge.FeedSystem;
using Xunit;

namespace Voxelforge.Tests;

public class PumpEfficiencyCorrelationTests
{
    [Fact]
    public void Efficiency_AtPeakNs_ReturnsApproxLrePeakValue()
    {
        // Stepanoff peak ≈ 0.85 at N_s ≈ 2700, Q ≈ 200 gpm (anchor).
        double eta = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 2700, flowRate_gpm: 200);
        Assert.InRange(eta, 0.83, 0.87);
    }

    [Theory]
    [InlineData(600,  0.50, 0.60)]   // low-N_s paddle-ish radial impeller
    [InlineData(1500, 0.74, 0.82)]   // typical small-thrust pump
    [InlineData(2700, 0.83, 0.87)]   // peak band
    [InlineData(6000, 0.76, 0.84)]   // high-N_s mixed-flow
    [InlineData(9000, 0.68, 0.76)]   // upper bound
    public void Efficiency_AcrossNsRange_StaysWithinStepanoffBounds(double Ns, double lo, double hi)
    {
        double eta = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: Ns, flowRate_gpm: 200);
        Assert.InRange(eta, lo, hi);
    }

    [Fact]
    public void Efficiency_BelowLowerBound_ClampsToCurveLow()
    {
        // N_s = 100 is way below curve floor (600); should clamp to ~0.55.
        double eta = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 100, flowRate_gpm: 200);
        Assert.InRange(eta, 0.50, 0.60);
    }

    [Fact]
    public void Efficiency_AboveUpperBound_ClampsToCurveHigh()
    {
        // N_s = 20000 is above curve ceiling (9000); should clamp to ~0.72.
        double eta = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 20000, flowRate_gpm: 200);
        Assert.InRange(eta, 0.68, 0.76);
    }

    [Fact]
    public void Efficiency_NonPositiveInputs_FallsBackToLegacyConstant()
    {
        Assert.Equal(TurbopumpSizing.DefaultPumpEfficiency,
                     PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 0,    flowRate_gpm: 200));
        Assert.Equal(TurbopumpSizing.DefaultPumpEfficiency,
                     PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 2700, flowRate_gpm: 0));
        Assert.Equal(TurbopumpSizing.DefaultPumpEfficiency,
                     PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: -10,  flowRate_gpm: 200));
    }

    [Fact]
    public void Efficiency_SmallPump_LosesEfficiencyVsAnchor()
    {
        // Small pump (10 gpm) at peak N_s should lose ~5% to friction
        // per the Karassik Q-correction (linear in log-Q, anchored 200 gpm).
        double etaAnchor = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 2700, flowRate_gpm: 200);
        double etaSmall  = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 2700, flowRate_gpm: 10);
        Assert.True(etaSmall < etaAnchor, $"expected etaSmall ({etaSmall:F3}) < etaAnchor ({etaAnchor:F3})");
    }

    [Fact]
    public void Efficiency_LargePump_BeatsAnchor()
    {
        // Large pump (2000 gpm) at peak N_s gains ~4% from scale.
        double etaAnchor = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 2700, flowRate_gpm: 200);
        double etaLarge  = PumpEfficiencyCorrelation.Efficiency(specificSpeed_US: 2700, flowRate_gpm: 2000);
        Assert.True(etaLarge > etaAnchor, $"expected etaLarge ({etaLarge:F3}) > etaAnchor ({etaAnchor:F3})");
    }

    [Fact]
    public void Efficiency_AlwaysWithinPhysicalBounds()
    {
        // Random walk over the full param space — no extreme combination
        // should produce η outside [0.30, 0.92].
        double[] nsSamples = { 50, 600, 1000, 2500, 5000, 9000, 50000 };
        double[] qSamples  = { 0.1, 1, 10, 100, 1000, 10000, 100000 };
        foreach (var ns in nsSamples)
        foreach (var q in qSamples)
        {
            double eta = PumpEfficiencyCorrelation.Efficiency(ns, q);
            Assert.InRange(eta, 0.30, 0.92);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //   Production-path cascade verification
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void TurbopumpSizing_ReportsCorrelatedEfficiency_NotConstant()
    {
        // PH-8 companion verification: TurbopumpSizing.Size now reports
        // Stepanoff η on the PumpSizing result, not the input constant.
        //
        // PH-48 follow-up history:
        //   #274 (PR #309) replaced PH-48's conservative min(fuel_RPM,
        //     ox_RPM) common-shaft compromise with the geometric mean.
        //   #310 (this PR) replaced GMEAN with golden-section search.
        // On Merlin-class GG inputs both strategies land both pumps in
        // the 0.80-0.85 η band; OPT pushes the fuel pump slightly closer
        // to peak (~0.84 vs GMEAN's ~0.81). Threshold > 0.75 was relaxed
        // to > 0.70 during the PR #269 / PH-48 merge (when min(RPM)
        // pulled the fuel pump to η ≈ 0.71); GMEAN/OPT restore the
        // > 0.75 invariant.
        var cond = new Voxelforge.Optimization.OperatingConditions
        {
            EngineCycle = EngineCycle.GasGenerator,
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        // #310 use NPSH-realistic inputs (1.5 MPa inlet + inducer,
        // matching real LRE practice with boost pumps and inducers).
        // Previously this test ran at 0.4 MPa, no inducer — under
        // those conditions the GMEAN compromise (#274 / PR #309) was
        // SILENTLY producing NPSH-infeasible designs whose nominal η
        // exceeded 0.75. #310's NPSH-aware OPT correctly retreats to
        // NPSH-feasible RPMs, so the test had to be updated to inputs
        // with real NPSH headroom for OPT to demonstrate η > 0.75.
        var r = TurbopumpSizing.Size(
            cycle:                EngineCycle.GasGenerator,
            cond:                 cond,
            fuelFlow_kgs:         9.0,    // Merlin-1D class fuel flow
            oxFlow_kgs:           28.7,
            fuelDensity_kgm3:     420.0,
            oxDensity_kgm3:       1141.0,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa:   1.5e6,
            dischargePressure_Pa: 15e6,
            hasInducer:           true);

        Assert.NotNull(r.FuelPump);
        Assert.NotNull(r.OxPump);
        Assert.True(r.NPSHFeasible,
            "OPT must produce NPSH-feasible design — the η > 0.75 "
          + "claim is only meaningful at operating points the engine "
          + "can actually reach.");
        Assert.True(r.FuelPump!.Efficiency > 0.75,
            $"expected correlated η > 0.75 (post-#310 golden-section common-shaft RPM compromise), got {r.FuelPump.Efficiency:F3}");
        Assert.True(r.OxPump!.Efficiency  > 0.75,
            $"expected correlated η > 0.75 (post-#310 golden-section common-shaft RPM compromise), got {r.OxPump.Efficiency:F3}");
        Assert.NotEqual(TurbopumpSizing.DefaultPumpEfficiency, r.FuelPump.Efficiency);
    }

    [Fact]
    public void TurbopumpSizing_TinyAndLargePumps_HaveDifferentEfficiency()
    {
        // Pre-Sprint-34c: a 0.01 kg/s pump and a 50 kg/s pump both
        // reported the same constant 0.65. Post-PH-8 companion, the
        // small pump loses to friction while the large pump benefits
        // from scale — they MUST differ.
        var cond = new Voxelforge.Optimization.OperatingConditions
        {
            EngineCycle = EngineCycle.GasGenerator,
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var tiny = TurbopumpSizing.Size(
            cycle:                EngineCycle.GasGenerator, cond: cond,
            fuelFlow_kgs:         0.01, oxFlow_kgs: 0.034,
            fuelDensity_kgm3:     420.0, oxDensity_kgm3: 1141.0,
            fuelInletPressure_Pa: 0.4e6, oxInletPressure_Pa: 0.4e6,
            dischargePressure_Pa: 5e6);
        var large = TurbopumpSizing.Size(
            cycle:                EngineCycle.GasGenerator, cond: cond,
            fuelFlow_kgs:         50.0, oxFlow_kgs: 170.0,
            fuelDensity_kgm3:     420.0, oxDensity_kgm3: 1141.0,
            fuelInletPressure_Pa: 0.4e6, oxInletPressure_Pa: 0.4e6,
            dischargePressure_Pa: 25e6);

        Assert.NotEqual(tiny.FuelPump!.Efficiency, large.FuelPump!.Efficiency);
        Assert.True(tiny.FuelPump.Efficiency < large.FuelPump.Efficiency,
            $"expected tiny η ({tiny.FuelPump.Efficiency:F3}) < large η ({large.FuelPump.Efficiency:F3})");
    }
}
