// TapOffCycleTests.cs — Sprint 25 regression + invariant suite.
//
// Coverage:
//   • CycleSolver: TapOff registered + correct flags (no preburner,
//     has turbine, has turbopump, discharge dumps overboard)
//   • TapOffCycleSizing.Size:
//       - Null on non-TapOff cycles
//       - Null on degenerate chamber-P (P <= ambient)
//       - Tap-point T = BoundaryLayerFraction × Tc (J-2S heuristic)
//       - Tap mass flow = TapMassFlowFraction × total
//       - TapPointTemperatureOK flag flips when Tc × fraction > limit
//       - Open-tap back-pressure = ambient (not chamber-injection)
//   • Gate TAPOFF_HOT_GAS_TOO_HOT fires on infeasible tap-point T,
//     silent on feasible, silent on non-TapOff cycles.

using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class TapOffCycleTests
{
    // ── Solver flags ─────────────────────────────────────────────────

    [Fact]
    public void CycleSolver_TapOff_Registered()
    {
        var s = CycleSolvers.Get(EngineCycle.TapOff);
        Assert.NotNull(s);
        Assert.Equal(EngineCycle.TapOff, s.Cycle);
    }

    [Fact]
    public void CycleSolver_TapOff_HasExpectedFlags()
    {
        var s = CycleSolvers.Get(EngineCycle.TapOff);

        Assert.False(s.HasFuelRichPreburner);
        Assert.False(s.HasOxRichPreburner);
        Assert.Equal(0.0, s.PreburnerPcMultiplier,            precision: 6);
        Assert.Equal(0.0, s.FuelRichPreburnerMassFlowFraction, precision: 6);
        Assert.False(s.UsesFfscDualPreburnerSizing);
        Assert.True(s.HasTurbopump);
        Assert.False(s.HasElectricPowerConverter);
        Assert.True(s.HasTurbine);
        Assert.False(s.TurbineDischargeFeedsMainChamber);   // open — dumps overboard
    }

    [Fact]
    public void CycleSolver_TapOff_DiffersFromGasGeneratorOnlyOnPreburner()
    {
        // Both are open-discharge, turbine-driving cycles. Tap-off has
        // no preburner; GasGenerator has a fuel-rich one.
        var tap = CycleSolvers.Get(EngineCycle.TapOff);
        var gg  = CycleSolvers.Get(EngineCycle.GasGenerator);

        Assert.False(tap.HasFuelRichPreburner);
        Assert.True(gg.HasFuelRichPreburner);

        // Both open, both have turbine, both have turbopump.
        Assert.False(tap.TurbineDischargeFeedsMainChamber);
        Assert.False(gg.TurbineDischargeFeedsMainChamber);
        Assert.Equal(tap.HasTurbopump,  gg.HasTurbopump);
        Assert.Equal(tap.HasTurbine,    gg.HasTurbine);
    }

    // ── TapOffCycleSizing.Size ───────────────────────────────────────

    private const double ChamberT_K   = 3200.0;           // LOX/CH4-ish Tc
    private const double ChamberP_Pa  = 7e6;              // 7 MPa
    private const double TotalMdot    = 3.5;              // kg/s
    private const double Gamma        = 1.25;
    private const double MW_gmol      = 18.0;
    private const double PumpPower_W  = 200_000.0;        // 200 kW

    [Fact]
    public void TapOffSizing_ReturnsNull_OnNonTapOffCycle()
    {
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            if (c == EngineCycle.TapOff) continue;
            var r = TapOffCycleSizing.Size(
                cycle:                       c,
                chamberTemperature_K:        ChamberT_K,
                chamberPressure_Pa:          ChamberP_Pa,
                totalMassFlow_kgs:           TotalMdot,
                warmGasGamma:                Gamma,
                warmGasMolecularWeight_gmol: MW_gmol,
                requiredPumpShaftPower_W:    PumpPower_W);
            Assert.Null(r);
        }
    }

    [Fact]
    public void TapOffSizing_ReturnsNull_WhenChamberPBelowAmbient()
    {
        // Physically meaningless — forward expansion requires P_chamber > P_ambient.
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          0.05e6,         // below ambient
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);
        Assert.Null(r);
    }

    [Fact]
    public void TapOffSizing_TapPointTemperature_MatchesBoundaryLayerFraction()
    {
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);

        Assert.NotNull(r);
        Assert.Equal(
            TapOffCycleSizing.BoundaryLayerFraction * ChamberT_K,
            r!.TapPointTemperature_K,
            precision: 6);
    }

    [Fact]
    public void TapOffSizing_TapMassFlow_MatchesTapFraction()
    {
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);

        Assert.NotNull(r);
        Assert.Equal(
            TapOffCycleSizing.TapMassFlowFraction * TotalMdot,
            r!.TapMassFlow_kgs,
            precision: 9);
    }

    [Fact]
    public void TapOffSizing_TempOK_WhenChamberTc_Cool()
    {
        // Chamber Tc = 3000 K → tap point = 0.35 × 3000 = 1050 K < 1100 K limit.
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        3000.0,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);

        Assert.NotNull(r);
        Assert.True(r!.TapPointTemperatureOK);
        Assert.True(r.TapPointTemperature_K < r.TurbineInletLimit_K);
    }

    [Fact]
    public void TapOffSizing_TempNotOK_WhenChamberTc_Hot()
    {
        // Chamber Tc = 3500 K → tap point = 0.35 × 3500 = 1225 K > 1100 K limit.
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        3500.0,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);

        Assert.NotNull(r);
        Assert.False(r!.TapPointTemperatureOK);
        Assert.True(r.TapPointTemperature_K > r.TurbineInletLimit_K);
    }

    [Fact]
    public void TapOffSizing_OutletPressure_IsAmbient()
    {
        // Tap-off is open — turbine discharges to ambient.
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);

        Assert.NotNull(r);
        Assert.Equal(TapOffCycleSizing.AmbientBackPressure_Pa,
                     r!.OutletPressure_Pa, precision: 3);
    }

    [Fact]
    public void TapOffSizing_ProducesPositiveSpecificWork_OnForwardExpansion()
    {
        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W);

        Assert.NotNull(r);
        Assert.True(r!.IsentropicSpecificWork_Jkg > 0);
        Assert.True(r.ActualSpecificWork_Jkg > 0);
        Assert.Equal(r.Efficiency * r.IsentropicSpecificWork_Jkg,
                     r.ActualSpecificWork_Jkg, precision: 6);
    }

    // ── PH-49: axial-station knob ────────────────────────────────────

    [Fact]
    public void RegenChamberDesign_TapOffAxialStation_DefaultIsMidChamber()
    {
        var d = new RegenChamberDesign();
        Assert.Equal(0.5, d.TapOffAxialStation_frac, precision: 9);
    }

    [Fact]
    public void TapOffSizing_LocalGasTemperature_OverridesChamberT()
    {
        // When localGasTemperature_K is provided, T_tap = BLF × local T
        // rather than BLF × chamber T.
        const double localT = 2800.0;   // lower than ChamberT_K = 3200

        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W,
            localGasTemperature_K:       localT);

        Assert.NotNull(r);
        Assert.Equal(
            TapOffCycleSizing.BoundaryLayerFraction * localT,
            r!.TapPointTemperature_K, precision: 6);
    }

    [Fact]
    public void TapOffSizing_NullLocalT_FallsBackToChamberT()
    {
        // Null (default) falls back to chamber T — backward compatible with
        // the pre-PH-49 flat-chamber-T formula.
        var rExplicitNull = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W,
            localGasTemperature_K:       null);

        Assert.NotNull(rExplicitNull);
        Assert.Equal(
            TapOffCycleSizing.BoundaryLayerFraction * ChamberT_K,
            rExplicitNull!.TapPointTemperature_K, precision: 6);
    }

    [Fact]
    public void TapOffSizing_LowerLocalT_ProducesLowerTapTemp()
    {
        // A throat-station T (< Tc) produces a lower tap-point T than the
        // flat-Tc assumption — monotonicity check.
        const double throatT = 2816.0;  // ≈ Tc × 2/(γ+1) for Tc=3200, γ=1.25

        var rDefault = TapOffCycleSizing.Size(
            EngineCycle.TapOff, ChamberT_K, ChamberP_Pa,
            TotalMdot, Gamma, MW_gmol, PumpPower_W);

        var rThroat = TapOffCycleSizing.Size(
            EngineCycle.TapOff, ChamberT_K, ChamberP_Pa,
            TotalMdot, Gamma, MW_gmol, PumpPower_W,
            localGasTemperature_K: throatT);

        Assert.NotNull(rDefault);
        Assert.NotNull(rThroat);
        Assert.True(rThroat!.TapPointTemperature_K < rDefault!.TapPointTemperature_K);
    }

    [Fact]
    public void TapOffSizing_ChamberT_StillPresentOnResult()
    {
        // ChamberTemperature_K on the result always reflects the main-
        // chamber adiabatic T_c, regardless of the local station override.
        const double localT = 2800.0;

        var r = TapOffCycleSizing.Size(
            cycle:                       EngineCycle.TapOff,
            chamberTemperature_K:        ChamberT_K,
            chamberPressure_Pa:          ChamberP_Pa,
            totalMassFlow_kgs:           TotalMdot,
            warmGasGamma:                Gamma,
            warmGasMolecularWeight_gmol: MW_gmol,
            requiredPumpShaftPower_W:    PumpPower_W,
            localGasTemperature_K:       localT);

        Assert.NotNull(r);
        Assert.Equal(ChamberT_K, r!.ChamberTemperature_K, precision: 6);
    }

    // ── Gate integration ─────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_Fires_WhenTapPointTooHot()
    {
        // Synthesize a generation result with a TapOff infeasible
        // TapOffTurbine attached.
        var hotTap = new TapOffTurbineResult(
            ChamberTemperature_K:        3500.0,
            TapPointTemperature_K:       1225.0,      // > 1100
            TurbineInletLimit_K:         1100.0,
            TapPointTemperatureOK:       false,
            TapMassFlow_kgs:             0.1,
            TotalMassFlow_kgs:           3.5,
            ChamberPressure_Pa:          7e6,
            OutletPressure_Pa:           TapOffCycleSizing.AmbientBackPressure_Pa,
            Cp_Jkg_K:                    2300.0,
            EffectiveGamma:              1.25,
            IsentropicSpecificWork_Jkg:  500_000.0,
            ActualSpecificWork_Jkg:      300_000.0,
            Efficiency:                  0.60,
            AvailableShaftPower_W:       30_000.0,
            RequiredShaftPower_W:        200_000.0,
            PowerSufficient:             false,
            Notes:                       "test fixture hot-tap");

        var gen = MinimalGenerationResult() with { TapOffTurbine = hotTap };
        var gateResult = FeasibilityGate.Evaluate(gen);

        var v = System.Array.Find(gateResult.Violations,
            x => x.ConstraintId == "TAPOFF_HOT_GAS_TOO_HOT");
        Assert.NotNull(v);
        Assert.Equal(1225.0, v!.ActualValue, precision: 3);
        Assert.Equal(1100.0, v.Limit,        precision: 3);
    }

    [Fact]
    public void FeasibilityGate_Silent_WhenTapPointWithinLimit()
    {
        var okTap = new TapOffTurbineResult(
            ChamberTemperature_K:        3000.0,
            TapPointTemperature_K:       1050.0,     // OK
            TurbineInletLimit_K:         1100.0,
            TapPointTemperatureOK:       true,
            TapMassFlow_kgs:             0.1,
            TotalMassFlow_kgs:           3.5,
            ChamberPressure_Pa:          7e6,
            OutletPressure_Pa:           TapOffCycleSizing.AmbientBackPressure_Pa,
            Cp_Jkg_K:                    2300.0,
            EffectiveGamma:              1.25,
            IsentropicSpecificWork_Jkg:  500_000.0,
            ActualSpecificWork_Jkg:      300_000.0,
            Efficiency:                  0.60,
            AvailableShaftPower_W:       250_000.0,
            RequiredShaftPower_W:        200_000.0,
            PowerSufficient:             true,
            Notes:                       "test fixture ok tap");

        var gen = MinimalGenerationResult() with { TapOffTurbine = okTap };
        var gateResult = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(gateResult.Violations,
            v => v.ConstraintId == "TAPOFF_HOT_GAS_TOO_HOT");
    }

    [Fact]
    public void FeasibilityGate_Silent_WhenTapOffTurbineIsNull()
    {
        // Non-TapOff cycle → TapOffTurbine is null → gate skipped.
        var gen = MinimalGenerationResult();  // default PressureFed cond
        Assert.Null(gen.TapOffTurbine);
        var gateResult = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(gateResult.Violations,
            v => v.ConstraintId == "TAPOFF_HOT_GAS_TOO_HOT");
    }

    private static RegenGenerationResult MinimalGenerationResult()
    {
        // Cheapest known path to a valid RegenGenerationResult fixture —
        // matches the pattern used by ORSC / Expander / Dual-bell tests.
        var cond = new OperatingConditions();
        var design = new RegenChamberDesign();
        return RegenChamberOptimization.GenerateWith(cond, design);
    }
}
