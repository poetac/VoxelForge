// ElectricPropulsionFixture_Mr509Atos.cs — Sprint EP.W2.AJ acceptance.
//
// Wave-2 published-engine validation fixture for the Aerojet Rocketdyne
// MR-509 ATOS Arcjet thruster (Augmented Thermo-Optimized System; 1.8 kW,
// hydrazine, 0.20-0.26 N thrust class, 580 s Isp). Flown on Lockheed
// Martin A2100 commercial GEO satellites for north-south station-keeping.
//
//   Inputs:  V_arc=100 V, I_arc=18 A, ArcGap=2.0 mm,
//            ṁ=3.9e-5 kg/s post-catalyst hydrazine,
//            R_throat=0.5 mm, ε=100, L_chamber=12 mm, R_chamber=4 mm,
//            ElectrodeMaterial=Tungsten.
//   Targets: Thrust ≈ 0.222 N (datasheet 0.20–0.26 N), Isp ≈ 580 s,
//            P_arc=1800 W.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Arcjet (electrothermal) variant under ADR-036 § EP pillar
// (±20 % thrust / ±15 % Isp / ±5 % arc-power). MR-509 ATOS is the GEO
// commercial production anchor; its Sutton 9e §16.3 + NASA TM-2002-211314
// data lineage makes the Maecker-Kovitya energy-balance model well-calibrated.
// Same envelope as BPT-4000 — energy-balance arcjet model has comparable
// calibration uncertainty to the Busch HET model.
//
// Citations:
//   • Sutton GP & Biblarz O. (2017). "Rocket Propulsion Elements" 9e §16.3.
//   • NASA TM-2002-211314 §4 (electrothermal arcjet survey).
//   • Aerojet Rocketdyne MR-509 ATOS datasheet (publicly summarised in
//     Sutton 9e Table 16-2).

using Voxelforge.ElectricPropulsion.Plasma;

namespace Voxelforge.ElectricPropulsion.Tests.Validation;

public sealed class ElectricPropulsionFixture_Mr509Atos
{
    private const double TargetThrust_N = 0.222;
    private const double TargetIsp_s    = 580.0;
    private const double TargetPArc_W   = 1800.0;

    // ADR-029 D4 tolerance contract.
    private const double ThrustToleranceFraction = 0.20;  // ±20 %
    private const double IspToleranceFraction    = 0.15;  // ±15 %
    private const double PArcToleranceFraction   = 0.05;  // ±5 % (V_arc × I_arc is exact arithmetic)

    private static ElectricPropulsionEngineDesign Mr509Design() => new(
        Kind:                    ElectricPropulsionEngineKind.Arcjet,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:  3.9e-5,
        NozzleThroatRadius_mm:   0.5,
        NozzleAreaRatio:        100.0,
        HeaterChamberLength_mm:  12.0,
        HeaterChamberRadius_mm:   4.0)
    {
        ArcVoltage_V             = 100.0,
        ArcCurrent_A             =  18.0,
        ArcGap_mm                =   2.0,
        ArcjetElectrodeMaterial  = ArcjetElectrodeMaterial.Tungsten,
        // ArcjetThermalEfficiency left at NaN — uses the cluster anchor (0.40).
    };

    private static ResistojetConditions Mr509Conditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail:   2200.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 900.0,                // post-Shell-405 catalyst
        InletComposition:   PropellantInletComposition.PureH2);   // placeholder; arcjet ignores composition

    [Fact]
    public void Mr509_Thrust_WithinTwentyPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        double low  = TargetThrust_N * (1.0 - ThrustToleranceFraction);
        double high = TargetThrust_N * (1.0 + ThrustToleranceFraction);
        Assert.InRange(result.Thrust_N, low, high);
    }

    [Fact]
    public void Mr509_Isp_WithinFifteenPercent()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        double low  = TargetIsp_s * (1.0 - IspToleranceFraction);
        double high = TargetIsp_s * (1.0 + IspToleranceFraction);
        Assert.InRange(result.IspVacuum_s, low, high);
    }

    [Fact]
    public void Mr509_ArcPower_MatchesVarcTimesIarc()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        var plasma = Assert.IsType<ArcjetPlasmaState>(result.PlasmaState);
        double low  = TargetPArc_W * (1.0 - PArcToleranceFraction);
        double high = TargetPArc_W * (1.0 + PArcToleranceFraction);
        Assert.InRange(plasma.ArcPower_W, low, high);
    }

    [Fact]
    public void Mr509_ThermalEfficiency_PhysicallyReasonable()
    {
        // Sutton 9e §16.3 reports η_thermal ≈ 0.30-0.50 for the arcjet cluster.
        // Default model anchor is 0.40; accept [0.25, 0.55] band.
        var result = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        var plasma = Assert.IsType<ArcjetPlasmaState>(result.PlasmaState);
        Assert.InRange(plasma.ThermalEfficiency, 0.25, 0.55);
    }

    [Fact]
    public void Mr509_PlasmaState_IsArcjet_NotNull()
    {
        var result = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        Assert.NotNull(result.PlasmaState);
        Assert.IsType<ArcjetPlasmaState>(result.PlasmaState);
        Assert.True(result.IsFeasible,
            "MR-509 ATOS baseline should pass all hard gates (tungsten anode, "
          + "V_arc in band, anode wall T below limit).");
    }

    [Fact]
    public void Mr509_BeamCurrent_EqualsArcCurrent()
    {
        // Arcjet has no separate neutraliser path — all of I_arc flows through
        // the useful path. ArcjetPlasmaState.BeamCurrent_A == design.ArcCurrent_A.
        var result = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        var plasma = Assert.IsType<ArcjetPlasmaState>(result.PlasmaState);
        Assert.Equal(18.0, plasma.BeamCurrent_A, precision: 9);
    }

    [Fact]
    public void Mr509_Deterministic()
    {
        var r1 = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        var r2 = ElectricPropulsionOptimization.GenerateWith(Mr509Design(), Mr509Conditions());
        Assert.Equal(r1.Thrust_N,    r2.Thrust_N);
        Assert.Equal(r1.IspVacuum_s, r2.IspVacuum_s);
    }
}
