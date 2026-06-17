// StartTransientSim.cs — Lumped 0-D engine-start transient simulator.
//
// "Watch the engine start in simulation" — narrative unlock. Inputs:
// valve open ramp, dome volumes, igniter delay, target steady-state
// Pc. Outputs: time histories of valve position, dome fill fraction,
// propellant accumulated in the chamber pre-ignition, and chamber
// pressure; plus single-figure summaries (time-to-90 % Pc, peak
// pressure spike, hard-start risk flag).
//
// Lumped-parameter state model:
//   • Valve position v(t) ramps linearly from 0 → 1 over
//     ValveOpenTime_s.
//   • Pre-dome-fill: ṁ_inj(t) = 0; v(t)·ṁ_steady accumulates in
//     dome until m_dome ≥ ρ·V_dome.
//   • Post-dome-fill: ṁ_inj(t) = v(t)·ṁ_steady (steady-state
//     pass-through, no compressibility).
//   • Pre-ignition: injected propellant accumulates in the chamber
//     volume without burning. Mass tracked as
//     m_unburned += ṁ_inj·dt.
//   • Igniter fires at IgniterDelay_s. From there, the chamber
//     pressure tracks a first-order lag toward
//     P_target,t = (ṁ_inj / ṁ_steady) · ChamberPressure_Pa with
//     time constant τ_c = V_chamber / (c* · A_t).
//   • Hard-start spike: at ignition, the unburned propellant
//     accumulated in the chamber combusts in roughly the
//     residence time. The peak pressure overshoot is estimated
//     as ΔP_spike ≈ (m_unburned / ṁ_steady) · P_target / τ_c · 0.5
//     — a half-cycle equivalent of an instantaneous mass dump.
//
// MVP simplifications (deliberate for tractability):
//   • Pure linear valve. Real solenoid / hydraulic dynamics add
//     a ~10 ms second-order envelope; ignored.
//   • Both propellants share the same ramp. Independent ox / fuel
//     ramps are a follow-on field.
//   • c* assumed at steady-state value during the entire ramp;
//     real c* at low-Pc / off-mixture is typically lower, so the
//     τ_c estimate is conservative.
//   • Pre-ignition fuel/ox accumulation is tracked as a single
//     scalar mass; mixture-ratio scrambling at start is not
//     modelled. Hard-start estimate is therefore a bound, not a
//     prediction.
//
// References:
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e §10.6
//     (start transients, dome fill, hard-start mitigation).
//   Huzel & Huang AIAA Vol. 147 §7 (ignition systems and start
//     sequencing).
//   Crocco & Cheng, "Theory of Combustion Instability in Liquid
//     Propellant Rocket Motors" — for τ_c sizing background.

namespace Voxelforge.Combustion;

/// <summary>
/// Inputs to the start-transient simulator. SI units throughout.
/// <c>SteadyMassFlow_kgs</c> is the total propellant flow at the
/// design Pc (fuel + ox). <c>ChamberVolume_m3</c> includes
/// converging-section volume up to the throat.
/// </summary>
public sealed record StartTransientInputs(
    double ValveOpenTime_s,
    double IgniterDelay_s,
    double DomeVolume_m3,             // total fuel+ox dome volume
    double DomePropellantDensity_kgm3,// average liquid density in dome
    double SteadyMassFlow_kgs,
    double ChamberVolume_m3,
    double CStar_ms,
    double ThroatArea_m2,
    double ChamberPressure_Pa,
    double SimulationDuration_s,
    double TimeStep_s,
    double HardStartFactor,           // ΔP_spike/P_target above which we flag hard start
    // Independent ox / fuel valve ramps.
    // 0 (default) ⇒ both sides use the shared `ValveOpenTime_s`.
    // Non-zero ⇒ that side ramps on its own clock, enabling staged
    // starts (e.g. fuel-lead = fill fuel dome before opening ox to
    // mitigate hard-start). The simulator tracks dome fill per side
    // and only injects each propellant once ITS dome is full.
    double OxValveOpenTime_s     = 0.0,
    double FuelValveOpenTime_s   = 0.0,
    // Per-side dome volumes + steady mass flows. When either side's
    // value is 0 (default), the simulator splits `DomeVolume_m3` and
    // `SteadyMassFlow_kgs` 50/50 between ox and fuel — preserves
    // legacy single-channel behaviour exactly.
    double OxDomeVolume_m3       = 0.0,
    double FuelDomeVolume_m3     = 0.0,
    double OxSteadyMassFlow_kgs  = 0.0,
    double FuelSteadyMassFlow_kgs = 0.0);

