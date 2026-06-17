// ShutdownBlowdownSim.cs — Lumped 0-D engine-shutdown transient.
//
// Companion to StartTransientSim — closes the hot-fire-readiness loop
// for the symmetric "shutdown / blowdown" half. Inputs: valve close
// ramp, steady-state Pc + ṁ, chamber volume, throat area, c*.
// Outputs: time histories of valve position, injected ṁ, chamber Pc;
// plus single-figure summaries (time-to-subcritical, time-to-10%-Pc,
// residual propellant burned/vented, total-impulse loss vs an ideal
// cutoff).
//
// Lumped-parameter state model:
//   • Valve position v(t) ramps linearly 1 → 0 over ValveCloseTime_s.
//     Optional per-side ramps via OxValveCloseTime_s /
//     FuelValveCloseTime_s for staged shutdowns (e.g. ox-lean cutoff
//     to clear residual fuel before complete close).
//   • Injected mass flow ṁ_in(t) = v(t) · ṁ_steady. Drops to zero
//     when both valves fully close.
//   • Chamber pressure tracks a first-order lag toward
//     P_target,t = (ṁ_in / ṁ_steady) · Pc_steady with time constant
//     τ_c = V_chamber / (c* · A_t) — same constant the start-transient
//     uses for chamber-fill, by symmetry.
//   • Once valves are fully closed, ṁ_in = 0 → P_target = 0 → Pc
//     decays exponentially: Pc(t) = Pc(t_close) · exp(-(t-t_close)/τ_c).
//   • Optional purge-gas sweep: a constant ṁ_purge applied after
//     PurgeTriggerDelay_s. Purge mass is tracked separately from
//     residual propellant; chamber Pc tracks toward
//     (ṁ_purge / ṁ_steady) · Pc_steady so a steady purge holds a
//     small positive ullage above ambient.
//   • Subcritical threshold: chamber Pc has fallen below
//     1.1 × AmbientPressure_Pa, i.e. the throat is no longer
//     choked-flow and the engine has effectively cut off thrust.
//
// MVP simplifications (deliberate for tractability):
//   • Same first-order lag as start-transient. Real blowdowns develop
//     a second-order envelope around throat-flow choking transitions
//     and (for liquid engines) two-phase flashing in the manifold;
//     ignored for the MVP.
//   • Both propellants share the same close-side τ_c. Independent
//     ramps differ only in how fast each side's contribution to ṁ_in
//     falls to zero — Pc dynamics still follow a single τ_c.
//   • c* is held at the steady-state value through the transient.
//     Real c* drops with mixture-ratio scrambling at low ṁ; the
//     τ_c estimate is therefore optimistic on decay time (real
//     blowdown is slower than the model predicts when MR scrambles).
//   • Residual propellant tracking: any flow that arrives after the
//     last useful combustion (defined as: when the chamber has
//     dropped below 50 % of steady-state Pc) is counted as "vented"
//     rather than "burned." Hence-residual mass is a soft estimate;
//     the real distinction between "burned through the throat" and
//     "vented overboard" is a detailed-CFD problem.
//
// References:
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e §10.7
//     (shutdown sequencing, chill-out, residual pressure decay).
//   Huzel & Huang AIAA Vol. 147 §7 (start + cutoff sequencing,
//     hard-cutoff vs soft-cutoff trade-offs).
//   NASA SP-194 §8 (Liquid Rocket Engine Combustion Stability,
//     shutdown-transient discussion).

namespace Voxelforge.Combustion;

