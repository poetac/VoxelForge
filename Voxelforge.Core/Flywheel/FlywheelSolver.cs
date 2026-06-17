// FlywheelSolver.cs — Sprint FW.W1 closed-form flywheel-energy-storage
// rotor performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes moment of
// inertia, tip speed, stored energy, specific energy, hoop stress,
// and burst-speed safety factor for a flywheel at the design (R, m, N)
// operating point.
//
//   ω        = 2π · N / 60                         [rpm → rad/s]
//   I        = α · m · R²                          [α from per-shape registry]
//   E        = ½ · I · ω²                          [J]
//   E/m      = α · ½ · ω² · R²                     [J/kg]
//   v_tip    = ω · R
//   σ_hoop   = ρ · ω² · R²                         [thin rim approx]
//   ω_burst  = √(σ_yield / (ρ · R²))               [rad/s]
//
// Specific-energy upper bound (material-only) follows the textbook
// formula E/m ≤ K · σ_y / ρ with K (shape factor) from the per-shape
// registry. The solver does not enforce this bound directly — instead
// it reports the BurstSpeedSafetyFactor which captures the same
// limit in operational terms.
//
// References:
//   Genta G. (1985). "Kinetic Energy Storage." Butterworths.
//   Genta G. (2007). "Spacecraft Vibration Control," chap 11 (rotor dynamics).
//   Beacon Power Smart Energy 25 Flywheel Brochure (2020).

using System;

namespace Voxelforge.Flywheel;

/// <summary>
/// Closed-form flywheel-rotor performance snapshot solver (Sprint FW.W1).
/// </summary>
internal static class FlywheelSolver
{
    /// <summary>1 kWh in Joules.</summary>
    internal const double JoulePerKwh = 3.6e6;

    /// <summary>1 kWh in Wh.</summary>
    internal const double WhPerKwh = 1000.0;

    /// <summary>
    /// Solve the flywheel rotor snapshot at the design operating point.
    /// </summary>
    internal static FlywheelResult Solve(FlywheelDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = FlywheelMaterialRegistry.For(design.Material);
        double alpha = FlywheelShapeFactors.MomentOfInertiaCoefficient(design.Shape);

        // 1. Rotational kinematics. Sprint FW.W2: RotationSpeed_rpm is
        //    the design *maximum* speed; the instantaneous ω scales with
        //    √SoC (since E ∝ ω², and SoC := E/E_max → ω/ω_max = √SoC).
        //    At SoC = 1.0 (FW.W1 default), ω_actual = ω_design → bit-
        //    identical Wave-1 behaviour.
        double omega_design = 2.0 * Math.PI * design.RotationSpeed_rpm / 60.0;
        double omega = omega_design * Math.Sqrt(design.StateOfCharge);
        double v_tip = omega * design.OuterRadius_m;

        // 2. Moment of inertia + stored energy at the instantaneous SoC.
        double I = alpha * design.Mass_kg * design.OuterRadius_m * design.OuterRadius_m;
        double E_J = 0.5 * I * omega * omega;       // = ½·I·ω_design²·SoC = E_max·SoC
        double E_kWh = E_J / JoulePerKwh;
        // Specific energy in Wh/kg: E_J / m gives J/kg; convert to Wh/kg.
        double specificEnergy_Wh_kg = (E_J / design.Mass_kg) / 3600.0;

        // 3. Stress + burst speed (thin-rim approx σ = ρ·ω²·R²).
        double sigma_hoop = props.Density_kgm3 * omega * omega
                          * design.OuterRadius_m * design.OuterRadius_m;
        double omega_burst = Math.Sqrt(props.YieldStrength_Pa
                          / (props.Density_kgm3 * design.OuterRadius_m * design.OuterRadius_m));
        double rpm_burst = omega_burst * 60.0 / (2.0 * Math.PI);
        // Safety factor uses the DESIGN max speed (not the instantaneous
        // SoC-derated speed) since the burst-stress envelope is the
        // operating-life worst case.
        double safetyFactor = omega_burst / omega_design;

        // 4. Sprint FW.W2 — bearing parasitic drag + auto-discharge.
        //    Per-bearing-type drag fraction times nominal-energy ÷ ω gives
        //    a torque-class estimate. Then τ_loss = E / P_drag.
        double dragFractionOfDesignTorque = GetBearingDragFraction(design.Bearing);
        // Design torque scale: I · ω_design² / time-scale ≈ (½·I·ω_design²)/ω_design
        //                       = E_max / ω_design — used as the nominal
        //                       torque-product magnitude.
        double E_max_J = 0.5 * I * omega_design * omega_design;
        double tau_drag = dragFractionOfDesignTorque * (E_max_J / omega_design);
        double P_drag   = tau_drag * omega;     // drag power at actual ω
        // Auto-discharge time constant: dE/dt = −P_drag → τ_loss = E / P_drag.
        double tau_loss_s = P_drag > 0 ? E_J / P_drag : double.PositiveInfinity;

        return new FlywheelResult(
            MomentOfInertia_kgm2:         I,
            AngularVelocity_rads:         omega,
            TipSpeed_ms:                  v_tip,
            StoredEnergy_J:               E_J,
            StoredEnergy_kWh:             E_kWh,
            SpecificEnergy_Wh_kg:         specificEnergy_Wh_kg,
            MaximumHoopStress_Pa:         sigma_hoop,
            BurstSpeed_rpm:               rpm_burst,
            BurstSpeedSafetyFactor:       safetyFactor,
            ParasiticDragTorque_Nm:       tau_drag,
            ParasiticPowerLoss_W:         P_drag,
            AutoDischargeTimeConstant_s:  tau_loss_s);
    }

    /// <summary>
    /// Per-bearing-type parasitic-drag fraction (Sprint FW.W2 helper).
    /// Cluster-anchored mid-band: mechanical bearings lose ~ 1 % of
    /// design torque continuously; magnetic levitation drops this to
    /// ~ 0.05 %; superconducting magnetic to ~ 0.005 %.
    /// </summary>
    internal static double GetBearingDragFraction(BearingType bearing) => bearing switch
    {
        BearingType.Mechanical                          => 1.0e-2,
        BearingType.MagneticLevitation                  => 5.0e-4,
        BearingType.SuperconductingMagneticLevitation   => 5.0e-5,
        _ => throw new ArgumentOutOfRangeException(nameof(bearing), bearing,
                $"Unknown BearingType '{bearing}'."),
    };

    /// <summary>
    /// Compute the theoretical maximum specific energy E/m [Wh/kg] for
    /// a given (material, shape) combination. Closed form:
    /// E/m = K · σ_yield / ρ. Public-static helper for material-
    /// selection studies.
    /// </summary>
    internal static double ComputeMaximumSpecificEnergy(
        FlywheelMaterial material,
        FlywheelShape    shape)
    {
        var props = FlywheelMaterialRegistry.For(material);
        double K = FlywheelShapeFactors.For(shape);
        double E_per_m_J_kg = K * props.YieldStrength_Pa / props.Density_kgm3;
        return E_per_m_J_kg / 3600.0;   // J/kg → Wh/kg
    }
}
