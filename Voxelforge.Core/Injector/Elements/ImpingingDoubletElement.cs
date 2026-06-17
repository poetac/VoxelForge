// ImpingingDoubletElement.cs — Unlike-impinging doublet injector element.
//
// Geometry: one oxidiser hole and one fuel hole drilled at angle ±θ/2 to
// the chamber axis so the jets impinge at a distance L_imp downstream
// of the injector face. Atomisation is driven by the momentum of the
// two jets at impact.
//
//   Ox hole:    area = OxOrificeArea_mm²  (one hole)
//   Fuel hole:  area = FuelOrificeArea_mm²  (one hole)
//   Total bores per element = 2
//
// The MR of the resultant mixture depends on the element ratio; here both
// holes carry their per-element share of the total flow.
//
// Preliminary-design fidelity:
//   • Sharp-edge orifice equation only. No breakup-length model.
//   • Impingement angle is stored as metadata but does not affect area sizing.
//   • Like-doublet (ox-ox or fuel-fuel) not modelled in this MVP.
//
// References:
//   Sutton & Biblarz RPE 9e §9.5; Huzel & Huang AIAA Vol. 147 §8.1–8.2.

using Voxelforge.Injector;

namespace Voxelforge.Injector.Elements;

public sealed class ImpingingDoubletElement : IInjectorElement
{
    /// <summary>
    /// Half-angle at which each jet is inclined toward the impingement
    /// point, measured from the chamber axis [degrees]. Typical: 15–30°.
    /// Kept as a class-level default but the design-time value is
    /// sourced from <see cref="SizingInputs.ImpingementHalfAngle_deg"/>
    /// so the user / optimiser can override it without rebuilding the
    /// element instance via the factory.
    /// </summary>
    public double ImpingementHalfAngle_deg { get; init; } = 20.0;

    public string ElementType => "ImpingingDoublet";
    public bool IsImplemented => true;

    public OrificeResult Size(SizingInputs inp)
    {
        double vOx   = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.OxDensity_kgm3, inp.CdOx);
        double vFuel = OrificeModel.JetVelocity_ms(inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        // Each element has ONE ox hole + ONE fuel hole.
        double aOx_mm2   = OrificeModel.OrificeArea_mm2(
            inp.OxFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.OxDensity_kgm3, inp.CdOx);
        double aFuel_mm2 = OrificeModel.OrificeArea_mm2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        double velRatio = vFuel / System.Math.Max(vOx, 1e-9);
        double momRatio = (inp.FuelFlowPerElement_kgs * vFuel)
                        / System.Math.Max(inp.OxFlowPerElement_kgs * vOx, 1e-9);

        double dOx_mm   = 2.0 * System.Math.Sqrt(aOx_mm2 / System.Math.PI);
        double dFuel_mm = 2.0 * System.Math.Sqrt(aFuel_mm2 / System.Math.PI);

        // Pull the impingement half-angle from SizingInputs so the
        // pattern can override it. Falls back to the class-level default
        // when SizingInputs uses its own default (20°). Clamped to a
        // physically sensible band [10°, 45°] — narrower than 10° gives
        // poor breakup, wider than 45° starts losing axial momentum.
        double angleDeg = System.Math.Clamp(
            inp.ImpingementHalfAngle_deg > 0 ? inp.ImpingementHalfAngle_deg : ImpingementHalfAngle_deg,
            10.0, 45.0);

        var notes = new System.Collections.Generic.List<string>
        {
            $"Ox hole Ø = {dOx_mm:F2} mm.  Fuel hole Ø = {dFuel_mm:F2} mm.",
            $"Half-angle = {angleDeg:F0}°.  Velocity ratio = {velRatio:F2}."
        };

        if (aOx_mm2 < 0.3 || aFuel_mm2 < 0.3)
            notes.Add("WARNING: one or more orifices < 0.3 mm² — may be below LPBF min feature.");
        if (System.Math.Abs(momRatio - 1.0) > 0.4)
            notes.Add($"NOTE: Momentum ratio {momRatio:F2} deviates >40% from 1.0 — poor atomization predicted.");

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
