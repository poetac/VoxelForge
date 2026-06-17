using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Structure;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class SafetyReportTests
{
    private static StructuralSummary StructAt(double minSF, bool yielded, double peakHoop_MPa = 100)
        => new(
            Stations: new StationStressResult[5],
            PeakHoop_MPa: peakHoop_MPa,
            PeakThermal_MPa: 0,
            PeakCombined_MPa: peakHoop_MPa,
            MinSafetyFactor: minSF,
            PeakStationIndex: 2,
            YieldExceeded: yielded);

    private static ProofTestResult Proof(
        double minSF, bool passes, double burstMargin, bool yielded = false,
        string[]? warnings = null, string designHash = "abc123")
        => new(
            ProofPressure_Pa: 10e6,
            ProofFactor: 1.5,
            ColdStructure: StructAt(minSF, yielded),
            ElasticBurstPressure_Pa: burstMargin * 6.7e6,
            BurstMarginFactor: burstMargin,
            Passes: passes,
            Warnings: warnings ?? Array.Empty<string>(),
            DesignHash: designHash);

    [Fact]
    public void GoodDesign_ReportsPASS_AndAllRequiredSections()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1],
            meop_Pa: 6.7e6,
            gasSideWallThickness_mm: 1.0);

        Assert.Contains("**Status:** PASS", md);
        Assert.Contains("# Hydrostatic Proof Test — Safety Report", md);
        Assert.Contains("## Test summary", md);
        Assert.Contains("## Pass / fail criteria", md);
        Assert.Contains("## Burst margin", md);
        Assert.Contains("## Wall + material data", md);
        Assert.Contains("### Yield strength vs temperature", md);
        Assert.Contains("## Operator notes", md);
        Assert.Contains("## Limitations", md);
    }

    [Fact]
    public void LowSafetyFactor_ReportsFAIL()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 0.7, passes: false, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.Contains("**Status:** FAIL", md);
        Assert.Contains("0.70", md);
    }

    [Fact]
    public void LowBurstMargin_ReportsFAIL_EvenWhenProofPasses()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.2, passes: true, burstMargin: 1.4),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.Contains("**Status:** FAIL", md);
        Assert.Contains("1.40×", md);
    }

    [Fact]
    public void YieldExceeded_ReportsFAIL_AndYESInTable()
    {
        var md = SafetyReport.BuildMarkdown(
            new ProofTestResult(
                ProofPressure_Pa: 10e6, ProofFactor: 1.5,
                ColdStructure: StructAt(minSF: 0.5, yielded: true),
                ElasticBurstPressure_Pa: 16e6, BurstMarginFactor: 2.4,
                Passes: false, Warnings: Array.Empty<string>(), DesignHash: "x"),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.Contains("**Status:** FAIL", md);
        Assert.Contains("| Yield exceeded | YES |", md);
    }

    [Fact]
    public void Warnings_AppearInDedicatedSection()
    {
        var warnings = new[] { "Burst margin too low.", "Wall thickness near LPBF minimum." };
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5, warnings: warnings),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.Contains("## Warnings", md);
        Assert.Contains("- Burst margin too low.", md);
        Assert.Contains("- Wall thickness near LPBF minimum.", md);
    }

    [Fact]
    public void NoWarnings_OmitsWarningSection()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.DoesNotContain("## Warnings", md);
    }

    [Fact]
    public void HotFireContext_AppearsOnlyWhenProvided()
    {
        var proof = Proof(minSF: 1.5, passes: true, burstMargin: 2.5);
        var withoutHot = SafetyReport.BuildMarkdown(
            proof, WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);
        var withHot = SafetyReport.BuildMarkdown(
            proof, WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0,
            hotFire: StructAt(minSF: 1.1, yielded: false, peakHoop_MPa: 250));

        Assert.DoesNotContain("Hot-fire structural margins", withoutHot);
        Assert.Contains("## Hot-fire structural margins (context only)", withHot);
        Assert.Contains("250 MPa", withHot);
    }

    [Fact]
    public void DesignHash_AppearsInHeader()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5, designHash: "deadbeef1234"),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.Contains("**Design hash:** `deadbeef1234`", md);
    }

    [Fact]
    public void EmptyDesignHash_OmitsHashLine()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5, designHash: ""),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.DoesNotContain("Design hash:", md);
    }

    [Fact]
    public void MaterialDataSourceAndCertStatus_AreIncluded()
    {
        var wall = WallMaterials.All[1];
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            wall, meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.Contains(wall.Name, md);
        Assert.Contains(wall.DataSource, md);
        Assert.Contains(wall.LPBFProcessNote, md);
        Assert.Contains(wall.CertificationStatus, md);
    }

    [Fact]
    public void YieldStrengthTable_HasMonotonicRows()
    {
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        foreach (var T in new[] { "293", "400", "500", "600", "700", "800", "900" })
            Assert.Contains($"| {T} |", md);
    }

    [Fact]
    public void SaveMarkdown_WritesExactBuildOutput()
    {
        var proof = Proof(minSF: 1.5, passes: true, burstMargin: 2.5);
        var wall = WallMaterials.All[1];
        string expected = SafetyReport.BuildMarkdown(
            proof, wall, meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        using var tmp = TestTempFile.WithUniqueName("safety-report-test", "md");
        SafetyReport.SaveMarkdown(tmp.Path, proof, wall, meop_Pa: 6.7e6,
            gasSideWallThickness_mm: 1.0);
        Assert.Equal(expected, File.ReadAllText(tmp.Path));
    }

    [Fact]
    public void SchemaVersionIsExposedAsConst()
    {
        Assert.False(string.IsNullOrWhiteSpace(SafetyReport.ReportSchemaVersion));
        Assert.StartsWith("v", SafetyReport.ReportSchemaVersion);
    }

    // ── Hot-fire Item 4 close-out (2026-04-28): Startup/Shutdown ──────

    [Fact]
    public void NoTransientResults_OmitsStartupShutdownSection()
    {
        // Default proof-only call: no chilldown, no startTransient, no
        // shutdownBlowdown. The Startup/Shutdown section must be absent.
        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0);

        Assert.DoesNotContain("## Startup / Shutdown sequence", md);
        Assert.DoesNotContain("Pre-fire chilldown", md);
        Assert.DoesNotContain("Main-stage start-up", md);
        Assert.DoesNotContain("Shutdown / blowdown", md);
    }

    [Fact]
    public void StartTransient_AddsMainStageStartupSubsection()
    {
        var startTr = new Voxelforge.Combustion.StartTransientResult(
            Samples:                   System.Array.Empty<Voxelforge.Combustion.StartTransientSample>(),
            TimeTo90Pc_s:              0.250,
            IgnitionTime_s:            0.080,
            UnburnedMassAtIgnition_kg: 0.025,
            PeakPressure_Pa:           8.4e6,
            PeakPressureOvershoot:     0.20,
            HardStartRisk:             false,
            ChamberFillTimeConstant_s: 0.010,
            Warnings:                  System.Array.Empty<string>());

        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0,
            startTransient: startTr);

        Assert.Contains("## Startup / Shutdown sequence", md);
        Assert.Contains("### Main-stage start-up", md);
        Assert.Contains("0.250 s", md);                      // time to 90% Pc
        Assert.Contains("Hard-start risk flag",  md);
        Assert.Contains("ok", md);                           // not flagged
    }

    [Fact]
    public void ShutdownBlowdown_AddsShutdownSubsection()
    {
        var shutdown = new Voxelforge.Combustion.ShutdownBlowdownResult(
            Samples:                       System.Array.Empty<Voxelforge.Combustion.ShutdownBlowdownSample>(),
            TimeToSubcritical_s:           0.450,
            TimeTo10PctPc_s:               0.092,
            ResidualPropellantBurned_kg:   1.65,
            ResidualPropellantVented_kg:   0.05,
            TotalImpulseLoss_Ns:           1500,
            Warnings:                      System.Array.Empty<string>());

        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0,
            shutdownBlowdown: shutdown);

        Assert.Contains("## Startup / Shutdown sequence", md);
        Assert.Contains("### Shutdown / blowdown", md);
        Assert.Contains("0.092 s", md);                  // time to 10 % Pc
        Assert.Contains("0.450 s", md);                  // time to subcritical
        Assert.Contains("1500 N·s", md);                 // impulse loss
    }

    [Fact]
    public void Chilldown_AddsChilldownSubsection()
    {
        var chill = new Voxelforge.HeatTransfer.ChilldownResult(
            TimeToChill_s:               18.0,
            TimeConstant_s:               6.0,
            PropellantMassConsumed_kg:    2.4,
            PeakThermalShockStress_MPa: 180.0,
            ChilldownComplete:           true,
            IsAcceptable:                true,
            Regime:                      "nucleate boiling",
            Warnings:                    System.Array.Empty<string>());

        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0,
            chilldown: chill);

        Assert.Contains("## Startup / Shutdown sequence", md);
        Assert.Contains("### Pre-fire chilldown", md);
        Assert.Contains("18.0 s",  md);                  // time to chill
        Assert.Contains("nucleate boiling", md);
    }

    [Fact]
    public void ShutdownNaNTimes_RenderedAsEmDash()
    {
        // When the integrator's time-to-X output is NaN (threshold not
        // crossed within sim window), the report renders "—" so the
        // operator can tell the predicted time wasn't reachable.
        var shutdown = new Voxelforge.Combustion.ShutdownBlowdownResult(
            Samples:                       System.Array.Empty<Voxelforge.Combustion.ShutdownBlowdownSample>(),
            TimeToSubcritical_s:           double.NaN,
            TimeTo10PctPc_s:               double.NaN,
            ResidualPropellantBurned_kg:   0.50,
            ResidualPropellantVented_kg:   0.05,
            TotalImpulseLoss_Ns:           500,
            Warnings:                      new[] { "duration too short" });

        var md = SafetyReport.BuildMarkdown(
            Proof(minSF: 1.5, passes: true, burstMargin: 2.5),
            WallMaterials.All[1], meop_Pa: 6.7e6, gasSideWallThickness_mm: 1.0,
            shutdownBlowdown: shutdown);

        // Both NaN-valued rows should render with the em-dash sentinel.
        // Match the exact "| —" cell so we don't false-positive on
        // dashes elsewhere in the report.
        Assert.Matches(@"\| Time to 10 % Pc \| —\s*\|", md);
        Assert.Matches(@"\| Time to subcritical \(1\.1× ambient\) \| —\s*\|", md);
    }
}
