// ToleranceAnalysisTests.cs — Ensure the Monte-Carlo sweep converges to
// the nominal at zero-tolerance, and that wider tolerance bands produce
// wider output distributions.

using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class ToleranceAnalysisTests
{
    private static (ChamberContour Contour, OperatingConditions Cond, RegenChamberDesign Design) MakeBaseline()
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 500, ChamberPressure_Pa = 1000 * 6894.76,
            MixtureRatio = 3.3, CoolantInletTemp_K = 150,
            CoolantInletPressure_Pa = 12e6, WallMaterialIndex = 1,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            FilmCooling = new HeatTransfer.FilmCoolingInputs
            {
                Enabled = true, FuelFractionAsFilm = 0.05, FilmSlotHeight_mm = 0.6,
            },
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: derived.ThroatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount: 90);
        return (contour, cond, design);
    }

    [Fact]
    public void ZeroTolerance_GivesTightDistribution()
    {
        var (contour, cond, design) = MakeBaseline();
        var r = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 80,
                WallThicknessTolerance_mm: 0,
                ChannelHeightTolerance_mm: 0,
                RibThicknessTolerance_mm: 0,
                JacketThicknessTolerance_mm: 0));
        // All samples should be nearly identical → p10 ≈ p90.
        double spread = r.PeakWallT_K.P90 - r.PeakWallT_K.P10;
        Assert.True(spread < 5, $"Zero-tol sweep should have near-zero spread (got {spread:F1} K)");
    }

    [Fact]
    public void WiderTolerance_GivesWiderDistribution()
    {
        var (contour, cond, design) = MakeBaseline();
        var tight = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 150, WallThicknessTolerance_mm: 0.02,
                ChannelHeightTolerance_mm: 0.02));
        var wide = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 150, WallThicknessTolerance_mm: 0.20,
                ChannelHeightTolerance_mm: 0.20));
        double tightSpread = tight.PeakWallT_K.P90 - tight.PeakWallT_K.P10;
        double wideSpread  = wide.PeakWallT_K.P90 - wide.PeakWallT_K.P10;
        Assert.True(wideSpread > 2 * tightSpread,
            $"Wider tolerance should broaden peak-T distribution (tight={tightSpread:F0}, wide={wideSpread:F0})");
    }

    [Fact]
    public void Quantiles_AreOrdered()
    {
        var (contour, cond, design) = MakeBaseline();
        var r = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 100));
        Assert.True(r.PeakWallT_K.P10 <= r.PeakWallT_K.P50);
        Assert.True(r.PeakWallT_K.P50 <= r.PeakWallT_K.P90);
        Assert.True(r.PeakWallT_K.P90 <= r.PeakWallT_K.P99);
        Assert.True(r.MinSafetyFactor.P10 <= r.MinSafetyFactor.P50);
    }

    [Fact]
    public void Deterministic_GivenSameSeed()
    {
        var (contour, cond, design) = MakeBaseline();
        var inp = new ToleranceInputs(SampleCount: 50, RandomSeed: 42);
        var r1 = ToleranceAnalysis.Run(contour, cond, design, inp);
        var r2 = ToleranceAnalysis.Run(contour, cond, design, inp);
        Assert.Equal(r1.PeakWallT_K.P50, r2.PeakWallT_K.P50, precision: 3);
        Assert.Equal(r1.MinSafetyFactor.P10, r2.MinSafetyFactor.P10, precision: 3);
    }

    [Fact]
    public void ComputeTime_IsSaneForInteractiveUse()
    {
        var (contour, cond, design) = MakeBaseline();
        var r = ToleranceAnalysis.Run(contour, cond, design,
            new ToleranceInputs(SampleCount: 50));
        Assert.True(r.MeanComputeTime_ms < 200,
            $"Mean sample time {r.MeanComputeTime_ms:F1} ms too high for interactive use");
    }
}
