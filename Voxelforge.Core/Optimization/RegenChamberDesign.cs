// RegenChamberDesign.cs — Full parameter bundle for one chamber design.
//
// Split into three levels:
//   1. OperatingConditions — what the chamber must achieve (user-fixed).
//   2. RegenChamberDesign  — geometry and cooling parameters (optimizer-tunable).
//   3. DerivedValues        — computed outputs (throat diameter, mass flows, etc.)
//
// Optimizer touches only the geometry parameters; operating conditions stay
// fixed across an optimization run.

using System.Text.Json.Serialization;
using Voxelforge.Combustion;
using Voxelforge.Engines;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;

namespace Voxelforge.Optimization;

/// <summary>
/// PHASE 4 (2026-04-20): cooling-channel topology selector.
/// </summary>
public enum ChannelTopology
{
    /// <summary>Straight channels parallel to the chamber axis (legacy).</summary>
    Axial,
    /// <summary>Channels spiral around the chamber at a non-zero pitch angle.</summary>
    Helical,
    /// <summary>
    /// No regen channels or jacket. The chamber becomes a pure shell
    /// of wall + jacket thickness; the regen solver short-circuits
    /// (zero coolant flow, peak wall T stamped above the material
    /// service limit) so an ablative-only design must go through the
    /// ablative-recession path to be feasible.
    /// </summary>
    None,
    /// <summary>
    /// Schoen gyroid TPMS cooling. Continuous porous-medium lattice in
    /// place of discrete channels; thermal-hydraulics routed through
    /// <see cref="HeatTransfer.TpmsCorrelations"/>. Gyroid is the
    /// balanced workhorse — highest Nu of the three TPMS families at
    /// moderate friction. Requires
    /// <see cref="RegenChamberDesign.TpmsCellEdge_mm"/> +
    /// <see cref="RegenChamberDesign.TpmsSolidFraction"/>. The
    /// TPMS_CELL_FEATURE_TOO_SMALL gate fires when the implied strut
    /// thickness is below 2.0 mm.
    /// </summary>
    TpmsGyroid,
    /// <summary>
    /// Schwarz-P TPMS cooling. Lowest surface-area density of the
    /// three; best for pressure-budget-limited designs where heat
    /// uptake is slack.
    /// </summary>
    TpmsSchwarzP,
    /// <summary>
    /// Schwarz-D TPMS cooling. Highest surface-area density; best for
    /// heat-flux-limited designs with coolant pressure headroom.
    /// </summary>
    TpmsSchwarzD,
    /// <summary>
    /// Aerospike / plug-nozzle topology. The conventional bell is
    /// replaced by an annular throat + plug body with optional
    /// truncation controlled by
    /// <see cref="RegenChamberDesign.PlugLengthRatio"/>. The voxel
    /// builder dispatches to
    /// <see cref="Geometry.AerospikeBuilder.Build"/> instead of the
    /// standard <see cref="Geometry.ChamberVoxelBuilder.Build"/>
    /// pipeline, producing a single-part engine shell suitable for
    /// LPBF with inherent altitude-compensation benefits over a bell.
    /// </summary>
    Aerospike,
    /// <summary>
    /// Linear (extruded-rectangular) aerospike. Same Angelino 2D
    /// expansion curve as <see cref="Aerospike"/>, but extruded along
    /// a transverse axis
    /// rather than revolved — giving a rectangular plug with
    /// bilateral-symmetric top + bottom throat slots (X-33 / XRS-2200
    /// lineage). Dispatches to
    /// <see cref="Geometry.AerospikeBuilder.BuildLinearPhysicsOnly"/>;
    /// voxelisation of the rectangular plug body is a follow-on.
    /// The <c>LINEAR_AEROSPIKE_ASPECT_RATIO</c> feasibility gate fires
    /// on designs with aspect ratio outside [0.30, 5.00].
    /// </summary>
    LinearAerospike,
    /// <summary>
    /// Expansion-deflection (E-D) nozzle. A truncated plug centre-body
    /// occupies the axial core so the annular throat deflects flow outward
    /// into a closed outer bell, giving altitude-compensation (like an
    /// aerospike) with the structural envelope of a conventional bell.
    /// The outer bell is regeneratively cooled as a standard regen jacket;
    /// the inner plug is thermally isolated in this first-pass model. The
    /// annular throat area equals the standard round-throat area (same
    /// Thrust / Pc / Cf relationship), with the cowl radius inflated by
    /// 1/√(1−0.40²) ≈ 1.091 to account for the plug blocking 40 % of the
    /// cowl radius (Angelino fixed inner/outer ratio). Voxelisation is
    /// physics-only in this release; the full PicoGK geometry builder is
    /// a follow-on sprint. See issue #213.
    /// </summary>
    ExpansionDeflection,
    /// <summary>
    /// SIMP density-field topology-optimized regen channels (OOB-2 / ADR-024).
    /// Channel count per axial station is redistributed proportional to the
    /// local Bartz heat-flux field via an Optimality Criteria update
    /// (Sigmund 2001). Same total channel volume as the baseline schedule.
    /// Sprint 1: physics-only; voxel branching deferred to Sprint 2.
    /// </summary>
    TopologyOptimized = 9,
    /// <summary>
    /// Ablative + regen hybrid throat (OOB-14 / issue #341).
    /// Regen channels cover the chamber + divergent section; an ablative
    /// liner occupies the throat band defined by
    /// <see cref="RegenChamberDesign.AblativeZoneStart_frac"/> …
    /// <see cref="RegenChamberDesign.AblativeZoneEnd_frac"/> (fractional
    /// axial position from chamber inlet to nozzle exit). The regen solver
    /// runs the full contour to produce the heat-flux profile; the ablative
    /// recession integral is applied only to the throat band.
    /// </summary>
    AblativeThroat = 10,
}

/// <summary>
/// Operating point the chamber must achieve. Fixed across an optimization run.
/// </summary>
public sealed record OperatingConditions : IEngineConditions
{
    /// <inheritdoc />
    public string Family => Engines.EngineFamilies.Rocket;

    public double Thrust_N { get; init; } = 2224.0;            // 500 lbf default
    public double ChamberPressure_Pa { get; init; } = 6.9e6;   // 1000 psia
    public double MixtureRatio { get; init; } = 3.3;            // LOX/CH4 near peak C*
    public double CoolantInletTemp_K { get; init; } = 150.0;
    public double CoolantInletPressure_Pa { get; init; } = 12e6;

    /// <summary>
    /// Oxidizer-side tank-inlet temperature (K) for pump-NPSHA
    /// computation. When &gt; 0, the Antoine equation
    /// (<see cref="Coolant.Antoine.VaporPressure_Pa"/>) computes
    /// P_vap from this temperature; the result feeds
    /// <c>NPSHA = (P_inlet − P_vap) / (ρ g) + v²/2g</c> in
    /// <c>TurbopumpSizing.SizeOnePump</c>.
    /// <para>
    /// Default <c>0</c> is a sentinel meaning "use legacy constant
    /// P_vap table" — preserves bit-identical pre-A6 behaviour for
    /// callers that don't opt in. Set to (e.g.) <c>90.18</c> for
    /// LOX-saturated-at-1-atm, or higher (subcooled or warmed by
    /// long feed lines) per the actual tank-side physics. Schema v19+
    /// (added by issue #158).
    /// </para>
    /// </summary>
    public double OxidizerInletTemp_K { get; init; } = 0.0;

    /// <summary>
    /// PH-35 (2026-04-29): override for the injector-face material's max
    /// service temperature (K). Default <c>0</c> = use the legacy constant
    /// <see cref="HeatTransfer.InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K"/>
    /// (1200 K — IN625/SS face material per A1-follow-on). Set to a lower
    /// value (e.g. 1100 K) when the design uses a brazed SS316L face on a
    /// CuCrZr liner — the face often runs cooler than the gas-side liner
    /// limit. Feeds the <c>INJECTOR_FACE_T_EXCEEDED</c> feasibility gate
    /// via <see cref="HeatTransfer.InjectorFaceResult.MaxServiceTemp_K"/>.
    /// Pre-PH-35 the gate read a hardcoded 1200 K regardless of the
    /// user's face-plate alloy choice.
    /// </summary>
    public double InjectorFaceMaxTemp_K_Override { get; init; } = 0.0;

    public int WallMaterialIndex { get; init; } = 1;           // 0=GRCop42, 1=CuCrZr, 2=Inc625, 3=Inc718
    public double CStarEfficiency { get; init; } = 0.95;

    /// <summary>
    /// Boundary-layer + two-phase loss factor on <c>C_F</c>. Default
    /// 0.94 retained from the pre-PH-19 lumped knob.
    /// <para>
    /// PH-19 (#176, 2026-04-29) split the divergence-loss term out of
    /// this knob. Pre-PH-19, <c>NozzleCfEfficiency</c> lumped three
    /// distinct losses: divergence λ_div (geometry), boundary layer η_BL
    /// (Pc·Dt-dependent), and two-phase η_2Φ (propellant-dependent).
    /// SA saw no Isp incentive to minimise bell-exit angle θ_e because
    /// θ_e affected only the bell's physical length, not C_F.
    /// </para>
    /// <para>
    /// Post-PH-19, divergence is computed per-design from
    /// <see cref="Chamber.RaoBellTable.DivergenceLossFactor"/> and applied
    /// alongside this knob:
    /// <c>C_F = C_F_ideal · λ_div(ε, L%) · NozzleCfEfficiency</c>. This
    /// knob now strictly represents the BL+2Φ component. The divergence
    /// term is identically 1.0 on aerospike topologies (the plug exits
    /// near-axially at the design point).
    /// </para>
    /// <para>
    /// Migration: existing 0.94 callers will see C_F drop ~0.5–1.5 %
    /// versus pre-PH-19 (typical bell λ_div ∈ [0.985, 0.995]). Bench-sa
    /// fingerprints shift accordingly.
    /// </para>
    /// </summary>
    public double NozzleCfEfficiency { get; init; } = 0.94;
    public double AmbientPressure_Pa { get; init; } = 101_325.0;  // sea-level

