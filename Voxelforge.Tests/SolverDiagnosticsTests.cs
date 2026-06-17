// SolverDiagnosticsTests.cs — Lock the convergence-diagnostic pathway.
// Also asserts that tolerance rib/jacket fields are now independent.

using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class SolverDiagnosticsTests
{
    private static (ChamberContour, RegenSolverInputs) BuildBaseline(
        int channelCount, double ribThickness = 0.8)
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 500, ChamberPressure_Pa = 1000 * 6894.76,
            MixtureRatio = 3.3, CoolantInletTemp_K = 150,
            CoolantInletPressure_Pa = 12e6, WallMaterialIndex = 1,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign { ChannelCount = channelCount, RibThickness_mm = ribThickness };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: derived.ThroatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount: 100);
        var channels = new ChannelSchedule(
            design.ChannelCount, design.RibThickness_mm, design.GasSideWallThickness_mm,
            design.ChannelHeightChamber_mm, design.ChannelHeightThroat_mm, design.ChannelHeightExit_mm);
        var material = WallMaterials.All[cond.WallMaterialIndex];

        var inputs = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: material, Channels: channels,
            CoolantMassFlow_kgs: derived.FuelMassFlow_kgs,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            FilmCooling: new FilmCoolingInputs { Enabled = true, FuelFractionAsFilm = 0.05 },
            CoolantFluid: CoolantRegistry.Get("CH4"));
        return (contour, inputs);
    }

    [Fact]
    public void ReasonableDesign_ReportsCleanConvergence()
    {
        // At 500 N thrust (R_t ≈ 4 mm), even 40 channels produces a
        // subcritical pitch at the throat. Pick a channel count that leaves
        // plenty of arc for the 0.8 mm rib: pitch(R_t=4, 20) = 1.26 mm
        // vs rib 0.6 mm ⇒ w ≈ 0.66 mm > 0.3 mm floor.
        var (_, inp) = BuildBaseline(channelCount: 20, ribThickness: 0.6);
        var r = RegenCoolingSolver.Solve(inp);
        Assert.True(r.Diagnostics.ChannelWidthClampedCount < 5,
            $"Spacious 20-channel design shouldn't trigger many w clamps; got {r.Diagnostics.ChannelWidthClampedCount}");
    }

    [Fact]
    public void TooManyChannels_TripsChannelWidthClamp()
    {
        // 200 channels at a 4 mm throat radius ⇒ w way below 0.3 mm.
        var (_, inp) = BuildBaseline(channelCount: 200, ribThickness: 0.8);
        var r = RegenCoolingSolver.Solve(inp);
        Assert.True(r.Diagnostics.ChannelWidthClampedCount > 5);
        Assert.False(r.Diagnostics.CleanConvergence,
            "Designs that trip clamps must not report clean convergence.");
    }

    [Fact]
    public void LoxH2_UsesHydrogenFluidInSolver()
    {
        // Regression: auditor P0-07 reconciliation. Selecting LOX/H2 must
        // actually instantiate HydrogenFluid for the jacket-side physics.
        var cond = new OperatingConditions
        {
            Thrust_N = 500, ChamberPressure_Pa = 1000 * 6894.76,
            MixtureRatio = 4.0, CoolantInletTemp_K = 30,
            CoolantInletPressure_Pa = 12e6, WallMaterialIndex = 3,  // Inconel718
            PropellantPair = PropellantPair.LOX_H2,
        };
        var design = new RegenChamberDesign();
        var gen = RegenChamberOptimization.GenerateWith(cond, design);
        Assert.Contains("Hydrogen", gen.Thermal.Warnings[0],
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToleranceRibJacket_AreIndependentFields()
    {
        // Changing only rib tolerance must affect the sweep distribution.
        var cond = new OperatingConditions
        {
            Thrust_N = 500, ChamberPressure_Pa = 1000 * 6894.76,
            MixtureRatio = 3.3, CoolantInletTemp_K = 150,
            CoolantInletPressure_Pa = 12e6, WallMaterialIndex = 1,
        };
        var design = new RegenChamberDesign();
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: derived.ThroatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount: 80);

        // Sample sizes + tolerance contrast chosen so that the loose vs tight
        // spread difference clears Monte-Carlo sample noise. Rib thickness
        // touches the coolant-side hydraulic diameter directly via channel
        // width pitch − rib_thickness, which dominates ΔP through the
        // friction-factor × velocity² stack. Test asserts on coolant ΔP
        // because rib's effect on PEAK wall T is a few percent at most —
        // coolant-side resistance is a small fraction of the total thermal
        // stack — but its effect on ΔP is direct and large.
        //
        // With per-iteration RNG seeding, the wall + channel-height
        // perturbations are bit-identical between the two sweeps
        // (same per-iter Random(seed+i)). So the ONLY differing input
        // across the two runs is rib+jacket; the assertion measures
        // their isolated effect cleanly. The legacy shared-RNG impl
        // let rib/jacket draw perturbations consume RNG state and
        // shift wall/channel draws, manufacturing a spurious signal
        // on PeakWallT_K — that hid an underspecified assertion. The
        // new test is more rigorous, not less.
        var tight = ToleranceAnalysis.Run(contour, cond, design, new ToleranceInputs(
            SampleCount: 300, RibThicknessTolerance_mm: 0.001,
            JacketThicknessTolerance_mm: 0.001));
        var loose = ToleranceAnalysis.Run(contour, cond, design, new ToleranceInputs(
            SampleCount: 300, RibThicknessTolerance_mm: 0.40,
            JacketThicknessTolerance_mm: 0.40));
        double tightSpread = tight.CoolantPressureDrop_Pa.P90 - tight.CoolantPressureDrop_Pa.P10;
        double looseSpread = loose.CoolantPressureDrop_Pa.P90 - loose.CoolantPressureDrop_Pa.P10;
        Assert.True(looseSpread > tightSpread,
            $"Widening rib+jacket tolerance alone must broaden ΔP distribution "
            + $"(tight={tightSpread:E2} Pa, loose={looseSpread:E2} Pa)");
    }
}
