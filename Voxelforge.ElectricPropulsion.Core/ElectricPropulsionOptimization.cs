// ElectricPropulsionOptimization.cs — top-level entry point for the
// electric-propulsion pillar.
//
// Sprint E.1: wired four physics solvers into the resistojet pipeline.
// Sprint E.2: extended the result with feasibility gates.
// Sprint EP.W2.HET: switched on design.Kind per ADR-029 D2; added the
//   Hall-Effect pipeline. Resistojet path is bit-identical to E.2 — the
//   existing body is extracted into RunResistojetPipeline(); HallEffect
//   routes through the Busch discharge model in HetCycleSolver.
// Sprint EP.W2.AJ: added the Arcjet pipeline (Maecker-Kovitya thermal-arc).
// Sprint EP.W2.PPT: added the PPT pipeline (Solbes-Vondra ablation discharge).
// Sprint EP.W2.GIT: added the GIT pipeline (Child-Langmuir beam extraction).
// Sprint EP.W2.MPD: added the MPD pipeline (self-field Maecker Lorentz acceleration).

using System;
using System.Collections.Generic;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Voxelforge.Optimization;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Top-level orchestration for the electric-propulsion pillar.
/// </summary>
public static class ElectricPropulsionOptimization
{
    /// <summary>
    /// Solve a single design + conditions pair end-to-end. Dispatches on
    /// <see cref="ElectricPropulsionEngineDesign.Kind"/> per ADR-029 D2:
    /// <see cref="ElectricPropulsionEngineKind.Resistojet"/> runs the
    /// Wave-1 electrothermal pipeline; <see cref="ElectricPropulsionEngineKind.HallEffect"/>
    /// runs the Wave-2 Busch discharge model;
    /// <see cref="ElectricPropulsionEngineKind.Arcjet"/> runs the Wave-2
    /// Maecker-Kovitya thermal-arc model;
    /// <see cref="ElectricPropulsionEngineKind.PulsedPlasmaThruster"/> runs
    /// the Wave-2 Solbes-Vondra ablation-discharge model;
    /// <see cref="ElectricPropulsionEngineKind.GriddedIon"/> runs the Wave-2
    /// Child-Langmuir beam-extraction model;
    /// <see cref="ElectricPropulsionEngineKind.MagnetoPlasmaDynamic"/> runs
    /// the Wave-2 self-field Maecker Lorentz-acceleration model.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="cond"/> is null.
    /// </exception>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when an enum value outside the implemented kinds is supplied.
    /// </exception>
    /// <exception cref="System.NotImplementedException">
    /// Thrown when a declared-but-deferred kind is supplied — currently
    /// <see cref="ElectricPropulsionEngineKind.Vasimr"/> (Wave-3, EP.W4
    /// reserved enum slot per ADR-032). The slot is reserved for schema
    /// forward-compatibility; the physics dispatch is not yet implemented.
    /// </exception>
    public static ElectricPropulsionResult GenerateWith(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(cond);

        return design.Kind switch
        {
            ElectricPropulsionEngineKind.Resistojet           => RunResistojetPipeline(design, cond),
            ElectricPropulsionEngineKind.HallEffect           => RunHetPipeline(design, cond),
            ElectricPropulsionEngineKind.Arcjet               => RunArcjetPipeline(design, cond),
            ElectricPropulsionEngineKind.PulsedPlasmaThruster => RunPptPipeline(design, cond),
            ElectricPropulsionEngineKind.GriddedIon           => RunGitPipeline(design, cond),
            ElectricPropulsionEngineKind.MagnetoPlasmaDynamic => RunMpdPipeline(design, cond),
            ElectricPropulsionEngineKind.Vasimr               => RunVasimrPipeline(design, cond),
            ElectricPropulsionEngineKind.Feep                 => RunFeepPipeline(design, cond),
            ElectricPropulsionEngineKind.Hdlt                 => RunHdltPipeline(design, cond),
            _ => throw new NotSupportedException(
                $"Kind={design.Kind} is not a recognised electric-propulsion variant. "
              + "Declared kinds: Resistojet, HET, Arcjet, PPT, GriddedIon, "
              + "MagnetoPlasmaDynamic, Vasimr (deferred), Feep (deferred), Hdlt (deferred)."),
        };
    }

