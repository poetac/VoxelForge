// AirbreathingGates.cs — air-breathing pillar's declarative gate
// registrations (first non-rocket *Gates.cs file, 2026-05-05).
//
// Models the rocket-side `RocketGates.cs` structure: one static
// `RegisterAll()` entry point invoked once by `AirbreathingGateRegistry`
// on first access. Each registered gate is a `FeasibilityGateDescriptor<
// AirbreathingGateInput>` with a typed `Emit` callback.
//
// Wave 1 PR-4 (sub-step 1a.5, 2026-05-05): 2 pulsejet gates:
//   • PULSEJET_BLOWOUT_LEAN (Hard, PhysicsLimit, Glassman §3 LFL)
//   • PULSEJET_ACOUSTIC_OVERPRESSURE (Advisory, EmpiricalBand, Foa §11.4)
// Wave 2 (issue #428 sub-task 3, 2026-05-06): 1 afterburner gate:
//   • AFTERBURNER_LINER_OVERTEMP (Hard, PhysicsLimit, Mattingly §12)
// Wave 2 (issue #428, turboprop): 1 turboprop gate:
//   • TURBOPROP_SHAFT_POWER_INSUFFICIENT (Hard, PhysicsLimit, Mattingly §8)
// per the pillar-spec at docs/pillar-specs/.
//
// The 22 inline air-breathing gates in AirbreathingFeasibility.cs:34-179
// are NOT lifted in Wave 1/2 — that's a separate Stream B sprint. Gates
// added to this file ship NEW behavior; they don't replace existing
// inline gates until that lift sprint runs.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Thermo;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Optimization;

/// <summary>
/// Declarative registration entry point for air-breathing-pillar gates.
/// Invoked exactly once by <see cref="AirbreathingGateRegistry.Instance"/>'s
/// bootstrap delegate on first registry access.
/// </summary>
internal static class AirbreathingGates
{
    /// <summary>
    /// Hydrocarbon lower flammability limit (LFL) as fuel-air mass
    /// fraction. Per Glassman 1996 §3 Table 3.1 (volume LFL ~1.4 % for
    /// gasoline / JP-8 / JetA → ~0.030 mass fraction). Below this, the
    /// pulsejet cycle blows out regardless of equivalence-ratio
    /// bookkeeping. Distinct from the existing
    /// <c>COMBUSTOR_BLOWOUT_LEAN</c> at φ &lt; 0.20 — that gate covers
    /// the steady-combustion lean-blowout regime; this one is the LFL
    /// physics floor for the cyclic Humphrey combustion in pulsejets.
    /// </summary>
    public const double HydrocarbonLflMassFraction = 0.030;

    /// <summary>
    /// Peak-to-steady chamber pressure advisory ceiling.
    /// <c>P_peak / P_steady &gt; 1.30</c> suggests the cycle is approaching
    /// mode-jump or detonation transition (Foa §11.4). Calibrated against
    /// NACA RM E50A04 V-1 instrumented data (V-1 at nominal: ~1.22).
    /// Advisory severity — model is empirical and the threshold is a
    /// stability-margin advisory, not a hard physics limit.
    /// </summary>
    public const double AcousticOverpressureCeiling = 1.30;

    /// <summary>
    /// Maximum afterburner liner temperature [K]. Matches
    /// <see cref="TurbojetCycleSolver.AfterburnerMaxLinerTemp_K"/>.
    /// Duplicated here so the gate predicate doesn't import the cycle
    /// solver (pillar-purity / VFA001).
    /// </summary>
    public const double AfterburnerMaxLinerTemp_K = TurbojetCycleSolver.AfterburnerMaxLinerTemp_K;

    /// <summary>
    /// Minimum propeller power extraction fraction for a turboprop to be
    /// physically viable. A turboprop with <c>PropellerPowerExtraction_frac</c>
    /// below 0.5 means less than half of the available gas-generator exit
    /// enthalpy is captured by the power turbine — at that point the
    /// design is physically closer to a turbojet than a turboprop. This
    /// floor prevents mis-parameterised designs from silently masquerading
    /// as turboprops. Per Mattingly §8 and Walsh &amp; Fletcher §9, realistic
    /// values are 0.85–0.95.
    /// </summary>
    public const double TurbopropPowerExtractionMinimum = 0.50;

