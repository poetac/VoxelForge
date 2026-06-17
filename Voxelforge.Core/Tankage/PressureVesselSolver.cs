// PressureVesselSolver.cs — Sprint TANK.W1 closed-form thin-wall
// cylindrical pressure-vessel solver.
//
// Stateless, allocation-free, deterministic. Computes hoop / axial /
// von-Mises stresses, burst pressure, safety factor, shell mass,
// internal volume, and the P·V / (m·g) gravimetric figure of merit.
//
//   σ_hoop  = P · R / t                            [thin-wall hoop]
//   σ_axial = P · R / (2 · t)                      [thin-wall axial]
//   σ_vm    = √(σ_h² − σ_h·σ_a + σ_a²)
//           = (P · R / t) · √(3) / 2               [thin-wall identity]
//   P_burst = σ_yield · t / R
//   SF      = P_burst / P_operating
//   V_internal = π·R²·L + (4/3)·π·R³ (if hemi end caps)
//   V_shell    = π·((R+t)² − R²)·L + 2·(2/3)·π·((R+t)³ − R³) (if hemi)
//   gravimetric_eff = P·V / (m_shell · g₀)         [m] — pressure-volume
//                                                  energy per shell weight
//
// References:
//   Roark R.J., Young W.C. (2011). "Roark's Formulas for Stress and
//     Strain," 8th ed., chap 13 (shells of revolution).
//   ASME Boiler & Pressure Vessel Code, §VIII Div 1.
//   FMVSS 304 — Compressed Hydrogen Fuel Container Integrity (2.25 SF).

using System;

namespace Voxelforge.Tankage;

/// <summary>
/// Closed-form thin-wall cylindrical pressure-vessel solver
/// (Sprint TANK.W1).
/// </summary>
internal static class PressureVesselSolver
{
    /// <summary>Standard gravity [m/s²].</summary>
    internal const double G0_ms2 = 9.80665;

    /// <summary>
    /// Solve the pressure-vessel performance snapshot at the design
    /// operating point.
    /// </summary>
    internal static PressureVesselResult Solve(PressureVesselDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = TankShellRegistry.For(design.ShellType);

        double R = design.InternalRadius_m;
        double t = design.WallThickness_m;
        double L = design.ShellLength_m;
        double P = design.OperatingPressure_Pa;

        // 1. Thin-wall stresses.
        double sigma_hoop  = P * R / t;
        double sigma_axial = 0.5 * sigma_hoop;
        // σ_vm for σ_h = 2·σ_a reduces to σ_h · √3/2.
        double sigma_vm    = sigma_hoop * Math.Sqrt(3.0) * 0.5;

        // 2. Burst pressure + safety factor.
        double P_burst = props.YieldStrength_Pa * t / R;
        double SF      = P_burst / P;

        // 3. Internal volume.
        double V_cyl = Math.PI * R * R * L;
        double V_hemi = design.HasHemisphericalEndCaps
            ? (4.0 / 3.0) * Math.PI * R * R * R
            : 0.0;
        double V_internal = V_cyl + V_hemi;

        // 4. Shell volume (cylindrical wall + two hemispherical caps if any).
        double R_outer = R + t;
        double V_shell_cyl  = Math.PI * (R_outer * R_outer - R * R) * L;
        double V_shell_hemi = design.HasHemisphericalEndCaps
            ? 2.0 * (2.0 / 3.0) * Math.PI * (R_outer * R_outer * R_outer - R * R * R)
            : 0.0;
        double V_shell = V_shell_cyl + V_shell_hemi;
        double m_shell = props.Density_kgm3 * V_shell;

        // 5. Gravimetric efficiency = P·V / (m · g₀).
        double gravimetricEff = m_shell > 0
            ? (P * V_internal) / (m_shell * G0_ms2)
            : 0.0;

        return new PressureVesselResult(
            HoopStress_Pa:           sigma_hoop,
            AxialStress_Pa:          sigma_axial,
            VonMisesStress_Pa:       sigma_vm,
            BurstPressure_Pa:        P_burst,
            SafetyFactor:            SF,
            ShellMass_kg:            m_shell,
            InternalVolume_m3:       V_internal,
            GravimetricEfficiency:   gravimetricEff);
    }

    /// <summary>
    /// Sprint TANK.W2. Compute the maximum hoop stress for a thick-
    /// walled cylindrical pressure vessel using the Lamé equations
    /// (valid for R/t &lt; 10 where the thin-wall approximation breaks
    /// down):
    ///
    ///   σ_hoop_max = P · (R_outer² + R_inner²) / (R_outer² − R_inner²)
    ///
    /// The maximum hoop stress occurs at the inner wall, where σ_hoop
    /// is amplified vs the thin-wall PR/t value as R/t drops.
    /// </summary>
    /// <param name="internalRadius_m">R_inner [m].</param>
    /// <param name="wallThickness_m">t [m] = R_outer − R_inner.</param>
    /// <param name="operatingPressure_Pa">P [Pa].</param>
    /// <returns>σ_hoop_max [Pa] at the inner wall.</returns>
    internal static double ComputeThickWallHoopStress(
        double internalRadius_m,
        double wallThickness_m,
        double operatingPressure_Pa)
    {
        if (internalRadius_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(internalRadius_m),
                "R must be > 0.");
        if (wallThickness_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(wallThickness_m),
                "t must be > 0.");
        if (operatingPressure_Pa <= 0)
            throw new ArgumentOutOfRangeException(nameof(operatingPressure_Pa),
                "P must be > 0.");
        double R_i = internalRadius_m;
        double R_o = R_i + wallThickness_m;
        return operatingPressure_Pa * (R_o * R_o + R_i * R_i)
                                    / (R_o * R_o - R_i * R_i);
    }

    /// <summary>
    /// Solve for the minimum wall thickness required to satisfy a given
    /// safety factor at the design operating pressure. Public-static
    /// helper for sizing studies. t_min = SF · P · R / σ_yield.
    /// </summary>
    /// <param name="shellType">Shell-material construction.</param>
    /// <param name="internalRadius_m">R [m].</param>
    /// <param name="operatingPressure_Pa">P [Pa].</param>
    /// <param name="targetSafetyFactor">Required SF [-]. ASME §VIII typical 4.0;
    /// FMVSS 304 H₂ tank 2.25; aerospace LOX 1.5.</param>
    /// <returns>Required wall thickness [m], floored at the cluster
    /// manufacturing minimum.</returns>
    internal static double SolveForMinimumWallThickness(
        TankShellType shellType,
        double internalRadius_m,
        double operatingPressure_Pa,
        double targetSafetyFactor)
    {
        if (internalRadius_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(internalRadius_m),
                "R must be > 0.");
        if (operatingPressure_Pa <= 0)
            throw new ArgumentOutOfRangeException(nameof(operatingPressure_Pa),
                "P must be > 0.");
        if (targetSafetyFactor <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(targetSafetyFactor),
                "Target safety factor must be > 1.0.");

        var props = TankShellRegistry.For(shellType);
        double t_stress = targetSafetyFactor * operatingPressure_Pa * internalRadius_m
                        / props.YieldStrength_Pa;
        return Math.Max(t_stress, props.MinPracticalWallThickness_m);
    }
}