    /// <summary>
    /// Propellant combination. MR, C*, γ, etc. look up from the pair-specific
    /// CEA table in Combustion/. Default LOX/CH₄ for backward compatibility.
    /// </summary>
    public PropellantPair PropellantPair { get; init; } = PropellantPair.LOX_CH4;

    /// <summary>
    /// Calibration factor applied multiplicatively to the Bartz
    /// gas-side HTC. 1.0 = literature
    /// Bartz (default). Set by the measured-data overlay after a cold-flow
    /// or hot-fire run to nudge the predicted wall T / ΔT toward observed
    /// values. Carried on OperatingConditions rather than RegenChamberDesign
    /// because it is a hardware-specific fit, not a geometry variable.
    /// </summary>
    public double BartzScalingFactor { get; init; } = 1.0;

    /// <summary>
    /// Calibration factor applied multiplicatively to the coolant-side
    /// heat-transfer coefficient (Nusselt-based HTC and TPMS path).
    /// 1.0 = literature correlations. Set by multi-knob MAP calibration
    /// to adjust predicted CoolantDT_K toward observed values.
    /// </summary>
    public double CoolantHtcScalingFactor { get; init; } = 1.0;

    /// <summary>
    /// Calibration factor applied multiplicatively to the Darcy-Weisbach
    /// friction factor in coolant correlations. 1.0 = literature friction.
    /// Set by multi-knob MAP calibration to adjust predicted CoolantDP_Pa
    /// toward observed values.
    /// </summary>
    public double CoolantFrictionScalingFactor { get; init; } = 1.0;

    /// <summary>
    /// Distance (mm) from the TVC gimbal attach point to the throat section,
    /// used for the combined axial-bending structural check (Hibbeler §8.4).
    /// Default 0.0 = no gimballing — the <c>COMBINED_AXIAL_BENDING_INSUFFICIENT</c>
    /// gate self-suppresses and all <see cref="Voxelforge.Structure.StructuralSummary"/>
    /// outputs are bit-identical to pre-Sprint-C behaviour.
    /// Schema v27+.
    /// </summary>
    public double GimbalOffset_mm { get; init; } = 0.0;

    /// <summary>
    /// OOB-9 (issue #344): opt-in finite-rate (dissociation) Isp correction.
    /// When true, <c>GenerateWith</c> multiplies the equilibrium vacuum Isp
    /// by <see cref="Combustion.FiniteRateCorrection.DissociationCorrectionFactor"/>
    /// before the thermal solve. Default false → bit-identical legacy behaviour.
    /// Schema v30+.
    /// </summary>
    public bool UseFiniteRateCorrection { get; init; } = false;

    // ─── Feed-system ΔP stackup ─────────────────────────────────
    // Opt-in: when TankUllagePressure_Pa == 0 (default) the stackup is
    // SKIPPED and no result is attached to RegenGenerationResult, so
    // legacy saved designs round-trip unchanged.

    /// <summary>
    /// Tank ullage pressure (Pa) at the start of the feed-system stackup.
    /// 0 (default) disables the stackup and no feasibility gate fires.
    /// Typical pressure-fed hardware: 1.3–1.6 × chamber pressure.
    /// </summary>
    public double TankUllagePressure_Pa { get; init; } = 0.0;

    /// <summary>
    /// Final tank ullage pressure (Pa) at end-of-burn for a BLOW-DOWN
    /// pressure-fed system. 0 (default) =
    /// regulated mode — tank pressure is held constant throughout the
    /// burn by a high-pressure gas supply + regulator; the stackup
    /// runs at a single point (<see cref="TankUllagePressure_Pa"/>).
    /// <para>
    /// Non-zero = blow-down mode: the tank is pre-pressurized to
    /// <see cref="TankUllagePressure_Pa"/> and decays to this value as
    /// propellant drains and the ullage gas expands. The feed-system
    /// stackup is computed at BOTH endpoints and the
    /// <c>BLOW_DOWN_INSUFFICIENT</c> gate fires when the end-of-burn
    /// predicted chamber pressure falls below the target.
    /// </para>
    /// <para>
    /// The user is responsible for picking this value consistent with
    /// the tank geometry: for a blow-down with initial ullage volume
    /// fraction <c>f_u0</c> drained to fraction 1.0 at end-of-burn,
    /// isothermal expansion gives P_end ≈ P_start · f_u0; adiabatic
    /// N2 expansion gives P_end ≈ P_start · f_u0^1.4. Typical sport-
    /// class blow-down hardware uses f_u0 ≈ 0.20-0.30 → P_end / P_start
    /// in the 0.12-0.30 band.
    /// </para>
    /// </summary>
    public double BlowDownFinalPressure_Pa { get; init; } = 0.0;

    /// <summary>Straight-pipe feed-line length (m) used by Darcy-Weisbach.</summary>
    public double FeedLineLength_m { get; init; } = 1.5;

    /// <summary>Feed-line inner diameter (mm).</summary>
    public double FeedLineDiameter_mm { get; init; } = 8.0;

    /// <summary>Main-valve flow coefficient Cv (US gpm / √psi).</summary>
    public double MainValveCv { get; init; } = 2.0;

    /// <summary>
    /// Filter pressure drop (Pa) at rated flow. Used as the "clean"
    /// reference value when <see cref="FilterStandard"/> is set to
    /// <see cref="FeedSystem.FilterStandard.Custom"/>; otherwise the
    /// preset's tabulated clean ΔP supersedes this scalar. Default
    /// 100 kPa preserves backward compatibility with legacy saved
    /// designs that pre-date the preset library.
    /// </summary>
    public double FilterDeltaP_Pa { get; init; } = 100_000.0;

    /// <summary>
    /// Inline propellant filter preset. Defaults to
    /// <see cref="FeedSystem.FilterStandard.Custom"/> so the legacy
    /// <see cref="FilterDeltaP_Pa"/> scalar still drives the stackup
    /// for legacy saved designs. Switching to a named preset replaces
    /// the scalar with the preset's tabulated clean ΔP and exposes
    /// the dirty multiplier through
    /// <see cref="FilterContaminationFraction"/>.
    /// </summary>
    public FeedSystem.FilterStandard FilterStandard { get; init; }
        = FeedSystem.FilterStandard.Custom;

    /// <summary>
    /// Filter loading state on a 0-1 scale. 0 = clean / fresh element;
    /// 1 = end-of-life. Linearly interpolates between clean ΔP and
    /// clean × dirty-multiplier.
    /// </summary>
    public double FilterContaminationFraction { get; init; } = 0.0;

    // ─── Pre-fire chilldown transient ──────────────────────────
    // Opt-in via IncludeChilldownTransient. When false the lumped-
    // jacket integrator is not run and no result is attached, so
    // legacy saved designs round-trip unchanged.

    /// <summary>
    /// Enable the pre-fire chilldown transient analysis. Skipped on
    /// non-cryogenic propellant pairs (RP-1) regardless of this flag.
    /// </summary>
    public bool IncludeChilldownTransient { get; init; } = false;

    /// <summary>
    /// Initial regen-jacket wall temperature (K) at the start of the
    /// chilldown integral. 298 K = sea-level ambient.
    /// </summary>
    public double ChilldownInitialJacketTemp_K { get; init; } = 298.0;

    /// <summary>
    /// Effective two-phase heat-transfer coefficient (W/m²·K) used by
    /// the chilldown lumped model. Default 5000 sits in the Chen /
    /// Shah transition-boiling envelope for LCH4 / LH2 against warm
    /// metal walls.
    /// </summary>
    public double ChilldownTwoPhaseHTC_Wm2K { get; init; } = 5000.0;

    /// <summary>
    /// "Done" threshold (K) — chilldown is declared complete when the
    /// jacket-wall temperature is within this many degrees of the
    /// coolant saturation temperature. Default 50 K.
    /// </summary>
    public double ChilldownDoneDeltaT_K { get; init; } = 50.0;

    /// <summary>
    /// Maximum acceptable chilldown time (s). The chilldown gate
    /// fires soft when the integrated time exceeds this budget.
    /// </summary>
    public double ChilldownMaxTime_s { get; init; } = 60.0;

    // ─── Start-transient simulator ─────────────────────────────
    // Opt-in. When enabled, the start-transient lumped 0-D simulator
    // runs after the steady-state generation pass and attaches
    // <c>RegenGenerationResult.StartTransient</c>. The HARD_START_RISK
    // gate fires soft when the predicted Pc overshoot crosses the
    // <see cref="StartHardStartFactor"/> threshold.

    /// <summary>Enable the start-transient simulator.</summary>
    public bool IncludeStartTransient { get; init; } = false;

    /// <summary>Linear valve open ramp duration (s). Typical 0.05–0.20 s.</summary>
    public double StartValveOpenTime_s { get; init; } = 0.10;

    /// <summary>
    /// Igniter delay (s) measured from valve-open command. The
    /// simulator pools all propellant injected before this time and
    /// folds it into the hard-start spike estimate.
    /// </summary>
    public double StartIgniterDelay_s { get; init; } = 0.05;

    /// <summary>Total simulation duration (s).</summary>
    public double StartSimulationDuration_s { get; init; } = 1.0;

    /// <summary>Time step (s) for the explicit Euler integrator.</summary>
    public double StartSimulationTimeStep_s { get; init; } = 0.001;

    /// <summary>
    /// Hard-start risk threshold — predicted Pc overshoot above which
    /// the simulator flags HARD_START_RISK. 0.5 (50 %) per Sutton §10.6.
    /// </summary>
    public double StartHardStartFactor { get; init; }
        = Combustion.StartTransientSim.DefaultHardStartFactor;

    // Independent ox / fuel valve ramps for the start-transient
    // simulator. 0 (default) ⇒ that side uses the shared
    // `StartValveOpenTime_s`. Non-zero overrides enable staged
    // starts (e.g. fuel-lead = open fuel valve early so fuel dome
    // fills before ox starts injecting — classic hard-start
    // mitigation).

    /// <summary>Ox valve open ramp (s). 0 = use the shared <see cref="StartValveOpenTime_s"/>.</summary>
    public double OxStartValveOpenTime_s { get; init; } = 0.0;

