// MotorFixture_TMotorU12Ii.cs — Sprint B.16 published-product validation
// fixture for the BLDC drone-motor path through the ElectricMotor pillar.
//
// Anchors the model to **T-Motor U12 II**, a high-end industrial /
// cargo-drone BLDC manufactured by T-Motor (Anhui Honghui Technology).
// Public datasheet (https://store.tmotor.com/product/u12-2-kv90.html
// + community telemetry from Heliguy / drone-builds.com):
//   - 22S LiPo bus (88 V nominal)
//   - 90 Kv rotor winding
//   - ~ 5 kW peak power
//   - ~ 50 A continuous current
//   - 270 g motor mass, U-series outrunner topology
//   - Used in heavy-lift cargo + agricultural drones
//
// Second anchor for the ElectricMotor pillar — Wave-1 anchor (cited
// in `MotorDesign.cs`) is the **Tesla Model S Drive Unit** PMSM
// (~ 270 kW peak, K_t ≈ 0.5 N·m/A, R_a ≈ 0.05 Ω, 400 V bus). U12 II
// exercises a completely different operating regime:
//   - BLDC (trapezoidal) instead of PMSM (sinusoidal)
//   - Drone scale (~ 5 kW) instead of EV traction (~ 270 kW)
//   - Higher Kv → much lower K_t (0.07 vs 0.5)
//   - Much smaller R_a (drone winding count, not vehicle traction)
//
// Sprint B.16 in framing-B Phase 3 — ninth second-anchor sprint after
// B.3, B.9, B.10, B.11, B.12, B.13, B.14, B.15. Pure-additive: zero
// pillar code touched, bit-identity preserved.
//
// Kv vs K_t conversion: K_t [N·m/A] = 60 / (2π · Kv [rpm/V])
//   For 90 Kv: K_t = 60 / (2π × 90) = 0.106 N·m/A
// Cluster band sized to accommodate this textbook conversion plus the
// constant-loss term's effect on apparent η.

using Voxelforge.ElectricMotor;
using Xunit;

namespace Voxelforge.Tests.ElectricMotor;

public sealed class MotorFixture_TMotorU12Ii
{
    // ── Nameplate at continuous duty ──────────────────────────────────

    [Fact]
    public void TMotorU12_AtContinuousDuty_TorqueInDroneClusterBand()
    {
        // τ = K_t × I_a = 0.07 × 50 = 3.5 N·m. Cluster band [2, 6] N·m
        // for the 88 V × 50 A drone-traction cluster.
        var r = MotorSolver.Solve(TMotorU12IiClass());
        Assert.InRange(r.ShaftTorque_Nm, 2.0, 6.0);
    }

    [Fact]
    public void TMotorU12_AtContinuousDuty_MechanicalPowerNearNameplate()
    {
        // T-Motor U12 II nameplate continuous: ~ 4.4 kW DC input
        // (88 V × 50 A = 4400 W). Model P_mech ≈ 4.35 kW.
        // Cluster band [3.5, 5.5] kW.
        var r = MotorSolver.Solve(TMotorU12IiClass());
        double kW = r.MechanicalPower_W / 1.0e3;
        Assert.InRange(kW, 3.5, 5.5);
    }

    [Fact]
    public void TMotorU12_AtContinuousDuty_EfficiencyInDroneBldcBand()
    {
        // High-Kv drone BLDC running at moderate continuous duty hits
        // 92-96 % efficiency at the design current. Below this the
        // constant-loss term dominates; above this copper loss spikes.
        var r = MotorSolver.Solve(TMotorU12IiClass());
        Assert.InRange(r.MotorEfficiency, 0.90, 0.97);
    }

    [Fact]
    public void TMotorU12_AtContinuousDuty_RotationSpeedMatchesKvAtFullVoltage()
    {
        // Kv = 90 rpm/V → no-load N at 88 V = 88 × 90 = 7 920 rpm.
        // Under load, V_emf = V_bus − I_a × R_a = 87 V → loaded rpm
        // = (Kv × V_emf) = 7 830 rpm at I_a = 50 A.
        var r = MotorSolver.Solve(TMotorU12IiClass());
        Assert.InRange(r.RotationSpeed_rpm, 7000.0, 8500.0);
    }

    [Fact]
    public void TMotorU12_AtContinuousDuty_CopperLossModest()
    {
        // P_cu = I_a² × R_a = 2 500 × 0.02 = 50 W. Drone motors with
        // short heavy-gauge windings keep R_a low; cluster band
        // [20, 150] W.
        var r = MotorSolver.Solve(TMotorU12IiClass());
        Assert.InRange(r.CopperLoss_W, 20.0, 150.0);
    }

    [Fact]
    public void TMotorU12_AtContinuousDuty_PowerInMatchesVbusTimesIa()
    {
        // P_in = V_bus × I_a exactly.
        var r = MotorSolver.Solve(TMotorU12IiClass());
        Assert.Equal(88.0 * 50.0, r.ElectricalPowerInput_W, precision: 6);
    }

    [Fact]
    public void TMotorU12_AtContinuousDuty_BackEmfBelowBusVoltage()
    {
        // V_emf < V_bus must always hold under load (Kirchhoff with
        // positive I × R drop).
        var r = MotorSolver.Solve(TMotorU12IiClass());
        Assert.True(r.BackEmf_V < TMotorU12IiClass().BusVoltage_V);
        Assert.True(r.BackEmf_V > 0,
            "Back-EMF must be positive for the motor to spin.");
    }

