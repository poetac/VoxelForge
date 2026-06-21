// ElectricPropulsionEngineDesign.cs — central design record for the
// electric-propulsion pillar.
//
// Wave-1 ships resistojet-only fields. The Kind discriminator is present
// from the start so future Wave-2 variants (HET / MPD / ion / arcjet)
// can extend the record additively without breaking schema v1 reads
// (additive fields land as init-only properties with defaults).
//
// Sibling to RegenChamberDesign on the rocket side and
// AirbreathingEngineDesign on the air-breathing side. Single record
// (rather than per-kind subtypes) so the SA framework's reflection-
// driven binding works without special-casing — kind-specific
// properties live alongside common ones and are ignored by other kinds.

using Voxelforge.Engines;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Central design record for an electric-propulsion engine. Pairs with
/// <see cref="ResistojetConditions"/> to fully specify a candidate.
/// </summary>
/// <param name="Kind">Engine kind. Drives solver dispatch + which knob fields are honoured.</param>
/// <param name="HeaterPower_W">
/// Electrical power delivered to the heater coil [W]. SA design variable
/// 1 of 6 per pillar spec §2; bind-time clipped to
/// <c>min(3000, conditions.BusPower_W_avail)</c>.
/// </param>
/// <param name="PropellantMassFlow_kgs">
/// Propellant mass flow rate [kg/s]. SA design variable 2 of 6.
/// </param>
/// <param name="NozzleThroatRadius_mm">
/// Nozzle throat radius [mm]. SA design variable 3 of 6.
/// </param>
/// <param name="NozzleAreaRatio">
/// Nozzle area ratio ε = A_exit / A_throat (dimensionless). SA design
/// variable 4 of 6.
/// </param>
/// <param name="HeaterChamberLength_mm">
/// Heater chamber axial length [mm]. SA design variable 5 of 6.
/// </param>
/// <param name="HeaterChamberRadius_mm">
/// Heater chamber inner radius [mm]. SA design variable 6 of 6.
/// </param>
/// <remarks>
/// All geometric dimensions are physical (post-LPBF-shrinkage), not
/// corrected.
/// </remarks>
public sealed record ElectricPropulsionEngineDesign(
    ElectricPropulsionEngineKind Kind,
    double                       HeaterPower_W,
    double                       PropellantMassFlow_kgs,
    double                       NozzleThroatRadius_mm,
    double                       NozzleAreaRatio,
    double                       HeaterChamberLength_mm,
    double                       HeaterChamberRadius_mm) : IEngineDesign
{
    /// <inheritdoc />
    public string Family => EngineFamilies.ElectricPropulsion;

    /// <summary>
    /// Heater material. Default <see cref="HeaterMaterial.GrainStabilizedPlatinum"/>
    /// (T_max ≈ 2500 K). Drives the Hard gate
    /// <c>RESISTOJET_HEATER_TEMP_EXCEEDED</c>.
    /// </summary>
    public HeaterMaterial HeaterMaterial { get; init; } = HeaterMaterial.GrainStabilizedPlatinum;

    /// <summary>
    /// Chamber outer-wall material emissivity ε_emit ∈ (0, 1). Default 0.30
    /// (refractory metal, polished). Drives <c>RadiationLossSolver</c>.
    /// </summary>
    public double ChamberEmissivity { get; init; } = 0.30;

    /// <summary>
    /// Chamber wall thickness [mm]. Default 1.5 mm. Not a Wave-1 SA dim
    /// (held constant); promote to SA dim if structural-margin gate
    /// becomes binding.
    /// </summary>
    public double ChamberWallThickness_mm { get; init; } = 1.5;

    /// <summary>
    /// Whether the diverging nozzle section is radiatively cooled (default true)
    /// or actively cooled (false). Wave-1 supports radiative-cooled niobium
    /// nozzles only — flipping this to false at this fidelity is a no-op,
    /// reserved for Wave-2 high-power variants where active cooling matters.
    /// </summary>
    public bool RadiativelyCooledNozzle { get; init; } = true;

    // ── Wave-2 HET fields ────────────────────────────────────────────────
    //
    // Per ADR-029 D3, HET-specific knobs ride on this record as init-only
    // properties with NaN/sentinel defaults rather than living on a
    // per-kind subtype. Resistojet ignores all of these. HET designs
    // must populate them. Schema v1 → v2 identity migration leaves them
    // at default for round-tripped Resistojet designs.

    /// <summary>
    /// HET discharge voltage [V]. SA design variable 1 of 6 for HET.
    /// Bounds 200–400 V (Goebel &amp; Katz §3 Table 3-1).
    /// Defaults to <see cref="double.NaN"/> for Resistojet.
    /// </summary>
    public double DischargeVoltage_V { get; init; } = double.NaN;

    /// <summary>
    /// HET discharge current [A]. SA design variable 2 of 6 for HET.
    /// Bounds 5–25 A. Bind-time clip on V_d × I_d ≤ BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for Resistojet.
    /// </summary>
    public double DischargeCurrent_A { get; init; } = double.NaN;

    /// <summary>
    /// Peak radial magnetic-field strength in the discharge channel [T].
    /// SA design variable 3 of 6 for HET. Bounds 0.01–0.03 T
    /// (Goebel &amp; Katz §3.6). Defaults to <see cref="double.NaN"/>
    /// for Resistojet.
    /// </summary>
    public double MagneticField_T { get; init; } = double.NaN;

    /// <summary>
    /// Anode (outer-channel-wall) radius [mm]. SA design variable 4 of 6
    /// for HET. Bounds 20–60 mm. Defaults to <see cref="double.NaN"/>
    /// for Resistojet.
    /// </summary>
    public double AnodeRadius_mm { get; init; } = double.NaN;

    /// <summary>
    /// Discharge-channel axial length [mm]. SA design variable 5 of 6 for
    /// HET. Bounds 15–40 mm. Defaults to <see cref="double.NaN"/> for
    /// Resistojet.
    /// </summary>
    public double ChannelLength_mm { get; init; } = double.NaN;

    /// <summary>
    /// Xenon mass-flow rate through the anode [kg/s]. SA design variable
    /// 6 of 6 for HET. Bounds 5e-6 to 3e-5 kg/s. Defaults to
    /// <see cref="double.NaN"/> for Resistojet (which uses
    /// <see cref="PropellantMassFlow_kgs"/> instead).
    /// </summary>
    public double XenonMassFlow_kgs { get; init; } = double.NaN;

    /// <summary>
    /// Anode wall material. Drives the Hard gate <c>HET_ANODE_OVERHEAT</c>.
    /// Defaults to <see cref="AnodeMaterial.None"/> (Resistojet).
    /// </summary>
    public AnodeMaterial AnodeMaterial { get; init; } = AnodeMaterial.None;

    /// <summary>
    /// Cathode neutraliser style. Drives the Advisory gate
    /// <c>HET_CATHODE_LIFE_LIMIT</c>.
    /// Defaults to <see cref="CathodeType.None"/> (Resistojet).
    /// </summary>
    public CathodeType CathodeType { get; init; } = CathodeType.None;

    // ── Wave-2 Arcjet fields (Sprint EP.W2.AJ) ──────────────────────────
    //
    // Arcjet adds 3 new fields (V_arc, I_arc, L_arc) plus an electrode-
    // material enum and an optional thermal-efficiency override. Reuses
    // PropellantMassFlow_kgs, NozzleThroatRadius_mm, NozzleAreaRatio,
    // HeaterChamberLength_mm, HeaterChamberRadius_mm from the resistojet
    // shape (per ADR-029 D3) — the chamber geometry is the same refractory
    // cylinder, only the heating mode changes.
    //
    // Schema v2 → v3 identity migration leaves these at default for round-
    // tripped Resistojet / HET designs.

    /// <summary>
    /// Arcjet arc terminal voltage [V]. SA design variable 1 of 6 for arcjet.
    /// Bounds 60–300 V (Sutton &amp; Biblarz 9e §16.3 cluster).
    /// Defaults to <see cref="double.NaN"/> for Resistojet / HET.
    /// </summary>
    public double ArcVoltage_V { get; init; } = double.NaN;

    /// <summary>
    /// Arcjet arc current [A]. SA design variable 2 of 6 for arcjet.
    /// Bounds 5–30 A. Bind-time clip on V_arc × I_arc ≤ BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for Resistojet / HET.
    /// </summary>
    public double ArcCurrent_A { get; init; } = double.NaN;

    /// <summary>
    /// Arcjet cathode-tip-to-constrictor arc-column length [mm]. SA design
    /// variable 3 of 6 for arcjet. Bounds 0.5–3.0 mm (Sutton §16.3 typical
    /// low-power arcjet gap). Defaults to <see cref="double.NaN"/> for
    /// Resistojet / HET.
    /// </summary>
    public double ArcGap_mm { get; init; } = double.NaN;

    /// <summary>
    /// Electrode (cathode + anode) material. Drives the Hard gate
    /// <c>ARCJET_ANODE_OVERHEAT</c>.
    /// Defaults to <see cref="ArcjetElectrodeMaterial.None"/> (Resistojet / HET).
    /// </summary>
    public ArcjetElectrodeMaterial ArcjetElectrodeMaterial { get; init; }
        = ArcjetElectrodeMaterial.None;

    /// <summary>
    /// Optional override for the arcjet thermal efficiency η_thermal ∈ (0, 1].
    /// <see cref="double.NaN"/> (default) uses
    /// <see cref="Solvers.MaeckerKovityaArcModel.DefaultThermalEfficiency"/>
    /// = 0.40 (MR-509 ATOS cluster anchor). Set to a richer value when
    /// fixture-derived calibration data exists. Resistojet / HET ignore this.
    /// </summary>
    public double ArcjetThermalEfficiency { get; init; } = double.NaN;

    // ── Wave-2 PPT fields (Sprint EP.W2.PPT) ────────────────────────────
    //
    // Pulsed Plasma Thruster adds 6 net-new fields covering capacitor + pulse
    // + parallel-rail electrode geometry. PPT does NOT reuse the resistojet-
    // shape continuous-flow fields (HeaterPower_W / PropellantMassFlow_kgs /
    // NozzleThroatRadius_mm / NozzleAreaRatio / HeaterChamberLength_mm /
    // HeaterChamberRadius_mm) — there is no continuous mass flow (ablation
    // is per-pulse), no nozzle (the discharge plume exhausts directly), and
    // no chamber (parallel rails are open-ended).
    //
    // Schema v3 → v4 identity migration leaves these at default for round-
    // tripped Resistojet / HET / Arcjet designs.

    /// <summary>
    /// Capacitor energy per pulse [J]. SA design variable 1 of 6 for PPT.
    /// Bounds 0.5 – 50 J (Solbes-Vondra Wave-2 envelope; EO-1 anchor 8 J).
    /// Defaults to <see cref="double.NaN"/> for non-PPT kinds.
    /// </summary>
    public double CapacitorEnergy_J { get; init; } = double.NaN;

    /// <summary>
    /// Pulse repetition frequency [Hz]. SA design variable 2 of 6 for PPT.
    /// Bounds 0.1 – 10 Hz. Bind-time clip on E_cap × f_pulse ≤ BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for non-PPT kinds.
    /// </summary>
    public double PulseFrequency_Hz { get; init; } = double.NaN;

    /// <summary>
    /// Inter-electrode gap [mm] (between the two parallel rails). SA design
    /// variable 3 of 6 for PPT. Bounds 5 – 30 mm (Solbes-Vondra cluster).
    /// Defaults to <see cref="double.NaN"/> for non-PPT kinds.
    /// </summary>
    public double PptElectrodeGap_mm { get; init; } = double.NaN;

    /// <summary>
    /// Solid-PTFE propellant bar length along the discharge axis [mm]. SA
    /// design variable 4 of 6 for PPT. Bounds 5 – 30 mm. Drives ablation
    /// area and Δm-per-pulse via the Solbes-Vondra fit.
    /// Defaults to <see cref="double.NaN"/> for non-PPT kinds.
    /// </summary>
    public double PptPropellantBarLength_mm { get; init; } = double.NaN;

    /// <summary>
    /// Electrode rail width [mm] (perpendicular to the discharge axis). SA
    /// design variable 5 of 6 for PPT. Bounds 5 – 30 mm.
    /// Defaults to <see cref="double.NaN"/> for non-PPT kinds.
    /// </summary>
    public double PptElectrodeWidth_mm { get; init; } = double.NaN;

    /// <summary>
    /// Optional Isp calibration override [s]. SA design variable 6 of 6 for
    /// PPT. <see cref="double.NaN"/> (default) uses
    /// <see cref="Solvers.AblationDischargeModel.DefaultExhaustVelocity_ms"/> = 8500 m/s
    /// → ≈ 870 s on PTFE (Solbes-Vondra cluster anchor). When finite, the
    /// model derives the effective exit velocity v = PptIspCalibration · g₀.
    /// Resistojet / HET / Arcjet ignore this.
    /// </summary>
    public double PptIspCalibration { get; init; } = double.NaN;

    // ── Wave-2 GIT fields (Sprint EP.W2.GIT) ────────────────────────────
    //
    // Gridded-Ion Thruster adds 6 net-new fields covering the two-grid
    // optics + beam-extraction physics. GIT does NOT reuse any of the
    // resistojet-shape continuous-flow fields — the discharge chamber feeds
    // an electrostatic-grid acceleration stage (no nozzle, no continuous
    // mass-flow concept beyond the beam-line current).
    //
    // Schema v4 → v5 identity migration leaves these at default for round-
    // tripped Resistojet / HET / Arcjet / PPT designs.

    /// <summary>
    /// Net beam-accelerating voltage V_b [V]. SA design variable 1 of 6 for
    /// GIT. Bounds 500 – 1500 V (Goebel &amp; Katz §5 cluster envelope; NSTAR
    /// anchors 1100 V).
    /// Defaults to <see cref="double.NaN"/> for non-GIT kinds.
    /// </summary>
    public double BeamVoltage_V { get; init; } = double.NaN;

    /// <summary>
    /// Design-requested ion-beam current J_b [A]. SA design variable 2 of 6
    /// for GIT. Bounds 0.5 – 3.0 A. Physics caps the actual extracted current
    /// at the Child-Langmuir saturation limit (perveance gate fires when the
    /// request exceeds the limit). Bind-time clip on V_b × J_b ≤
    /// BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for non-GIT kinds.
    /// </summary>
    public double BeamCurrent_A { get; init; } = double.NaN;

    /// <summary>
    /// Outer radius of the active screen-grid beam area [mm]. SA design
    /// variable 3 of 6 for GIT. Bounds 50 – 200 mm (NSTAR ≈ 140 mm, NEXT
    /// ≈ 180 mm). Drives the perveance limit through area scaling.
    /// Defaults to <see cref="double.NaN"/> for non-GIT kinds.
    /// </summary>
    public double ScreenGridRadius_mm { get; init; } = double.NaN;

    /// <summary>
    /// Effective screen-to-accelerator grid gap d [mm]. SA design variable 4
    /// of 6 for GIT. Bounds 0.5 – 3.0 mm. Inversely-quadratically related to
    /// the Child-Langmuir saturation current density.
    /// Defaults to <see cref="double.NaN"/> for non-GIT kinds.
    /// </summary>
    public double AccelGridGap_mm { get; init; } = double.NaN;

    /// <summary>
    /// Neutraliser-cathode emission current [A]. SA design variable 5 of 6
    /// for GIT. Bounds 0.1 – 3.5 A — must match the beam current within
    /// ±10 % to avoid spacecraft charge build-up
    /// (<c>GIT_NEUTRALIZER_CURRENT_MISMATCH</c> gate).
    /// Defaults to <see cref="double.NaN"/> for non-GIT kinds.
    /// </summary>
    public double NeutralizerCathodeCurrent_A { get; init; } = double.NaN;

    /// <summary>
    /// Optional mass-utilisation efficiency override η_m ∈ (0, 1]. SA design
    /// variable 6 of 6 for GIT. <see cref="double.NaN"/> (default) uses
    /// <see cref="Solvers.ChildLangmuirBeamModel.DefaultMassUtilization"/> = 0.90
    /// (NSTAR cluster mid-band). Set to a richer value when fixture-derived
    /// calibration data exists. Resistojet / HET / Arcjet / PPT ignore this.
    /// </summary>
    public double GitMassUtilizationOverride { get; init; } = double.NaN;

    // ── Wave-2 MPD fields (Sprint EP.W2.MPD) ────────────────────────────
    //
    // Magnetoplasmadynamic Thruster adds 4 net-new geometry/discharge fields
    // covering the self-field Maecker formula. MPD reuses
    // PropellantMassFlow_kgs from the resistojet shape (Li or Ar feed) but
    // adds its own discharge-current + chamber-geometry knobs. Schema v5 → v6
    // identity migration leaves these at default for round-tripped
    // Resistojet / HET / Arcjet / PPT / GIT designs.

    /// <summary>
    /// Arc current J_arc [A]. SA design variable 1 of 5 for MPD. Bounds
    /// 500 – 8000 A (Maecker self-field cluster envelope; NASA-Lewis 200 kW
    /// SF-MPD anchors 4000 A). Bind-time clip on V_arc × J_arc ≤
    /// BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for non-MPD kinds.
    /// </summary>
    public double MpdArcCurrent_A { get; init; } = double.NaN;

    /// <summary>
    /// Cathode outer radius r_c [mm]. SA design variable 2 of 5 for MPD.
    /// Bounds 3 – 25 mm. Drives the magnetic-pressure peak at the cathode
    /// tip (B² ∝ 1/r_c²) and the cathode-tip thermal load.
    /// Defaults to <see cref="double.NaN"/> for non-MPD kinds.
    /// </summary>
    public double MpdCathodeRadius_mm { get; init; } = double.NaN;

    /// <summary>
    /// Anode inner radius r_a [mm]. SA design variable 3 of 5 for MPD.
    /// Bounds 30 – 200 mm. Drives the Maecker thrust coefficient
    /// b ∝ ln(r_a / r_c). Must exceed <see cref="MpdCathodeRadius_mm"/>.
    /// Defaults to <see cref="double.NaN"/> for non-MPD kinds.
    /// </summary>
    public double MpdAnodeRadius_mm { get; init; } = double.NaN;

    /// <summary>
    /// Chamber axial length L [mm] (cathode tip to anode lip). SA design
    /// variable 4 of 5 for MPD. Bounds 30 – 250 mm. Drives the discharge-
    /// voltage fit V_arc = V_anode + V_col · (L / r_a).
    /// Defaults to <see cref="double.NaN"/> for non-MPD kinds.
    /// </summary>
    public double MpdChamberLength_mm { get; init; } = double.NaN;

    /// <summary>
    /// Cathode tip material. Drives the Hard gate <c>MPD_CATHODE_OVERHEAT</c>.
    /// Defaults to <see cref="MpdCathodeMaterial.None"/> (non-MPD kinds).
    /// </summary>
    public MpdCathodeMaterial MpdCathodeMaterial { get; init; } = MpdCathodeMaterial.None;

    // ── Wave-3 Applied-Field MPD fields (Sprint EP.W3.AF) ───────────────
    //
    // Self-field MPD (Wave-2) scales thrust as T = b · J² (Maecker). At kA-
    // scale currents the cathode-tip thermal load saturates the design space;
    // an external solenoid that imposes an axial B_z field opens a second
    // acceleration channel:
    //
    //   T_af = k_af · J · B_applied · r_a       (Sankaran et al. 2004; Tikhonov 1997)
    //
    // where k_af is the applied-field coupling coefficient (cluster 0.20 –
    // 0.40 across published Li/Ar campaigns; LiLFA Polk 1991 anchor ≈ 0.30).
    //
    // Total thrust: T_total = T_self + T_af. T_af is purely additive — at
    // B_applied = 0 (or NaN, treated as 0) the model degenerates to the
    // existing self-field Maecker physics with bit-identical numerical
    // output. Schema v6 → v7 identity migration.

    /// <summary>
    /// Applied-field solenoid axial magnetic-field strength B_z [T]. The
    /// thrust-augmentation knob for Wave-3 LiLFA-style MPD; see
    /// <see cref="Solvers.SelfFieldLorentzModel.Solve"/> for the additive
    /// Sankaran-2004 fit. Cluster envelope 0.05 – 0.50 T (LiLFA Polk 1991
    /// 0.10 T; Princeton X9 0.20 T). Defaults to <see cref="double.NaN"/>
    /// (treated as 0 → self-field-only behaviour for round-tripped v6
    /// designs). Drives the Hard gate <c>MPD_APPLIED_FIELD_OUT_OF_BAND</c>
    /// (when finite, must sit in [0.05, 0.50] T).
    /// </summary>
    public double MpdAppliedFieldStrength_T { get; init; } = double.NaN;

    /// <summary>
    /// Optional override for the applied-field coupling coefficient k_af
    /// (dimensionless). <see cref="double.NaN"/> (default) uses
    /// <see cref="Solvers.SelfFieldLorentzModel.DefaultAppliedFieldCoupling"/> = 0.20
    /// (cluster mid-band). Set to a fixture-derived value (e.g. the LiLFA Polk
    /// 1991 anchor ≈ 0.30) when calibration data exists. Self-field MPD / non-MPD
    /// kinds ignore this.
    /// </summary>
    public double MpdAppliedFieldCouplingOverride { get; init; } = double.NaN;

    // ── Wave-3 VASIMR fields (Sprint EP.W4 phase 1 — scaffold only) ────
    //
    // VASIMR (Variable Specific Impulse Magnetoplasma Rocket) uses a
    // helicon-source ionization stage + ion-cyclotron-resonance-heating
    // (ICRH) stage + magnetic-nozzle expansion to deliver continuously-
    // variable specific impulse at fixed input power. Reference: VX-200
    // ground-test article documented in Chang Diaz F.R., Squire J.P.,
    // Glover T.W. et al. (2009) "The VASIMR engine: project status and
    // recent accomplishments" (J. Propulsion & Power 25 / IEPC-2009-217)
    // + Bering E.A., Brukardt M., et al. (2010) "Recent improvements in
    // ionization costs and ion-cyclotron heating efficiency in the
    // VASIMR engine" (AIAA-2010-6859).
    //
    // EP.W4 phase 1 (this PR) ships ONLY the field scaffold — schema
    // forward-compatibility so future designs can be serialised today
    // even though the physics dispatch in
    // ElectricPropulsionOptimization.GenerateWith throws
    // NotImplementedException until EP.W4 phase 2 lands the helicon +
    // ICRH solver. NaN defaults ⇒ non-VASIMR kinds round-trip unchanged.

    /// <summary>
    /// Helicon-source ionization-stage RF power [W]. SA design variable
    /// 1 of 5 for VASIMR. Cluster envelope 5 – 50 kW (VX-200 baseline
    /// ~30 kW). Bind-time clip on P_helicon + P_icrh ≤ BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for non-VASIMR kinds.
    /// Wave-3 (Sprint EP.W4 phase 1 scaffold; physics deferred to phase 2).
    /// </summary>
    public double VasimrHeliconRfPower_W { get; init; } = double.NaN;

    /// <summary>
    /// ICRH (ion-cyclotron-resonance-heating) stage RF power [W]. SA
    /// design variable 2 of 5 for VASIMR. Cluster envelope 50 – 200 kW
    /// (VX-200 baseline ~170 kW). This is the dominant power consumer;
    /// the Isp lever (more ICRH = hotter ions = higher Isp at lower
    /// thrust).
    /// Defaults to <see cref="double.NaN"/> for non-VASIMR kinds.
    /// </summary>
    public double VasimrIcrhRfPower_W { get; init; } = double.NaN;

    /// <summary>
    /// Axial solenoid magnetic-field strength B_z [T] at the heating
    /// stage. SA design variable 3 of 5 for VASIMR. Cluster envelope
    /// 0.5 – 2.0 T (VX-200 baseline 0.6–1.2 T across operating points).
    /// Sets the ion-cyclotron resonance frequency
    /// (ω_ci = qB/m → 1.5 MHz for Ar at 0.6 T).
    /// Defaults to <see cref="double.NaN"/> for non-VASIMR kinds.
    /// </summary>
    public double VasimrSolenoidField_T { get; init; } = double.NaN;

    /// <summary>
    /// Magnetic-nozzle exit radius [mm] at the throat of the divergent
    /// flux-tube expansion stage. SA design variable 4 of 5 for VASIMR.
    /// Cluster envelope 50 – 200 mm (VX-200 baseline ~150 mm).
    /// Defaults to <see cref="double.NaN"/> for non-VASIMR kinds.
    /// </summary>
    public double VasimrNozzleExitRadius_mm { get; init; } = double.NaN;

    /// <summary>
    /// Argon propellant mass flow [kg/s]. SA design variable 5 of 5 for
    /// VASIMR. Cluster envelope 50 – 200 mg/s (VX-200 baseline ~110 mg/s
    /// at high-Isp operating point). VASIMR-specific (separate from the
    /// resistojet-shape <see cref="PropellantMassFlow_kgs"/> to keep
    /// kind-specific knobs cleanly separated).
    /// Defaults to <see cref="double.NaN"/> for non-VASIMR kinds.
    /// </summary>
    public double VasimrArgonMassFlow_kgs { get; init; } = double.NaN;

    // ── Wave-3 FEEP fields (Sprint EP.W5 phase 1 — scaffold only) ──────
    //
    // FEEP (Field-Emission Electric Propulsion) uses a sharp metal
    // emitter tip biased to 5–10 kV; the strong electric field at the
    // tip extracts ions from a liquid-metal propellant (typically indium
    // or cesium) directly by field-emission, no separate ionizer needed.
    // Single-stage electrostatic acceleration; sub-mN thrust at very
    // high Isp (4 000–8 000 s). References: Mair G., Genovese A.,
    // Tajmar M. (Indium-FEEP development at TUI / Austrian Research
    // Centers, 1996–2010); Marcuccio S., Genovese A., Andrenucci M.
    // (FEEP scaling laws, J. Propulsion & Power 13(5) 1997).
    //
    // EP.W5 phase 1 (this PR) ships ONLY the field scaffold — schema
    // forward-compatibility so future designs can be serialised today
    // even though the physics dispatch throws NotImplementedException
    // until EP.W5 phase 2 lands the Mair-Lozano emitter model. NaN
    // defaults ⇒ non-FEEP kinds round-trip unchanged.

    /// <summary>
    /// Accelerating voltage [V] applied between the emitter tip and the
    /// extractor electrode. SA design variable 1 of 4 for FEEP. Cluster
    /// envelope 5 000 – 12 000 V (Mair Indium-FEEP baseline ~ 9 kV).
    /// Bind-time clip on V_acc × I_beam ≤ BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for non-FEEP kinds.
    /// Wave-3 (Sprint EP.W5 phase 1 scaffold; physics deferred to phase 2).
    /// </summary>
    public double FeepAcceleratingVoltage_V { get; init; } = double.NaN;

    /// <summary>
    /// Emitter beam current [A]. SA design variable 2 of 4 for FEEP.
    /// Cluster envelope 10 – 500 μA per emitter tip (sub-mA for a
    /// single-tip emitter; mA-class for arrays). Drives thrust:
    /// T ≈ √(2 m_ion / e) · I_beam · √V_acc.
    /// Defaults to <see cref="double.NaN"/> for non-FEEP kinds.
    /// </summary>
    public double FeepBeamCurrent_A { get; init; } = double.NaN;

    /// <summary>
    /// Emitter tip radius [mm]. SA design variable 3 of 4 for FEEP.
    /// Cluster envelope 0.001 – 0.05 mm (1 – 50 μm). Smaller tip →
    /// higher local field at given V_acc → easier emission threshold.
    /// Defaults to <see cref="double.NaN"/> for non-FEEP kinds.
    /// </summary>
    public double FeepEmitterTipRadius_mm { get; init; } = double.NaN;

    /// <summary>
    /// Liquid-metal propellant kind. Discriminator drives the Mair-
    /// Lozano emitter-model branch (indium vs cesium have different
    /// work functions + ionic masses).
    /// Defaults to <see cref="FeepPropellant.None"/> for non-FEEP kinds.
    /// </summary>
    public FeepPropellant FeepPropellantMaterial { get; init; } = FeepPropellant.None;

    // ── Wave-3 HDLT fields (Sprint EP.W6 phase 1 — scaffold only) ──────
    //
    // HDLT (Helicon Double-Layer Thruster) — RF-driven helicon plasma
    // source where the plasma self-forms a current-free electrostatic
    // double-layer (CFDL) at the expansion region. The double-layer
    // accelerates ions out the back without grids, biased electrodes,
    // or a neutralizer cathode (the same population of electrons that
    // form the high-potential side eventually exit, keeping the
    // spacecraft current-balanced). Reference: Charles C., Boswell R.W.
    // (2003) "Current-free double-layer formation in a high-density
    // helicon discharge." Appl. Phys. Lett. 82(9).
    //
    // EP.W6 phase 1 (this PR) ships ONLY the field scaffold — schema
    // forward-compatibility so future designs can be serialised today
    // even though the physics dispatch throws NotImplementedException
    // until EP.W6 phase 2 lands the helicon + double-layer solver.
    // NaN defaults ⇒ non-HDLT kinds round-trip unchanged.

    /// <summary>
    /// Helicon RF power [W] driving the plasma source. SA design
    /// variable 1 of 4 for HDLT. Cluster envelope 100 – 5 000 W
    /// (ANU Charles-Boswell campaign baseline ~ 500 W).
    /// Bind-time clip on P_helicon ≤ BusPower_W_avail.
    /// Defaults to <see cref="double.NaN"/> for non-HDLT kinds.
    /// Wave-3 (Sprint EP.W6 phase 1 scaffold; physics deferred to phase 2).
    /// </summary>
    public double HdltHeliconRfPower_W { get; init; } = double.NaN;

    /// <summary>
    /// Magnetic-field gradient at the double-layer expansion region
    /// [T/m]. SA design variable 2 of 4 for HDLT. Drives the CFDL
    /// formation strength via the ratio of magnetic-mirror force to
    /// pressure gradient. Cluster envelope 1 – 50 T/m (typical
    /// Charles-Boswell setups run 5–20 T/m).
    /// Defaults to <see cref="double.NaN"/> for non-HDLT kinds.
    /// </summary>
    public double HdltMagneticFieldGradient_TpM { get; init; } = double.NaN;

    /// <summary>
    /// Discharge channel axial length [mm]. SA design variable 3 of 4
    /// for HDLT. Determines where the magnetic-flux-tube expansion
    /// crosses the double-layer formation threshold. Cluster envelope
    /// 100 – 500 mm.
    /// Defaults to <see cref="double.NaN"/> for non-HDLT kinds.
    /// </summary>
    public double HdltChannelLength_mm { get; init; } = double.NaN;

    /// <summary>
    /// Argon propellant mass flow [kg/s]. SA design variable 4 of 4
    /// for HDLT. Cluster envelope 1 – 50 mg/s (low compared to MPD
    /// / VASIMR because HDLT is sub-mN thrust class).
    /// Defaults to <see cref="double.NaN"/> for non-HDLT kinds.
    /// </summary>
    public double HdltArgonMassFlow_kgs { get; init; } = double.NaN;
}

