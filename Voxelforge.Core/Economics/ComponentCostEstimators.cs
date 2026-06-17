// ComponentCostEstimators.cs — Sprint EC.W1 cluster-anchored cost
// factories for the three highest-impact electric-stack pillars.
//
// All figures are 2026 cluster mid-band; vendor pricing spans ±30 %.
//
//   Battery (Li-ion NMC / LFP):
//     • Mass:     ~ 0.070 kg per Wh   (NMC) / 0.090 (LFP)
//     • Capex:    ~ $135 / kWh        (NMC, pack-level 2026)
//                 ~ $105 / kWh        (LFP)
//     • CO₂:     ~ 75 kgCO₂/kWh       (NMC, BloombergNEF 2025)
//                 ~ 60 kgCO₂/kWh      (LFP)
//   Photovoltaic (mono-Si module):
//     • Mass:     ~ 0.013 kg/W       (module-level)
//     • Capex:    ~ $0.21 / W        (module-only, 2026)
//     • CO₂:     ~ 0.50 kgCO₂/W      (cradle-to-gate)
//   Electric motor (PMSM traction):
//     • Mass:     ~ 0.40 kg/kW
//     • Capex:    ~ $25 / kW         (high-volume EV pricing)
//     • CO₂:     ~ 4.5 kgCO₂/kW      (rare-earth permanent magnets)
//
// Sources: BloombergNEF 2025 Battery Price Survey; IEA 2024 PV Cost
// Roadmap; Tesla Model 3 / Model S teardowns; Munro & Associates
// 2023 motor disassembly reports.

using System;
using Voxelforge.Aerostructures;
using Voxelforge.Antenna;
using Voxelforge.Battery;
using Voxelforge.Chemical;
using Voxelforge.Compressor;
using Voxelforge.ElectricMotor;
using Voxelforge.Electrolyser;
using Voxelforge.Flywheel;
using Voxelforge.HeatExchanger;
using Voxelforge.HeatPipe;
using Voxelforge.Hybrid;
using Voxelforge.Hydroelectric;
using Voxelforge.HydrogenStorage;
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

namespace Voxelforge.Economics;

/// <summary>
/// Cluster-anchored cost / mass / CO₂ factories for select pillars
/// (Sprint EC.W1).
/// </summary>
internal static class ComponentCostEstimators
{
    // ── Battery ─────────────────────────────────────────────────────────

