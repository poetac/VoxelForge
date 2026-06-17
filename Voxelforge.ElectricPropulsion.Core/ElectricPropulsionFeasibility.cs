// ElectricPropulsionFeasibility.cs — feasibility-gate evaluator for the
// electric-propulsion pillar.
//
// Sprint E.2 (Wave-1): 5 hard + 5 advisory gates per pillar spec §6 (Resistojet only).
// Sprint EP.W2.HET (Wave-2): + 3 hard + 3 advisory gates per ADR-029 D6 (HallEffect).
//
// Per ADR-029 D6, this stays a single parallel-evaluator entry point with
// kind-predicated blocks rather than splitting into per-kind static
// classes. AirbreathingFeasibility.Evaluate uses the same idiom across
// 7 engine kinds, and the registry's rocket-shaped
// Action<RegenGenerationResult,…> signature (risk #2 in ADR-026 §9)
// is still not in scope for unification.
//
// Gate ordering is load-bearing: GateOrderingSnapshotTests pins the emission
// sequence so a future refactor can't silently drop a gate. Per-kind
// emission ordering is preserved (Resistojet block first, HallEffect
// block second; future variants extend below).

using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Output of <see cref="ElectricPropulsionFeasibility.Evaluate"/>.
/// Hard violations make the candidate infeasible; advisories surface
/// to UI / reporting without gating optimization.
/// </summary>
/// <param name="Hard">Violations that fail <c>IsFeasible</c>.</param>
/// <param name="Advisories">Soft warnings.</param>
public sealed record ElectricPropulsionFeasibilityResult(
    IReadOnlyList<FeasibilityViolation> Hard,
    IReadOnlyList<FeasibilityViolation> Advisories);

/// <summary>
/// Parallel feasibility-gate evaluator for the electric-propulsion
/// pillar. Wave-1 covers the resistojet variant (10 gates per pillar
/// spec §6).
/// </summary>
public static class ElectricPropulsionFeasibility
{
    /// <summary>
    /// Hard gate threshold: gas radiative loss fraction beyond which the
    /// heater can't reach steady state at the design power. Per pillar
    /// spec §6 #2 (NASA TM-2002-211314 §3).
    /// </summary>
    public const double RadiationFractionHardLimit = 0.50;

    /// <summary>
    /// Hard gate threshold for tungsten-rhenium heater material [K].
    /// Pillar spec §6 #1; Lyon NASA-CR-179614 (1986).
    /// </summary>
    public const double HeaterTempLimitWRe_K = 2800.0;

    /// <summary>
    /// Hard gate threshold for grain-stabilized platinum heater [K]. Pillar spec §6 #1.
    /// </summary>
    public const double HeaterTempLimitPt_K = 2500.0;

    /// <summary>
    /// Advisory area-ratio band lower limit. Below this, the advisory gate
    /// <c>RESISTOJET_AREA_RATIO_OUT_OF_BAND</c> fires.
    /// </summary>
    public const double AreaRatioBandLow = 25.0;

    /// <summary>
    /// Advisory area-ratio band upper limit.
    /// </summary>
    public const double AreaRatioBandHigh = 150.0;

    /// <summary>
    /// Advisory thrust floor [N]. Below this, the design is below the
    /// typical mission-floor for station-keeping use (Iridium / EOS-AM1
    /// mission specs).
    /// </summary>
    public const double ThrustFloor_N = 0.05;

    /// <summary>
    /// Advisory Isp floor [s]. Below this, resistojet isn't competitive
    /// vs cold-gas thrusters (Sutton §16.1).
    /// </summary>
    public const double IspFloor_s = 200.0;

    /// <summary>
    /// Advisory thrust-efficiency floor. NASA TM-2002-211314 §3 efficiency-
    /// band literature survey.
    /// </summary>
    public const double EfficiencyFloor = 0.65;

    /// <summary>
    /// Advisory frozen-flow loss threshold [K]. Above this T_chamber, with
    /// N or H species present, recombination is suppressed in sub-mm-throat
    /// residence times (NASA TM-2002-211314 §4).
    /// </summary>
    public const double FrozenFlowThreshold_K = 2500.0;

    /// <summary>
    /// Mole-fraction floor below which a species is considered "trace" and
    /// doesn't activate the frozen-flow gate. Matches the per-species
    /// decomposition-limit floor in <c>PropellantTables.MixtureDecompositionLimit_K</c>.
    /// </summary>
    private const double SpeciesMoleFractionFloor = 0.01;

    // ── HET gate thresholds (ADR-029 D6, ADR-038 D1, Goebel & Katz §3 / §6) ─

    /// <summary>
    /// HET discharge voltage hard-band lower limit [V]. Set at 100 V to
    /// cover the Busch HET model's semi-empirical validity floor and to
    /// admit lower-power TAL-family configurations near 200 V; below 100 V
    /// xenon does not reliably ionise (Goebel &amp; Katz §3.4 breakdown
    /// floor ≈ 90–110 V). Widened from 150 V per ADR-038 D1.
    /// </summary>
    public const double HetDischargeVoltageMin_V = 100.0;

    /// <summary>
    /// HET discharge voltage hard-band upper limit [V]. Set at 1 000 V to
    /// cover HiVHAc / BHT-8000 / HERMeS-class HV-Hall thrusters
    /// (600–800 V cluster) plus headroom. The Busch ion-acceleration
    /// term remains valid in this regime; above 1 kV the binding
    /// constraint is channel-wall erosion captured separately by
    /// HET_ANODE_OVERHEAT. Widened from 500 V per ADR-038 D1.
    /// </summary>
    public const double HetDischargeVoltageMax_V = 1000.0;

    /// <summary>Anode-wall temperature limit for graphite [K].</summary>
    public const double HetAnodeLimitGraphite_K = 2000.0;

    /// <summary>Anode-wall temperature limit for boron-nitride [K].</summary>
    public const double HetAnodeLimitBoronNitride_K = 1500.0;

    /// <summary>Anode-wall temperature limit for alumina-SiC [K].</summary>
    public const double HetAnodeLimitAluminaSiC_K = 1900.0;

    /// <summary>Minimum B-field [T] required to confine electrons (Hall parameter cutoff).</summary>
    public const double HetMagneticFieldMin_T = 0.005;

    /// <summary>Plume divergence half-angle advisory limit [rad] = 30°.</summary>
    public const double HetPlumeDivergenceAdvisoryLimit_rad = 0.524;

    /// <summary>HollowCathode rated discharge current ceiling [A].</summary>
    public const double HetCathodeRatedHollow_A = 20.0;

    /// <summary>FilamentCathode rated discharge current ceiling [A].</summary>
    public const double HetCathodeRatedFilament_A = 5.0;

    /// <summary>Cathode-life advisory factor (Goebel &amp; Katz §6.2).</summary>
    public const double HetCathodeLifeLimitFactor = 1.2;

    /// <summary>Mass-utilisation advisory floor (Goebel &amp; Katz §3.5).</summary>
    public const double HetMassUtilizationFloor = 0.85;

    // ── Arcjet gate thresholds (Sprint EP.W2.AJ, Sutton 9e §16.3) ───────

    /// <summary>Arcjet voltage hard-band lower limit [V].</summary>
    public const double ArcjetVoltageMin_V = 40.0;

    /// <summary>Arcjet voltage hard-band upper limit [V].</summary>
    public const double ArcjetVoltageMax_V = 400.0;

    /// <summary>Anode-wall temperature limit for tungsten [K].</summary>
    public const double ArcjetAnodeLimitTungsten_K = 3650.0;

    /// <summary>Anode-wall temperature limit for molybdenum [K].</summary>
    public const double ArcjetAnodeLimitMolybdenum_K = 2890.0;

    /// <summary>Anode-wall temperature limit for rhenium [K].</summary>
    public const double ArcjetAnodeLimitRhenium_K = 3460.0;

    /// <summary>
    /// Arcjet thermal efficiency advisory floor. Below this, an arcjet
    /// is uncompetitive vs a resistojet at the same mass flow (Sutton §16.3
    /// reports 0.30–0.50; floor at 0.25 catches catastrophic wall-loss
    /// designs).
    /// </summary>
    public const double ArcjetThermalEfficiencyFloor = 0.25;

    /// <summary>
    /// Arcjet frozen-flow advisory threshold [K]. Above this chamber T,
    /// dissociation of N/H species suppresses recombination on sub-mm-throat
    /// residence times (NASA TM-2002-211314 §4 — same physics as resistojet
    /// gate, looser threshold because arcjet T_chamber is much higher).
    /// </summary>
    public const double ArcjetFrozenFlowThreshold_K = 4500.0;

    // ── PPT gate thresholds (Sprint EP.W2.PPT, Solbes-Vondra cluster) ────

    /// <summary>PPT capacitor-energy hard-band lower limit [J] (Solbes-Vondra cluster envelope).</summary>
    public const double PptCapacitorEnergyMin_J = 0.5;

    /// <summary>PPT capacitor-energy hard-band upper limit [J].</summary>
    public const double PptCapacitorEnergyMax_J = 50.0;

    /// <summary>
    /// PPT no-breakdown threshold [J]. Below this E_cap a stable arc channel
    /// does not form between the parallel rails; the discharge does not
    /// ionise enough propellant to produce useful thrust (Vondra &amp;
    /// Thomassen 1974 cluster threshold).
    /// </summary>
    public const double PptBreakdownEnergyMin_J = 1.0;

    /// <summary>
    /// PPT impulse-bit advisory floor [N·s] = 100 µN·s. Below this, the
    /// design is below typical CubeSat-class mission utility — cold-gas
    /// thrusters become competitive at lower complexity.
    /// </summary>
    public const double PptImpulseBitFloor_Ns = 100e-6;

    /// <summary>
    /// PPT ablation-rate advisory ceiling [kg/pulse] = 200 µg/pulse. Above
    /// this Δm, PTFE bar lifetime drops below the mission-typical 100 hr
    /// continuous-operation envelope at typical pulse rates. EO-1 EP-12
    /// nominal Δm ≈ 100 µg/pulse at E_cap = 22 J — designs above 50 J or
    /// with anomalously high ablation coefficient surface here.
    /// </summary>
    public const double PptAblationRateMax_kgPerPulse = 2e-7;

