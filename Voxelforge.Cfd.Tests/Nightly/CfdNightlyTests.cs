// CfdNightlyTests.cs — Nightly CFD validation suite (#656).
//
// Three representative LOX/CH4 cases at Coarse mesh density.
// Each test calls CfdCalibrationRunner.RunCalibration(), asserts T_aw drift
// is within ±20% of Bartz, and emits a CfdDriftReport Markdown block to
// the xUnit output for artifact capture.
//
// Skip-on-missing-SU2 contract (ADR-026 §3): when Su2Locator.FindSu2Cfd()
// returns null the test returns without asserting (passes vacuously). No
// SU2_RUN env var is required for CI — only the nightly workflow sets it.
//
// Run selectively: dotnet test --filter "Category=CfdNightly"

using System.Diagnostics;
using Voxelforge.Cfd;
using Voxelforge.Cfd.Config;
using Voxelforge.Cfd.Mesh;
using Voxelforge.Cfd.Report;
using Voxelforge.Cfd.Su2;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;
using Xunit;
using Xunit.Abstractions;

namespace Voxelforge.Cfd.Tests.Nightly;

[Trait("Category", "CfdNightly")]
public sealed class CfdNightlyTests(ITestOutputHelper output)
{
    // ── Case 1: LOX/CH4 small (10 kN class, throat 20 mm) ────────────────

    [Fact]
    public void Case1_LoxCh4_Small_DriftWithin20Pct()
    {
        if (Su2Locator.FindSu2Cfd() is null)
        {
            output.WriteLine("SKIP: SU2_CFD not found — nightly CFD verification requires SU2.");
            return;
        }

        RunAndAssert(
            caseLabel:          "LOX/CH4 small (10 kN class)",
            throatRadius_mm:    20,
            contractionRatio:   4.0,
            expansionRatio:     8.0,
            chamberPressure_Pa: 5_000_000,
            pair:               PropellantPair.LOX_CH4,
            mixtureRatio:       3.5);
    }

    // ── Case 2: LOX/CH4 medium (50 kN class, throat 30 mm) ───────────────

    [Fact]
    public void Case2_LoxCh4_Medium_DriftWithin20Pct()
    {
        if (Su2Locator.FindSu2Cfd() is null)
        {
            output.WriteLine("SKIP: SU2_CFD not found — nightly CFD verification requires SU2.");
            return;
        }

        RunAndAssert(
            caseLabel:          "LOX/CH4 medium (50 kN class)",
            throatRadius_mm:    30,
            contractionRatio:   4.0,
            expansionRatio:     12.0,
            chamberPressure_Pa: 5_000_000,
            pair:               PropellantPair.LOX_CH4,
            mixtureRatio:       3.5);
    }

    // ── Case 3: LOX/CH4 large (100 kN class, throat 40 mm) ───────────────

    [Fact]
    public void Case3_LoxCh4_Large_DriftWithin20Pct()
    {
        if (Su2Locator.FindSu2Cfd() is null)
        {
            output.WriteLine("SKIP: SU2_CFD not found — nightly CFD verification requires SU2.");
            return;
        }

        RunAndAssert(
            caseLabel:          "LOX/CH4 large (100 kN class)",
            throatRadius_mm:    40,
            contractionRatio:   4.0,
            expansionRatio:     15.0,
            chamberPressure_Pa: 5_000_000,
            pair:               PropellantPair.LOX_CH4,
            mixtureRatio:       3.5);
    }

    // ── Shared runner ─────────────────────────────────────────────────────

    private void RunAndAssert(
        string caseLabel,
        double throatRadius_mm,
        double contractionRatio,
        double expansionRatio,
        double chamberPressure_Pa,
        PropellantPair pair,
        double mixtureRatio)
    {
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:         throatRadius_mm,
            contractionRatio:        contractionRatio,
            expansionRatio:          expansionRatio,
            characteristicLength_m:  1.0);

