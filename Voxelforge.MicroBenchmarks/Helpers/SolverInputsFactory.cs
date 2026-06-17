// BB-3 (2026-04-29): builds RegenSolverInputs identical to the xUnit
// MakeSolverInputs helper in Phase4PerfBenchmarks.cs so BDN microbench
// readings stay numerically comparable to the Phase4 perf-ceiling tests.
//
// Returns the same 4-tuple shape (cond, design, contour, inputs) so
// any future extension that needs `cond`/`design` can grab them without
// re-running the seeding.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.MicroBenchmarks.Helpers;

internal static class SolverInputsFactory
{
    public static (OperatingConditions cond, RegenChamberDesign design,
                   ChamberContour contour, RegenSolverInputs inputs)
        MakeSolverInputs(int stationCount)
    {
        var cond = new OperatingConditions
        {
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = stationCount,
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           stationCount);
        var material = WallMaterials.All[
            Math.Clamp(cond.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        var pairMeta = PropellantPairs.GetMeta(cond.PropellantPair);
        var fluid = CoolantRegistry.Get(pairMeta.CoolantFluidKey);
        var channels = new ChannelSchedule(
            ChannelCount: design.ChannelCount,
            RibThickness_mm: design.RibThickness_mm,
            GasSideWallThickness_mm: design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm: design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm: design.ChannelHeightExit_mm);
        var inputs = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: material, Channels: channels,
            CoolantMassFlow_kgs: derived.FuelMassFlow_kgs,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            CoolantFluid: fluid);
        return (cond, design, contour, inputs);
    }
}