    // ── GIT gate thresholds (Sprint EP.W2.GIT, ADR-038 D2, Goebel & Katz §5) —

    /// <summary>
    /// GIT beam voltage hard-band lower limit [V]. Set at 200 V — the
    /// Child-Langmuir space-charge-limited beam-current expression is
    /// closed-form analytic for any V_b &gt; 0; the floor exists only to
    /// flag designs below the practical extraction-optics envelope.
    /// Widened from 300 V per ADR-038 D2.
    /// </summary>
    public const double GitBeamVoltageMin_V = 200.0;

    /// <summary>
    /// GIT beam voltage hard-band upper limit [V]. Set at 12 000 V to
    /// cover NEXIS (7.5 kV) + HiPEP (8.0 kV) + kilovolt-class NEP-concept
    /// headroom. The Child-Langmuir physics is closed-form and valid
    /// arbitrarily high; binding constraints above 12 kV are grid
    /// impingement / sputtering, captured separately by
    /// GIT_PERVEANCE_LIMIT_EXCEEDED + GIT_GRID_LIFETIME_BELOW_FLOOR.
    /// Widened from 2 000 V per ADR-038 D2.
    /// </summary>
    public const double GitBeamVoltageMax_V = 12000.0;

    /// <summary>
    /// Neutraliser-current matching tolerance (fractional). |J_neut − J_beam| / J_beam
    /// above this fraction fires <c>GIT_NEUTRALIZER_CURRENT_MISMATCH</c>.
    /// </summary>
    public const double GitNeutralizerMatchingTolerance = 0.10;

    /// <summary>
    /// Plume-divergence advisory limit [rad] = 30° (slightly looser than HET
    /// because GIT beams are inherently more collimated by the grid optics).
    /// </summary>
    public const double GitPlumeDivergenceAdvisoryLimit_rad = 0.524;

    /// <summary>
    /// Grid-lifetime advisory floor [hours] = 1000 h. NSTAR demonstrated
    /// &gt; 30 000 h on Deep Space 1; the floor catches designs with anomalous
    /// accel-grid geometry that drives sputter erosion outside the cluster
    /// envelope (Goebel &amp; Katz §5.6 sputter-yield model). Computed from
    /// the accel-gap × beam-current product as a coarse proxy at this fidelity.
    /// </summary>
    public const double GitGridLifetimeAdvisoryFloor_hr = 1000.0;

    // ── MPD gate thresholds (Sprint EP.W2.MPD, Polk 1991 / Sovey 1990) ──

    /// <summary>MPD arc current hard-band lower limit [A] (self-field cluster).</summary>
    public const double MpdArcCurrentMin_A = 200.0;

    /// <summary>MPD arc current hard-band upper limit [A] (above this, anode spotting becomes pathological).</summary>
    public const double MpdArcCurrentMax_A = 10000.0;

    /// <summary>Cathode-tip temperature limit for tungsten [K].</summary>
    public const double MpdCathodeLimitTungsten_K = 3700.0;

    /// <summary>Cathode-tip temperature limit for thoriated tungsten [K].</summary>
    public const double MpdCathodeLimitThoriatedTungsten_K = 3200.0;

    /// <summary>Cathode-tip temperature limit for lanthanum hexaboride [K].</summary>
    public const double MpdCathodeLimitLaB6_K = 2200.0;

    /// <summary>
    /// "Onset" parameter ξ = J_kA² / ṁ_g/s above which the MPD transitions
    /// from steady operation into anode-spotting / pathological regimes
    /// (Choueiri 1998 empirical fit). The cluster onset is
    /// ξ_onset ≈ 100–200 (kA)²/(g/s) depending on propellant — Li sits at
    /// ~150, Ar at ~100. Use 150 as a propellant-agnostic upper bound; the
    /// advisory ceiling sits at 80 % of that (= 120) to surface designs
    /// approaching onset before they cross it.
    /// </summary>
    public const double MpdOnsetParameterAdvisoryLimit_kA2PerGs = 150.0;

    /// <summary>
    /// Maecker thrust-efficiency advisory floor. Self-field MPD without
    /// applied B field lands 0.10–0.30 (Polk 1991). Floor at 0.05 catches
    /// catastrophic geometry choices that nuke η_T below the cluster
    /// envelope.
    /// </summary>
    public const double MpdThrustEfficiencyFloor = 0.05;

    // ── Applied-Field MPD gate thresholds (Sprint EP.W3.AF, LiLFA Polk 1991) ──

    /// <summary>
    /// Applied-field solenoid B_z lower band limit [T]. Below this the
    /// augmentation contribution is negligible (T_af / T_total &lt; 5 % at
    /// kA-scale arc currents on cluster geometry); designs sitting below
    /// this threshold should run as self-field MPD instead. LiLFA Polk 1991
    /// anchor 0.10 T; cluster envelope 0.05–0.50 T.
    /// </summary>
    public const double MpdAppliedFieldMin_T = 0.05;

    /// <summary>
    /// Applied-field solenoid B_z upper band limit [T]. Above this the
    /// Sankaran-2004 linear fit breaks down (Hall-parameter rises into a
    /// strongly-magnetised regime not captured by the additive thrust
    /// model). 0.50 T is the documented upper band of the LiLFA / Princeton
    /// X9 envelope. Hard gate when finite.
    /// </summary>
    public const double MpdAppliedFieldMax_T = 0.50;

    /// <summary>
    /// Advisory ceiling on the applied-field thrust fraction T_af /
    /// (T_self + T_af). Above this fraction the bare-Maecker T_self term is
    /// no longer the dominant acceleration mechanism — the design is in the
    /// pure-AF regime which sits outside the Sankaran-2004 fit's calibrated
    /// range and the cluster-band fixture coverage. 0.80 caps the fraction
    /// at the LiLFA documented upper bound.
    /// </summary>
    public const double MpdAppliedFieldDominanceCeiling = 0.80;

    // ── FEEP gate thresholds (Sprint EP.W5.phase2, Mair-Lozano cluster) ──

    /// <summary>FEEP accelerating-voltage hard-band lower limit [V] (Mair cluster).</summary>
    public const double FeepAcceleratingVoltageMin_V = 5000.0;

    /// <summary>FEEP accelerating-voltage hard-band upper limit [V] (above this the extractor breakdown becomes pathological).</summary>
    public const double FeepAcceleratingVoltageMax_V = 12000.0;

    /// <summary>FEEP emitter-tip radius hard-band lower limit [mm] (1 μm — manufacturing floor).</summary>
    public const double FeepEmitterTipRadiusMin_mm = 0.001;

    /// <summary>FEEP emitter-tip radius hard-band upper limit [mm] (50 μm — above this the tip field collapses below the FN threshold at cluster V_acc).</summary>
    public const double FeepEmitterTipRadiusMax_mm = 0.050;

    /// <summary>FEEP beam-current hard-band lower limit [A] (1 μA — emission floor; below this T &lt; 100 nN and ṁ is negligible).</summary>
    public const double FeepBeamCurrentMin_A = 1.0e-6;

    /// <summary>FEEP beam-current hard-band upper limit [A] (1 mA — single-emitter ceiling; arrays scale linearly via multiple tips, modelled per-tip).</summary>
    public const double FeepBeamCurrentMax_A = 1.0e-3;

    /// <summary>
    /// Fowler-Nordheim emission-threshold advisory limit [V/m]. Real
    /// emitters turn on between 5×10⁸ and 2×10⁹ V/m depending on work
    /// function; the model uses 1×10⁹ V/m as the cluster mid-band. Below
    /// this the SA design is in the sub-threshold regime where the beam
    /// current would in reality be much lower than the design's
    /// FeepBeamCurrent_A specification (the model accepts the input
    /// uncritically; this advisory flags the gap).
    /// </summary>
    public const double FeepFnThresholdField_VperM = 1.0e9;

    /// <summary>
    /// FEEP thrust advisory floor [N] (1 μN). Below this the engine is
    /// sub-mission for any practical FEEP application (attitude control
    /// at small-sat scale requires ≥ 1 μN; long-baseline interferometry
    /// formation-flying requires sub-μN but those are out of cluster).
    /// </summary>
    public const double FeepThrustAdvisoryFloor_N = 1.0e-6;

    // ── HDLT gate thresholds (Sprint EP.W6.phase2, Charles-Boswell cluster) ──

    /// <summary>
    /// Helicon-mode RF power floor [W]. Below this the helicon source
    /// collapses to inductive / capacitive and the CFDL fails to form
    /// (Chen 1991; cluster floor 50 W).
    /// </summary>
    public const double HdltHeliconModeFloor_W = 50.0;

    /// <summary>
    /// Minimum double-layer strength to produce useful thrust [V].
    /// Below ~ 5 V the ion-acceleration energy is sub-eV and thrust
    /// approaches the cold-flow neutral floor. Cluster floor from
    /// Charles 2007 review.
    /// </summary>
    public const double HdltDoubleLayerMinStrength_V = 5.0;

    /// <summary>
    /// Channel-length × field-gradient product needed for DL formation
    /// [T]. The integrated `∇B · L` is the "B-field expansion ratio";
    /// below ~ 0.5 T (in integrated units, corresponding to ln(B_ratio)
    /// &lt; 0.22) the flux-tube expansion is too gentle for a CFDL to
    /// self-organise. Plihon 2007 measurements.
    /// </summary>
    public const double HdltMinIntegratedGradient_T = 0.5;

    /// <summary>
    /// HDLT plume-divergence advisory ceiling [rad]. HDLT plumes are
    /// already wide (~28°); above ~40° the magnetic flux-tube downstream
    /// is dispersing the beam too quickly and the thrust efficiency
    /// drops sharply.
    /// </summary>
    public const double HdltPlumeDivergenceAdvisoryCeiling_rad = 0.70;

    /// <summary>
    /// Ionisation fraction advisory floor [-]. Below 0.01 the model is
    /// effectively running on neutral throughflow; either the RF power
    /// is undersized or the channel length is too short for adequate
    /// ionisation residence time.
    /// </summary>
    public const double HdltIonisationFractionFloor = 0.01;