    /// <summary>Fuel valve open ramp (s). 0 = use the shared <see cref="StartValveOpenTime_s"/>.</summary>
    public double FuelStartValveOpenTime_s { get; init; } = 0.0;

    // ─── Turbopump sizing stub ─────────────────────────────────
    // PressureFed (default) preserves the legacy baseline behaviour;
    // GasGenerator / ElectricPump / OpenExpander run the per-pump
    // sizing math and surface NPSHA/NPSHR + shaft power.

    /// <summary>
    /// Engine cycle. PressureFed (default) skips turbopump sizing
    /// entirely; the other three values run
    /// <see cref="FeedSystem.TurbopumpSizing.Size"/> and attach the
    /// result to <c>RegenGenerationResult.Turbopump</c>.
    /// </summary>
    public FeedSystem.EngineCycle EngineCycle { get; init; }
        = FeedSystem.EngineCycle.PressureFed;

    /// <summary>
    /// Pump-suction pressure (Pa) on the propellant lines feeding the
    /// turbopump. 0 (default) auto-resolves: if
    /// <see cref="TankUllagePressure_Pa"/> is non-zero, that value is
    /// used; otherwise 0.3 MPa (typical cryogenic NPSH-margin ullage).
    /// </summary>
    public double PumpInletPressure_Pa { get; init; } = 0.0;

    /// <summary>
    /// Pump discharge pressure (Pa). 0 (default) auto-sizes to
    /// ChamberPressure_Pa × 1.5.
    /// </summary>
    public double PumpDischargePressure_Pa { get; init; } = 0.0;

    /// <summary>Centrifugal-pump efficiency assumption.</summary>
    public double PumpEfficiency { get; init; }
        = FeedSystem.TurbopumpSizing.DefaultPumpEfficiency;

    /// <summary>
    /// Preburner mixture ratio for staged-combustion / gas-generator
    /// / FFSC cycles. 0 (default) triggers
    /// <see cref="Chamber.PreburnerChamber.SuggestPreburnerMr"/> which
    /// picks a propellant-appropriate fuel-rich value
    /// (LOX/CH4 → 0.60, LOX/H2 → 0.80, LOX/RP-1 → 0.40). Non-zero
    /// value wins. Only consumed when <see cref="EngineCycle"/> is
    /// GasGenerator / StagedCombustion / FullFlow.
    /// </summary>
    public double PreburnerMrRatio { get; init; } = 0.0;

    /// <summary>
    /// Preburner chamber pressure (Pa). 0 (default) auto-sizes to
    /// 1.5 × <see cref="ChamberPressure_Pa"/>
    /// for staged-combustion (preburner Pc > main Pc to drive the
    /// pressure cascade) and 1.2 × for gas-generator (preburner
    /// operates standalone at slightly elevated Pc). Non-zero value
    /// wins. Only consumed when <see cref="EngineCycle"/> is a
    /// preburner cycle.
    /// </summary>
    public double PreburnerChamberPressure_Pa { get; init; } = 0.0;

    /// <summary>
    /// Generate parametric turbopump voxel geometry (inducer +
    /// impeller + volute + casing) via
    /// <see cref="Turbopump.TurbopumpGeometryGenerator.Generate"/>
    /// and attach it to
    /// <see cref="FeedSystem.TurbopumpResult.FuelPumpGeometry"/> /
    /// <see cref="FeedSystem.TurbopumpResult.OxPumpGeometry"/>. Only
    /// meaningful when <see cref="EngineCycle"/> is not PressureFed.
    /// Default <c>false</c> because the geometry is only needed for
    /// monolithic-engine composition and analytical-only SA candidate
    /// evaluation doesn't need it.
    /// </summary>
    public bool IncludeTurbopumpGeometry { get; init; } = false;

    /// <summary>
    /// Ground-side umbilical / quick-disconnect standard. Only
    /// consumed by the feed-system pressure stackup; voxel wiring is
    /// a follow-on. None = skipped in the stackup.
    /// </summary>
    public Geometry.UmbilicalStandard UmbilicalStandard { get; init; }
        = Geometry.UmbilicalStandard.None;

    // ── Electric-pump battery energy budget (PH-47 / issue #192) ─────────

    /// <summary>
    /// Design burn time (s) for the electric-pump cycle battery energy
    /// budget. Default <c>0</c> is a sentinel meaning "do not add battery
    /// mass" — preserves bit-identical behaviour for non-electric cycles
    /// and legacy electric-pump designs that did not set this field.
    /// <para>
    /// Battery energy = <c>TotalShaftPower_W × BurnTime_s</c> (joules).
    /// Battery mass   = <c>energy_MJ × BatteryEnergyDensity_kg_per_MJ</c>.
    /// Only consumed when <see cref="EngineCycle"/> == ElectricPump AND
    /// BurnTime_s &gt; 0. Schema v20+ (issue #192).
    /// </para>
    /// </summary>
    public double BurnTime_s { get; init; } = 0.0;

    /// <summary>
    /// Battery system specific mass (kg per MJ of shaft energy delivered).
    /// Includes cell + BMS + packaging + wiring overhead.
    /// Default <c>1.0</c> ≈ 278 Wh/kg — representative packaged Li-Po
    /// system (Rocket Lab Rutherford: ~167 Wh/kg cell → ~1.67 kg/MJ with
    /// packaging; 1.0 kg/MJ is optimistic but within the next-generation
    /// Li-ion roadmap). Only consumed when
    /// <see cref="EngineCycle"/> == ElectricPump AND
    /// <see cref="BurnTime_s"/> &gt; 0. Schema v20+ (issue #192).
    /// </summary>
    public double BatteryEnergyDensity_kg_per_MJ { get; init; } = 1.0;
}

/// <summary>
/// All tunable chamber geometry parameters. Optimizer edits this struct.
/// </summary>
public sealed record RegenChamberDesign : IEngineDesign
{
    /// <inheritdoc />
    public string Family => Engines.EngineFamilies.Rocket;

    // Overall nozzle proportions. SA dims 0–5. Bounds track
    // RegenChamberOptimization.Bounds; DesignVariableRegistry asserts
    // the two agree at test time (Sprint 5 Dev A, ADR-010).
    [SaDesignVariable(index: 0,  min: 3.0,  max: 10.0)]
    public double ContractionRatio { get; init; } = 6.0;
    [SaDesignVariable(index: 1,  min: 3.0,  max: 25.0)]
    public double ExpansionRatio { get; init; } = 8.0;
    [SaDesignVariable(index: 2,  min: 0.7,  max: 1.6)]
    public double CharacteristicLength_m { get; init; } = 1.1;
    [SaDesignVariable(index: 3,  min: 20.0, max: 38.0)]
    public double BellEntranceAngle_deg { get; init; } = 30.0;
    [SaDesignVariable(index: 4,  min: 6.0,  max: 16.0)]
    public double BellExitAngle_deg { get; init; } = 10.0;
    [SaDesignVariable(index: 5,  min: 0.6,  max: 0.9)]
    public double BellLengthFraction { get; init; } = 0.8;

    // ── Dual-bell (altitude-compensating) nozzle ──
    // Opt-in fields for a two-segment bell with a wall-angle discontinuity
    // at an intermediate expansion ratio. First (sea-level) bell is short
    // and optimised for full flow at SL ambient; second (altitude) bell
    // continues to the full ExpansionRatio. At low altitude the boundary
    // layer separates at the inflection, so the nozzle flows as if it
    // ended at SeaLevelExpansionRatio — higher ambient C_F than the full
    // second bell would deliver. At altitude the flow fills the second
    // bell, delivering the higher vacuum C_F of the full expansion.
    //
    // Reuses 100% of the single-bell infrastructure: the existing bell-arc
    // sits upstream of both parabolas; BellEntranceAngle_deg (θ_n) defines
    // both the arc→first-bell transition AND the first-bell→second-bell
    // re-entrance (the designed discontinuity); BellExitAngle_deg (θ_e)
    // defines the slope at the full exit. Only the INFLECTION angle is
    // new.
    //
    // Not SA-tunable today — promote later if a full dual-bell optimisation
    // study lands. Defaults preserve single-bell behaviour bit-identically
    // (IncludeDualBell = false ⇒ ChamberContourGenerator.Generate skips
    // the dual-bell path entirely).

    /// <summary>
    /// Enable altitude-compensating dual-bell nozzle geometry.
    /// Default <c>false</c> preserves legacy single-bell behaviour
    /// bit-identically. When <c>true</c>,
    /// <see cref="SeaLevelExpansionRatio"/>
    /// must be in (1.0, <see cref="ExpansionRatio"/>) and the contour
    /// generator splits the bell into two parabolic sections joined at
    /// the inflection; <see cref="Chamber.ChamberContour.InflectionIndex"/>
    /// surfaces the inflection station so downstream voxel/channel/thermal
    /// solvers can handle the slope discontinuity cleanly.
    /// </summary>
    public bool IncludeDualBell { get; init; } = false;

    /// <summary>
    /// Intermediate expansion ratio at the dual-bell inflection.
    /// Must be strictly between 1.0 and <see cref="ExpansionRatio"/>.
    /// Typical 4–12 for sea-level-optimised first bell paired with a
    /// vacuum ExpansionRatio of 20–60. Ignored unless
    /// <see cref="IncludeDualBell"/> is <c>true</c>.
    /// </summary>
    public double SeaLevelExpansionRatio { get; init; } = 0.0;

    /// <summary>
    /// Sprint 20: wall-slope angle (degrees, from axial) at the inflection
    /// on the first-bell side. Typical 3–10°; sets how aggressively the
    /// first bell turns back toward the axis before the second-bell
    /// entrance re-introduces <see cref="BellEntranceAngle_deg"/>'s steeper
    /// turn. The difference between this and θ_n is the designed angle
    /// discontinuity that provokes boundary-layer separation at the
    /// inflection during sea-level operation. Ignored unless
    /// <see cref="IncludeDualBell"/> is <c>true</c>.
    /// </summary>
    public double InflectionAngle_deg { get; init; } = 7.0;

