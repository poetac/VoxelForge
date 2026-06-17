// DemoSubsystemsTests.cs — Sprint SI.W4 headline multi-pillar
// integration demos. These tests are the FIRST cross-pillar physics
// studies in voxelforge — each one wires components from 3+ pillars
// into a coherent subsystem.

using Voxelforge.Antenna;
using Voxelforge.Battery;
using Voxelforge.ElectricMotor;
using Voxelforge.Electrolyser;
using Voxelforge.Flywheel;
using Voxelforge.HeatExchanger;
using Voxelforge.HeatPipe;
using Voxelforge.Hydroelectric;
using Voxelforge.HydrogenStorage;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Voxelforge.Photovoltaic;
using Voxelforge.PowerGen;
using Voxelforge.Pump;
using Voxelforge.Radiator;
using Voxelforge.SolarThermal;
using Voxelforge.Stirling;
using Voxelforge.Thermoelectric;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class DemoSubsystemsTests
{
    // ── PV + Battery + Motor (solar-storage EV powertrain) ──────────────

    [Fact]
    public void Demo_PvSolar_To_Battery_To_Motor()
    {
        // Topology: PV array → Battery pack → drive motor.
        // PV operating point sets the bus voltage at MPP; battery
        // absorbs (PV charges battery when motor I_load < PV I_mpp).
        var n = new ComponentNetwork();
        n.Add(new PhotovoltaicComponent("pv", SunPowerPanel()));
        n.Add(new BatteryComponent("pack", ModelSPack()));
        n.Add(new MotorComponent("motor", ModelSMotor()));

        // Wire: PV MPP voltage drives the bus → motor BusVoltage_V.
        n.Connect("pv",  "MaxPowerPointVoltage_V", "motor", "BusVoltage_V");

        // External inputs:
        n.SetExternalInput("pv",    "Irradiance_W_m2",       800.0);
        n.SetExternalInput("pv",    "CellTemperature_C",      55.0);
        n.SetExternalInput("pack",  "LoadCurrent_A",          50.0);
        n.SetExternalInput("motor", "ArmatureCurrent_A",      50.0);

        var r = n.Solve();

        // PV at 800 W/m², 55 °C, 96-cell mono panel → MPP V around 50 V
        // (vs ~ 55 V STC due to temperature). MPP power around 250 W.
        Assert.InRange(r["pv"]["MaxPower_W"], 150.0, 350.0);
        Assert.InRange(r["pv"]["MaxPowerPointVoltage_V"], 35.0, 60.0);

        // Motor at low BusVoltage (~ 50 V from PV) and 50 A current:
        // V_emf = 50 − 50·0.05 = 47.5 V; ω = 47.5/0.5 = 95 rad/s ≈ 907 rpm.
        Assert.InRange(r["motor"]["RotationSpeed_rpm"], 700.0, 1100.0);
        Assert.Equal(25.0, r["motor"]["ShaftTorque_Nm"], precision: 6);
    }

    // ── Solar-thermal Stirling cogen with radiator heat sink ────────────

    [Fact]
    public void Demo_SolarThermal_To_Stirling_To_Radiator()
    {
        // Topology: parabolic-trough collector → Stirling engine (hot
        // side) → spacecraft radiator (cold-side heat rejection).
        // The Stirling indicated power flows out via its electrical
        // port; the Q_cold flows to the radiator as the heat to reject.
        var n = new ComponentNetwork();
        n.Add(new SolarThermalComponent("st", ParabolicTrough()));
        n.Add(new StirlingComponent("str", AdvancedStirling()));
        n.Add(new RadiatorComponent("rad", DeepSpaceRadiator()));

        // Sprint SI.W4: the per-component wiring is currently advisory —
        // we set external inputs for everything and verify the
        // standalone-solver values come through coherent. A future
        // SI.W5 sprint will add a "ThermalConduit" wire that conserves
        // heat-flux between the Stirling cold side and the radiator
        // hot side automatically.
        n.SetExternalInput("st",  "DirectNormalIrradiance_W_m2", 800.0);
        n.SetExternalInput("st",  "CollectorTemperature_C",      400.0);
        n.SetExternalInput("st",  "AmbientTemperature_C",         25.0);
        n.SetExternalInput("str", "HotSideTemperature_K",        850.0);
        n.SetExternalInput("str", "ColdSideTemperature_K",       350.0);
        n.SetExternalInput("rad", "OperatingTemperature_K",      350.0);

        var r = n.Solve();

        // Stirling at 850/350 K, 1 MPa He, 40 cm³, 50 Hz → ~ 1 kW elec
        // (WhisperGen baseline).
        Assert.InRange(r["str"]["IndicatedPower_W"], 500.0, 2000.0);

        // Solar-thermal trough at 400 °C, 800 W/m², 1 m² aperture:
        // Q_useful ≈ 420 W per m².
        Assert.InRange(r["st"]["UsefulHeatPower_W"], 300.0, 600.0);

        // Radiator at T = 350 K vs T_sink = 240 K rejects ~ 4 kW per
        // 30 m² in the cold-eclipse case.
        Assert.True(r["rad"]["NetHeatRejectionRate_W"] > 0);
    }

    // ── Green-H₂ chain: PEM electrolyser + H₂ tank + PEM fuel cell ─────

    [Fact]
    public void Demo_GreenHydrogenChain_ElectrolyserPlusTankPlusFuelCell()
    {
        // Topology:
        //   (renewable electrical input) → PEM Electrolyser → H₂ Tank
        //   H₂ Tank → PEM Fuel Cell → (electrical output)
        //
        // For Sprint SI.W4 we treat tank as a static storage element
        // (no time-domain integration of mass yet — Sprint SI.W5 will
        // add that). We just verify the electrolyser produces a
        // positive H₂ flow rate AND the fuel cell produces a positive
        // electrical power at independent operating points.
        var n = new ComponentNetwork();
        n.Add(new ElectrolyserComponent("el", NelA485Electrolyser()));
        n.Add(new HydrogenStorageComponent("tank", MiraiTank700bar()));
        n.Add(new PowerGenComponent("fc", MiraiFuelCell()));

        n.SetExternalInput("el", "OperatingCurrentDensity_A_cm2", 1.5);
        n.SetExternalInput("fc", "OperatingCurrentDensity_A_cm2", 1.0);

        var r = n.Solve();

        // Electrolyser at 1.5 A/cm² produces ~ 12 Nm³/h H₂.
        Assert.InRange(r["el"]["HydrogenProductionRate_Nm3_h"], 8.0, 16.0);

        // Tank stores ~ 4.9 kg compressed H₂.
        Assert.InRange(r["tank"]["StoredHydrogenMass_kg"], 3.0, 6.0);

        // Fuel cell at 1.0 A/cm² produces ~ 40 kW.
        Assert.InRange(r["fc"]["StackElectricPower_W"], 30_000.0, 60_000.0);
    }

    // ── Wind + Hydro generation grid ────────────────────────────────────

    [Fact]
    public void Demo_HybridRenewableGenerationGrid_WindPlusHydro()
    {
        // Two independent generators feeding a common conceptual grid.
        // Verify both produce power at design point.
        var n = new ComponentNetwork();
        n.Add(new WindTurbineComponent("wt", Nrel5MW()));
        n.Add(new HydroelectricComponent("he", ThreeGorgesUnit()));

        n.SetExternalInput("wt", "WindSpeed_ms", 11.4);
        n.SetExternalInput("he", "Head_m", 80.0);
        n.SetExternalInput("he", "VolumetricFlowRate_m3s", 850.0);

        var r = n.Solve();

        Assert.InRange(r["wt"]["ElectricalPower_W"], 4e6, 6e6);     // ~ 5 MW
        Assert.InRange(r["he"]["ElectricalPower_W"], 4e8, 8e8);     // ~ 600 MW
    }

    // ── Thermal-cooling loop: TEG + Heat pipe + Radiator ───────────────

    [Fact]
    public void Demo_RTG_HeatPipe_Radiator_SpacecraftThermalLoop()
    {
        // A simplified spacecraft thermal-power loop:
        //   TEG (RTG) generates electricity from Pu-238 heat;
        //   Heat pipe transports waste heat from RTG cold side;
        //   Radiator dumps heat to deep space.
        var n = new ComponentNetwork();
        n.Add(new ThermoelectricComponent("teg", CassiniRtg()));
        n.Add(new HeatPipeComponent("hp", SodiumHeatPipe()));
        n.Add(new RadiatorComponent("rad", DeepSpaceRadiator()));

        n.SetExternalInput("teg", "HotSideTemperature_K",  1273.0);
        n.SetExternalInput("teg", "ColdSideTemperature_K",  575.0);
        n.SetExternalInput("teg", "HotSideHeatInput_W",    4400.0);
        n.SetExternalInput("hp",  "HeatThroughput_W",      4000.0);
        n.SetExternalInput("hp",  "OperatingTemperature_K", 700.0);
        n.SetExternalInput("rad", "OperatingTemperature_K", 320.0);

        var r = n.Solve();

        // RTG ~ 470 W electrical output (theoretical figure-of-merit).
        Assert.InRange(r["teg"]["ElectricPowerOutput_W"], 300.0, 600.0);
        // Heat pipe at 4 kW: ΔT < 50 K (sodium-stainless at high
        // operating T conducts very efficiently).
        Assert.True(r["hp"]["EndToEndDeltaT_K"] < 50.0);
        // Radiator rejects positive heat at 320 K.
        Assert.True(r["rad"]["NetHeatRejectionRate_W"] > 0);
    }

    // ── 5-pillar mega-subsystem (PV + BP + EM + RAD + HX) ──────────────

    [Fact]
    public void Demo_MegaSubsystem_FiveComponentEvWithCoolingLoop()
    {
        // PV solar → Battery → Motor (3-component spine) PLUS a
        // heat-exchanger handling battery cooling AND a radiator
        // dumping the heat. All 5 components solve simultaneously.
        var n = new ComponentNetwork();
        n.Add(new PhotovoltaicComponent("pv",   SunPowerPanel()));
        n.Add(new BatteryComponent("pack",      ModelSPack()));
        n.Add(new MotorComponent("motor",       ModelSMotor()));
        n.Add(new HeatExchangerComponent("hx",  CoolingHX()));
        n.Add(new RadiatorComponent("rad",      DeepSpaceRadiator()));

        // Wiring (all DAG — no cycles, so plain Solve works):
        n.Connect("pack", "PackLoadedVoltage_V", "motor", "BusVoltage_V");

        n.SetExternalInput("pv",    "Irradiance_W_m2",       1000.0);
        n.SetExternalInput("pv",    "CellTemperature_C",       25.0);
        n.SetExternalInput("pack",  "LoadCurrent_A",          100.0);
        n.SetExternalInput("motor", "ArmatureCurrent_A",      100.0);
        // HX inputs cover hot/cold side flows (battery cooling).
        n.SetExternalInput("hx", "HotMassFlow_kgs",        0.10);
        n.SetExternalInput("hx", "ColdMassFlow_kgs",       0.10);
        n.SetExternalInput("hx", "HotInletTemperature_K",  330.0);
        n.SetExternalInput("hx", "ColdInletTemperature_K", 290.0);
        // Radiator input.
        n.SetExternalInput("rad", "OperatingTemperature_K", 320.0);

        var r = n.Solve();

        // Sanity-check that every component produced output values.
        Assert.True(r["pv"]["MaxPower_W"]              > 0);
        Assert.True(r["pack"]["PackLoadedVoltage_V"]   > 0);
        Assert.True(r["motor"]["MechanicalPower_W"]    > 0);
        Assert.True(r["hx"]["HeatDuty_W"]              > 0);
        Assert.True(r["rad"]["NetHeatRejectionRate_W"] > 0);
    }

    // ── Helpers — design fixtures ────────────────────────────────────────

    private static PvPanelDesign SunPowerPanel() => new(
        CellType:           PhotovoltaicCellType.Monocrystalline,
        CellsInSeries:      96, StringsInParallel: 1,
        CellArea_cm2:       161.5, Irradiance_W_m2: 1000.0,
        CellTemperature_C:  25.0);

    private static BatteryPackDesign ModelSPack() => new(
        Chemistry: BatteryChemistry.NickelManganeseCobalt,
        CellsInSeries: 96, ParallelStrings: 46,
        StateOfCharge: 1.0, LoadCurrent_A: 0.0);

    private static MotorDesign ModelSMotor() => new(
        Kind: MotorKind.PermanentMagnetSynchronous,
        TorqueConstant_NmA: 0.5, ArmatureResistance_Ohm: 0.05,
        ConstantPowerLoss_W: 500.0,
        BusVoltage_V: 400.0, ArmatureCurrent_A: 100.0);

    private static SolarCollectorDesign ParabolicTrough() => new(
        Kind: SolarCollectorKind.ParabolicTrough,
        ApertureArea_m2: 1.0, DirectNormalIrradiance_W_m2: 800.0,
        CollectorTemperature_C: 400.0, AmbientTemperature_C: 25.0);

    private static StirlingDesign AdvancedStirling() => new(
        Configuration: StirlingConfiguration.Gamma,
        HotSideTemperature_K: 850.0, ColdSideTemperature_K: 350.0,
        MeanPressure_Pa: 1e6, SweptVolume_m3: 4e-5,
        OperatingFrequency_Hz: 50.0, SecondLawEfficiency: 0.45);

    private static SpacecraftRadiatorDesign DeepSpaceRadiator() => new(
        Kind: RadiatorKind.FlatPanel, PanelArea_m2: 30.0,
        OperatingTemperature_K: 320.0, SinkTemperature_K: 240.0,
        Emissivity: 0.85, SolarAbsorptivity: 0.20,
        IncidentSolarFlux_W_m2: 0.0);

    private static PemElectrolyserDesign NelA485Electrolyser() => new(
        Kind: ElectrolyserKind.Pem, CellCount: 100,
        ActiveAreaPerCell_cm2: 200.0,
        OperatingCurrentDensity_A_cm2: 1.5,
        OperatingTemperature_C: 70.0, OperatingPressure_bar: 10.0);

    private static HydrogenStorageDesign MiraiTank700bar() => new(
        Kind: HydrogenStorageKind.CompressedGas,
        InternalVolume_m3: 0.122, OperatingPressure_bar: 700.0,
        OperatingTemperature_K: 298.15, DryMass_kg: 95.0);

    private static PemFuelCellDesign MiraiFuelCell() => new(
        Kind: PowerGenKind.PemFuelCell, CellCount: 330,
        ActiveAreaPerCell_cm2: 200.0,
        OperatingCurrentDensity_A_cm2: 1.0,
        OperatingTemperature_C: 80.0, OperatingPressure_bar: 2.5);

    private static HawtDesign Nrel5MW() => new(
        Kind: WindTurbineKind.HorizontalAxis, RotorRadius_m: 63.0,
        BladeCount: 3, HubHeight_m: 90.0, DesignWindSpeed_ms: 11.4,
        DesignTipSpeedRatio: 7.5, GearboxAndGeneratorEfficiency: 0.944,
        CutInWindSpeed_ms: 3.0, CutOutWindSpeed_ms: 25.0);

    private static HydroTurbineDesign ThreeGorgesUnit() => new(
        Kind: HydroTurbineKind.Francis, Head_m: 80.0,
        VolumetricFlowRate_m3s: 850.0, GeneratorEfficiency: 0.97);

    private static ThermoelectricGeneratorDesign CassiniRtg() => new(
        Material: ThermoelectricMaterial.SiliconGermanium,
        HotSideTemperature_K: 1273.0, ColdSideTemperature_K: 575.0,
        HotSideHeatInput_W: 4400.0);

    private static HeatPipeDesign SodiumHeatPipe() => new(
        Fluid: HeatPipeFluid.Sodium, InternalDiameter_m: 0.025,
        Length_m: 1.0, HeatThroughput_W: 4000.0,
        OperatingTemperature_K: 700.0);

    private static PlateFinDesign CoolingHX() => new(
        Kind: HeatExchangerKind.PlateFinCounterflow,
        CoreLength_m: 0.10, CoreWidth_m: 0.15, CoreHeight_m: 0.10,
        PlateSpacing_m: 0.006, FinPitch_m: 0.002, FinThickness_m: 0.0004,
        HotMassFlow_kgs: 0.10, ColdMassFlow_kgs: 0.10,
        HotInletTemperature_K: 330.0, ColdInletTemperature_K: 290.0,
        HotCp_JkgK: 4180.0, ColdCp_JkgK: 4180.0,    // water both sides
        HotDensity_kgm3: 1000.0, ColdDensity_kgm3: 1000.0,
        HotViscosity_PaS: 5.5e-4, ColdViscosity_PaS: 1.0e-3);
}
