// AirbreathingEngineDesign.cs — central design record for the
// air-breathing pillar.
//
// Sibling to RegenChamberDesign on the rocket side. A single record
// (rather than per-engine-kind subtypes) so the SA framework's
// reflection-driven [SaDesignVariable] vector layout works without
// special-casing — kind-specific properties (BypassRatio for Turbofan,
// IsolatorLength_m for Scramjet) live alongside common ones and are
// just ignored by other kinds. Mirrors the rocket-side pattern where
// RegenChamberDesign carries Pintle*/Helmholtz*/QuarterWave* fields
// that only matter for specific element/damper choices.
//
// Sprint A1: scaffolding only — common geometry knobs + Kind
// discriminator. Per-kind physics knobs land in their respective
// sprints (A4 ramjet, A7 turbojet, A8 turbofan).

using Voxelforge.Engines;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Central design record for an air-breathing engine. Pairs with
/// <see cref="FlightConditions"/> to fully specify a candidate.
/// </summary>
/// <param name="Kind">
/// Engine kind. Drives cycle-solver dispatch + which knob fields are
/// honoured.
/// </param>
/// <param name="InletThroatArea_m2">
/// Inlet capture area [m²]. For a ramjet this also doubles as the
/// inlet face area; for a turbojet/turbofan a separate
/// <c>InletFaceArea_m2</c> may be added later when intake-fan geometry
/// matters.
/// </param>
/// <param name="CombustorArea_m2">
/// Combustor cross-sectional area [m²]. Drives Mach-number-into-
/// combustor + residence time. For ramjets, this is post-diffuser
/// (post-shock) station-3 area.
/// </param>
/// <param name="CombustorLength_m">
/// Combustor axial length [m]. Drives residence time + LPBF
/// printability gates downstream.
/// </param>
/// <param name="NozzleThroatArea_m2">
/// Nozzle throat (station-8) area [m²]. Together with
/// <see cref="NozzleExitArea_m2"/> sets the expansion ratio.
/// </param>
/// <param name="NozzleExitArea_m2">
/// Nozzle exit (station-9) area [m²].
/// </param>
/// <param name="EquivalenceRatio">
/// Fuel-air equivalence ratio φ (dimensionless). 1.0 = stoichiometric;
/// &lt; 1 = lean (combustor-cool but Isp-suboptimal); &gt; 1 = rich
/// (Isp-optimal but combustor-hot, drives wall-T and incomplete
/// combustion). Bounded by per-kind <c>FUEL_AIR_RATIO_OUT_OF_BAND</c>
/// gate.
/// </param>
/// <param name="CompressorPressureRatio">
/// Total-to-total compressor pressure ratio π_c = P_t3 / P_t2.
/// Turbojet / turbofan only — ramjet has no compressor and ignores
/// this field (default 1.0). Realistic single-spool turbojet π_c
/// ranges 5-15 (J85 ≈ 8); modern high-bypass turbofans push toward
/// 40+. Sprint A7 ships parametric Jones-style constant-η stand-in
/// maps; real compressor / turbine maps with off-design behaviour
/// land in a follow-on sprint.
/// </param>
/// <param name="BypassRatio">
/// Bypass ratio (BPR) — cold-stream mass flow divided by core-stream
/// mass flow. Turbofan only — ramjet / turbojet / scramjet ignore this
/// field (default 0.0 → no bypass, degenerates to a turbojet by
/// construction). Sprint A8 ships a single-spool low-bypass mixed-flow
/// turbofan, with the gate <c>BYPASS_RATIO_OUT_OF_BAND</c> clamping BPR
/// to [0.10, 2.00] — the envelope where the single-spool fan-on-the-same-
/// shaft-as-HPC simplification stays physically sensible. F404 sits at
/// BPR ≈ 0.34; high-bypass commercial engines (BPR &gt; 5) require
/// the two-spool architecture deferred to Stream B.
/// </param>
/// <param name="IsolatorLength_m">
/// Isolator axial length [m]. Scramjet only — other engine kinds
/// ignore this field (default 0.5 m). The isolator is the constant-
/// area duct between the inlet throat (station 2) and the combustor
/// entrance (station 3) that buffers combustor back-pressure from
/// the inlet. Sprint A10 uses this as a design-space knob in the
/// ScramjetObjective SA vector.
/// </param>
/// <param name="RbccMode">
/// RBCC operating mode. RBCC only — other engine kinds ignore this
/// field (default <see cref="RbccOperatingMode.Ramjet"/>). Drives
/// cycle-solver dispatch in <see cref="Cycles.RbccCycleSolver"/>:
/// DuctedRocket (M ≤ 2.5), Ramjet (M ≈ 2–6), or Scramjet (M ≥ 4).
/// Sprint A11 (sub-step 1e).
/// </param>
/// <param name="EjectorEntrainmentRatio">
/// Ejector secondary-to-primary mass-flow ratio ER = ṁ_s / ṁ_p.
/// RBCC DuctedRocket mode only — other modes ignore this field
/// (default 1.0). Phase 1 uses a constant-ER isobaric-mixing model;
/// variable-geometry ejector is a Stream B follow-on.
/// </param>
/// <remarks>
/// All areas are physical (post-LPBF-shrinkage), not corrected.
/// </remarks>
public sealed record AirbreathingEngineDesign(
    AirbreathingEngineKind Kind,
    double InletThroatArea_m2,
    double CombustorArea_m2,
    double CombustorLength_m,
    double NozzleThroatArea_m2,
    double NozzleExitArea_m2,
    double EquivalenceRatio,
    double CompressorPressureRatio = 1.0,
    double BypassRatio = 0.0,
    double IsolatorLength_m = 0.5,
    RbccOperatingMode RbccMode = RbccOperatingMode.Ramjet,
    double EjectorEntrainmentRatio = 1.0) : IEngineDesign
{
    /// <inheritdoc />
    public string Family => EngineFamilies.Airbreathing;

    /// <summary>
    /// Turbine cooling fraction τ ∈ [0, 0.3]. When > 0 the effective
    /// turbine inlet temperature is blended with the HPC exit temperature:
    ///   T_t4_eff = T_t4 · (1 − τ) + T_t3 · τ
    /// Raises the TIT feasibility ceiling from 1700 K (uncooled) to 2200 K
    /// (single-crystal Ni + TBC). 0.08 is a representative military engine
    /// compressor-bleed fraction.
    /// </summary>
    public double TurbineCoolingFraction { get; init; } = 0.0;

    /// <summary>
    /// Recuperator effectiveness ε ∈ [0, 1). GasTurbine only — other
    /// kinds ignore this field (default 0.0 = no recuperator). When > 0
    /// the turbine exhaust pre-heats the compressor discharge air before
    /// it enters the combustor, raising cycle thermal efficiency at the
    /// cost of a larger heat exchanger. Values above 0.85 are impractical.
    /// </summary>
    public double RecuperatorEffectiveness { get; init; } = 0.0;

    /// <summary>
    /// Target shaft power output [W]. GasTurbine only — other kinds
    /// ignore this field (default 0.0 = compute from design point without
    /// an explicit target). When > 0 the optimizer treats this as an
    /// aspirational shaft-power constraint; the cycle solver always
    /// returns the physically-derived W_net regardless.
    /// </summary>
    public double ShaftPowerTarget_W { get; init; } = 0.0;

    /// <summary>
    /// Fan-spool pressure ratio π_fan. Turbofan two-spool mode only.
    /// When null (default) the solver uses the single-spool proxy
    /// π_fan = √π_c and drives both fan + HPC from a single turbine.
    /// When set, activates the two-spool LP/HP architecture: LP spool
    /// drives the fan (π_fan) and HP spool drives the HPC
    /// (π_hpc = π_c / π_fan). Enables high-bypass configurations
    /// (BPR up to 8.0) where the single-spool shaft-load model breaks down.
    /// Typical values: 2.5–4.5 for military engines; 1.4–1.9 for the
    /// fan stage of high-bypass commercial engines.
    /// </summary>
    public double? PiFan { get; init; } = null;

    /// <summary>
    /// Steam boiler (drum) pressure [bar]. SteamTurbine only — other
    /// kinds ignore this field (default 0.0). Sets the high-pressure
    /// saturation temperature and superheated steam conditions entering
    /// the turbine. Practical range: 10–200 bar for industrial/utility
    /// plant cycles.
    /// </summary>
    public double SteamBoilerPressure_bar { get; init; } = 0.0;

    /// <summary>
    /// Steam condenser pressure [bar]. SteamTurbine only — other kinds
    /// ignore this field (default 0.0). Sets the low-pressure saturation
    /// temperature at the turbine exit. Practical range: 0.02–0.1 bar
    /// (near-vacuum) for condensing plant; fire by the
    /// <c>STEAM_CONDENSE_BELOW_VACUUM</c> hard gate when &lt; 0.01 bar.
    /// </summary>
    public double SteamCondensePressure_bar { get; init; } = 0.0;

    /// <summary>
    /// Superheat above saturation temperature at the boiler exit [K].
    /// SteamTurbine only — other kinds ignore this field (default 0.0 =
    /// dry saturated steam). Adding superheat increases cycle thermal
    /// efficiency and reduces turbine exit wetness. Typical values: 50–300 K.
    /// </summary>
    public double SteamSuperheatDeltaT_K { get; init; } = 0.0;

    /// <summary>
    /// Propeller / free-power-turbine power extraction fraction [-].
    /// Turboprop only — other kinds ignore this field (default 0.0).
    /// Fraction of the isentropic enthalpy available downstream of the
    /// gas-generator turbine exit (station 5) that the free power turbine
    /// captures and delivers to the propeller shaft. Typical range:
    /// 0.85–0.95 (Allison T56-A-15 ≈ 0.89 at cruise). Values below 0.5
    /// fire the hard gate <c>TURBOPROP_SHAFT_POWER_INSUFFICIENT</c>.
    /// Turboshaft ignores this field — the solver forces fpe = 1.0.
    /// </summary>
    public double PropellerPowerExtraction_frac { get; init; } = 0.0;

    /// <summary>
    /// Total resonant tube length [m] — intake horn + diffuser + combustor
    /// + tailpipe. Pulsejet only — other kinds ignore this field (default
    /// 0.0). Drives Helmholtz frequency f = (c / (2π)) · √(A_neck / (V·L))
    /// per Foa 1960 §11.2 eq 11-3. Distinct from <see cref="CombustorLength_m"/>
    /// which is just the combustor segment.
    /// </summary>
    public double PulsejetTubeLength_m { get; init; } = 0.0;

    /// <summary>
    /// Forward-firing diffuser intake area [m²] — the "neck" area in the
    /// Helmholtz lump. Pulsejet only — other kinds ignore this field
    /// (default 0.0). Cannot reuse <see cref="InletThroatArea_m2"/> because
    /// for valveless geometry the intake is structurally distinct from the
    /// nozzle throat (V-1 Argus geometry: intake ~30 cm² horn, tailpipe
    /// ~40 cm² exit; both can vary independently).
    /// </summary>
    public double PulsejetIntakeArea_m2 { get; init; } = 0.0;

    /// <summary>
    /// Tailpipe exit area [m²]. Pulsejet only — other kinds ignore this
    /// field (default 0.0). Semantically distinct from
    /// <see cref="NozzleExitArea_m2"/> (no CD nozzle on a valveless
    /// pulsejet); the cycle solver aliases to <see cref="NozzleExitArea_m2"/>
    /// when this is 0.0 so legacy v5 designs round-trip identity.
    /// </summary>
    public double PulsejetTailpipeArea_m2 { get; init; } = 0.0;

    /// <summary>
    /// Pulsejet geometry variant — selects volumetric-efficiency in the
    /// cycle solver. <see cref="Cycles.PulsejetVariant.Standard"/> (V-1-style
    /// reed-valve, η_vol = 0.14) or
    /// <see cref="Cycles.PulsejetVariant.Valveless"/> (Lockwood-Hiller U-tube,
    /// η_vol = 0.10, Foa 1960 §11.4). Pulsejet only — other kinds ignore
    /// this field (default Standard).
    /// </summary>
    public Cycles.PulsejetVariant PulsejetVariant { get; init; } = Cycles.PulsejetVariant.Standard;

    /// <summary>
    /// Enable afterburner (reheat) augmentation. Turbojet only — other
    /// engine kinds ignore this field (default false). When true, a second
    /// combustion stage is modelled between the gas-generator turbine exit
    /// (station 5) and the CD nozzle (station 8), raising T_t7 from the
    /// turbine exit temperature to the augmented value. The additional fuel
    /// flow is <see cref="AfterburnerFuelAirRatio"/> × ṁ_air.
    /// </summary>
    public bool EnableAfterburner { get; init; } = false;

    /// <summary>
    /// Afterburner fuel-to-air mass ratio f_ab [-]. Turbojet only with
    /// <see cref="EnableAfterburner"/> = true — other conditions ignore
    /// this field (default 0.0). Added on top of the core combustor
    /// fuel-air ratio f = φ · f_stoich. Typical values: 0.01–0.04
    /// (J79-GE-17 wet: ~0.025 above dry). Values that push T_t7 above
    /// the afterburner liner material limit fire
    /// <c>AFTERBURNER_LINER_OVERTEMP</c> (Hard).
    /// </summary>
    public double AfterburnerFuelAirRatio { get; init; } = 0.0;

    /// <summary>
    /// Outer bypass-duct pressure-shell wall thickness [mm]. Turbofan voxel
    /// builder only — other engine kinds ignore this field (default 2.0 mm).
    /// Distinct from the core-flow wall thickness (which is carried as the
    /// generic <c>WallThickness_mm</c> on <c>RamjetBuildOptions</c> /
    /// <c>TurbofanBuildOptions</c>). Cold-stream bypass duct sees lower
    /// pressures than the hot-stream core shell, so a typical low-bypass
    /// turbofan can run a thinner bypass shell than core (~2.0 mm vs ~3.0 mm)
    /// — exposing this as a separate knob lets the LPBF printability gates
    /// see realistic per-flow-path geometry.
    /// </summary>
    public double BypassDuctWallThickness_mm { get; init; } = 2.0;

    // ── Wave-3 LACE fields (Sprint A.W3) ───────────────────────────────────
    //
    // LACE-specific knobs ride on this record as init-only properties with
    // 0.0/NaN defaults. Other kinds ignore them. Schema airbreathing
    // v10 → v11 identity migration leaves them at default for round-
    // tripped non-LACE designs.

    /// <summary>
    /// Precooler thermal effectiveness ε [-]. LACE only — other kinds ignore
    /// (default 0.0). Bounded by [0, 1]: ε = (T_air_in − T_air_out) /
    /// (T_air_in − T_LH2_in). Cluster envelope 0.85–0.95 for the RB-545 /
    /// SABRE class; values below ~0.80 fail to liquefy air at the design
    /// flight Mach, fired by <c>LACE_PRECOOLER_EFFECTIVENESS_LOW</c>.
    /// </summary>
    public double PrecoolerEffectiveness { get; init; } = 0.0;

    /// <summary>
    /// LH₂ propellant mass flow ṁ_H2 [kg/s]. LACE only — other kinds ignore
    /// (default 0.0). Sized to provide enough precooler cooling capacity
    /// while satisfying the chamber air/fuel-ratio target. Cluster envelope
    /// 1–10 kg/s for the RB-545 reference at ~Mach 5 / 200 kN.
    /// </summary>
    public double LH2MassFlow_kgs { get; init; } = 0.0;

    /// <summary>
    /// LACE rocket-chamber pressure P_c [bar]. LACE only — other kinds
    /// ignore (default 0.0). Cluster envelope 50–150 bar; RB-545 anchored
    /// at ~70 bar.
    /// </summary>
    public double LaceChamberPressure_bar { get; init; } = 0.0;

    /// <summary>
    /// LACE air-to-fuel mass ratio MR_a/f = ṁ_air / ṁ_H2 [-]. LACE only —
    /// other kinds ignore (default 0.0). Stoichiometric LH₂/air ≈ 34.3
    /// (mass basis); RB-545 ran ox-rich at MR ≈ 5–8 to keep chamber T
    /// down. Above 34 the chamber runs lean (cool, lower Isp), below 5
    /// the chamber runs very rich (excess unburned fuel exits).
    /// </summary>
    public double LaceAirToFuelRatio { get; init; } = 0.0;

    // ── Wave-3 RDE fields (Sprint A.W4) ─────────────────────────────────
    //
    // RDE-specific knobs ride on this record as init-only properties with
    // 0/0.0 defaults. Other kinds ignore. Schema airbreathing v11 → v12
    // identity migration leaves them at default for round-tripped non-RDE
    // designs.

    /// <summary>
    /// Pressure gain ratio PGR = P_t,burned / P_t,unburned across the
    /// detonation wave [-]. RDE only — other kinds ignore (default 0.0).
    /// Chapman-Jouguet detonation yields PGR ≈ 1.10–1.30 depending on the
    /// fuel-air mixture (H₂/air at φ=1 → ~1.27; CH₄/air at φ=1 → ~1.18).
    /// Above 1.50 indicates an unstable / over-driven detonation
    /// (gate fires).
    /// </summary>
    public double RdePressureGainRatio { get; init; } = 0.0;

    /// <summary>
    /// Number of rotating detonation waves n [-] in the annulus. RDE only.
    /// Typical cluster envelope 2–6 waves at H₂/air design points.
    /// </summary>
    public int RdeWaveCount { get; init; } = 0;

    /// <summary>
    /// Annular combustor outer diameter D_o [m]. RDE only. Cluster envelope
    /// 0.05 – 0.30 m (AFRL test articles + Mitsubishi-IHI scale).
    /// </summary>
    public double RdeAnnularOuterDiameter_m { get; init; } = 0.0;

    /// <summary>
    /// Annular combustor inner diameter D_i [m]. RDE only. Must satisfy
    /// D_i &lt; D_o (annular geometry valid). Cluster envelope
    /// 0.04 – 0.28 m. Channel width (D_o − D_i)/2 must exceed the
    /// detonation-cell size (~0.5–2 mm for H₂/air at typical Pc).
    /// </summary>
    public double RdeAnnularInnerDiameter_m { get; init; } = 0.0;

    /// <summary>
    /// Annular combustor axial length L [m]. RDE only. Cluster envelope
    /// 0.05 – 0.50 m. Drives residence time and detonation-wave stability;
    /// too short → wave decay; too long → unnecessary mass + cooling.
    /// </summary>
    public double RdeAnnularLength_m { get; init; } = 0.0;
}
