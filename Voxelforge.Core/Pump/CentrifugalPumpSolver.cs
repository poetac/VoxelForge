// CentrifugalPumpSolver.cs — Sprint PMP.W1 closed-form centrifugal
// pump performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes hydraulic +
// shaft power, specific speed, and NPSH_a / NPSH_r / cavitation
// margin at the design operating point.
//
//   P_hyd  = ρ · g · Q · H
//   P_shaft = P_hyd / η_pump
//   N_s    = ω · √Q / (g · H)^0.75      [SI dimensionless form]
//   NPSH_a = (P_inlet − p_vapour) / (ρ · g) − z_lift − h_friction
//   NPSH_r = 0.10 · H · (N_s / 0.5)^(4/3)   [Thoma cluster fit]
//
// References:
//   Karassik I.J. et al. (2008). "Pump Handbook," 4th ed.
//   Gülich J.F. (2010). "Centrifugal Pumps," 2nd ed., chap 6 (cavitation).
//   Stepanoff A.J. (1957). "Centrifugal and Axial Flow Pumps."

using System;

namespace Voxelforge.Pump;

/// <summary>
/// Closed-form centrifugal pump performance snapshot solver
/// (Sprint PMP.W1).
/// </summary>
internal static class CentrifugalPumpSolver
{
    /// <summary>Standard gravity [m/s²].</summary>
    internal const double G0_ms2 = 9.80665;

    /// <summary>
    /// Thoma-style NPSH_r cluster-fit coefficient [-]. NPSH_r scales as
    /// 0.05 · H · (N_s / N_s_ref)^(4/3) where N_s_ref = 0.5 is the
    /// best-efficiency radial-flow cluster centroid. The 0.05 anchor
    /// is the cluster mid-band for commercial single-stage centrifugals
    /// at the BEP; pumps designed for low NPSH_r (cryo / suction-
    /// limited) drop to ~ 0.02, while older / off-design pumps run up
    /// to ~ 0.10.
    /// </summary>
    internal const double ThomaCoefficient = 0.05;

    /// <summary>Reference specific speed for the NPSH_r cluster fit.</summary>
    internal const double NpshrSpecificSpeedReference = 0.5;

    /// <summary>
    /// Solve the centrifugal pump performance snapshot at the design
    /// operating point.
    /// </summary>
    internal static CentrifugalPumpResult Solve(CentrifugalPumpDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // 1. Hydraulic + shaft power.
        double P_hyd = design.FluidDensity_kgm3
                     * G0_ms2
                     * design.VolumetricFlowRate_m3s
                     * design.HeadRise_m;
        double P_shaft = P_hyd / design.OverallEfficiency;

        // 2. Specific speed N_s = ω·√Q / (g·H)^0.75.
        double omega = 2.0 * Math.PI * design.RotationSpeed_rpm / 60.0;
        double N_s = omega * Math.Sqrt(design.VolumetricFlowRate_m3s)
                   / Math.Pow(G0_ms2 * design.HeadRise_m, 0.75);

        // 3. NPSH balance.
        double npsh_a = (design.InletStaticPressure_Pa - design.FluidVapourPressure_Pa)
                      / (design.FluidDensity_kgm3 * G0_ms2)
                      - design.InletElevationLift_m
                      - design.InletFrictionLoss_m;
        double npsh_r = ThomaCoefficient
                      * design.HeadRise_m
                      * Math.Pow(N_s / NpshrSpecificSpeedReference, 4.0 / 3.0);
        double margin = npsh_a - npsh_r;

        return new CentrifugalPumpResult(
            HydraulicPower_W:                   P_hyd,
            ShaftPowerInput_W:                  P_shaft,
            SpecificSpeedSI:                    N_s,
            NetPositiveSuctionHeadAvailable_m:  npsh_a,
            NetPositiveSuctionHeadRequired_m:   npsh_r,
            CavitationMargin_m:                 margin);
    }

    /// <summary>
    /// Sprint PMP.W2. For a positive-displacement pump, compute the
    /// volumetric flow rate from the geometric displacement-per-
    /// revolution + rotation speed:
    ///
    ///   Q [m³/s] = (V_displacement [m³/rev]) · (N [rpm] / 60)
    ///            · η_volumetric
    ///
    /// where η_volumetric ∈ (0.85, 0.98] captures internal slip /
    /// leakage past tight-clearance seals. Defaults to 0.95.
    /// </summary>
    /// <param name="displacementPerRevolution_m3">V [m³/rev].</param>
    /// <param name="rotationSpeed_rpm">N [rpm].</param>
    /// <param name="volumetricEfficiency">η_vol [-]. Default 0.95.</param>
    /// <returns>Q [m³/s].</returns>
    internal static double ComputePositiveDisplacementFlow(
        double displacementPerRevolution_m3,
        double rotationSpeed_rpm,
        double volumetricEfficiency = 0.95)
    {
        if (displacementPerRevolution_m3 <= 0)
            throw new ArgumentOutOfRangeException(nameof(displacementPerRevolution_m3),
                "V must be > 0.");
        if (rotationSpeed_rpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(rotationSpeed_rpm),
                "N must be > 0.");
        if (volumetricEfficiency <= 0 || volumetricEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(volumetricEfficiency),
                "η_vol must be in (0, 1].");
        return displacementPerRevolution_m3 * (rotationSpeed_rpm / 60.0) * volumetricEfficiency;
    }

    /// <summary>
    /// Apply the centrifugal-pump affinity laws to scale (Q, H, P) from
    /// one operating point to another at constant impeller diameter.
    /// Public-static helper for per-speed sizing studies.
    /// </summary>
    /// <param name="Q1">Reference flow rate [m³/s].</param>
    /// <param name="H1">Reference head [m].</param>
    /// <param name="P1">Reference shaft power [W].</param>
    /// <param name="N1">Reference rotation speed [rpm].</param>
    /// <param name="N2">Target rotation speed [rpm].</param>
    /// <returns>(Q₂, H₂, P₂) at N₂ — affinity-law-scaled values.</returns>
    internal static (double Q2, double H2, double P2) ApplyAffinityLaws(
        double Q1, double H1, double P1, double N1, double N2)
    {
        if (N1 <= 0)
            throw new ArgumentOutOfRangeException(nameof(N1), "N1 must be > 0.");
        if (N2 <= 0)
            throw new ArgumentOutOfRangeException(nameof(N2), "N2 must be > 0.");
        double ratio = N2 / N1;
        return (Q2: Q1 * ratio,
                H2: H1 * ratio * ratio,
                P2: P1 * ratio * ratio * ratio);
    }
}
