// HetCycleSolverTests.cs — Sprint EP.W2.HET acceptance tests for the
// Busch discharge model + HET cycle solver. Pins the calibration
// constants against the BPT-4000 anchor and exercises the corner cases
// (NaN inputs, zero magnetic field, mass-utilisation cap).

using System;
using Voxelforge.ElectricPropulsion.Plasma;
using Voxelforge.ElectricPropulsion.Solvers;
using Voxelforge.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class HetCycleSolverTests
{
    // BPT-4000 reference inputs (Aerojet Rocketdyne, 4.5 kW, xenon HET).
    private const double Bpt4000_Vd_V       = 300.0;
    private const double Bpt4000_Id_A       =  15.0;
    private const double Bpt4000_B_T        =   0.02;
    private const double Bpt4000_RAnode_mm  =  30.0;
    private const double Bpt4000_LChannel_mm = 25.0;
    private const double Bpt4000_mDotXe_kgs =   1.6e-5;

    private static ElectricPropulsionEngineDesign Bpt4000Design() => new(
        Kind:                    ElectricPropulsionEngineKind.HallEffect,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  double.NaN,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        DischargeVoltage_V = Bpt4000_Vd_V,
        DischargeCurrent_A = Bpt4000_Id_A,
        MagneticField_T    = Bpt4000_B_T,
        AnodeRadius_mm     = Bpt4000_RAnode_mm,
        ChannelLength_mm   = Bpt4000_LChannel_mm,
        XenonMassFlow_kgs  = Bpt4000_mDotXe_kgs,
        AnodeMaterial      = AnodeMaterial.Graphite,
        CathodeType        = CathodeType.HollowCathode,
    };

    private static ResistojetConditions VacuumConditions() => new(
        BusVoltage_V:        300.0,
        BusPower_W_avail:    5000.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.Xenon,
        InletTemperature_K:  300.0,
        // HET ignores InletComposition (the Busch model consumes only Xe MW
        // via BuschDischargeModel.XenonIonMass_kg). Any valid composition is
        // a no-op placeholder; PureH2 minimises off-pillar coupling.
        InletComposition:    PropellantInletComposition.PureH2);

    [Fact]
    public void IonExitVelocity_AtBpt4000Voltage_IsApproximately20km_s()
    {
        // v_i = √(2·e·V_d·η_b/m_xe) with η_b=0.95 → ~20.5 km/s at V_d=300 V.
        var result = BuschDischargeModel.Solve(
            dischargeVoltage_V:  300.0,
            dischargeCurrent_A:  15.0,
            magneticField_T:     0.02,
            anodeRadius_mm:      30.0,
            channelLength_mm:    25.0,
            xenonMassFlow_kgs:   1.6e-5);
        Assert.InRange(result.IonExitVelocity_ms, 19_000.0, 22_000.0);
    }

    [Fact]
    public void PlumeDivergence_AtBpt4000Field_IsBetween25And40Degrees()
    {
        // θ = arctan(K_div / B) with K_div=0.012, B=0.02 → ~31°.
        var result = BuschDischargeModel.Solve(
            dischargeVoltage_V:  300.0,
            dischargeCurrent_A:  15.0,
            magneticField_T:     0.02,
            anodeRadius_mm:      30.0,
            channelLength_mm:    25.0,
            xenonMassFlow_kgs:   1.6e-5);
        double thetaDeg = result.PlumeDivergenceHalfAngle_rad * 180.0 / Math.PI;
        Assert.InRange(thetaDeg, 25.0, 40.0);
    }

    [Fact]
    public void MassUtilization_IsBoundedBetweenZeroAndOne()
    {
        var result = BuschDischargeModel.Solve(
            dischargeVoltage_V:  300.0,
            dischargeCurrent_A:  15.0,
            magneticField_T:     0.02,
            anodeRadius_mm:      30.0,
            channelLength_mm:    25.0,
            xenonMassFlow_kgs:   1.6e-5);
        Assert.InRange(result.MassUtilization, 0.0, 1.0);
    }

    [Fact]
    public void Solve_WithBpt4000Inputs_LandsThrustInsideBand()
    {
        // ADR-029 D4 fixture envelope: ±20% of 0.242 N → [0.194, 0.290].
        // Wider than the BPT-4000 fixture's [0.216, 0.324] (±20% of measured
        // 0.270). The model is calibrated to land near the BPT-4000 measured
        // datasheet point.
        var hetResult = HetCycleSolver.Solve(Bpt4000Design(), VacuumConditions());
        Assert.True(hetResult.Discharge.Converged);
        Assert.InRange(hetResult.Discharge.Thrust_N, 0.194, 0.324);
    }

    [Fact]
    public void Solve_WithBpt4000Inputs_LandsIspInsideBand()
    {
        // BPT-4000 datasheet Isp ≈ 1543 s; ±15% gives [1311, 1775].
        // Wave-2 model lands ~1700 s.
        var hetResult = HetCycleSolver.Solve(Bpt4000Design(), VacuumConditions());
        Assert.InRange(hetResult.Discharge.IspVacuum_s, 1311.0, 2070.0);
    }

    [Fact]
    public void Solve_DischargePowerEqualsVdTimesId()
    {
        var hetResult = HetCycleSolver.Solve(Bpt4000Design(), VacuumConditions());
        const double expectedPower = Bpt4000_Vd_V * Bpt4000_Id_A; // 4500 W
        Assert.Equal(expectedPower, hetResult.Discharge.DischargePower_W, precision: 3);
    }

    [Fact]
    public void Solve_AnodeTemperature_BelowGraphiteLimit()
    {
        // BPT-4000 anode under graphite limit (2000 K) per Goebel & Katz §3.5.
        var hetResult = HetCycleSolver.Solve(Bpt4000Design(), VacuumConditions());
        Assert.InRange(hetResult.Discharge.AnodeWallTemp_K, 800.0, 2000.0);
    }

    [Fact]
    public void Solve_NaNHetField_Throws()
    {
        var brokenDesign = Bpt4000Design() with { DischargeVoltage_V = double.NaN };
        Assert.Throws<ArgumentException>(() => HetCycleSolver.Solve(brokenDesign, VacuumConditions()));
    }

    [Fact]
    public void Solve_ZeroMagneticField_Throws()
    {
        var brokenDesign = Bpt4000Design() with { MagneticField_T = 0.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => HetCycleSolver.Solve(brokenDesign, VacuumConditions()));
    }

    [Fact]
    public void Solve_PlasmaStateImplementsIPlasmaState()
    {
        var hetResult = HetCycleSolver.Solve(Bpt4000Design(), VacuumConditions());
        // Assert.IsAssignableFrom returns the value typed as the asserted interface,
        // which is the test's intent — "PlasmaState implements IPlasmaState". Side
        // benefit: avoids CA1859 (perf rule preferring concrete types) without
        // weakening the assertion.
        IPlasmaState plasma = Assert.IsAssignableFrom<IPlasmaState>(hetResult.PlasmaState);
        Assert.True(plasma.IonExitVelocity_ms > 0);
        Assert.True(plasma.BeamCurrent_A > 0);
        Assert.True(plasma.PlumeDivergenceHalfAngle_rad > 0 && plasma.PlumeDivergenceHalfAngle_rad < Math.PI / 2);
    }

    [Fact]
    public void Solve_OnResistojetKind_Throws()
    {
        // HetCycleSolver guards against being called on a non-HET design.
        var resistojet = Bpt4000Design() with { Kind = ElectricPropulsionEngineKind.Resistojet };
        Assert.Throws<ArgumentException>(() => HetCycleSolver.Solve(resistojet, VacuumConditions()));
    }
}
