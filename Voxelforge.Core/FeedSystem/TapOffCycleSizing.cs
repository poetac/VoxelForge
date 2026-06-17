// TapOffCycleSizing.cs — Sprint 25 (2026-04-23):
// Tap-off cycle turbine energy balance.
//
// What this models
// ────────────────
// Tap-off cycles (Rocketdyne J-2S, Blue Origin BE-4 on some
// configurations) divert a small fraction of the combustion gas — NOT
// from the core (where T ≈ 3500 K is way past any turbine-material
// limit) but from a fuel-film-cooled boundary-layer region where the
// gas sits at ~30-40 % of chamber Tc. That tapped flow drives the
// turbine; turbine discharge dumps overboard (open cycle, similar to
// gas-generator but without the preburner hardware).
//
// The governing physics: is the tap-point T cool enough for an
// uncooled turbine wheel (~1100 K service limit for Inconel 718),
// AND can the tapped mass flow × isentropic expansion across the
// pressure ratio produce enough specific work to drive the pumps?
//
//   Tap-point T (fuel-film-cooled boundary):
//     T_tap = BoundaryLayerFraction · T_chamber
//   Tapped mass flow (small side-stream, like GG):
//     ṁ_tap = TapMassFlowFraction · ṁ_total
//   Ideal specific work (isentropic expansion to ambient):
//     w_isen = cp · T_tap · (1 − (P_amb/P_c)^((γ−1)/γ))
//   Actual specific work (turbine efficiency η):
//     w      = η · w_isen
//   Available shaft power:
//     P_avail = ṁ_tap · w
//
// Heuristic constants below are sourced from the J-2S / BE-4 tap-off
// literature; every one is exposed as a `public const` so a future
// sprint (or a user with real hardware data) can lift them onto
// `OperatingConditions` for per-design tuning.
//
// Feasibility: TAPOFF_HOT_GAS_TOO_HOT fires when T_tap exceeds the
// turbine-material service limit. Power-balance deficit is detected
// but doesn't seed its own gate — the existing TURBINE_POWER_DEFICIT
// logic applies to preburner-driven cycles; a tap-off deficit
// surfaces through the PowerSufficient flag on the result record
// (future sprint can promote it to a hard gate if demand justifies).
//
// Simplifications (deliberate)
// ────────────────────────────
//   • Ideal-gas γ from CoolantState cp + propellant-pair molecular
//     weight — same approximation pattern used by
//     ExpanderCycleSizing.cs. For methane combustion products γ ≈ 1.25
//     at 1000 K, MW ≈ 18 g/mol.
//   • Fixed BoundaryLayerFraction = 0.35 (fuel-film-cooled boundary
//     at 35 % of core Tc). Real J-2S tap-offs run 30-40 %; this is
//     the conservative middle.
//   • Fixed TapMassFlowFraction = 0.03 (3 % of main-chamber flow).
//     J-2S literature cites 2-4 %; 3 % is representative. Lower
//     reduces available power; higher reduces Isp more aggressively.
//   • No discharge-to-main-chamber option — this module models only
//     the open-tap variant. A closed-tap (discharge feeds back into
//     nozzle exit for additional thrust) is a future sprint.
//
// References
// ──────────
//   Sutton & Biblarz "Rocket Propulsion Elements" 9e §10.4 (tap-off
//     cycle topology, Fig 10-4).
//   NASA TN D-2196 (1964) — J-2S tap-off design rationale + measured
//     boundary-layer T fractions.
//   Huzel & Huang AIAA Vol. 147 §6.5 (turbine material T limits).

namespace Voxelforge.FeedSystem;