    /// <summary>
    /// Estimate cost of a battery pack from its design + chemistry.
    /// </summary>
    public static CostEstimate ForBattery(string componentName, BatteryPackDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var chem = BatteryChemistryRegistry.For(design.Chemistry);
        // Pack energy at nominal voltage × nominal capacity.
        double nominalCellVoltage = 0.5 * (chem.OcvMin_V + chem.OcvMax_V);
        double packEnergy_Wh = design.CellsInSeries * design.ParallelStrings
                             * nominalCellVoltage * chem.NominalCapacity_Ah;
        double packEnergy_kWh = packEnergy_Wh / 1000.0;

        // Cluster pricing per kWh by chemistry.
        var (massPerWh, costPerKWh, co2PerKWh) = design.Chemistry switch
        {
            BatteryChemistry.NickelManganeseCobalt => (0.0070, 135.0, 75.0),
            BatteryChemistry.LithiumIronPhosphate  => (0.0090, 105.0, 60.0),
            _ => throw new ArgumentOutOfRangeException(nameof(design),
                $"No cost data for chemistry '{design.Chemistry}'."),
        };

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              packEnergy_Wh * massPerWh,
            CapitalCost_USD:      packEnergy_kWh * costPerKWh,
            EmbodiedCO2_kgCO2eq:  packEnergy_kWh * co2PerKWh);
    }

    // ── Photovoltaic panel ──────────────────────────────────────────────

    /// <summary>
    /// Estimate cost of a PV panel from its design.
    /// </summary>
    public static CostEstimate ForPhotovoltaic(string componentName, PvPanelDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        // Approximate rated-power at STC via the solver.
        var stcDesign = design with { Irradiance_W_m2 = 1000.0, CellTemperature_C = 25.0 };
        var r = PvPanelSolver.Solve(stcDesign);
        double ratedPower_W = r.MaxPower_W;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_W * 0.013,
            CapitalCost_USD:      ratedPower_W * 0.21,
            EmbodiedCO2_kgCO2eq:  ratedPower_W * 0.50);
    }

    // ── Electric motor (PMSM / BLDC) ────────────────────────────────────

    /// <summary>
    /// Estimate cost of an electric motor from its design.
    /// </summary>
    public static CostEstimate ForMotor(string componentName, MotorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        // Mechanical-power proxy at the design operating point.
        var r = MotorSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.MechanicalPower_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 0.40,
            CapitalCost_USD:      ratedPower_kW * 25.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 4.5);
    }

    // ── Sprint EC.W2 ────────────────────────────────────────────────────
    // Wind turbine, electrolyser, hydrogen tank, PEM fuel cell, flywheel.

    /// <summary>
    /// Wind-turbine cost. Cluster-anchored to the IEA 2024 Onshore Wind
    /// Cost Roadmap: ~ $1100 / kW installed, ~ 110 kgCO₂/kW lifecycle,
    /// ~ 50 kg/kW (rotor + nacelle + tower steel-dominated).
    /// </summary>
    public static CostEstimate ForWindTurbine(string componentName, HawtDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        // Use the design wind speed to pull a representative power
        // figure from the BEM solver.
        var r = HawtSolver.Solve(design, design.DesignWindSpeed_ms);
        double ratedPower_kW = Math.Max(0.0, r.ElectricalPower_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 50.0,
            CapitalCost_USD:      ratedPower_kW * 1100.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 110.0);
    }

    /// <summary>
    /// PEM electrolyser cost. Cluster mid-band (2026 production-scale):
    /// $1500 / kW stack + BoP, 100 kgCO₂/kW, 10 kg/kW.
    /// </summary>
    public static CostEstimate ForElectrolyser(string componentName,
        PemElectrolyserDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = PemElectrolyserSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.StackElectricPower_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 10.0,
            CapitalCost_USD:      ratedPower_kW * 1500.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 100.0);
    }

    /// <summary>
    /// Hydrogen storage cost. Cost scales with kg-H₂ capacity, NOT
    /// power. Compressed Type-IV 700-bar: $500 / kg-H₂, 50 kgCO₂/kg-H₂,
    /// 14 kg-tank/kg-H₂. Cryogenic LH₂: $800 / kg-H₂, 70 kgCO₂/kg-H₂,
    /// 8 kg-tank/kg-H₂ (lighter but more expensive due to MLI).
    /// </summary>
    public static CostEstimate ForHydrogenStorage(string componentName,
        HydrogenStorageDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = HydrogenStorageSolver.Solve(design);
        double m_H2 = r.StoredHydrogenMass_kg;

        var (massPerKg, costPerKg, co2PerKg) = design.Kind switch
        {
            HydrogenStorageKind.CompressedGas    => (14.0, 500.0, 50.0),
            HydrogenStorageKind.LiquidCryogenic  => ( 8.0, 800.0, 70.0),
            HydrogenStorageKind.MetalHydride     => (50.0, 700.0, 60.0),
            _ => throw new ArgumentOutOfRangeException(nameof(design),
                $"No cost data for storage kind '{design.Kind}'."),
        };

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              m_H2 * massPerKg,
            CapitalCost_USD:      m_H2 * costPerKg,
            EmbodiedCO2_kgCO2eq:  m_H2 * co2PerKg);
    }

    /// <summary>
    /// PEM fuel-cell stack cost. Cluster: $300 / kW at automotive
    /// volume (DoE 2025 target), 30 kgCO₂/kW, 1.5 kg/kW.
    /// </summary>
    public static CostEstimate ForFuelCell(string componentName,
        PemFuelCellDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = PemFuelCellSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.StackElectricPower_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 1.5,
            CapitalCost_USD:      ratedPower_kW * 300.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 30.0);
    }

    /// <summary>
    /// Flywheel storage cost. Cluster mid-band for composite-rotor
    /// units: $1500 / kWh, 80 kgCO₂/kWh, 100 kg/kWh. Mass-dominated
    /// by the rotor + containment.
    /// </summary>
    public static CostEstimate ForFlywheel(string componentName,
        FlywheelDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = FlywheelSolver.Solve(design);
        double maxEnergy_kWh = Math.Max(0.0, r.StoredEnergy_kWh);

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              maxEnergy_kWh * 100.0,
            CapitalCost_USD:      maxEnergy_kWh * 1500.0,
            EmbodiedCO2_kgCO2eq:  maxEnergy_kWh * 80.0);
    }

    // ── Sprint EC.W4 ────────────────────────────────────────────────────
    // Compressor, pump, HX, radiator, hydro, solar thermal.

    /// <summary>
    /// Centrifugal compressor cost. Industrial cluster: ~ $1200/kW
    /// shaft, 20 kgCO₂/kW (cast iron + steel housings), 8 kg/kW.
    /// </summary>
    public static CostEstimate ForCompressor(string componentName,
        CentrifugalCompressorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = CentrifugalCompressorSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.ShaftPowerInput_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 8.0,
            CapitalCost_USD:      ratedPower_kW * 1200.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 20.0);
    }

    /// <summary>
    /// Centrifugal pump cost. Industrial cluster: ~ $700/kW shaft,
    /// 18 kgCO₂/kW, 5 kg/kW.
    /// </summary>
    public static CostEstimate ForPump(string componentName,
        CentrifugalPumpDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = CentrifugalPumpSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.ShaftPowerInput_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 5.0,
            CapitalCost_USD:      ratedPower_kW * 700.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 18.0);
    }

    /// <summary>
    /// Plate-fin heat-exchanger cost. Cost scales with thermal duty:
    /// ~ $250/kW thermal, 12 kgCO₂/kW, 4 kg/kW (compact aluminum cores).
    /// </summary>
    public static CostEstimate ForHeatExchanger(string componentName,
        PlateFinDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var r = EpsilonNtuSolver.Solve(design);
        double heatDuty_kW = Math.Max(0.0, r.HeatDuty_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              heatDuty_kW * 4.0,
            CapitalCost_USD:      heatDuty_kW * 250.0,
            EmbodiedCO2_kgCO2eq:  heatDuty_kW * 12.0);
    }

    /// <summary>
    /// Spacecraft flat-panel radiator cost. Aerospace pricing scales
    /// with area: ~ $50 000 / m² (LM-class aluminum honeycomb +
    /// optical coating + heat pipes), 60 kgCO₂/m², 6 kg/m².
    /// </summary>
    public static CostEstimate ForRadiator(string componentName,
        SpacecraftRadiatorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        double area_m2 = design.PanelArea_m2;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              area_m2 * 6.0,
            CapitalCost_USD:      area_m2 * 50_000.0,
            EmbodiedCO2_kgCO2eq:  area_m2 * 60.0);
    }

    /// <summary>
    /// Hydro turbine cost. Large hydro is the cheapest renewable per
    /// kWh but capital-intensive: ~ $2500 / kW (civil works dominate),
    /// 6 kgCO₂/kW (steel + concrete), 25 kg/kW.
    /// </summary>
    public static CostEstimate ForHydroTurbine(string componentName,
        HydroTurbineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = HydroTurbineSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.ShaftPower_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 25.0,
            CapitalCost_USD:      ratedPower_kW * 2500.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 6.0);
    }

    /// <summary>
    /// Solar-thermal collector cost. Flat-plate cluster: ~ $200/kWt,
    /// 25 kgCO₂/kWt, 30 kg/kWt. Parabolic-trough runs ~ 2× that.
    /// </summary>
    public static CostEstimate ForSolarThermal(string componentName,
        SolarCollectorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = SolarCollectorSolver.Solve(design);
        double ratedPower_kWt = Math.Max(0.0, r.UsefulHeatPower_W) / 1000.0;

        double costPerKWt = design.Kind switch
        {
            SolarCollectorKind.FlatPlate         => 200.0,
            SolarCollectorKind.ParabolicTrough   => 400.0,
            _ => throw new ArgumentOutOfRangeException(nameof(design),
                $"No cost data for solar collector kind '{design.Kind}'."),
        };

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kWt * 30.0,
            CapitalCost_USD:      ratedPower_kWt * costPerKWt,
            EmbodiedCO2_kgCO2eq:  ratedPower_kWt * 25.0);
    }

    // ── Sprint EC.W5 ────────────────────────────────────────────────────
    // Stirling, TEG, Tank, HeatPipe, Antenna, Reactor, Refrigeration.

    /// <summary>
    /// Stirling-engine cost. Cluster: $1500/kW shaft, 80 kgCO₂/kW,
    /// 25 kg/kW (heavy due to displacer + regenerator + heater head).
    /// </summary>
    public static CostEstimate ForStirling(string componentName,
        StirlingDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = StirlingSolver.Solve(design);
        double ratedPower_kW = Math.Max(0.0, r.IndicatedPower_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_kW * 25.0,
            CapitalCost_USD:      ratedPower_kW * 1500.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_kW * 80.0);
    }

    /// <summary>
    /// Terrestrial thermoelectric-generator cost (NOT radioisotope —
    /// Pu-238 RTGs run > $50k/W). TEG modules: $300/W electric,
    /// 35 kgCO₂/W, 0.4 kg/W. Heavy-metal Bi₂Te₃ / PbTe pellets.
    /// </summary>
    public static CostEstimate ForThermoelectric(string componentName,
        ThermoelectricGeneratorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = ThermoelectricGeneratorSolver.Solve(design);
        double ratedPower_W = Math.Max(0.0, r.ElectricPowerOutput_W);

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              ratedPower_W * 0.4,
            CapitalCost_USD:      ratedPower_W * 300.0,
            EmbodiedCO2_kgCO2eq:  ratedPower_W * 35.0);
    }

    /// <summary>
    /// Pressure-vessel cost. Shell mass drives cost: $20 / kg-steel
    /// (CrMoV or 4130 / 4340), 2 kgCO₂/kg, 1 kg/kg (identity).
    /// </summary>
    public static CostEstimate ForPressureVessel(string componentName,
        PressureVesselDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = PressureVesselSolver.Solve(design);
        double shellMass_kg = Math.Max(0.0, r.ShellMass_kg);

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              shellMass_kg,
            CapitalCost_USD:      shellMass_kg * 20.0,
            EmbodiedCO2_kgCO2eq:  shellMass_kg * 2.0);
    }

    /// <summary>
    /// Heat-pipe cost. Driven by rated heat throughput: $50/W,
    /// 0.5 kgCO₂/W, 0.05 kg/W (very compact + light).
    /// </summary>
    public static CostEstimate ForHeatPipe(string componentName,
        HeatPipeDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        double Q_W = Math.Max(0.0, design.HeatThroughput_W);

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              Q_W * 0.05,
            CapitalCost_USD:      Q_W * 50.0,
            EmbodiedCO2_kgCO2eq:  Q_W * 0.5);
    }

    /// <summary>
    /// Communications-antenna cost. Driven by dish aperture area:
    /// $10 000 / m², 50 kgCO₂/m², 8 kg/m². Wave-1 simplification —
    /// in reality electronics + RF chain dominate small-aperture
    /// systems. For non-dish (omni) antennas with TransmitDishDiameter
    /// = 0, returns a $500 flat scaffold cost.
    /// </summary>
    public static CostEstimate ForAntenna(string componentName,
        AntennaLinkDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        double dishDiameter_m = design.TransmitDishDiameter_m;
        if (dishDiameter_m <= 0)
        {
            // Omnidirectional / monopole — flat scaffold cost.
            return new CostEstimate(
                ComponentName:        componentName,
                Mass_kg:              1.0,
                CapitalCost_USD:      500.0,
                EmbodiedCO2_kgCO2eq:  5.0);
        }
        double area_m2 = Math.PI * dishDiameter_m * dishDiameter_m * 0.25;
        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              area_m2 * 8.0,
            CapitalCost_USD:      area_m2 * 10_000.0,
            EmbodiedCO2_kgCO2eq:  area_m2 * 50.0);
    }

    /// <summary>
    /// Chemical-reactor cost (CSTR / PFR). Cost driven by reactor
    /// volume: $5000 / m³ (vessel + stirrer + jacket), 200 kgCO₂/m³,
    /// 100 kg/m³ (stainless-steel wall + insulation).
    /// </summary>
    public static CostEstimate ForChemicalReactor(string componentName,
        ReactorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        double V_m3 = design.ReactorVolume_m3;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              V_m3 * 100.0,
            CapitalCost_USD:      V_m3 * 5000.0,
            EmbodiedCO2_kgCO2eq:  V_m3 * 200.0);
    }

    /// <summary>
    /// Vapor-compression refrigeration unit cost. Industrial cluster:
    /// $400/kW-cooling, 30 kgCO₂/kW, 20 kg/kW.
    /// </summary>
    public static CostEstimate ForRefrigeration(string componentName,
        RefrigerationDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = RefrigerationSolver.Solve(design);
        double cooling_kW = Math.Max(0.0, r.ColdSideHeatRemoval_W) / 1000.0;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              cooling_kW * 20.0,
            CapitalCost_USD:      cooling_kW * 400.0,
            EmbodiedCO2_kgCO2eq:  cooling_kW * 30.0);
    }

    // ── Sprint EC.W6 ────────────────────────────────────────────────────
    // Aerostructures + HybridRocket.

    /// <summary>
    /// Wing-spar cost. Driven by spar mass: per-material $/kg from
    /// aerospace material registries.
    ///   Al-7075:   $30/kg, 12 kgCO₂/kg  (extrusion + machining)
    ///   Steel-4340:$8/kg,  2.5 kgCO₂/kg
    ///   CFRP:      $80/kg, 25 kgCO₂/kg  (autoclave-cured aerospace
    ///                                    pre-preg + finishing)
    ///   Ti-6Al-4V: $90/kg, 35 kgCO₂/kg
    /// </summary>
    public static CostEstimate ForWingSpar(string componentName,
        WingSparDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = WingSparSolver.Solve(design);
        double mass_kg = Math.Max(0.0, r.SparMass_kg);

        var (costPerKg, co2PerKg) = design.Material switch
        {
            SparMaterial.Aluminum7075         => (30.0, 12.0),
            SparMaterial.Steel4340            => ( 8.0,  2.5),
            SparMaterial.CarbonFibreComposite => (80.0, 25.0),
            _ => throw new ArgumentOutOfRangeException(nameof(design),
                $"No cost data for spar material '{design.Material}'."),
        };
        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              mass_kg,
            CapitalCost_USD:      mass_kg * costPerKg,
            EmbodiedCO2_kgCO2eq:  mass_kg * co2PerKg);
    }

    /// <summary>
    /// Hybrid-rocket motor cost. Driven by vacuum thrust:
    /// ~ $50/N hardware capex (LPBF chamber + injector + ablative liner
    /// + tankage allowance), 5 kgCO₂/N (mostly the ablative + propellant
    /// load), 0.012 kg-hardware/N (chamber + injector + nozzle dry mass
    /// fraction; HTPB grain mass tracked separately by the propellant
    /// load model).
    /// </summary>
    public static CostEstimate ForHybridRocket(string componentName,
        HybridRocketDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var r = HybridRocketCycleSolver.SolveInitial(design);
        double thrust_N = Math.Max(0.0, r.VacuumThrust_N);

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              thrust_N * 0.012,
            CapitalCost_USD:      thrust_N * 50.0,
            EmbodiedCO2_kgCO2eq:  thrust_N * 5.0);
    }

    // ── Sprint EC.W7 ────────────────────────────────────────────────────
    // Regen-cooled rocket engine. Composed of three cost components:
    //   1. LPBF print cost — read directly from ManufacturingReport
    //      (Voxelforge.Manufacturing.ManufacturingAnalysis already
    //      computes EstimatedBuildCost_USD as machine-hour × rate).
    //   2. Injector + feed-system overhead — flat $/kg of chamber mass
    //      to approximate the non-printed hardware.
    //   3. Embodied CO₂ — Inconel-718 / CuCrZr cradle-to-gate, 18-25
    //      kgCO₂/kg depending on alloy.

    /// <summary>
    /// Regen-cooled rocket engine cost. Aggregates LPBF print cost
    /// from <see cref="Manufacturing.ManufacturingReport.EstimatedBuildCost_USD"/>
    /// + a 30 % overhead for injector / feed / valves, with embodied
    /// CO₂ from <see cref="Voxelforge.Geometry.ChamberGeometryResult.TotalMass_g"/>
    /// at a per-alloy rate.
    /// </summary>
    /// <param name="componentName">Network-unique name.</param>
    /// <param name="result">A successful regen-chamber generation
    /// result, e.g. from
    /// <c>RegenChamberOptimization.GenerateWith(cond, design)</c>.</param>
    public static CostEstimate ForRegenRocketEngine(string componentName,
        Voxelforge.Optimization.RegenGenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        double mass_kg = result.Geometry.TotalMass_g / 1000.0;
        double printCost_USD = result.Manufacturing.EstimatedBuildCost_USD;
        // 30 % overhead for non-printed hardware (igniter, valves,
        // feed-side fittings, harnesses, instrumentation bosses).
        const double NonPrintedOverheadFactor = 0.30;
        double capex_USD = printCost_USD * (1.0 + NonPrintedOverheadFactor);

        // Embodied CO₂ — superalloy-dominated:
        //   Inconel-718 / Inco-625: ~ 22 kgCO₂/kg (powder atomization +
        //     mining + LPBF energy)
        //   GRCop-42 / GRCop-84 / CuCrZr: ~ 18 kgCO₂/kg
        // Use 22 kgCO₂/kg as a cluster mid-band; refine per alloy on
        // demand. Cluster source: Worldsteel + IAI 2024 reports.
        const double Co2PerKg_LpbfSuperalloy = 22.0;
        double co2_kg = mass_kg * Co2PerKg_LpbfSuperalloy;

        return new CostEstimate(
            ComponentName:        componentName,
            Mass_kg:              mass_kg,
            CapitalCost_USD:      capex_USD,
            EmbodiedCO2_kgCO2eq:  co2_kg);
    }
}
