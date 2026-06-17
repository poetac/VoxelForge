// FeasibilityGateTests.cs — Contract tests for UPGRADE 3: optimizer
// feasibility gate. Each test either:
//   (a) uses `with`-injection to build a provably-feasible or provably-infeasible
//       result, or
//   (b) runs a real GenerateWith to verify end-to-end wiring.
//
// Why `with`-injection for the feasible baseline?
//   The default 2224 N / 6.9 MPa LOX/CH4 design with 80 channels already
//   violates WALL_TEMP (CuCrZr 800 K limit, T_wg > 800 K without film) and
//   FEATURE_TOO_SMALL (80 channels can't fit at a 16 mm throat). Rather than
//   tuning physics parameters to thread the needle, we build the "feasible"
//   test fixture deterministically by clamping gate metrics to safe values.
//   This cleanly separates "gate logic" tests from "solver accuracy" tests.
//
// For violation tests, we always inject exactly ONE bad value so that
// Assert.Single(violations, pred) finds precisely one matching entry.

using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class FeasibilityGateTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Shared infrastructure
    // ─────────────────────────────────────────────────────────────────

    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N              = 2224.0,
        ChamberPressure_Pa    = 6.9e6,
        MixtureRatio          = 3.3,
        CoolantInletTemp_K    = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex     = 1,   // CuCrZr: MaxServiceTemp_K = 800, used by gate lookups
        PropellantPair        = PropellantPair.LOX_CH4,
    };

    /// <summary>
    /// Generate a result via the full solver (slow — cached).
    /// The raw result may or may not be gate-feasible depending on design physics.
    /// Use <see cref="SafeResult"/> when you need a guaranteed-feasible fixture.
    /// </summary>
    private static RegenGenerationResult? _rawCache;
    private static readonly object _rawLock = new();

    private static RegenGenerationResult RawResult()
    {
        lock (_rawLock)
            return _rawCache ??= RegenChamberOptimization.GenerateWith(
                DefaultConditions(),
                new RegenChamberDesign
                {
                    IncludeManifolds      = false,
                    IncludePorts          = false,
                    IncludeInjectorFlange = false,
                    ContourStationCount   = 60,
                });
    }

    /// <summary>
    /// Deterministically-feasible result: run the real solver, then clamp
    /// every gate-relevant metric to a value safely inside all five constraints.
    /// This lets us test gate logic without depending on specific physics output.
    /// </summary>
    private static RegenGenerationResult SafeResult()
    {
        var r   = RawResult();
        var mat = WallMaterials.All[DefaultConditions().WallMaterialIndex];   // CuCrZr, 800 K
        var ch4 = MethaneFluid.Instance;

        return r with
        {
            Thermal = r.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,   // 600 K < 800 K ✓
                WallTempExceedsLimit = false,
                CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0, // 800 K < 900 K ✓
            },
            Stress = r.Stress with
            {
                MinSafetyFactor = 2.5,   // > 1.0 ✓
                YieldExceeded   = false,
            },
            Manufacturing = r.Manufacturing with
            {
                MinFeatureSize_mm = 0.55,  // > 0.30 mm ✓
                FeatureSizeOK     = true,
            },
            Stability = r.Stability with
            {
                Composite       = StabilityRating.Pass,
                CompositeReason = "test-injected feasible",
            },
            // Sprint 29 (2026-04-24): the pre-Sprint-29 fixture left
            // IgniterType at None, which now correctly trips the
            // IGNITER_MISSING gate on LOX/CH4 (non-hypergolic).
            // SparkTorch is the minimum modality for LOX/CH4 per
            // Combustion.IgnitionRequirements.
            IgniterType = Geometry.IgniterType.SparkTorch,
            // Sprint 36 (2026-04-24): contour-derived L* on the small-
            // thrust fixture (2224 N at 6.9 MPa LOX/CH4) lands below the
            // new PH-11 L_STAR_BELOW_PROPELLANT_MIN floor (95 % of the
            // 1.10 m LOX/CH4 nominal = 1.045 m). Override to 1.10 m so
            // the safe-baseline fixture stays gate-clean; per-gate
            // violation tests retain their explicit-injection pattern.
            Contour = r.Contour with { CharacteristicLength_m = 1.10 },
            // Z2.8 (2026-04-28): the small-thruster fixture's default
            // wall (~1.0 mm) at 6.9 MPa MEOP gives a burst margin below
            // the new 2.5× ASME threshold. Override to 3.0× so the
            // safe-baseline fixture stays gate-clean — per-gate
            // violation tests retain their explicit-injection pattern.
            BurstMarginFactor = 3.0,
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  1. Deterministically feasible result passes all gates
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_SafeInjectedResult_PassesAllGates()
    {
        var gate = FeasibilityGate.Evaluate(SafeResult());
        Assert.True(gate.IsFeasible,
            $"Safe-injected result should be feasible; got {gate.Violations.Length} violation(s): "
          + string.Join(", ", Array.ConvertAll(gate.Violations, v => v.ConstraintId)));
        Assert.Empty(gate.Violations);
    }

    // ─────────────────────────────────────────────────────────────────
    //  2. Gate 1 — WALL_TEMP
    //     Inject peak wall T > CuCrZr service limit (800 K).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_WallTempAboveLimit_TriggersViolation()
    {
        var mat = WallMaterials.All[1];   // CuCrZr
        double overLimit = mat.MaxServiceTemp_K + 300.0;   // 1100 K

        // Start from safe baseline so only WALL_TEMP is violated
        var result = SafeResult() with
        {
            Thermal = SafeResult().Thermal with
            {
                PeakGasSideWallT_K   = overLimit,
                WallTempExceedsLimit = true,
            }
        };

        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "WALL_TEMP");
        Assert.Equal(overLimit,            v.ActualValue, precision: 1);
        Assert.Equal(mat.MaxServiceTemp_K, v.Limit,       precision: 1);
    }

    // ─────────────────────────────────────────────────────────────────
    //  3. Gate 2 — YIELD_EXCEEDED
    //     Inject SF = 0.80.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_SafetyFactorBelowOne_TriggersViolation()
    {
        var result = SafeResult() with
        {
            Stress = SafeResult().Stress with
            {
                MinSafetyFactor = 0.80,
                YieldExceeded   = true,
            }
        };

        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "YIELD_EXCEEDED");
        Assert.Equal(0.80, v.ActualValue, precision: 3);
        Assert.Equal(1.0,  v.Limit,       precision: 3);
    }

    // ─────────────────────────────────────────────────────────────────
    //  4. Gate 3 — FEATURE_TOO_SMALL
    //     Inject MinFeatureSize = 0.20 mm (below 0.30 mm LPBF floor).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_FeatureBelowLpbfFloor_TriggersViolation()
    {
        var result = SafeResult() with
        {
            Manufacturing = SafeResult().Manufacturing with
            {
                MinFeatureSize_mm = 0.20,
                FeatureSizeOK     = false,
            }
        };

        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "FEATURE_TOO_SMALL");
        Assert.Equal(0.20,                              v.ActualValue, precision: 3);
        Assert.Equal(FeasibilityGate.LpbfFeatureFloor_mm, v.Limit,    precision: 3);
    }

    // ─────────────────────────────────────────────────────────────────
    //  5. Gate 4 — COOLANT_T_EXCEEDED
    //     Inject coolant outlet T above CH4 service limit (900 K).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_CoolantOutletAboveFluidLimit_TriggersViolation()
    {
        double ch4Limit  = MethaneFluid.Instance.Metadata.MaxBulkT_K;   // 900 K
        double overLimit = ch4Limit + 100.0;

        var result = SafeResult() with
        {
            Thermal = SafeResult().Thermal with { CoolantOutletT_K = overLimit }
        };

        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "COOLANT_T_EXCEEDED");
        Assert.Equal(overLimit, v.ActualValue, precision: 1);
        Assert.Equal(ch4Limit,  v.Limit,       precision: 1);
    }

    // ─────────────────────────────────────────────────────────────────
    //  6. Gate 5 — STABILITY_FAIL
    //     Inject Composite = Fail.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_StabilityCompositeFail_TriggersViolation()
    {
        var result = SafeResult() with
        {
            Stability = SafeResult().Stability with
            {
                Composite       = StabilityRating.Fail,
                CompositeReason = "injected for test",
            }
        };

        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "STABILITY_FAIL");
    }

    // ─────────────────────────────────────────────────────────────────
    //  7. Multiple simultaneous violations are all collected (no fail-fast).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibilityGate_MultipleViolations_AllCollected()
    {
        var mat = WallMaterials.All[1];
        double ch4Limit = MethaneFluid.Instance.Metadata.MaxBulkT_K;

        var result = SafeResult() with
        {
            Thermal = SafeResult().Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K + 200.0,   // WALL_TEMP
                WallTempExceedsLimit = true,
                CoolantOutletT_K     = ch4Limit + 50.0,                // COOLANT_T_EXCEEDED
            },
            Stress = SafeResult().Stress with
            {
                MinSafetyFactor = 0.70,   // YIELD_EXCEEDED
                YieldExceeded   = true,
            },
        };

        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        Assert.True(gate.Violations.Length >= 3,
            $"Expected ≥ 3 violations, got {gate.Violations.Length}: "
          + string.Join(", ", Array.ConvertAll(gate.Violations, v => v.ConstraintId)));
        Assert.Contains(gate.Violations, v => v.ConstraintId == "WALL_TEMP");
        Assert.Contains(gate.Violations, v => v.ConstraintId == "YIELD_EXCEEDED");
        Assert.Contains(gate.Violations, v => v.ConstraintId == "COOLANT_T_EXCEEDED");
    }

    // ─────────────────────────────────────────────────────────────────
    //  8. Evaluate returns +∞ score when gate fires.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ReturnsPositiveInfinityScoreOnFeasibilityViolation()
    {
        var result = SafeResult() with
        {
            Stress = SafeResult().Stress with { MinSafetyFactor = 0.5, YieldExceeded = true }
        };

        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(result, RegenChamberOptimization.Profiles[0]);

        Assert.Equal(double.PositiveInfinity, score.TotalScore);
        Assert.True(score.FeasibilityViolations.Length > 0);
        Assert.Contains(score.FeasibilityViolations, v => v.ConstraintId == "YIELD_EXCEEDED");
    }

    // ─────────────────────────────────────────────────────────────────
    //  9. Evaluate returns finite score for the safe-injected result.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_FeasibleResult_ScoreIsFinite()
    {
        var score = RegenChamberOptimization.Evaluate(SafeResult(), RegenChamberOptimization.Profiles[0]);
        Assert.True(double.IsFinite(score.TotalScore),
            $"Safe-injected result score should be finite, got {score.TotalScore}.");
        Assert.Empty(score.FeasibilityViolations);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Sprint 36 (2026-04-24) — five new physics-cascade gates.
    //  All follow the inject-into-safe-baseline pattern.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Gate_PH17_ContractionRatioBelowFloor_TriggersViolation()
    {
        var result = SafeResult() with
        {
            Contour = SafeResult().Contour with { ContractionRatio = 2.0 } // < 2.5 floor
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "CONTRACTION_RATIO_OUT_OF_BAND");
        Assert.Equal(2.0, v.ActualValue, precision: 2);
        Assert.Equal(FeasibilityGate.ContractionRatioFloor, v.Limit, precision: 2);
    }

    [Fact]
    public void Gate_PH17_ContractionRatioAboveCeiling_TriggersViolation()
    {
        var result = SafeResult() with
        {
            Contour = SafeResult().Contour with { ContractionRatio = 12.0 } // > 10.0 ceiling
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.False(gate.IsFeasible);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "CONTRACTION_RATIO_OUT_OF_BAND");
        Assert.Equal(12.0, v.ActualValue, precision: 2);
        Assert.Equal(FeasibilityGate.ContractionRatioCeiling, v.Limit, precision: 2);
    }

    [Fact]
    public void Gate_PH17_ContractionRatioInBand_DoesNotFire()
    {
        // ε_c = 6.0 is the SafeResult default — should not fire.
        var gate = FeasibilityGate.Evaluate(SafeResult());
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "CONTRACTION_RATIO_OUT_OF_BAND");
    }

    [Fact]
    public void Gate_PH23_ChannelAspectRatioStrict_TriggersViolation()
    {
        var safe = SafeResult();
        // Inject a station with depth/width = 12 (> 10 strict ceiling).
        var stations = (StationResult[])safe.Thermal.Stations.Clone();
        stations[0] = stations[0] with
        {
            ChannelHeight_mm = 6.0,
            ChannelWidth_mm  = 0.5,
        };
        var result = safe with
        {
            Thermal = safe.Thermal with { Stations = stations }
        };
        var gate = FeasibilityGate.Evaluate(result);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "CHANNEL_ASPECT_RATIO_EXCEEDED");
        Assert.Equal(12.0, v.ActualValue, precision: 1);
        Assert.Equal(FeasibilityGate.ChannelAspectRatioStrict, v.Limit, precision: 1);
    }

    [Fact]
    public void Gate_PH23_ChannelAspectRatioWarn_TriggersViolation()
    {
        var safe = SafeResult();
        // depth/width = 9 (between 8 warn and 10 strict).
        var stations = (StationResult[])safe.Thermal.Stations.Clone();
        stations[0] = stations[0] with
        {
            ChannelHeight_mm = 4.5,
            ChannelWidth_mm  = 0.5,
        };
        var result = safe with
        {
            Thermal = safe.Thermal with { Stations = stations }
        };
        var gate = FeasibilityGate.Evaluate(result);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "CHANNEL_ASPECT_RATIO_EXCEEDED");
        Assert.Equal(9.0, v.ActualValue, precision: 1);
        Assert.Equal(FeasibilityGate.ChannelAspectRatioWarn, v.Limit, precision: 1);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Z2.8 — BURST_MARGIN_INSUFFICIENT (gate 14c)
    //  PR #104 raised the ProofTestAnalysis warning threshold to 2.5×
    //  (ASME BPVC §VIII Div 1) but didn't add a gate. Z2.8 closes the
    //  loophole: designs with margin < 2.5× now fail feasibility.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Gate_BurstMargin_BelowAsmeThreshold_TriggersViolation()
    {
        // Inject burst margin = 2.0× (was the pre-PH-33 PR #104 threshold);
        // gate fires now per the ASME BPVC §VIII Div 1 ground-test floor.
        var result = SafeResult() with { BurstMarginFactor = 2.0 };
        var gate = FeasibilityGate.Evaluate(result);
        var v = Assert.Single(gate.Violations,
            v => v.ConstraintId == "BURST_MARGIN_INSUFFICIENT");
        Assert.Equal(2.0, v.ActualValue, precision: 2);
        Assert.Equal(2.5, v.Limit, precision: 2);
    }

    [Fact]
    public void Gate_BurstMargin_AtThreshold_DoesNotTrigger()
    {
        // BurstMarginFactor exactly at 2.5 — the gate uses strict <,
        // so this should pass (matches ProofTestAnalysis warning convention).
        var result = SafeResult() with { BurstMarginFactor = 2.5 };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "BURST_MARGIN_INSUFFICIENT");
    }

    [Fact]
    public void Gate_BurstMargin_Zero_SkipsGate_LegacyCallSites()
    {
        // BurstMarginFactor == 0 short-circuits the gate so synthetic /
        // legacy call sites that don't populate it preserve pre-Z2.8
        // behaviour bit-identically. This is also the default value on
        // the record, so any test fixture that doesn't override the
        // field gets the gate-skip behaviour.
        var result = SafeResult() with { BurstMarginFactor = 0.0 };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "BURST_MARGIN_INSUFFICIENT");
    }

    [Fact]
    public void Gate_PH11_LStarBelowPropellantMin_TriggersViolation()
    {
        // LOX/CH4 nominal = 1.10 m; floor at 0.95 × = 1.045 m; inject 0.80 m.
        var result = SafeResult() with
        {
            Contour = SafeResult().Contour with { CharacteristicLength_m = 0.80 }
        };
        var gate = FeasibilityGate.Evaluate(result);
        var v = Assert.Single(gate.Violations, v => v.ConstraintId == "L_STAR_BELOW_PROPELLANT_MIN");
        Assert.Equal(0.80, v.ActualValue, precision: 2);
        Assert.True(v.Limit > 1.0,
            $"LOX/CH4 floor should be > 1.0 m (95 % of 1.10 m); got {v.Limit:F3}.");
    }

    [Fact]
    public void Gate_PH22_HighFluxBoss_OnHighConductivityWall_TriggersViolation()
    {
        // CuCrZr (k ≈ 300 W/m·K @ 500 K) wall vs assumed 16 W/m·K
        // stainless boss → conductivity delta ≈ 0.95, well above the 0.5
        // threshold. Use a uniform-peak profile so every station qualifies
        // as "high-flux" (q ≥ 80 % of peak), guaranteeing the boss lands
        // in the high-flux region regardless of AxialFraction-to-X_mm
        // mapping. Azimuth=22.5° lands the boss between channels (channel
        // pitch on the 20-channel default is 18°, so 22.5° is mid-rib at
        // channel index 0; far enough from any boss-to-channel clash to
        // keep INSTRUMENTATION_TAP_INTERFERENCE silent).
        var safe = SafeResult();
        var boss = new Geometry.SensorBoss(
            AxialFraction: 0.5,
            AzimuthDeg:    9.0,
            Type:          Geometry.SensorBossType.Pressure_M5);

        int n = safe.Thermal.Stations.Length;
        var stations = (StationResult[])safe.Thermal.Stations.Clone();
        for (int i = 0; i < n; i++)
            stations[i] = stations[i] with { HeatFlux_Wm2 = 8e6 };
        var result = safe with
        {
            Thermal      = safe.Thermal with { Stations = stations },
            SensorBosses = new[] { boss },
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "INSTRUMENTATION_THERMAL_BRIDGE_RISK");
    }

    [Fact]
    public void Gate_PH22_LowFluxBoss_DoesNotFire()
    {
        // Boss in a region with q far below 80 % of peak — should not fire.
        // Make stations' q a step profile: q = 1e5 in the first half, q = 8e6
        // in the second half. Boss at AxialFraction = 0.1 lands in the
        // low-flux zone (q ≈ 1e5; threshold = 6.4e6).
        var safe = SafeResult();
        var boss = new Geometry.SensorBoss(
            AxialFraction: 0.1,
            AzimuthDeg:    9.0,
            Type:          Geometry.SensorBossType.Pressure_M5);

        int n = safe.Thermal.Stations.Length;
        var stations = (StationResult[])safe.Thermal.Stations.Clone();
        for (int i = 0; i < n; i++)
        {
            double xfrac = (double)i / Math.Max(n - 1, 1);
            double q = xfrac < 0.5 ? 1e5 : 8e6;
            stations[i] = stations[i] with { HeatFlux_Wm2 = q };
        }
        var result = safe with
        {
            Thermal      = safe.Thermal with { Stations = stations },
            SensorBosses = new[] { boss },
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "INSTRUMENTATION_THERMAL_BRIDGE_RISK");
    }

    [Fact]
    public void Gate_PH21_GInjTooLow_TriggersViolation()
    {
        // ṁ_total ≈ 0.78 kg/s on the 2224 N LOX/CH4 fixture; for G_inj < 140
        // need A_total > 5.6 × 10⁻³ m² = 5600 mm². Inject 6500 mm² total
        // → G_inj ≈ 120 kg/(m²·s) → fires G_INJ_TOO_LOW.
        var safe = SafeResult();
        var perElem = new Injector.Elements.OrificeResult(
            OxOrificeArea_mm2:   100.0,
            FuelOrificeArea_mm2: 100.0,
            OxVelocity_ms:       30.0,
            FuelVelocity_ms:     30.0,
            VelocityRatio:       1.0,
            MomentumRatio:       1.0,
            Notes:               System.Array.Empty<string>());
        var sizing = new Injector.PatternSizingResult(
            ElementCount:      20,
            PerElementResult:  perElem,
            TotalOxArea_mm2:   3300,
            TotalFuelArea_mm2: 3300,
            FlowSplitCheck:    1.0,
            Warnings:          System.Array.Empty<string>());
        var result = safe with { InjectorSizing = sizing };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "G_INJ_TOO_LOW");
    }

    [Fact]
    public void Gate_PH21_GInjTooHigh_TriggersViolation()
    {
        // ṁ_total ≈ 0.78 kg/s on the SafeResult fixture; for G_inj > 50,000
        // need A_total < 0.78/50,000 = 1.56 × 10⁻⁵ m² = 15.6 mm². Inject 7 mm²
        // ox + 7 mm² fuel = 14 mm² total → G_inj ≈ 55,700 kg/(m²·s) → fires
        // G_INJ_TOO_HIGH. Threshold updated 2026-04-26 from 500 to 50,000 per
        // Sutton 9e Chapter 8 p. 270 (real engines run 7,000-16,000 kg/(m²·s);
        // F-1 / SSME / Merlin pinned by PublishedEngineInjectorMassFluxTests).
        var safe = SafeResult();
        var perElem = new Injector.Elements.OrificeResult(
            OxOrificeArea_mm2:   0.35,
            FuelOrificeArea_mm2: 0.35,
            OxVelocity_ms:       30.0,
            FuelVelocity_ms:     30.0,
            VelocityRatio:       1.0,
            MomentumRatio:       1.0,
            Notes:               System.Array.Empty<string>());
        var sizing = new Injector.PatternSizingResult(
            ElementCount:      20,
            PerElementResult:  perElem,
            TotalOxArea_mm2:   7,
            TotalFuelArea_mm2: 7,
            FlowSplitCheck:    1.0,
            Warnings:          System.Array.Empty<string>());
        var result = safe with { InjectorSizing = sizing };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "G_INJ_TOO_HIGH");
    }

    [Fact]
    public void Sprint36_NewGatesAreCensused()
    {
        // Pin the gate-census expectation so any additions in future sprints
        // surface here. Sprint 36 brings 5 new gates (PH-21 contributes 2 IDs
        // for the high/low band, so 6 unique ConstraintId values).
        var newIds = new[]
        {
            "CONTRACTION_RATIO_OUT_OF_BAND",
            "CHANNEL_ASPECT_RATIO_EXCEEDED",
            "G_INJ_TOO_LOW",
            "G_INJ_TOO_HIGH",
            "L_STAR_BELOW_PROPELLANT_MIN",
            "INSTRUMENTATION_THERMAL_BRIDGE_RISK",
        };
        Assert.Equal(6, newIds.Length);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Z3 #20 / Geometry B3 — TPMS_AND_MANIFOLD_OVERLAP advisory gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Gate_TpmsManifoldOverlap_FiresWhen2xManifoldExceedsChamberLength()
    {
        // Synthesise a TPMS fixture where 2 × ManifoldLength_mm > TotalLength_mm.
        // Use SafeResult as the baseline for a guaranteed-feasible starting
        // point and override the TPMS topology + manifold + total-length fields.
        // The contour's TotalLength_mm must be set (the gate skips silently when
        // it's 0); SafeResult's contour comes from a real solver run so it has
        // a real TotalLength_mm.
        var safe = SafeResult();
        var totalLen = safe.Contour.TotalLength_mm;
        Assert.True(totalLen > 0, "SafeResult contour must have a real total length.");

        var result = safe with
        {
            ChannelTopology   = ChannelTopology.TpmsSchwarzP,
            ManifoldLength_mm = totalLen * 0.6,    // 2 × 0.6 = 1.2 × total > total → fires
        };
        var gate = FeasibilityGate.Evaluate(result);
        var v = Assert.Single(gate.Violations,
            v => v.ConstraintId == "TPMS_AND_MANIFOLD_OVERLAP");
        Assert.True(v.ActualValue >= v.Limit,
            $"Expected ManifoldSpan ({v.ActualValue:F1}) >= TotalLength ({v.Limit:F1}).");
    }

    [Fact]
    public void Gate_TpmsManifoldOverlap_DoesNotFireWhenManifoldsFit()
    {
        // 2 × 0.3 = 0.6 × total < total → no overlap; gate silent.
        var safe = SafeResult();
        var result = safe with
        {
            ChannelTopology   = ChannelTopology.TpmsGyroid,
            ManifoldLength_mm = safe.Contour.TotalLength_mm * 0.3,
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "TPMS_AND_MANIFOLD_OVERLAP");
    }

    [Fact]
    public void Gate_TpmsManifoldOverlap_SkippedOnNonTpmsTopology()
    {
        // Same overlapping geometry on Axial topology — gate skipped.
        var safe = SafeResult();
        var result = safe with
        {
            ChannelTopology   = ChannelTopology.Axial,
            ManifoldLength_mm = safe.Contour.TotalLength_mm * 0.6,
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "TPMS_AND_MANIFOLD_OVERLAP");
    }

    [Fact]
    public void Gate_TpmsManifoldOverlap_LegacyZeroManifold_GateSilent()
    {
        // ManifoldLength_mm == 0 (default for legacy fixtures) → gate skipped.
        var safe = SafeResult();
        var result = safe with
        {
            ChannelTopology   = ChannelTopology.TpmsSchwarzP,
            ManifoldLength_mm = 0,
        };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "TPMS_AND_MANIFOLD_OVERLAP");
    }

    [Fact]
    public void Gate_TpmsManifoldOverlap_KindIsAdvisoryHeuristic()
    {
        Assert.Equal(GateKind.AdvisoryHeuristic,
                     FeasibilityGate.GetGateKind("TPMS_AND_MANIFOLD_OVERLAP"));
    }
}