    private static ElectricPropulsionResult RunResistojetPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // Reject a degenerate inlet composition up front: the mixture-property
        // helpers divide by MixtureMW (= Σ xᵢ·MWᵢ), which is 0 when every mole
        // fraction is 0, yielding NaN γ/cp that propagate silently to NaN
        // thrust/Isp — and NaN-vs-limit gate comparisons that never fire. The
        // (previously dead) ValidateOrThrow also rejects negative fractions and
        // sums that don't reach 1.0.
        cond.InletComposition.ValidateOrThrow();

        // 1. Solve heater thermal state (lumped 0-D Newton on T_chamber).
        var heater = ElectrothermalHeaterSolver.Solve(design, cond);

        // 2. Solve nozzle (choked-throat continuity → chamber P, then
        //    isentropic to exit; Newton on M_exit given ε).
        var nozzle = IsentropicNozzleSolver.Solve(
            design,
            cond,
            chamberTemperature_K: heater.ChamberTemperature_K,
            propellantMassFlow_kgs: design.PropellantMassFlow_kgs);

        // 3. Radiation-loss fraction from the heater solve.
        double radiationLossFraction = design.HeaterPower_W > 0
            ? heater.RadiationLoss_W / design.HeaterPower_W
            : double.NaN;

        // 4. Thrust efficiency η_T = (1 − q_rad/P_in): the fraction of
        //    electrical input that ends up in the gas as enthalpy
        //    (vs lost as radiation). Bounded [0, 1]. Real-resistojet
        //    target 0.65–0.80 per NASA TM-2002-211314 §3. Frozen-flow
        //    loss multiplies into this separately via the advisory
        //    gate RESISTOJET_FROZEN_FLOW_LOSS_EXCESSIVE; not folded here.
        double thrustEfficiency = double.IsFinite(radiationLossFraction)
            ? Math.Max(0.0, Math.Min(1.0, 1.0 - radiationLossFraction))
            : 0.0;