    /// <summary>
    /// Register all air-breathing pillar gates against the
    /// <see cref="AirbreathingGateRegistry.Instance"/>. Wave 1 PR-4 ships
    /// 2 pulsejet gates; Wave 2 adds 1 afterburner gate and 1 turboprop gate;
    /// future gates that don't already exist as inline if/else branches in
    /// <see cref="AirbreathingFeasibility.Evaluate"/> ship through this
    /// file.
    /// </summary>
    public static void RegisterAll()
    {
        AirbreathingGateRegistry.Instance.Register(
            new FeasibilityGateDescriptor<AirbreathingGateInput>(
                Id:           "PULSEJET_BLOWOUT_LEAN",
                Severity:     GateSeverity.Hard,
                Kind:         GateKind.PhysicsLimit,
                Applicability:EngineFamilyMask.Airbreathing,
                AdrRef:       "Pulsejet Wave-1 / Glassman §3",
                Emit:         EmitPulsejetBlowoutLean));

        AirbreathingGateRegistry.Instance.Register(
            new FeasibilityGateDescriptor<AirbreathingGateInput>(
                Id:           "PULSEJET_ACOUSTIC_OVERPRESSURE",
                Severity:     GateSeverity.Advisory,
                Kind:         GateKind.EmpiricalBand,
                Applicability:EngineFamilyMask.Airbreathing,
                AdrRef:       "Pulsejet Wave-1 / Foa §11.4",
                Emit:         EmitPulsejetAcousticOverpressure));

        AirbreathingGateRegistry.Instance.Register(
            new FeasibilityGateDescriptor<AirbreathingGateInput>(
                Id:           "AFTERBURNER_LINER_OVERTEMP",
                Severity:     GateSeverity.Hard,
                Kind:         GateKind.PhysicsLimit,
                Applicability:EngineFamilyMask.Airbreathing,
                AdrRef:       "Wave-2 / Mattingly §12 / GE J79 design notes",
                Emit:         EmitAfterburnerLinerOvertemp));

        AirbreathingGateRegistry.Instance.Register(
            new FeasibilityGateDescriptor<AirbreathingGateInput>(
                Id:           "TURBOPROP_SHAFT_POWER_INSUFFICIENT",
                Severity:     GateSeverity.Hard,
                Kind:         GateKind.PhysicsLimit,
                Applicability:EngineFamilyMask.Airbreathing,
                AdrRef:       "Turboprop Wave-2 / Mattingly §8",
                Emit:         EmitTurbopropShaftPowerInsufficient));
    }

    /// <summary>
    /// PULSEJET_BLOWOUT_LEAN — pulsejet fuel-air mass fraction below LFL.
    /// Self-guards on Kind=Pulsejet. Skips H₂ fuel (LFL ~0.003 mass — the
    /// existing <c>COMBUSTOR_BLOWOUT_LEAN</c> at φ &lt; 0.20 covers H₂
    /// blowout; firing this gate on H₂ would be spuriously aggressive
    /// because hydrogen's flammability range extends much leaner than
    /// hydrocarbons).
    /// </summary>
    private static void EmitPulsejetBlowoutLean(
        AirbreathingGateInput input, List<FeasibilityViolation> violations)
    {
        if (input.Design.Kind != AirbreathingEngineKind.Pulsejet) return;
        if (input.Conditions.Fuel == AirbreathingFuel.H2) return;

        var fuel = AirbreathingFuelTables.Lookup(input.Conditions.Fuel);
        double f = input.Design.EquivalenceRatio * fuel.StoichiometricFuelAirRatio;
        if (f >= HydrocarbonLflMassFraction) return;

        violations.Add(new FeasibilityViolation(
            ConstraintId: "PULSEJET_BLOWOUT_LEAN",
            Description:
                $"Pulsejet fuel-air mass fraction f = {f:F4} (φ = {input.Design.EquivalenceRatio:F3}) " +
                $"below hydrocarbon lower-flammability-limit floor {HydrocarbonLflMassFraction:F3}. " +
                $"Pulsejet cyclic combustion cannot sustain below this threshold (Glassman §3 Table 3.1). " +
                $"Increase φ or switch to a higher-LHV fuel.",
            ActualValue: f,
            Limit:       HydrocarbonLflMassFraction));
    }

