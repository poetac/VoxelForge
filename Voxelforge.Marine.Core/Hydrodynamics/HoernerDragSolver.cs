// HoernerDragSolver.cs — empirical drag for streamlined axisymmetric body.
//
// Decomposes total hull drag into skin-friction + form-drag components
// using Hoerner's (1965) correlations for streamlined bodies.
//
// Formulas (Hoerner 1965, Fluid-Dynamic Drag §6-2):
//   Turbulent skin friction (Prandtl-Schlichting):
//     C_f = 0.455 / (log10(Re_L))^2.58
//   Form drag (slender body):
//     C_D_form = C_f × (1 + 1.5×(D/L)^1.5 + 7×(D/L)^3)
//   Drag force (based on frontal area π/4×D²):
//     F_drag = 0.5 × ρ × V² × (π/4×D²) × C_D_form × k_app
//
// Appendage interference factor k_app = 1.0 for bare AUV hull (M1).
// Wave-1 scope; appendage drag (k_app > 1) deferred to Wave 2+ M6.
//
// References:
//   Hoerner, S. F. (1965). Fluid-Dynamic Drag. Hoerner Fluid Dynamics. §6.
//   Schlichting, H. (1979). Boundary Layer Theory (7th ed.). §21.3.

using System;

namespace Voxelforge.Marine.Hydrodynamics;

/// <summary>
/// Result of the Hoerner drag decomposition.
/// </summary>
public sealed record DragResult(
    double DragForce_N,
    double DragCoefficient,
    double ReynoldsNumber,
    double SkinFrictionCoefficient,
    double FormDragCoefficient);

/// <summary>
/// Computes empirical hull drag for a Myring-faired streamlined body.
/// </summary>
public static class HoernerDragSolver
{
    private const double AppendageInterferenceFactor = 1.0; // k_app for bare hull

    /// <summary>
    /// Solve for drag at the cruise speed specified in <paramref name="cond"/>.
    /// </summary>
    public static DragResult Solve(FairingGeometry fairing, MarineConditions cond)
    {
        if (fairing is null) throw new ArgumentNullException(nameof(fairing));
        if (cond is null) throw new ArgumentNullException(nameof(cond));

        double l = fairing.Length_m;
        double d = fairing.Diameter_m;
        double v = cond.CruiseSpeed_ms;
        double rho = cond.WaterDensity_kgm3;
        double nu = MarineConditions.KinematicViscosity_m2s;

        // Reynolds number based on hull length
        double re = v * l / nu;
        if (re <= 0) throw new InvalidOperationException("Re_L must be > 0.");

        // Prandtl-Schlichting turbulent skin friction
        double logRe = Math.Log10(re);
        double cf = 0.455 / Math.Pow(logRe, 2.58);

        // Hoerner §6-2 form-drag factor for streamlined body
        double dOverL = d / l;
        double cdForm = cf * (1.0 + 1.5 * Math.Pow(dOverL, 1.5) + 7.0 * Math.Pow(dOverL, 3.0));

        // Total C_D (frontal-area based) with appendage factor
        double cdTotal = cdForm * AppendageInterferenceFactor;

        // Frontal area A_f = π/4 × D² — used for C_D reporting convention
        double aFrontal = Math.PI / 4.0 * d * d;

        // Drag force referenced to wetted area (Hoerner §6 — S_wet is the correct
        // reference for skin-friction + form drag on a streamlined body of revolution)
        double sWet  = fairing.WettedArea_m2;
        double fDrag = 0.5 * rho * v * v * sWet * cdTotal;

        // Report C_D based on frontal area (AUV convention)
        double cdFrontal = cdTotal * sWet / aFrontal;

        return new DragResult(
            DragForce_N:             fDrag,
            DragCoefficient:         cdFrontal,
            ReynoldsNumber:          re,
            SkinFrictionCoefficient: cf,
            FormDragCoefficient:     cdForm);
    }
}