/// <summary>
/// One sample of the time-history captured by
/// <see cref="StartTransientSim.Run"/>.
/// <c>ValvePosition</c> + <c>DomeFillFraction</c> are the legacy
/// aggregate values — when ox/fuel ramps differ, they report the
/// AVERAGE so existing chart consumers degrade gracefully.
/// Per-side fields cover the staged-start case; the chart shows them
/// when non-default.
/// </summary>
public readonly record struct StartTransientSample(
    double Time_s,
    double ValvePosition,
    double DomeFillFraction,
    double UnburnedMassInChamber_kg,
    double ChamberPressure_Pa,
    // Per-side detail. Defaults match the aggregate values for
    // back-compat — when fed with a single shared ramp, both ox/fuel
    // mirror the aggregate.
    double OxValvePosition       = 0.0,
    double FuelValvePosition     = 0.0,
    double OxDomeFillFraction    = 0.0,
    double FuelDomeFillFraction  = 0.0);

/// <summary>
/// Output of <see cref="StartTransientSim.Run"/>.
/// </summary>
public sealed record StartTransientResult(
    StartTransientSample[] Samples,
    double TimeTo90Pc_s,
    double IgnitionTime_s,
    double UnburnedMassAtIgnition_kg,
    double PeakPressure_Pa,
    double PeakPressureOvershoot,     // (peak − target) / target, ≥ 0
    bool   HardStartRisk,
    double ChamberFillTimeConstant_s,
    string[] Warnings);

public static class StartTransientSim
{
    /// <summary>
    /// Default hard-start threshold: peak Pc overshoot beyond which
    /// we flag the start as a hard-start risk. 0.5 (=50 % overshoot)
    /// matches the rule-of-thumb cited in Sutton §10.6.
    /// </summary>
    public const double DefaultHardStartFactor = 0.5;

    /// <summary>
    /// Minimum accepted time step for the integrator. A pathological
    /// value like 1e-10 with a default 1 s duration would compute
    /// <c>N = 1e10</c>, which overflows <see cref="int"/> and wraps to
    /// a negative sample count before the 200 000-sample guard fires.
    /// 1 µs is already two orders of magnitude below any meaningful
    /// LRE start-transient time scale.
    /// </summary>
    public const double MinTimeStep_s = 1e-6;

