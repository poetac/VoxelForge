using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.Manufacturing;
using Voxelforge.Structure;

namespace Voxelforge.Optimization;

public sealed record RegenGenerationResult(
    ChamberContour Contour,
    ChamberGeometryResult Geometry,
    RegenSolverOutputs Thermal,
    StructuralSummary Stress,
    ManufacturingReport Manufacturing,
    DerivedValues Derived,
    PropellantState Gas,
    OperatingConditions Conditions,
    StabilityReport Stability,
    InjectorPattern? InjectorPattern = null,
    PatternSizingResult? InjectorSizing = null,
    Analysis.VoxelAdequacyResult? VoxelAdequacy = null,
    // Provenance hash over (cond, design). Lets the UI detect and
    // badge stale dependent analyses (proof test, tolerance).
    string DesignHash = "",
    // PHASE 2 (2026-04-20): injector face equilibrium T estimate. Null when
    // no implemented pattern is set (nothing to size a bore-cooled face from).
    HeatTransfer.InjectorFaceResult? InjectorFace = null,
    // TIER B.6 (2026-04-21): residual-stress / warp prediction after LPBF
    // build + heat-treatment, using the inherent-strain method.
    Manufacturing.ResidualStressResult? Residual = null,
    // Structural-confidence downgrade when threaded ports / flanges
    // introduce stress concentrations the analytical hoop/thermal VM
    // check does not model. High = plain ports + no flange, Medium =
    // threaded or flanged, Low = threaded axial propellant ports
    // piercing the injector flange.
    StructuralConfidence StructuralConfidence = StructuralConfidence.High,
    string StructuralConfidenceReason = "",
    // Feed-system ΔP stackup from tank ullage to chamber. Null when
    // the user hasn't opted in
    // (OperatingConditions.TankUllagePressure_Pa == 0).
    FeedSystem.PressureStackupResult? FeedStackup = null,
    // Igniter preset in use, surfaced on the result so
    // FeasibilityGate / ReportExport can read it without a separate
    // design reference. None = no igniter configured.
    Geometry.IgniterType IgniterType = Geometry.IgniterType.None,
    // Gimbal mount stiffness + bearing-stress evaluation for the
    // selected mount configuration. FixedFlange sets infinite notional
    // stiffness and is always structurally acceptable.
    Structure.GimbalMountResult? GimbalMount = null,
    // Per-port purge-flow evaluation results. Empty array when the
    // design has no purge ports configured.
    Coolant.PurgePortResult[]? PurgeResults = null,
    // Ablative-liner recession analysis, populated when
    // RegenChamberDesign.AblativeMaterial != None.
    Manufacturing.AblativeResult? Ablative = null,
    // Pre-fire chilldown transient, populated when
    // RegenChamberDesign.IncludeChilldownTransient is true AND the
    // propellant pair is cryogenic.
    HeatTransfer.ChilldownResult? Chilldown = null,
    // Start-transient simulation, populated when
    // RegenChamberDesign.IncludeStartTransient is true.
    Combustion.StartTransientResult? StartTransient = null,
    // Shutdown / blowdown transient (Hot-fire Item 4 close-out,
    // 2026-04-28). Shares the IncludeStartTransient opt-in flag —
    // safety-review cycles that want the startup analysis also want
    // the symmetric shutdown for residual-propellant + time-to-cutoff
    // accounting.
    Combustion.ShutdownBlowdownResult? ShutdownBlowdown = null,
    // Turbopump sizing + NPSH check. Populated when EngineCycle !=
    // PressureFed.
    FeedSystem.TurbopumpResult? Turbopump = null,
    // Channel-topology echo. Surfaced on the result so FeasibilityGate
    // + ReportExport can branch without a separate design reference.
    // Axial is the default for legacy designs; TPMS fields below are
    // only consumed when this is a TPMS family.
    ChannelTopology ChannelTopology = ChannelTopology.Axial,
    // TPMS unit-cell edge (mm). Only meaningful on a TPMS topology.
    double TpmsCellEdge_mm = 0.0,
    // TPMS solid-volume fraction. Only meaningful on a TPMS topology.
    double TpmsSolidFraction = 0.50,
    // Preburner sizing result for staged-combustion / gas-generator /
    // FFSC cycles. Null on PressureFed / ElectricPump / OpenExpander
    // (no preburner). For FFSC: this holds the fuel-rich preburner;
    // OxidizerPreburner holds the ox-rich sibling.
    Chamber.PreburnerResult? Preburner = null,
    // FFSC ox-rich preburner. Non-null only when
    // OperatingConditions.EngineCycle is FullFlow.
    // Paired with Preburner (fuel-rich side).
    Chamber.PreburnerResult? OxidizerPreburner = null,
    // Aerospike physics-only build result. Populated by GenerateWith
    // when design.ChannelTopology is Aerospike; null otherwise.
    // Carries the aerospike contour + plug cooling result + mass /
    // volume so UI / report / aerospike-specific scoring terms can
    // read aerospike-meaningful values without calling the voxel
    // builder again. The regen-path fields (Contour, Thermal, etc.)
    // still reflect the fallback bell-chamber computation so the
    // existing Evaluate scoring doesn't crash.
    Geometry.AerospikeBuildResult? Aerospike = null,
    // Sprint 23 (2026-04-23): expander-cycle turbine energy balance.
    // Populated when EngineCycle is OpenExpander or ClosedExpander and
    // the regen jacket absorbed heat. PowerSufficient == false seeds
    // the EXPANDER_TURBINE_ENTHALPY_DEFICIT feasibility gate.
    FeedSystem.ExpanderTurbineResult? ExpanderTurbine = null,
    // Sprint 25 (2026-04-23): tap-off cycle turbine energy balance.
    // Populated when EngineCycle is TapOff. TapPointTemperatureOK ==
    // false seeds the TAPOFF_HOT_GAS_TOO_HOT feasibility gate.
    FeedSystem.TapOffTurbineResult? TapOffTurbine = null,
    // Sprint 27 (2026-04-23): LPBF printability analysis. Populated
    // only when design.IncludeLpbfPrintabilityAnalysis is true.
    // Seeds the OVERHANG_ANGLE_EXCEEDED / TRAPPED_POWDER_REGION /
    // DRAIN_PATH_MISSING feasibility gates (all opt-in: gates silent
    // when the result is null).
    Geometry.LpbfAnalysis.LpbfPrintabilityResult? Printability = null,
    // Sprint 28 (2026-04-24): instrumentation-tap clash-detection inputs
    // surfaced on the result so the feasibility evaluator can fire the
    // INSTRUMENTATION_TAP_INTERFERENCE gate without needing a separate
    // RegenChamberDesign handle. ChannelCount and SensorBosses together
    // are the minimum data the axisymmetric clash check needs
    // (channel-vs-boss overlap in azimuth, boss-vs-boss arc spacing).
    // Defaults are a no-op: ChannelCount=0 short-circuits the channel-
    // clash branch; empty bosses list short-circuits everything else.
    int ChannelCount = 0,
    System.Collections.Generic.IReadOnlyList<Geometry.SensorBoss>? SensorBosses = null,
    // Z2.8 (2026-04-28): elastic burst margin factor (= P_burst_elastic /
    // MEOP) computed cheaply via thin-wall hoop on the gas-side wall
    // profile. Seeds the BURST_MARGIN_INSUFFICIENT feasibility gate (gate
    // 14c) with threshold 2.5× per ASME BPVC §VIII Div 1 (PR #104). Default
    // 0 short-circuits the gate for legacy/synthetic call sites that don't
    // populate it (matches pre-Z2.8 behaviour bit-identically).
    double BurstMarginFactor = 0.0,
    // PH-40 / issue #259 (2026-04-29): low-cycle-fatigue result computed
    // by Voxelforge.Structure.LowCycleFatigueAnalysis. Seeds the
    // LCF_LIFE_INSUFFICIENT feasibility gate which only fires when
    // RegenChamberDesign.MissionCycles ≥ 100 AND PredictedCyclesToFailure
    // < 4× MissionCycles. Default null short-circuits the gate for
    // legacy/synthetic call sites that don't populate it.
    Voxelforge.Structure.LowCycleFatigueResult? LowCycleFatigue = null,
    // OOB-2 Sprint 3 (2026-05-04): SIMP topology channel routing result.
    // Populated when design.ChannelTopology == ChannelTopology.TopologyOptimized
    // and the thermal solve completes. Null on all other topologies so existing
    // gate / report code is bit-identical. Seeds TOPOLOGY_CHANNEL_NOT_PRINTABLE.
    TopologyChannelResult? TopologyChannels = null,
    // OOB-12 (2026-05-04): transpiration cooling echo. Surfaced on the result
    // so FeasibilityGate + BuildSheet can branch without a separate design ref.
    // Default false / 0.02 short-circuits the gate for legacy/synthetic callers.
    bool EnableTranspirationCooling = false,
    double TranspirationBleedFraction = 0.02)
{
    /// <summary>
    /// Z3 #20 / Geometry B3 (2026-04-29): coolant manifold axial length
    /// per end (mm). Echoed from the design so FeasibilityGate can flag
    /// the <c>TPMS_AND_MANIFOLD_OVERLAP</c> advisory without dragging
    /// the <see cref="RegenChamberDesign"/> record through the gate
    /// signature. Default 0 routes the gate to skip silently for legacy
    /// callers / synthetic fixtures that don't set it. Production code
    /// paths populate from <see cref="RegenChamberDesign.ManifoldLength_mm"/>
    /// in <c>GenerateWith</c>.
    /// </summary>
    public double ManifoldLength_mm { get; init; }

    /// <summary>
    /// OOB-9 (issue #344): dissociation correction factor applied to vacuum Isp.
    /// 1.0 when <see cref="OperatingConditions.UseFiniteRateCorrection"/> is false
    /// (default — bit-identical to pre-OOB-9 behaviour). Less than 1.0 when
    /// finite-rate correction is enabled. Seeds the advisory gate
    /// <c>FINITE_RATE_ISP_PENALTY_LARGE</c> when factor &lt; 0.985.
    /// </summary>
    public double FiniteRateCorrectionFactor { get; init; } = 1.0;

    /// <summary>
    /// OOB-7 (issue #343): RDE combustion topology echoed from the design.
    /// <see cref="RdeTopology.None"/> when conventional deflagration is used.
    /// </summary>
    public RdeTopology RdeTopology { get; init; } = RdeTopology.None;

    /// <summary>
    /// OOB-7 (issue #343): estimated number of simultaneous detonation waves.
    /// 0 when <see cref="RdeTopology"/> is <see cref="RdeTopology.None"/>.
    /// </summary>
    public int RdeWaveCount { get; init; } = 0;

    /// <summary>
    /// OOB-7 (issue #343): annulus propellant fill time (µs) between wave passages.
    /// 0 when <see cref="RdeTopology"/> is <see cref="RdeTopology.None"/>.
    /// Seeds the hard gate <c>RDE_ANNULUS_FILL_STARVED</c>.
    /// </summary>
    public double RdeAnnulusFillTime_us { get; init; } = 0.0;

    /// <summary>
    /// T6 (2026-04-28): adapter that hoists the physics-only fields
    /// the injector-face thermal solver consumes into a standalone
    /// <see cref="HeatTransfer.InjectorFaceGeometry"/> record. Returns
    /// <c>null</c> when the design has no implemented injector pattern,
    /// no orifice sizing, or no thermal stations — the same precondition
    /// the legacy in-solver guards encoded. Lets
    /// <see cref="HeatTransfer.InjectorFaceThermal.Estimate"/> stay
    /// decoupled from <see cref="RegenGenerationResult"/> so it can be
    /// unit-tested from hand-built records.
    /// </summary>
    public HeatTransfer.InjectorFaceGeometry? ToInjectorFaceGeometry()
    {
        if (InjectorPattern is not { } pat
            || InjectorSizing is not { } sizing
            || Thermal.Stations.Length == 0)
            return null;
        var s0 = Thermal.Stations[0];
        return new HeatTransfer.InjectorFaceGeometry(
            ChamberRadius_mm:        Contour.ChamberRadius_mm,
            H_g_x0_Wm2K:             s0.h_g_Wm2K,
            T_aw_x0_K:                s0.AdiabaticWallTemp_K,
            T_film_face_x0_K:         s0.EffectiveRecoveryTemp_K,
            PropellantPair:           Conditions.PropellantPair,
            CoolantInletTemp_K:       Conditions.CoolantInletTemp_K,
            CoolantInletPressure_Pa:  Conditions.CoolantInletPressure_Pa,
            WallMaterialIndex:        Conditions.WallMaterialIndex,
            OxidizerMassFlow_kgs:     Derived.OxidizerMassFlow_kgs,
            FuelMassFlow_kgs:         Derived.FuelMassFlow_kgs,
            TotalMassFlow_kgs:        Derived.TotalMassFlow_kgs,
            Pattern:                  pat,
            Sizing:                   sizing,
            // PH-36 (2026-04-29): forward the per-pair oxidizer T from
            // OperatingConditions. Default 0 → InjectorFaceThermal falls
            // back to DefaultOxidizerInjectionT_K(pair).
            OxidizerInletTemp_K:      Conditions.OxidizerInletTemp_K,
            // PH-35 (2026-04-29): forward face-material max-T override.
            // Default 0 → InjectorFaceThermal uses
            // DefaultInjectorFaceMaxTemp_K (1200 K, IN625/SS).
            InjectorFaceMaxTemp_K_Override: Conditions.InjectorFaceMaxTemp_K_Override,
            // Z3-F4 (2026-04-29): chamber-side Mach for the mixing-layer
            // Mach attenuation. Computed from the station-0 area ratio
            // (s0.AreaRatioToThroat = A_t/A_chamber = 1/ε_c) via the
            // subsonic isentropic area-Mach relation. Falls back to 0
            // (legacy constant-mixing-eff path) when AreaRatioToThroat is
            // degenerate.
            ChamberMach: ComputeChamberMach(s0.AreaRatioToThroat, Gas.GammaChamber));
    }

    /// <summary>
    /// Z3-F4 (2026-04-29): compute the chamber-side Mach number from the
    /// station-0 area ratio (= A_throat / A_chamber = 1/ε_c) and the chamber
    /// γ via the subsonic isentropic area-Mach relation. Returns 0 (sentinel
    /// for "skip Mach-aware path") when the input is degenerate.
    /// </summary>
    private static double ComputeChamberMach(double areaRatioToThroat, double gammaChamber)
    {
        if (!(areaRatioToThroat > 0) || !(areaRatioToThroat < 1)) return 0.0;
        if (!(gammaChamber > 1.0)) return 0.0;
        double epsilonC = 1.0 / areaRatioToThroat;
        return Combustion.PropellantTables.MachFromAreaRatio(
            epsilonC, gammaChamber, supersonic: false);
    }
}
