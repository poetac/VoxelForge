// CommonShaftRpmTests.cs — Issue #193 (PH-48 — common-shaft turbopump
// enforces ω_fuel = ω_ox).
//
// Covers three areas:
//   1. IsCommonShaft dispatch — returns true for every cycle that shares
//      one shaft, false for FFSC / electric / pressure-fed.
//   2. Sizing enforcement — TurbopumpSizing.Size() must produce identical
//      pump RPMs on common-shaft cycles when pumpRpm_rpm == 0 (auto-derive).
//   3. Gate 37 — COMMON_SHAFT_RPM_INCONSISTENT fires on any result whose
//      fuel/ox pump RPMs diverge by > 0.5 %, and is silent on
//      properly-sized designs.

using System.Linq;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class CommonShaftRpmTests
{
    // ── IsCommonShaft dispatch ───────────────────────────────────────────

    [Theory]
    [InlineData(EngineCycle.StagedCombustion, true)]
    [InlineData(EngineCycle.GasGenerator,     true)]
    [InlineData(EngineCycle.ORSC,             true)]
    [InlineData(EngineCycle.OpenExpander,     true)]
    [InlineData(EngineCycle.ClosedExpander,   true)]
    [InlineData(EngineCycle.TapOff,           true)]
    [InlineData(EngineCycle.FullFlow,         false)]
    [InlineData(EngineCycle.ElectricPump,     false)]
    [InlineData(EngineCycle.PressureFed,      false)]
    public void IsCommonShaft_ReturnsExpectedValue(EngineCycle cycle, bool expected)
    {
        Assert.Equal(expected, TurbopumpSizing.IsCommonShaft(cycle));
    }

    // ── Sizing enforcement: auto-derive path ─────────────────────────────

    private static TurbopumpResult SizeLoxCh4(EngineCycle cycle) =>
        TurbopumpSizing.Size(
            cycle:                cycle,
            cond:                 new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 },
            fuelFlow_kgs:         0.50,
            oxFlow_kgs:           1.70,
            fuelDensity_kgm3:     420.0,     // LCH4 near saturation
            oxDensity_kgm3:       1141.0,    // LOX
            fuelInletPressure_Pa: 0.4e6,
            oxInletPressure_Pa:   0.4e6,
            dischargePressure_Pa: 15e6);

    [Fact]
    public void StagedCombustion_FuelAndOxPump_ShareSameRpm()
    {
        var r = SizeLoxCh4(EngineCycle.StagedCombustion);
        Assert.NotNull(r.FuelPump);
        Assert.NotNull(r.OxPump);
        Assert.Equal(r.FuelPump!.Rpm, r.OxPump!.Rpm, precision: 3);
    }

    [Fact]
    public void GasGenerator_FuelAndOxPump_ShareSameRpm()
    {
        var r = SizeLoxCh4(EngineCycle.GasGenerator);
        Assert.NotNull(r.FuelPump);
        Assert.NotNull(r.OxPump);
        Assert.Equal(r.FuelPump!.Rpm, r.OxPump!.Rpm, precision: 3);
    }

    [Fact]
    public void CommonShaft_SharedRpm_IsEqualOnLoxLh2HighDiscrepancyCase()
    {
        // LOX/LH2 staged combustion: LH2 has ~16× lower density than LOX,
        // so unconstrained N_s-derived RPMs diverge significantly. After
        // enforcement, both pumps must report the same shaft speed.
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_H2 };
        var r = TurbopumpSizing.Size(
            cycle:                EngineCycle.StagedCombustion,
            cond:                 cond,
            fuelFlow_kgs:         0.10,
            oxFlow_kgs:           0.70,
            fuelDensity_kgm3:     71.0,      // LH2
            oxDensity_kgm3:       1141.0,    // LOX
            fuelInletPressure_Pa: 0.2e6,
            oxInletPressure_Pa:   0.2e6,
            dischargePressure_Pa: 8e6);
        Assert.NotNull(r.FuelPump);
        Assert.NotNull(r.OxPump);
        Assert.Equal(r.FuelPump!.Rpm, r.OxPump!.Rpm, precision: 3);
    }

    [Fact]
    public void ExplicitUserRpm_BothPumpsUseExactUserSpeed()
    {
        // When pumpRpm_rpm > 0, the user overrides shaft speed explicitly.
        // Both pumps already use the same value via SizeOnePump(userRpm:),
        // so no extra enforcement pass is needed. Confirm they match.
        const double userRpm = 25_000.0;
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var r = TurbopumpSizing.Size(
            cycle:                EngineCycle.StagedCombustion,
            cond:                 cond,
            fuelFlow_kgs:         0.50,
            oxFlow_kgs:           1.70,
            fuelDensity_kgm3:     420.0,
            oxDensity_kgm3:       1141.0,
            fuelInletPressure_Pa: 0.4e6,
            oxInletPressure_Pa:   0.4e6,
            dischargePressure_Pa: 15e6,
            pumpRpm_rpm:          userRpm);
        Assert.Equal(userRpm, r.FuelPump!.Rpm, precision: 3);
        Assert.Equal(userRpm, r.OxPump!.Rpm,   precision: 3);
    }

    // ── Gate 37: COMMON_SHAFT_RPM_INCONSISTENT ───────────────────────────

    private static RegenGenerationResult? _scCache;
    private static readonly object _scLock = new();

    private static RegenGenerationResult ScResult()
    {
        lock (_scLock)
        {
            if (_scCache is not null) return _scCache;
            var cond = new OperatingConditions
            {
                EngineCycle    = EngineCycle.StagedCombustion,
                PropellantPair = PropellantPair.LOX_CH4,
            };
            return _scCache = RegenChamberOptimization.GenerateWith(
                cond, new RegenChamberDesign());
        }
    }

    [Fact]
    public void Gate_SilentOnProperlyEnforcedDesign()
    {
        // Post-fix: TurbopumpSizing.Size() enforces equal RPMs on common-shaft
        // cycles. The gate must be silent on any result that went through the
        // normal sizing path.
        var gen = ScResult();
        var gate = FeasibilityGate.Evaluate(gen);
        bool fired = gate.Violations
            .Any(v => v.ConstraintId == "COMMON_SHAFT_RPM_INCONSISTENT");
        Assert.False(fired,
            "COMMON_SHAFT_RPM_INCONSISTENT should be silent on a properly-sized design");
    }

    [Fact]
    public void Gate_FiresOnSyntheticRpmMismatch()
    {
        // Inject mismatched pump RPMs directly into the result to confirm
        // the gate's regression-guard logic fires. Real sizing never
        // produces this state post-fix, but a code-path bug could.
        var gen  = ScResult();
        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelPump);
        Assert.NotNull(gen.Turbopump!.OxPump);

        var fuelP = gen.Turbopump!.FuelPump! with { Rpm = 50_000.0 };
        var oxP   = gen.Turbopump!.OxPump!  with { Rpm = 30_000.0 };
        var injected = gen with
        {
            Turbopump = gen.Turbopump with { FuelPump = fuelP, OxPump = oxP }
        };

        var gate = FeasibilityGate.Evaluate(injected);
        var v = gate.Violations
            .FirstOrDefault(v => v.ConstraintId == "COMMON_SHAFT_RPM_INCONSISTENT");
        Assert.NotNull(v);
        // |50000 - 30000| / 50000 = 40 % → ActualValue ≈ 40.0
        Assert.True(v!.ActualValue > 30.0,
            $"Expected discrepancy% > 30, got {v.ActualValue:F1}");
    }

    [Fact]
    public void Gate_SilentWhenRpmsAreEqualOnCommonShaft()
    {
        // Explicitly equal RPMs: no violation.
        var gen = ScResult();
        Assert.NotNull(gen.Turbopump);
        double sharedRpm = gen.Turbopump!.FuelPump?.Rpm ?? 20_000.0;
        var fuelP = gen.Turbopump!.FuelPump! with { Rpm = sharedRpm };
        var oxP   = gen.Turbopump!.OxPump!  with { Rpm = sharedRpm };
        var injected = gen with
        {
            Turbopump = gen.Turbopump with { FuelPump = fuelP, OxPump = oxP }
        };
        var gate = FeasibilityGate.Evaluate(injected);
        bool fired = gate.Violations
            .Any(v => v.ConstraintId == "COMMON_SHAFT_RPM_INCONSISTENT");
        Assert.False(fired, "Gate must be silent when fuel.Rpm == ox.Rpm");
    }

    // ── PH-48 follow-up (#274 + #310): compromise beats MIN ─────────────

    [Fact]
    public void OptimalRpm_BeatsMinRpm()
    {
        // PH-48 follow-up: the auto-derived common-shaft RPM compromise
        // must outperform the legacy min(fuel_RPM, ox_RPM) baseline by
        // ≥ 5 % in combined shaft power on a representative Merlin-class
        // LOX/CH₄ + GG design — operating point with realistic NPSH
        // headroom (1.5 MPa boost-pump-fed inlet + inducer, matching
        // real LRE hardware practice). #310 added NPSH-aware soft
        // penalty + min-RPM fallback in golden-section search; OPT now
        // lands just below the ox pump's NPSH cliff (~94k rpm here)
        // rather than at GMEAN's NPSH-infeasible 100k+ rpm.
        //
        // Pre-#310 history: PR #309 (#274) shipped GMEAN with a similar
        // 5 % assertion on artificially low (0.4 MPa) inlet conditions.
        // That assertion passed because GMEAN was producing
        // NPSH-INFEASIBLE designs at high RPM that nominally had lower
        // shaft power but couldn't actually run. #310's NPSH-aware OPT
        // makes this assertion truthful: improvement is real because
        // OPT only counts NPSH-feasible operating points.
        //
        // Baseline: explicit pumpRpm_rpm bypasses the auto-derive
        // enforcement block in TurbopumpSizing.Size (verified at
        // TurbopumpSizing.cs — userRpm > 0 takes priority over both
        // N_s-derived rpm and the common-shaft compromise). The
        // captured value 62562 rpm is the ox-pump's independent
        // N_s=2500 target on these inputs (ox is the constraining
        // lower one); replicates the pre-#274 min(fuel_RPM, ox_RPM)
        // PH-48 path exactly.
        const double LegacyMinRpm = 62_562.0;

        var cond = new OperatingConditions
        {
            EngineCycle    = EngineCycle.GasGenerator,
            PropellantPair = PropellantPair.LOX_CH4,
        };

        var baseline = TurbopumpSizing.Size(
            cycle:                EngineCycle.GasGenerator,
            cond:                 cond,
            fuelFlow_kgs:         9.0,
            oxFlow_kgs:           28.7,
            fuelDensity_kgm3:     420.0,
            oxDensity_kgm3:       1141.0,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa:   1.5e6,
            dischargePressure_Pa: 15e6,
            hasInducer:           true,
            pumpRpm_rpm:          LegacyMinRpm);

        var optimum = TurbopumpSizing.Size(
            cycle:                EngineCycle.GasGenerator,
            cond:                 cond,
            fuelFlow_kgs:         9.0,
            oxFlow_kgs:           28.7,
            fuelDensity_kgm3:     420.0,
            oxDensity_kgm3:       1141.0,
            fuelInletPressure_Pa: 1.5e6,
            oxInletPressure_Pa:   1.5e6,
            dischargePressure_Pa: 15e6,
            hasInducer:           true);

        Assert.NotNull(baseline.FuelPump);
        Assert.NotNull(baseline.OxPump);
        Assert.NotNull(optimum.FuelPump);
        Assert.NotNull(optimum.OxPump);
        Assert.True(optimum.NPSHFeasible,
            $"OPT must produce NPSH-feasible design on these inputs; "
          + $"got fuel NPSHA={optimum.FuelPump!.NPSHA_m:F1}m vs NPSHR={optimum.FuelPump.NPSHR_m:F1}m, "
          + $"ox NPSHA={optimum.OxPump!.NPSHA_m:F1}m vs NPSHR={optimum.OxPump.NPSHR_m:F1}m.");

        double baselinePower =
            baseline.FuelPump!.ShaftPower_W + baseline.OxPump!.ShaftPower_W;
        double optimumPower =
            optimum.FuelPump!.ShaftPower_W + optimum.OxPump!.ShaftPower_W;
        double improvement = (baselinePower - optimumPower) / baselinePower;

        Assert.True(improvement >= 0.05,
            $"Expected ≥ 5 % combined-shaft-power reduction vs. min(RPM) baseline; "
          + $"got {improvement:P2} (baseline {baselinePower:F0} W, optimum {optimumPower:F0} W, "
          + $"baselineRpm {LegacyMinRpm:F0}, optimumRpm {optimum.FuelPump!.Rpm:F0}).");
    }

    [Fact]
    public void OptimumRpm_RetreatsToNpshFeasibleRegion_OnLoxLh2HighDensityGap()
    {
        // PH-48 follow-up #310: investigated whether golden-section
        // search beats the closed-form geometric mean (#274 / PR #309)
        // on LOX/LH2 closed-expander designs (16× density gap). Found
        // that on real LH2 designs the dominant constraint is NOT the
        // Stepanoff η-vs-N_s curve asymmetry — it's NPSH feasibility.
        // GMEAN's high shared rpm (sqrt(fuel_indep × ox_indep) ≈ 123k
        // on RL10-class inputs) trips the ox pump's Thoma NPSHR. The
        // golden-section search with NPSH-aware soft penalty correctly
        // retreats to the lowest-RPM endpoint where both pumps stay
        // NPSH-feasible. This test pins the safety property: on
        // RL10-class LOX/LH2 with realistic inlet pressure + inducer,
        // OPT must produce an NPSH-feasible design (GMEAN as written
        // would not — it's a latent bug in #274's compromise on the
        // LH2 side).
        //
        // Operating point: RL10A-3-3A-class — fuelFlow 2.81 kg/s LH2,
        // oxFlow 14.04 kg/s LOX, fuel discharge 16 MPa (5×Pc closed-
        // expander), ox discharge 3.84 MPa (1.2×Pc), inlet 1 MPa,
        // hasInducer = true (matches real RL10).
        var cond = new OperatingConditions
        {
            EngineCycle        = EngineCycle.ClosedExpander,
            PropellantPair     = PropellantPair.LOX_H2,
            ChamberPressure_Pa = 3.2e6,
        };

        var optimum = TurbopumpSizing.Size(
            cycle:                  EngineCycle.ClosedExpander,
            cond:                   cond,
            fuelFlow_kgs:           2.81,
            oxFlow_kgs:             14.04,
            fuelDensity_kgm3:       71.0,
            oxDensity_kgm3:         1141.0,
            fuelInletPressure_Pa:   1.0e6,
            oxInletPressure_Pa:     1.0e6,
            dischargePressure_Pa:   16e6,
            oxDischargePressure_Pa: 3.84e6,
            hasInducer:             true);

        Assert.NotNull(optimum.FuelPump);
        Assert.NotNull(optimum.OxPump);

        // Common-shaft contract: both pumps run at the same shaft speed.
        Assert.Equal(optimum.FuelPump!.Rpm, optimum.OxPump!.Rpm, precision: 3);

        // Primary safety assertion: OPT must produce an NPSH-feasible
        // design on this case. GMEAN at 122_629 rpm would NOT.
        Assert.True(optimum.NPSHFeasible,
            $"OPT must retreat to NPSH-feasible RPM on RL10-class LOX/LH2; "
          + $"got fuel NPSHA={optimum.FuelPump.NPSHA_m:F1}m vs NPSHR={optimum.FuelPump.NPSHR_m:F1}m, "
          + $"ox NPSHA={optimum.OxPump.NPSHA_m:F1}m vs NPSHR={optimum.OxPump.NPSHR_m:F1}m, "
          + $"shared rpm {optimum.FuelPump.Rpm:F0}.");

        // Secondary assertion: explicit high-RPM (matches the GMEAN
        // landing point pre-#310) does NOT survive NPSH on these
        // inputs. Locks in the contrast: GMEAN's blind sqrt(rpms)
        // crossed the NPSH cliff; OPT's NPSH-penalised search did not.
        var gmeanLanding = TurbopumpSizing.Size(
            cycle:                  EngineCycle.ClosedExpander,
            cond:                   cond,
            fuelFlow_kgs:           2.81,
            oxFlow_kgs:             14.04,
            fuelDensity_kgm3:       71.0,
            oxDensity_kgm3:         1141.0,
            fuelInletPressure_Pa:   1.0e6,
            oxInletPressure_Pa:     1.0e6,
            dischargePressure_Pa:   16e6,
            oxDischargePressure_Pa: 3.84e6,
            hasInducer:             true,
            pumpRpm_rpm:            122_629.0);  // GMEAN landing pre-#310
        Assert.False(gmeanLanding.NPSHFeasible,
            "GMEAN's pre-#310 landing rpm 122_629 should NOT survive NPSH on "
          + "RL10-class LOX/LH2 — the latent bug #310 fixes.");
    }
}
