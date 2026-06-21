// HetPowerBalanceGuardRegressionTests.cs — regression guard for the HET
// power-balance bug (red-team round-2 finding).
//
// The 0-D HET thrust model computes thrust from discharge voltage V_d and mass
// flow ṁ but NOT from discharge current I_d, while discharge power P_d = V_d·I_d
// scales with I_d. So a low-I_d corner (which the SA optimizer reaches, since
// thrust/Isp carry no I_d penalty) yields jet kinetic power ½ṁv² > P_d — an
// efficiency > 1. The reported ThrustEfficiency was clamped to 1.0, hiding it,
// while Thrust_N / IspVacuum_s were reported unclamped and the design validated
// as feasible. A new hard gate (HET_POWER_BALANCE_VIOLATED) re-derives the
// balance from the unclamped thrust and rejects the impossible corner. The real
// BPT-4000 anchor (η_T ≈ 0.5) is unaffected.

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests;

public sealed class HetPowerBalanceGuardRegressionTests
{
    private static ElectricPropulsionEngineDesign HetDesign(double dischargeCurrent_A) => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = 300.0,
        DischargeCurrent_A = dischargeCurrent_A,
        MagneticField_T    = 0.02,
        AnodeRadius_mm     = 30.0,
        ChannelLength_mm   = 25.0,
        XenonMassFlow_kgs  = 1.6e-5,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions Cond() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:    5000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void LowDischargeCurrent_PowerBalanceViolated_IsInfeasible()
    {
        // I_d = 2 A → P_d = 600 W, but the V_d/ṁ-driven thrust implies a jet
        // kinetic power of ~2.3 kW (η_T ≈ 3.8). Physically impossible.
        var r = ElectricPropulsionOptimization.GenerateWith(HetDesign(2.0), Cond());
        Assert.False(r.IsFeasible, "low-I_d power-violating HET corner must be infeasible");
        Assert.Contains(r.Violations, v => v.ConstraintId == "HET_POWER_BALANCE_VIOLATED");
    }

    [Fact]
    public void NominalDischargeCurrent_PowerBalanceSatisfied_NoViolation()
    {
        // BPT-4000 anchor: V_d=300, I_d=15 → P_d=4.5 kW, η_T ≈ 0.5. The gate
        // must not fire for a real, power-consistent design.
        var r = ElectricPropulsionOptimization.GenerateWith(HetDesign(15.0), Cond());
        Assert.DoesNotContain(r.Violations, v => v.ConstraintId == "HET_POWER_BALANCE_VIOLATED");
    }
}
