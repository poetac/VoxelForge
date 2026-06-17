// GateOrderingSnapshotTests.cs — Sprint 0 PR-1 risk insurance.
//
// Pins the BYTE-IDENTICAL ConstraintId ordering of FeasibilityGate.Evaluate()
// across multi-violation fixtures. The existing FeasibilityGateTests.cs covers
// individual gate firing (each test injects exactly one violation), but no
// pre-existing test asserts the ORDER multiple violations come back in.
//
// Sprint 0 refactors FeasibilityGate.Evaluate() from a 1,150-line monolithic
// if-chain into a declarative GateRegistry. The migration's hard invariant is
// that gate-firing order must be preserved — Dictionary enumeration order is
// implementation-defined in C#, and a naive registry that iterates `.All`
// could silently shuffle the order. Existing single-violation tests would
// stay green even under shuffled order.
//
// These tests are the canary. They MUST pass against `main` HEAD at sprint
// start (capturing baseline) and STAY GREEN after every gate migration step.
// If a migration step breaks one of these, that's a real ordering drift —
// either the registry needs explicit ordering or a gate predicate has a side
// effect that depended on a previous gate having fired.
//
// Notes:
//   • Each fixture injects MULTIPLE violations from independent gate-blocks
//     so the ordering check is meaningful.
//   • Assertions compare Select(v => v.ConstraintId).ToArray() — values +
//     limits stay covered by the per-gate tests in FeasibilityGateTests.cs;
//     this file owns the ORDERING contract.
//   • SafeResult helper is duplicated from FeasibilityGateTests.cs rather
//     than promoted to internal — these tests are self-contained ordering
//     guards and shouldn't couple to the broader test fixture.

using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

// File lives in the Optimization/ folder for organization, but uses the flat
// `Voxelforge.Tests` namespace. A nested `Voxelforge.Tests.Optimization`
// namespace shadows references to `Optimization.OperatingConditions` etc. in sibling
// test files (C# resolves unqualified `Optimization.X` to nearest enclosing first).
namespace Voxelforge.Tests;