        var gas = PropellantTables.Lookup(pair, mixtureRatio, chamberPressure_Pa);

        var channels = new ChannelSchedule(
            ChannelCount:              40,
            RibThickness_mm:           0.5,
            GasSideWallThickness_mm:   0.8,
            ChannelHeightAtChamber_mm: 2.5,
            ChannelHeightAtThroat_mm:  1.2,
            ChannelHeightAtExit_mm:    2.0);

        var solverInputs = new RegenSolverInputs(
            Contour:                 contour,
            Gas:                     gas,
            Wall:                    WallMaterials.GRCop42,
            Channels:                channels,
            CoolantMassFlow_kgs:     0.8,
            CoolantInletTemp_K:      110,
            CoolantInletPressure_Pa: 6_000_000);

        // Give the runner an explicit work directory so we can clean it up.
        // Without this, CfdCalibrationRunner mints a fresh %TEMP%/vxf_cfd_<guid>
        // folder per call and never deletes it, so nightly runs accumulate SU2
        // mesh + solution trees on the self-hosted runner disk (#853).
        string workDir = Path.Combine(
            Path.GetTempPath(), $"vxf_cfd_nightly_{Guid.NewGuid():N}");

        var inputs = new CfdCalibrationInputs(
            Contour:            contour,
            Gas:                gas,
            SolverInputs:       solverInputs,
            ChamberPressure_Pa: chamberPressure_Pa,
            Density:            Su2MeshDensity.Coarse,
            Pair:               pair,
            WorkDirectory:      workDir);

        bool succeeded = false;
        try
        {
            // Bartz prediction before CFD (same inputs, no SU2 involvement).
            double bartzPeak = RegenCoolingSolver.Solve(solverInputs).PeakAdiabaticWallTemp_K;

            var sw = Stopwatch.StartNew();
            var result = CfdCalibrationRunner.RunCalibration(inputs,
                inp => RegenCoolingSolver.Solve(inp));
            sw.Stop();

            double su2Peak  = result.WallProfile.PeakAdiabaticWallTemp_K;
            double driftPct = bartzPeak > 0
                ? 100.0 * Math.Abs(su2Peak - bartzPeak) / bartzPeak
                : double.NaN;

            string report = CfdDriftReport.BuildMarkdown(
                wallProfile:                    result.WallProfile,
                calibration:                    result.CalibrationResult,
                bartzPeakAdiabaticWallTemp_K:   bartzPeak,
                cpModel:                        inputs.CpModel);

            output.WriteLine($"=== Nightly CFD: {caseLabel} ===");
            output.WriteLine($"Wall-clock: {sw.Elapsed.TotalSeconds:F1} s");
            output.WriteLine($"SU2 T_aw peak: {su2Peak:F0} K");
            output.WriteLine($"Bartz T_aw peak: {bartzPeak:F0} K");
            output.WriteLine($"Drift: {driftPct:F1}%");
            output.WriteLine("");
            output.WriteLine(report);

            Assert.True(result.WallProfile.NodeCount > 0,
                $"{caseLabel}: SU2 produced no wall nodes.");
            Assert.True(result.WallProfile.Converged,
                $"{caseLabel}: SU2 did not converge.");
            Assert.InRange(su2Peak, 1_000.0, 6_000.0);
            Assert.True(!double.IsNaN(driftPct) && driftPct <= 20.0,
                $"{caseLabel}: T_aw drift {driftPct:F1}% exceeds ±20% acceptance threshold.");

            succeeded = true;
        }
        finally
        {
            if (succeeded)
            {
                // Success: drop the transient SU2 work directory.
                try { Directory.Delete(workDir, recursive: true); }
                catch (Exception ex)
                {
                    output.WriteLine($"(cleanup) could not delete {workDir}: {ex.Message}");
                }
            }
            else
            {
                // Failure: KEEP the SU2 artifacts and surface the path for triage.
                output.WriteLine($"CFD work directory retained for triage: {workDir}");
            }
        }
    }
}
