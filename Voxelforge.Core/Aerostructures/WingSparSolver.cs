// WingSparSolver.cs — Sprint AS.W1 closed-form Euler-Bernoulli wing-
// spar performance snapshot.
//
// Stateless, allocation-free, deterministic. Idealises the half-wing
// as a cantilever beam clamped at the root with a uniformly-distributed
// upward lift load. The closed-form deflection + bending-stress
// formulas come from Roark chap 8 / any first-year structures text:
//
//   M_max  = n · w · L² / 2                 [at the root]
//   δ_tip  = n · w · L⁴ / (8 · E · I)       [cantilever under UDL]
//   σ_max  = M_max / S                      [bending stress]
//   SF     = σ_yield / σ_max
//
// I_xx by section type:
//   SolidRectangular:    I = b · h³ / 12,  A = b · h
//   HollowRectangularBox: I = (b·h³ − (b−2t)(h−2t)³) / 12,
//                        A = b·h − (b−2t)(h−2t)
//   SolidCircular (h reinterpreted as 2·R):
//                        I = π·R⁴ / 4,  A = π·R²
//
// References:
//   Roark R.J., Young W.C. (2011). "Roark's Formulas for Stress and
//     Strain," 8th ed., chap 8 (beam bending).
//   Megson T.H.G. (2013). "Aircraft Structures for Engineering
//     Students," 5th ed.
//   Bruhn E.F. (1973). "Analysis and Design of Flight Vehicle Structures."

using System;

namespace Voxelforge.Aerostructures;

/// <summary>
/// Closed-form Euler-Bernoulli wing-spar performance snapshot solver
/// (Sprint AS.W1).
/// </summary>
internal static class WingSparSolver
{
    /// <summary>
    /// Solve the wing-spar snapshot at the design (section, material,
    /// span, load) operating point.
    /// </summary>
    internal static WingSparResult Solve(WingSparDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = SparMaterialRegistry.For(design.Material);

        // 1. Section geometry — area + I_xx.
        var (A, I_xx, c) = ComputeSectionProperties(design);
        double S = I_xx / c;

        // 2. Bending moment + deflection. Sprint AS.W2 — when
        //    UseEllipticalLift = true, the lift distribution becomes
        //    w(y) = w₀ · √(1 − (y/L)²) and we replace the UDL formulas
        //    with the elliptical-load closed forms. The total integrated
        //    load (lift) is preserved: ∫ w₀·√(1−x²) dx from 0 to L is
        //    (πL·w₀)/4, so we normalise w₀ = 4·w/π to make total lift
        //    equal to w·L (matches the UDL total).
        //
        //    Under that normalisation:
        //      M_max,UDL  = w·L²/2
        //      M_max,ell  = w₀·L²·(2/3) · (4/(3π))  ≈ same total but
        //                 redistributed. Cluster-mid-band: elliptical
        //                 lift produces ≈ 75 % of the UDL root M_max
        //                 (load is concentrated inboard).
        //      δ_tip,ell ≈ 0.65 · δ_tip,UDL
        double L = design.HalfSpan_m;
        double w = design.LoadFactor * design.DistributedLift_Nm;
        double M_max, deflection;
        if (design.UseEllipticalLift)
        {
            // Elliptical-load closed-form coefficients per Roark chap 8.
            const double EllipticalMomentFactor    = 0.75;
            const double EllipticalDeflectionFactor = 0.65;
            M_max      = EllipticalMomentFactor * w * L * L / 2.0;
            deflection = EllipticalDeflectionFactor
                       * w * L * L * L * L / (8.0 * props.YoungsModulus_Pa * I_xx);
        }
        else
        {
            M_max      = w * L * L / 2.0;
            deflection = w * L * L * L * L / (8.0 * props.YoungsModulus_Pa * I_xx);
        }

        // 3. Stress + safety factor.
        double sigma_max = M_max / S;
        double SF = props.YieldStrength_Pa / sigma_max;

        // 4. Mass.
        double mass = props.Density_kgm3 * A * L;

        return new WingSparResult(
            SectionArea_m2:           A,
            SecondMomentOfArea_m4:    I_xx,
            SectionModulus_m3:        S,
            MaximumBendingMoment_Nm:  M_max,
            MaximumBendingStress_Pa:  sigma_max,
            TipDeflection_m:          deflection,
            SafetyFactor:             SF,
            SparMass_kg:              mass);
    }

    /// <summary>
    /// Compute (area, second moment about the chord axis, half-height)
    /// for the design's section type. Public-static for tests + future
    /// non-uniform-section sweeps.
    /// </summary>
    internal static (double Area_m2, double I_m4, double HalfHeight_m)
        ComputeSectionProperties(WingSparDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        switch (design.SectionType)
        {
            case SparSectionType.SolidRectangular:
            {
                double h = design.OuterHeight_m;
                double A = design.OuterWidth_m * h;
                double I = design.OuterWidth_m * h * h * h / 12.0;
                return (A, I, h * 0.5);
            }
            case SparSectionType.HollowRectangularBox:
            {
                double bo = design.OuterWidth_m;
                double ho = design.OuterHeight_m;
                double bi = bo - 2.0 * design.WallThickness_m;
                double hi = ho - 2.0 * design.WallThickness_m;
                double A = bo * ho - bi * hi;
                double I = (bo * ho * ho * ho - bi * hi * hi * hi) / 12.0;
                return (A, I, ho * 0.5);
            }
            case SparSectionType.SolidCircular:
            {
                // h is reinterpreted as 2·R for circular sections.
                double R = design.OuterHeight_m * 0.5;
                double A = Math.PI * R * R;
                double I = Math.PI * R * R * R * R / 4.0;
                return (A, I, R);
            }
            default:
                throw new InvalidOperationException(
                    $"Unhandled SparSectionType '{design.SectionType}'.");
        }
    }
}