public class GateOrderingSnapshotTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Shared fixture — gate-clean baseline (mirrors FeasibilityGateTests.SafeResult)
    // ─────────────────────────────────────────────────────────────────

    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 1,   // CuCrZr: MaxServiceTemp_K = 800 K
        PropellantPair          = PropellantPair.LOX_CH4,
    };

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
    /// Gate-clean baseline result. Inject one or more violations via
    /// <c>with</c>-expressions and verify ordered ConstraintId sequence.
    /// </summary>
    private static RegenGenerationResult SafeResult()
    {
        var r   = RawResult();
        var mat = WallMaterials.All[DefaultConditions().WallMaterialIndex];
        var ch4 = MethaneFluid.Instance;

        return r with
        {
            Thermal = r.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,
                WallTempExceedsLimit = false,
                CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0,
            },
            Stress = r.Stress with
            {
                MinSafetyFactor = 2.5,
                YieldExceeded   = false,
            },
            Manufacturing = r.Manufacturing with
            {
                MinFeatureSize_mm = 0.55,
                FeatureSizeOK     = true,
            },
            Stability = r.Stability with
            {
                Composite       = StabilityRating.Pass,
                CompositeReason = "test-injected feasible",
            },
            IgniterType       = Geometry.IgniterType.SparkTorch,
            Contour           = r.Contour with { CharacteristicLength_m = 1.10 },
            BurstMarginFactor = 3.0,
        };
    }

    private static string[] Order(FeasibilityGateResult r)
        => System.Linq.Enumerable.ToArray(
               System.Linq.Enumerable.Select(r.Violations, v => v.ConstraintId));

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 1 — early-cluster violations (gates 1, 2, 3, 4, 5)
    //
    //  Inject WALL_TEMP, YIELD_EXCEEDED, FEATURE_TOO_SMALL,
    //  COOLANT_T_EXCEEDED, STABILITY_FAIL together. Pin the exact
    //  emission order. These are the first-five gates in Evaluate()
    //  in declaration order.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_EarlyClusterAllFive_OrderingIsStable()
    {
        var safe = SafeResult();
        var mat  = WallMaterials.All[DefaultConditions().WallMaterialIndex];
        var ch4  = MethaneFluid.Instance;

        var result = safe with
        {
            Thermal = safe.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K + 200.0,   // → WALL_TEMP
                WallTempExceedsLimit = true,
                CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K + 50.0, // → COOLANT_T_EXCEEDED
            },
            Stress = safe.Stress with
            {
                MinSafetyFactor = 0.70,                                // → YIELD_EXCEEDED
                YieldExceeded   = true,
            },
            Manufacturing = safe.Manufacturing with
            {
                MinFeatureSize_mm = 0.20,                              // → FEATURE_TOO_SMALL
                FeatureSizeOK     = false,
            },
            Stability = safe.Stability with
            {
                Composite       = StabilityRating.Fail,                // → STABILITY_FAIL
                CompositeReason = "test-injected unstable",
            },
        };

        var gate     = FeasibilityGate.Evaluate(result);
        var expected = new[]
        {
            "WALL_TEMP",
            "YIELD_EXCEEDED",
            "FEATURE_TOO_SMALL",
            "COOLANT_T_EXCEEDED",
            "STABILITY_FAIL",
        };
        Assert.Equal(expected, Order(gate));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 2 — late-cluster ordering: BURST + CONTRACTION + L_STAR
    //
    //  Verifies cross-block ordering: gates declared in Evaluate() at
    //  positions ~14c, ~29, ~32 must come out in declaration order.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_BurstAndContractionAndLStar_OrderingIsStable()
    {
        var safe = SafeResult();
        var result = safe with
        {
            BurstMarginFactor = 2.0,                                       // → BURST_MARGIN_INSUFFICIENT
            Contour = safe.Contour with
            {
                ContractionRatio       = 12.0,                              // → CONTRACTION_RATIO_OUT_OF_BAND
                CharacteristicLength_m = 0.50,                              // → L_STAR_BELOW_PROPELLANT_MIN
            },
        };

        var gate     = FeasibilityGate.Evaluate(result);
        var expected = new[]
        {
            "BURST_MARGIN_INSUFFICIENT",
            "CONTRACTION_RATIO_OUT_OF_BAND",
            "L_STAR_BELOW_PROPELLANT_MIN",
        };
        Assert.Equal(expected, Order(gate));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 3 — mixed early + late: WALL_TEMP + CONTRACTION_RATIO
    //
    //  Verifies that an early-block violation (WALL_TEMP) precedes a
    //  late-block violation (CONTRACTION_RATIO) regardless of registry
    //  enumeration order. Smallest meaningful cross-block test.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_EarlyAndLateMixed_OrderingIsStable()
    {
        var safe = SafeResult();
        var mat  = WallMaterials.All[DefaultConditions().WallMaterialIndex];

        var result = safe with
        {
            Thermal = safe.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K + 200.0,
                WallTempExceedsLimit = true,
            },
            Contour = safe.Contour with { ContractionRatio = 12.0 },
        };

        var gate     = FeasibilityGate.Evaluate(result);
        var expected = new[]
        {
            "WALL_TEMP",
            "CONTRACTION_RATIO_OUT_OF_BAND",
        };
        Assert.Equal(expected, Order(gate));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 4 — TPMS-topology ordering
    //
    //  TPMS topology selects two TPMS-specific gates that appear at
    //  positions ~15 (TPMS_CELL_FEATURE_TOO_SMALL) and ~36
    //  (TPMS_AND_MANIFOLD_OVERLAP). Both fire when the unit cell is
    //  too thin AND the manifolds overlap. Pin order.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_TpmsCellAndManifoldOverlap_OrderingIsStable()
    {
        var safe     = SafeResult();
        var totalLen = safe.Contour.TotalLength_mm;
        Assert.True(totalLen > 0,
            "SafeResult contour must have a real total length for this fixture.");

        var result = safe with
        {
            ChannelTopology   = ChannelTopology.TpmsSchwarzP,
            TpmsCellEdge_mm   = 1.0,
            TpmsSolidFraction = 0.30,                                       // strut = 0.3 mm < 2.0 mm floor
            ManifoldLength_mm = totalLen * 0.6,                             // 2 × 0.6 > total → overlap
        };

        var gate     = FeasibilityGate.Evaluate(result);
        var expected = new[]
        {
            "TPMS_CELL_FEATURE_TOO_SMALL",
            "TPMS_AND_MANIFOLD_OVERLAP",
        };
        Assert.Equal(expected, Order(gate));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 5 — gate-clean baseline produces zero violations
    //
    //  Pins that SafeResult() itself stays clean post-refactor — any
    //  spurious gate firing on the baseline indicates a registry bug.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_SafeResult_ProducesEmptyOrderedList()
    {
        var gate = FeasibilityGate.Evaluate(SafeResult());
        Assert.True(gate.IsFeasible,
            $"SafeResult should be gate-clean; got: "
          + string.Join(", ", Order(gate)));
        Assert.Empty(gate.Violations);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 6 — duplicate-gate emission ordering
    //
    //  CHANNEL_ASPECT_RATIO_EXCEEDED and CONTRACTION_RATIO_OUT_OF_BAND
    //  both fire on the same fixture (chamber bloat). Pin that
    //  CONTRACTION fires FIRST since gate 29 < gate 30 in declaration
    //  order. (Note: actual emission depends on Stations[] aspect-ratio
    //  values; we only set ContractionRatio here, so this is really a
    //  single-gate-vs-snapshot regression check.)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_ContractionRatioBelowFloor_OnlyOneViolationFires()
    {
        var safe   = SafeResult();
        var result = safe with { Contour = safe.Contour with { ContractionRatio = 2.0 } };

        var gate     = FeasibilityGate.Evaluate(result);
        var expected = new[] { "CONTRACTION_RATIO_OUT_OF_BAND" };
        Assert.Equal(expected, Order(gate));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Snapshot 7 — PreScreen ordering (PR-1 must preserve PreScreen
    //  semantics; if registry refactor unifies PreScreen + Evaluate
    //  this test catches the divergence).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_PreScreen_ContractionThenLStarThenTpms_FirstFireWins()
    {
        // PreScreen returns the FIRST violation it finds (not all). The order
        // checked is: CONTRACTION → L_STAR → TPMS. Pin that order so a
        // registry-based PreScreen rewrite can't silently shuffle.
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ContractionRatio       = 2.0,                  // would fire CONTRACTION
            CharacteristicLength_m = 0.50,                 // would also fire L_STAR (LOX/CH4 nominal=1.10m)
            ChannelTopology        = ChannelTopology.TpmsSchwarzP,
            TpmsCellEdge_mm        = 1.0,
            TpmsSolidFraction      = 0.30,                 // would also fire TPMS
        };

        var v = FeasibilityGate.PreScreen(cond, design);
        Assert.NotNull(v);
        Assert.Equal("CONTRACTION_RATIO_OUT_OF_BAND", v!.ConstraintId);
    }

    [Fact]
    public void Snapshot_PreScreen_LStarBeforeTpms_OrderPinned()
    {
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ContractionRatio       = 5.0,                  // safe
            CharacteristicLength_m = 0.50,                 // would fire L_STAR
            ChannelTopology        = ChannelTopology.TpmsSchwarzP,
            TpmsCellEdge_mm        = 1.0,
            TpmsSolidFraction      = 0.30,                 // would also fire TPMS
        };

        var v = FeasibilityGate.PreScreen(cond, design);
        Assert.NotNull(v);
        Assert.Equal("L_STAR_BELOW_PROPELLANT_MIN", v!.ConstraintId);
    }

    [Fact]
    public void Snapshot_PreScreen_TpmsLastInOrder()
    {
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            ContractionRatio       = 5.0,                  // safe
            CharacteristicLength_m = 1.10,                 // safe
            ChannelTopology        = ChannelTopology.TpmsSchwarzP,
            TpmsCellEdge_mm        = 1.0,
            TpmsSolidFraction      = 0.30,                 // fires TPMS
        };

        var v = FeasibilityGate.PreScreen(cond, design);
        Assert.NotNull(v);
        Assert.Equal("TPMS_CELL_FEATURE_TOO_SMALL", v!.ConstraintId);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Registry-completeness tests (Phase 2 final)
    //
    //  After Phase 2 migrates all 49 inline gates to RocketGates.RegisterAll,
    //  these tests pin the registry shape so a future addition that fails to
    //  call Register surfaces immediately.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_AllRocketRegenGatesRegistered_CountMatchesExpected()
    {
        // Expected = 38 distinct ConstraintIds emitted from the original
        // FeasibilityGate.Evaluate (rocket-regen path, excluding aerospike
        // and the optimizer-level VOXEL_RESOLUTION gate which lives in
        // RegenChamberOptimization). When this number changes intentionally,
        // update the snapshot list below to document which gates are in.
        var rocketRegenIds = new System.Collections.Generic.HashSet<string>(
            System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(
                    GateRegistry.All,
                    g => (g.Applicability & EngineFamilyMask.RocketRegen) != 0),
                g => g.Id));

        // The full ordered list of rocket-regen ConstraintIds, in registration
        // (= declaration) order. Pinned snapshot.
        var expected = new[]
        {
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
            "COMMON_SHAFT_RPM_INCONSISTENT",
            "TPMS_AND_MANIFOLD_OVERLAP",
            "BIMETALLIC_BOND_ZONE_SHEAR",
            "LCF_LIFE_INSUFFICIENT",
            // Sprint C / #350 (2026-05-04): combined axial-bending gate (PhysicsLimit/Hard).
            "COMBINED_AXIAL_BENDING_INSUFFICIENT",
            // OOB-6 / Sprint B-3 (2026-04-30): two advisory damper gates.
            "ACOUSTIC_DAMPER_DETUNED",
            "ACOUSTIC_DAMPER_OVERSIZED",
            // OOB-13 / E-D nozzle / issue #213 (2026-04-30): plug-clearance advisory.
            "EXPANSION_DEFLECTION_PLUG_CLEARANCE",
            // OOB-2 Sprint 3 / ADR-024 / issue #198 (2026-05-04): SIMP topology printability advisory.
            "TOPOLOGY_CHANNEL_NOT_PRINTABLE",
            // OOB-12 / issue #342 (2026-05-04): transpiration bleed excessive advisory.
            "TRANSPIRATION_BLEED_EXCESSIVE",
            // OOB-14 / issue #341 (2026-05-04): ablative throat recession budget (Hard) + interface overtemp (Advisory).
            "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET",
            "ABLATIVE_REGEN_INTERFACE_OVERTEMP",
            // OOB-9 / issue #344 (2026-05-05): finite-rate Isp penalty (Advisory). Self-guards when disabled.
            "FINITE_RATE_ISP_PENALTY_LARGE",
            // OOB-7 / issue #343 (2026-05-05): two RDE gates.
            "RDE_ANNULUS_FILL_STARVED",
            "RDE_WAVE_COUNT_BELOW_MINIMUM",
        };

        Assert.Equal(expected.Length, rocketRegenIds.Count);
        foreach (var id in expected)
            Assert.Contains(id, rocketRegenIds);
    }

    [Fact]
    public void Registry_OrderingMatchesDeclarationSequence()
    {
        // Pin the EXACT registration order. This is the canonical
        // declaration-order list that Evaluate() iterates; any reordering
        // would silently shift FeasibilityGateResult.Violations ordering for
        // multi-violation candidates.
        var expectedOrder = new[]
        {
            "WALL_TEMP", "YIELD_EXCEEDED", "FEATURE_TOO_SMALL", "COOLANT_T_EXCEEDED",
            "STABILITY_FAIL", "ELEMENT_DENSITY_TOO_HIGH", "PINTLE_BLOCKAGE_OUT_OF_BAND",
            "PINTLE_TMR_OUT_OF_BAND", "INJECTOR_FACE_T_EXCEEDED", "IGNITER_MISSING",
            "IGNITER_ENERGY_INSUFFICIENT", "IGNITER_MODALITY_UNSUITABLE",
            "FEED_PRESSURE_INSUFFICIENT", "BLOW_DOWN_INSUFFICIENT",
            "TAPOFF_HOT_GAS_TOO_HOT", "PURGE_FLOW_INSUFFICIENT",
            "CHILLDOWN_BUDGET_EXCEEDED", "ABLATIVE_BURNTHROUGH", "HARD_START_RISK",
            "NPSH_INSUFFICIENT", "PUMP_PRESSURE_INVERTED", "BURST_MARGIN_INSUFFICIENT",
            "TURBINE_POWER_DEFICIT", "EXPANDER_TURBINE_ENTHALPY_DEFICIT",
            "SHAFT_WHIRL", "PREBURNER_WALL_TEMP", "ORSC_PREBURNER_OXCORROSION",
            "TPMS_CELL_FEATURE_TOO_SMALL", "OVERHANG_ANGLE_EXCEEDED",
            "TRAPPED_POWDER_REGION", "DRAIN_PATH_MISSING",
            "INSTRUMENTATION_TAP_INTERFERENCE", "CONTRACTION_RATIO_OUT_OF_BAND",
            "CHANNEL_ASPECT_RATIO_EXCEEDED", "G_INJ_TOO_LOW", "G_INJ_TOO_HIGH",
            "L_STAR_BELOW_PROPELLANT_MIN", "PUMP_SPECIFIC_SPEED_OFF_BAND",
            "TURBINE_UNCHOKED", "INSTRUMENTATION_THERMAL_BRIDGE_RISK",
            "COMMON_SHAFT_RPM_INCONSISTENT", "TPMS_AND_MANIFOLD_OVERLAP",
            "BIMETALLIC_BOND_ZONE_SHEAR", "LCF_LIFE_INSUFFICIENT",
            // Sprint C / #350 (2026-05-04) — combined axial-bending gate.
            "COMBINED_AXIAL_BENDING_INSUFFICIENT",
            // OOB-6 / Sprint B-3 (2026-04-30) — advisory damper gates.
            "ACOUSTIC_DAMPER_DETUNED", "ACOUSTIC_DAMPER_OVERSIZED",
            // OOB-13 / E-D nozzle / issue #213 (2026-04-30) — plug-clearance advisory.
            "EXPANSION_DEFLECTION_PLUG_CLEARANCE",
            // OOB-2 Sprint 3 / ADR-024 / issue #198 (2026-05-04) — SIMP topology printability advisory.
            "TOPOLOGY_CHANNEL_NOT_PRINTABLE",
            // OOB-12 / issue #342 (2026-05-04) — transpiration bleed excessive advisory.
            "TRANSPIRATION_BLEED_EXCESSIVE",
            // OOB-14 / issue #341 (2026-05-04) — ablative throat gates.
            "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET",
            "ABLATIVE_REGEN_INTERFACE_OVERTEMP",
            // OOB-9 / issue #344 (2026-05-05) — finite-rate Isp penalty (Advisory). Self-guards when disabled.
            "FINITE_RATE_ISP_PENALTY_LARGE",
            // OOB-7 / issue #343 (2026-05-05) — two RDE gates.
            "RDE_ANNULUS_FILL_STARVED",
            "RDE_WAVE_COUNT_BELOW_MINIMUM",
        };

        var actualOrder = System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Select(GateRegistry.All, g => g.Id));

        Assert.Equal(expectedOrder, actualOrder);
    }

    [Fact]
    public void Registry_AllGatesAreRocketRegenMask()
    {
        // Phase 2 only migrates rocket-regen gates. Aerospike gates remain
        // in AerospikeFeasibility.Evaluate; air-breathing reservation is
        // just a commented-out enum value. So every registered gate today
        // must carry RocketRegen in its mask.
        foreach (var gate in GateRegistry.All)
        {
            Assert.True((gate.Applicability & EngineFamilyMask.RocketRegen) != 0,
                $"Gate '{gate.Id}' missing RocketRegen mask "
              + $"(applicability = {gate.Applicability}).");
        }
    }

    [Fact]
    public void Registry_TryGetByIdReturnsKnownGate()
    {
        Assert.True(GateRegistry.TryGetById("WALL_TEMP", out var wallTemp));
        Assert.NotNull(wallTemp);
        Assert.Equal("WALL_TEMP", wallTemp!.Id);
        Assert.Equal(GateKind.PhysicsLimit, wallTemp.Kind);

        Assert.False(GateRegistry.TryGetById("DEFINITELY_NOT_A_GATE", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Registry_ById_ReturnsRegisteredDescriptor()
    {
        var descriptor = GateRegistry.ById("BURST_MARGIN_INSUFFICIENT");
        Assert.NotNull(descriptor);
        Assert.Equal("BURST_MARGIN_INSUFFICIENT", descriptor.Id);
        Assert.Equal(GateKind.PhysicsLimit, descriptor.Kind);
        Assert.Equal(EngineFamilyMask.RocketRegen, descriptor.Applicability);
    }
}
