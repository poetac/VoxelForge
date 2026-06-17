// RocketGates.cs — Sprint 0 PR-1 Phase 2 (in flight): rocket-family
// feasibility gate registrations.
//
// This file is the migration target for the 49 gates currently emitted
// inline by FeasibilityGate.Evaluate(). Each gate becomes a static Emit*
// method that appends 0+ violations to a caller-supplied list, plus a
// RegisterAll() entry that wires the descriptor metadata.
//
// Migration is in declaration-order batches. After each batch:
//   • the inline if-block is removed from FeasibilityGate.Evaluate
//   • the gate is registered here
//   • GateOrderingSnapshotTests verifies emission order is preserved
//
// Internal visibility: this is a private impl detail of the gate
// framework. External callers consume gates via FeasibilityGate.Evaluate
// or GateRegistry.All; they never reach into this class directly.

using System.Collections.Generic;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Manufacturing;

namespace Voxelforge.Optimization;

internal static class RocketGates
{
    /// <summary>
    /// Register the rocket-family feasibility gates with
    /// <see cref="GateRegistry"/>. Called once at first registry access
    /// from <c>GateRegistry.EnsureInitialized</c>. Order = current
    /// declaration order in <see cref="FeasibilityGate.Evaluate"/>;
    /// preserved by <c>GateOrderingSnapshotTests</c>.
    /// </summary>
    public static void RegisterAll()
    {
        // Batch 1 (gates 1-5): early-cluster physics + manufacturability +
        // stability. All are independent of feed-system / cycle / printability
        // / TPMS / preburner state — fire on any rocket-regen design.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "WALL_TEMP",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 1",
            Emit:         EmitWallTemp));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "YIELD_EXCEEDED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 1",
            Emit:         EmitYieldExceeded));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "FEATURE_TOO_SMALL",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 1",
            Emit:         EmitFeatureTooSmall));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "COOLANT_T_EXCEEDED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 1",
            Emit:         EmitCoolantTExceeded));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "STABILITY_FAIL",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 1",
            Emit:         EmitStabilityFail));

        // Batch 2 (gates 6-7): injector cluster. ELEMENT_DENSITY +
        // PINTLE_BLOCKAGE + PINTLE_TMR all gate on InjectorPattern +
        // InjectorSizing presence; PINTLE_* additionally requires
        // ElementType == "Pintle". INJECTOR_FACE_T gates on InjectorFace
        // estimate presence. Each predicate is self-guarded so non-applicable
        // designs short-circuit silently.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ELEMENT_DENSITY_TOO_HIGH",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 1.3",
            Emit:         EmitElementDensityTooHigh));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "PINTLE_BLOCKAGE_OUT_OF_BAND",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 18 (Heister 2017)",
            Emit:         EmitPintleBlockage));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "PINTLE_TMR_OUT_OF_BAND",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 18 (Dressler / Heister)",
            Emit:         EmitPintleTmr));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "INJECTOR_FACE_T_EXCEEDED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PHASE 2 + PH-35",
            Emit:         EmitInjectorFaceT));

        // Batch 3 (gates 8-10): igniter cluster + feed-system + tap-off
        // + purge-flow. The 3 igniter gates are each guarded by
        // !ignitionReq.IsHypergolic AND the relevant per-gate condition
        // (None / energy floor / modality ordinal). Order preserved =
        // IGNITER_MISSING, IGNITER_ENERGY_INSUFFICIENT, IGNITER_MODALITY_UNSUITABLE.
        // The FEED_PRESSURE / BLOW_DOWN gates are paired (both read
        // FeedStackup); TAPOFF and PURGE_FLOW are independent.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "IGNITER_MISSING",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 29",
            Emit:         EmitIgniterMissing));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "IGNITER_ENERGY_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 29 + PH-12",
            Emit:         EmitIgniterEnergyInsufficient));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "IGNITER_MODALITY_UNSUITABLE",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 29",
            Emit:         EmitIgniterModalityUnsuitable));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "FEED_PRESSURE_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 8",
            Emit:         EmitFeedPressureInsufficient));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "BLOW_DOWN_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 19",
            Emit:         EmitBlowDownInsufficient));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TAPOFF_HOT_GAS_TOO_HOT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 25",
            Emit:         EmitTapOffHotGas));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "PURGE_FLOW_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 28",
            Emit:         EmitPurgeFlow));

        // Batch 4 (gates 12-15): chilldown / ablative / start-transient
        // / NPSH / pump-pressure-inverted / burst-margin. CHILLDOWN +
        // HARD_START + ABLATIVE are opt-in (analysis result null = silent).
        // NPSH + PUMP_INVERTED both read Turbopump but with different semantics
        // (NPSH = NPSHFeasible flag; INVERTED = post-clamp dP detection).
        // BURST_MARGIN is an additive Z2.8 gate (silent on legacy callers
        // that leave BurstMarginFactor at 0).
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "CHILLDOWN_BUDGET_EXCEEDED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Hot-fire Item 4",
            Emit:         EmitChilldownBudgetExceeded));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ABLATIVE_BURNTHROUGH",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 11",
            Emit:         EmitAblativeBurnthrough));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "HARD_START_RISK",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Hot-fire Item 4",
            Emit:         EmitHardStartRisk));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "NPSH_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 14",
            Emit:         EmitNpshInsufficient));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "PUMP_PRESSURE_INVERTED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / L2 PR #96",
            Emit:         EmitPumpPressureInverted));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "BURST_MARGIN_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Z2.8 PR #112",
            Emit:         EmitBurstMarginInsufficient));

        // Batch 5 (gates 16-18 + ORSC): turbine power deficit, expander
        // enthalpy deficit, shaft whirl, fuel-rich preburner, ox-rich
        // preburner. PREBURNER_WALL_TEMP is registered ONCE in the registry
        // even though it can fire from BOTH fuel-rich and ox-rich predicates;
        // we model that as one descriptor whose Emit method walks both sides.
        // ORSC_PREBURNER_OXCORROSION is a separate descriptor since it has
        // its own ConstraintId.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TURBINE_POWER_DEFICIT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 9",
            Emit:         EmitTurbinePowerDeficit));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "EXPANDER_TURBINE_ENTHALPY_DEFICIT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 23",
            Emit:         EmitExpanderTurbineEnthalpyDeficit));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "SHAFT_WHIRL",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 3",
            Emit:         EmitShaftWhirl));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "PREBURNER_WALL_TEMP",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 9 Track B",
            Emit:         EmitPreburnerWallTemp));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ORSC_PREBURNER_OXCORROSION",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 24",
            Emit:         EmitOrscPreburnerOxCorrosion));

        // Batch 6 (gates 15 + 25-28): TPMS strut + LPBF printability cluster
        // (3 gates, all opt-in via Printability null) + sensor-boss clash.
        // OVERHANG / TRAPPED_POWDER / DRAIN_PATH each guarded by their own
        // Has* flag on the printability result; INSTRUMENTATION_TAP only
        // fires when SensorBosses non-empty.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TPMS_CELL_FEATURE_TOO_SMALL",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Track A TPMS",
            Emit:         EmitTpmsCellTooSmall));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "OVERHANG_ANGLE_EXCEEDED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 27",
            Emit:         EmitOverhangAngleExceeded));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TRAPPED_POWDER_REGION",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 27",
            Emit:         EmitTrappedPowderRegion));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "DRAIN_PATH_MISSING",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 27",
            Emit:         EmitDrainPathMissing));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "INSTRUMENTATION_TAP_INTERFERENCE",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint 28",
            Emit:         EmitInstrumentationTapInterference));

        // Batch 7 (gates 29-32 + 35): contour + injector mass flux + L*
        // + pump specific speed. CONTRACTION + L_STAR are simple scalar
        // checks; CHANNEL_ASPECT iterates stations; G_INJ has TOO_LOW /
        // TOO_HIGH branches with the same Limit field; PUMP_SPECIFIC_SPEED
        // checks fuel + ox independently.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "CONTRACTION_RATIO_OUT_OF_BAND",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-17 Sprint 36",
            Emit:         EmitContractionRatioOutOfBand));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "CHANNEL_ASPECT_RATIO_EXCEEDED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-23 Sprint 36",
            Emit:         EmitChannelAspectRatioExceeded));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "G_INJ_TOO_LOW",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-21 Sprint 36",
            Emit:         EmitGInjTooLow));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "G_INJ_TOO_HIGH",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-21 Sprint 36",
            Emit:         EmitGInjTooHigh));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "L_STAR_BELOW_PROPELLANT_MIN",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-11 Sprint 36",
            Emit:         EmitLStarBelowPropellantMin));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "PUMP_SPECIFIC_SPEED_OFF_BAND",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.EmpiricalBand,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-8 Sprint 34b",
            Emit:         EmitPumpSpecificSpeedOffBand));

        // Batch 8 (final, gates 33-34 + 36-38): TURBINE_UNCHOKED has 4
        // sources (fuel turbine, ox turbine, expander, tap-off);
        // INSTRUMENTATION_THERMAL_BRIDGE_RISK fires per boss;
        // COMMON_SHAFT_RPM_INCONSISTENT is a regression guard;
        // TPMS_AND_MANIFOLD_OVERLAP guards against PicoGK pitfall #2;
        // BIMETALLIC_BOND_ZONE_SHEAR is advisory for CTE-mismatched walls.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TURBINE_UNCHOKED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-26 Sprint 34",
            Emit:         EmitTurbineUnchoked));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "INSTRUMENTATION_THERMAL_BRIDGE_RISK",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-22 Sprint 36",
            Emit:         EmitInstrumentationThermalBridgeRisk));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "COMMON_SHAFT_RPM_INCONSISTENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-48 PR #269",
            Emit:         EmitCommonShaftRpmInconsistent));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TPMS_AND_MANIFOLD_OVERLAP",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Z3 #20 Geometry B3",
            Emit:         EmitTpmsAndManifoldOverlap));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "BIMETALLIC_BOND_ZONE_SHEAR",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Z3-M2 PR #263",
            Emit:         EmitBimetallicBondZoneShear));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "LCF_LIFE_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / PH-40 issue #259",
            Emit:         EmitLcfLifeInsufficient));

        // Sprint C / #350 (2026-05-04): combined axial-bending structural gate.
        // Self-suppresses when GimbalOffset_mm = 0 (default) so all existing
        // designs remain gate-neutral. Fires when peak σ_VM (hoop + axial
        // membrane + bending extreme-fiber) > σ_y / 1.5 per Hibbeler §8.4.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "COMBINED_AXIAL_BENDING_INSUFFICIENT",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / Sprint C issue #350",
            Emit:         EmitCombinedAxialBendingInsufficient));

        // OOB-6 / Sprint B-3 (2026-04-30) — acoustic-damper advisory gates.
        // Both Severity = Advisory: the damper-tuning model is empirical
        // (Harrje & Reardon §8 simplification, Q ≈ 15 anchor) so a Hard
        // fail on it would be making a strong claim on weak data. The
        // gates surface as warnings on the build sheet + report exporter
        // and the SA optimizer treats them as zero-cost; users see
        // "ACOUSTIC_DAMPER_DETUNED — f₀ 8230 Hz, nearest mode T1
        // 12100 Hz, detune 32 %" and can retune. Self-suppress when no
        // damper is configured (DamperType = None ⇒ no AcousticDamper
        // result on StabilityReport).
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ACOUSTIC_DAMPER_DETUNED",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / OOB-6 issue #200",
            Emit:         EmitAcousticDamperDetuned));
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ACOUSTIC_DAMPER_OVERSIZED",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / OOB-6 issue #200",
            Emit:         EmitAcousticDamperOversized));

        // OOB-13 / E-D nozzle (2026-04-30, issue #213): advisory gate fires
        // when the cowl throat radius for an ExpansionDeflection design is below
        // 12 mm — at that scale the inner plug tip (40 % of cowl = 4.8 mm) is
        // too small for an LPBF-manufacturable plug with any internal geometry.
        // Severity = Advisory because the physics model still runs; the gate
        // warns rather than blocks so a designer can deliberately accept a
        // micro-thruster scale with an uncooled solid plug.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "EXPANSION_DEFLECTION_PLUG_CLEARANCE",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-009 / OOB-13 issue #213",
            Emit:         EmitExpansionDeflectionPlugClearance));

        // OOB-2 Sprint 3 (ADR-024): SIMP topology-channel printability check.
        // Fires when any station's topology-optimized channel is narrower than
        // the universal LPBF feature floor (0.30 mm). Advisory only — a narrow
        // station is informational (the voxel builder clips to 0.3 mm internally);
        // the designer may accept it or reduce N_base. Silent on all other
        // topologies (TopologyChannels == null short-circuits the predicate).
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TOPOLOGY_CHANNEL_NOT_PRINTABLE",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.ManufacturabilityFloor,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "ADR-024 / OOB-2 issue #198",
            Emit:         EmitTopologyChannelNotPrintable));

        // OOB-12 (2026-05-04): transpiration bleed advisory gate.
        // Fires when TranspirationBleedFraction > 0.15 (15% of total coolant
        // mass flow). Above this fraction the regen jacket is starved
        // non-linearly (Sutton §4.3). Advisory — the physics model still runs;
        // the gate warns rather than blocks so a designer can accept a
        // high-bleed configuration at reduced T_wg margin.
        // Silent when EnableTranspirationCooling = false (echoed on gen).
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "TRANSPIRATION_BLEED_EXCESSIVE",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "OOB-12 / issue #342",
            Emit:         EmitTranspirationBleedExcessive));

        // OOB-14 (issue #341): gate 57 — ablative throat recession budget.
        // Hard gate: fires when the ablative liner's predicted recession (×SF)
        // exceeds the initial liner thickness, meaning burnthrough before
        // end-of-burn. Predicate reads gen.Ablative (populated by
        // AblativeAnalysis.Run whenever ChannelTopology == AblativeThroat
        // or design.AblativeMaterial != None).
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "OOB-14 / issue #341",
            Emit:         EmitAblativeThroatRecessionExceedsBudget));

        // OOB-14 (issue #341): gate 58 — ablative-regen interface temperature.
        // Advisory: when the ablative liner coexists with regen channels, the
        // regen-side wall at the zone boundary must not exceed the ablative
        // material's char temperature (otherwise the ablative layer is
        // self-defeating — the regen wall temperature alone exceeds the char
        // threshold, activating pyrolysis outside the design intent).
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "ABLATIVE_REGEN_INTERFACE_OVERTEMP",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "OOB-14 / issue #341",
            Emit:         EmitAblativeRegenInterfaceOvertemp));

        // OOB-9 (issue #344): gate 59 — large finite-rate Isp penalty (Advisory).
        // Self-guards: silent when UseFiniteRateCorrection is false so legacy and
        // pressure-fed designs are never penalised for the disabled correction.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "FINITE_RATE_ISP_PENALTY_LARGE",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "OOB-9 / issue #344",
            Emit:         EmitFiniteRateIspPenaltyLarge));

        // OOB-7 (issue #343): gate 60 — RDE annulus fill time starved (Hard).
        // Fires when the inter-wave period is shorter than the annulus fill time,
        // meaning fresh propellant cannot refill the channel before the next
        // detonation wave arrives (misfire / detonation collapse). Hard gate —
        // a starved annulus is a non-starter. Self-guards: silent when
        // RdeTopology == None.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "RDE_ANNULUS_FILL_STARVED",
            Severity:     GateSeverity.Hard,
            Kind:         GateKind.PhysicsLimit,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "OOB-7 / issue #343",
            Emit:         EmitRdeAnnulusFillStarved));

        // OOB-7 (issue #343): gate 61 — RDE wave count below minimum (Advisory).
        // Fewer than 2 simultaneous detonation waves indicates the annulus
        // geometry is too small to sustain stable multi-wave propagation;
        // the RDE devolves toward single-wave or deflagration behaviour
        // (Wolański 2013 §4). Advisory — design may still fire, but efficiency
        // gain is uncertain. Self-guards: silent when RdeTopology == None.
        GateRegistry.Register(new FeasibilityGateDescriptor(
            Id:           "RDE_WAVE_COUNT_BELOW_MINIMUM",
            Severity:     GateSeverity.Advisory,
            Kind:         GateKind.AdvisoryHeuristic,
            Applicability:EngineFamilyMask.RocketRegen,
            AdrRef:       "OOB-7 / issue #343",
            Emit:         EmitRdeWaveCountBelowMinimum));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 1 — early cluster (gates 1-5)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitWallTemp(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        var material = WallMaterials.All[System.Math.Clamp(
            gen.Conditions.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        double peakWallT = gen.Thermal.PeakGasSideWallT_K;
        double matLimit  = material.MaxServiceTemp_K;
        if (peakWallT > matLimit)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "WALL_TEMP",
                Description:  $"Peak gas-side wall T {peakWallT:F0} K > {material.Name} service limit {matLimit:F0} K.",
                ActualValue:  peakWallT,
                Limit:        matLimit));
        }
    }

    private static void EmitYieldExceeded(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        double sf = gen.Stress.MinSafetyFactor;
        if (sf < 1.0)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "YIELD_EXCEEDED",
                Description:  $"Min safety factor {sf:F3} < 1.0 — wall yield stress exceeded.",
                ActualValue:  sf,
                Limit:        1.0));
        }
    }

    private static void EmitFeatureTooSmall(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        double minFeat = gen.Manufacturing.MinFeatureSize_mm;
        if (minFeat < FeasibilityGate.LpbfFeatureFloor_mm)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "FEATURE_TOO_SMALL",
                Description:  $"Min feature {minFeat:F3} mm < {FeasibilityGate.LpbfFeatureFloor_mm:F2} mm LPBF floor — cannot be printed.",
                ActualValue:  minFeat,
                Limit:        FeasibilityGate.LpbfFeatureFloor_mm));
        }
    }

    private static void EmitCoolantTExceeded(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        var pairMeta     = PropellantPairs.GetMeta(gen.Conditions.PropellantPair);
        var coolantFluid = CoolantRegistry.Get(pairMeta.CoolantFluidKey);
        double maxBulkT  = coolantFluid.Metadata.MaxBulkT_K;
        double coolantT  = gen.Thermal.CoolantOutletT_K;
        if (coolantT > maxBulkT)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "COOLANT_T_EXCEEDED",
                Description:  $"Coolant outlet T {coolantT:F0} K > {coolantFluid.Metadata.DisplayName} service limit {maxBulkT:F0} K ({coolantFluid.Metadata.ServiceLimitNote}).",
                ActualValue:  coolantT,
                Limit:        maxBulkT));
        }
    }

    private static void EmitStabilityFail(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Stability.Composite == StabilityRating.Fail)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "STABILITY_FAIL",
                Description:  $"Stability composite: FAIL — {gen.Stability.CompositeReason}.",
                ActualValue:  double.NaN,
                Limit:        double.NaN));
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 2 — injector cluster (gates 6-7)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitElementDensityTooHigh(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.InjectorPattern is not { } pat || gen.InjectorSizing is null) return;

        double rChamber_mm = gen.Contour.ChamberRadius_mm;
        double faceArea_cm2 = System.Math.PI * (rChamber_mm * rChamber_mm) / 100.0;   // mm² → cm²
        double density_per_cm2 = pat.ElementCount / System.Math.Max(faceArea_cm2, 1e-6);
        if (density_per_cm2 > FeasibilityGate.ElementDensityCeiling_per_cm2)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "ELEMENT_DENSITY_TOO_HIGH",
                Description:
                    $"Injector element density {density_per_cm2:F2} / cm² > " +
                    $"{FeasibilityGate.ElementDensityCeiling_per_cm2:F2} / cm² ceiling ({pat.ElementCount} elements " +
                    $"on a {faceArea_cm2:F2} cm² face) — face-plate burnout risk.",
                ActualValue:  density_per_cm2,
                Limit:        FeasibilityGate.ElementDensityCeiling_per_cm2));
        }
    }

    private static void EmitPintleBlockage(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.InjectorPattern is not { } pat || gen.InjectorSizing is null) return;
        if (pat.ElementType != "Pintle") return;

        double blockage = gen.InjectorSizing.PerElementResult.PintleBlockageFraction;
        if (blockage > 0
         && (blockage < FeasibilityGate.PintleBlockageFloor || blockage > FeasibilityGate.PintleBlockageCeiling))
        {
            double limit = blockage < FeasibilityGate.PintleBlockageFloor
                ? FeasibilityGate.PintleBlockageFloor : FeasibilityGate.PintleBlockageCeiling;
            string direction = blockage < FeasibilityGate.PintleBlockageFloor ? "below" : "above";
            v.Add(new FeasibilityViolation(
                ConstraintId: "PINTLE_BLOCKAGE_OUT_OF_BAND",
                Description:
                    $"Pintle blockage factor {blockage:F2} is {direction} the "
                  + $"Dressler stable-combustion band [{FeasibilityGate.PintleBlockageFloor:F2}, "
                  + $"{FeasibilityGate.PintleBlockageCeiling:F2}] — "
                  + (blockage < FeasibilityGate.PintleBlockageFloor
                      ? "sheet breakup is starved; reduce PintleDiameter_mm or increase PintleSleeveHoleCount."
                      : "jets collide too aggressively; increase PintleDiameter_mm or reduce PintleSleeveHoleCount."),
                ActualValue: blockage,
                Limit:       limit));
        }
    }

    private static void EmitPintleTmr(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.InjectorPattern is not { } pat || gen.InjectorSizing is null) return;
        if (pat.ElementType != "Pintle") return;

        double tmr = gen.InjectorSizing.PerElementResult.MomentumRatio;
        if (tmr > 0
         && (tmr < FeasibilityGate.PintleTmrFloor || tmr > FeasibilityGate.PintleTmrCeiling))
        {
            double limit = tmr < FeasibilityGate.PintleTmrFloor
                ? FeasibilityGate.PintleTmrFloor : FeasibilityGate.PintleTmrCeiling;
            string direction = tmr < FeasibilityGate.PintleTmrFloor ? "below" : "above";
            v.Add(new FeasibilityViolation(
                ConstraintId: "PINTLE_TMR_OUT_OF_BAND",
                Description:
                    $"Pintle total momentum ratio {tmr:F2} is {direction} the Dressler "
                  + $"mixing-quality band [{FeasibilityGate.PintleTmrFloor:F2}, {FeasibilityGate.PintleTmrCeiling:F2}]. "
                  + "Tune the main-chamber mixture ratio or injector ΔP to bring TMR closer "
                  + "to 1.0 (the log-symmetric centre of the band).",
                ActualValue: tmr,
                Limit:       limit));
        }
    }

    private static void EmitInjectorFaceT(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.InjectorFace is not { } face) return;

        double injectorFaceServiceTemp_K = face.MaxServiceTemp_K > 0
            ? face.MaxServiceTemp_K
            : InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K;
        if (face.TFace_K > injectorFaceServiceTemp_K)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "INJECTOR_FACE_T_EXCEEDED",
                Description:
                    $"Predicted injector face T {face.TFace_K:F0} K > injector face " +
                    $"service limit {injectorFaceServiceTemp_K:F0} K. " +
                    $"Increase bore-cooling (lower element density, larger bores), add a " +
                    $"face-film-cooling pattern, or pick a higher-T face alloy.",
                ActualValue:  face.TFace_K,
                Limit:        injectorFaceServiceTemp_K));
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 3 — igniter cluster + feed-system + tap-off + purge (gates 8-10)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitIgniterMissing(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        var ignitionReq = IgnitionRequirements.For(gen.Conditions.PropellantPair);
        if (ignitionReq.IsHypergolic) return;
        if (gen.IgniterType != Geometry.IgniterType.None) return;

        var pairMetaIg = PropellantPairs.GetMeta(gen.Conditions.PropellantPair);
        v.Add(new FeasibilityViolation(
            ConstraintId: "IGNITER_MISSING",
            Description:
                $"Propellant pair {pairMetaIg.Name} is NOT hypergolic — "
              + $"an external igniter is required for first-fire. "
              + $"IgniterType.None produces no ignition energy. "
              + $"Remediation: pick at least {ignitionReq.MinModality} "
              + $"(rated ≥ {ignitionReq.MinEnergy_J:G3} J). {ignitionReq.Notes}",
            ActualValue:  0.0,
            Limit:        ignitionReq.MinEnergy_J));
    }

    private static void EmitIgniterEnergyInsufficient(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        var ignitionReq = IgnitionRequirements.For(gen.Conditions.PropellantPair);
        if (ignitionReq.IsHypergolic) return;
        if (gen.IgniterType == Geometry.IgniterType.None) return;

        var iSpec = Geometry.IgniterPresets.SpecFor(gen.IgniterType);
        if (iSpec.IgnitionEnergy_J >= ignitionReq.MinEnergy_J) return;

        var pairMetaIg = PropellantPairs.GetMeta(gen.Conditions.PropellantPair);
        v.Add(new FeasibilityViolation(
            ConstraintId: "IGNITER_ENERGY_INSUFFICIENT",
            Description:
                $"Igniter '{iSpec.DisplayName}' rated at "
              + $"{iSpec.IgnitionEnergy_J:G3} J < "
              + $"{ignitionReq.MinEnergy_J:G3} J floor for "
              + $"{pairMetaIg.Name}. {ignitionReq.Notes}",
            ActualValue:  iSpec.IgnitionEnergy_J,
            Limit:        ignitionReq.MinEnergy_J));
    }

    private static void EmitIgniterModalityUnsuitable(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        var ignitionReq = IgnitionRequirements.For(gen.Conditions.PropellantPair);
        if (ignitionReq.IsHypergolic) return;
        if (gen.IgniterType == Geometry.IgniterType.None) return;

        int selectedOrdinal = IgnitionRequirements.ModalityOrdinal(gen.IgniterType);
        int minOrdinal      = IgnitionRequirements.ModalityOrdinal(ignitionReq.MinModality);
        if (selectedOrdinal >= minOrdinal) return;

        var iSpec = Geometry.IgniterPresets.SpecFor(gen.IgniterType);
        var pairMetaIg = PropellantPairs.GetMeta(gen.Conditions.PropellantPair);
        v.Add(new FeasibilityViolation(
            ConstraintId: "IGNITER_MODALITY_UNSUITABLE",
            Description:
                $"Igniter '{iSpec.DisplayName}' modality "
              + $"({gen.IgniterType}) is below the recommended "
              + $"minimum ({ignitionReq.MinModality}) for "
              + $"{pairMetaIg.Name}. {ignitionReq.Notes}",
            ActualValue:  selectedOrdinal,
            Limit:        minOrdinal));
    }

    private static void EmitFeedPressureInsufficient(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.FeedStackup is not { } stackup) return;
        if (stackup.IsFeasible) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "FEED_PRESSURE_INSUFFICIENT",
            Description:
                $"Feed-system stackup yields Pc {stackup.PredictedChamberPressure_Pa / 1e6:F2} MPa < " +
                $"target {stackup.TargetChamberPressure_Pa / 1e6:F2} MPa " +
                $"(margin {stackup.MarginFraction * 100:+0.0;-0.0;0.0} %). " +
                $"Raise tank ullage or cut line / valve / filter / umbilical / injector ΔP.",
            ActualValue:  stackup.PredictedChamberPressure_Pa,
            Limit:        stackup.TargetChamberPressure_Pa));
    }

    private static void EmitBlowDownInsufficient(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.FeedStackup is not { } stk) return;
        if (stk.EndOfBurnTankPressure_Pa <= 0.0) return;
        if (stk.EndOfBurnIsFeasible) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "BLOW_DOWN_INSUFFICIENT",
            Description:
                $"Blow-down end-of-burn yields Pc "
              + $"{stk.EndOfBurnPredictedChamberPressure_Pa / 1e6:F2} MPa < "
              + $"target {stk.TargetChamberPressure_Pa / 1e6:F2} MPa at "
              + $"final tank pressure {stk.EndOfBurnTankPressure_Pa / 1e6:F2} MPa "
              + $"(margin {stk.EndOfBurnMarginFraction * 100:+0.0;-0.0;0.0} %). "
              + "Start-of-burn stackup is feasible but the engine cannot "
              + "sustain chamber pressure as the tank decays. Raise the "
              + "final tank pressure (smaller initial ullage volume) or "
              + "switch to a regulated-pressure feed.",
            ActualValue:  stk.EndOfBurnPredictedChamberPressure_Pa,
            Limit:        stk.TargetChamberPressure_Pa));
    }

    private static void EmitTapOffHotGas(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.TapOffTurbine is not { TapPointTemperatureOK: false } tap) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "TAPOFF_HOT_GAS_TOO_HOT",
            Description:
                $"Tap-off tap-point T {tap.TapPointTemperature_K:F0} K exceeds "
              + $"uncooled-wheel limit {tap.TurbineInletLimit_K:F0} K "
              + $"(chamber T_c {tap.ChamberTemperature_K:F0} K; boundary-layer "
              + $"fraction {FeedSystem.TapOffCycleSizing.BoundaryLayerFraction:P0}). "
              + "Lower chamber Pc, boost film-cooling fraction, or switch to a "
              + "preburner / expander cycle.",
            ActualValue:  tap.TapPointTemperature_K,
            Limit:        tap.TurbineInletLimit_K));
    }

    private static void EmitPurgeFlow(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.PurgeResults is not { Length: > 0 } purge) return;

        foreach (var pr in purge)
        {
            if (!pr.MeetsRequestedFlow && pr.Port.MassFlow_kgs > 0)
            {
                v.Add(new FeasibilityViolation(
                    ConstraintId: "PURGE_FLOW_INSUFFICIENT",
                    Description:
                        $"Purge port {pr.Port.Location} ({pr.Port.Fluid}) delivers "
                      + $"{pr.ActualMassFlow_kgs:E2} kg/s < 95 % of requested "
                      + $"{pr.Port.MassFlow_kgs:E2} kg/s at inlet "
                      + $"{pr.Port.InletPressure_Pa / 1e6:F1} MPa. Raise inlet P or bore Ø.",
                    ActualValue:  pr.ActualMassFlow_kgs,
                    Limit:        pr.Port.MassFlow_kgs * 0.95));
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 4 — chilldown / ablative / start / NPSH / pump-inverted /
    //            burst-margin (gates 12-15)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitChilldownBudgetExceeded(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Chilldown is not { } chill || chill.IsAcceptable) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "CHILLDOWN_BUDGET_EXCEEDED",
            Description:
                $"Chilldown integrator predicts {chill.TimeToChill_s:F1} s "
              + $"(τ = {chill.TimeConstant_s:F1} s, regime: {chill.Regime}), "
              + $"with {chill.PropellantMassConsumed_kg:F2} kg of propellant "
              + $"boiled off before steady regen flow begins. Reduce jacket mass, "
              + $"raise two-phase HTC, or extend the budget.",
            ActualValue:  chill.TimeToChill_s,
            Limit:        double.NaN));
    }

    private static void EmitAblativeBurnthrough(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Ablative is not { } abl || abl.IsAcceptable) return;

        double penetrated = (abl.MaxRecession_mm + abl.MaxCharDepth_mm) * abl.SafetyFactor;
        v.Add(new FeasibilityViolation(
            ConstraintId: "ABLATIVE_BURNTHROUGH",
            Description:
                $"Ablative liner ({abl.Material}) penetrated "
              + $"{penetrated:F2} mm (recession {abl.MaxRecession_mm:F2} + char {abl.MaxCharDepth_mm:F2}, "
              + $"× SF {abl.SafetyFactor:F2}) exceeds initial thickness "
              + $"{abl.InitialThickness_mm:F2} mm at burn {abl.BurnDuration_s:F1} s. "
              + $"Increase liner thickness, switch to a higher-grade material, or reduce burn duration.",
            ActualValue:  penetrated,
            Limit:        abl.InitialThickness_mm));
    }

    private static void EmitHardStartRisk(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.StartTransient is not { } start || !start.HardStartRisk) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "HARD_START_RISK",
            Description:
                $"Start-transient sim predicts {start.PeakPressureOvershoot * 100:F0} % Pc "
              + $"overshoot at t = {start.IgnitionTime_s * 1000:F1} ms ignition; "
              + $"{start.UnburnedMassAtIgnition_kg * 1000:F1} g of propellant pooled in chamber "
              + $"before light. Tighten igniter delay, stage the valves, or stiffen the dome.",
            ActualValue:  start.PeakPressureOvershoot,
            Limit:        gen.Conditions.StartHardStartFactor));
    }

    private static void EmitNpshInsufficient(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump is not { } pump || pump.NPSHFeasible) return;

        string lines = "";
        double worstNPSHA = double.PositiveInfinity;
        double worstNPSHR = 0;
        double worstMargin = double.PositiveInfinity;
        if (pump.FuelPump is { } f && !f.NPSHAcceptable)
        {
            lines += $"Fuel: NPSHA {f.NPSHA_m:F2} m < NPSHR {f.NPSHR_m:F2} m. ";
            double margin = f.NPSHA_m - f.NPSHR_m;
            if (margin < worstMargin)
            { worstMargin = margin; worstNPSHA = f.NPSHA_m; worstNPSHR = f.NPSHR_m; }
        }
        if (pump.OxPump is { } o && !o.NPSHAcceptable)
        {
            lines += $"Ox: NPSHA {o.NPSHA_m:F2} m < NPSHR {o.NPSHR_m:F2} m.";
            double margin = o.NPSHA_m - o.NPSHR_m;
            if (margin < worstMargin)
            { worstMargin = margin; worstNPSHA = o.NPSHA_m; worstNPSHR = o.NPSHR_m; }
        }
        v.Add(new FeasibilityViolation(
            ConstraintId: "NPSH_INSUFFICIENT",
            Description:
                $"{pump.Cycle} cycle: pump cavitation risk — {lines}"
              + " Raise tank ullage, lower suction-line velocity, or add an inducer.",
            ActualValue:  worstNPSHA,
            Limit:        worstNPSHR));
    }

    private static void EmitPumpPressureInverted(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump is not { } pumpInv) return;

        bool fuelInverted = pumpInv.FuelPump is { } fp && fp.MassFlow_kgs > 0
                            && fp.DischargePressure_Pa <= fp.InletPressure_Pa;
        bool oxInverted   = pumpInv.OxPump is { } op && op.MassFlow_kgs > 0
                            && op.DischargePressure_Pa <= op.InletPressure_Pa;
        if (!fuelInverted && !oxInverted) return;

        string which = (fuelInverted, oxInverted) switch
        {
            (true, true)   => "fuel and ox pumps",
            (true, false)  => "fuel pump",
            (false, true)  => "ox pump",
            _              => "(none)",
        };
        double worstDeltaP = double.PositiveInfinity;
        if (fuelInverted)
            worstDeltaP = System.Math.Min(worstDeltaP,
                pumpInv.FuelPump!.DischargePressure_Pa - pumpInv.FuelPump!.InletPressure_Pa);
        if (oxInverted)
            worstDeltaP = System.Math.Min(worstDeltaP,
                pumpInv.OxPump!.DischargePressure_Pa - pumpInv.OxPump!.InletPressure_Pa);
        v.Add(new FeasibilityViolation(
            ConstraintId: "PUMP_PRESSURE_INVERTED",
            Description:
                $"{pumpInv.Cycle} cycle: {which} discharge ≤ inlet "
              + $"(worst dP = {worstDeltaP / 1e6:F2} MPa). Feed line "
              + $"wired backwards or PumpDischargePressure_Pa override "
              + $"too low. Set discharge > inlet (Sutton §6.5).",
            ActualValue: worstDeltaP,
            Limit: 0.0));
    }

    private static void EmitBurstMarginInsufficient(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.BurstMarginFactor <= 0) return;
        if (gen.BurstMarginFactor >= Structure.ProofTestAnalysis.MinBurstMarginFactor) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "BURST_MARGIN_INSUFFICIENT",
            Description:
                $"Elastic burst margin {gen.BurstMarginFactor:F2}× MEOP < "
              + $"{Structure.ProofTestAnalysis.MinBurstMarginFactor:F1}× ASME BPVC §VIII Div 1 "
              + "ground-test threshold (PR #104). Increase wall thickness "
              + "or pick a higher-yield material; without margin the design "
              + "would fail standard proof-test review.",
            ActualValue: gen.BurstMarginFactor,
            Limit:       Structure.ProofTestAnalysis.MinBurstMarginFactor));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 5 — turbine + shaft + preburner cluster (gates 16-18 + ORSC)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitTurbinePowerDeficit(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump is not { Turbine: { } turbine } || turbine.PowerBalanceOK) return;

        string which = "";
        double actual = 0, limit = 0;
        if (turbine.FuelTurbine is { PowerSufficient: false } ft)
        {
            which += $"Fuel: {ft.AvailableShaftPower_W / 1e3:F1} kW avail < "
                  +  $"{ft.RequiredShaftPower_W / 1e3:F1} kW req. ";
            actual = ft.AvailableShaftPower_W;
            limit  = ft.RequiredShaftPower_W;
        }
        if (turbine.OxTurbine is { PowerSufficient: false } ot)
        {
            which += $"Ox: {ot.AvailableShaftPower_W / 1e3:F1} kW avail < "
                  +  $"{ot.RequiredShaftPower_W / 1e3:F1} kW req.";
            // If both short, report the worst gap on the `actual` slot.
            if (ot.RequiredShaftPower_W - ot.AvailableShaftPower_W
                  > limit - actual)
            {
                actual = ot.AvailableShaftPower_W;
                limit  = ot.RequiredShaftPower_W;
            }
        }
        v.Add(new FeasibilityViolation(
            ConstraintId: "TURBINE_POWER_DEFICIT",
            Description:
                $"Turbine shaft-power deficit on {gen.Turbopump.Cycle} cycle. "
              + which
              + " Raise preburner Pc, raise preburner mass flow, lower pump head, "
              + "or switch cycle.",
            ActualValue:  actual,
            Limit:        limit));
    }

    private static void EmitExpanderTurbineEnthalpyDeficit(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.ExpanderTurbine is not { PowerSufficient: false } expander) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "EXPANDER_TURBINE_ENTHALPY_DEFICIT",
            Description:
                $"Expander-cycle coolant enthalpy insufficient on {gen.ExpanderTurbine.Cycle}: "
              + $"{expander.AvailableShaftPower_W / 1e3:F1} kW available vs "
              + $"{expander.RequiredShaftPower_W / 1e3:F1} kW required "
              + $"({expander.ActualSpecificWork_Jkg / 1e3:F0} kJ/kg actual specific work "
              + $"at η {expander.Efficiency:P0}). "
              + "Raise jacket ΔT (smaller channel, more flow, longer chamber), "
              + "raise jacket outlet P, or switch to a preburner cycle.",
            ActualValue:  expander.AvailableShaftPower_W,
            Limit:        expander.RequiredShaftPower_W));
    }

    private static void EmitShaftWhirl(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump is not { } tp) return;
        if (!(tp.FuelShaft is { WhirlOk: false } || tp.OxShaft is { WhirlOk: false })) return;

        var failed = new List<string>(2);
        double worstAbsMargin = double.PositiveInfinity;
        double worstActualRpm = 0, worstLimitRpm = 0;

        if (tp.FuelShaft is { WhirlOk: false } fs)
        {
            failed.Add(FeedSystem.ShaftCriticalSpeed.FormatWarning(fs)!);
            double absMargin = System.Math.Abs(fs.WhirlSafetyMargin);
            if (absMargin < worstAbsMargin)
            {
                worstAbsMargin = absMargin;
                worstActualRpm = fs.OperatingRpm;
                worstLimitRpm  = fs.FirstCriticalRpm;
            }
        }
        if (tp.OxShaft is { WhirlOk: false } os)
        {
            failed.Add(FeedSystem.ShaftCriticalSpeed.FormatWarning(os)!);
            double absMargin = System.Math.Abs(os.WhirlSafetyMargin);
            if (absMargin < worstAbsMargin)
            {
                worstAbsMargin = absMargin;
                worstActualRpm = os.OperatingRpm;
                worstLimitRpm  = os.FirstCriticalRpm;
            }
        }

        v.Add(new FeasibilityViolation(
            ConstraintId: "SHAFT_WHIRL",
            Description:  string.Join("  ", failed),
            ActualValue:  worstActualRpm,
            Limit:        worstLimitRpm));
    }

    private static void EmitPreburnerWallTemp(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        // Fuel-rich preburner side
        if (gen.Preburner is { Thermal: { } preThermal })
        {
            var preMat = WallMaterials.All[System.Math.Clamp(
                gen.Conditions.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
            if (preThermal.PeakWallT_K > preMat.MaxServiceTemp_K)
            {
                v.Add(new FeasibilityViolation(
                    ConstraintId: "PREBURNER_WALL_TEMP",
                    Description:
                        $"Preburner peak wall T {preThermal.PeakWallT_K:F0} K exceeds "
                      + $"{preMat.Name} service limit {preMat.MaxServiceTemp_K:F0} K "
                      + $"(h_g = {preThermal.HGasSide_Wm2K / 1e3:F1} kW·m⁻²·K⁻¹, "
                      + $"coolant ΔT = {preThermal.CoolantOutletT_K - gen.Conditions.CoolantInletTemp_K:F0} K). "
                      + $"Increase preburner channel depth or count, raise coolant flow, "
                      + $"switch to a higher-temperature wall material, or reduce preburner Pc / MR.",
                    ActualValue:  preThermal.PeakWallT_K,
                    Limit:        preMat.MaxServiceTemp_K));
            }
        }
        // Ox-rich preburner side (FFSC + Sprint 24 ORSC)
        if (gen.OxidizerPreburner is { Thermal: { } oxPreThermal })
        {
            var oxPreMat = WallMaterials.All[System.Math.Clamp(
                gen.Conditions.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
            if (oxPreThermal.PeakWallT_K > oxPreMat.MaxServiceTemp_K)
            {
                v.Add(new FeasibilityViolation(
                    ConstraintId: "PREBURNER_WALL_TEMP",
                    Description:
                        $"Ox-rich preburner peak wall T {oxPreThermal.PeakWallT_K:F0} K exceeds "
                      + $"{oxPreMat.Name} service limit {oxPreMat.MaxServiceTemp_K:F0} K "
                      + $"({gen.Conditions.EngineCycle} ox-rich side). "
                      + $"Increase preburner channel depth or count, raise coolant flow, "
                      + $"switch to a higher-temperature wall material, or reduce preburner Pc / MR.",
                    ActualValue:  oxPreThermal.PeakWallT_K,
                    Limit:        oxPreMat.MaxServiceTemp_K));
            }
        }
    }

    private static void EmitOrscPreburnerOxCorrosion(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.OxidizerPreburner is not { Thermal: { } oxPreThermal }) return;
        if (gen.Conditions.EngineCycle != FeedSystem.EngineCycle.ORSC) return;

        var oxPreMat = WallMaterials.All[System.Math.Clamp(
            gen.Conditions.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        const double OxRichCorrosionMarginK = 50.0;
        if (oxPreThermal.PeakWallT_K <= oxPreMat.MaxServiceTemp_K - OxRichCorrosionMarginK) return;

        double corrosionLimit = oxPreMat.MaxServiceTemp_K - OxRichCorrosionMarginK;
        v.Add(new FeasibilityViolation(
            ConstraintId: "ORSC_PREBURNER_OXCORROSION",
            Description:
                $"ORSC ox-rich preburner peak wall T {oxPreThermal.PeakWallT_K:F0} K "
              + $"exceeds the {OxRichCorrosionMarginK:F0} K corrosion-margin floor "
              + $"{corrosionLimit:F0} K ({oxPreMat.Name} service limit "
              + $"{oxPreMat.MaxServiceTemp_K:F0} K − {OxRichCorrosionMarginK:F0} K). "
              + $"Ox-rich combustion accelerates metal-oxidation — "
              + $"increase preburner cooling, lower preburner Pc / MR, "
              + $"or switch to a corrosion-resistant alloy (Cu-coated "
              + $"Inconel 718 / NiCrAl heritage).",
            ActualValue:  oxPreThermal.PeakWallT_K,
            Limit:        corrosionLimit));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 6 — TPMS strut + LPBF printability + sensor-boss clash
    //            (gates 15 + 25-28)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitTpmsCellTooSmall(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.ChannelTopology is not (ChannelTopology.TpmsGyroid
                                     or ChannelTopology.TpmsSchwarzP
                                     or ChannelTopology.TpmsSchwarzD)) return;

        double strut_mm = TpmsCorrelations.StrutThickness_mm(
            cellEdge_mm:   gen.TpmsCellEdge_mm,
            solidFraction: gen.TpmsSolidFraction);
        if (strut_mm >= TpmsCorrelations.MinStrutThickness_mm) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "TPMS_CELL_FEATURE_TOO_SMALL",
            Description:
                $"TPMS strut thickness {strut_mm:F2} mm "
              + $"(= {gen.TpmsSolidFraction:F2} × {gen.TpmsCellEdge_mm:F2} mm cell) "
              + $"< {TpmsCorrelations.MinStrutThickness_mm:F1} mm LPBF floor for "
              + $"{gen.ChannelTopology} lattice. "
              + $"Raise TpmsCellEdge_mm or TpmsSolidFraction.",
            ActualValue:  strut_mm,
            Limit:        TpmsCorrelations.MinStrutThickness_mm));
    }

    private static void EmitOverhangAngleExceeded(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Printability is not { } printability) return;
        if (!printability.HasOverhangViolation) return;

        var ov = printability.Overhang;
        v.Add(new FeasibilityViolation(
            ConstraintId: "OVERHANG_ANGLE_EXCEEDED",
            Description:
                $"{ov.ViolationCount} surface patch(es) overhang below "
              + $"{printability.Material.DisplayName}'s "
              + $"{printability.Material.MinUnsupportedOverhangAngle_deg:F0}° "
              + $"unsupported floor — worst patch at "
              + $"{ov.WorstOverhangAngle_deg:F0}°, total overhang area "
              + $"{ov.TotalOverhangArea_mm2:F0} mm². "
              + (printability.Orientation is { } orient
                  ? $"Advisor recommends building along {orient.RecommendedAxis}: {orient.Rationale}"
                  : "Add sacrificial supports, re-orient the build, or soften the steepest slopes."),
            ActualValue:  ov.WorstOverhangAngle_deg,
            Limit:        printability.Material.MinUnsupportedOverhangAngle_deg));
    }

    private static void EmitTrappedPowderRegion(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Printability is not { } printability) return;
        if (!printability.HasTrappedPowder) return;
        if (printability.TrappedPowder is not { } powderReport) return;

        int k = 0;
        foreach (var pocket in powderReport.Pockets)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "TRAPPED_POWDER_REGION",
                Description:
                    $"Trapped-powder pocket #{++k}: {pocket.Volume_mm3:F1} mm³ "
                  + $"({pocket.VoxelCount} voxels) centred at "
                  + $"({pocket.CentroidWorld_mm.X:F1}, "
                  + $"{pocket.CentroidWorld_mm.Y:F1}, "
                  + $"{pocket.CentroidWorld_mm.Z:F1}) mm — cannot evacuate to "
                  + "any external surface or configured opening.",
                ActualValue:  pocket.Volume_mm3,
                Limit:        0.0));
        }
    }

    private static void EmitDrainPathMissing(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Printability is not { } printability) return;
        if (!printability.HasDrainPathViolation) return;

        foreach (var dp in printability.DrainPath.Violations)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "DRAIN_PATH_MISSING",
                Description:
                    $"Drain-path violation ({dp.Reason}): node '{dp.Label}' "
                  + $"[{dp.NodeId}] has no path for powder to evacuate. "
                  + "Wire the branch to an external port, remove it, or add a drain tap.",
                ActualValue:  double.NaN,
                Limit:        double.NaN));
        }
    }

    private static void EmitInstrumentationTapInterference(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.SensorBosses is not { Count: > 0 } bosses) return;

        var clashes = Geometry.SensorBossClashEvaluator.Evaluate(
            bosses:        bosses,
            channelCount:  gen.ChannelCount,
            topology:      gen.ChannelTopology,
            contour:       gen.Contour);
        foreach (var c in clashes)
        {
            v.Add(new FeasibilityViolation(
                ConstraintId: "INSTRUMENTATION_TAP_INTERFERENCE",
                Description:  c.Description,
                ActualValue:  c.ArcDistance_mm,
                Limit:        c.MinClearance_mm));
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 7 — contour + injector mass flux + L* + pump N_s
    //            (gates 29-32 + 35)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitContractionRatioOutOfBand(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        double epsCActual = gen.Contour.ContractionRatio;
        if (epsCActual >= FeasibilityGate.ContractionRatioFloor
         && epsCActual <= FeasibilityGate.ContractionRatioCeiling) return;

        string side = epsCActual < FeasibilityGate.ContractionRatioFloor ? "below" : "above";
        double bound = epsCActual < FeasibilityGate.ContractionRatioFloor
            ? FeasibilityGate.ContractionRatioFloor : FeasibilityGate.ContractionRatioCeiling;
        v.Add(new FeasibilityViolation(
            ConstraintId: "CONTRACTION_RATIO_OUT_OF_BAND",
            Description:
                $"Contraction ratio ε_c = {epsCActual:F2} {side} practical band "
              + $"[{FeasibilityGate.ContractionRatioFloor:F1}, {FeasibilityGate.ContractionRatioCeiling:F1}] (Sutton §8.2 / "
              + "Huzel & Huang §4.1). Low ε_c risks combustion instability via chamber Mach "
              + "> 0.2; high ε_c bloats wall area and cooling surface.",
            ActualValue:  epsCActual,
            Limit:        bound));
    }

    private static void EmitChannelAspectRatioExceeded(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.ChannelTopology == ChannelTopology.None
         || gen.ChannelTopology == ChannelTopology.TpmsGyroid
         || gen.ChannelTopology == ChannelTopology.TpmsSchwarzP
         || gen.ChannelTopology == ChannelTopology.TpmsSchwarzD) return;

        double worstAr = 0;
        int worstIdx = -1;
        for (int i = 0; i < gen.Thermal.Stations.Length; i++)
        {
            var st = gen.Thermal.Stations[i];
            double w = System.Math.Max(st.ChannelWidth_mm, 1e-6);
            double ar = st.ChannelHeight_mm / w;
            if (ar > worstAr) { worstAr = ar; worstIdx = i; }
        }
        if (worstAr > FeasibilityGate.ChannelAspectRatioStrict && worstIdx >= 0)
        {
            var st = gen.Thermal.Stations[worstIdx];
            v.Add(new FeasibilityViolation(
                ConstraintId: "CHANNEL_ASPECT_RATIO_EXCEEDED",
                Description:
                    $"Channel aspect ratio {worstAr:F1} (h={st.ChannelHeight_mm:F2} mm / "
                  + $"w={st.ChannelWidth_mm:F2} mm at x={st.X_mm:F0} mm) > "
                  + $"{FeasibilityGate.ChannelAspectRatioStrict:F0} LPBF strict ceiling — rib slenderness "
                  + "risks buckling during print. Lower channel height or raise channel count.",
                ActualValue:  worstAr,
                Limit:        FeasibilityGate.ChannelAspectRatioStrict));
        }
        else if (worstAr > FeasibilityGate.ChannelAspectRatioWarn && worstIdx >= 0)
        {
            var st = gen.Thermal.Stations[worstIdx];
            v.Add(new FeasibilityViolation(
                ConstraintId: "CHANNEL_ASPECT_RATIO_EXCEEDED",
                Description:
                    $"Channel aspect ratio {worstAr:F1} at x={st.X_mm:F0} mm > "
                  + $"{FeasibilityGate.ChannelAspectRatioWarn:F0} LPBF warn threshold — verify with vendor "
                  + "process map before print.",
                ActualValue:  worstAr,
                Limit:        FeasibilityGate.ChannelAspectRatioWarn));
        }
    }

    /// <summary>
    /// Helper for G_INJ_TOO_LOW + G_INJ_TOO_HIGH: returns the computed mass
    /// flux or NaN when the gate doesn't apply (no sized injector / no flow).
    /// </summary>
    private static double TryComputeGInj(RegenGenerationResult gen)
    {
        if (gen.InjectorSizing is not { } sizing) return double.NaN;
        if ((sizing.TotalOxArea_mm2 + sizing.TotalFuelArea_mm2) <= 0) return double.NaN;

        double mDotTotal = gen.Derived.OxidizerMassFlow_kgs + gen.Derived.FuelMassFlow_kgs;
        double totalInjArea_m2 = (sizing.TotalOxArea_mm2 + sizing.TotalFuelArea_mm2) * 1e-6;
        if (totalInjArea_m2 <= 0 || mDotTotal <= 0) return double.NaN;

        return mDotTotal / totalInjArea_m2;
    }

    private static void EmitGInjTooLow(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        double gInj = TryComputeGInj(gen);
        if (double.IsNaN(gInj)) return;
        if (gInj >= FeasibilityGate.InjectorMassFluxFloor_kgPerm2s) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "G_INJ_TOO_LOW",
            Description:
                $"Injector mass flux G_inj = {gInj:F0} kg/(m²·s) below "
              + $"{FeasibilityGate.InjectorMassFluxFloor_kgPerm2s:F0} kg/(m²·s) chug-instability floor "
              + "(Sutton §6.3). Reduce orifice count or shrink orifice diameters.",
            ActualValue:  gInj,
            Limit:        FeasibilityGate.InjectorMassFluxFloor_kgPerm2s));
    }

    private static void EmitGInjTooHigh(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        double gInj = TryComputeGInj(gen);
        if (double.IsNaN(gInj)) return;
        if (gInj <= FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "G_INJ_TOO_HIGH",
            Description:
                $"Injector mass flux G_inj = {gInj:F0} kg/(m²·s) above "
              + $"{FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s:F0} kg/(m²·s) over-mix / face-burnout "
              + "ceiling (Sutton §6.3). Add orifices or grow orifice diameters.",
            ActualValue:  gInj,
            Limit:        FeasibilityGate.InjectorMassFluxCeiling_kgPerm2s));
    }

    private static void EmitLStarBelowPropellantMin(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        double lStarNominal = AutoSeeder.CharacteristicLengthFor(gen.Conditions.PropellantPair);
        double lStarFloor   = lStarNominal * FeasibilityGate.LStarFloorFraction;
        double lStarActual = gen.Contour.CharacteristicLength_m;
        if (lStarActual <= 0 || lStarActual >= lStarFloor) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "L_STAR_BELOW_PROPELLANT_MIN",
            Description:
                $"L\\* = {lStarActual:F2} m below {FeasibilityGate.LStarFloorFraction * 100:F0} % of "
              + $"{gen.Conditions.PropellantPair} nominal {lStarNominal:F2} m. Real engines "
              + "below this floor lose 2-5 % on C\\* — the η_C\\* default does not capture "
              + "the penalty. Raise CharacteristicLength_m or accept a less-aggressive "
              + "chamber-volume target.",
            ActualValue:  lStarActual,
            Limit:        lStarFloor));
    }

    private static void EmitPumpSpecificSpeedOffBand(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump?.FuelPump is { } fuelP
            && fuelP.SpecificSpeed_US > 0
            && (fuelP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor
             || fuelP.SpecificSpeed_US > FeasibilityGate.PumpSpecificSpeedCeiling))
        {
            string side = fuelP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor ? "below" : "above";
            double bound = fuelP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor
                ? FeasibilityGate.PumpSpecificSpeedFloor : FeasibilityGate.PumpSpecificSpeedCeiling;
            v.Add(new FeasibilityViolation(
                ConstraintId: "PUMP_SPECIFIC_SPEED_OFF_BAND",
                Description:
                    $"Fuel pump N_s = {fuelP.SpecificSpeed_US:F0} (US) {side} practical band "
                  + $"[{FeasibilityGate.PumpSpecificSpeedFloor:F0}, {FeasibilityGate.PumpSpecificSpeedCeiling:F0}] "
                  + $"(Karassik §2.5 / Stepanoff §2.7). "
                  + (fuelP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor
                       ? "Low N_s → axial-flow regime, centrifugal-pump similarity does not hold. "
                       : "High N_s → multi-stage / mixed-flow territory beyond the single-stage model. ")
                  + $"Adjust PumpRpm_rpm or stage count.",
                ActualValue:  fuelP.SpecificSpeed_US,
                Limit:        bound));
        }
        if (gen.Turbopump?.OxPump is { } oxP
            && oxP.SpecificSpeed_US > 0
            && (oxP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor
             || oxP.SpecificSpeed_US > FeasibilityGate.PumpSpecificSpeedCeiling))
        {
            string side = oxP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor ? "below" : "above";
            double bound = oxP.SpecificSpeed_US < FeasibilityGate.PumpSpecificSpeedFloor
                ? FeasibilityGate.PumpSpecificSpeedFloor : FeasibilityGate.PumpSpecificSpeedCeiling;
            v.Add(new FeasibilityViolation(
                ConstraintId: "PUMP_SPECIFIC_SPEED_OFF_BAND",
                Description:
                    $"Ox pump N_s = {oxP.SpecificSpeed_US:F0} (US) {side} practical band "
                  + $"[{FeasibilityGate.PumpSpecificSpeedFloor:F0}, {FeasibilityGate.PumpSpecificSpeedCeiling:F0}].",
                ActualValue:  oxP.SpecificSpeed_US,
                Limit:        bound));
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Batch 8 (FINAL) — turbine choke + thermal bridge + common-shaft
    //                    + TPMS overlap + bimetallic
    //                    (gates 33-34 + 36-38)
    // ─────────────────────────────────────────────────────────────────

    private static void EmitTurbineUnchoked(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump?.Turbine is { } turbines)
        {
            if (turbines.FuelTurbine is { IsChoked: false } fuelT)
            {
                double pr = fuelT.OutletPressure_Pa
                          / System.Math.Max(fuelT.InletPressure_Pa, 1.0);
                v.Add(new FeasibilityViolation(
                    ConstraintId: "TURBINE_UNCHOKED",
                    Description:
                        $"Fuel turbine subsonic: π = p_out/p_in = {pr:F3} > "
                      + $"π_crit = {fuelT.CriticalPressureRatio:F3} for γ={fuelT.Gamma:F2}. "
                      + "Stator throat does not choke; assumed η collapses. "
                      + "Raise preburner Pc, lower turbine back-pressure, or "
                      + "switch to a higher-π cycle.",
                    ActualValue:  pr,
                    Limit:        fuelT.CriticalPressureRatio));
            }
            if (turbines.OxTurbine is { IsChoked: false } oxT)
            {
                double pr = oxT.OutletPressure_Pa
                          / System.Math.Max(oxT.InletPressure_Pa, 1.0);
                v.Add(new FeasibilityViolation(
                    ConstraintId: "TURBINE_UNCHOKED",
                    Description:
                        $"Ox turbine subsonic: π = p_out/p_in = {pr:F3} > "
                      + $"π_crit = {oxT.CriticalPressureRatio:F3} for γ={oxT.Gamma:F2}. "
                      + "Stator throat does not choke; assumed η collapses.",
                    ActualValue:  pr,
                    Limit:        oxT.CriticalPressureRatio));
            }
        }
        if (gen.ExpanderTurbine is { IsChoked: false } exT)
        {
            double pr = exT.OutletPressure_Pa
                      / System.Math.Max(exT.InletPressure_Pa, 1.0);
            v.Add(new FeasibilityViolation(
                ConstraintId: "TURBINE_UNCHOKED",
                Description:
                    $"Expander turbine subsonic: π = p_out/p_in = {pr:F3} > "
                  + $"π_crit = {exT.CriticalPressureRatio:F3} for γ={exT.EffectiveGamma:F2}. "
                  + "Closed-expander cycles are at high risk: jacket ΔP is modest. "
                  + "Raise jacket outlet pressure, lower turbine back-pressure, or "
                  + "switch to a preburner cycle.",
                ActualValue:  pr,
                Limit:        exT.CriticalPressureRatio));
        }
        if (gen.TapOffTurbine is { IsChoked: false } toT)
        {
            double pr = toT.OutletPressure_Pa
                      / System.Math.Max(toT.ChamberPressure_Pa, 1.0);
            v.Add(new FeasibilityViolation(
                ConstraintId: "TURBINE_UNCHOKED",
                Description:
                    $"Tap-off turbine subsonic: π = p_out/p_in = {pr:F3} > "
                  + $"π_crit = {toT.CriticalPressureRatio:F3} for γ={toT.EffectiveGamma:F2}. "
                  + "Tap-off is the most exposed cycle — low-Pc designs may not "
                  + "choke. Raise chamber Pc or switch to a preburner / expander cycle.",
                ActualValue:  pr,
                Limit:        toT.CriticalPressureRatio));
        }
    }

    private static void EmitInstrumentationThermalBridgeRisk(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.SensorBosses is not { Count: > 0 } bossesForBridge) return;
        if (gen.Thermal.Stations.Length == 0) return;
        if (gen.Contour.TotalLength_mm <= 0) return;

        var bridgeMaterial = WallMaterials.All[System.Math.Clamp(
            gen.Conditions.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        const double k_boss_typical_WmK = 16.0; // 316L LPBF default
        double k_wall = bridgeMaterial.ConductivityAt(500.0);
        double conductivityDelta =
            System.Math.Abs(k_boss_typical_WmK - k_wall) / System.Math.Max(k_wall, 1e-3);
        if (conductivityDelta <= FeasibilityGate.InstrumentationBridgeConductivityRatio) return;

        double peakFlux = 0;
        for (int i = 0; i < gen.Thermal.Stations.Length; i++)
            peakFlux = System.Math.Max(peakFlux, gen.Thermal.Stations[i].HeatFlux_Wm2);
        double highFluxThreshold = peakFlux * FeasibilityGate.InstrumentationBridgeHighFluxFraction;

        int idx = 0;
        foreach (var boss in bossesForBridge)
        {
            idx++;
            double bossX_mm = boss.AxialFraction * gen.Contour.TotalLength_mm;
            int nearest = 0;
            double bestDx = double.MaxValue;
            for (int i = 0; i < gen.Thermal.Stations.Length; i++)
            {
                double dx = System.Math.Abs(gen.Thermal.Stations[i].X_mm - bossX_mm);
                if (dx < bestDx) { bestDx = dx; nearest = i; }
            }
            double q = gen.Thermal.Stations[nearest].HeatFlux_Wm2;
            if (q > highFluxThreshold)
            {
                v.Add(new FeasibilityViolation(
                    ConstraintId: "INSTRUMENTATION_THERMAL_BRIDGE_RISK",
                    Description:
                        $"Sensor boss #{idx} ({boss.Type}) at x={bossX_mm:F0} mm sits in "
                      + $"high-flux region (q\"={q / 1e6:F2} MW/m², "
                      + $"{q / System.Math.Max(peakFlux, 1) * 100:F0} % of peak). Wall material "
                      + $"k={k_wall:F0} W/m·K vs typical 316L boss k≈{k_boss_typical_WmK:F0} "
                      + "W/m·K creates a thermal-bridge hot-spot beyond 1-D solver prediction. "
                      + "Move the boss out of the throat region or specify a matching alloy.",
                    ActualValue:  q,
                    Limit:        highFluxThreshold));
            }
        }
    }

    private static void EmitCommonShaftRpmInconsistent(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Turbopump is not { } tpG) return;
        if (!FeedSystem.TurbopumpSizing.IsCommonShaft(tpG.Cycle)) return;
        if (tpG.FuelPump is not { } fpG || fpG.Rpm <= 0) return;
        if (tpG.OxPump  is not { } opG || opG.Rpm <= 0) return;

        double rpmDiscFrac = System.Math.Abs(fpG.Rpm - opG.Rpm)
                           / System.Math.Max(fpG.Rpm, opG.Rpm);
        if (rpmDiscFrac <= 0.005) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "COMMON_SHAFT_RPM_INCONSISTENT",
            Description:
                $"Common-shaft cycle ({tpG.Cycle}): fuel pump RPM {fpG.Rpm:F0} "
              + $"≠ ox pump RPM {opG.Rpm:F0} "
              + $"({rpmDiscFrac * 100:F1} % discrepancy). "
              + "A single shaft cannot rotate at two angular velocities simultaneously. "
              + "Both pumps must share the lower N_s-derived shaft speed.",
            ActualValue: rpmDiscFrac * 100,
            Limit:       0.5));
    }

    private static void EmitTpmsAndManifoldOverlap(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        bool isTpmsTopology = gen.ChannelTopology == ChannelTopology.TpmsGyroid
                           || gen.ChannelTopology == ChannelTopology.TpmsSchwarzP;
        if (!isTpmsTopology) return;
        if (gen.ManifoldLength_mm <= 0) return;
        if (gen.Contour.TotalLength_mm <= 0) return;

        double manifoldSpan_mm = 2.0 * gen.ManifoldLength_mm;
        if (manifoldSpan_mm < gen.Contour.TotalLength_mm) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "TPMS_AND_MANIFOLD_OVERLAP",
            Description:
                $"TPMS topology with manifolds spanning {manifoldSpan_mm:F1} mm "
              + $"(2 × {gen.ManifoldLength_mm:F1} mm per end) reaches or "
              + $"exceeds total chamber length {gen.Contour.TotalLength_mm:F1} mm — "
              + "opposite-end manifolds would overlap at the chamber centre, "
              + "leaving no clear TPMS unit-cell region. Risks PicoGK pitfall "
              + "#2 (BoolSubtract-through-TPMS produces fragments). Shorten "
              + "the manifold, lengthen the chamber, or switch to Axial topology.",
            ActualValue:  manifoldSpan_mm,
            Limit:        gen.Contour.TotalLength_mm));
    }

    private static void EmitBimetallicBondZoneShear(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Stress.BondZoneShearStress_MPa <= 0) return;
        if (gen.Stress.BondZoneShearRatio <= 1.0) return;

        double shearLimit_MPa = gen.Stress.BondZoneShearStress_MPa / gen.Stress.BondZoneShearRatio;
        v.Add(new FeasibilityViolation(
            ConstraintId: "BIMETALLIC_BOND_ZONE_SHEAR",
            Description:
                $"Bimetallic bond-zone shear τ {gen.Stress.BondZoneShearStress_MPa:F1} MPa > "
              + $"σ_y·0.5 threshold {shearLimit_MPa:F1} MPa (ratio {gen.Stress.BondZoneShearRatio:F2}×). "
              + "CTE mismatch between liner and jacket drives interfacial shear that may "
              + "initiate bond-zone cracking under thermal cycling (NASA PURS LPBF data). "
              + "Remediation: reduce ΔT across the wall by increasing coolant flow or "
              + "widening throat channels; or introduce a CTE-gradient interlayer. "
              + "Real FEA with constraint geometry is advised before fabrication.",
            ActualValue: gen.Stress.BondZoneShearStress_MPa,
            Limit:       shearLimit_MPa));
    }

    private static void EmitLcfLifeInsufficient(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.LowCycleFatigue is not { } lcf) return;
        if (lcf.MissionCycles < Structure.LowCycleFatigueAnalysis.LowCycleAdvisoryThreshold) return;
        double limit = Structure.LowCycleFatigueAnalysis.SafetyFactorOnCycles * lcf.MissionCycles;
        if (lcf.PredictedCyclesToFailure >= limit) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "LCF_LIFE_INSUFFICIENT",
            Description:
                $"Predicted LCF life {lcf.PredictedCyclesToFailure:F0} cycles < "
              + $"{Structure.LowCycleFatigueAnalysis.SafetyFactorOnCycles:F1}× mission demand of "
              + $"{lcf.MissionCycles} (= {limit:F0} cycles) at station {lcf.CriticalStationIndex} "
              + $"(T_wg={lcf.CriticalStationT_wg_K:F0} K, T_wc={lcf.CriticalStationT_wc_K:F0} K, "
              + $"Δε={lcf.TotalStrainRange:G3}). Coffin-Manson per NASA PURS {lcf.MaterialName} data. "
              + "Reduce ΔT through wall (more coolant flow, thinner liner), pick a higher-σ_f "
              + "material, or lower the cycle target.",
            ActualValue: lcf.PredictedCyclesToFailure,
            Limit:       limit));
    }

    // OOB-6 / Sprint B-3 (2026-04-30): acoustic-damper advisory gates.
    // Both self-suppress when no damper is configured (StabilityReport.AcousticDamper
    // is null). Severity = Advisory so SA never rejects on these — model is
    // empirical and the user's free to ignore them. The Notes string from
    // AcousticDamperResult is preserved verbatim into the violation
    // description so the build sheet + report exporter surface the closest
    // mode + detune fraction without re-deriving the comparison.

    private static void EmitAcousticDamperDetuned(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Stability.AcousticDamper is not { } damper) return;
        if (damper.IsTunedToAnyMode) return;

        // Detune metric: ratio of damper f₀ to nearest mode. > 1.10 (i.e.
        // > 10 % off) → outside the tuning band; > 1.20 → effectively
        // detuned (Lorentzian gives < 25 % of peak damping).
        double f0 = damper.ResonanceFrequency_Hz;
        double[] modes = { gen.Stability.Screech.L1_Hz, gen.Stability.Screech.T1_Hz, gen.Stability.Screech.T2_Hz };
        double closest = modes[0]; double bestErr = double.MaxValue;
        foreach (double m in modes)
        {
            double err = m > 0 ? System.Math.Abs(f0 - m) / m : double.MaxValue;
            if (err < bestErr) { bestErr = err; closest = m; }
        }

        v.Add(new FeasibilityViolation(
            ConstraintId: "ACOUSTIC_DAMPER_DETUNED",
            Description:
                $"{damper.Notes} Damper outside ±{Combustion.Stability.AcousticDamper.TuningBandFraction:P0} "
              + $"tuning band of any chamber mode (closest at {bestErr:P1} detune). "
              + $"Effective Δζ on the closest mode is < {Combustion.Stability.AcousticDamper.PeakDampingRatio_PerResonator * 0.5:F3}; "
              + "retune the geometry (Helmholtz: trade neck-area vs. cavity-volume; "
              + "quarter-wave: change cavity length) to land on L1, T1, or T2.",
            ActualValue: f0,
            Limit:       closest));
    }

    private static void EmitAcousticDamperOversized(RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Stability.AcousticDamper is not { Count: > 0 } damper) return;

        // Recompute total damper volume from the saved design fields.
        // Stability.AcousticDamper holds the result; the input config
        // is already gone, so derive total volume from the design's
        // damper fields directly.
        Combustion.Stability.AcousticDamperConfig config =
            damper.Type == Combustion.Stability.AcousticDamperType.Helmholtz
                ? Combustion.Stability.AcousticDamperConfig.Helmholtz(
                    count: damper.Count,
                    neckArea_mm2: 0, neckLength_mm: 0, cavityVolume_mm3: 0)
                : Combustion.Stability.AcousticDamperConfig.QuarterWave(
                    count: damper.Count, length_mm: 0, diameter_mm: 0);
        // The `damper` result doesn't carry the geometry payload through
        // (deliberately — Δζ is the consumer-facing surface). Re-read
        // the design's damper fields directly from gen.Conditions's
        // sibling design record on RegenGenerationResult is not available
        // here — RegenGenerationResult.Conditions holds OperatingConditions,
        // not RegenChamberDesign. Skip this gate body when we can't
        // recompute the total volume; the DETUNED gate already surfaces
        // the configuration mistake the user should care about.
        // Future RegenGenerationResult rev should carry the design too;
        // tracked as a follow-on (no GH issue yet, see PR description).
        _ = config;

        // Conservative oversize check based on the resonator count alone:
        // > 16 distributed resonators around a chamber circumference
        // implies inter-resonator spacing < 22.5° which competes with
        // injector-element placement and adds mass-fraction concerns.
        const int CountAdvisoryThreshold = 16;
        if (damper.Count <= CountAdvisoryThreshold) return;
        v.Add(new FeasibilityViolation(
            ConstraintId: "ACOUSTIC_DAMPER_OVERSIZED",
            Description:
                $"Damper count {damper.Count} > {CountAdvisoryThreshold} resonators around the "
              + "chamber circumference packs the cavities at < 22.5° azimuthal pitch. "
              + "Mutual coupling scrambles the √N coherent-combining assumption (capped "
              + $"at {Combustion.Stability.AcousticDamper.CoherentCombiningCap}) and the "
              + "added mass-fraction starts dominating the Isp budget. Reduce count or "
              + "split into two axial bands at different f₀.",
            ActualValue: damper.Count,
            Limit:       CountAdvisoryThreshold));
    }

    private static void EmitExpansionDeflectionPlugClearance(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.ChannelTopology != ChannelTopology.ExpansionDeflection) return;

        // For E-D designs, GenerateWith inflated the contour throat radius to
        // R_cowl = R_t / √(1 − 0.40²) ≈ 1.091·R_t. The plug tip radius is
        // then 0.40·R_cowl. Below 12 mm cowl the plug tip is < 4.8 mm —
        // too small for an LPBF-printable plug with any internal geometry.
        double rCowl_mm = gen.Contour.ThroatRadius_mm;
        const double MinCowlRadius_mm = 12.0;
        if (rCowl_mm >= MinCowlRadius_mm) return;

        double rPlug_mm = 0.40 * rCowl_mm;
        v.Add(new FeasibilityViolation(
            ConstraintId: "EXPANSION_DEFLECTION_PLUG_CLEARANCE",
            Description:
                $"E-D cowl throat radius {rCowl_mm:F1} mm < {MinCowlRadius_mm:F0} mm advisory floor "
              + $"(plug tip radius = {rPlug_mm:F1} mm). At this scale the inner plug "
              + "cannot accommodate LPBF-printable cooling passages (LPBF min wall ≈ 0.4 mm, "
              + "min channel ≈ 0.5 mm). Consider a higher-thrust design or accept a solid uncooled plug.",
            ActualValue: rCowl_mm,
            Limit:       MinCowlRadius_mm));
    }

    // OOB-2 Sprint 3 (ADR-024 / #198): SIMP topology-channel printability.
    // Computes the topology-optimized channel width at each station from
    // W_i = max(0, (2π·R_i − n_i·rib_mm) / n_i) and fires when any W_i
    // drops below the universal LPBF feature floor. Short-circuits when
    // TopologyChannels is null (non-TopologyOptimized designs).
    private static void EmitTopologyChannelNotPrintable(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.TopologyChannels is not { } topo) return;
        if (gen.Contour.Stations.Length == 0) return;

        int[] nArr = topo.ChannelsPerStation;
        if (nArr.Length != gen.Contour.Stations.Length) return;

        // Derive rib thickness from the baseline thermal-solver output at
        // station 0: rib = (2π·R − N_base·W_baseline) / N_base.
        // gen.ChannelCount is the baseline N echoed from the design.
        double floor = FeasibilityGate.LpbfFeatureFloor_mm;
        int nBase = System.Math.Max(gen.ChannelCount, 1);
        var st0  = gen.Thermal.Stations.Length > 0 ? gen.Thermal.Stations[0] : null;
        double ribEst_mm = st0 is not null
            ? System.Math.Max(0.0,
                (2.0 * System.Math.PI * gen.Contour.Stations[0].R_mm
                 - nBase * st0.ChannelWidth_mm) / nBase)
            : 0.4;   // conservative LPBF rib fallback

        double minWidth = double.MaxValue;
        int    minIdx   = 0;
        for (int i = 0; i < nArr.Length; i++)
        {
            int    n    = System.Math.Max(nArr[i], 1);
            double r_mm = gen.Contour.Stations[i].R_mm;
            double w    = System.Math.Max(0.0,
                (2.0 * System.Math.PI * r_mm - n * ribEst_mm) / n);
            if (w < minWidth) { minWidth = w; minIdx = i; }
        }

        if (minWidth >= floor) return;

        double xMin_mm = gen.Contour.Stations[minIdx].X_mm;
        v.Add(new FeasibilityViolation(
            ConstraintId: "TOPOLOGY_CHANNEL_NOT_PRINTABLE",
            Description:
                $"Topology-optimized channel width {minWidth:F2} mm at axial station "
              + $"x≈{xMin_mm:F0} mm (station {minIdx}) < LPBF print floor {floor:F2} mm. "
              + "Reduce N_base or increase rib thickness so the narrowest SIMP station "
              + "stays above the LPBF minimum feature size (ADR-024 §Consequences).",
            ActualValue: minWidth,
            Limit:       floor));
    }

    // Sprint C / #350 (2026-05-04): combined axial-bending gate.
    // Self-suppresses when GimbalOffset_mm = 0 so the gate is gate-neutral
    // for all pre-Sprint-C designs. Hibbeler §8.4 safety factor = 1.5.
    private static void EmitCombinedAxialBendingInsufficient(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Conditions.GimbalOffset_mm <= 0) return;
        double peakVM  = gen.Stress.PeakAxialBendingVM_MPa;
        double sigmaY  = gen.Stress.PeakAxialBendingYield_MPa;
        const double RequiredSF = 1.5;
        double limit = sigmaY / RequiredSF;
        if (!(peakVM > limit)) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "COMBINED_AXIAL_BENDING_INSUFFICIENT",
            Description:
                $"Peak combined axial-bending VM {peakVM:F1} MPa > σ_y/{RequiredSF:F1} = {limit:F1} MPa "
              + $"(σ_y = {sigmaY:F1} MPa at peak station, GimbalOffset = {gen.Conditions.GimbalOffset_mm:F1} mm). "
              + "Hibbeler §8.4 combined-load SF 1.5 not met. Increase wall thickness at the throat, "
              + "reduce gimbal offset, or choose a higher-σ_y liner material.",
            ActualValue: peakVM,
            Limit:       limit));
    }

    // OOB-12 (2026-05-04): transpiration bleed excessive advisory gate.
    private static void EmitTranspirationBleedExcessive(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (!gen.EnableTranspirationCooling) return;
        const double ExcessiveBleedFraction = 0.15;
        if (gen.TranspirationBleedFraction <= ExcessiveBleedFraction) return;
        v.Add(new FeasibilityViolation(
            ConstraintId: "TRANSPIRATION_BLEED_EXCESSIVE",
            Description:
                $"Transpiration bleed {gen.TranspirationBleedFraction:P1} exceeds {ExcessiveBleedFraction:P0} — "
              + "regen jacket starved non-linearly above this fraction (Sutton §4.3). "
              + "Reduce TranspirationBleedFraction or accept reduced jacket coolant margin.",
            ActualValue: gen.TranspirationBleedFraction,
            Limit:       ExcessiveBleedFraction));
    }

    // OOB-14 (issue #341): ablative throat recession budget (Hard gate).
    // Fires when the AblativeResult.IsAcceptable == false, meaning the
    // predicted recession × SF exceeds the initial liner thickness before
    // end-of-burn. Suppressed when gen.Ablative is null (material == None).
    private static void EmitAblativeThroatRecessionExceedsBudget(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Ablative is not { } ablative) return;
        if (ablative.IsAcceptable) return;
        double maxPenetrated = (ablative.MaxRecession_mm + ablative.MaxCharDepth_mm)
                             * ablative.SafetyFactor;
        v.Add(new FeasibilityViolation(
            ConstraintId: "ABLATIVE_THROAT_RECESSION_EXCEEDS_BUDGET",
            Description:
                $"Ablative liner predicted penetration {maxPenetrated:F2} mm "
              + $"(recession {ablative.MaxRecession_mm:F2} + char {ablative.MaxCharDepth_mm:F2} mm, "
              + $"SF {ablative.SafetyFactor:F2}×) exceeds initial thickness "
              + $"{ablative.InitialThickness_mm:F2} mm in {ablative.BurnDuration_s:F1} s "
              + $"(Sutton 9e §16.3). Increase AblativeThickness_mm, reduce burn duration, "
              + "or select a more refractory material (CarbonPhenolic > SilicaPhenolic).",
            ActualValue: maxPenetrated,
            Limit:       ablative.InitialThickness_mm));
    }

    // OOB-14 (issue #341): ablative-regen interface temperature advisory.
    // Fires when ChannelTopology == AblativeThroat AND the thermal result's
    // peak gas-side wall temperature at the regen stations nearest the
    // zone boundary exceeds the ablative material's char temperature.
    // Uses gen.Thermal.PeakGasSideWallT_K as a conservative proxy
    // (peak occurs at/near the throat band boundary where regen and
    // ablative zones meet). Suppressed when gen.Ablative is null.
    private static void EmitAblativeRegenInterfaceOvertemp(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.Ablative is not { } ablative) return;
        if (gen.ChannelTopology != ChannelTopology.AblativeThroat) return;
        if (gen.Thermal is not { } thermal) return;

        if (!AblativeMaterials.All.TryGetValue(ablative.Material, out var spec)) return;

        double charT = spec.CharTemperature_K;
        double peakT = thermal.PeakGasSideWallT_K;
        if (!(peakT > charT)) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "ABLATIVE_REGEN_INTERFACE_OVERTEMP",
            Description:
                $"Regen-side peak wall T {peakT:F0} K exceeds ablative char temperature "
              + $"{charT:F0} K for {ablative.Material} at the zone interface. "
              + "Pyrolysis will activate in the regen zone, defeating the hybrid-throat "
              + "strategy. Consider a higher char-T material or reducing Pc.",
            ActualValue: peakT,
            Limit:       charT));
    }

    // OOB-9 (issue #344): advisory — finite-rate Isp penalty > 1.5 %.
    // Self-guards when UseFiniteRateCorrection is false so the gate is
    // silent on all legacy / pressure-fed designs by default.
    private static void EmitFiniteRateIspPenaltyLarge(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (!gen.Conditions.UseFiniteRateCorrection) return;
        const double threshold = 0.985;
        if (gen.FiniteRateCorrectionFactor >= threshold) return;
        double penaltyPct = (1.0 - gen.FiniteRateCorrectionFactor) * 100.0;
        v.Add(new FeasibilityViolation(
            ConstraintId: "FINITE_RATE_ISP_PENALTY_LARGE",
            Description:  $"Finite-rate dissociation penalty {penaltyPct:F1} % "
                        + $"(factor {gen.FiniteRateCorrectionFactor:F4}) exceeds the "
                        + $"1.5 % advisory threshold at Pc "
                        + $"{gen.Conditions.ChamberPressure_Pa / 1e6:F1} MPa. "
                        + "Consider raising chamber pressure or switching to a "
                        + "less dissociation-prone propellant pair.",
            ActualValue:  gen.FiniteRateCorrectionFactor,
            Limit:        threshold));
    }

    // OOB-7 (issue #343): RDE annulus fill time gate.
    // Inter-wave period = 1 / (N × wave_frequency).
    // Wave frequency approximated as wave_speed / circumference
    //   where wave_speed ≈ CJ speed × waveSpeedFraction.
    // CJ speed anchored at 2400 m/s (LOX/CH4 class, Shepherd 1986).
    // Gate fires when τ_fill > τ_period (annulus starved between waves).
    private static void EmitRdeAnnulusFillStarved(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.RdeTopology == Optimization.RdeTopology.None) return;
        if (gen.RdeWaveCount <= 0) return;
        if (gen.RdeAnnulusFillTime_us <= 0.0) return;

        // Annulus circumference from design; outer-radius field on the result
        // is unavailable directly — reconstruct from the conditions record and
        // the design echoed in Conditions. Use a nominal 60 mm default.
        const double cjSpeed_ms = 2400.0;
        const double waveSpeedFraction = 0.90;
        // Outer circumference must be re-derived; echo fields give us wave count
        // and fill time. Wave period = 1 / (N × f_wave).
        // f_wave = v_wave / C where C = outer circumference.
        // We cannot recover C from the echo fields alone — use a representative
        // circumference that's consistent with DetonationWaveCount's formula
        // inverted: C_nominal = N × f × L_cj / v_frac.
        const double lcj_m = 0.020;
        double cNominal_m = gen.RdeWaveCount * waveSpeedFraction * lcj_m;
        double waveFreq_hz   = cjSpeed_ms * waveSpeedFraction / cNominal_m;
        double interWavePeriod_us = 1e6 / (gen.RdeWaveCount * waveFreq_hz);

        if (!(gen.RdeAnnulusFillTime_us > interWavePeriod_us)) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "RDE_ANNULUS_FILL_STARVED",
            Description:
                $"RDE annulus fill time {gen.RdeAnnulusFillTime_us:F1} µs exceeds "
              + $"inter-wave period {interWavePeriod_us:F1} µs "
              + $"(N={gen.RdeWaveCount} waves, f_wave ≈ {waveFreq_hz:F0} Hz). "
              + "Propellant cannot refill the channel before the next detonation wave — "
              + "misfire or detonation collapse expected. Reduce channel height or "
              + "increase injector ΔP.",
            ActualValue: gen.RdeAnnulusFillTime_us,
            Limit:       interWavePeriod_us));
    }

    // OOB-7 (issue #343): RDE wave count below minimum advisory.
    // Fewer than 2 simultaneous waves → single-wave or deflagration
    // behaviour; efficiency gain is uncertain (Wolański 2013 §4).
    private static void EmitRdeWaveCountBelowMinimum(
        RegenGenerationResult gen, List<FeasibilityViolation> v)
    {
        if (gen.RdeTopology == Optimization.RdeTopology.None) return;
        const int minWaves = 2;
        if (gen.RdeWaveCount >= minWaves) return;

        v.Add(new FeasibilityViolation(
            ConstraintId: "RDE_WAVE_COUNT_BELOW_MINIMUM",
            Description:
                $"Estimated detonation wave count {gen.RdeWaveCount} < {minWaves}. "
              + "Single-wave or transitional RDE combustion provides uncertain Isp gains. "
              + "Increase annulus outer radius to support stable multi-wave propagation "
              + "(Wolański 2013 §4).",
            ActualValue: gen.RdeWaveCount,
            Limit:       minWaves));
    }
}
