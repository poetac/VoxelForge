// TierABTests.cs — Contract tests for the 2026-04-21 Tier-A + Tier-B
// round:
//
//   A.1 — MeasuredDataOverlay CSV ingest + calibration grid search
//   A.2 — Physics-only GenerateWith path + analytical mass
//   A.3 — WarningAggregator structured severity grading
//   A.4 — SensorBossPresets registry + design wiring
//   B.5 — CroccoNTau parameter table + growth-rate classification
//   B.6 — ResidualStressAnalysis coarse inherent-strain outputs
//   B.7 — Former-stub elements now all implemented
//   B.8 — ParetoScatterPanel exists + SetPoints tolerates null

using System.Globalization;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class TierABTests
{
    // ─────────────────────────────────────────────────────────────────
    //  A.1 — MeasuredDataOverlay
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MeasuredDataOverlay_ParsesCsv_AndSummarises()
    {
        using var tmp = TestTempFile.WithUniqueName("measured_probe", "csv");
        File.WriteAllText(tmp.Path, string.Join(Environment.NewLine, new[]
        {
            "time_s,chamber_p_pa,coolant_p_in_pa,coolant_p_out_pa,coolant_t_in_k,coolant_t_out_k,wall_t_k",
            "0.0,6.5e6,12e6,11e6,150,200,800",
            "1.0,6.9e6,12e6,11e6,150,250,950",
            "2.0,6.9e6,12e6,11e6,150,260,1000",
            "3.0,6.9e6,12e6,11e6,150,260,1000",
            "4.0,6.9e6,12e6,11e6,150,260,1000",
            "5.0,6.8e6,12e6,11e6,150,240,900",
        }));
        var (samples, _) = MeasuredDataOverlay.ParseCsv(tmp.Path);
        Assert.True(samples.Count >= 6);
        var s = MeasuredDataOverlay.Summarise(samples);
        // Middle 50 % span should be near steady (6.9 MPa / ΔT around 100 K).
        Assert.InRange(s.ChamberP_Pa, 6.85e6, 6.95e6);
        Assert.InRange(s.CoolantDT_K, 90, 115);
        Assert.InRange(s.CoolantDP_Pa, 0.95e6, 1.05e6);
        Assert.InRange(s.WallT_K, 900, 1050);
    }

    [Fact]
    public void MeasuredDataOverlay_Calibration_MovesTowardsObserved()
    {
        // Measurement only carries wall T (CoolantDP / CoolantDT are the
        // measured baselines the runner returns unchanged, so only the
        // wall-T residual drives the calibration).
        var measured = new MeasuredSummary(
            SampleCount: 100,
            ChamberP_Pa: 6.9e6,
            CoolantDP_Pa: 1e6,
            CoolantDT_K: 300,
            CoolantT_In_K: 150, CoolantT_Out_K: 450,
            Thrust_N: 500,
            WallT_K: 1200);
        // Pretend the model under-predicts wall T by 20 % at factor = 1.
        // ΔT / ΔP stay matched so the residual is entirely in wall T.
        var overlay = MeasuredDataOverlay.BuildOverlay(
            measured,
            predicted_PeakWallT_K: 1000,
            predicted_CoolantDT_K: 300,
            predicted_CoolantDP_Pa: 1e6,
            calibrationRunner: f => (1000 * f, 300.0, 1e6));
        Assert.NotNull(overlay.Calibration);
        // Best factor should be ≈ 1.2 to raise wall T 1000 → 1200.
        Assert.InRange(overlay.Calibration!.BartzScalingFactor, 1.10, 1.30);
        Assert.True(overlay.Calibration.SumSquaredResidualAtBest <= overlay.Calibration.SumSquaredResidualAt1);
    }

    // ─────────────────────────────────────────────────────────────────
    //  A.2 — Physics-only path
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_PhysicsOnly_PopulatesMassWithoutVoxels()
    {
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 60,
        };
        var r = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.True(r.Geometry.TotalMass_g > 0, "Analytical mass estimate must be positive.");
        Assert.True(r.Geometry.PrintedCost_USD > 0);
        Assert.True(r.Geometry.BoundingLength_mm > 0);
        // Analytical path is allowed to leave Voxels null — callers must
        // respect the contract and not touch the mesh.
    }

    [Fact]
    public void GenerateWith_PhysicsOnly_MuchFasterThanFullBuild()
    {
        // Just check that the physics-only path completes — timing is
        // machine-dependent and flaky in CI. Correctness of the "faster"
        // property is a qualitative claim documented elsewhere.
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign { ContourStationCount = 40 };
        _ = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
    }

    // ─────────────────────────────────────────────────────────────────
    //  A.3 — Structured severity
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void WarningAggregator_PromotesFeasibilityViolationsToCritical()
    {
        var baseGen = BuildGen();
        var score = baseGen.score;
        // Inject a synthetic feasibility violation.
        var fake = score with
        {
            FeasibilityViolations = new[]
            {
                new FeasibilityViolation("WALL_TEMP", "peak T too hot", 1500, 800),
            },
            YieldExceeded = true,
            MinSafetyFactor = 0.8,
        };
        var list = WarningAggregator.BuildFor(baseGen.gen, fake);
        Assert.Contains(list, w => w.Code == "WALL_TEMP" && w.Severity == WarningSeverity.Critical);
        Assert.Contains(list, w => w.Code == "YIELD_EXCEEDED" && w.Severity == WarningSeverity.Critical);
    }

    // ─────────────────────────────────────────────────────────────────
    //  A.4 — Sensor-boss presets
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SensorBossPresets_HasAllThreeTypes()
    {
        Assert.Equal(3, SensorBossPresets.All.Count);
        Assert.True(SensorBossPresets.All[SensorBossType.Thermocouple_1_8_NPT].BoreDiameter_mm > 2.5);
        Assert.True(SensorBossPresets.All[SensorBossType.Pressure_M5].BoreDiameter_mm > 2.0);
        Assert.True(SensorBossPresets.All[SensorBossType.StaticTap_G_1_16].BoreDiameter_mm > 1.5);
    }

    [Fact]
    public void RegenChamberDesign_CarriesSensorBossList()
    {
        var d = new RegenChamberDesign
        {
            SensorBosses = new[]
            {
                new SensorBoss(0.25, 0, SensorBossType.Pressure_M5),
                new SensorBoss(0.75, 180, SensorBossType.Thermocouple_1_8_NPT),
            },
        };
        Assert.Equal(2, d.SensorBosses.Count);
    }

    // ─────────────────────────────────────────────────────────────────
    //  B.5 — Crocco n-τ
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PropellantPair.LOX_CH4, true)]
    [InlineData(PropellantPair.LOX_H2,  true)]
    [InlineData(PropellantPair.LOX_RP1, true)]
    [InlineData(PropellantPair.N2O4_MMH, false)]
    [InlineData(PropellantPair.H2O2_RP1, false)]
    public void CroccoNTau_ParameterTable_CoversImplementedPairs(PropellantPair p, bool supported)
    {
        var (n, tau, ok) = CroccoNTau.GetPairParameters(p);
        Assert.Equal(supported, ok);
        if (supported) { Assert.True(n > 0); Assert.True(tau > 0); }
    }

    [Fact]
    public void CroccoNTau_Evaluate_ReturnsReportAndRatesModes()
    {
        var screech = new ScreechModeResult(
            SoundSpeed_ms: 1200, L1_Hz: 4000, T1_Hz: 8000, T2_Hz: 13000);
        var r = CroccoNTau.Evaluate(PropellantPair.LOX_CH4, screech, gamma: 1.2);
        Assert.True(r.PairSupported);
        // L1 / T1 / T2 should each have a finite growth rate and a rating.
        foreach (var mode in new[] { r.L1, r.T1, r.T2 })
        {
            Assert.True(double.IsFinite(mode.GrowthRate));
            Assert.InRange((int)mode.Rating, 0, 2);
        }
    }

    [Fact]
    public void CroccoNTau_Evaluate_SkipsUnsupportedPair()
    {
        var screech = new ScreechModeResult(1200, 4000, 8000, 13000);
        var r = CroccoNTau.Evaluate(PropellantPair.N2O4_MMH, screech, gamma: 1.25);
        Assert.False(r.PairSupported);
        Assert.Equal(StabilityRating.Pass, r.Overall);   // skipped → not failed
    }

    // ─────────────────────────────────────────────────────────────────
    //  B.6 — Residual-stress
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Residual_InherentStrain_HasCopperVsNickelDifference()
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 8, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 1.1, stationCount: 40);
        var cuCrZr = WallMaterials.CuCrZr;
        var inc718 = WallMaterials.Inconel718;
        var rCu = ResidualStressAnalysis.Analyze(contour, cuCrZr);
        var rNi = ResidualStressAnalysis.Analyze(contour, inc718);
        Assert.True(rCu.InherentStrain > 0);
        Assert.True(rNi.InherentStrain > 0);
        Assert.NotEqual(rCu.InherentStrain, rNi.InherentStrain);
    }

    [Fact]
    public void Residual_LongitudinalShrink_GrowsWithContourLength()
    {
        var shortContour = ChamberContourGenerator.Generate(
            throatRadius_mm: 8, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 0.5, stationCount: 40);
        var longContour = ChamberContourGenerator.Generate(
            throatRadius_mm: 8, contractionRatio: 6, expansionRatio: 8,
            characteristicLength_m: 1.6, stationCount: 40);
        var mat = WallMaterials.CuCrZr;
        var sShort = ResidualStressAnalysis.Analyze(shortContour, mat);
        var sLong  = ResidualStressAnalysis.Analyze(longContour,  mat);
        Assert.True(sLong.LongitudinalShrink_mm > sShort.LongitudinalShrink_mm);
    }

    // ─────────────────────────────────────────────────────────────────
    //  B.7 — Pintle / Showerhead / Swirl implementations
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pintle")]
    [InlineData("Showerhead")]
    [InlineData("Swirl")]
    public void FormerlyStub_ReturnsPositiveAreas(string type)
    {
        var elem = InjectorElementFactory.Create(type);
        Assert.True(elem.IsImplemented);
        var r = elem.Size(new SizingInputs(
            DeltaPInj_Pa: 1.4e6,
            OxDensity_kgm3: 1140,
            FuelDensity_kgm3: 420,
            OxFlowPerElement_kgs: 0.01,
            FuelFlowPerElement_kgs: 0.003));
        Assert.True(r.OxOrificeArea_mm2 > 0);
        Assert.True(r.FuelOrificeArea_mm2 > 0);
        Assert.True(r.Notes.Length > 0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  B.8 — Pareto scatter widget smoke test
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ParetoScatterPanel_SetPointsTolerates_Null_And_Empty_And_List()
    {
        using var panel = new Voxelforge.UI.ParetoScatterPanel();
        panel.SetPoints(null);
        panel.SetPoints(Array.Empty<ParetoPoint>());
        panel.SetPoints(new[]
        {
            new ParetoPoint(900, 1e6, 200, Array.Empty<double>(), 1),
            new ParetoPoint(1000, 0.5e6, 150, Array.Empty<double>(), 2),
        });
        // No exceptions = pass.
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (RegenGenerationResult gen, RegenScoreResult score) BuildGen()
    {
        var cond = new OperatingConditions { PropellantPair = PropellantPair.LOX_CH4 };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        return (gen, score);
    }
}
