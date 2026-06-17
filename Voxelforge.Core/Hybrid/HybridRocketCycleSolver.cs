// HybridRocketCycleSolver.cs — Sprint R.W2 closed-form hybrid-rocket
// performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes the per-snapshot
// regression rate, fuel mass flow, O/F ratio, c*, C_F, vacuum I_sp,
// and vacuum thrust for a hybrid rocket motor at a specified port
// radius. Two convenience entry points wrap the snapshot:
//
//   SolveInitial(design)    — snapshot at design.InitialPortRadius_m.
//   Solve(design, portRadius_m) — snapshot at an arbitrary port radius
//                                 (clamped to ≤ design.OuterGrainRadius_m).
//
// The classical Marxman regression-rate fit is taken from
// HybridFuelRegistry. c* and C_F are cluster-anchored for LOX/HTPB
// (a follow-on R.W3 sprint will lift these from a real CEA table).
//
// References:
//   Marxman G., Wooldridge C., Muzzy R. (1963). "Fundamentals of
//     Hybrid Boundary-Layer Combustion." AIAA Progress in Astronautics
//     and Aeronautics, 15.
//   Karabeyoglu A., Cantwell B.J., Altman D. (2003). AIAA-2003-4506.
//   Sutton G.P., Biblarz O. (2017). "Rocket Propulsion Elements," 9th
//     ed., chap 16 "Hybrid Propellant Rockets" — c* and C_F cluster.

using System;

namespace Voxelforge.Hybrid;

/// <summary>
/// Closed-form hybrid-rocket performance snapshot solver (Sprint R.W2).
/// </summary>
internal static class HybridRocketCycleSolver
{
    /// <summary>Standard gravity [m/s²].</summary>
    internal const double G0_ms2 = 9.80665;

    // ── Cluster-anchored thermochemistry for LOX/HTPB ────────────────────

    /// <summary>
    /// Cluster c* for LOX/HTPB near the O/F ≈ 2.3 mixture-optimum, at
    /// chamber pressure ≈ 20 bar. Source: Sutton chap 16 tabulation
    /// (cluster mid-band 1610-1660 m/s for LOX/HTPB). Wave-1 baseline.
    /// </summary>
    internal const double LoxHtpbCharacteristicVelocity_ms = 1640.0;

    /// <summary>
    /// Cluster vacuum C_F for ε = 10 expansion at γ ≈ 1.20 (LOX/HTPB
    /// combustion-product band). Sutton fig 3-7 cluster mid-band ~1.62.
    /// </summary>
    internal const double LoxHtpbVacuumThrustCoeffAtEps10 = 1.62;

    /// <summary>
    /// Cluster vacuum C_F sensitivity to ε. Linear-in-log(ε) approximation
    /// around the ε = 10 anchor: C_F(ε) ≈ 1.62 + 0.08·log10(ε/10).
    /// Conservative — real C_F flattens at high ε.
    /// </summary>
    internal const double VacuumThrustCoeffEpsSensitivity = 0.08;

    /// <summary>
    /// Solve the hybrid-rocket performance snapshot at a specified port
    /// radius.
    /// </summary>
    /// <param name="design">Validated hybrid rocket design.</param>
    /// <param name="portRadius_m">
    /// Port radius at the snapshot [m]. Must satisfy
    /// <c>design.InitialPortRadius_m ≤ portRadius_m ≤
    /// design.OuterGrainRadius_m</c>. Use
    /// <see cref="SolveInitial"/> for the burn-start snapshot.
    /// </param>
    /// <returns>Solved performance snapshot.</returns>
    internal static HybridRocketResult Solve(
        HybridRocketDesign design,
        double             portRadius_m)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        if (portRadius_m < design.InitialPortRadius_m)
            throw new ArgumentOutOfRangeException(nameof(portRadius_m),
                $"portRadius_m {portRadius_m:F4} must be ≥ "
              + $"InitialPortRadius_m {design.InitialPortRadius_m:F4}.");
        if (portRadius_m > design.OuterGrainRadius_m)
            throw new ArgumentOutOfRangeException(nameof(portRadius_m),
                $"portRadius_m {portRadius_m:F4} must be ≤ "
              + $"OuterGrainRadius_m {design.OuterGrainRadius_m:F4} (grain "
              + "fully consumed beyond this radius).");

        var fuelProps = HybridFuelRegistry.For(design.Fuel);

        // 1. Oxidiser mass flux through the port.
        double portArea_m2 = Math.PI * portRadius_m * portRadius_m;
        double G_ox = design.OxidiserMassFlow_kgs / portArea_m2;

        // 2. Marxman regression rate.
        double r_dot = fuelProps.MarxmanA * Math.Pow(G_ox, fuelProps.MarxmanN);

        // 3. Fuel mass flow off the cylindrical port surface.
        double burnArea_m2 = 2.0 * Math.PI * portRadius_m * design.GrainLength_m;
        double m_fuel = fuelProps.Density_kgm3 * burnArea_m2 * r_dot;

        // 4. O/F and total mass flow.
        double m_total = design.OxidiserMassFlow_kgs + m_fuel;
        double of_ratio = m_fuel > 1e-12
            ? design.OxidiserMassFlow_kgs / m_fuel
            : double.PositiveInfinity;

        // 5. Cluster thermochemistry — LOX/HTPB Sutton anchors.
        double c_star = LoxHtpbCharacteristicVelocity_ms;
        double C_F    = ComputeVacuumThrustCoefficient(design.ExpansionRatio);

        // 6. Vacuum Isp + thrust.
        double isp_vac = c_star * C_F / G0_ms2;
        double F_vac   = m_total * isp_vac * G0_ms2;

        return new HybridRocketResult(
            PortRadius_m:              portRadius_m,
            OxidiserMassFlux_kgm2s:    G_ox,
            RegressionRate_ms:         r_dot,
            FuelMassFlow_kgs:          m_fuel,
            TotalMassFlow_kgs:         m_total,
            OxidiserFuelRatio:         of_ratio,
            CharacteristicVelocity_ms: c_star,
            ThrustCoefficient:         C_F,
            VacuumIsp_s:               isp_vac,
            VacuumThrust_N:            F_vac);
    }

    /// <summary>
    /// Convenience wrapper — solve at <c>design.InitialPortRadius_m</c>
    /// (the burn-start snapshot).
    /// </summary>
    internal static HybridRocketResult SolveInitial(HybridRocketDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return Solve(design, design.InitialPortRadius_m);
    }

    /// <summary>
    /// Compute the vacuum thrust coefficient C_F as a function of
    /// expansion ratio ε. Cluster-fit: linear-in-log(ε) around the ε = 10
    /// LOX/HTPB anchor (Sutton chap 3 cluster). Public-static for tests.
    /// </summary>
    internal static double ComputeVacuumThrustCoefficient(double expansionRatio)
    {
        if (expansionRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(expansionRatio),
                $"ExpansionRatio must be ≥ 1.0; got {expansionRatio}.");
        return LoxHtpbVacuumThrustCoeffAtEps10
             + VacuumThrustCoeffEpsSensitivity * Math.Log10(expansionRatio / 10.0);
    }
}