/// <summary>
/// Sprint 25: result of <see cref="TapOffCycleSizing.Size"/>. Attached
/// to <see cref="Optimization.RegenGenerationResult.TapOffTurbine"/>
/// when the cycle is <see cref="EngineCycle.TapOff"/>.
/// <see cref="TapPointTemperatureOK"/> seeds the
/// <c>TAPOFF_HOT_GAS_TOO_HOT</c> feasibility gate.
/// </summary>
public sealed record TapOffTurbineResult(
    double ChamberTemperature_K,         // main chamber T_c (uncorrected for efficiency)
    double TapPointTemperature_K,        // T at the fuel-film-cooled boundary
    double TurbineInletLimit_K,          // material service limit for uncooled wheel
    bool   TapPointTemperatureOK,        // T_tap <= limit
    double TapMassFlow_kgs,              // tapped mass flow
    double TotalMassFlow_kgs,            // main-chamber ṁ (for reference)
    double ChamberPressure_Pa,
    double OutletPressure_Pa,            // ambient (open-tap cycle)
    double Cp_Jkg_K,
    double EffectiveGamma,
    double IsentropicSpecificWork_Jkg,
    double ActualSpecificWork_Jkg,
    double Efficiency,
    double AvailableShaftPower_W,
    double RequiredShaftPower_W,
    bool   PowerSufficient,
    string Notes,
    // Sprint 34 / PH-26 (2026-04-25): turbine-stator choke check.
    // Default IsChoked = true to preserve back-compat for synthetic
    // test fixtures that build TapOffTurbineResult directly.
    double CriticalPressureRatio = 0.0,
    bool   IsChoked = true);

public static class TapOffCycleSizing
{
    /// <summary>
    /// Fraction of main-chamber T_c at which the fuel-film-cooled
    /// boundary-layer gas sits. J-2S literature cites 0.30–0.40; 0.35
    /// is the conservative middle. Drives the tap-point temperature.
    /// </summary>
    public const double BoundaryLayerFraction = 0.35;

    /// <summary>
    /// Fraction of total mass flow diverted to the tap-off turbine.
    /// J-2S / BE-4 literature cites 0.02–0.04; 0.03 is representative.
    /// Raising improves available power at the cost of Isp.
    /// </summary>
    public const double TapMassFlowFraction = 0.03;

    /// <summary>
    /// Turbine inlet temperature limit for an uncooled Inconel-718
    /// wheel. Shared with <see cref="Chamber.PreburnerChamber.TurbineInletTempLimit_K"/>.
    /// </summary>
    public const double TurbineInletTempLimit_K = 1100.0;

    /// <summary>
    /// Default single-stage impulse turbine efficiency. Same default
    /// as <see cref="TurbineSizing.DefaultEfficiency"/> for consistency.
    /// </summary>
    public const double DefaultEfficiency = 0.60;

    /// <summary>Ambient back-pressure for an open-tap turbine discharge (Pa).</summary>
    public const double AmbientBackPressure_Pa = 101_325.0;

    /// <summary>Universal gas constant (J/(kmol·K)).</summary>
    public const double R_universal = 8314.5;