    // Cooling channels. SA dims 6–12.
    [SaDesignVariable(index: 6,  min: 40.0, max: 120.0)]
    public int ChannelCount { get; init; } = 80;
    [SaDesignVariable(index: 7,  min: 1.0,  max: 5.0)]
    public double ChannelHeightChamber_mm { get; init; } = 2.5;
    [SaDesignVariable(index: 8,  min: 0.8,  max: 3.0)]
    public double ChannelHeightThroat_mm { get; init; } = 1.5;
    [SaDesignVariable(index: 9,  min: 1.0,  max: 5.0)]
    public double ChannelHeightExit_mm { get; init; } = 2.0;
    [SaDesignVariable(index: 10, min: 0.5,  max: 2.0)]
    public double RibThickness_mm { get; init; } = 0.8;
    // **Sprint feasibility-audit-A (2026-04-26 evening):** SA upper bound
    // bumped 2.0 → 4.0 mm to give SA room to satisfy YIELD_EXCEEDED on
    // high-Pc / high-thrust LRE designs. Real production engines run
    // wall thicknesses up to 4-5 mm in high-stress regions (Merlin MCC
    // ~3 mm, F-1 MCC ~4 mm); the prior 2 mm cap left aerospike + pintle
    // sf at 1.28-1.48 (just barely feasible) under the post-PR-#82
    // hoop-stress fix, but coupled-gate explorations couldn't reach
    // those wall thicknesses without breaking other constraints.
    //
    // **Sprint feasibility-audit-Y (2026-04-27):** GasSideWallThickness_mm
    // upper bound bumped 4.0 → 5.0 mm. PR #88 (Sprint F) bumped expander-
    // cycle coolant-inlet pressures to 14-16 MPa, which inflates hoop
    // stress (single-wall formula uses gas-side wall only). RL10 at SA-max
    // 4 mm post-Sprint-F has SF ≈ 0.76; bumping ceiling to 5 mm gives ~25 %
    // more hoop margin without breaking other constraints (the lower bound
    // stays at 0.5 mm so thin-wall thermally-tuned designs are still in
    // SA's reach). F-1 MCC's 4 mm liner is the high-end real reference;
    // 5 mm is conservative one-notch beyond.
    [SaDesignVariable(index: 11, min: 0.5,  max: 5.0)]
    public double GasSideWallThickness_mm { get; init; } = 0.8;
    // **Sprint feasibility-audit-X (2026-04-27):** OuterJacketThickness_mm
    // upper bound bumped 4.0 → 6.0 mm. Same Sprint F driver — when the
    // multi-wall hoop-stress credit (Sprint G' from the v5 handoff) lands,
    // SA needs jacket-thickness headroom to find feasibility on RL10 +
    // closed-expander-class designs. Real RL10's IN625 jacket sits at
    // ~3 mm; SSME's at ~4 mm; F-1's at ~6 mm. Going to 6 mm covers the
    // F-1-class envelope. Lower bound stays at 1.0 mm.
    [SaDesignVariable(index: 12, min: 1.0,  max: 6.0)]
    public double OuterJacketThickness_mm { get; init; } = 2.0;

    /// <summary>
    /// Fillet radius (mm) at the axial ends of each cooling channel where
    /// they meet the manifold plenums. Rounds off the 90° rib-termination
    /// corner → lower stress concentration, fewer LPBF recoater catches.
    /// 0.0 reproduces pre-upgrade sharp ends (unit tests rely on this).
    /// </summary>
    public double ChannelManifoldFilletRadius_mm { get; init; } = 0.8;

    /// <summary>
    /// PHASE 4 (2026-04-20): cooling-channel topology.
    /// Axial = straight channels along the chamber axis (legacy, default).
    /// Helical = channels spiral around the inner wall at <see cref="HelixPitchAngle_deg"/>.
    /// Helical runs more coolant-wetted length per unit axial distance
    /// (L_eff = L_axial / cos α), raising heat uptake and ΔP proportionally.
    /// </summary>
    public ChannelTopology ChannelTopology { get; init; } = ChannelTopology.Axial;

    /// <summary>
    /// Pitch angle for helical channels, in degrees from the chamber axis.
    /// Ignored when <see cref="ChannelTopology"/> is Axial. Typical 10–25°.
    /// Larger angles give more ΔT + more ΔP; small angles are nearly axial.
    /// </summary>
    public double HelixPitchAngle_deg { get; init; } = 15.0;

    /// <summary>
    /// Sprint 34b / PH-8 (2026-04-25): user-overrideable shaft RPM for
    /// the turbopump. Default 0 = auto-derive from a fixed N_s = 2500
    /// (pre-Sprint-34b behaviour). When &gt; 0, treats RPM as the
    /// mechanical constraint (bearings / shaft strength / seal limits)
    /// and lets specific speed N_s fall out of the design — matching
    /// real LRE workflow. Values that drift N_s outside the [600, 9000]
    /// band fire <c>PUMP_SPECIFIC_SPEED_OFF_BAND</c>. Karassik §2.5
    /// suggests 1500-12000 rpm typical for LRE-class centrifugal pumps;
    /// values outside that envelope are unusual and the gate-band check
    /// will catch the worst pathologies.
    /// </summary>
    public double PumpRpm_rpm { get; init; } = 0.0;

    /// <summary>
    /// Sprint 34 / PH-10 (2026-04-25): turbopump shaft mounting layout.
    /// Selects the boundary condition on
    /// <see cref="FeedSystem.ShaftCriticalSpeed.Estimate"/>: Straddled
    /// (fixed-fixed, β₁L = 4.73 — pump and turbine sit between a pair
    /// of outboard bearings) vs Overhung (fixed-free / cantilever,
    /// β₁L = 1.875 — pump or turbine hangs off one end past the
    /// outermost bearing). Critical speed scales as β₁L² → ω_n drops
    /// by ≈ 6× under Overhung. Default Straddled preserves the
    /// pre-Sprint-34 SHAFT_WHIRL behaviour. Set Overhung for small
    /// Rutherford-class turbopumps (typical thrust &lt; 10 kN);
    /// Karassik §2.3 has the geometric tell-tale.
    /// </summary>
    public FeedSystem.ShaftLayout ShaftLayout { get; init; }
        = FeedSystem.ShaftLayout.Straddled;

    /// <summary>
    /// Sprint 33 / PH-7 (2026-04-24): coolant-side relative roughness ε/D_h
    /// used by the Haaland friction-factor correlation in
    /// <see cref="HeatTransfer.CoolantCorrelations"/>. LPBF-printed channels
    /// run at ε/D ≈ 0.01-0.05 (Strauss et al. 2018); pre-Sprint-33 the
    /// solver used smooth-tube Petukhov which under-predicted ΔP by 2-4×
    /// and silently passed designs needing impossible tank pressure at
    /// the FEED_PRESSURE_INSUFFICIENT gate. Default 0.02 is the centre
    /// of the LPBF band; bump toward 0.05 for as-built (no surface
    /// finish), drop toward 0.01 for chemically-polished or AFM-finished
    /// channels. 0 reverts to smooth-tube Petukhov.
    /// </summary>
    public double LpbfRelativeRoughness { get; init; } = 0.02;

    /// <summary>
    /// Unit-cell edge length (mm) for TPMS cooling. Ignored when
    /// <see cref="ChannelTopology"/> is not a TPMS family. Typical
    /// 2–6 mm; smaller cells increase surface-area density (higher
    /// Nu, higher ΔP) but push strut thickness toward the LPBF
    /// feature floor. The feasibility gate fires when strut thickness
    /// (= SolidFraction × CellEdge) falls below
    /// <see cref="HeatTransfer.TpmsCorrelations.MinStrutThickness_mm"/>.
    /// </summary>
    [SaDesignVariable(index: 18, min: 2.0,  max: 6.0,  gate: SaGate.TpmsTopology)]
    public double TpmsCellEdge_mm { get; init; } = 3.0;

    /// <summary>
    /// TPMS solid volume fraction (1 − porosity). 0.50 (default) is
    /// the literature-calibration centre; valid envelope is [0.30,
    /// 0.70]. Higher solid fraction = thicker struts + lower porosity
    /// = worse heat transfer but cheaper ΔP.
    /// </summary>
    [SaDesignVariable(index: 19, min: 0.35, max: 0.65, gate: SaGate.TpmsTopology)]
    public double TpmsSolidFraction { get; init; } = 0.50;

    /// <summary>
    /// Aerospike-plug truncation ratio. Full spike = 1.0; typical
    /// truncations 0.20–0.40 trade
    /// ~1 % C_F at vacuum for ~60 % hardware-length reduction +
    /// dramatically improved manufacturability + provide a flat plug
    /// base to mount test-stand instrumentation on. Clamped to
    /// [<see cref="Chamber.AerospikeContourGenerator.MinPlugLengthRatio"/>,
    /// <see cref="Chamber.AerospikeContourGenerator.MaxPlugLengthRatio"/>]
    /// at generation time. Ignored when <see cref="ChannelTopology"/>
    /// is not <see cref="ChannelTopology.Aerospike"/>.
    /// </summary>
    [SaDesignVariable(index: 22, min: 0.15, max: 1.00, gate: SaGate.AerospikeTopology)]
    public double PlugLengthRatio { get; init; } = 0.30;

    /// <summary>
    /// Aerospike pre-throat chamber contraction ratio
    /// (A_chamber / A_throat). Surfaces as an SA-tunable design
    /// variable gated on <see cref="SaGate.AerospikeTopology"/>.
    /// Default 6.0 preserves legacy aerospike sizing bit-identically
    /// — only explicit overrides (user-set or SA-sampled) change
    /// chamber radius. Separate from the regen
    /// <see cref="ContractionRatio"/>
    /// (dim 0) because aerospike chamber geometry differs; keeping both
    /// independent lets SA tune each separately on mixed-topology
    /// Pareto fronts. Bounds [3.0, 10.0] match the regen envelope;
    /// smaller values give a more compact chamber (higher injector
    /// mass flux); larger values give more residence time at the cost
    /// of weight.
    /// </summary>
    [SaDesignVariable(index: 23, min: 3.0, max: 10.0, gate: SaGate.AerospikeTopology)]
    public double AerospikeContractionRatio { get; init; } = 6.0;

