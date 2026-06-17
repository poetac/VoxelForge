// PintleElement.cs — TIER B.7 (2026-04-21): pintle injector element.
//
// Classical pintle geometry: a central post ("pintle") emits one propellant
// radially through an annular gap of height h_gap around its perimeter;
// the second propellant enters axially through N sleeve holes surrounding
// the pintle and impinges on the radial sheet.
//
// Sizing (single-element, single pintle — SpaceX Merlin / LMDE style):
//   • Annular ox sheet: Q_ox = Cd_ox · (π · D_pintle · h_gap) · √(2·ΔP/ρ_ox)
//     → h_gap = Q_ox / (Cd_ox · π · D_pintle · v_ox)
//   • Sleeve fuel holes: N_sleeve jets drilled around the pintle at
//     diameter d_sleeve. Area per hole = a_f = A_f / N_sleeve.
//     Conventional choice: N_sleeve = 12–24 depending on thrust.
//   • Blockage factor BL = N · d_sleeve / (π · D_pintle):
//     BL ≈ 0.5–0.8 gives stable combustion (Heister 2011). We target 0.6.
//   • Impingement half-angle fixed at 90° (radial sheet meets axial jets).
//
// The "element count" on the InjectorPattern must be 1 for a pintle (one
// pintle per chamber); Size() will return correct per-pintle areas.
// Pattern-level validation happens in SizePattern, not here.
//
// References:
//   Heister & Zarchan (eds.), "Pintle Injectors", AIAA Progress Series 260, 2017.
//   Dressler, "TRW Pintle Engine Heritage & Performance Characteristics",
//     AIAA 2000-3871.
//   SpaceX Merlin pintle documentation (public).

namespace Voxelforge.Injector.Elements;

public sealed class PintleElement : IInjectorElement
{
    public string ElementType => "Pintle";
    public bool IsImplemented => true;

    // Sprint 18 (2026-04-23): the three pintle knobs (diameter, sleeve
    // hole count, blockage target) used to live as instance properties
    // on this class. They now come through `SizingInputs` so the
    // InjectorPattern record owns them end-to-end (SA-tunable, UI-
    // settable, JSON-persistable via the pattern). The pre-Sprint-18
    // defaults (12 mm / 18 holes / 0.60) live on the SizingInputs
    // record, so a non-Pintle caller that instantiates a bare
    // PintleElement with default inputs still reproduces pre-Sprint-18
    // sizing bit-identically.

    public OrificeResult Size(SizingInputs inp)
    {
        double vOx   = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.OxDensity_kgm3,   inp.CdOx);
        double vFuel = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        // Annular ox gap sized to pass the per-pintle ox flow at the
        // requested ΔP. D_pintle from the inputs; h_gap follows.
        double D_p_mm = inp.PintleDiameter_mm;
        double D_p_m = System.Math.Max(D_p_mm * 1e-3, 1e-4);
        double A_ox_m2 = OrificeModel.OrificeArea_m2(
            inp.OxFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.OxDensity_kgm3, inp.CdOx);
        double h_gap_m = A_ox_m2 / (System.Math.PI * D_p_m);
        double h_gap_mm = h_gap_m * 1e3;

        // Fuel sleeve holes: total area from flow rate, divided across N.
        double A_fuel_m2 = OrificeModel.OrificeArea_m2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);
        int N = System.Math.Max(inp.PintleSleeveHoleCount, 1);
        double a_fuel_per_m2 = A_fuel_m2 / N;
        double d_sleeve_mm = 2.0 * System.Math.Sqrt(a_fuel_per_m2 / System.Math.PI) * 1e3;
        double blockage = N * d_sleeve_mm / (System.Math.PI * D_p_mm);

        double velRatio = vFuel / System.Math.Max(vOx, 1e-9);
        double momRatio = (inp.FuelFlowPerElement_kgs * vFuel)
                        / System.Math.Max(inp.OxFlowPerElement_kgs * vOx, 1e-9);

        var notes = new System.Collections.Generic.List<string>
        {
            $"Pintle Ø = {D_p_mm:F1} mm.  Annular gap h = {h_gap_mm:F2} mm.",
            $"{N} sleeve holes Ø = {d_sleeve_mm:F2} mm.  Blockage factor = {blockage:F2}.",
        };
        if (h_gap_mm < 0.2)
            notes.Add("WARNING: annular gap < 0.20 mm — below LPBF min feature; increase pintle diameter.");
        if (blockage < 0.4 || blockage > 0.85)
            notes.Add($"NOTE: Blockage factor {blockage:F2} outside stable band [0.40, 0.85] (Dressler). "
                    + $"Target was {inp.PintleBlockageFractionTarget:F2} — consider tuning PintleDiameter_mm "
                    + $"or PintleSleeveHoleCount. A feasibility gate (PINTLE_BLOCKAGE_OUT_OF_BAND) also fires.");

        return new OrificeResult(
            OxOrificeArea_mm2:   A_ox_m2 * 1e6,
            FuelOrificeArea_mm2: A_fuel_m2 * 1e6,
            OxVelocity_ms:   vOx,
            FuelVelocity_ms: vFuel,
            VelocityRatio:   velRatio,
            MomentumRatio:   momRatio,
            Notes: notes.ToArray(),
            PintleBlockageFraction: blockage);
    }
}