    /// <summary>
    /// Size the tap-off cycle turbine energy balance. Returns
    /// <c>null</c> on any non-TapOff cycle (dispatch via
    /// <see cref="CycleSolvers"/>).
    /// </summary>
    /// <param name="cycle">Engine cycle — must be <see cref="EngineCycle.TapOff"/> to return non-null.</param>
    /// <param name="chamberTemperature_K">Main chamber adiabatic flame temperature (K).</param>
    /// <param name="chamberPressure_Pa">Main chamber pressure (Pa).</param>
    /// <param name="totalMassFlow_kgs">Main chamber total mass flow (fuel + ox).</param>
    /// <param name="warmGasGamma">Main-chamber combustion-gas γ (from propellant tables).</param>
    /// <param name="warmGasMolecularWeight_gmol">Combustion-gas MW (from propellant tables).</param>
    /// <param name="requiredPumpShaftPower_W">Total pump shaft power required, from <see cref="TurbopumpResult.TotalShaftPower_W"/>.</param>
    /// <param name="efficiency">Turbine total-to-static efficiency; defaults to <see cref="DefaultEfficiency"/>.</param>
    /// <param name="localGasTemperature_K">
    /// PH-49: static gas temperature at the tap-off axial station (K).
    /// When provided, <c>T_tap = BoundaryLayerFraction × localGasTemperature_K</c>
    /// instead of <c>BoundaryLayerFraction × chamberTemperature_K</c>.
    /// Null (default) preserves the pre-PH-49 flat-chamber-T behaviour.
    /// Interpolated from <c>StationResult.StaticTemp_K</c> at the station
    /// index corresponding to <see cref="RegenChamberDesign.TapOffAxialStation_frac"/>.
    /// </param>
    public static TapOffTurbineResult? Size(
        EngineCycle cycle,
        double      chamberTemperature_K,
        double      chamberPressure_Pa,
        double      totalMassFlow_kgs,
        double      warmGasGamma,
        double      warmGasMolecularWeight_gmol,
        double      requiredPumpShaftPower_W,
        double      efficiency = DefaultEfficiency,
        double?     localGasTemperature_K = null)
    {
        if (cycle != EngineCycle.TapOff) return null;
        if (totalMassFlow_kgs <= 0 || chamberPressure_Pa <= 0 || chamberTemperature_K <= 0)
            return null;
        if (chamberPressure_Pa <= AmbientBackPressure_Pa)
            return null;   // no forward expansion — not a physically meaningful tap-off

        // PH-49: use the local static T at the tap-off axial station when
        // provided; fall back to the flat chamber-T for legacy callers.
        double stationT = localGasTemperature_K ?? chamberTemperature_K;
        double tapT = BoundaryLayerFraction * stationT;
        double tapMassFlow = TapMassFlowFraction * totalMassFlow_kgs;

        // Use γ from the combustion-gas table directly (the tapped gas
        // is main-chamber combustion product, so the table's γ applies).
        double gamma = System.Math.Max(warmGasGamma, 1.05);
        double R_specific = R_universal / System.Math.Max(warmGasMolecularWeight_gmol, 0.1);
        double cp = gamma / System.Math.Max(gamma - 1.0, 1e-6) * R_specific;

        double pr = AmbientBackPressure_Pa / chamberPressure_Pa;
        double w_isen = cp * tapT * (1.0 - System.Math.Pow(pr, (gamma - 1.0) / gamma));
        double w = efficiency * System.Math.Max(w_isen, 0.0);
        double P_avail = tapMassFlow * w;

        // Sprint 34 / PH-26 (2026-04-25): stator-throat choke check.
        // Tap-off is the most exposed cycle to unchoked turbine flow —
        // low-Pc designs (< 1 MPa) discharging to ambient may not reach
        // π_crit. The audit calls this out explicitly.
        double piCrit = System.Math.Pow(
            2.0 / (gamma + 1.0), gamma / (gamma - 1.0));
        bool isChoked = pr <= piCrit;

        bool tempOK = tapT <= TurbineInletTempLimit_K;
        bool powerOK = P_avail >= requiredPumpShaftPower_W;

        // PH-49: note whether the axial-station T differs from the flat Tc.
        string stationNote = localGasTemperature_K.HasValue
            ? $"local station T {stationT:F0} K"
            : $"chamber T {chamberTemperature_K:F0} K";
        string notes = tempOK
            ? $"Tap-off open cycle — {TapMassFlowFraction:P0} tap at "
            + $"T_tap ≈ {tapT:F0} K ({BoundaryLayerFraction:P0} of {stationNote}) "
            + $"drives turbine; discharge dumps overboard."
            : $"Tap-off infeasible: T_tap ≈ {tapT:F0} K exceeds uncooled-wheel "
            + $"limit {TurbineInletTempLimit_K:F0} K. Lower chamber Pc, boost film-cooling "
            + $"fraction, or switch to a preburner cycle.";

        return new TapOffTurbineResult(
            ChamberTemperature_K:        chamberTemperature_K,
            TapPointTemperature_K:       tapT,
            TurbineInletLimit_K:         TurbineInletTempLimit_K,
            TapPointTemperatureOK:       tempOK,
            TapMassFlow_kgs:             tapMassFlow,
            TotalMassFlow_kgs:           totalMassFlow_kgs,
            ChamberPressure_Pa:          chamberPressure_Pa,
            OutletPressure_Pa:           AmbientBackPressure_Pa,
            Cp_Jkg_K:                    cp,
            EffectiveGamma:              gamma,
            IsentropicSpecificWork_Jkg:  w_isen,
            ActualSpecificWork_Jkg:      w,
            Efficiency:                  efficiency,
            AvailableShaftPower_W:       P_avail,
            RequiredShaftPower_W:        requiredPumpShaftPower_W,
            PowerSufficient:             powerOK,
            Notes:                       notes,
            CriticalPressureRatio:       piCrit,
            IsChoked:                    isChoked);
    }
}
