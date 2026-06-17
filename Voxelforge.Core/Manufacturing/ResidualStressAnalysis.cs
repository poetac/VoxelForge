// ResidualStressAnalysis.cs — TIER B.6 (2026-04-21):
//
// Coarse inherent-strain-method estimate of residual stress + thermal
// distortion after LPBF build + heat treatment. Informational; not a
// substitute for Ansys Mechanical or Simufact.
//
// Model outline (simplified Papadakis / NASA-STD LPBF handbook):
//   1. Inherent strain ε_in = α · ΔT_proc + ε_plastic
//      where ΔT_proc ≈ 1000 K is the laser melt-pool quench and
//      ε_plastic ≈ 0.002 for Cu alloys, 0.0035 for Ni superalloys
//      (Inherent-Strain-Method literature fit).
//   2. Longitudinal shrinkage δL ≈ ε_in · L for a slender axisymmetric
//      body printed throat-up (nozzle-first).
//   3. Radial bow δR_max ≈ 0.5 · ε_in · R_max · (L / R_max)² for thin
//      walls with L/R > 3. Saturates at 0.5 % of L. Uses material-specific
//      creep relaxation after stress-relief heat treatment.
//   4. Post-HT relaxation fraction: 0.6 (GRCop, CuCrZr), 0.4 (Inconel).
//      Residual ε left after standard SR + HIP.
//
// Outputs are for design-sensitivity scoping. Print vendors will fit
// their own process models to these numbers; serving as a conversation
// starter more than a final tolerance.

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Manufacturing;

public sealed record ResidualStressResult(
    double InherentStrain,           // ε_in, dimensionless
    double LongitudinalShrink_mm,    // predicted δL after build
    double RadialBowMax_mm,          // peak radial deviation after build
    double ResidualStrainAfterHT,    // ε left after stress-relief / HIP
    double ResidualStressEstimate_MPa,  // σ_res = E · ε_residual
    double MarginToYieldCold,        // σ_y(300K) / σ_res (pre-firing margin)
    string BuildRecommendation,
    string[] Warnings);

public static class ResidualStressAnalysis
{
    /// <summary>Plastic inherent-strain contribution (m/m) typical for Cu LPBF.</summary>
    public const double EpsPlasticCopper = 0.0020;
    /// <summary>Plastic inherent-strain contribution (m/m) typical for Ni LPBF.</summary>
    public const double EpsPlasticNickel = 0.0035;
    /// <summary>Process ΔT used in α·ΔT term; LPBF melt-pool quench.</summary>
    public const double ProcessDeltaT_K = 1000.0;
    /// <summary>Fraction of inherent strain left after stress-relief + HIP (Cu).</summary>
    public const double RelaxationFractionCopper = 0.40;
    /// <summary>Fraction of inherent strain left after SR + HIP (Ni superalloys).</summary>
    public const double RelaxationFractionNickel = 0.60;

    public static ResidualStressResult Analyze(
        ChamberContour contour, WallMaterial material)
    {
        var warnings = new List<string>();

        // Classify Cu vs Ni by material name (simple but robust given our
        // fixed-enumeration materials; extend when more alloys are added).
        bool isCopper = material.Name.Contains("Cu", StringComparison.OrdinalIgnoreCase)
                     || material.Name.Contains("GRCop", StringComparison.OrdinalIgnoreCase);
        double epsPlastic = isCopper ? EpsPlasticCopper : EpsPlasticNickel;
        double relaxFrac  = isCopper ? RelaxationFractionCopper : RelaxationFractionNickel;

        double epsIn = material.CTE_perK * ProcessDeltaT_K + epsPlastic;

        double L_mm = contour.TotalLength_mm;
        double Rmax_mm = 0;
        foreach (var st in contour.Stations)
            if (st.R_mm > Rmax_mm) Rmax_mm = st.R_mm;

        double deltaL_mm = epsIn * L_mm;
        double slenderness = Rmax_mm > 0 ? L_mm / Rmax_mm : 0;
        double deltaR_mm = 0.5 * epsIn * Rmax_mm * slenderness * slenderness;
        // Cap radial bow at 0.5 % of L (literature upper bound for a
        // well-controlled LPBF build; larger values mean the build failed).
        deltaR_mm = System.Math.Min(deltaR_mm, 0.005 * L_mm);

        double epsResidual = epsIn * (1.0 - relaxFrac);
        double sigmaResidual_MPa = epsResidual * material.ElasticModulusCold_GPa * 1000.0;
        double marginCold = material.YieldStrengthCold_MPa
                          / System.Math.Max(sigmaResidual_MPa, 1e-3);

        string rec;
        if (deltaR_mm > 0.5)
        {
            rec = "Predicted radial bow > 0.5 mm — orient throat-up and HIP after build.";
            warnings.Add(rec);
        }
        else if (slenderness > 5.0)
        {
            rec = $"Slenderness L/R = {slenderness:F1} is high — recommend throat-up orientation + support rings.";
        }
        else
        {
            rec = "Residual stress envelope acceptable for a standard HIP-then-SR cycle.";
        }

        if (marginCold < 1.5)
            warnings.Add($"Residual σ {sigmaResidual_MPa:F0} MPa leaves only {marginCold:F2}× margin to cold yield — consider a thicker wall or a different alloy.");

        return new ResidualStressResult(
            InherentStrain:              epsIn,
            LongitudinalShrink_mm:       deltaL_mm,
            RadialBowMax_mm:             deltaR_mm,
            ResidualStrainAfterHT:       epsResidual,
            ResidualStressEstimate_MPa:  sigmaResidual_MPa,
            MarginToYieldCold:           marginCold,
            BuildRecommendation:         rec,
            Warnings:                    warnings.ToArray());
    }
}