    // ── Sprint 9 Track B (2026-04-22) — preburner regen cooling ──
    // Opt-in fields for high-Pc preburner designs (staged-combustion /
    // full-flow / gas-generator cycles) where the warm-gas temperature
    // brushes the wall material service limit. None of these are
    // SA-tunable today — they're user-set via the UI or the CLI; a
    // future sprint can promote channel count / dimensions to the SA
    // vector if an end-to-end preburner-cooling optimisation study is
    // on the roadmap.

    /// <summary>
    /// Sprint 9 Track B: enable the preburner regen-cooling solver.
    /// Default false preserves pre-Sprint-9 adiabatic-wall behaviour
    /// (PreburnerResult.Thermal stays null; PREBURNER_WALL_TEMP gate
    /// skipped). When true AND <see cref="OperatingConditions.EngineCycle"/>
    /// is a preburner cycle (GasGenerator / StagedCombustion /
    /// FullFlow), <see cref="HeatTransfer.PreburnerCooling.Solve"/>
    /// runs inside <see cref="RegenChamberOptimization.GenerateWith"/>
    /// and populates the thermal field.
    /// </summary>
    public bool IncludePreburnerRegenCooling { get; init; } = false;

    /// <summary>Number of axial regen channels in the preburner wall (typ. 16–40).</summary>
    public int PreburnerChannelCount { get; init; } = 24;

    /// <summary>Preburner channel width (mm) measured on the chamber circumference.</summary>
    public double PreburnerChannelWidth_mm { get; init; } = 2.5;

    /// <summary>Preburner channel depth (mm) measured radially from the hot-wall surface.</summary>
    public double PreburnerChannelDepth_mm { get; init; } = 2.0;

    /// <summary>Preburner hot-wall thickness (mm) between combustion gas and coolant channel.</summary>
    public double PreburnerWallThickness_mm { get; init; } = 0.8;

    // ── Sprint 15 / Track G (2026-04-22) — aerospike plug-channel regen cooling ──
    // Mirrors the Sprint 9 preburner-cooling opt-in pattern. Closes the
    // feature loop opened by Sprint 11 Track F: SA scoring already reads
    // from gen.Aerospike.Thermal when populated, but until this field set
    // existed the UI/SA path couldn't request the thermal calculation in
    // the first place. AerospikeOptimization.ToSpec now forwards these
    // four values (plus IncludeAerospikeRegenCooling) into AerospikeSpec
    // so AerospikeBuilder.BuildPhysicsOnly invokes the AerospikePlugCooling
    // solver and the AEROSPIKE_PLUG_WALL_TEMP gate has data to fire on.
    // None are SA-tunable today; promote later if a per-channel sweep is
    // worth optimising end-to-end.

    /// <summary>
    /// Sprint 15: enable plug-interior regen-cooling channels on aerospike
    /// designs. Default false preserves the pre-Sprint-15 geometry-only
    /// path (AerospikeBuildResult.Thermal stays null; the
    /// AEROSPIKE_PLUG_WALL_TEMP gate is silently skipped). When true AND
    /// <see cref="ChannelTopology"/> is Aerospike, the four plug-channel
    /// fields below are forwarded into <see cref="Geometry.AerospikeSpec.IncludeRegenChannels"/>
    /// + the channel geometry, and the aerospike thermal solver runs.
    /// Silently inert on non-aerospike topologies (the field is still
    /// packed/unpacked but never consumed).
    /// </summary>
    public bool IncludeAerospikeRegenCooling { get; init; } = false;

    /// <summary>Number of axial regen channels around the aerospike plug (typ. 16–40).</summary>
    public int AerospikePlugChannelCount { get; init; } = 24;

    /// <summary>Aerospike plug channel width (mm) measured on the plug circumference.</summary>
    public double AerospikePlugChannelWidth_mm { get; init; } = 2.5;

    /// <summary>Aerospike plug channel depth (mm) measured radially from the plug surface inward.</summary>
    public double AerospikePlugChannelDepth_mm { get; init; } = 2.0;

    /// <summary>Aerospike plug wall thickness (mm) between combustion gas and channel.</summary>
    public double AerospikePlugWallThickness_mm { get; init; } = 0.8;

    // ── Sprint 26 (2026-04-23) — linear (extruded) aerospike ─────────
    // Two opt-in geometry knobs for the Sprint 26 linear-aerospike
    // topology. Consumed only when ChannelTopology = LinearAerospike;
    // silently carried (packed + unpacked, never read) on every other
    // topology per the §7 categorical-silent-revert convention.

    /// <summary>
    /// Sprint 26: transverse extrusion width (mm) of the linear plug.
    /// The plug cross-section is <c>(2 · h_throat) × LinearAerospikePlugWidth_mm</c>
    /// at every axial station, where h_throat is derived from the
    /// thrust / Pc / expansion ratio. Default 60 mm matches the
    /// X-33 XRS-2200 thrust-cell transverse scale. Consumed by
    /// <see cref="AerospikeOptimization.ToSpec"/> only when
    /// <see cref="ChannelTopology"/> is
    /// <see cref="ChannelTopology.LinearAerospike"/>.
    /// </summary>
    public double LinearAerospikePlugWidth_mm { get; init; } = 60.0;

    /// <summary>
    /// Sprint 26: design-visible aspect-ratio target for the linear
    /// plug (plug length / plug width). Informational only — the
    /// contour generator computes the actual aspect ratio from the
    /// derived <c>PlugTruncatedLength_mm / LinearAerospikePlugWidth_mm</c>,
    /// and the <c>LINEAR_AEROSPIKE_ASPECT_RATIO</c> feasibility gate
    /// fires on the derived value. A non-default value here can be
    /// surfaced in the UI to hint at the design intent without
    /// overriding the physics-derived result. Default 1.0 = square-ish
    /// footprint.
    /// </summary>
    public double LinearAerospikeAspectRatio { get; init; } = 1.0;

    /// <summary>
    /// SA-promoted preburner mixture ratio. 0 (default) = inherit from
    /// <see cref="OperatingConditions.PreburnerMrRatio"/>, which in
    /// turn falls back to
    /// <see cref="Chamber.PreburnerChamber.SuggestPreburnerMr"/>.
    /// Non-zero wins — this is the SA-tunable knob so preburner
    /// cycles can optimise the fuel-rich MR together with the main
    /// chamber variables. Silently ignored (still packed, still
    /// unpacked) when <see cref="OperatingConditions.EngineCycle"/>
    /// is not a preburner cycle, per the categorical-silent-revert
    /// convention.
    /// Gate.None: the 0 ⇒ default sentinel means "applied
    /// unconditionally" is safe — non-preburner cycles simply never
    /// read the field.
    /// </summary>
    [SaDesignVariable(index: 20, min: 0.30, max: 1.00)]
    public double PreburnerMrRatio { get; init; } = 0.0;

    /// <summary>
    /// Number of serial centrifugal stages on each propellant pump.
    /// Default 1 preserves the legacy single-stage behaviour
    /// bit-identically. Valid range [1, 4]:
    /// <see cref="FeedSystem.TurbopumpSizing.SizeOnePump"/> clamps
    /// outside values to that envelope. Multi-stage trades hardware
    /// complexity for lower per-stage head + lower RPM; it's the
    /// classical remediation when the single-stage RPM lands inside
    /// the shaft-whirl band (Gate 17, SHAFT_WHIRL). Only consumed
    /// when <see cref="OperatingConditions.EngineCycle"/> is not
    /// PressureFed; silently carried for pressure-fed designs.
    /// </summary>
    public int PumpStageCount { get; init; } = 1;

    /// <summary>
    /// SA-promoted pump-mount flange radial projection (mm). 0
    /// (default) = use
    /// <see cref="Turbopump.PumpMountFlange.DefaultRadialProjection_mm"/>
    /// (12 mm). Positive value overrides the default when
    /// <see cref="Geometry.MonolithicEngineBuilder"/> sizes the
    /// fuel/ox pump flanges. Inert (packed + unpacked, never read) for
    /// non-monolithic builds. Valid exploration band [8, 24] mm — below
    /// 8 mm the flange would ride the casing OD too tightly to clear
    /// bolt heads; above 24 mm the flange intersects the adjacent pump
    /// envelope on a standard 160 mm pump-centre-to-centre layout.
    /// </summary>
    [SaDesignVariable(index: 21, min: 8.0,  max: 24.0)]
    public double FlangeRadialProjection_mm { get; init; } = 0.0;

    /// <summary>
    /// Convenience projection of <see cref="ChannelTopology"/> to the
    /// intrinsic <see cref="HeatTransfer.TpmsKind"/> enum. Returns
    /// null for non-TPMS topologies so callers can branch cleanly.
    /// </summary>
    [JsonIgnore]
    public HeatTransfer.TpmsKind? TpmsKind => ChannelTopology switch
    {
        ChannelTopology.TpmsGyroid   => HeatTransfer.TpmsKind.Gyroid,
        ChannelTopology.TpmsSchwarzP => HeatTransfer.TpmsKind.SchwarzP,
        ChannelTopology.TpmsSchwarzD => HeatTransfer.TpmsKind.SchwarzD,
        _ => null,
    };

    /// <summary>
    /// PHASE 5 (2026-04-20): internal crossover passage from the coolant
    /// outlet manifold into the fuel injector plenum. Enables the "closed
    /// expander cycle" story — hot regen-heated fuel goes straight to the
    /// injector with no external plumbing. Cut as a short axial cylinder
    /// through the injector flange from the upstream manifold into the
    /// chamber-face zone. Thermally couples <see cref="HeatTransfer.InjectorFaceThermal"/>
    /// to the coolant outlet temperature (injector sees HOT fuel, not cold).
    /// </summary>
    public bool IncludeCoolantCrossover { get; init; } = false;

    /// <summary>
    /// Diameter (mm) of the coolant-crossover passage. Sized to keep
    /// velocity ≤ 20 m/s for the nominal fuel flow; default is generous.
    /// </summary>
    public double CoolantCrossoverDiameter_mm { get; init; } = 3.0;