    // ── VASIMR gate thresholds (Sprint EP.W4.phase2, VX-200i cluster) ──

    /// <summary>VASIMR solenoid field hard-band lower limit [T].</summary>
    public const double VasimrSolenoidFieldMin_T = 0.3;

    /// <summary>VASIMR solenoid field hard-band upper limit [T] (above this superconducting magnet limits + safety).</summary>
    public const double VasimrSolenoidFieldMax_T = 6.0;

    /// <summary>
    /// VASIMR helicon-to-ICRH power-ratio advisory band. Cluster envelope
    /// 0.05-0.50 (helicon fraction of total RF power). VX-200i runs at
    /// ~0.15. Outside this band the design is either under-ionised
    /// (helicon-starved) or wasting power on excess ionisation.
    /// </summary>
    public const double VasimrHeliconRatioMin = 0.05;

    /// <summary>VASIMR helicon-to-ICRH power-ratio advisory upper band.</summary>
    public const double VasimrHeliconRatioMax = 0.50;

    /// <summary>
    /// VASIMR ionisation fraction advisory floor. Below 0.50 the helicon
    /// stage is undersized for the propellant flow — substantial fraction
    /// of argon exits unionised (cold neutral) and contributes negligibly
    /// to thrust.
    /// </summary>
    public const double VasimrIonisationFractionFloor = 0.50;

    /// <summary>
    /// VASIMR nozzle-conversion-efficiency advisory floor. Below 0.30 the
    /// magnetic mirror is too gentle and most T_⊥ stays perpendicular
    /// (sub-thrust loop in the plume). Cluster mid-band 0.50-0.85.
    /// </summary>
    public const double VasimrNozzleConversionFloor = 0.30;

    /// <summary>
    /// Run gates in canonical per-kind order. Resistojet emits 5 hard +
    /// 5 advisory; HallEffect emits 3 hard + 3 advisory. Future plasma
    /// variants extend the switch additively per ADR-029 D2.
    /// </summary>
    /// <param name="design">The candidate design.</param>
    /// <param name="conditions">The operating conditions.</param>
    /// <param name="result">
    /// Result of <see cref="ElectricPropulsionOptimization.GenerateWith"/>'s
    /// physics solve. Gates inspect chamber temp, thrust, Isp, plasma
    /// state etc. from this object.
    /// </param>
    public static ElectricPropulsionFeasibilityResult Evaluate(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        ElectricPropulsionResult result)
    {
        var hard = new List<FeasibilityViolation>(8);
        var advisories = new List<FeasibilityViolation>(8);

        switch (design.Kind)
        {
            case ElectricPropulsionEngineKind.Resistojet:
                // Hard gate 1 — heater temperature exceeded.
                EvaluateHeaterTempExceeded(design, result, hard);
                // Hard gate 2 — radiation loss fraction excessive.
                EvaluateRadiationFractionExcessive(result, hard);
                // Hard gate 3 — nozzle unchoked.
                EvaluateNozzleUnchoked(result, hard);
                // Hard gate 4 — propellant decomposition.
                EvaluatePropellantDecomposition(conditions, result, hard);
                // Hard gate 5 — heat leak exceeds input.
                EvaluateHeatLeakExceedsInput(design, result, hard);

                // Advisory gate 6 — area ratio out of band.
                EvaluateAreaRatioOutOfBand(design, advisories);
                // Advisory gate 7 — thrust below mission floor.
                EvaluateThrustBelowMin(result, advisories);
                // Advisory gate 8 — Isp below floor.
                EvaluateIspBelowFloor(result, advisories);
                // Advisory gate 9 — efficiency below floor.
                EvaluateEfficiencyBelowFloor(result, advisories);
                // Advisory gate 10 — frozen flow loss excessive.
                EvaluateFrozenFlowLossExcessive(conditions, result, advisories);
                break;

            case ElectricPropulsionEngineKind.HallEffect:
                // Hard HET gate 1 — discharge voltage out of band.
                EvaluateHetDischargeVoltageOutOfBand(design, hard);
                // Hard HET gate 2 — anode wall overheating.
                EvaluateHetAnodeOverheat(design, result, hard);
                // Hard HET gate 3 — magnetic field insufficient for electron confinement.
                EvaluateHetMagneticFieldInsufficient(design, hard);

                // Advisory HET gate 4 — plume divergence excessive.
                EvaluateHetPlumeDivergenceExcessive(result, advisories);
                // Advisory HET gate 5 — cathode life limit (operating I above rated × 1.2).
                EvaluateHetCathodeLifeLimit(design, advisories);
                // Advisory HET gate 6 — mass utilisation below floor.
                EvaluateHetMassUtilizationLow(result, advisories);
                break;

            case ElectricPropulsionEngineKind.Arcjet:
                // Hard arcjet gate 1 — arc voltage out of band.
                EvaluateArcjetVoltageOutOfBand(design, hard);
                // Hard arcjet gate 2 — anode wall overheating.
                EvaluateArcjetAnodeOverheat(design, result, hard);

                // Advisory arcjet gate 3 — thermal efficiency below floor.
                EvaluateArcjetThermalEfficiencyLow(result, advisories);
                // Advisory arcjet gate 4 — frozen-flow loss excessive.
                EvaluateArcjetFrozenFlowLossExcessive(result, advisories);
                break;

            case ElectricPropulsionEngineKind.PulsedPlasmaThruster:
                // Hard PPT gate 1 — capacitor energy out of cluster band.
                EvaluatePptCapacitorEnergyOutOfBand(design, hard);
                // Hard PPT gate 2 — capacitor energy below breakdown threshold.
                EvaluatePptNoBreakdown(design, hard);

                // Advisory PPT gate 3 — impulse bit below mission floor.
                EvaluatePptImpulseBitBelowFloor(result, advisories);
                // Advisory PPT gate 4 — ablation rate excessive.
                EvaluatePptAblationRateExcessive(result, advisories);
                break;

            case ElectricPropulsionEngineKind.GriddedIon:
                // Hard GIT gate 1 — beam voltage out of band.
                EvaluateGitBeamVoltageOutOfBand(design, hard);
                // Hard GIT gate 2 — Child-Langmuir perveance saturation.
                EvaluateGitPerveanceLimitExceeded(result, hard);
                // Hard GIT gate 3 — neutraliser-cathode current mismatch.
                EvaluateGitNeutralizerCurrentMismatch(design, result, hard);

                // Advisory GIT gate 4 — plume divergence excessive.
                EvaluateGitPlumeDivergenceExcessive(result, advisories);
                // Advisory GIT gate 5 — grid lifetime below floor.
                EvaluateGitGridLifetimeBelowFloor(design, result, advisories);
                break;

            case ElectricPropulsionEngineKind.MagnetoPlasmaDynamic:
                // Hard MPD gate 1 — arc current out of band.
                EvaluateMpdArcCurrentOutOfBand(design, hard);
                // Hard MPD gate 2 — cathode wall overheating.
                EvaluateMpdCathodeOverheat(design, result, hard);
                // Hard MPD gate 3 — anode-cathode geometry inverted (r_a ≤ r_c).
                EvaluateMpdGeometryInverted(design, hard);
                // Hard MPD gate 4 — applied-field strength out of band (Sprint EP.W3).
                EvaluateMpdAppliedFieldOutOfBand(design, hard);

                // Advisory MPD gate 5 — onset parameter (J²/ṁ) excessive.
                EvaluateMpdOnsetParameterExcessive(design, advisories);
                // Advisory MPD gate 6 — Maecker thrust efficiency below floor.
                EvaluateMpdThrustEfficiencyLow(result, advisories);
                // Advisory MPD gate 7 — applied-field thrust fraction dominates (Sprint EP.W3).
                EvaluateMpdAppliedFieldDominates(result, advisories);
                break;

            case ElectricPropulsionEngineKind.Feep:
                // Hard FEEP gate 1 — accelerating voltage out of band.
                EvaluateFeepAcceleratingVoltageOutOfBand(design, hard);
                // Hard FEEP gate 2 — emitter tip radius out of band.
                EvaluateFeepEmitterTipRadiusOutOfBand(design, hard);
                // Hard FEEP gate 3 — beam current out of band.
                EvaluateFeepBeamCurrentOutOfBand(design, hard);
                // Hard FEEP gate 4 — total power exceeds bus.
                EvaluateFeepTotalPowerExceedsBus(design, conditions, hard);

                // Advisory FEEP gate 5 — tip field below Fowler-Nordheim threshold.
                EvaluateFeepTipFieldBelowFnThreshold(result, advisories);
                // Advisory FEEP gate 6 — thrust below FEEP mission floor (1 μN).
                EvaluateFeepThrustBelowFloor(result, advisories);
                break;

            case ElectricPropulsionEngineKind.Hdlt:
                // Hard HDLT gate 1 — RF power below helicon-mode threshold.
                EvaluateHdltRfPowerBelowHeliconThreshold(design, hard);
                // Hard HDLT gate 2 — DL strength too weak for thrust.
                EvaluateHdltDoubleLayerTooWeak(result, hard);
                // Hard HDLT gate 3 — channel geometry too short for DL.
                EvaluateHdltChannelGeometryInsufficient(design, hard);
                // Hard HDLT gate 4 — total RF power exceeds bus.
                EvaluateHdltTotalPowerExceedsBus(design, conditions, hard);

                // Advisory HDLT gate 5 — plume divergence excessive (no nozzle).
                EvaluateHdltPlumeDivergenceExcessive(result, advisories);
                // Advisory HDLT gate 6 — ionisation fraction below floor.
                EvaluateHdltIonisationFractionBelowFloor(result, advisories);
                break;

            case ElectricPropulsionEngineKind.Vasimr:
                // Hard VASIMR gate 1 — total power exceeds bus.
                EvaluateVasimrTotalPowerExceedsBus(design, conditions, hard);
                // Hard VASIMR gate 2 — solenoid field out of band.
                EvaluateVasimrSolenoidFieldOutOfBand(design, hard);
                // Hard VASIMR gate 3 — magnetic-nozzle geometry inverted (M < 1).
                EvaluateVasimrMagneticMirrorInverted(result, hard);

                // Advisory VASIMR gate 4 — helicon-to-ICRH ratio out of cluster band.
                EvaluateVasimrHeliconIcrhRatioOutOfBand(design, advisories);
                // Advisory VASIMR gate 5 — ionisation fraction below floor.
                EvaluateVasimrIonisationFractionBelowFloor(result, advisories);
                // Advisory VASIMR gate 6 — nozzle conversion efficiency below floor.
                EvaluateVasimrNozzleConversionLow(result, advisories);
                break;

            default:
                // All declared kinds dispatch through the switch above.
                // Default arm reserved as a tripwire for future enum additions.
                break;
        }

        return new ElectricPropulsionFeasibilityResult(hard, advisories);
    }