/// <summary>
/// Liquid-metal propellant choice for FEEP thrusters. Drives the
/// emitter-model branch (work function + ionic mass).
/// </summary>
public enum FeepPropellant
{
    /// <summary>Sentinel — non-FEEP kinds default here.</summary>
    None = 0,

    /// <summary>
    /// Indium (In, m_i = 114.82 u, φ_W ≈ 4.12 eV). The dominant
    /// FEEP propellant choice — easier handling than Cs, longer storage
    /// life, lower vapor pressure. Mair / TUI Indium-FEEP cluster anchor.
    /// </summary>
    Indium = 1,

    /// <summary>
    /// Cesium (Cs, m_i = 132.91 u, φ_W ≈ 1.95 eV). Lowest work function
    /// of any stable element → lowest extraction threshold. Air-sensitive
    /// (reacts with H₂O / O₂) and harder to handle than indium. Used
    /// in early ESA FEEP campaigns.
    /// </summary>
    Cesium = 2,
}

/// <summary>
/// Heater coil material. Drives the maximum sustained heater temperature
/// the structural gate <c>RESISTOJET_HEATER_TEMP_EXCEEDED</c> enforces.
/// </summary>
public enum HeaterMaterial
{
    /// <summary>
    /// Grain-stabilized platinum (Aerojet MR-501-series). T_max ≈ 2500 K
    /// for sustained operation. Wave-1 default.
    /// </summary>
    GrainStabilizedPlatinum = 0,

    /// <summary>
    /// Tungsten-rhenium alloy (high-power resistojets, NASA Lewis test
    /// articles). T_max ≈ 2800 K. Higher capability but higher mass and
    /// recrystallization risk above ~2500 K.
    /// </summary>
    TungstenRhenium = 1,
}
