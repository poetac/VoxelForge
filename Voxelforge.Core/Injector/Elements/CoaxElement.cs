// CoaxElement.cs — Coaxial shear injector element.
//
// Geometry: a central LOX post (solid cylinder, Ø_ox) is surrounded by an
// annular fuel passage (inner radius R_ox_post + wall, outer radius R_fuel).
// Combustion is driven by shear between the high-velocity fuel annulus and
// the slower LOX core. Widely used in LOX/CH4 and LOX/H2 engines.
//
//   LOX port:  single circular orifice, area = OxOrificeArea_mm²
//   Fuel port: annular orifice, area = FuelOrificeArea_mm²
//              inner radius = R_ox_outer + post_wall (default 0.4 mm wall)
//              outer radius derived from inner + area / (2π·R_inner approx)
//
// Preliminary-design fidelity: areas from the sharp-edge orifice formula;
// no breakup model, no gas-gas coaxial, no recess ratio, no face recess.
//
// References:
//   Sutton & Biblarz RPE 9e §9.5; Huzel & Huang AIAA Vol. 147 §8.1.

using Voxelforge.Injector;

namespace Voxelforge.Injector.Elements;

public sealed class CoaxElement : IInjectorElement
{
    /// <summary>
    /// Wall thickness separating the LOX post outer surface from the
    /// inner face of the fuel annulus. Typical: 0.3–0.8 mm for AM parts.
    /// </summary>
    public double PostWallThickness_mm { get; init; } = 0.40;

    public string ElementType => "Coax";
    public bool IsImplemented => true;

    public OrificeResult Size(SizingInputs inp)
    {
        double vOx   = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.OxDensity_kgm3, inp.CdOx);
        double vFuel = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        double aOx_mm2   = OrificeModel.OrificeArea_mm2(
            inp.OxFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.OxDensity_kgm3, inp.CdOx);
        double aFuel_mm2 = OrificeModel.OrificeArea_mm2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        double velRatio  = vFuel / System.Math.Max(vOx, 1e-9);
        double momRatio  = (inp.FuelFlowPerElement_kgs * vFuel)
                         / System.Math.Max(inp.OxFlowPerElement_kgs * vOx, 1e-9);

        var notes = new System.Collections.Generic.List<string>
        {
            $"LOX post Ø = {2.0 * System.Math.Sqrt(aOx_mm2 / System.Math.PI):F2} mm equiv.",
            $"Fuel annulus area = {aFuel_mm2:F2} mm²."
        };

        if (aOx_mm2 < 0.3)
            notes.Add("WARNING: LOX orifice < 0.3 mm² — may be below LPBF min feature.");
        if (aFuel_mm2 < 0.3)
            notes.Add("WARNING: Fuel orifice < 0.3 mm² — may be below LPBF min feature.");
        if (velRatio < 0.5 || velRatio > 4.0)
            notes.Add($"NOTE: Velocity ratio {velRatio:F2} outside typical 0.5–4.0 band.");

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