    public static StartTransientResult Run(StartTransientInputs inp)
    {
        var warnings = new System.Collections.Generic.List<string>();

        if (inp.SimulationDuration_s <= 0 || inp.TimeStep_s <= 0)
            return EmptyResult("Simulation duration / time step ≤ 0; skipped.");
        if (inp.TimeStep_s < MinTimeStep_s)
            return EmptyResult($"Time step {inp.TimeStep_s:E2} s is below the {MinTimeStep_s:E0} s floor "
                             + "(integer-overflow / memory-blowup guard); raise TimeStep_s.");
        if (inp.SteadyMassFlow_kgs <= 0 || inp.ChamberVolume_m3 <= 0)
            return EmptyResult("Steady mass flow / chamber volume ≤ 0; skipped.");

        int N = (int)System.Math.Ceiling(inp.SimulationDuration_s / inp.TimeStep_s) + 1;
        if (N > 200_000)
        {
            warnings.Add($"Time step {inp.TimeStep_s * 1000:F1} ms over duration "
                       + $"{inp.SimulationDuration_s:F2} s yields {N} samples; "
                       + "raise the step to keep memory bounded.");
            N = 200_000;
        }

        var samples = new StartTransientSample[N];
        double tau_c = inp.CStar_ms > 0 && inp.ThroatArea_m2 > 0
            ? inp.ChamberVolume_m3 / (inp.CStar_ms * inp.ThroatArea_m2)
            : 1e-3;
        if (tau_c <= 0) tau_c = 1e-3;

        // Per-side ramps + dome volumes + steady flows. When the user
        // hasn't supplied an explicit value, fall back to the shared
        // ramp + a 50/50 split of dome volume & total mass flow so the
        // legacy single-channel behaviour reproduces exactly.
        double valveRampOx   = System.Math.Max(
            inp.OxValveOpenTime_s   > 0 ? inp.OxValveOpenTime_s   : inp.ValveOpenTime_s, 1e-6);
        double valveRampFuel = System.Math.Max(
            inp.FuelValveOpenTime_s > 0 ? inp.FuelValveOpenTime_s : inp.ValveOpenTime_s, 1e-6);
        double mDotOxSteady   = inp.OxSteadyMassFlow_kgs   > 0
            ? inp.OxSteadyMassFlow_kgs   : 0.5 * inp.SteadyMassFlow_kgs;
        double mDotFuelSteady = inp.FuelSteadyMassFlow_kgs > 0
            ? inp.FuelSteadyMassFlow_kgs : 0.5 * inp.SteadyMassFlow_kgs;
        double domeVolOx   = inp.OxDomeVolume_m3   > 0 ? inp.OxDomeVolume_m3   : 0.5 * inp.DomeVolume_m3;
        double domeVolFuel = inp.FuelDomeVolume_m3 > 0 ? inp.FuelDomeVolume_m3 : 0.5 * inp.DomeVolume_m3;
        double mDomeOxFull   = inp.DomePropellantDensity_kgm3 * domeVolOx;
        double mDomeFuelFull = inp.DomePropellantDensity_kgm3 * domeVolFuel;

        double m_dome_ox   = 0, m_dome_fuel = 0;
        double m_unburn  = 0;
        double Pc        = 0;

        double timeTo90 = double.NaN;
        double peakPc   = 0;
        double ignTime  = -1;
        double m_unburn_at_ign = 0;

        for (int i = 0; i < N; i++)
        {
            double t = i * inp.TimeStep_s;
            double valveOx   = System.Math.Clamp(t / valveRampOx,   0.0, 1.0);
            double valveFuel = System.Math.Clamp(t / valveRampFuel, 0.0, 1.0);
            double m_dot_in_ox   = valveOx   * mDotOxSteady;
            double m_dot_in_fuel = valveFuel * mDotFuelSteady;

            // Per-side dome fill. Each side flows through the injector
            // only once its OWN dome is full; the other side can be
            // still filling without blocking it. This is what enables a
            // staged start (fuel-lead → fuel injects first → ox catches
            // up by ignition time).
            double m_dot_inj_ox, m_dot_inj_fuel;
            if (m_dome_ox < mDomeOxFull)
            { m_dome_ox += m_dot_in_ox * inp.TimeStep_s; m_dot_inj_ox = 0; }
            else m_dot_inj_ox = m_dot_in_ox;
            if (m_dome_fuel < mDomeFuelFull)
            { m_dome_fuel += m_dot_in_fuel * inp.TimeStep_s; m_dot_inj_fuel = 0; }
            else m_dot_inj_fuel = m_dot_in_fuel;
            double m_dot_inj = m_dot_inj_ox + m_dot_inj_fuel;

            // Aggregate (legacy) signals for chart back-compat.
            double valve     = 0.5 * (valveOx + valveFuel);
            double domeFrac  = 0.5 * (
                System.Math.Clamp(m_dome_ox   / System.Math.Max(mDomeOxFull,   1e-12), 0, 1) +
                System.Math.Clamp(m_dome_fuel / System.Math.Max(mDomeFuelFull, 1e-12), 0, 1));

            bool ignited = t >= inp.IgniterDelay_s;
            if (!ignited)
            {
                // Propellant pools in the chamber unburned. Pc stays ~0.
                m_unburn += m_dot_inj * inp.TimeStep_s;
                Pc = 0;
            }
            else
            {
                if (ignTime < 0)
                {
                    ignTime = t;
                    m_unburn_at_ign = m_unburn;
                }
                // First-order lag toward instantaneous target Pc.
                double Pc_target = (m_dot_inj / inp.SteadyMassFlow_kgs)
                                 * inp.ChamberPressure_Pa;
                Pc += (Pc_target - Pc) * inp.TimeStep_s / tau_c;

                // Hard-start spike — instantaneous burn of pooled
                // unburned propellant. Spike decays in one tau_c.
                if (m_unburn > 0)
                {
                    double dPSpike = (m_unburn / inp.SteadyMassFlow_kgs)
                                   * inp.ChamberPressure_Pa / tau_c
                                   * 0.5
                                   * System.Math.Exp(-(t - ignTime) / tau_c);
                    Pc += dPSpike * inp.TimeStep_s / tau_c;
                }

                if (Pc > peakPc) peakPc = Pc;
                if (double.IsNaN(timeTo90) && Pc >= 0.9 * inp.ChamberPressure_Pa)
                    timeTo90 = t;
            }

            double oxFracNow   = System.Math.Clamp(m_dome_ox   / System.Math.Max(mDomeOxFull,   1e-12), 0, 1);
            double fuelFracNow = System.Math.Clamp(m_dome_fuel / System.Math.Max(mDomeFuelFull, 1e-12), 0, 1);
            samples[i] = new StartTransientSample(
                Time_s:                   t,
                ValvePosition:            valve,
                DomeFillFraction:         domeFrac,
                UnburnedMassInChamber_kg: m_unburn,
                ChamberPressure_Pa:       Pc,
                OxValvePosition:          valveOx,
                FuelValvePosition:        valveFuel,
                OxDomeFillFraction:       oxFracNow,
                FuelDomeFillFraction:     fuelFracNow);
        }

        double overshoot = (peakPc - inp.ChamberPressure_Pa)
                         / System.Math.Max(inp.ChamberPressure_Pa, 1);
        if (overshoot < 0) overshoot = 0;
        bool hardStart = overshoot >= inp.HardStartFactor;

        if (hardStart)
            warnings.Add($"Predicted Pc overshoot {overshoot * 100:F0} % "
                       + $"≥ {inp.HardStartFactor * 100:F0} % threshold — "
                       + "tighten igniter delay or stage the valves to fire dome-fuel-then-ox.");

        if (double.IsNaN(timeTo90))
        {
            timeTo90 = double.PositiveInfinity;
            warnings.Add("Simulation ended before chamber reached 90 % of target Pc — "
                       + "extend SimulationDuration_s or raise SteadyMassFlow_kgs.");
        }

        return new StartTransientResult(
            Samples:                     samples,
            TimeTo90Pc_s:                timeTo90,
            IgnitionTime_s:              ignTime < 0 ? double.PositiveInfinity : ignTime,
            UnburnedMassAtIgnition_kg:   m_unburn_at_ign,
            PeakPressure_Pa:             peakPc,
            PeakPressureOvershoot:       overshoot,
            HardStartRisk:               hardStart,
            ChamberFillTimeConstant_s:   tau_c,
            Warnings:                    warnings.ToArray());
    }

    private static StartTransientResult EmptyResult(string note)
        => new StartTransientResult(
            Samples:                     System.Array.Empty<StartTransientSample>(),
            TimeTo90Pc_s:                double.NaN,
            IgnitionTime_s:              double.NaN,
            UnburnedMassAtIgnition_kg:   0,
            PeakPressure_Pa:             0,
            PeakPressureOvershoot:       0,
            HardStartRisk:               false,
            ChamberFillTimeConstant_s:   0,
            Warnings:                    new[] { note });
}