/// <summary>
/// Inputs to the shutdown blowdown simulator. Mirrors the shape of
/// <see cref="StartTransientInputs"/> for symmetry; the integrator
/// reuses the same chamber time constant (τ_c = V_c / (c*·A_t)).
/// SI units throughout.
/// </summary>
public sealed record ShutdownBlowdownInputs(
    double SteadyMassFlow_kgs,
    double ChamberPressure_Pa,
    double ChamberVolume_m3,
    double CStar_ms,
    double ThroatArea_m2,
    double ValveCloseTime_s,
    double AmbientPressure_Pa,
    double SimulationDuration_s,
    double TimeStep_s,
    // Per-side close ramps. 0 (default) ⇒ both sides use the shared
    // ValveCloseTime_s. Non-zero ⇒ that side ramps on its own clock.
    double OxValveCloseTime_s   = 0.0,
    double FuelValveCloseTime_s = 0.0,
    // Optional purge-gas sweep. 0 ⇒ no purge. Non-zero ⇒ ṁ_purge
    // applied starting at PurgeTriggerDelay_s.
    double PurgeMassFlow_kgs    = 0.0,
    double PurgeTriggerDelay_s  = 0.0);

/// <summary>
/// One sample of the time-history captured by
/// <see cref="ShutdownBlowdownSim.Run"/>.
/// </summary>
public readonly record struct ShutdownBlowdownSample(
    double Time_s,
    double ValvePosition,             // average across ox + fuel
    double InjectedMassFlow_kgs,
    double ChamberPressure_Pa,
    // Per-side detail. Defaults match the aggregate value for
    // back-compat with single-channel callers.
    double OxValvePosition   = 0.0,
    double FuelValvePosition = 0.0);

/// <summary>
/// Output of <see cref="ShutdownBlowdownSim.Run"/>.
/// </summary>
public sealed record ShutdownBlowdownResult(
    ShutdownBlowdownSample[] Samples,
    double TimeToSubcritical_s,         // when Pc < 1.1 × AmbientPressure_Pa
    double TimeTo10PctPc_s,             // when Pc < 0.1 × Pc_steady
    double ResidualPropellantBurned_kg, // arrived while Pc ≥ 50 % steady
    double ResidualPropellantVented_kg, // arrived while Pc < 50 % steady
    double TotalImpulseLoss_Ns,         // vs ideal-cutoff (steady Pc → 0 instant)
    string[] Warnings);

public static class ShutdownBlowdownSim
{
    /// <summary>
    /// Minimum accepted time step for the integrator. Symmetric with
    /// <see cref="StartTransientSim.MinTimeStep_s"/>.
    /// </summary>
    public const double MinTimeStep_s = 1e-6;

    /// <summary>
    /// Threshold below which Pc is considered "subcritical" — the
    /// throat is no longer choked-flow and the engine has effectively
    /// cut off thrust. 1.1 × ambient is a soft heuristic; the exact
    /// transition depends on γ (P_subcritical / P_ambient =
    /// ((γ+1)/2)^(γ/(γ-1)) ≈ 1.84 for γ = 1.2 ideal-gas), but for an
    /// operator-facing report the 1.1× threshold is conservative and
    /// easy to read.
    /// </summary>
    public const double SubcriticalAboveAmbient = 1.1;