        // 5. Build a preliminary result so the feasibility evaluator can
        //    inspect the physics state. The Violations + Advisories slots
        //    are filled below.
        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               nozzle.Thrust_N,
            IspVacuum_s:            nozzle.IspVacuum_s,
            ExitVelocity_ms:        nozzle.ExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           heater.HeaterCoilTemperature_K,
            ChamberTemp_K:          heater.ChamberTemperature_K,
            ExitMachNumber:         nozzle.ExitMachNumber,
            ExitPressure_Pa:        nozzle.ExitPressure_Pa,
            RadiationLossFraction:  radiationLossFraction,
            ChokedFlow:             nozzle.ChokedFlow,
            Violations:             emptyViolations,
            IsFeasible:             true);

        // 6. Run the 10-gate feasibility evaluator (Sprint E.2).
        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool solverConverged = heater.Converged && nozzle.Converged;
        bool isFeasible = solverConverged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunArcjetPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the Maecker-Kovitya thermal-arc model.
        var arcCycle = ArcjetCycleSolver.Solve(design, cond);
        MaeckerKovityaResult m = arcCycle.Maecker;
        ArcjetPlasmaState plasma = arcCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ · v_eff²) / P_arc. This collapses to η_thermal · cos²(θ)
        //    in the energy-balance model (since v_eff = v_exit · cos θ and
        //    ½ ṁ v_exit² = η_thermal · P_arc); the explicit form keeps the
        //    bookkeeping uniform with the resistojet / HET pipelines.
        double mDot = design.PropellantMassFlow_kgs;
        double v_eff = mDot > 0 ? m.Thrust_N / mDot : 0.0;
        double thrustEfficiency = m.ArcPower_W > 0
            ? Math.Max(0.0, Math.Min(1.0, 0.5 * mDot * v_eff * v_eff / m.ArcPower_W))
            : 0.0;

        double radiationLossFraction = m.ArcPower_W > 0
            ? m.AnodePowerLoss_W / m.ArcPower_W
            : double.NaN;

        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               m.Thrust_N,
            IspVacuum_s:            m.IspVacuum_s,
            ExitVelocity_ms:        m.ExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,           // arcjet has an arc column, not a heater coil
            ChamberTemp_K:          m.AnodeWallTemp_K,    // closest analogue (anode wall T)
            ExitMachNumber:         double.NaN,           // partial-equilibrium / frozen-flow ambiguity
            ExitPressure_Pa:        0.0,                  // vacuum operation
            RadiationLossFraction:  radiationLossFraction,
            ChokedFlow:             true,                 // arcjet always chokes at design power
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = m.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunPptPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the Solbes-Vondra ablation-discharge model.
        var pptCycle = PptCycleSolver.Solve(design, cond);
        AblationDischargeResult a = pptCycle.Ablation;
        PptPlasmaState plasma = pptCycle.PlasmaState;

        // 2. Time-averaged thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ_avg · v_eff²) / P_avg. PPT typical 5–15 % (Sutton 9e §16.4).
        double mDotAvg = a.AverageMassFlow_kgs;
        double v_eff = mDotAvg > 0 ? a.AverageThrust_N / mDotAvg : 0.0;
        double thrustEfficiency = a.AveragePower_W > 0
            ? Math.Max(0.0, Math.Min(1.0, 0.5 * mDotAvg * v_eff * v_eff / a.AveragePower_W))
            : 0.0;

        // PPT has no continuous discharge channel + no anode wall reaching
        // steady state. ChamberTemp_K + RadiationLossFraction don't apply
        // (the gates handle PPT-specific failure modes via the plasma state).
        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               a.AverageThrust_N,
            IspVacuum_s:            a.AverageIsp_s,
            ExitVelocity_ms:        a.ExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,                 // PPT has no heater coil
            ChamberTemp_K:          double.NaN,                 // µs-pulse — no thermal steady state
            ExitMachNumber:         double.NaN,                 // partially-ionised vapour, not continuum gas
            ExitPressure_Pa:        0.0,                        // vacuum operation
            RadiationLossFraction:  double.NaN,                 // no steady-state radiation balance
            ChokedFlow:             false,                      // no nozzle to choke
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = a.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunGitPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the Child-Langmuir beam-extraction model.
        var gitCycle = GitCycleSolver.Solve(design, cond);
        ChildLangmuirBeamResult b = gitCycle.Beam;
        IonPlasmaState plasma = gitCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ · v_eff²) / P_beam. For GIT the v_eff in the denominator
        //    is the effective exit velocity (Thrust / ṁ), which already folds
        //    in η_m, so the ratio collapses to η_m · (jet KE / V_b·J_beam).
        //    NSTAR cluster ≈ 0.60–0.70 (Goebel & Katz §5.4).
        double mDot = b.MassFlow_kgs;
        double v_eff = mDot > 0 ? b.Thrust_N / mDot : 0.0;
        double thrustEfficiency = b.BeamPower_W > 0
            ? Math.Max(0.0, Math.Min(1.0, 0.5 * mDot * v_eff * v_eff / b.BeamPower_W))
            : 0.0;

        // GIT has no discharge-chamber wall reaching a meaningful thermal
        // steady state at this fidelity (anode losses live downstream of
        // the plasma); ChamberTemp_K + RadiationLossFraction stay NaN.
        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               b.Thrust_N,
            IspVacuum_s:            b.IspVacuum_s,
            ExitVelocity_ms:        b.IonExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,         // no heater coil in GIT
            ChamberTemp_K:          double.NaN,         // discharge-chamber bulk T not modeled at this fidelity
            ExitMachNumber:         double.NaN,         // collisionless beam — Mach not defined
            ExitPressure_Pa:        0.0,                // vacuum operation
            RadiationLossFraction:  double.NaN,         // no steady-state radiation balance
            ChokedFlow:             true,               // perveance-limited beam is "choked" in the analogue sense
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = b.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunMpdPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the self-field Maecker MPD model.
        var mpdCycle = MpdCycleSolver.Solve(design, cond);
        SelfFieldLorentzResult m = mpdCycle.Lorentz;
        MpdPlasmaState plasma = mpdCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ · v²) / P_arc. Already computed inside the model as
        //    ThrustEfficiency_Maecker — propagate through the pillar's
        //    common ThrustEfficiency slot. Cluster 0.10–0.30 self-field
        //    (Polk 1991).
        double radiationLossFraction = m.DischargePower_W > 0
            // Cathode-fall power as a fraction of total — the model captures
            // it as the dominant loss path (anode-fall + radiation lumped
            // into the residual).
            ? Math.Min(1.0, SelfFieldLorentzModel.CathodeFallVoltage_V * design.MpdArcCurrent_A
                              / m.DischargePower_W)
            : double.NaN;

        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               m.Thrust_N,
            IspVacuum_s:            m.IspVacuum_s,
            ExitVelocity_ms:        m.ExitVelocity_ms,
            ThrustEfficiency:       m.ThrustEfficiency_Maecker,
            HeaterTemp_K:           double.NaN,             // no heater coil in MPD
            ChamberTemp_K:          m.CathodeWallTemp_K,    // closest analogue (cathode tip T)
            ExitMachNumber:         double.NaN,             // partially-ionised plasma; Mach not well-defined
            ExitPressure_Pa:        0.0,                    // vacuum operation
            RadiationLossFraction:  radiationLossFraction,
            ChokedFlow:             true,                   // self-field MPD is "choked" — pinch + magnetosonic limit
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = m.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunHetPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the Busch HET discharge model.
        var hetCycle = HetCycleSolver.Solve(design, cond);
        BuschDischargeResult d = hetCycle.Discharge;
        HetPlasmaState plasma = hetCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ_total · v_eff²) / P_d
        //    where v_eff = T / ṁ_total. Typical HET 0.45–0.55 (Goebel & Katz §3.5).
        double mDotTotal = design.XenonMassFlow_kgs;
        double v_eff = mDotTotal > 0 ? d.Thrust_N / mDotTotal : 0.0;
        double thrustEfficiency = d.DischargePower_W > 0
            ? Math.Max(0.0, Math.Min(1.0, 0.5 * mDotTotal * v_eff * v_eff / d.DischargePower_W))
            : 0.0;

        double radiationLossFraction = d.DischargePower_W > 0
            ? d.AnodePowerLoss_W / d.DischargePower_W
            : double.NaN;

        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               d.Thrust_N,
            IspVacuum_s:            d.IspVacuum_s,
            ExitVelocity_ms:        d.IonExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,            // no heater coil in HET
            ChamberTemp_K:          d.AnodeWallTemp_K,     // closest analogue (anode wall T)
            ExitMachNumber:         double.NaN,            // continuum-flow concept
            ExitPressure_Pa:        0.0,                   // vacuum operation
            RadiationLossFraction:  radiationLossFraction, // anode power-loss fraction
            ChokedFlow:             true,                  // HET is always "choked" — ion-acoustic limit
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = d.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunFeepPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the Mair-Lozano single-component emitter model.
        var feepCycle = FeepCycleSolver.Solve(design, cond);
        MairLozanoEmitterResult e = feepCycle.Emitter;
        FeepPlasmaState plasma = feepCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ · v²) / (V_acc · I_beam).
        //    For a perfect single-component beam this is 1.0 by construction
        //    (energy conservation). The number we report reflects how the
        //    energy actually flows through the device; the simplified model
        //    has no internal loss channel so η_T = 1.0 is the model's honest
        //    answer. Future Wave-4 work can add ionisation-energy + extractor-
        //    interception losses to push this toward the published 0.4–0.6.
        double inputPower = design.FeepAcceleratingVoltage_V * design.FeepBeamCurrent_A;
        double thrustEfficiency = inputPower > 0
            ? Math.Min(1.0, 0.5 * e.MassFlow_kgs * e.ExitVelocity_ms * e.ExitVelocity_ms / inputPower)
            : 0.0;

        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               e.Thrust_N,
            IspVacuum_s:            e.IspVacuum_s,
            ExitVelocity_ms:        e.ExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,            // no heater in FEEP
            ChamberTemp_K:          double.NaN,            // no chamber — emitter is a free-surface liquid metal
            ExitMachNumber:         double.NaN,            // collisionless beam; Mach not well-defined
            ExitPressure_Pa:        0.0,                   // vacuum operation
            RadiationLossFraction:  0.0,                   // single-component model has no internal radiation channel
            ChokedFlow:             true,                  // emission is current-limited (Fowler-Nordheim cliff)
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = e.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunHdltPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the parameterized cluster-fit Helicon Double-Layer model.
        var hdltCycle = HdltCycleSolver.Solve(design, cond);
        HeliconDoubleLayerResult h = hdltCycle.Helicon;
        HdltPlasmaState plasma = hdltCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / RF-input
        //    = (½ · ṁ_ion · v²) / P_rf. Cluster mid-band 0.02-0.10
        //    because only the ionised fraction contributes to thrust
        //    and the DL itself wastes much of the RF input on
        //    plasma-wall losses + radiation.
        double inputPower = design.HdltHeliconRfPower_W;
        double thrustEfficiency = inputPower > 0
            ? Math.Min(1.0, 0.5 * h.MassFlow_kgs * h.ExitVelocity_ms * h.ExitVelocity_ms / inputPower)
            : 0.0;

        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               h.Thrust_N,
            IspVacuum_s:            h.IspVacuum_s,
            ExitVelocity_ms:        h.ExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,            // no heater coil in HDLT
            ChamberTemp_K:          double.NaN,            // RF-heated plasma; T_e is electron-only
            ExitMachNumber:         double.NaN,            // collisionless beam at the DL throat
            ExitPressure_Pa:        0.0,                   // vacuum operation
            RadiationLossFraction:  double.NaN,            // model lumps RF losses into η_T; no separate radiation channel
            ChokedFlow:             true,                  // ionisation is helicon-mode-limited
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = h.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }

    private static ElectricPropulsionResult RunVasimrPipeline(
        ElectricPropulsionEngineDesign design,
        ResistojetConditions cond)
    {
        // 1. Solve the 3-stage helicon + ICRH + magnetic-nozzle model.
        var vasimrCycle = VasimrCycleSolver.Solve(design, cond);
        HeliconIcrhMagneticNozzleResult v = vasimrCycle.Helicon;
        VasimrPlasmaState plasma = vasimrCycle.PlasmaState;

        // 2. Thrust efficiency η_T = jet-kinetic / electrical-input
        //    = (½ · ṁ_ion · v²) / (P_helicon + P_icrh). Cluster mid-band
        //    0.50-0.70 for VASIMR (Chang Diaz 2009 reports ~60 % at the
        //    VX-200 bench). The model lumps stage losses into η_i (helicon
        //    ionisation cost) and η_nozzle (mirror conversion); η_T
        //    emerges from the energy balance.
        double inputPower = design.VasimrHeliconRfPower_W + design.VasimrIcrhRfPower_W;
        double thrustEfficiency = inputPower > 0
            ? Math.Min(1.0, 0.5 * v.MassFlow_kgs * v.ExitVelocity_ms * v.ExitVelocity_ms / inputPower)
            : 0.0;

        var emptyViolations = (IReadOnlyList<FeasibilityViolation>)Array.Empty<FeasibilityViolation>();
        var preliminary = new ElectricPropulsionResult(
            Design:                 design,
            Conditions:             cond,
            Thrust_N:               v.Thrust_N,
            IspVacuum_s:            v.IspVacuum_s,
            ExitVelocity_ms:        v.ExitVelocity_ms,
            ThrustEfficiency:       thrustEfficiency,
            HeaterTemp_K:           double.NaN,            // no resistive heater
            ChamberTemp_K:          double.NaN,            // RF-heated plasma; T_e set by helicon, T_i by ICRH
            ExitMachNumber:         double.NaN,            // collisionless beam at the magnetic-nozzle throat
            ExitPressure_Pa:        0.0,                   // vacuum operation
            RadiationLossFraction:  double.NaN,            // model lumps RF + radiation losses into η_T
            ChokedFlow:             true,                  // ionisation + ICRH set the mass-flow limit
            Violations:             emptyViolations,
            IsFeasible:             true)
        {
            PlasmaState = plasma,
        };

        var feasibility = ElectricPropulsionFeasibility.Evaluate(design, cond, preliminary);
        bool isFeasible = v.Converged && feasibility.Hard.Count == 0;

        return preliminary with
        {
            Violations = feasibility.Hard,
            Advisories = feasibility.Advisories,
            IsFeasible = isFeasible,
        };
    }
}