    /// <summary>
    /// TIER A.4 (2026-04-21): instrumentation bosses drilled through the
    /// outer jacket for cold-flow / hot-fire test instrumentation.
    /// Each boss is a radial hole at (axial fraction, azimuth); preset
    /// selects bore and boss-OD from <see cref="Geometry.SensorBossPresets"/>.
    /// Empty list (default) = no instrumentation drilled.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<Geometry.SensorBoss> SensorBosses { get; init; }
        = Array.Empty<Geometry.SensorBoss>();

    /// <summary>
    /// Igniter type for the injector flange. None = no igniter cavity
    /// drilled (default). Selecting anything else cuts a cavity +
    /// optional feed bore through the flange and runs the
    /// ignition-energy gate.
    /// </summary>
    public Geometry.IgniterType IgniterType { get; init; } = Geometry.IgniterType.None;

    /// <summary>
    /// Radial offset of the igniter cavity from the chamber axis,
    /// expressed as a fraction of the chamber radius. 0 = on-axis
    /// (centre of the injector flange). 0.4–0.6 is typical for torch
    /// igniters that inject off-centre to avoid the main injector pattern.
    /// </summary>
    public double IgniterRadialFraction { get; init; } = 0.0;

    /// <summary>
    /// Whether the turbopump first-stage impeller is preceded by an
    /// inducer. Inducers boost suction-specific-speed S_s from the
    /// radial-pump baseline (~8 500 US) to ~20 000 US, which roughly
    /// halves NPSHR for a given (RPM, Q) combination. Sprint 30 (PH-2)
    /// — drives the Thoma-cavitation NPSHR computation in
    /// <see cref="FeedSystem.TurbopumpSizing"/>. Default false (no
    /// inducer); set true when the design explicitly carries one.
    /// </summary>
    public bool HasInducer { get; init; } = false;

    // ─── Inlet dome geometry ────────────────────────────────────
    // Opt-in via DomeDepth > 0. When 0 (default) the injector flange
    // stays solid and the feed stackup falls back to the
    // 1.5-velocity-head placeholder for the dome term.

    /// <summary>
    /// Fuel-side injector dome depth (mm), behind the injector face.
    /// 0 = no dome cavity cut; feed stackup uses placeholder.
    /// Typical 6–15 mm on small engines.
    /// </summary>
    public double FuelDomeDepth_mm { get; init; } = 0.0;

    /// <summary>
    /// Ox-side injector dome depth (mm). Same conventions as
    /// <see cref="FuelDomeDepth_mm"/>. Ox domes often match the fuel
    /// dome depth on symmetric engines.
    /// </summary>
    public double OxDomeDepth_mm { get; init; } = 0.0;

    /// <summary>Dome inlet-port diameter (mm), used by DomeHydraulics.</summary>
    public double DomeInletDiameter_mm { get; init; } = 8.0;

    /// <summary>
    /// Include a radial anti-vortex baffle at the dome apex. Costs
    /// ~0.3 velocity heads of ΔP but improves distribution uniformity
    /// and damps transient swirl during start.
    /// </summary>
    public bool IncludeAntiVortexBaffle { get; init; } = false;

    // ─── Gimbal mount ──────────────────────────────────────────

    /// <summary>
    /// Thrust-mount configuration. FixedFlange (default) preserves the
    /// legacy nozzle-exit flange behaviour. Other values route through
    /// <see cref="Structure.GimbalMount.Evaluate"/> for stiffness and
    /// bearing-stress checks.
    /// </summary>
    public Structure.MountConfiguration MountConfiguration { get; init; }
        = Structure.MountConfiguration.FixedFlange;

    /// <summary>
    /// Purge-port list. Each entry specifies a GN₂ / He / GOX
    /// inerting path, the requested mass flow, and the ground-side
    /// supply pressure. Empty (default) = no purges. Sized by
    /// <see cref="Coolant.PurgeFlowModel.Evaluate"/>; results surface
    /// via <c>RegenGenerationResult.PurgeResults</c>.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<Coolant.PurgePort> PurgePorts { get; init; }
        = System.Array.Empty<Coolant.PurgePort>();

    // ─── Ablative liner variant ──────────────────────────────
    // Opt-in via AblativeMaterial != None. The regen solver still
    // runs (so the user gets a comparison against a regen-cooled
    // baseline); the ablative analysis is layered on top using the
    // station-by-station heat-flux profile.

    /// <summary>
    /// Ablative wall-liner material. None (default) skips the
    /// recession analysis and leaves
    /// <c>RegenGenerationResult.Ablative</c> null. Other values run
    /// <see cref="Manufacturing.AblativeAnalysis.Run"/> against the
    /// regen-solver heat-flux profile and surface the result.
    /// </summary>
    public Manufacturing.AblativeMaterial AblativeMaterial { get; init; }
        = Manufacturing.AblativeMaterial.None;

    /// <summary>
    /// Initial ablative-liner thickness (mm) measured from the gas-side
    /// surface to the structural shell. Default 5 mm is typical for
    /// sounding-rocket / hybrid pressure-fed engines on a 30 s burn.
    /// </summary>
    public double AblativeThickness_mm { get; init; } = 5.0;

    /// <summary>
    /// Burn duration (s) used by the ablative recession integral. The
    /// constant-q assumption gets optimistic past the material's
    /// <see cref="Manufacturing.AblativeMaterialSpec.MaxBurnDuration_s"/>
    /// — a soft warning fires above that bound.
    /// </summary>
    public double AblativeBurnDuration_s { get; init; } = 30.0;

    /// <summary>
    /// Safety factor applied to (recession + char_depth) before
    /// comparing against initial thickness. Default 1.5 covers the
    /// recession-correlation scatter cited in Sutton 9e §16.3.
    /// </summary>
    public double AblativeSafetyFactor { get; init; }
        = Manufacturing.AblativeAnalysis.DefaultSafetyFactor;

    /// <summary>
    /// Fractional axial position (0 = chamber inlet, 1 = nozzle exit)
    /// where the ablative liner zone begins for
    /// <see cref="ChannelTopology.AblativeThroat"/>.
    /// The zone spans from <see cref="AblativeZoneStart_frac"/> to
    /// <see cref="AblativeZoneEnd_frac"/>; regen channels occupy the
    /// remainder. Default 0.30 places the zone start near the convergent
    /// section upstream of the throat.
    /// </summary>
    public double AblativeZoneStart_frac { get; init; } = 0.30;

    /// <summary>
    /// Fractional axial position (0 = chamber inlet, 1 = nozzle exit)
    /// where the ablative liner zone ends for
    /// <see cref="ChannelTopology.AblativeThroat"/>.
    /// Default 0.70 places the zone end in the early divergent section
    /// just past the throat, bracketing the peak-heat-flux region.
    /// </summary>
    public double AblativeZoneEnd_frac { get; init; } = 0.70;

    // ── LPBF printability analysis opt-in ──
    // Three opt-in fields drive the Geometry.LpbfAnalysis modules and
    // the OVERHANG_ANGLE_EXCEEDED / TRAPPED_POWDER_REGION /
    // DRAIN_PATH_MISSING feasibility gates. Defaults preserve legacy
    // behaviour bit-identically: IncludeLpbfPrintabilityAnalysis is
    // false, so the three gates are silent and no printability result
    // is attached to RegenGenerationResult. The analysis is voxel-free
    // on the fast SA path (synthesises surface samples from the
    // chamber contour) so it's xUnit-safe per ADR-005.

    /// <summary>
    /// Enable LPBF printability analysis (overhang / trapped-powder /
    /// drain-path). Default <c>false</c> preserves legacy behaviour.
    /// When <c>true</c>,
    /// <see cref="Geometry.LpbfAnalysis.LpbfPrintabilityAnalysis.ForChamber"/>
    /// runs inside <see cref="RegenChamberOptimization.GenerateWith"/>
    /// and populates <c>RegenGenerationResult.Printability</c>; the
    /// three associated feasibility gates consume that result.
    /// </summary>
    public bool IncludeLpbfPrintabilityAnalysis { get; init; } = false;

    /// <summary>
    /// LPBF alloy profile driving per-material overhang thresholds.
    /// Maps onto the <see cref="HeatTransfer.WallMaterial"/>
    /// index by default via <see cref="Geometry.LpbfAnalysis.LpbfMaterialProfiles.FromWallMaterialIndex"/>
    /// when the caller doesn't override it; the explicit field lets
    /// users print one wall alloy (e.g. CuCrZr for conductivity) but
    /// evaluate printability against a tighter profile (e.g. IN718's
    /// 35° floor) when they plan to qualify on both alloys. Ignored
    /// unless <see cref="IncludeLpbfPrintabilityAnalysis"/> is true.
    /// </summary>
    public Geometry.LpbfAnalysis.LpbfMaterial LpbfMaterial { get; init; }
        = Geometry.LpbfAnalysis.LpbfMaterial.CuCrZr;

    /// <summary>
    /// Build-axis orientation override (degrees, rotation about +Z
    /// from +X). <c>-1</c> (default) = auto-orient via
    /// <see cref="Geometry.LpbfAnalysis.PrintOrientationAdvisor"/>; the
    /// advisor's recommendation is surfaced on the result regardless.
    /// A non-negative value forces the overhang analysis to use that
    /// build axis — useful when the user has already picked an axis
    /// for LPBF tooling reasons and wants the gate to score against
    /// that choice rather than the advisor's preference.
    /// </summary>
    public double LpbfPrintOrientationAxis_deg { get; init; } = -1.0;

    // Build / finish
    public double SmoothingRadius_mm { get; init; } = 0.0;
    public bool IncludeManifolds { get; init; } = true;
    public bool IncludePorts { get; init; } = true;
    public double ManifoldLength_mm { get; init; } = 15.0;
    public double PortDiameter_mm { get; init; } = 10.0;
    public int ContourStationCount { get; init; } = 240;

