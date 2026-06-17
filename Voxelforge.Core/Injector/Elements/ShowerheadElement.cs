// ShowerheadElement.cs — TIER B.7 (2026-04-21): showerhead element.
//
// A showerhead injects each propellant axially through its own set of
// orifices (no impingement). Classical heritage pattern — simple to
// manufacture, forgiving on atomisation, moderate c* efficiency.
//
// Per element (one showerhead "cell" = one ox hole + one fuel hole):
//   A = ṁ / (Cd · √(2·ρ·ΔP))       for each side independently.
//
// Because both sides are axial straight jets, atomisation is driven by
// shear against ambient gas rather than jet-on-jet impact. The classical
// SMD scaling is ~30 % worse than an impinging doublet at the same
// velocity; we reflect that by stamping a note and a softer velocity
// band, but don't change the area math (caller is still responsible for
// sanity-checking via scoring penalties).
//
// References:
//   Sutton & Biblarz RPE 9e §9.4 ("Injector types").
//   Huzel & Huang, AIAA Vol. 147, §8.1 (non-impinging orifice sizing).

namespace Voxelforge.Injector.Elements;

public sealed class ShowerheadElement : IInjectorElement
{
    public string ElementType => "Showerhead";
    public bool IsImplemented => true;

    public OrificeResult Size(SizingInputs inp)
    {
        double vOx   = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.OxDensity_kgm3,   inp.CdOx);
        double vFuel = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        double aOx_mm2 = OrificeModel.OrificeArea_mm2(
            inp.OxFlowPerElement_kgs,   inp.DeltaPInj_Pa, inp.OxDensity_kgm3,   inp.CdOx);
        double aFuel_mm2 = OrificeModel.OrificeArea_mm2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        double velRatio = vFuel / System.Math.Max(vOx, 1e-9);
        double momRatio = (inp.FuelFlowPerElement_kgs * vFuel)
                        / System.Math.Max(inp.OxFlowPerElement_kgs * vOx, 1e-9);

        double dOx_mm   = 2.0 * System.Math.Sqrt(aOx_mm2   / System.Math.PI);
        double dFuel_mm = 2.0 * System.Math.Sqrt(aFuel_mm2 / System.Math.PI);

        var notes = new System.Collections.Generic.List<string>
        {
            $"Ox hole Ø = {dOx_mm:F2} mm.  Fuel hole Ø = {dFuel_mm:F2} mm.",
            "Non-impinging (showerhead) — atomisation is shear-driven; SMD ~30 % worse than doublet at same v.",
        };
        if (aOx_mm2 < 0.3 || aFuel_mm2 < 0.3)
            notes.Add("WARNING: hole area < 0.30 mm² — below LPBF min feature.");

        return new OrificeResult(
            OxOrificeArea_mm2:   aOx_mm2,
            FuelOrificeArea_mm2: aFuel_mm2,
            OxVelocity_ms:   vOx,
            FuelVelocity_ms: vFuel,
            VelocityRatio:   velRatio,
            MomentumRatio:   momRatio,
            Notes: notes.ToArray());
    }
}
