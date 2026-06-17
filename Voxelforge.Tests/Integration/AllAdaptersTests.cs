// AllAdaptersTests.cs — Sprint SI.W2 unit tests confirming each new
// pillar adapter round-trips bit-identical to the underlying pillar
// solver's standalone output.

using Voxelforge.Antenna;
using Voxelforge.Battery;
using Voxelforge.Chemical;
using Voxelforge.Compressor;
using Voxelforge.Electrolyser;
using Voxelforge.ElectricMotor;
using Voxelforge.Flywheel;
using Voxelforge.HeatExchanger;
using Voxelforge.HeatPipe;
using Voxelforge.Hybrid;
using Voxelforge.Hydroelectric;
using Voxelforge.HydrogenStorage;
using Voxelforge.Integration;
using Voxelforge.Integration.Components;
using Voxelforge.Photovoltaic;
using Voxelforge.PowerGen;
using Voxelforge.Pump;
using Voxelforge.Radiator;
using Voxelforge.Refrigeration;
using Voxelforge.SolarThermal;
using Voxelforge.Stirling;
using Voxelforge.Tankage;
using Voxelforge.Thermoelectric;
using Voxelforge.WindTurbine;
using Xunit;

namespace Voxelforge.Tests.Integration;

public sealed class AllAdaptersTests
{
    // Each test wraps the pillar solver via the adapter, runs the
    // ComponentNetwork, and asserts the returned output port value
    // matches the value the standalone solver would have produced.

    [Fact]
    public void PowerGen_AdapterMatchesStandalone()
    {
        var d = new PemFuelCellDesign(
            Kind: PowerGenKind.PemFuelCell, CellCount: 330,
            ActiveAreaPerCell_cm2: 200.0,
            OperatingCurrentDensity_A_cm2: 1.0,
            OperatingTemperature_C: 80.0, OperatingPressure_bar: 2.5);
        var n = new ComponentNetwork();
        n.Add(new PowerGenComponent("pg", d));
        n.SetExternalInput("pg", "OperatingCurrentDensity_A_cm2", 1.0);
        var r = n.Solve();
        var standalone = PemFuelCellSolver.Solve(d);
        Assert.Equal(standalone.StackElectricPower_W,
            r["pg"]["StackElectricPower_W"], precision: 6);
    }

    [Fact]
    public void WindTurbine_AdapterMatchesStandalone()
    {
        var d = new HawtDesign(
            Kind: WindTurbineKind.HorizontalAxis, RotorRadius_m: 63.0,
            BladeCount: 3, HubHeight_m: 90.0, DesignWindSpeed_ms: 11.4,
            DesignTipSpeedRatio: 7.5, GearboxAndGeneratorEfficiency: 0.944,
            CutInWindSpeed_ms: 3.0, CutOutWindSpeed_ms: 25.0);
        var n = new ComponentNetwork();
        n.Add(new WindTurbineComponent("wt", d));
        n.SetExternalInput("wt", "WindSpeed_ms", 11.4);
        var r = n.Solve();
        var standalone = HawtSolver.Solve(d, windSpeed_ms: 11.4);
        Assert.Equal(standalone.ElectricalPower_W,
            r["wt"]["ElectricalPower_W"], precision: 6);
    }

    [Fact]
    public void Photovoltaic_AdapterMatchesStandalone()
    {
        var d = new PvPanelDesign(
            CellType: PhotovoltaicCellType.Monocrystalline,
            CellsInSeries: 96, StringsInParallel: 1,
            CellArea_cm2: 161.5, Irradiance_W_m2: 1000.0,
            CellTemperature_C: 25.0);
        var n = new ComponentNetwork();
        n.Add(new PhotovoltaicComponent("pv", d));
        n.SetExternalInput("pv", "Irradiance_W_m2", 1000.0);
        n.SetExternalInput("pv", "CellTemperature_C", 25.0);
        var r = n.Solve();
        var standalone = PvPanelSolver.Solve(d);
        Assert.Equal(standalone.MaxPower_W, r["pv"]["MaxPower_W"], precision: 6);
    }