    // Flanges (non-optimized — user choices, not in the search vector)
    public bool IncludeInjectorFlange { get; init; } = true;
    public double InjectorFlangeThickness_mm { get; init; } = 8.0;
    public double InjectorFlangeOuterRadiusFactor { get; init; } = 1.25;
    public double PropellantPortDiameter_mm { get; init; } = 6.0;
    public bool IncludeMountingFlange { get; init; } = false;
    public double MountingFlangeThickness_mm { get; init; } = 6.0;

    /// <summary>
    /// Bolt-pattern preset for the nozzle-exit mounting flange. Only
    /// consumed when <see cref="IncludeMountingFlange"/> is true.
    /// Defaults to the legacy 8-bolt generic pattern for backward
    /// compatibility with legacy saved designs.
    /// </summary>
    public Geometry.MountingFlangeStandard MountingFlangeStandard { get; init; }
        = Geometry.MountingFlangeStandard.Generic8Bolt;

    // Hot-fire readiness Item 6 / OOB-260 (2026-04-30): test-stand thrust-takeout
    // adapter. The chamber's mounting flange (IncludeMountingFlange) is the engine-
    // side interface; the takeout adapter is an aft structural extension that bolts
    // through to the test-stand load cell. When both are on, ChamberVoxelBuilder
    // emits a cylindrical adapter body downstream of the mounting flange, with its
    // own bottom bolt circle (test-stand-side preset, distinct from the chamber-side
    // preset) and optional radial umbilical pass-throughs. Defaults are off; demand-
    // gated per the Hot-fire roadmap. Requires IncludeMountingFlange=true; with
    // mounting flange off the field is silently ignored.

    /// <summary>
    /// When true AND <see cref="IncludeMountingFlange"/> is also true, the
    /// chamber STL gets a structural adapter body printed downstream of the
    /// mounting flange. Closes the test-stand-integration gap on the
    /// Hot-fire readiness roadmap (Item 6).
    /// </summary>
    public bool IncludeThrustTakeoutAdapter { get; init; } = false;

    /// <summary>Adapter body height (mm) — distance from mounting flange aft
    /// face to the test-stand bolt face. Default 50 mm.</summary>
    public double ThrustTakeoutAdapterHeight_mm { get; init; } = 50.0;

    /// <summary>Adapter outer diameter (mm). 0 = match the chamber mounting
    /// flange OD (typical case — single straight cylinder). Set non-zero to
    /// flare or step the adapter for a wider test-stand mount face.</summary>
    public double ThrustTakeoutOuterDiameter_mm { get; init; } = 0.0;

    /// <summary>Bolt-pattern preset for the test-stand-side bottom face.
    /// Independent of <see cref="MountingFlangeStandard"/> so the adapter
    /// can mate to a test stand whose bolt circle differs from the engine-
    /// side bolt circle. Defaults to the same Generic8Bolt for symmetry.</summary>
    public Geometry.MountingFlangeStandard ThrustTakeoutMountStandard { get; init; }
        = Geometry.MountingFlangeStandard.Generic8Bolt;

    /// <summary>Number of radial umbilical / instrumentation pass-throughs
    /// drilled through the adapter body. 0 = none. Pass-throughs are
    /// distributed at evenly-spaced azimuthal positions between the chamber-
    /// side bolts and pass straight through the adapter sidewall.</summary>
    public int ThrustTakeoutUmbilicalPassThroughCount { get; init; } = 0;

    /// <summary>Diameter (mm) of each umbilical pass-through hole.</summary>
    public double ThrustTakeoutUmbilicalPassThroughDiameter_mm { get; init; } = 8.0;

    // OOB-6 / Sprint B-3 (2026-04-30): acoustic-damper geometry. Closes a
    // real gap with commercial RPA — voxelforge can now ship a "stable by
    // construction" workflow by tuning Helmholtz / quarter-wave dampers
    // against the L1 / T1 / T2 chamber modes ScreechModes already reports.
    // Defaults are off (DamperType = None); the AcousticDamper.Evaluate
    // pass returns null on inactive configs and StabilityScreening
    // short-circuits — bit-identical for legacy designs.
    //
    // SA dims 31, 32, 33 wire the three highest-leverage geometry knobs
    // (HelmholtzNeckArea, HelmholtzCavityVolume, QuarterWaveLength) into
    // the optimizer. The optimizer can co-tune these against the design's
    // stability response when DamperType is set; when None they're noise.

    /// <summary>Acoustic damper family. <c>None</c> = no damper modelled.</summary>
    public Combustion.Stability.AcousticDamperType DamperType { get; init; }
        = Combustion.Stability.AcousticDamperType.None;

    /// <summary>Resonator count distributed around the chamber circumference.</summary>
    public int DamperCount { get; init; } = 8;

    /// <summary>Helmholtz neck area (mm²). SA dim 31 — drives the
    /// Helmholtz resonance band when DamperType = Helmholtz.</summary>
    [SaDesignVariable(index: 31, min: 4.0, max: 120.0)]
    public double HelmholtzNeckArea_mm2 { get; init; } = 30.0;

    /// <summary>Helmholtz neck length (mm). End-correction dominates
    /// for short necks — kept off the SA vector since its leverage is
    /// secondary to A_neck and V_cavity.</summary>
    public double HelmholtzNeckLength_mm { get; init; } = 6.0;

    /// <summary>Helmholtz cavity volume (mm³). SA dim 32 — pairs with
    /// dim 31 to set the resonance frequency.</summary>
    [SaDesignVariable(index: 32, min: 200.0, max: 8000.0)]
    public double HelmholtzCavityVolume_mm3 { get; init; } = 1500.0;

    /// <summary>Quarter-wave cavity length (mm). SA dim 33 — direct
    /// f₀ = c/(4·L) inverse, so the SA vector tunes resonance directly.</summary>
    [SaDesignVariable(index: 33, min: 6.0, max: 80.0)]
    public double QuarterWaveLength_mm { get; init; } = 20.0;

    /// <summary>Quarter-wave cavity diameter (mm). Drives volume but not
    /// resonance; kept off the SA vector — the SA optimizer has no axis
    /// of leverage on this beyond avoiding oversize gates.</summary>
    public double QuarterWaveDiameter_mm { get; init; } = 4.0;

    // Threaded port standards (categorical — never in the optimizer vector).
    public PortStandard CoolantPortStandard { get; init; } = PortStandard.Plain;
    public PortStandard PropellantPortStandard { get; init; } = PortStandard.Plain;

    // Film cooling (physics upgrade — not in optimizer vector yet, but carried through).
    public FilmCoolingInputs FilmCooling { get; init; } = new();

    /// <summary>
    /// Sprint feasibility-audit-5 (2026-04-26): SA dim 24 — fuel fraction
    /// diverted to face-injected film cooling. Overrides
    /// <see cref="FilmCoolingInputs.FuelFractionAsFilm"/> when > 0; below
    /// the bound (0.02) the override defers to whatever the FilmCooling
    /// record holds (typically AutoSeeder's 0.08 default). Range
    /// 0.02-0.15 matches the typical 2-15 % LRE film band; below 2 % the
    /// film provides little measurable wall-T attenuation, above 15 % the
    /// Isp penalty dominates and SA never wants more.
    /// </summary>
    [SaDesignVariable(index: 24, min: 0.02, max: 0.15)]
    public double FilmFuelFraction { get; init; } = 0.0;

    /// <summary>
    /// Sprint feasibility-audit-5 (2026-04-26): SA dim 25 — film-slot
    /// radial height in mm. Overrides
    /// <see cref="FilmCoolingInputs.FilmSlotHeight_mm"/> when > 0. Range
    /// 0.5-15 mm covers small attitude-thruster slots through full-scale
    /// LRE main-chamber slots. Larger slots → slower Stechman decay
    /// → film survives further downstream → more wall-T attenuation,
    /// at the cost of structural complexity.
    /// </summary>
    [SaDesignVariable(index: 25, min: 0.5, max: 15.0)]
    public double FilmSlotHeightOverride_mm { get; init; } = 0.0;

    /// <summary>
    /// Sprint feasibility-audit-H2 (2026-04-27): SA dim 26 — pintle
    /// post diameter override in mm. Overrides
    /// <see cref="Injector.InjectorPattern.PintleDiameter_mm"/> when
    /// > 0. Consumed only when the injector pattern's
    /// <see cref="Injector.InjectorPattern.ElementType"/> is
    /// <c>"Pintle"</c>; ignored on every other element type.
    ///
    /// Why this shipped: PR #88 / Sprint H1 widened the pintle
    /// blockage band but didn't fix the pintle-preset's 99 % gate
    /// firing rate. The new --dump-sa-trace tooling revealed why:
    /// SA candidates land at BL ≈ 0.20 (well below the 0.35 floor)
    /// because none of D_pintle / N_sleeve were SA-tunable, so SA's
    /// other-dim perturbations drove BL away from the seed without
    /// any way to compensate. Promoting the two pintle knobs to
    /// SA dims gives SA direct control of BL → SA can land in the
    /// [0.35, 0.90] band rather than fighting against it.
    ///
    /// Range 6-30 mm:
    ///   • Small attitude / RCS-class pintle: 6-12 mm (Apollo SPS
    ///     family; LM Descent throttle valve)
    ///   • Mid LRE-class pintle: 12-25 mm (LMDE; TRW LMA range)
    ///   • Large LRE-class pintle: 25-30 mm (SuperDraco analogue)
    /// </summary>
    [SaDesignVariable(index: 26, min: 6.0, max: 30.0)]
    public double PintleDiameterOverride_mm { get; init; } = 0.0;

    /// <summary>
    /// Sprint feasibility-audit-H2 (2026-04-27): SA dim 27 — pintle
    /// sleeve hole count override. Overrides
    /// <see cref="Injector.InjectorPattern.PintleSleeveHoleCount"/>
    /// when > 0. Quantised to nearest int when applied. Same gating
    /// as <see cref="PintleDiameterOverride_mm"/>.
    ///
    /// Range 8-32: Heister 2017 surveys real pintle sleeve counts
    /// from 12 (LMDE family) through 24-32 (SuperDraco-class). 8 is
    /// the lower limit before the angular-uniformity assumption
    /// breaks down; 32 is the upper before manufacturing complexity
    /// dominates.
    /// </summary>
    [SaDesignVariable(index: 27, min: 8.0, max: 32.0)]
    public double PintleSleeveHoleCountOverride { get; init; } = 0.0;