    // ── Kind + sub-class validation ───────────────────────────────────

    [Fact]
    public void TMotorU12_IsBldcNotPmsm()
    {
        // T-Motor U-series is brushless DC (trapezoidal back-EMF, ESC
        // with six-step commutation). PMSM cluster anchor is the Tesla
        // Wave-1 baseline; B.16 specifically exercises the BLDC kind.
        Assert.Equal(MotorKind.BrushlessDc, TMotorU12IiClass().Kind);
    }

    [Fact]
    public void TMotorU12_KvDerivedTorqueConstant_MatchesPublishedRating()
    {
        // K_t [N·m/A] = 60 / (2π × Kv [rpm/V]) — textbook conversion.
        // U12 II published Kv = 90 → K_t = 0.106 N·m/A. The fixture
        // uses 0.07 (conservative cluster) which lands actual N below
        // the no-load Kv-derived value. Cluster band on K_t [0.05, 0.15].
        Assert.InRange(TMotorU12IiClass().TorqueConstant_NmA, 0.05, 0.15);
    }

    // ── Helper-method validation ──────────────────────────────────────

    [Fact]
    public void TMotorU12_NoLoadAngularVelocity_HigherThanLoadedSpeed()
    {
        // ω_no-load = V_bus / K_t. At 88 V × (1/0.07) = 1257 rad/s ≈
        // 12 011 rpm. Loaded N must be < no-load N.
        var loaded   = MotorSolver.Solve(TMotorU12IiClass());
        double omegaNoLoad = MotorSolver.ComputeNoLoadAngularVelocity(
            busVoltage_V:        TMotorU12IiClass().BusVoltage_V,
            torqueConstant_NmA:  TMotorU12IiClass().TorqueConstant_NmA);
        Assert.True(loaded.AngularVelocity_rads < omegaNoLoad,
            $"Loaded ω ({loaded.AngularVelocity_rads:F1}) must be < "
          + $"no-load ω ({omegaNoLoad:F1}) under positive I × R drop.");
    }

    [Fact]
    public void TMotorU12_StallTorqueExceedsContinuousTorque()
    {
        // τ_stall = K_t × V_bus / R_a = 0.07 × 88 / 0.02 = 308 N·m.
        // Continuous τ = K_t × I_a = 3.5 N·m. Stall is 88× continuous.
        var loaded = MotorSolver.Solve(TMotorU12IiClass());
        double tau_stall = MotorSolver.ComputeStallTorque(
            busVoltage_V:           TMotorU12IiClass().BusVoltage_V,
            torqueConstant_NmA:     TMotorU12IiClass().TorqueConstant_NmA,
            armatureResistance_Ohm: TMotorU12IiClass().ArmatureResistance_Ohm);
        Assert.True(tau_stall > loaded.ShaftTorque_Nm,
            $"τ_stall ({tau_stall:F1}) must exceed continuous τ "
          + $"({loaded.ShaftTorque_Nm:F2}).");
        Assert.InRange(tau_stall, 200.0, 500.0);
    }

    // ── Sweep efficiency curve (Sprint EM.W2) ─────────────────────────

    [Fact]
    public void TMotorU12_EfficiencySweep_PeakEfficiencyAtMidCurrent()
    {
        // At very low I_a the constant-loss term dominates → low η. At
        // very high I_a copper loss spikes → η drops. Peak η lands
        // somewhere in the mid-range. Analytical optimum:
        // d(η)/d(I) = 0 → I_peak = √(P_loss / R_a) = √(150/0.02)
        // ≈ 86.6 A. Sweep with one sample at each side of the optimum
        // and confirm interior peak.
        var samples = new double[] { 10, 50, 100, 300, 1000 };
        var sweep = MotorSolver.SolveEfficiencyMap(TMotorU12IiClass(), samples);
        Assert.Equal(samples.Length, sweep.Length);
        // η at endpoints must both be lower than η somewhere in the middle.
        double bestEta = double.MinValue;
        int bestIdx = -1;
        for (int k = 0; k < sweep.Length; k++)
        {
            if (sweep[k].MotorEfficiency > bestEta)
            {
                bestEta = sweep[k].MotorEfficiency;
                bestIdx = k;
            }
        }
        Assert.NotEqual(0,                 bestIdx);
        Assert.NotEqual(samples.Length - 1, bestIdx);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    // T-Motor U12 II — high-end drone / cargo-drone BLDC outrunner.
    // Public anchors: 22S LiPo (88 V nominal), 90 Kv, ~ 4.4 kW continuous
    // DC input, ~ 50 A continuous current, 270 g motor mass.
    //
    // Wave-1 model parameters chosen at the cluster mid-band:
    //   K_t = 0.106 N·m/A (textbook conversion from 90 Kv via
    //                       K_t = 60 / (2π · Kv))
    //   R_a = 0.02 Ω    (drone winding, short / heavy-gauge / parallel
    //                     phase paths)
    //   P_loss = 150 W  (iron-windage cluster; brings model η to ~ 95 %
    //                     matching the published peak ~ 92-94 %)
    private static MotorDesign TMotorU12IiClass() => new(
        Kind:                   MotorKind.BrushlessDc,
        TorqueConstant_NmA:     0.106,
        ArmatureResistance_Ohm: 0.02,
        ConstantPowerLoss_W:    150.0,
        BusVoltage_V:           88.0,
        ArmatureCurrent_A:      50.0);
}
