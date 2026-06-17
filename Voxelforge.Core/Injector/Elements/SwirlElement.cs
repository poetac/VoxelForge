// SwirlElement.cs — TIER B.7 (2026-04-21): centrifugal swirl injector element.
//
// A swirl element imparts tangential momentum through N tangential feed
// holes into a swirl chamber. The fluid exits the discharge orifice as a
// hollow conical sheet whose half-angle depends on the swirl number.
//
// Simplified Abramovich / Bazarov closed-form:
//   Swirl parameter K = A_tan · R_s / (A_out · r_out)
//     where A_tan = tangential-hole area, A_out = discharge area,
//     R_s = swirl radius (tangential-hole offset from axis),
//     r_out = discharge orifice radius.
//   Discharge coefficient μ drops with K:
//     μ ≈ 0.35 / √K        (Bazarov, for K ≥ 0.5)
//   Cone half-angle 2α:
//     sin(α) = 2·√(1 − μ²) / (1 + √(1 − μ²))       (Abramovich)
//   Orifice area sized from ṁ = μ · A_out · √(2·ρ·ΔP).
//
// MVP assumptions:
//   • Only the OXidiser side uses swirl; fuel uses a plain sharp-edge
//     orifice (matches most Russian staged-combustion usage).
//   • SwirlRatio is a user-specified design knob; K = 2.0 is a sensible
//     default that gives ~60° cone half-angle + μ ≈ 0.25.
//   • Cd_ox on the SizingInputs is ignored for the ox side (swirl number
//     sets its own discharge coefficient); Cd_fuel is used as-is.
//
// References:
//   Bazarov, Yang, Puri (eds.), "Design and Dynamics of Jet and Swirl
//     Injectors", AIAA Progress Vol. 200, 2004, Ch. 2.
//   Abramovich, "Applied Gas Dynamics", Nauka 1969, Ch. 21.
//   Lefebvre & McDonell, "Atomization and Sprays", 2017, §6.

namespace Voxelforge.Injector.Elements;

public sealed class SwirlElement : IInjectorElement
{
    /// <summary>
    /// Swirl parameter K (dimensionless) used to pick the discharge
    /// coefficient. Typical 1.0–4.0. Higher K ⇒ wider cone + lower μ.
    /// </summary>
    public double SwirlParameter { get; init; } = 2.0;

    public string ElementType => "Swirl";
    public bool IsImplemented => true;

    public OrificeResult Size(SizingInputs inp)
    {
        double K = System.Math.Max(SwirlParameter, 0.2);
        // Bazarov fit for discharge coefficient.
        double muOx = System.Math.Min(0.35 / System.Math.Sqrt(K), 0.65);

        // Ox side: swirl discharge, mu from K instead of the 0.70 default.
        double vOx = System.Math.Sqrt(2.0 * inp.DeltaPInj_Pa / System.Math.Max(inp.OxDensity_kgm3, 1e-6));
        double A_ox_m2 = inp.OxFlowPerElement_kgs
                       / System.Math.Max(muOx * inp.OxDensity_kgm3 * vOx, 1e-9);

        // Fuel side: plain orifice at supplied Cd.
        double vFuel = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);
        double A_fuel_m2 = OrificeModel.OrificeArea_m2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        // Cone half-angle (Abramovich).
        double oneMinusMu2 = System.Math.Max(1.0 - muOx * muOx, 0);
        double sqrtTerm = System.Math.Sqrt(oneMinusMu2);
        double sinAlpha = 2.0 * sqrtTerm / (1.0 + sqrtTerm);
        double halfAngle_deg = System.Math.Asin(System.Math.Clamp(sinAlpha, 0, 1))
                             * 180.0 / System.Math.PI;

        double velRatio = vFuel / System.Math.Max(muOx * vOx, 1e-9);
        double momRatio = (inp.FuelFlowPerElement_kgs * vFuel)
                        / System.Math.Max(inp.OxFlowPerElement_kgs * muOx * vOx, 1e-9);

        var notes = new System.Collections.Generic.List<string>
        {
            $"Swirl K = {K:F2}, μ_ox = {muOx:F2}, cone half-angle = {halfAngle_deg:F0}°.",
            $"Ox orifice area = {A_ox_m2 * 1e6:F2} mm².  Fuel hole = {A_fuel_m2 * 1e6:F2} mm².",
        };
        if (halfAngle_deg > 75)
            notes.Add("NOTE: cone half-angle > 75° — may over-wet the chamber walls.");
        if (halfAngle_deg < 30)
            notes.Add("NOTE: cone half-angle < 30° — tight spray; consider raising SwirlParameter.");

        return new OrificeResult(
            OxOrificeArea_mm2:   A_ox_m2   * 1e6,
            FuelOrificeArea_mm2: A_fuel_m2 * 1e6,
            OxVelocity_ms:   muOx * vOx,
            FuelVelocity_ms: vFuel,
            VelocityRatio:   velRatio,
            MomentumRatio:   momRatio,
            Notes: notes.ToArray());
    }
}