    // ── Per-station gas-side wall thickness (Track B, 2026-04-27) ──
    // Override-style SA dims (default 0 = use baseline GasSideWallThickness_mm)
    // letting the optimizer thicken the wall locally without paying the
    // chamber/throat penalty everywhere. Primary use case: RL10-class
    // designs with ε ≈ 80 where the exit station's r ≈ 290 mm produces
    // a hoop stress σ = ΔP·r/t that needs t > 5 mm to stay under the
    // composite hot-yield limit, but the chamber-region thermal solver
    // wants t ≈ 0.8-1.5 mm for heat extraction.
    //
    // Profile: linear interpolation in station-index space between the
    // three anchors (chamber → throat → exit). Chamber-side stations
    // (i ≤ throatIdx) interpolate (chamber_t, throat_t); exit-side
    // stations (i ≥ throatIdx) interpolate (throat_t, exit_t). Each
    // anchor falls back to GasSideWallThickness_mm when its override
    // value is 0, so legacy designs (and tests pinning the prior
    // structural numbers) round-trip bit-identically.
    //
    // Bounds: 0 (use baseline) OR [0.5, 8.0] mm. The 8 mm upper bound
    // is one notch above F-1's 4-6 mm liner — comfortable headroom for
    // RL10's ε=84 exit-station r=290 mm hoop. Lower bound 0.5 mm
    // matches GasSideWallThickness_mm's floor.

    /// <summary>
    /// Track B (2026-04-27): chamber-section wall thickness override (mm).
    /// 0 = use <see cref="GasSideWallThickness_mm"/> (uniform fallback).
    /// </summary>
    [SaDesignVariable(index: 28, min: 0.5, max: 8.0)]
    public double ChamberWallThicknessOverride_mm { get; init; } = 0.0;

    /// <summary>
    /// Track B (2026-04-27): throat-section wall thickness override (mm).
    /// 0 = use <see cref="GasSideWallThickness_mm"/> (uniform fallback).
    /// </summary>
    [SaDesignVariable(index: 29, min: 0.5, max: 8.0)]
    public double ThroatWallThicknessOverride_mm { get; init; } = 0.0;

    /// <summary>
    /// Track B (2026-04-27): exit-section wall thickness override (mm).
    /// 0 = use <see cref="GasSideWallThickness_mm"/> (uniform fallback).
    /// Primary lever for RL10-class large-ε designs where the exit hoop
    /// dominates feasibility.
    /// </summary>
    [SaDesignVariable(index: 30, min: 0.5, max: 8.0)]
    public double ExitWallThicknessOverride_mm { get; init; } = 0.0;

    // PH-49 (2026-04-29): tap-off cycle axial-station knob.
    // Real J-2S tap-off ports sit at the Mach 0.3-0.5 station in the
    // converging section (40-60 % of injector-face-to-throat distance).
    // At the throat, static T ≈ Tc × 2/(γ+1) — ~11 % cooler than the
    // injector face for γ=1.25. The prior flat-Tc assumption is
    // preserved as the default-0.5 midpoint case. Only consumed when
    // EngineCycle == TapOff; ignored on all other cycles.

    /// <summary>
    /// PH-49: axial location of the tap-off port in the converging section,
    /// expressed as a fraction of the injector-face-to-throat distance.
    /// 0 = injector face (hottest local T), 0.5 = mid-chamber (default,
    /// matches J-2S historical practice), 1 = throat (coolest local T).
    /// Only consumed when <see cref="FeedSystem.EngineCycle.TapOff"/> is
    /// selected; identity on all other cycles.
    /// </summary>
    public double TapOffAxialStation_frac { get; init; } = 0.5;

    /// <summary>
    /// PH-40 / issue #259: nominal mission firing count. Drives LCF
    /// feasibility gating via Coffin-Manson at gate
    /// <c>LCF_LIFE_INSUFFICIENT</c>. Below
    /// <see cref="Voxelforge.Structure.LowCycleFatigueAnalysis.LowCycleAdvisoryThreshold"/>
    /// (= 100), LCF is treated as non-credible (Notes-disclosure only,
    /// no gate). Default 1 = single-firing dev hardware. NOT
    /// SA-tunable — mission spec, not a continuous design knob.
    /// </summary>
    public int MissionCycles { get; init; } = 1;

    // Optional injector-face STL import (user-supplied element pattern).
    public bool IncludeInjectorSTL { get; init; } = false;
    public string InjectorSTLPath { get; init; } = "";
    public double InjectorSTLOffsetX_mm { get; init; } = -8.0;   // place minX here
    public double InjectorSTLScale { get; init; } = 1.0;
    public bool InjectorSTLAutoCenter { get; init; } = true;

    // Proof-test parameters (run on demand).
    public double ProofFactor { get; init; } = 1.5;

    // Solver knobs (2D conduction, radial profile resolution).
    public int AxialConductionSweeps { get; init; } = 3;
    public int RadialWallNodes { get; init; } = 5;

    /// <summary>Enable Mayer BL-acceleration + barrel-mixing corrections on Bartz.</summary>
    public bool EnableBartzBLCorrections { get; init; } = true;

    // Injector element pattern (non-optimized; set by UI or test code).
    // JsonIgnore: IInjectorElement is an interface and cannot be
    // round-tripped by System.Text.Json without a custom converter.
    // The ElementType string can be persisted separately in a future
    // schema migration. Null = fall back to the legacy 2-hole pattern.
    [JsonIgnore]
    public InjectorPattern? InjectorElementPattern { get; init; } = null;

    // Tolerance analysis (non-optimized; UI-configurable).
    // All four tolerances are independent; defaults represent typical LPBF
    // ±0.10 mm (3σ) on geometric dimensions. Earlier code derived rib/jacket
    // tolerance from the wall/channel knobs, which collapsed four knobs into
    // two and could hide rib-sensitive failure modes.
    public int ToleranceSampleCount { get; init; } = 400;
    public double WallThicknessTolerance_mm { get; init; } = 0.10;
    public double ChannelHeightTolerance_mm { get; init; } = 0.10;
    public double RibThicknessTolerance_mm { get; init; } = 0.10;
    public double JacketThicknessTolerance_mm { get; init; } = 0.10;

    // OOB-12: transpiration cooling (non-optimized; design-mode flag).
    // Bleeds a fraction of coolant through the porous LPBF wall using the
    // Eckert-Livingood effusion model (Sutton §4.3). Not [SaDesignVariable] —
    // preserved as categorical state across SA Unpack via CloneRecord.
    public bool   EnableTranspirationCooling { get; init; } = false;
    public double TranspirationBleedFraction { get; init; } = 0.02;
    public double TranspirationEfficiency    { get; init; } = 0.85;

    // OOB-7 (issue #343): rotating detonation engine topology.
    // Opt-in; default None preserves bit-identical legacy deflagration behaviour.
    // Not [SaDesignVariable] — categorical design decision, not a continuous knob.
    // When non-None, GenerateWith applies an Isp-gain multiplier via
    // RdeCombustion.IspGain and echoes wave-count + fill-time to the result.

    /// <summary>
    /// OOB-7 (issue #343): rotating detonation combustion-chamber topology.
    /// Default <see cref="RdeTopology.None"/> uses conventional deflagration.
    /// </summary>
    public RdeTopology RdeTopology { get; init; } = RdeTopology.None;

    /// <summary>
    /// OOB-7 (issue #343): outer radius of the RDE annular detonation channel (mm).
    /// Ignored when <see cref="RdeTopology"/> is <see cref="RdeTopology.None"/>.
    /// Default 60 mm matches a representative 5 kN LOX/CH4 RDE demonstrator.
    /// </summary>
    public double RdeAnnulusOuterRadius_mm { get; init; } = 60.0;

    /// <summary>
    /// OOB-7 (issue #343): width of the annular channel (mm), i.e. outer radius
    /// minus inner radius. Sets the channel volume available for propellant fill
    /// between detonation wave passages. Default 15 mm.
    /// </summary>
    public double RdeAnnulusWidth_mm { get; init; } = 15.0;

    /// <summary>
    /// OOB-7 (issue #343): axial height of the detonation channel (mm).
    /// Drives the annulus-fill-time calculation: taller channels require longer
    /// fill times and are more susceptible to <c>RDE_ANNULUS_FILL_STARVED</c>.
    /// Default 20 mm.
    /// </summary>
    public double RdeChannelHeight_mm { get; init; } = 20.0;
}

/// <summary>
/// Computed quantities from (OperatingConditions × RegenChamberDesign).
/// </summary>
public sealed record DerivedValues
{
    public double ThroatRadius_mm { get; init; }
    public double ThroatDiameter_mm { get; init; }
    public double ChamberRadius_mm { get; init; }
    public double ExitRadius_mm { get; init; }
    public double TotalMassFlow_kgs { get; init; }
    public double FuelMassFlow_kgs { get; init; }
    public double OxidizerMassFlow_kgs { get; init; }
    public double IdealIspVacuum_s { get; init; }
    public double IdealIspSeaLevel_s { get; init; }
    public double ThrustCoefficient { get; init; }
    public double CStarActual_ms { get; init; }
    public double ChamberTemp_K { get; init; }

    /// <summary>
    /// PH-19 (#176): bell-nozzle divergence-loss factor
    /// <c>λ_div = (1 + cos θ_e) / 2</c> applied to <c>C_F</c>. Looked up via
    /// <see cref="Chamber.RaoBellTable.DivergenceLossFactor"/> on
    /// (ε, L%) for bell + dual-bell topologies; identically <c>1.0</c>
    /// for aerospike topologies (axial plug exit). Default <c>1.0</c>
    /// preserves pre-PH-19 reads on instances that don't pass through
    /// <see cref="RegenChamberOptimization.ComputeDerived"/>.
    /// </summary>
    public double DivergenceLoss { get; init; } = 1.0;
}