    [Fact]
    public void Electrolyser_AdapterMatchesStandalone()
    {
        var d = new PemElectrolyserDesign(
            Kind: ElectrolyserKind.Pem, CellCount: 100,
            ActiveAreaPerCell_cm2: 200.0,
            OperatingCurrentDensity_A_cm2: 1.5,
            OperatingTemperature_C: 70.0, OperatingPressure_bar: 10.0);
        var n = new ComponentNetwork();
        n.Add(new ElectrolyserComponent("el", d));
        n.SetExternalInput("el", "OperatingCurrentDensity_A_cm2", 1.5);
        var r = n.Solve();
        var standalone = PemElectrolyserSolver.Solve(d);
        Assert.Equal(standalone.HydrogenProductionRate_kgs,
            r["el"]["HydrogenProductionRate_kgs"], precision: 12);
    }

    [Fact]
    public void Hydroelectric_AdapterMatchesStandalone()
    {
        var d = new HydroTurbineDesign(
            Kind: HydroTurbineKind.Francis, Head_m: 80.0,
            VolumetricFlowRate_m3s: 850.0, GeneratorEfficiency: 0.97);
        var n = new ComponentNetwork();
        n.Add(new HydroelectricComponent("he", d));
        n.SetExternalInput("he", "Head_m", 80.0);
        n.SetExternalInput("he", "VolumetricFlowRate_m3s", 850.0);
        var r = n.Solve();
        var standalone = HydroTurbineSolver.Solve(d);
        Assert.Equal(standalone.ElectricalPower_W,
            r["he"]["ElectricalPower_W"], precision: 3);
    }

    [Fact]
    public void Radiator_AdapterMatchesStandalone()
    {
        var d = new SpacecraftRadiatorDesign(
            Kind: RadiatorKind.FlatPanel, PanelArea_m2: 30.0,
            OperatingTemperature_K: 320.0, SinkTemperature_K: 240.0,
            Emissivity: 0.85, SolarAbsorptivity: 0.20,
            IncidentSolarFlux_W_m2: 0.0);
        var n = new ComponentNetwork();
        n.Add(new RadiatorComponent("rad", d));
        n.SetExternalInput("rad", "OperatingTemperature_K", 320.0);
        var r = n.Solve();
        var standalone = SpacecraftRadiatorSolver.Solve(d);
        Assert.Equal(standalone.NetHeatRejectionRate_W,
            r["rad"]["NetHeatRejectionRate_W"], precision: 3);
    }

    [Fact]
    public void HydrogenStorage_AdapterMatchesStandalone()
    {
        var d = new HydrogenStorageDesign(
            Kind: HydrogenStorageKind.CompressedGas,
            InternalVolume_m3: 0.122, OperatingPressure_bar: 700.0,
            OperatingTemperature_K: 298.15, DryMass_kg: 95.0);
        var n = new ComponentNetwork();
        n.Add(new HydrogenStorageComponent("tank", d));
        var r = n.Solve();
        var standalone = HydrogenStorageSolver.Solve(d);
        Assert.Equal(standalone.StoredHydrogenMass_kg,
            r["tank"]["StoredHydrogenMass_kg"], precision: 6);
    }

    [Fact]
    public void Thermoelectric_AdapterMatchesStandalone()
    {
        var d = new ThermoelectricGeneratorDesign(
            Material: ThermoelectricMaterial.SiliconGermanium,
            HotSideTemperature_K: 1273.0, ColdSideTemperature_K: 575.0,
            HotSideHeatInput_W: 4400.0);
        var n = new ComponentNetwork();
        n.Add(new ThermoelectricComponent("teg", d));
        n.SetExternalInput("teg", "HotSideTemperature_K",  1273.0);
        n.SetExternalInput("teg", "ColdSideTemperature_K",  575.0);
        n.SetExternalInput("teg", "HotSideHeatInput_W",    4400.0);
        var r = n.Solve();
        var standalone = ThermoelectricGeneratorSolver.Solve(d);
        Assert.Equal(standalone.ElectricPowerOutput_W,
            r["teg"]["ElectricPowerOutput_W"], precision: 4);
    }

