// PressureHullBuckling.cs — external-pressure buckling for thin cylindrical shell.
//
// ASME BPVC §VIII Division 1 UG-28 simplified formula for long cylinders under
// uniform external pressure. Uses the Windenburg-Trilling (1934) elastic
// thin-shell collapse formula as the underlying calculation.
//
// Formula (long cylinder, L/D > 4):
//   P_cr = 2E × (t/D)^3 / (1 − ν²)
//
// Safety factor:
//   SF = P_cr / P_hydrostatic    (must be ≥ 1.5 per PRESSURE_HULL_SF gate)
//
// Material elastic properties (distinct from hydrostaticequilibrium densities):
//   Ti-6Al-4V : E = 113.8 GPa, ν = 0.342
//   Al-6061   : E = 68.9 GPa,  ν = 0.330
//   AISI-316L : E = 193.0 GPa, ν = 0.270 (LPBF-grade)
//
// References:
//   ASME BPVC §VIII Div 1 UG-28 (2023 edition)
//   Windenburg, D. F. & Trilling, C. (1934). NACA-TN-517, eq.(5).

using System;

namespace Voxelforge.Marine.Structure;

/// <summary>
/// Result of the external-pressure buckling analysis.
/// </summary>
public sealed record BucklingResult(
    double CriticalBucklingPressure_Pa,
    double BucklingSafetyFactor,
    double YoungModulus_Pa,
    double PoissonRatio);

/// <summary>
/// Computes elastic thin-shell external-pressure buckling per ASME UG-28.
/// </summary>
public static class PressureHullBuckling
{
    // E [Pa], ν [-] for each MaterialIndex
    private static readonly double[] YoungModulus_Pa = { 113.8e9, 68.9e9, 193.0e9 };
    private static readonly double[] PoissonRatio    = { 0.342,   0.330,  0.270   };

    /// <summary>
    /// Compute critical external pressure and safety factor.
    /// </summary>
    public static BucklingResult Solve(MarineDesign design, MarineConditions cond)
    {
        if (design is null) throw new ArgumentNullException(nameof(design));
        if (cond is null) throw new ArgumentNullException(nameof(cond));

        int matIdx = Math.Clamp(design.MaterialIndex, 0, YoungModulus_Pa.Length - 1);
        double e  = YoungModulus_Pa[matIdx];
        double nu = PoissonRatio[matIdx];
        double t  = design.WallThickness_m;
        double d  = design.Diameter_m;

        // Windenburg-Trilling thin-shell collapse (long-cylinder limit L/D > 4):
        // P_cr = 2E × (t/D)^3 / (1 − ν²)
        double tOverD = t / d;
        double pCr = 2.0 * e * Math.Pow(tOverD, 3) / (1.0 - nu * nu);

        double pHydro = cond.HydrostaticPressure_Pa;
        double sf = pHydro > 0 ? pCr / pHydro : double.PositiveInfinity;

        return new BucklingResult(
            CriticalBucklingPressure_Pa: pCr,
            BucklingSafetyFactor:        sf,
            YoungModulus_Pa:             e,
            PoissonRatio:                nu);
    }
}
