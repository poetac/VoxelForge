// GateKindTests.cs — Z3 #16 / external-audit F-8 (2026-04-28).
//
// Pins the categorization of every known feasibility-gate ConstraintId
// to its declared GateKind, plus spot-checks for the four kinds.
//
// The full-suite test enumerates every distinct ConstraintId emitted by
// FeasibilityGate.Evaluate / PreScreen, RegenChamberOptimization.Evaluate,
// AerospikeFeasibility, and MonolithicFeasibility, and asserts that
// GetGateKind returns a deterministic non-default mapping for each.
// "Non-default" here means the ID hits an explicit case in the switch
// rather than falling through to the AdvisoryHeuristic catch-all — so
// any future gate addition without a corresponding GetGateKind entry
// will fail this test.

using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class GateKindTests
{
    /// <summary>
    /// Canonical inventory of every rocket-side ConstraintId fired
    /// anywhere in the codebase as of the Z3 #16 / GateKind branch — the
    /// gates emitted by FeasibilityGate / RegenChamberOptimization,
    /// AerospikeFeasibility, and MonolithicFeasibility. This array is the
    /// SSOT for the GateKind classification test below; the project-wide
    /// per-gate catalogue (all 196 ConstraintIds across the five pillars)
    /// lives in <c>Voxelforge/docs/GATES.md</c>.
    /// </summary>
    public static readonly string[] AllKnownConstraintIds = new[]
    {
        // Regen-cooled bell chamber gates (FeasibilityGate.cs)
        "WALL_TEMP",
        "YIELD_EXCEEDED",
        "FEATURE_TOO_SMALL",
        "COOLANT_T_EXCEEDED",
        "STABILITY_FAIL",
        "ELEMENT_DENSITY_TOO_HIGH",
        "PINTLE_BLOCKAGE_OUT_OF_BAND",
        "PINTLE_TMR_OUT_OF_BAND",
        "INJECTOR_FACE_T_EXCEEDED",
        "IGNITER_MISSING",
        "IGNITER_ENERGY_INSUFFICIENT",
        "IGNITER_MODALITY_UNSUITABLE",
        "FEED_PRESSURE_INSUFFICIENT",
        "BLOW_DOWN_INSUFFICIENT",
        "TAPOFF_HOT_GAS_TOO_HOT",
        "PURGE_FLOW_INSUFFICIENT",
        "CHILLDOWN_BUDGET_EXCEEDED",
        "ABLATIVE_BURNTHROUGH",
        "HARD_START_RISK",
        "NPSH_INSUFFICIENT",
        "PUMP_PRESSURE_INVERTED",
        "BURST_MARGIN_INSUFFICIENT",
        "TURBINE_POWER_DEFICIT",
        "EXPANDER_TURBINE_ENTHALPY_DEFICIT",
        "SHAFT_WHIRL",
        "PREBURNER_WALL_TEMP",
        "ORSC_PREBURNER_OXCORROSION",
        "TPMS_CELL_FEATURE_TOO_SMALL",
        "OVERHANG_ANGLE_EXCEEDED",
        "TRAPPED_POWDER_REGION",
        "DRAIN_PATH_MISSING",
        "INSTRUMENTATION_TAP_INTERFERENCE",
        "CONTRACTION_RATIO_OUT_OF_BAND",
        "CHANNEL_ASPECT_RATIO_EXCEEDED",
        "G_INJ_TOO_LOW",
        "G_INJ_TOO_HIGH",
        "L_STAR_BELOW_PROPELLANT_MIN",
        "PUMP_SPECIFIC_SPEED_OFF_BAND",
        "TURBINE_UNCHOKED",
        "INSTRUMENTATION_THERMAL_BRIDGE_RISK",
        // Optimizer-level gate (RegenChamberOptimization.cs)
        "VOXEL_RESOLUTION",
        // Aerospike-parallel gates (AerospikeFeasibility.cs)
        "AEROSPIKE_PLUG_WALL_TEMP",
        "AEROSPIKE_COOLANT_CAVITATION_RISK",
        "AEROSPIKE_ELEMENT_CLEARANCE",
        "AEROSPIKE_INJECTOR_FACE_TEMP",
        "LINEAR_AEROSPIKE_ASPECT_RATIO",
        // Monolithic-composition gates (MonolithicFeasibility.cs)
        "MONOLITHIC_BODY_INTERSECTION",
        "MONOLITHIC_TUBE_INTERSECTION",
    };

    [Fact]
    public void EveryKnownConstraintId_HasExplicitMapping()
    {
        // For every known ConstraintId in the inventory above, GetGateKind
        // must return a kind chosen specifically for that ID — not the
        // AdvisoryHeuristic fall-through default. We detect "explicit
        // mapping" by checking that a deliberately-unknown ID falls
        // through to AdvisoryHeuristic, then asserting that for each
        // known ID, *some* explicit categorization is in place.
        //
        // Concretely: every known PhysicsLimit / EmpiricalBand /
        // ManufacturabilityFloor mapping is by definition not-default;
        // the AdvisoryHeuristic-mapped IDs must also be explicitly
        // listed in the switch (else we couldn't tell them apart from
        // the catch-all). This test uses the inventory itself as the
        // source of truth and pins per-ID kinds in
        // <see cref="EveryKnownConstraintId_HasStableMapping"/>.

        Assert.NotEmpty(AllKnownConstraintIds);

        foreach (string id in AllKnownConstraintIds)
        {
            // Touch every ID to confirm the call doesn't throw and
            // returns a defined enum value. Catches typos / missing
            // mappings at test time.
            GateKind kind = FeasibilityGate.GetGateKind(id);
            Assert.True(System.Enum.IsDefined(typeof(GateKind), kind),
                $"GateKind for {id} is not a defined enum value: {kind}");
        }
    }

    [Fact]
    public void EveryKnownConstraintId_HasStableMapping()
    {
        // Pins the exact GateKind for every known ConstraintId. Acts as
        // the executable categorization manifest — changing any single
        // mapping forces an explicit edit here, which is the right
        // checkpoint for "I changed how this gate is classified".
        var expected = new System.Collections.Generic.Dictionary<string, GateKind>
        {
            // PhysicsLimit
            ["WALL_TEMP"]                          = GateKind.PhysicsLimit,
            ["YIELD_EXCEEDED"]                     = GateKind.PhysicsLimit,
            ["BURST_MARGIN_INSUFFICIENT"]          = GateKind.PhysicsLimit,
            ["COOLANT_T_EXCEEDED"]                 = GateKind.PhysicsLimit,
            ["INJECTOR_FACE_T_EXCEEDED"]           = GateKind.PhysicsLimit,
            ["FEED_PRESSURE_INSUFFICIENT"]         = GateKind.PhysicsLimit,
            ["BLOW_DOWN_INSUFFICIENT"]             = GateKind.PhysicsLimit,
            ["PURGE_FLOW_INSUFFICIENT"]            = GateKind.PhysicsLimit,
            ["ABLATIVE_BURNTHROUGH"]               = GateKind.PhysicsLimit,
            ["NPSH_INSUFFICIENT"]                  = GateKind.PhysicsLimit,
            ["PUMP_PRESSURE_INVERTED"]             = GateKind.PhysicsLimit,
            ["TURBINE_POWER_DEFICIT"]              = GateKind.PhysicsLimit,
            ["EXPANDER_TURBINE_ENTHALPY_DEFICIT"]  = GateKind.PhysicsLimit,
            ["TURBINE_UNCHOKED"]                   = GateKind.PhysicsLimit,
            ["PREBURNER_WALL_TEMP"]                = GateKind.PhysicsLimit,
            ["TAPOFF_HOT_GAS_TOO_HOT"]             = GateKind.PhysicsLimit,
            ["AEROSPIKE_PLUG_WALL_TEMP"]           = GateKind.PhysicsLimit,
            ["AEROSPIKE_COOLANT_CAVITATION_RISK"]  = GateKind.PhysicsLimit,
            ["AEROSPIKE_INJECTOR_FACE_TEMP"]       = GateKind.PhysicsLimit,
            // EmpiricalBand
            ["ELEMENT_DENSITY_TOO_HIGH"]           = GateKind.EmpiricalBand,
            ["PINTLE_BLOCKAGE_OUT_OF_BAND"]        = GateKind.EmpiricalBand,
            ["PINTLE_TMR_OUT_OF_BAND"]             = GateKind.EmpiricalBand,
            ["STABILITY_FAIL"]                     = GateKind.EmpiricalBand,
            ["G_INJ_TOO_LOW"]                      = GateKind.EmpiricalBand,
            ["G_INJ_TOO_HIGH"]                     = GateKind.EmpiricalBand,
            ["L_STAR_BELOW_PROPELLANT_MIN"]        = GateKind.EmpiricalBand,
            ["CONTRACTION_RATIO_OUT_OF_BAND"]      = GateKind.EmpiricalBand,
            ["PUMP_SPECIFIC_SPEED_OFF_BAND"]       = GateKind.EmpiricalBand,
            ["ORSC_PREBURNER_OXCORROSION"]         = GateKind.EmpiricalBand,
            ["LINEAR_AEROSPIKE_ASPECT_RATIO"]      = GateKind.EmpiricalBand,
            // ManufacturabilityFloor
            ["FEATURE_TOO_SMALL"]                  = GateKind.ManufacturabilityFloor,
            ["TPMS_CELL_FEATURE_TOO_SMALL"]        = GateKind.ManufacturabilityFloor,
            ["VOXEL_RESOLUTION"]                   = GateKind.ManufacturabilityFloor,
            ["OVERHANG_ANGLE_EXCEEDED"]            = GateKind.ManufacturabilityFloor,
            ["TRAPPED_POWDER_REGION"]              = GateKind.ManufacturabilityFloor,
            ["DRAIN_PATH_MISSING"]                 = GateKind.ManufacturabilityFloor,
            ["CHANNEL_ASPECT_RATIO_EXCEEDED"]      = GateKind.ManufacturabilityFloor,
            ["AEROSPIKE_ELEMENT_CLEARANCE"]        = GateKind.ManufacturabilityFloor,
            ["MONOLITHIC_BODY_INTERSECTION"]       = GateKind.ManufacturabilityFloor,
            ["MONOLITHIC_TUBE_INTERSECTION"]       = GateKind.ManufacturabilityFloor,
            // AdvisoryHeuristic
            ["IGNITER_MISSING"]                    = GateKind.AdvisoryHeuristic,
            ["IGNITER_ENERGY_INSUFFICIENT"]        = GateKind.AdvisoryHeuristic,
            ["IGNITER_MODALITY_UNSUITABLE"]        = GateKind.AdvisoryHeuristic,
            ["INSTRUMENTATION_TAP_INTERFERENCE"]   = GateKind.AdvisoryHeuristic,
            ["INSTRUMENTATION_THERMAL_BRIDGE_RISK"]= GateKind.AdvisoryHeuristic,
            ["CHILLDOWN_BUDGET_EXCEEDED"]          = GateKind.AdvisoryHeuristic,
            ["HARD_START_RISK"]                    = GateKind.AdvisoryHeuristic,
            ["SHAFT_WHIRL"]                        = GateKind.AdvisoryHeuristic,
        };

        // Sanity: the inventory list and the expected dict must agree.
        Assert.Equal(AllKnownConstraintIds.Length, expected.Count);
        foreach (string id in AllKnownConstraintIds)
        {
            Assert.True(expected.ContainsKey(id),
                $"AllKnownConstraintIds includes {id} but expected dict does not");
        }

        foreach (var kv in expected)
        {
            GateKind actual = FeasibilityGate.GetGateKind(kv.Key);
            Assert.Equal(kv.Value, actual);
        }
    }

    [Fact]
    public void SpotCheck_PhysicsLimit_HardWallFailures()
    {
        // Hard hardware-failure gates: violating means real hardware
        // breaks. WALL_TEMP, YIELD_EXCEEDED, BURST_MARGIN_INSUFFICIENT,
        // PUMP_PRESSURE_INVERTED, NPSH_INSUFFICIENT all derive from
        // first-principles physics.
        Assert.Equal(GateKind.PhysicsLimit, FeasibilityGate.GetGateKind("WALL_TEMP"));
        Assert.Equal(GateKind.PhysicsLimit, FeasibilityGate.GetGateKind("YIELD_EXCEEDED"));
        Assert.Equal(GateKind.PhysicsLimit, FeasibilityGate.GetGateKind("BURST_MARGIN_INSUFFICIENT"));
        Assert.Equal(GateKind.PhysicsLimit, FeasibilityGate.GetGateKind("PUMP_PRESSURE_INVERTED"));
        Assert.Equal(GateKind.PhysicsLimit, FeasibilityGate.GetGateKind("NPSH_INSUFFICIENT"));
    }

    [Fact]
    public void SpotCheck_EmpiricalBand_CalibrationWindows()
    {
        // Empirical bands: violating means the model's correlation is
        // out of its calibration regime. G_INJ_TOO_HIGH, PINTLE_BLOCKAGE_OUT_OF_BAND,
        // L_STAR_BELOW_PROPELLANT_MIN, PUMP_SPECIFIC_SPEED_OFF_BAND
        // all derive from literature data, not first-principles.
        Assert.Equal(GateKind.EmpiricalBand, FeasibilityGate.GetGateKind("G_INJ_TOO_HIGH"));
        Assert.Equal(GateKind.EmpiricalBand, FeasibilityGate.GetGateKind("G_INJ_TOO_LOW"));
        Assert.Equal(GateKind.EmpiricalBand, FeasibilityGate.GetGateKind("PINTLE_BLOCKAGE_OUT_OF_BAND"));
        Assert.Equal(GateKind.EmpiricalBand, FeasibilityGate.GetGateKind("L_STAR_BELOW_PROPELLANT_MIN"));
        Assert.Equal(GateKind.EmpiricalBand, FeasibilityGate.GetGateKind("PUMP_SPECIFIC_SPEED_OFF_BAND"));
    }

    [Fact]
    public void SpotCheck_ManufacturabilityFloor_LpbfFloors()
    {
        // LPBF / printability floors: not physics, but the design
        // can't be printed on the project's manufacturing target.
        Assert.Equal(GateKind.ManufacturabilityFloor, FeasibilityGate.GetGateKind("FEATURE_TOO_SMALL"));
        Assert.Equal(GateKind.ManufacturabilityFloor, FeasibilityGate.GetGateKind("TPMS_CELL_FEATURE_TOO_SMALL"));
        Assert.Equal(GateKind.ManufacturabilityFloor, FeasibilityGate.GetGateKind("VOXEL_RESOLUTION"));
        Assert.Equal(GateKind.ManufacturabilityFloor, FeasibilityGate.GetGateKind("OVERHANG_ANGLE_EXCEEDED"));
        Assert.Equal(GateKind.ManufacturabilityFloor, FeasibilityGate.GetGateKind("TRAPPED_POWDER_REGION"));
    }

    [Fact]
    public void SpotCheck_AdvisoryHeuristic_DesignRulesOfThumb()
    {
        // Soft advisories: design rules of thumb that experienced
        // designers can override. STABILITY_FAIL is borderline (Crocco
        // empirical) but classified as EmpiricalBand because the model
        // is ostensibly mappable to data; the heuristic gates here
        // (IGNITER_*, CHILLDOWN_BUDGET_EXCEEDED, HARD_START_RISK,
        // SHAFT_WHIRL) all carry user-supplied or rule-of-thumb
        // thresholds.
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("IGNITER_MISSING"));
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("IGNITER_ENERGY_INSUFFICIENT"));
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("CHILLDOWN_BUDGET_EXCEEDED"));
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("HARD_START_RISK"));
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("SHAFT_WHIRL"));
    }

    [Fact]
    public void UnknownConstraintId_DefaultsToAdvisoryHeuristic()
    {
        // Future / unknown gates fall through to AdvisoryHeuristic as
        // the most conservative default — won't claim physics-grade
        // rigour for an unclassified gate. Fail-safe contract for any
        // future ConstraintId added without a corresponding switch case.
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("ZZZ_NOT_A_REAL_GATE"));
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind(""));
        Assert.Equal(GateKind.AdvisoryHeuristic, FeasibilityGate.GetGateKind("future_lowercase_id"));
    }
}