    [Fact]
    public void SolarThermal_AdapterMatchesStandalone()
    {
        var d = new SolarCollectorDesign(
            Kind: SolarCollectorKind.ParabolicTrough,
            ApertureArea_m2: 1.0, DirectNormalIrradiance_W_m2: 800.0,
            CollectorTemperature_C: 400.0, AmbientTemperature_C: 25.0);
        var n = new ComponentNetwork();
        n.Add(new SolarThermalComponent("st", d));
        n.SetExternalInput("st", "DirectNormalIrradiance_W_m2", 800.0);
        n.SetExternalInput("st", "CollectorTemperature_C",      400.0);
        n.SetExternalInput("st", "AmbientTemperature_C",         25.0);
        var r = n.Solve();
        var standalone = SolarCollectorSolver.Solve(d);
        Assert.Equal(standalone.UsefulHeatPower_W,
            r["st"]["UsefulHeatPower_W"], precision: 6);
    }

    [Fact]
    public void Compressor_AdapterMatchesStandalone()
    {
        var d = new CentrifugalCompressorDesign(
            Kind: CompressorKind.Centrifugal, MassFlow_kgs: 0.30,
            InletTotalTemperature_K: 298.0, InletTotalPressure_Pa: 101325.0,
            PressureRatio: 2.5, IsentropicEfficiency: 0.74,
            WorkingGasGamma: 1.40, WorkingGasSpecificHeat_J_kgK: 1005.0);
        var n = new ComponentNetwork();
        n.Add(new CompressorComponent("cmp", d));
        n.SetExternalInput("cmp", "MassFlow_kgs",             0.30);
        n.SetExternalInput("cmp", "InletTotalTemperature_K",  298.0);
        n.SetExternalInput("cmp", "InletTotalPressure_Pa", 101325.0);
        n.SetExternalInput("cmp", "PressureRatio",             2.5);
        var r = n.Solve();
        var standalone = CentrifugalCompressorSolver.Solve(d);
        Assert.Equal(standalone.ShaftPowerInput_W,
            r["cmp"]["ShaftPowerInput_W"], precision: 3);
    }

    [Fact]
    public void Pump_AdapterMatchesStandalone()
    {
        var d = new CentrifugalPumpDesign(
            Kind: PumpKind.Centrifugal, VolumetricFlowRate_m3s: 0.050,
            HeadRise_m: 50.0, RotationSpeed_rpm: 3600,
            OverallEfficiency: 0.75);
        var n = new ComponentNetwork();
        n.Add(new PumpComponent("pmp", d));
        n.SetExternalInput("pmp", "VolumetricFlowRate_m3s", 0.050);
        n.SetExternalInput("pmp", "HeadRise_m",              50.0);
        var r = n.Solve();
        var standalone = CentrifugalPumpSolver.Solve(d);
        Assert.Equal(standalone.HydraulicPower_W,
            r["pmp"]["HydraulicPower_W"], precision: 3);
    }

    [Fact]
    public void Refrigeration_AdapterMatchesStandalone()
    {
        var d = new RefrigerationDesign(
            Mode: RefrigerationMode.Cooling, Refrigerant: Refrigerant.R410A,
            ColdReservoirTemperature_K: 283.15,
            HotReservoirTemperature_K: 308.15,
            CompressorPowerInput_W: 3500.0);
        var n = new ComponentNetwork();
        n.Add(new RefrigerationComponent("rfg", d));
        n.SetExternalInput("rfg", "ColdReservoirTemperature_K", 283.15);
        n.SetExternalInput("rfg", "HotReservoirTemperature_K",  308.15);
        n.SetExternalInput("rfg", "CompressorPowerInput_W",   3500.0);
        var r = n.Solve();
        var standalone = RefrigerationSolver.Solve(d);
        Assert.Equal(standalone.ColdSideHeatRemoval_W,
            r["rfg"]["ColdSideHeatRemoval_W"], precision: 3);
    }