    /// <summary>
    /// PULSEJET_ACOUSTIC_OVERPRESSURE — predicted peak-cycle chamber
    /// pressure ratio above the empirical stability ceiling. Uses the
    /// closed-form Humphrey peak-to-steady model from
    /// <see cref="HumphreyCyclePerformance.PeakChamberPressureRatio"/>.
    /// Self-guards on Kind=Pulsejet and on station-2/4 finite values.
    /// </summary>
    private static void EmitPulsejetAcousticOverpressure(
        AirbreathingGateInput input, List<FeasibilityViolation> advisories)
    {
        if (input.Design.Kind != AirbreathingEngineKind.Pulsejet) return;
        var s2 = input.Stations.Station(2);
        var s4 = input.Stations.Station(4);
        if (s2.StagnationT_K <= 0.0 || double.IsNaN(s2.StagnationT_K)) return;
        if (s4.StagnationT_K <= 0.0 || double.IsNaN(s4.StagnationT_K)) return;

        double ratio = HumphreyCyclePerformance.PeakChamberPressureRatio(
            s2.StagnationT_K, s4.StagnationT_K);
        if (double.IsNaN(ratio)) return;
        if (ratio <= AcousticOverpressureCeiling) return;

        advisories.Add(new FeasibilityViolation(
            ConstraintId: "PULSEJET_ACOUSTIC_OVERPRESSURE",
            Description:
                $"Predicted pulsejet peak-to-steady chamber pressure ratio P_peak/P_steady = " +
                $"{ratio:F2} above advisory ceiling {AcousticOverpressureCeiling:F2}. " +
                $"Cycle stability margin is tight — buzz mode may transition to mode-jump " +
                $"or detonation (Foa §11.4 + NACA RM E50A04). Reduce φ or shorten the " +
                $"combustor to soften combustion.",
            ActualValue: ratio,
            Limit:       AcousticOverpressureCeiling));
    }

    /// <summary>
    /// AFTERBURNER_LINER_OVERTEMP — afterburner exit temperature T_t7 above
    /// the uncooled Inconel 625 liner material limit. Self-guards on
    /// <see cref="AirbreathingEngineDesign.EnableAfterburner"/> = true and
    /// on station-7 being finite (NaN when afterburner is off). Hard gate:
    /// liner failure above this temperature is structurally certain.
    /// </summary>
    private static void EmitAfterburnerLinerOvertemp(
        AirbreathingGateInput input, List<FeasibilityViolation> violations)
    {
        if (!input.Design.EnableAfterburner) return;
        var s7 = input.Stations.Station(7);
        if (double.IsNaN(s7.StagnationT_K)) return;

        double T_t7 = s7.StagnationT_K;
        if (T_t7 <= AfterburnerMaxLinerTemp_K) return;

        violations.Add(new FeasibilityViolation(
            ConstraintId: "AFTERBURNER_LINER_OVERTEMP",
            Description:
                $"Afterburner exit temperature T_t7 = {T_t7:F0} K exceeds the uncooled " +
                $"Inconel 625 liner material limit {AfterburnerMaxLinerTemp_K:F0} K " +
                $"(Mattingly §12). Reduce afterburner fuel-air ratio " +
                $"(AfterburnerFuelAirRatio = {input.Design.AfterburnerFuelAirRatio:F4}) " +
                $"or add active liner cooling.",
            ActualValue: T_t7,
            Limit:       AfterburnerMaxLinerTemp_K));
    }

    /// <summary>
    /// TURBOPROP_SHAFT_POWER_INSUFFICIENT — turboprop power extraction
    /// fraction below the physical viability floor. Self-guards on
    /// Kind=Turboprop. A design with fpe &lt;
    /// <see cref="TurbopropPowerExtractionMinimum"/> is effectively a
    /// turbojet (all thrust from the residual nozzle, minimal shaft power)
    /// and signals a mis-parameterised design rather than a viable turboprop.
    /// Per Mattingly §8, realistic turboprop values are 0.85–0.95.
    /// </summary>
    private static void EmitTurbopropShaftPowerInsufficient(
        AirbreathingGateInput input, List<FeasibilityViolation> violations)
    {
        if (input.Design.Kind != AirbreathingEngineKind.Turboprop) return;
        double fpe = input.Design.PropellerPowerExtraction_frac;
        if (fpe >= TurbopropPowerExtractionMinimum) return;

        violations.Add(new FeasibilityViolation(
            ConstraintId: "TURBOPROP_SHAFT_POWER_INSUFFICIENT",
            Description:
                $"Turboprop PropellerPowerExtraction_frac = {fpe:F3} is below the viability " +
                $"floor {TurbopropPowerExtractionMinimum:F2}. At this extraction level the design " +
                $"delivers less than half the available gas-generator exit enthalpy to the propeller " +
                $"shaft, making it physically closer to a turbojet than a turboprop. " +
                $"Set PropellerPowerExtraction_frac in the range 0.85–0.95 (Mattingly §8).",
            ActualValue: fpe,
            Limit:       TurbopropPowerExtractionMinimum));
    }
}
