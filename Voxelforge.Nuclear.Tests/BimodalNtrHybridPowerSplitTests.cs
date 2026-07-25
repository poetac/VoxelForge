// BimodalNtrHybridPowerSplitTests.cs — issue #77: BimodalMode.Hybrid must
// split reactor power between the thrust cycle and the Brayton electric
// tap by energy conservation, instead of double-counting the full reactor
// output for both consumers.
//
// Pre-fix, NuclearOptimization.GenerateWith ran NtrCycleSolver.Solve with
// design.ReactorThermalPower_MW regardless of BimodalMode, then separately
// ran the Brayton loop against the same full reactor power — so a Hybrid
// design could draw more power than the reactor produces. Every test below
// that compares against a bare, unsplit NtrCycleSolver.Solve(design, cond)
// call fails on that pre-fix behaviour (Hybrid and "naive full power" were
// identical) and passes once the split is wired in.

using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class BimodalNtrHybridPowerSplitTests
{
    private static NuclearThermalDesign BaselineBimodal(BimodalMode mode = BimodalMode.Hybrid) =>
        new NuclearThermalDesign(
            Kind:                    NuclearKind.BimodalNtr,
            ReactorThermalPower_MW:  1.5,
            ReactorCoreLength_mm:    500.0,
            ReactorCoreDiameter_mm:  300.0,
            FuelLoadingFraction:     0.65,
            PropellantMassFlow_kgs:  0.5,
            ChamberPressure_bar:     40.0,
            ThroatRadius_mm:         50.0,
            ExpansionRatio:          100.0,
            NozzleLength_mm:         2000.0,
            RegenChannelDepth_mm:    2.0,
            RegenChannelCount:       80,
            NozzleWallThickness_mm:  1.5,
            NozzleChannelWidth_mm:   3.0,
            NozzleManifoldDepth_mm:  5.0) with
        {
            BimodalMode                  = mode,
            ElectricPowerTarget_kWe      = 100.0,
            BraytonTurbineInletTemp_K    = 1300.0,
            BraytonHePressure_bar        = 120.0,
            AlternatorRpm                = 45_000.0,
        };

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    // ── Wiring: GenerateWith actually passes (reactor − tap) to the cycle ───

    [Fact]
    public void HybridMode_CycleMatchesExplicitReactorMinusTapOverride()
    {
        var design = BaselineBimodal();
        var cond   = Cond();
        var r      = NuclearOptimization.GenerateWith(design, cond);

        double expectedPropellantPower_MW =
            design.ReactorThermalPower_MW - r.ReactorPowerToBrayton_MW;
        var direct = NtrCycleSolver.Solve(design, cond, expectedPropellantPower_MW);

        Assert.Equal(direct.ThrustVacuum_N, r.ThrustVacuum_N, precision: 9);
        Assert.Equal(direct.IspVacuum_s,    r.IspVacuum_s,    precision: 9);
        Assert.Equal(direct.CoreExitTemp_K, r.CoreExitTemp_K, precision: 9);
    }

    // ── Conservation: the failure mode issue #77 exists to close ────────────

    [Fact]
    public void HybridMode_ProducesLessThrustThanUnsplitFullReactorPower()
    {
        var design = BaselineBimodal();
        var cond   = Cond();
        var r      = NuclearOptimization.GenerateWith(design, cond);
        var naive  = NtrCycleSolver.Solve(design, cond);   // no override = full reactor power

        Assert.True(r.ReactorPowerToBrayton_MW > 0.0,
            "Baseline Hybrid design must tap nonzero Brayton power for this comparison to be meaningful.");
        Assert.True(r.ThrustVacuum_N < naive.ThrustVacuum_N,
            $"Hybrid-mode thrust ({r.ThrustVacuum_N:F1} N) must be below the naive "
          + $"full-reactor-power thrust ({naive.ThrustVacuum_N:F1} N) once the Brayton "
          + "tap is subtracted from the propellant-heating power.");
        Assert.True(r.CoreExitTemp_K < naive.CoreExitTemp_K);
    }

    [Fact]
    public void HybridMode_ProducesLessThrustThanThrustModeAtSameDesign()
    {
        var cond = Cond();
        var hybridResult = NuclearOptimization.GenerateWith(BaselineBimodal(BimodalMode.Hybrid), cond);
        var thrustResult = NuclearOptimization.GenerateWith(BaselineBimodal(BimodalMode.Thrust), cond);

        Assert.True(hybridResult.ThrustVacuum_N < thrustResult.ThrustVacuum_N,
            "Hybrid mode taps reactor power for the Brayton loop, so it must produce "
          + "strictly less thrust than an otherwise-identical Thrust-only design.");
        Assert.True(hybridResult.IspVacuum_s < thrustResult.IspVacuum_s);
    }

    // ── Q_vol stays a reactor-level metric, unaffected by the split ─────────

    [Fact]
    public void HybridMode_VolumetricHeatFluxUsesFullReactorPower_NotSplitPower()
    {
        var design = BaselineBimodal();
        var r = NuclearOptimization.GenerateWith(design, Cond());
        double expected = design.ReactorThermalPower_MW / design.ReactorCoreVolume_m3;
        Assert.Equal(expected, r.VolumetricHeatFlux_MWm3, precision: 6);
    }

    // ── Edge case: tap capped at 100 % of reactor power must stay finite ────

    [Fact]
    public void HybridMode_FullyCappedTap_StaysFiniteAndNonNegative()
    {
        // ElectricPowerTarget_kWe demands far more than the 1.5 MW reactor can
        // deliver, so BraytonGasLoopSolver caps the tap at the full reactor
        // output, leaving zero for the thrust cycle — a cold-flow (T_exit =
        // T_inlet) degenerate case, not a NaN/crash.
        var design = BaselineBimodal() with { ElectricPowerTarget_kWe = 1500.0 };
        var r = NuclearOptimization.GenerateWith(design, Cond());

        Assert.Equal(design.ReactorThermalPower_MW, r.ReactorPowerToBrayton_MW, precision: 9);
        Assert.True(double.IsFinite(r.ThrustVacuum_N) && r.ThrustVacuum_N >= 0.0);
        Assert.True(double.IsFinite(r.IspVacuum_s) && r.IspVacuum_s >= 0.0);
        Assert.Equal(80.0, r.CoreExitTemp_K, precision: 6);
    }

    // ── Thrust/Electric modes stay exactly as before this fix ───────────────

    [Fact]
    public void ThrustMode_CycleMatchesUnsplitFullReactorPower()
    {
        var design = BaselineBimodal(BimodalMode.Thrust);
        var cond   = Cond();
        var r      = NuclearOptimization.GenerateWith(design, cond);
        var direct = NtrCycleSolver.Solve(design, cond);

        Assert.Equal(direct.ThrustVacuum_N, r.ThrustVacuum_N, precision: 9);
        Assert.Equal(direct.CoreExitTemp_K, r.CoreExitTemp_K, precision: 9);
    }

    [Fact]
    public void ElectricMode_CoreExitTempMatchesUnsplitFullReactorPower()
    {
        var design = BaselineBimodal(BimodalMode.Electric);
        var cond   = Cond();
        var r      = NuclearOptimization.GenerateWith(design, cond);
        var direct = NtrCycleSolver.Solve(design, cond);

        // Thrust/Isp/c* are NaN'd for Electric mode by GenerateWith itself,
        // but the underlying core-exit temperature (which feeds the regen-
        // cooling advisory gate) must still reflect the full, un-split
        // reactor power — Electric mode is explicitly out of scope for
        // issue #77 (only Hybrid double-counted reactor power).
        Assert.Equal(direct.CoreExitTemp_K, r.CoreExitTemp_K, precision: 9);
    }
}
