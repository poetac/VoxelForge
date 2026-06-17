// FlywheelResult.cs — Sprint FW.W1 solver output.

namespace Voxelforge.Flywheel;

/// <summary>
/// Solve-time outputs for a flywheel-rotor snapshot (Sprint FW.W1).
/// </summary>
/// <param name="MomentOfInertia_kgm2">I = α · m · R² [kg·m²].</param>
/// <param name="AngularVelocity_rads">ω [rad/s] from RPM.</param>
/// <param name="TipSpeed_ms">v_tip = ω · R [m/s].</param>
/// <param name="StoredEnergy_J">E = ½ · I · ω² [J].</param>
/// <param name="StoredEnergy_kWh">E in [kWh] = E_J / 3.6e6.</param>
/// <param name="SpecificEnergy_Wh_kg">E / m [Wh/kg]. Modern composites
/// reach ~ 200 Wh/kg; steel rims ~ 50 Wh/kg.</param>
/// <param name="MaximumHoopStress_Pa">σ_hoop = ρ · ω² · R² for a thin
/// rim [Pa].</param>
/// <param name="BurstSpeed_rpm">ω_burst [rpm] = the speed at which
/// σ_hoop = σ_yield.</param>
/// <param name="BurstSpeedSafetyFactor">ω_burst / ω_design [-] — design
/// is "safe" when SF &gt; 1.</param>
/// <param name="ParasiticDragTorque_Nm">Sprint FW.W2. τ_drag [N·m] —
/// continuous parasitic-drag torque per bearing system. 0 for FW.W1
/// bit-identity callers.</param>
/// <param name="ParasiticPowerLoss_W">Sprint FW.W2. P_drag = τ_drag · ω
/// [W] — continuous power lost to drag at the design speed. Drives the
/// auto-discharge time constant.</param>
/// <param name="AutoDischargeTimeConstant_s">Sprint FW.W2. τ_loss [s]
/// — characteristic time constant for the rotor to lose ½ of its stored
/// energy at the parked / no-load state, assuming exponential decay
/// E(t) = E₀ · exp(−t / τ_loss). +∞ for FW.W1.</param>
internal sealed record FlywheelResult(
    double MomentOfInertia_kgm2,
    double AngularVelocity_rads,
    double TipSpeed_ms,
    double StoredEnergy_J,
    double StoredEnergy_kWh,
    double SpecificEnergy_Wh_kg,
    double MaximumHoopStress_Pa,
    double BurstSpeed_rpm,
    double BurstSpeedSafetyFactor,
    double ParasiticDragTorque_Nm     = 0.0,
    double ParasiticPowerLoss_W       = 0.0,
    double AutoDischargeTimeConstant_s = double.PositiveInfinity);