    /// <summary>
    /// Run the shutdown integrator. Always returns a result (never
    /// throws); pathological inputs produce an empty Samples array
    /// and a warning string.
    /// </summary>
    public static ShutdownBlowdownResult Run(ShutdownBlowdownInputs inp)
    {
        var warnings = new System.Collections.Generic.List<string>();

        if (inp.SimulationDuration_s <= 0 || inp.TimeStep_s <= 0)
            return EmptyResult("Simulation duration / time step ≤ 0; skipped.");
        if (inp.TimeStep_s < MinTimeStep_s)
            return EmptyResult($"Time step {inp.TimeStep_s:E2} s is below the {MinTimeStep_s:E0} s floor "
                             + "(integer-overflow / memory-blowup guard); raise TimeStep_s.");
        if (inp.SteadyMassFlow_kgs <= 0 || inp.ChamberVolume_m3 <= 0)
            return EmptyResult("Steady mass flow / chamber volume ≤ 0; skipped.");
        if (inp.CStar_ms <= 0 || inp.ThroatArea_m2 <= 0)
            return EmptyResult("c* / throat area ≤ 0; skipped.");
        if (inp.ChamberPressure_Pa <= 0)
            return EmptyResult("Steady chamber pressure ≤ 0; skipped.");

        int N = (int)System.Math.Ceiling(inp.SimulationDuration_s / inp.TimeStep_s) + 1;
        if (N > 200_000)
        {
            warnings.Add($"Time step {inp.TimeStep_s * 1000:F1} ms over duration "
                       + $"{inp.SimulationDuration_s:F2} s would produce {N} samples; "
                       + "capped at 200k to bound memory.");
            N = 200_000;
        }

        // Resolve per-side close ramps. Default to shared.
        double oxClose   = inp.OxValveCloseTime_s   > 0 ? inp.OxValveCloseTime_s   : inp.ValveCloseTime_s;
        double fuelClose = inp.FuelValveCloseTime_s > 0 ? inp.FuelValveCloseTime_s : inp.ValveCloseTime_s;
        // Per-side mass-flow split (50/50 by mass, MR-agnostic — the
        // shutdown integrator doesn't need MR resolution; what matters
        // is the aggregate ṁ → 0 trajectory).
        double mdotOxSteady   = 0.5 * inp.SteadyMassFlow_kgs;
        double mdotFuelSteady = 0.5 * inp.SteadyMassFlow_kgs;

        // Chamber-fill time constant — same shape as start-transient.
        double tauChamber = inp.ChamberVolume_m3 / (inp.CStar_ms * inp.ThroatArea_m2);

        // Allocate sample buffer. Pre-size at exact-needed length.
        var samples = new ShutdownBlowdownSample[N];

        double pcSteady   = inp.ChamberPressure_Pa;
        double pcSubcrit  = SubcriticalAboveAmbient * inp.AmbientPressure_Pa;
        double pc10Pct    = 0.10 * pcSteady;
        double pc50Pct    = 0.50 * pcSteady;

        double pc = pcSteady;
        double burnedMass  = 0;
        double ventedMass  = 0;
        double impulseInteg= 0;        // ∫ Pc · A_t · dt — converted to thrust loss vs ideal at end

        double tSubcritical = double.NaN;
        double t10Pct       = double.NaN;

        for (int i = 0; i < N; i++)
        {
            double t = i * inp.TimeStep_s;

            double vOx   = ValveProfile(t, oxClose);
            double vFuel = ValveProfile(t, fuelClose);
            double mdotInjectOx   = vOx   * mdotOxSteady;
            double mdotInjectFuel = vFuel * mdotFuelSteady;
            double mdotInject     = mdotInjectOx + mdotInjectFuel;

            // Optional purge gas adds to the chamber mass after trigger delay.
            double mdotPurge = (t >= inp.PurgeTriggerDelay_s) ? inp.PurgeMassFlow_kgs : 0;
            double mdotInChamber = mdotInject + mdotPurge;

            // First-order lag toward target Pc, where target reflects
            // the reduced ṁ_in. When ṁ_in = 0, target = 0 → exponential
            // decay through τ_chamber.
            double pcTarget = (mdotInChamber / inp.SteadyMassFlow_kgs) * pcSteady;
            double dPc      = (pcTarget - pc) / tauChamber;
            pc += dPc * inp.TimeStep_s;
            if (pc < 0) pc = 0;        // numerical floor

            // Track residual propellant. "Burned" while Pc ≥ 50 % of
            // steady (engine is still producing meaningful thrust);
            // "vented" otherwise.
            if (pc >= pc50Pct)
                burnedMass += mdotInject * inp.TimeStep_s;
            else
                ventedMass += mdotInject * inp.TimeStep_s;

            // Total impulse integral (Pc·A_t scales with thrust at a
            // constant nozzle expansion). Used downstream to compute
            // the "lost" tail.
            impulseInteg += pc * inp.ThroatArea_m2 * inp.TimeStep_s;

            samples[i] = new ShutdownBlowdownSample(
                Time_s:                 t,
                ValvePosition:          0.5 * (vOx + vFuel),
                InjectedMassFlow_kgs:   mdotInject,
                ChamberPressure_Pa:     pc,
                OxValvePosition:        vOx,
                FuelValvePosition:      vFuel);

            // Crossing-detection for the "single-figure" outputs.
            if (double.IsNaN(t10Pct) && pc < pc10Pct && i > 0)
                t10Pct = LinearInterpCrossing(samples[i - 1].ChamberPressure_Pa,
                                              pc, samples[i - 1].Time_s, t, pc10Pct);
            if (double.IsNaN(tSubcritical) && pc < pcSubcrit && i > 0)
                tSubcritical = LinearInterpCrossing(samples[i - 1].ChamberPressure_Pa,
                                                    pc, samples[i - 1].Time_s, t, pcSubcrit);
        }

        // If Pc never crossed the thresholds within the sim window,
        // the corresponding output stays NaN — surfaces as a warning
        // so the operator knows the simulation needs more duration.
        if (double.IsNaN(t10Pct))
            warnings.Add($"Chamber pressure did not fall below 10 % of steady "
                       + $"({pc10Pct/1e5:F2} bar) within {inp.SimulationDuration_s:F1} s "
                       + "of simulation — extend SimulationDuration_s for a "
                       + "tighter time-to-10%-Pc estimate.");
        if (double.IsNaN(tSubcritical))
            warnings.Add($"Chamber pressure did not fall below {pcSubcrit/1e5:F2} bar "
                       + "(1.1 × ambient) within the sim window — engine has "
                       + "not fully stopped thrust by the simulation cut-off.");

        // Total-impulse loss = (ideal impulse over decay window) -
        // (actual impulse). Ideal cutoff would have produced
        // pcSteady · A_t · t_close (instantaneous to zero after
        // t_close); actual produced impulseInteg above.
        double idealImpulse = pcSteady * inp.ThroatArea_m2 * inp.ValveCloseTime_s;
        double impulseLoss  = System.Math.Max(0, idealImpulse - impulseInteg);

        return new ShutdownBlowdownResult(
            Samples:                       samples,
            TimeToSubcritical_s:           tSubcritical,
            TimeTo10PctPc_s:               t10Pct,
            ResidualPropellantBurned_kg:   burnedMass,
            ResidualPropellantVented_kg:   ventedMass,
            TotalImpulseLoss_Ns:           impulseLoss,
            Warnings:                      warnings.ToArray());

        static ShutdownBlowdownResult EmptyResult(string warning) => new(
            Samples: System.Array.Empty<ShutdownBlowdownSample>(),
            TimeToSubcritical_s:         double.NaN,
            TimeTo10PctPc_s:             double.NaN,
            ResidualPropellantBurned_kg: 0,
            ResidualPropellantVented_kg: 0,
            TotalImpulseLoss_Ns:         0,
            Warnings:                    new[] { warning });
    }

    /// <summary>
    /// Linear ramp 1 → 0 over closeTime; clamps to 0 after closeTime.
    /// closeTime ≤ 0 → instant cutoff.
    /// </summary>
    internal static double ValveProfile(double t, double closeTime)
    {
        if (closeTime <= 0) return 0;
        if (t >= closeTime) return 0;
        if (t <= 0) return 1;
        return 1.0 - (t / closeTime);
    }

    /// <summary>
    /// Linear-interpolate the time at which a monotonically-decreasing
    /// signal crosses a target threshold between two samples.
    /// </summary>
    private static double LinearInterpCrossing(
        double yPrev, double yCurr, double tPrev, double tCurr, double yTarget)
    {
        double dy = yCurr - yPrev;
        if (System.Math.Abs(dy) < 1e-30) return tCurr;
        double frac = (yTarget - yPrev) / dy;
        return tPrev + frac * (tCurr - tPrev);
    }
}