    // ---- Hard gates -----------------------------------------------------

    private static void EvaluateHeaterTempExceeded(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        double limit = design.HeaterMaterial switch
        {
            HeaterMaterial.GrainStabilizedPlatinum => HeaterTempLimitPt_K,
            HeaterMaterial.TungstenRhenium         => HeaterTempLimitWRe_K,
            _                                      => HeaterTempLimitPt_K,
        };
        if (result.HeaterTemp_K > limit)
        {
            violations.Add(new FeasibilityViolation(
                "RESISTOJET_HEATER_TEMP_EXCEEDED",
                $"Heater temperature {result.HeaterTemp_K:F1} K exceeds {design.HeaterMaterial} limit {limit:F1} K. "
              + "Catastrophic burnthrough risk; reduce HeaterPower_W or upgrade material.",
                ActualValue: result.HeaterTemp_K,
                Limit:       limit));
        }
    }

    private static void EvaluateRadiationFractionExcessive(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.RadiationLossFraction > RadiationFractionHardLimit)
        {
            violations.Add(new FeasibilityViolation(
                "RESISTOJET_RADIATION_FRACTION_EXCESSIVE",
                $"Radiation losses {result.RadiationLossFraction:F2} of input power exceed {RadiationFractionHardLimit:F2}. "
              + "Heater cannot reach steady state at design power.",
                ActualValue: result.RadiationLossFraction,
                Limit:       RadiationFractionHardLimit));
        }
    }

    private static void EvaluateNozzleUnchoked(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (!result.ChokedFlow)
        {
            violations.Add(new FeasibilityViolation(
                "RESISTOJET_NOZZLE_UNCHOKED",
                $"Nozzle not choked (P_chamber/P_∞ below critical ratio). Sub-critical operation; "
              + "Isp invalid. Increase HeaterPower_W or reduce ambient pressure.",
                ActualValue: 0.0,  // categorical; ChokedFlow is bool
                Limit:       1.0));
        }
    }

    private static void EvaluatePropellantDecomposition(
        ResistojetConditions conditions,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        double decompLimit = RealGasGammaSolver.DecompositionLimit_K(conditions.InletComposition);
        if (result.ChamberTemp_K > decompLimit)
        {
            violations.Add(new FeasibilityViolation(
                "RESISTOJET_PROPELLANT_DECOMPOSITION",
                $"Chamber temperature {result.ChamberTemp_K:F1} K exceeds propellant decomposition limit {decompLimit:F1} K. "
              + "Gas species are no longer the simulated mixture; model output invalid.",
                ActualValue: result.ChamberTemp_K,
                Limit:       decompLimit));
        }
    }

    private static void EvaluateHeatLeakExceedsInput(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.RadiationLossFraction >= 1.0)
        {
            // Total radiative + conductive losses exceed electrical input —
            // no net heating possible; structurally unphysical.
            double q_total = result.RadiationLossFraction * design.HeaterPower_W;
            violations.Add(new FeasibilityViolation(
                "RESISTOJET_HEAT_LEAK_EXCEEDS_INPUT",
                $"Heat losses {q_total:F1} W ≥ input power {design.HeaterPower_W:F1} W. "
              + "Net heating impossible; design is structurally unphysical.",
                ActualValue: q_total,
                Limit:       design.HeaterPower_W));
        }
    }

    // ---- Advisory gates -------------------------------------------------

    private static void EvaluateAreaRatioOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> advisories)
    {
        if (design.NozzleAreaRatio < AreaRatioBandLow)
        {
            advisories.Add(new FeasibilityViolation(
                "RESISTOJET_AREA_RATIO_OUT_OF_BAND",
                $"Area ratio ε = {design.NozzleAreaRatio:F1} below typical resistojet band [{AreaRatioBandLow}, {AreaRatioBandHigh}]. "
              + "Higher ε would improve Isp but is outside flown-hardware envelope.",
                ActualValue: design.NozzleAreaRatio,
                Limit:       AreaRatioBandLow));
        }
        else if (design.NozzleAreaRatio > AreaRatioBandHigh)
        {
            advisories.Add(new FeasibilityViolation(
                "RESISTOJET_AREA_RATIO_OUT_OF_BAND",
                $"Area ratio ε = {design.NozzleAreaRatio:F1} above typical resistojet band [{AreaRatioBandLow}, {AreaRatioBandHigh}]. "
              + "May incur frozen-flow loss; outside flown-hardware envelope.",
                ActualValue: design.NozzleAreaRatio,
                Limit:       AreaRatioBandHigh));
        }
    }

    private static void EvaluateThrustBelowMin(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.Thrust_N < ThrustFloor_N)
        {
            advisories.Add(new FeasibilityViolation(
                "RESISTOJET_THRUST_BELOW_MIN",
                $"Thrust {result.Thrust_N:F4} N below typical mission-floor {ThrustFloor_N:F2} N (station-keeping use).",
                ActualValue: result.Thrust_N,
                Limit:       ThrustFloor_N));
        }
    }

    private static void EvaluateIspBelowFloor(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.IspVacuum_s < IspFloor_s)
        {
            advisories.Add(new FeasibilityViolation(
                "RESISTOJET_ISP_BELOW_FLOOR",
                $"Isp {result.IspVacuum_s:F1} s below {IspFloor_s:F0} s — uncompetitive vs cold-gas thrusters.",
                ActualValue: result.IspVacuum_s,
                Limit:       IspFloor_s));
        }
    }

    private static void EvaluateEfficiencyBelowFloor(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.ThrustEfficiency < EfficiencyFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "RESISTOJET_EFFICIENCY_BELOW_FLOOR",
                $"Thrust efficiency η_T = {result.ThrustEfficiency:F3} below typical resistojet floor {EfficiencyFloor:F2} "
              + "(NASA TM-2002-211314 §3 efficiency-band survey).",
                ActualValue: result.ThrustEfficiency,
                Limit:       EfficiencyFloor));
        }
    }

    private static void EvaluateFrozenFlowLossExcessive(
        ResistojetConditions conditions,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.ChamberTemp_K <= FrozenFlowThreshold_K) return;

        // Frozen-flow loss only meaningful when N or H species are present
        // (NH3, N2, H2 — not pure H2O).
        var c = conditions.InletComposition;
        bool hasNorH = c.NH3MoleFraction > SpeciesMoleFractionFloor
                    || c.N2MoleFraction  > SpeciesMoleFractionFloor
                    || c.H2MoleFraction  > SpeciesMoleFractionFloor;
        if (!hasNorH) return;

        advisories.Add(new FeasibilityViolation(
            "RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE",
            $"Chamber T {result.ChamberTemp_K:F1} K exceeds {FrozenFlowThreshold_K:F0} K with N/H species present. "
          + "Recombination suppressed in sub-mm-throat residence time — 5–15 % Isp loss "
          + "(NASA TM-2002-211314 §4).",
            ActualValue: result.ChamberTemp_K,
            Limit:       FrozenFlowThreshold_K));
    }

    // ---- HET gates (Wave-2, ADR-029 D6) --------------------------------

    /// <summary>
    /// Material-specific anode wall temperature limit [K]. Drives
    /// <c>HET_ANODE_OVERHEAT</c>.
    /// </summary>
    private static double HetAnodeLimitFor(AnodeMaterial material) => material switch
    {
        AnodeMaterial.Graphite     => HetAnodeLimitGraphite_K,
        AnodeMaterial.BoronNitride => HetAnodeLimitBoronNitride_K,
        AnodeMaterial.AluminaSiC   => HetAnodeLimitAluminaSiC_K,
        // None / unknown: pick the most conservative limit so unconfigured
        // designs surface the gate rather than passing silently.
        _                          => HetAnodeLimitBoronNitride_K,
    };

    /// <summary>
    /// Per-cathode rated current ceiling [A]. Drives
    /// <c>HET_CATHODE_LIFE_LIMIT</c>.
    /// </summary>
    private static double HetCathodeRatedFor(CathodeType type) => type switch
    {
        CathodeType.HollowCathode   => HetCathodeRatedHollow_A,
        CathodeType.FilamentCathode => HetCathodeRatedFilament_A,
        _                           => HetCathodeRatedHollow_A,
    };

    private static void EvaluateHetDischargeVoltageOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double Vd = design.DischargeVoltage_V;
        if (Vd < HetDischargeVoltageMin_V || Vd > HetDischargeVoltageMax_V)
        {
            violations.Add(new FeasibilityViolation(
                "HET_DISCHARGE_VOLTAGE_OUT_OF_BAND",
                $"Discharge voltage {Vd:F1} V outside HET operating envelope "
              + $"[{HetDischargeVoltageMin_V:F0}, {HetDischargeVoltageMax_V:F0}] V "
              + "(Goebel & Katz §3.4 cluster + ADR-038 HV-Hall envelope).",
                ActualValue: Vd,
                Limit:       Vd < HetDischargeVoltageMin_V ? HetDischargeVoltageMin_V : HetDischargeVoltageMax_V));
        }
    }

    private static void EvaluateHetAnodeOverheat(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        // Anode wall temp lives on the HET plasma state via the discharge solver;
        // pull it from the result via a typed cast.
        if (result.PlasmaState is not HetPlasmaState) return;

        // The anode wall temperature was computed in the discharge model;
        // the typed plasma-state record doesn't carry it directly because
        // the IPlasmaState interface is intentionally narrow. We re-derive
        // it from the cycle inputs to stay decoupled from the solver
        // internals (alternative: extend HetPlasmaState; not done because
        // anode temp is gate-only data).
        double Pd  = design.DischargeVoltage_V * design.DischargeCurrent_A;
        double Pa  = BuschDischargeModel.AnodeLossFraction * Pd;
        double Aa  = 2.0 * System.Math.PI * (design.AnodeRadius_mm * 1e-3) * (design.ChannelLength_mm * 1e-3);
        double rad = BuschDischargeModel.AnodeEmissivity * BuschDischargeModel.Sigma_SB * Aa;
        if (rad <= 0) return;
        double T_vac = BuschDischargeModel.T_Vacuum_K;
        double T4  = (Pa / rad) + T_vac * T_vac * T_vac * T_vac;
        double Tw  = System.Math.Sqrt(System.Math.Sqrt(T4));

        double limit = HetAnodeLimitFor(design.AnodeMaterial);
        if (Tw > limit)
        {
            violations.Add(new FeasibilityViolation(
                "HET_ANODE_OVERHEAT",
                $"Anode wall {Tw:F0} K exceeds {design.AnodeMaterial} sustained limit {limit:F0} K. "
              + "Reduce DischargeCurrent_A, increase channel area, or upgrade material "
              + "(Goebel & Katz §3.5).",
                ActualValue: Tw,
                Limit:       limit));
        }
    }

    private static void EvaluateHetMagneticFieldInsufficient(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double B = design.MagneticField_T;
        if (B < HetMagneticFieldMin_T)
        {
            violations.Add(new FeasibilityViolation(
                "HET_MAGNETIC_FIELD_INSUFFICIENT",
                $"Magnetic field {B:F4} T below {HetMagneticFieldMin_T:F4} T floor — "
              + "Hall parameter ω_e·τ_en < 100, electrons un-confined, discharge "
              + "collapses to arcjet-style distributed plasma (Goebel & Katz §3.6).",
                ActualValue: B,
                Limit:       HetMagneticFieldMin_T));
        }
    }

    private static void EvaluateHetPlumeDivergenceExcessive(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not HetPlasmaState plasma) return;
        if (plasma.PlumeDivergenceHalfAngle_rad > HetPlumeDivergenceAdvisoryLimit_rad)
        {
            double thetaDeg = plasma.PlumeDivergenceHalfAngle_rad * 180.0 / System.Math.PI;
            double cosineLossPct = (1.0 - System.Math.Cos(plasma.PlumeDivergenceHalfAngle_rad)) * 100.0;
            advisories.Add(new FeasibilityViolation(
                "HET_PLUME_DIVERGENCE_EXCESSIVE",
                $"Plume half-angle {thetaDeg:F1}° exceeds 30° advisory; "
              + $"cosine thrust-loss ≈ {cosineLossPct:F1} %. Increase B-field "
              + "or shorten channel.",
                ActualValue: plasma.PlumeDivergenceHalfAngle_rad,
                Limit:       HetPlumeDivergenceAdvisoryLimit_rad));
        }
    }

    private static void EvaluateHetCathodeLifeLimit(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> advisories)
    {
        double rated = HetCathodeRatedFor(design.CathodeType);
        double advisoryCeiling = HetCathodeLifeLimitFactor * rated;
        if (design.DischargeCurrent_A > advisoryCeiling)
        {
            advisories.Add(new FeasibilityViolation(
                "HET_CATHODE_LIFE_LIMIT",
                $"Discharge current {design.DischargeCurrent_A:F1} A exceeds "
              + $"{HetCathodeLifeLimitFactor:F1}× rated {rated:F1} A for "
              + $"{design.CathodeType}. Cathode-life curve inflection — expect "
              + "<1000 h life (Goebel & Katz §6.2).",
                ActualValue: design.DischargeCurrent_A,
                Limit:       advisoryCeiling));
        }
    }

    private static void EvaluateHetMassUtilizationLow(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not HetPlasmaState plasma) return;
        if (plasma.MassUtilization < HetMassUtilizationFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "HET_MASS_UTILIZATION_LOW",
                $"Mass utilisation η_m = {plasma.MassUtilization:F3} below "
              + $"{HetMassUtilizationFloor:F2} floor. Under-ionised plasma — "
              + "raise discharge current or reduce ṁ_xe (Goebel & Katz §3.5).",
                ActualValue: plasma.MassUtilization,
                Limit:       HetMassUtilizationFloor));
        }
    }

    // ---- Arcjet gates (Sprint EP.W2.AJ, Sutton 9e §16.3) ──────────────

    /// <summary>
    /// Material-specific anode wall temperature limit [K]. Drives
    /// <c>ARCJET_ANODE_OVERHEAT</c>.
    /// </summary>
    private static double ArcjetAnodeLimitFor(ArcjetElectrodeMaterial material) => material switch
    {
        ArcjetElectrodeMaterial.Tungsten   => ArcjetAnodeLimitTungsten_K,
        ArcjetElectrodeMaterial.Molybdenum => ArcjetAnodeLimitMolybdenum_K,
        ArcjetElectrodeMaterial.Rhenium    => ArcjetAnodeLimitRhenium_K,
        // None / unknown: pick the most conservative limit so unconfigured
        // designs surface the gate rather than passing silently.
        _                                  => ArcjetAnodeLimitMolybdenum_K,
    };

    private static void EvaluateArcjetVoltageOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double Va = design.ArcVoltage_V;
        if (Va < ArcjetVoltageMin_V || Va > ArcjetVoltageMax_V)
        {
            violations.Add(new FeasibilityViolation(
                "ARCJET_VOLTAGE_OUT_OF_BAND",
                $"Arc voltage {Va:F1} V outside arcjet operating envelope "
              + $"[{ArcjetVoltageMin_V:F0}, {ArcjetVoltageMax_V:F0}] V "
              + "(Sutton & Biblarz 9e §16.3 cluster envelope).",
                ActualValue: Va,
                Limit:       Va < ArcjetVoltageMin_V ? ArcjetVoltageMin_V : ArcjetVoltageMax_V));
        }
    }

    private static void EvaluateArcjetAnodeOverheat(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.PlasmaState is not ArcjetPlasmaState plasma) return;

        double Tw = plasma.AnodeWallTemp_K;
        double limit = ArcjetAnodeLimitFor(design.ArcjetElectrodeMaterial);
        if (Tw > limit)
        {
            violations.Add(new FeasibilityViolation(
                "ARCJET_ANODE_OVERHEAT",
                $"Anode wall {Tw:F0} K exceeds {design.ArcjetElectrodeMaterial} sustained limit {limit:F0} K. "
              + "Reduce ArcCurrent_A, increase chamber wall area, or upgrade material "
              + "(Sutton & Biblarz 9e §16.3).",
                ActualValue: Tw,
                Limit:       limit));
        }
    }

    private static void EvaluateArcjetThermalEfficiencyLow(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not ArcjetPlasmaState plasma) return;
        if (plasma.ThermalEfficiency < ArcjetThermalEfficiencyFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "ARCJET_THERMAL_EFFICIENCY_LOW",
                $"Thermal efficiency η_thermal = {plasma.ThermalEfficiency:F3} below "
              + $"{ArcjetThermalEfficiencyFloor:F2} floor. Wall losses dominate — "
              + "consider longer arc gap or smaller chamber area (Sutton §16.3 cluster: 0.30–0.50).",
                ActualValue: plasma.ThermalEfficiency,
                Limit:       ArcjetThermalEfficiencyFloor));
        }
    }

    private static void EvaluateArcjetFrozenFlowLossExcessive(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        // Use the AnodeWallTemp_K as a chamber-temperature proxy — the gas
        // sees temperatures in the same order of magnitude in steady state.
        // ChamberTemp_K on the result is set to AnodeWallTemp_K for arcjet.
        if (result.ChamberTemp_K <= ArcjetFrozenFlowThreshold_K) return;

        advisories.Add(new FeasibilityViolation(
            "ARCJET_FROZEN_FLOW_LOSS_EXCESSIVE",
            $"Chamber T {result.ChamberTemp_K:F1} K exceeds {ArcjetFrozenFlowThreshold_K:F0} K — "
          + "dissociation of N/H species suppresses recombination at sub-mm-throat residence times. "
          + "Expect 5–15 % Isp loss (NASA TM-2002-211314 §4).",
            ActualValue: result.ChamberTemp_K,
            Limit:       ArcjetFrozenFlowThreshold_K));
    }

    // ---- PPT gates (Sprint EP.W2.PPT, Solbes-Vondra cluster) ──────────

    private static void EvaluatePptCapacitorEnergyOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double E = design.CapacitorEnergy_J;
        if (E < PptCapacitorEnergyMin_J || E > PptCapacitorEnergyMax_J)
        {
            violations.Add(new FeasibilityViolation(
                "PPT_CAPACITOR_ENERGY_OUT_OF_BAND",
                $"Capacitor energy {E:F2} J outside PPT operating envelope "
              + $"[{PptCapacitorEnergyMin_J:F1}, {PptCapacitorEnergyMax_J:F0}] J "
              + "(Solbes-Vondra cluster envelope).",
                ActualValue: E,
                Limit:       E < PptCapacitorEnergyMin_J ? PptCapacitorEnergyMin_J : PptCapacitorEnergyMax_J));
        }
    }

    private static void EvaluatePptNoBreakdown(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double E = design.CapacitorEnergy_J;
        if (E < PptBreakdownEnergyMin_J)
        {
            violations.Add(new FeasibilityViolation(
                "PPT_NO_BREAKDOWN",
                $"Capacitor energy {E:F2} J below {PptBreakdownEnergyMin_J:F1} J breakdown threshold — "
              + "stable arc channel does not form between rails; physics-model output invalid "
              + "(Vondra & Thomassen 1974).",
                ActualValue: E,
                Limit:       PptBreakdownEnergyMin_J));
        }
    }

    private static void EvaluatePptImpulseBitBelowFloor(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.PptPlasmaState plasma) return;
        if (plasma.ImpulseBit_Ns < PptImpulseBitFloor_Ns)
        {
            advisories.Add(new FeasibilityViolation(
                "PPT_IMPULSE_BIT_BELOW_FLOOR",
                $"Impulse bit {plasma.ImpulseBit_Ns * 1e6:F1} µN·s below {PptImpulseBitFloor_Ns * 1e6:F0} µN·s "
              + "CubeSat-class mission floor — cold-gas thrusters become competitive at lower complexity.",
                ActualValue: plasma.ImpulseBit_Ns,
                Limit:       PptImpulseBitFloor_Ns));
        }
    }

    private static void EvaluatePptAblationRateExcessive(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.PptPlasmaState plasma) return;
        if (plasma.MassPerPulse_kg > PptAblationRateMax_kgPerPulse)
        {
            advisories.Add(new FeasibilityViolation(
                "PPT_ABLATION_RATE_EXCESSIVE",
                $"Mass-per-pulse {plasma.MassPerPulse_kg * 1e9:F1} µg exceeds {PptAblationRateMax_kgPerPulse * 1e9:F0} µg "
              + "ceiling — PTFE bar lifetime drops below mission-typical 100 hr continuous operation. "
              + "Reduce CapacitorEnergy_J or upgrade to a longer propellant bar.",
                ActualValue: plasma.MassPerPulse_kg,
                Limit:       PptAblationRateMax_kgPerPulse));
        }
    }

    // ---- GIT gates (Sprint EP.W2.GIT, Goebel & Katz §5) ────────────────

    private static void EvaluateGitBeamVoltageOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double V = design.BeamVoltage_V;
        if (V < GitBeamVoltageMin_V || V > GitBeamVoltageMax_V)
        {
            violations.Add(new FeasibilityViolation(
                "GIT_BEAM_VOLTAGE_OUT_OF_BAND",
                $"Beam voltage {V:F1} V outside gridded-ion operating envelope "
              + $"[{GitBeamVoltageMin_V:F0}, {GitBeamVoltageMax_V:F0}] V "
              + "(Goebel & Katz §5 cluster + ADR-038 HV-GIT envelope; NSTAR anchors 1100 V).",
                ActualValue: V,
                Limit:       V < GitBeamVoltageMin_V ? GitBeamVoltageMin_V : GitBeamVoltageMax_V));
        }
    }

    private static void EvaluateGitPerveanceLimitExceeded(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.PlasmaState is not Plasma.IonPlasmaState plasma) return;
        // The model already clamps BeamCurrent_A at the Child-Langmuir limit
        // for the physics path; the gate fires when the design requested more
        // than the geometry can deliver (request > limit).
        double requested = result.Design.BeamCurrent_A;
        double limit     = plasma.ChildLangmuirLimit_A;
        if (!double.IsFinite(limit) || limit <= 0) return;
        if (requested > limit)
        {
            violations.Add(new FeasibilityViolation(
                "GIT_PERVEANCE_LIMIT_EXCEEDED",
                $"Requested beam current {requested:F2} A exceeds Child-Langmuir "
              + $"saturation limit {limit:F2} A for the screen-grid geometry. "
              + "Beam extraction saturates; increase grid radius, reduce grid gap, "
              + "or raise BeamVoltage_V (Goebel & Katz §5.2).",
                ActualValue: requested,
                Limit:       limit));
        }
    }

    private static void EvaluateGitNeutralizerCurrentMismatch(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.PlasmaState is not Plasma.IonPlasmaState plasma) return;
        double Jb   = plasma.BeamCurrent_A;
        double Jn   = design.NeutralizerCathodeCurrent_A;
        if (Jb <= 0) return;
        double frac = System.Math.Abs(Jn - Jb) / Jb;
        if (frac > GitNeutralizerMatchingTolerance)
        {
            violations.Add(new FeasibilityViolation(
                "GIT_NEUTRALIZER_CURRENT_MISMATCH",
                $"Neutraliser current {Jn:F3} A differs from beam current {Jb:F3} A "
              + $"by {frac * 100.0:F1} % (limit {GitNeutralizerMatchingTolerance * 100.0:F0} %). "
              + "Spacecraft charge build-up; match J_neutralizer to J_beam "
              + "(Goebel & Katz §5.5).",
                ActualValue: frac,
                Limit:       GitNeutralizerMatchingTolerance));
        }
    }

    private static void EvaluateGitPlumeDivergenceExcessive(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.IonPlasmaState plasma) return;
        if (plasma.PlumeDivergenceHalfAngle_rad > GitPlumeDivergenceAdvisoryLimit_rad)
        {
            double thetaDeg = plasma.PlumeDivergenceHalfAngle_rad * 180.0 / System.Math.PI;
            double cosineLossPct = (1.0 - System.Math.Cos(plasma.PlumeDivergenceHalfAngle_rad)) * 100.0;
            advisories.Add(new FeasibilityViolation(
                "GIT_PLUME_DIVERGENCE_EXCESSIVE",
                $"Plume half-angle {thetaDeg:F1}° exceeds 30° advisory; "
              + $"cosine thrust-loss ≈ {cosineLossPct:F1} %. Tighten grid alignment or "
              + "increase BeamVoltage_V (Goebel & Katz §5.3).",
                ActualValue: plasma.PlumeDivergenceHalfAngle_rad,
                Limit:       GitPlumeDivergenceAdvisoryLimit_rad));
        }
    }

    private static void EvaluateGitGridLifetimeBelowFloor(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.IonPlasmaState plasma) return;
        // Coarse sputter-erosion proxy at this fidelity:
        //   t_life ≈ K_life · d_gap [mm] / J_beam [A]
        // K_life chosen so NSTAR (d=0.6 mm, J=1.76 A) lands ~30 000 h — the
        // demonstrated DS1 / Dawn lifetime. Designs with anomalously small
        // gaps or high beam currents surface below 1000 h.
        const double K_life_hours = 88_000.0;  // (d_mm · A · hr) calibration constant
        double Jb = plasma.BeamCurrent_A;
        if (Jb <= 0) return;
        double t_life_hr = K_life_hours * design.AccelGridGap_mm / Jb;
        if (t_life_hr < GitGridLifetimeAdvisoryFloor_hr)
        {
            advisories.Add(new FeasibilityViolation(
                "GIT_GRID_LIFETIME_BELOW_FLOOR",
                $"Estimated grid lifetime {t_life_hr:F0} h below {GitGridLifetimeAdvisoryFloor_hr:F0} h "
              + "advisory floor (sputter-erosion proxy: K · d_gap / J_beam). "
              + "Widen AccelGridGap_mm or lower BeamCurrent_A (Goebel & Katz §5.6).",
                ActualValue: t_life_hr,
                Limit:       GitGridLifetimeAdvisoryFloor_hr));
        }
    }

    // ---- MPD gates (Sprint EP.W2.MPD, Polk 1991 / Sovey 1990 / Choueiri 1998) ─

    /// <summary>
    /// Material-specific cathode-tip temperature limit [K]. Drives
    /// <c>MPD_CATHODE_OVERHEAT</c>.
    /// </summary>
    private static double MpdCathodeLimitFor(MpdCathodeMaterial material) => material switch
    {
        MpdCathodeMaterial.Tungsten            => MpdCathodeLimitTungsten_K,
        MpdCathodeMaterial.ThoriatedTungsten   => MpdCathodeLimitThoriatedTungsten_K,
        MpdCathodeMaterial.LanthanumHexaboride => MpdCathodeLimitLaB6_K,
        // None / unknown: pick the most-conservative limit so unconfigured
        // designs surface the gate rather than passing silently.
        _                                      => MpdCathodeLimitLaB6_K,
    };

    private static void EvaluateMpdArcCurrentOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double J = design.MpdArcCurrent_A;
        if (J < MpdArcCurrentMin_A || J > MpdArcCurrentMax_A)
        {
            violations.Add(new FeasibilityViolation(
                "MPD_ARC_CURRENT_OUT_OF_BAND",
                $"Arc current {J:F1} A outside MPD operating envelope "
              + $"[{MpdArcCurrentMin_A:F0}, {MpdArcCurrentMax_A:F0}] A "
              + "(self-field cluster envelope; below 200 A the J²-thrust dependency "
              + "produces sub-mN thrust, above 10 kA anode-spotting becomes pathological).",
                ActualValue: J,
                Limit:       J < MpdArcCurrentMin_A ? MpdArcCurrentMin_A : MpdArcCurrentMax_A));
        }
    }

    private static void EvaluateMpdCathodeOverheat(
        ElectricPropulsionEngineDesign design,
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.PlasmaState is not Plasma.MpdPlasmaState plasma) return;
        double Tc    = plasma.CathodeWallTemp_K;
        double limit = MpdCathodeLimitFor(design.MpdCathodeMaterial);
        if (Tc > limit)
        {
            violations.Add(new FeasibilityViolation(
                "MPD_CATHODE_OVERHEAT",
                $"Cathode tip {Tc:F0} K exceeds {design.MpdCathodeMaterial} sustained limit {limit:F0} K. "
              + "Reduce MpdArcCurrent_A, increase MpdCathodeRadius_mm, or upgrade material "
              + "(Polk 1991 LiLFA cathode-erosion campaign).",
                ActualValue: Tc,
                Limit:       limit));
        }
    }

    private static void EvaluateMpdGeometryInverted(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        // The Maecker formula b = (μ₀/4π) · (ln(r_a/r_c) + 3/4) requires
        // r_a > r_c. The solver throws ArgumentOutOfRangeException for the
        // inverted case, so this gate provides the friendlier surface for
        // design-space exploration where SA might wander into r_a ≤ r_c.
        if (design.MpdAnodeRadius_mm <= design.MpdCathodeRadius_mm)
        {
            violations.Add(new FeasibilityViolation(
                "MPD_GEOMETRY_INVERTED",
                $"Anode inner radius {design.MpdAnodeRadius_mm:F1} mm not greater than "
              + $"cathode outer radius {design.MpdCathodeRadius_mm:F1} mm. "
              + "Maecker formula b = (μ₀/4π)·(ln(r_a/r_c)+3/4) requires a finite "
              + "annular gap; flip the geometry or widen the anode.",
                ActualValue: design.MpdAnodeRadius_mm,
                Limit:       design.MpdCathodeRadius_mm));
        }
    }

    private static void EvaluateMpdOnsetParameterExcessive(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> advisories)
    {
        // Onset parameter ξ = J_kA² / ṁ_g/s. Cluster onset ξ_onset ≈ 100
        // (Choueiri 1998); designs above 80 % of it surface the advisory so
        // the user knows they're in the anode-spotting / hot-spot transition
        // regime.
        double Jka = design.MpdArcCurrent_A * 1e-3;
        double mDot_kgs = design.PropellantMassFlow_kgs;
        if (mDot_kgs <= 0) return;
        double mDot_gs = mDot_kgs * 1000.0;
        double xi = Jka * Jka / mDot_gs;
        double advisoryCeiling = 0.80 * MpdOnsetParameterAdvisoryLimit_kA2PerGs;
        if (xi > advisoryCeiling)
        {
            advisories.Add(new FeasibilityViolation(
                "MPD_ONSET_PARAMETER_EXCESSIVE",
                $"Onset parameter ξ = J_kA²/ṁ_g/s = {xi:F1} above {advisoryCeiling:F1} "
              + $"(80 % of cluster onset {MpdOnsetParameterAdvisoryLimit_kA2PerGs:F0}). "
              + "Anode-spotting / hot-spot transition risk; raise PropellantMassFlow_kgs "
              + "or lower MpdArcCurrent_A (Choueiri 1998 onset criterion).",
                ActualValue: xi,
                Limit:       advisoryCeiling));
        }
    }

    private static void EvaluateMpdThrustEfficiencyLow(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.MpdPlasmaState plasma) return;
        if (plasma.ThrustEfficiency_Maecker < MpdThrustEfficiencyFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "MPD_THRUST_EFFICIENCY_LOW",
                $"Maecker thrust efficiency η_T = {plasma.ThrustEfficiency_Maecker:F3} "
              + $"below {MpdThrustEfficiencyFloor:F2} floor. Self-field MPD cluster "
              + "lands 0.10–0.30 (Polk 1991); below 0.05 the geometry chokes the "
              + "J×B coupling. Widen anode-cathode ratio or lower mass flow.",
                ActualValue: plasma.ThrustEfficiency_Maecker,
                Limit:       MpdThrustEfficiencyFloor));
        }
    }

    // ---- Applied-Field MPD gates (Sprint EP.W3.AF, Sankaran 2004) ─────

    /// <summary>Hard gate: B_applied outside the [0.05, 0.50] T band.</summary>
    private static void EvaluateMpdAppliedFieldOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> hard)
    {
        // NaN sentinel → applied-field disabled. Self-field-only MPD is the
        // Wave-2 path; the gate only fires when B is finite (the user
        // explicitly opted into Wave-3 augmentation).
        double B = design.MpdAppliedFieldStrength_T;
        if (double.IsNaN(B)) return;

        // Zero (or numerically zero) is treated identically to NaN — the
        // solver disables augmentation. No need to fire the band gate.
        if (B < Solvers.SelfFieldLorentzModel.AppliedFieldNumericFloor_T) return;

        if (B < MpdAppliedFieldMin_T || B > MpdAppliedFieldMax_T)
        {
            hard.Add(new FeasibilityViolation(
                "MPD_APPLIED_FIELD_OUT_OF_BAND",
                $"Applied-field B_z = {B:F3} T outside the [{MpdAppliedFieldMin_T:F2}, "
              + $"{MpdAppliedFieldMax_T:F2}] T cluster band (Sprint EP.W3.AF). LiLFA Polk "
              + "1991 anchor 0.10 T; Princeton X9 0.20 T. Outside this band the "
              + "Sankaran-2004 linear fit is not calibrated — either set B to NaN to "
              + "disable applied-field augmentation, or place B inside the band.",
                ActualValue: B,
                Limit:       B < MpdAppliedFieldMin_T ? MpdAppliedFieldMin_T : MpdAppliedFieldMax_T));
        }
    }

    /// <summary>
    /// Advisory: applied-field thrust contribution dominates over the bare-
    /// Maecker self-field component. T_af / (T_self + T_af) above 0.80 sits
    /// outside the cluster-band fixture coverage; the bare-Maecker term is
    /// no longer the dominant acceleration mechanism.
    /// </summary>
    private static void EvaluateMpdAppliedFieldDominates(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.MpdPlasmaState plasma) return;
        if (plasma.AppliedFieldStrength_T <= 0) return;

        double total = plasma.SelfFieldThrust_N + plasma.AppliedFieldThrust_N;
        if (total <= 0) return;
        double afFraction = plasma.AppliedFieldThrust_N / total;

        if (afFraction > MpdAppliedFieldDominanceCeiling)
        {
            advisories.Add(new FeasibilityViolation(
                "MPD_APPLIED_FIELD_DOMINATES",
                $"Applied-field thrust fraction T_af / T_total = {afFraction:F2} above "
              + $"{MpdAppliedFieldDominanceCeiling:F2} ceiling. The bare-Maecker self-field "
              + "physics no longer dominates; the Sankaran-2004 linear fit sits outside its "
              + "calibrated range. Either reduce B_z, lower r_a, or raise the arc current.",
                ActualValue: afFraction,
                Limit:       MpdAppliedFieldDominanceCeiling));
        }
    }

    // ── FEEP gates (Sprint EP.W5 phase 2) ────────────────────────────────

    private static void EvaluateFeepAcceleratingVoltageOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double V = design.FeepAcceleratingVoltage_V;
        if (V < FeepAcceleratingVoltageMin_V || V > FeepAcceleratingVoltageMax_V)
        {
            violations.Add(new FeasibilityViolation(
                "FEEP_ACCELERATING_VOLTAGE_OUT_OF_BAND",
                $"FEEP V_acc {V:F0} V outside operating envelope "
              + $"[{FeepAcceleratingVoltageMin_V:F0}, {FeepAcceleratingVoltageMax_V:F0}] V "
              + "(Mair-Lozano cluster; below 5 kV the FN-emission threshold isn't reliably "
              + "crossed, above 12 kV extractor breakdown becomes pathological).",
                ActualValue: V,
                Limit:       V < FeepAcceleratingVoltageMin_V
                                ? FeepAcceleratingVoltageMin_V
                                : FeepAcceleratingVoltageMax_V));
        }
    }

    private static void EvaluateFeepEmitterTipRadiusOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double r = design.FeepEmitterTipRadius_mm;
        if (r < FeepEmitterTipRadiusMin_mm || r > FeepEmitterTipRadiusMax_mm)
        {
            violations.Add(new FeasibilityViolation(
                "FEEP_EMITTER_TIP_RADIUS_OUT_OF_BAND",
                $"FEEP r_tip {r:F4} mm outside operating envelope "
              + $"[{FeepEmitterTipRadiusMin_mm:F4}, {FeepEmitterTipRadiusMax_mm:F4}] mm "
              + "(below 1 μm the tip is at the manufacturing floor / EM-current density "
              + "blows up; above 50 μm the tip field collapses below FN threshold at "
              + "cluster V_acc).",
                ActualValue: r,
                Limit:       r < FeepEmitterTipRadiusMin_mm
                                ? FeepEmitterTipRadiusMin_mm
                                : FeepEmitterTipRadiusMax_mm));
        }
    }

    private static void EvaluateFeepBeamCurrentOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double I = design.FeepBeamCurrent_A;
        if (I < FeepBeamCurrentMin_A || I > FeepBeamCurrentMax_A)
        {
            violations.Add(new FeasibilityViolation(
                "FEEP_BEAM_CURRENT_OUT_OF_BAND",
                $"FEEP I_beam {I:E2} A outside per-tip envelope "
              + $"[{FeepBeamCurrentMin_A:E2}, {FeepBeamCurrentMax_A:E2}] A "
              + "(below 1 μA thrust is sub-mission, above 1 mA single-tip overheats — "
              + "model larger systems as emitter arrays via multi-tip scaling).",
                ActualValue: I,
                Limit:       I < FeepBeamCurrentMin_A
                                ? FeepBeamCurrentMin_A
                                : FeepBeamCurrentMax_A));
        }
    }

    private static void EvaluateFeepTotalPowerExceedsBus(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        List<FeasibilityViolation> violations)
    {
        double P_req = design.FeepAcceleratingVoltage_V * design.FeepBeamCurrent_A;
        double P_avail = conditions.BusPower_W_avail;
        if (P_avail > 0 && P_req > P_avail)
        {
            violations.Add(new FeasibilityViolation(
                "FEEP_TOTAL_POWER_EXCEEDS_BUS",
                $"FEEP required power V_acc·I_beam = {P_req:F2} W exceeds BusPower_W_avail "
              + $"{P_avail:F2} W. Reduce FeepAcceleratingVoltage_V or FeepBeamCurrent_A, "
              + "or upsize the bus.",
                ActualValue: P_req,
                Limit:       P_avail));
        }
    }

    private static void EvaluateFeepTipFieldBelowFnThreshold(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.FeepPlasmaState plasma) return;
        double E = plasma.EmitterTipField_VperM;
        if (E < FeepFnThresholdField_VperM)
        {
            advisories.Add(new FeasibilityViolation(
                "FEEP_TIP_FIELD_BELOW_FN_THRESHOLD",
                $"Tip field E_tip = {E:E2} V/m below Fowler-Nordheim threshold "
              + $"{FeepFnThresholdField_VperM:E2} V/m. Real emitter would produce far "
              + "less current than FeepBeamCurrent_A specifies; sharpen the tip "
              + "(reduce FeepEmitterTipRadius_mm) or raise FeepAcceleratingVoltage_V "
              + "to push E_tip above 1×10⁹ V/m.",
                ActualValue: E,
                Limit:       FeepFnThresholdField_VperM));
        }
    }

    private static void EvaluateFeepThrustBelowFloor(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        double T = result.Thrust_N;
        if (T > 0 && T < FeepThrustAdvisoryFloor_N)
        {
            advisories.Add(new FeasibilityViolation(
                "FEEP_THRUST_BELOW_FLOOR",
                $"Thrust {T:E2} N below FEEP mission floor "
              + $"{FeepThrustAdvisoryFloor_N:E2} N. Most FEEP applications "
              + "(attitude control, formation flying) require ≥ 1 μN per thruster. "
              + "Raise V_acc or I_beam.",
                ActualValue: T,
                Limit:       FeepThrustAdvisoryFloor_N));
        }
    }

    // ── HDLT gates (Sprint EP.W6 phase 2) ────────────────────────────────

    private static void EvaluateHdltRfPowerBelowHeliconThreshold(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double P = design.HdltHeliconRfPower_W;
        if (P < HdltHeliconModeFloor_W)
        {
            violations.Add(new FeasibilityViolation(
                "HDLT_RF_POWER_BELOW_IONIZATION_THRESHOLD",
                $"Helicon RF power {P:F1} W below helicon-mode threshold "
              + $"{HdltHeliconModeFloor_W:F0} W. Source collapses to inductive / "
              + "capacitive mode (Chen 1991); CFDL fails to form. Raise "
              + "HdltHeliconRfPower_W above the threshold.",
                ActualValue: P,
                Limit:       HdltHeliconModeFloor_W));
        }
    }

    private static void EvaluateHdltDoubleLayerTooWeak(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.PlasmaState is not Plasma.HdltPlasmaState plasma) return;
        double dV = plasma.DoubleLayerStrength_V;
        if (dV < HdltDoubleLayerMinStrength_V)
        {
            violations.Add(new FeasibilityViolation(
                "HDLT_DOUBLE_LAYER_TOO_WEAK",
                $"Double-layer strength ΔV = {dV:F2} V below minimum "
              + $"{HdltDoubleLayerMinStrength_V:F0} V for useful thrust "
              + "(Charles 2007). Increase HdltMagneticFieldGradient_TpM or "
              + "HdltChannelLength_mm to raise ln(B_ratio).",
                ActualValue: dV,
                Limit:       HdltDoubleLayerMinStrength_V));
        }
    }

    private static void EvaluateHdltChannelGeometryInsufficient(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double L_m = design.HdltChannelLength_mm * 1.0e-3;
        double integrated = design.HdltMagneticFieldGradient_TpM * L_m;
        if (integrated < HdltMinIntegratedGradient_T)
        {
            violations.Add(new FeasibilityViolation(
                "HDLT_CHANNEL_GEOMETRY_INSUFFICIENT",
                $"Integrated B-gradient ∇B·L = {integrated:F3} T below threshold "
              + $"{HdltMinIntegratedGradient_T:F2} T. Flux-tube expansion across the "
              + "channel is too gentle for a CFDL to self-organise (Plihon 2007). "
              + "Sharpen HdltMagneticFieldGradient_TpM or extend HdltChannelLength_mm.",
                ActualValue: integrated,
                Limit:       HdltMinIntegratedGradient_T));
        }
    }

    private static void EvaluateHdltTotalPowerExceedsBus(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        List<FeasibilityViolation> violations)
    {
        double P_req = design.HdltHeliconRfPower_W;
        double P_avail = conditions.BusPower_W_avail;
        if (P_avail > 0 && P_req > P_avail)
        {
            violations.Add(new FeasibilityViolation(
                "HDLT_TOTAL_POWER_EXCEEDS_BUS",
                $"HDLT required RF power {P_req:F2} W exceeds BusPower_W_avail "
              + $"{P_avail:F2} W. Reduce HdltHeliconRfPower_W or upsize the bus.",
                ActualValue: P_req,
                Limit:       P_avail));
        }
    }

    private static void EvaluateHdltPlumeDivergenceExcessive(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.HdltPlasmaState plasma) return;
        double theta = plasma.PlumeDivergenceHalfAngle_rad;
        if (theta > HdltPlumeDivergenceAdvisoryCeiling_rad)
        {
            advisories.Add(new FeasibilityViolation(
                "HDLT_PLUME_DIVERGENCE_EXCESSIVE",
                $"Plume half-angle θ = {theta:F3} rad ({theta * 180.0 / Math.PI:F1}°) "
              + $"above advisory ceiling {HdltPlumeDivergenceAdvisoryCeiling_rad:F3} rad "
              + "(no downstream magnetic-nozzle collimation). Add a B-field tail "
              + "section to tighten the plume.",
                ActualValue: theta,
                Limit:       HdltPlumeDivergenceAdvisoryCeiling_rad));
        }
    }

    private static void EvaluateHdltIonisationFractionBelowFloor(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.HdltPlasmaState plasma) return;
        double eta_i = plasma.IonisationFraction;
        if (eta_i < HdltIonisationFractionFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "HDLT_IONIZATION_FRACTION_LOW",
                $"Ionisation fraction η_i = {eta_i:F4} below floor "
              + $"{HdltIonisationFractionFloor:F2}. RF power undersized for the "
              + "argon flow; most of the propellant exits at thermal velocity and "
              + "contributes negligibly to thrust. Either raise HdltHeliconRfPower_W "
              + "or lower HdltArgonMassFlow_kgs.",
                ActualValue: eta_i,
                Limit:       HdltIonisationFractionFloor));
        }
    }

    // ── VASIMR gates (Sprint EP.W4 phase 2) ──────────────────────────────

    private static void EvaluateVasimrTotalPowerExceedsBus(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions conditions,
        List<FeasibilityViolation> violations)
    {
        double P_req = design.VasimrHeliconRfPower_W + design.VasimrIcrhRfPower_W;
        double P_avail = conditions.BusPower_W_avail;
        if (P_avail > 0 && P_req > P_avail)
        {
            violations.Add(new FeasibilityViolation(
                "VASIMR_TOTAL_POWER_EXCEEDS_BUS",
                $"VASIMR total RF power P_helicon + P_icrh = {P_req:F0} W exceeds "
              + $"BusPower_W_avail {P_avail:F0} W. Reduce VasimrHeliconRfPower_W or "
              + "VasimrIcrhRfPower_W, or upsize the bus.",
                ActualValue: P_req,
                Limit:       P_avail));
        }
    }

    private static void EvaluateVasimrSolenoidFieldOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> violations)
    {
        double B = design.VasimrSolenoidField_T;
        if (B < VasimrSolenoidFieldMin_T || B > VasimrSolenoidFieldMax_T)
        {
            violations.Add(new FeasibilityViolation(
                "VASIMR_SOLENOID_FIELD_OUT_OF_BAND",
                $"VASIMR solenoid field B_z = {B:F2} T outside cluster envelope "
              + $"[{VasimrSolenoidFieldMin_T:F1}, {VasimrSolenoidFieldMax_T:F1}] T "
              + "(below 0.3 T the magnetic mirror is too gentle for adequate nozzle "
              + "conversion; above 6 T crosses superconducting-magnet limits and "
              + "ICRH-frequency tunability constraints).",
                ActualValue: B,
                Limit:       B < VasimrSolenoidFieldMin_T
                                ? VasimrSolenoidFieldMin_T
                                : VasimrSolenoidFieldMax_T));
        }
    }

    private static void EvaluateVasimrMagneticMirrorInverted(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> violations)
    {
        if (result.PlasmaState is not Plasma.VasimrPlasmaState plasma) return;
        double M = plasma.MagneticMirrorRatio;
        if (M < 1.0)
        {
            violations.Add(new FeasibilityViolation(
                "VASIMR_MAGNETIC_MIRROR_INVERTED",
                $"Magnetic mirror ratio M = {M:F2} below 1.0. The nozzle expansion "
              + "would compress the flux tube instead of expanding it — η_nozzle "
              + "would be negative. Raise VasimrSolenoidField_T or "
              + "VasimrNozzleExitRadius_mm.",
                ActualValue: M,
                Limit:       1.0));
        }
    }

    private static void EvaluateVasimrHeliconIcrhRatioOutOfBand(
        ElectricPropulsionEngineDesign design,
        List<FeasibilityViolation> advisories)
    {
        double total = design.VasimrHeliconRfPower_W + design.VasimrIcrhRfPower_W;
        if (total <= 0) return;
        double ratio = design.VasimrHeliconRfPower_W / total;
        if (ratio < VasimrHeliconRatioMin || ratio > VasimrHeliconRatioMax)
        {
            advisories.Add(new FeasibilityViolation(
                "VASIMR_HELICON_TO_ICRH_RATIO_OUT_OF_BAND",
                $"Helicon power fraction P_h/P_total = {ratio:F3} outside cluster "
              + $"envelope [{VasimrHeliconRatioMin:F2}, {VasimrHeliconRatioMax:F2}]. "
              + "Outside this band the engine is either helicon-starved (under-ionised) "
              + "or wasting power on excess ionisation. VX-200i runs at ~0.15.",
                ActualValue: ratio,
                Limit:       ratio < VasimrHeliconRatioMin
                                ? VasimrHeliconRatioMin
                                : VasimrHeliconRatioMax));
        }
    }

    private static void EvaluateVasimrIonisationFractionBelowFloor(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.VasimrPlasmaState plasma) return;
        double eta_i = plasma.IonisationFraction;
        if (eta_i < VasimrIonisationFractionFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "VASIMR_IONIZATION_FRACTION_LOW",
                $"Helicon ionisation fraction η_i = {eta_i:F3} below floor "
              + $"{VasimrIonisationFractionFloor:F2}. Substantial argon exits "
              + "unionised. Raise VasimrHeliconRfPower_W or lower "
              + "VasimrArgonMassFlow_kgs.",
                ActualValue: eta_i,
                Limit:       VasimrIonisationFractionFloor));
        }
    }

    private static void EvaluateVasimrNozzleConversionLow(
        ElectricPropulsionResult result,
        List<FeasibilityViolation> advisories)
    {
        if (result.PlasmaState is not Plasma.VasimrPlasmaState plasma) return;
        double eta = plasma.NozzleConversionEfficiency;
        if (eta < VasimrNozzleConversionFloor)
        {
            advisories.Add(new FeasibilityViolation(
                "VASIMR_NOZZLE_CONVERSION_LOW",
                $"Magnetic-nozzle conversion η_nozzle = {eta:F3} below floor "
              + $"{VasimrNozzleConversionFloor:F2}. Mirror ratio too gentle; most "
              + "T_⊥ stays perpendicular and recirculates in the plume rather than "
              + "becoming directed thrust. Raise VasimrSolenoidField_T or "
              + "VasimrNozzleExitRadius_mm to push M higher.",
                ActualValue: eta,
                Limit:       VasimrNozzleConversionFloor));
        }
    }
}