    [Fact]
    public void HeatExchanger_AdapterMatchesStandalone()
    {
        var d = new PlateFinDesign(
            Kind: HeatExchangerKind.PlateFinCounterflow,
            CoreLength_m: 0.10, CoreWidth_m: 0.15, CoreHeight_m: 0.10,
            PlateSpacing_m: 0.006, FinPitch_m: 0.002, FinThickness_m: 0.0004,
            HotMassFlow_kgs: 0.05, ColdMassFlow_kgs: 0.05,
            HotInletTemperature_K: 700.0, ColdInletTemperature_K: 300.0,
            HotCp_JkgK: 1100.0, ColdCp_JkgK: 1050.0,
            HotDensity_kgm3: 0.5, ColdDensity_kgm3: 1.0,
            HotViscosity_PaS: 3.5e-5, ColdViscosity_PaS: 2.0e-5);
        var n = new ComponentNetwork();
        n.Add(new HeatExchangerComponent("hx", d));
        n.SetExternalInput("hx", "HotMassFlow_kgs",        0.05);
        n.SetExternalInput("hx", "ColdMassFlow_kgs",       0.05);
        n.SetExternalInput("hx", "HotInletTemperature_K",  700.0);
        n.SetExternalInput("hx", "ColdInletTemperature_K", 300.0);
        var r = n.Solve();
        var standalone = EpsilonNtuSolver.Solve(d);
        Assert.Equal(standalone.HeatDuty_W, r["hx"]["HeatDuty_W"], precision: 3);
    }

    [Fact]
    public void Chemical_AdapterMatchesStandalone()
    {
        var d = new ReactorDesign(
            Kind: ReactorKind.Cstr, ReactorVolume_m3: 0.100,
            VolumetricFlowRate_m3s: 1.667e-4,
            InletConcentration_mol_m3: 500.0,
            OperatingTemperature_K: 298.15,
            ArrheniusPreExponential_per_s: 6.1e4,
            ActivationEnergy_J_mol: 45_000.0);
        var n = new ComponentNetwork();
        n.Add(new ChemicalReactorComponent("chm", d));
        n.SetExternalInput("chm", "OperatingTemperature_K",    298.15);
        n.SetExternalInput("chm", "VolumetricFlowRate_m3s",    1.667e-4);
        n.SetExternalInput("chm", "InletConcentration_mol_m3", 500.0);
        var r = n.Solve();
        var standalone = ReactorSolver.Solve(d);
        Assert.Equal(standalone.Conversion, r["chm"]["Conversion"], precision: 9);
    }

    [Fact]
    public void HeatPipe_AdapterMatchesStandalone()
    {
        var d = new HeatPipeDesign(
            Fluid: HeatPipeFluid.Water, InternalDiameter_m: 0.006,
            Length_m: 0.20, HeatThroughput_W: 50.0,
            OperatingTemperature_K: 353.15);
        var n = new ComponentNetwork();
        n.Add(new HeatPipeComponent("hp", d));
        n.SetExternalInput("hp", "HeatThroughput_W",        50.0);
        n.SetExternalInput("hp", "OperatingTemperature_K", 353.15);
        var r = n.Solve();
        var standalone = HeatPipeSolver.Solve(d);
        Assert.Equal(standalone.EndToEndDeltaT_K,
            r["hp"]["EndToEndDeltaT_K"], precision: 9);
    }

    [Fact]
    public void Flywheel_AdapterMatchesStandalone()
    {
        var d = new FlywheelDesign(
            Shape: FlywheelShape.SolidDisk,
            Material: FlywheelMaterial.CarbonFibreComposite,
            OuterRadius_m: 0.30, Mass_kg: 100.0,
            RotationSpeed_rpm: 16000.0);
        var n = new ComponentNetwork();
        n.Add(new FlywheelComponent("fw", d));
        n.SetExternalInput("fw", "StateOfCharge", 1.0);
        var r = n.Solve();
        var standalone = FlywheelSolver.Solve(d);
        Assert.Equal(standalone.StoredEnergy_kWh,
            r["fw"]["StoredEnergy_kWh"], precision: 6);
    }

    [Fact]
    public void Stirling_AdapterMatchesStandalone()
    {
        var d = new StirlingDesign(
            Configuration: StirlingConfiguration.Gamma,
            HotSideTemperature_K: 850.0, ColdSideTemperature_K: 350.0,
            MeanPressure_Pa: 1e6, SweptVolume_m3: 4e-5,
            OperatingFrequency_Hz: 50.0, SecondLawEfficiency: 0.45);
        var n = new ComponentNetwork();
        n.Add(new StirlingComponent("str", d));
        n.SetExternalInput("str", "HotSideTemperature_K",  850.0);
        n.SetExternalInput("str", "ColdSideTemperature_K", 350.0);
        var r = n.Solve();
        var standalone = StirlingSolver.Solve(d);
        Assert.Equal(standalone.IndicatedPower_W,
            r["str"]["IndicatedPower_W"], precision: 3);
    }

    [Fact]
    public void AntennaLink_AdapterMatchesStandalone()
    {
        var d = new AntennaLinkDesign(
            TransmitAntennaKind: Voxelforge.Antenna.AntennaKind.ParabolicDish,
            ReceiveAntennaKind:  Voxelforge.Antenna.AntennaKind.ParabolicDish,
            Frequency_Hz: 8.4e9, TransmitPower_W: 20.0,
            LinkDistance_m: 1.43e12,
            TransmitDishDiameter_m: 4.0, ReceiveDishDiameter_m: 70.0);
        var n = new ComponentNetwork();
        n.Add(new AntennaLinkComponent("ant", d));
        n.SetExternalInput("ant", "TransmitPower_W",  20.0);
        n.SetExternalInput("ant", "LinkDistance_m", 1.43e12);
        var r = n.Solve();
        var standalone = Voxelforge.Antenna.AntennaSolver.Solve(d);
        Assert.Equal(standalone.ReceivedPower_dBm,
            r["ant"]["ReceivedPower_dBm"], precision: 4);
    }

    [Fact]
    public void Tankage_AdapterMatchesStandalone()
    {
        var d = new PressureVesselDesign(
            ShellType: Voxelforge.Tankage.TankShellType.Steel4130,
            InternalRadius_m: 1.83, ShellLength_m: 20.0,
            WallThickness_m: 0.00478, OperatingPressure_Pa: 3e5);
        var n = new ComponentNetwork();
        n.Add(new TankageComponent("tank", d));
        n.SetExternalInput("tank", "OperatingPressure_Pa", 3e5);
        var r = n.Solve();
        var standalone = Voxelforge.Tankage.PressureVesselSolver.Solve(d);
        Assert.Equal(standalone.HoopStress_Pa,
            r["tank"]["HoopStress_Pa"], precision: 3);
    }

    [Fact]
    public void Aerostructures_AdapterMatchesStandalone()
    {
        var d = new Voxelforge.Aerostructures.WingSparDesign(
            SectionType: Voxelforge.Aerostructures.SparSectionType.HollowRectangularBox,
            Material: Voxelforge.Aerostructures.SparMaterial.Aluminum7075,
            HalfSpan_m: 5.5, OuterHeight_m: 0.20, OuterWidth_m: 0.080,
            WallThickness_m: 0.008, DistributedLift_Nm: 981.0,
            LoadFactor: 3.8);
        var n = new ComponentNetwork();
        n.Add(new AerostructuresComponent("as", d));
        n.SetExternalInput("as", "DistributedLift_Nm", 981.0);
        n.SetExternalInput("as", "LoadFactor",            3.8);
        var r = n.Solve();
        var standalone = Voxelforge.Aerostructures.WingSparSolver.Solve(d);
        Assert.Equal(standalone.MaximumBendingStress_Pa,
            r["as"]["MaximumBendingStress_Pa"], precision: 3);
    }

    [Fact]
    public void HybridRocket_AdapterMatchesStandalone()
    {
        var d = new HybridRocketDesign(
            Fuel: HybridFuel.HTPB, GrainLength_m: 0.50,
            InitialPortRadius_m: 0.025, OuterGrainRadius_m: 0.075,
            OxidiserMassFlow_kgs: 0.50, ChamberPressure_bar: 20.0,
            ExpansionRatio: 10.0);
        var n = new ComponentNetwork();
        n.Add(new HybridRocketComponent("rkt", d));
        n.SetExternalInput("rkt", "PortRadius_m", 0.025);
        var r = n.Solve();
        var standalone = HybridRocketCycleSolver.Solve(d, 0.025);
        Assert.Equal(standalone.VacuumThrust_N,
            r["rkt"]["VacuumThrust_N"], precision: 3);
    }
}
