// NoyronTierA34Tests.cs — Tier A3 + A4 forcing-function suite.
//
// Covers:
//   • A3 — CfdFieldExport: VTK ImageData (.vti) writer for CFD handoff.
//     Forcing functions for file integrity, grid bounds, scalar/vector
//     field population, field-count sanity, input validation.
//   • A4 — BuildOrientationAdvisor: orientation sweep + support-volume
//     estimate + per-region breakdown.
//   • A4 — PrinterParameterPresets: catalog completeness, JSON schema,
//     machine × material mapping, WallMaterial → LpbfMaterial helper.

using System.Text;
using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class NoyronTierA34Tests
{
    // Helpers ─────────────────────────────────────────────────────

    private static ChamberContour MakeContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm:        3.0,
            contractionRatio:       6.0,
            expansionRatio:         8.0,
            characteristicLength_m: 1.1,
            thetaN_deg:             30.0,
            thetaE_deg:             10.0,
            bellLengthFraction:     0.8,
            stationCount:           120);

    private static ChannelSchedule MakeChannels() =>
        new(ChannelCount: 40,
            RibThickness_mm: 0.8,
            GasSideWallThickness_mm: 0.8,
            ChannelHeightAtChamber_mm: 2.5,
            ChannelHeightAtThroat_mm: 1.5,
            ChannelHeightAtExit_mm: 2.0);

    private static RegenSolverOutputs MakeSyntheticSolverOutputs(ChamberContour contour)
    {
        int N = contour.Stations.Length;
        var stations = new StationResult[N];
        for (int i = 0; i < N; i++)
        {
            var s = contour.Stations[i];
            stations[i] = new StationResult(
                Index:                      i,
                X_mm:                       s.X_mm,
                R_mm:                       s.R_mm,
                AreaRatioToThroat:          1.0,
                Mach:                       0.1,
                StaticTemp_K:               300.0,
                AdiabaticWallTemp_K:        3000.0,
                EffectiveRecoveryTemp_K:    2800.0,
                FilmEffectiveness:          0.0,
                HeatFlux_Wm2:               5e6,
                h_g_Wm2K:                   5e3,
                h_c_Wm2K:                   40e3,
                GasSideWallTemp_K:          800.0 + 5.0 * i,   // monotonic ramp for test visibility
                CoolantSideWallTemp_K:      400.0,
                WallRadialProfile_K:        new double[] { 800, 700, 600, 500, 400 },
                AxialConductionFlux_Wm2:    0,
                CoolantBulkTemp_K:          200.0 + 0.5 * i,
                CoolantBulkPressure_Pa:     1e7,
                CoolantVelocity_ms:         25.0,
                Reynolds:                   5e5,
                PrandtlBulk:                2.5,
                ChannelWidth_mm:            1.0,
                ChannelHeight_mm:           2.0,
                HydraulicDiameter_mm:       1.5,
                PressureGradient_Pam:       -1e5);
        }
        return new RegenSolverOutputs(
            Stations:                   stations,
            PeakGasSideWallT_K:         1000.0,
            PeakCoolantSideWallT_K:     500.0,
            PeakStationIndex:           N / 2,
            CoolantInletT_K:            150.0,
            CoolantOutletT_K:           350.0,
            CoolantInletP_Pa:           12e6,
            CoolantOutletP_Pa:          8e6,
            CoolantPressureDrop_Pa:     4e6,
            TotalHeatLoad_W:            50e3,
            TotalWettedArea_mm2:        20000.0,
            ThroatHeatFlux_Wm2:         5e6,
            WallTempExceedsLimit:       false,
            WallMarginK:                200.0,
            FilmMassFlow_kgs:           0.0,
            IspPenaltyFraction:         0.0,
            AxialConductionRMS_Wm2:     100.0,
            Diagnostics:                new SolverDiagnostics(0, 0, 0, 0, true),
            Warnings:                   Array.Empty<string>());
    }

    // ══════════════════════ A3 — CfdFieldExport ══════════════════════

    [Fact]
    public void CfdFieldExport_WritesValidVtiFile()
    {
        var contour  = MakeContour();
        var channels = MakeChannels();
        var solver   = MakeSyntheticSolverOutputs(contour);
        using var tmp = TestTempFile.WithUniqueName("fields", "vti");
        // Use a coarser grid for test speed.
        var stats = CfdFieldExport.Write(tmp.Path, contour, channels, solver,
            outerJacketThickness_mm: 2.0,
            grid: new CfdFieldGrid(Nx: 32, Ny: 16, Nz: 16,
                                    TransverseHalfWidth_mm: 20.0));
        Assert.True(File.Exists(tmp.Path));
        Assert.True(stats.FileBytes > 0);
        Assert.Equal(32, stats.Nx);
        Assert.True(stats.SolidVoxelCount > 0,
            "Expected at least some solid voxels in the wall/jacket annulus.");
        Assert.True(stats.FluidVoxelCount > 0,
            "Expected at least some fluid voxels in the cavity.");
    }

    [Fact]
    public void CfdFieldExport_XmlHeaderContainsExpectedArrays()
    {
        var contour  = MakeContour();
        var channels = MakeChannels();
        var solver   = MakeSyntheticSolverOutputs(contour);
        using var tmp = TestTempFile.WithUniqueName("fields", "vti");
        CfdFieldExport.Write(tmp.Path, contour, channels, solver,
            outerJacketThickness_mm: 2.0,
            grid: new CfdFieldGrid(32, 16, 16, 20.0));

        // Read enough of the file to grab the XML header.
        byte[] head = new byte[2048];
        using (var fs = File.OpenRead(tmp.Path))
        {
            int n = fs.Read(head, 0, head.Length);
            Array.Resize(ref head, n);
        }
        string xml = Encoding.ASCII.GetString(head);
        Assert.Contains("VTKFile", xml);
        Assert.Contains("ImageData", xml);
        Assert.Contains("solid_domain", xml);
        Assert.Contains("fluid_domain", xml);
        Assert.Contains("wall_temperature_K", xml);
        Assert.Contains("velocity_init_ms", xml);
        Assert.Contains("NumberOfComponents=\"3\"", xml);  // vector field tag
    }

    [Fact]
    public void CfdFieldExport_RejectsTooSmallGrid()
    {
        var contour  = MakeContour();
        var channels = MakeChannels();
        var solver   = MakeSyntheticSolverOutputs(contour);
        Assert.Throws<ArgumentException>(() =>
            CfdFieldExport.Write("nope.vti", contour, channels, solver, 2.0,
                grid: new CfdFieldGrid(Nx: 2, Ny: 2, Nz: 2, TransverseHalfWidth_mm: 10.0)));
    }

    [Fact]
    public void CfdFieldExport_RejectsTooLargeGrid()
    {
        var contour  = MakeContour();
        var channels = MakeChannels();
        var solver   = MakeSyntheticSolverOutputs(contour);
        Assert.Throws<ArgumentException>(() =>
            CfdFieldExport.Write("nope.vti", contour, channels, solver, 2.0,
                grid: new CfdFieldGrid(Nx: 1000, Ny: 1000, Nz: 1000, TransverseHalfWidth_mm: 10.0)));
    }

    [Fact]
    public void CfdFieldExport_DefaultGridIsDerivedFromContour()
    {
        var contour = MakeContour();
        var g = CfdFieldGrid.Default(contour);
        Assert.True(g.TransverseHalfWidth_mm > contour.ExitRadius_mm);
        Assert.True(g.Nx > 0 && g.Ny > 0 && g.Nz > 0);
    }

    // ══════════════════════ Sprint 10 Track C — CfdFieldExport aerospike ══════════════════════

    private static AerospikeContour MakeAerospikeContour() =>
        AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 20.0,
            expansionRatio:       15.0,
            plugLengthRatio:      0.30,
            gamma:                1.15,
            stationCount:         40,
            includeCowl:          true);

    private static AerospikeThermalResult MakeSyntheticAerospikeThermal(AerospikeContour contour)
    {
        int N = contour.Stations.Length;
        var wallT = new double[N];
        var coolT = new double[N];
        var heat  = new double[N];
        for (int i = 0; i < N; i++)
        {
            // Monotonic ramp gives test visibility into station indexing.
            wallT[i] = 600.0 + 10.0 * i;
            coolT[i] = 250.0 + 0.5 * i;
            heat[i]  = 3e6 + 1e4 * i;
        }
        return new AerospikeThermalResult(
            GasSideWallT_K:        wallT,
            CoolantBulkT_K:        coolT,
            HeatFlux_Wm2:          heat,
            PeakGasSideWallT_K:    wallT[^1],
            PeakStation_X_mm:      contour.Stations[^1].X_mm,
            CoolantOutletT_K:      coolT[^1],
            CoolantPressureDrop_Pa: 2e6,
            TotalHeatLoad_W:       40e3,
            Warnings:              Array.Empty<string>());
    }

    [Fact]
    public void CfdFieldExport_Aerospike_WritesValidVtiFile()
    {
        var contour = MakeAerospikeContour();
        var thermal = MakeSyntheticAerospikeThermal(contour);
        using var tmp = TestTempFile.WithUniqueName("aerofields", "vti");
        var stats = CfdFieldExport.WriteAerospike(tmp.Path, contour, thermal,
            grid: new CfdFieldGrid(Nx: 32, Ny: 16, Nz: 16,
                                    TransverseHalfWidth_mm: 30.0));
        Assert.True(File.Exists(tmp.Path));
        Assert.True(stats.FileBytes > 0);
        Assert.Equal(32, stats.Nx);
        Assert.True(stats.SolidVoxelCount > 0,
            "Expected at least some solid voxels inside the plug body.");
        Assert.True(stats.FluidVoxelCount > 0,
            "Expected at least some fluid voxels in the free-expansion region.");
    }

    [Fact]
    public void CfdFieldExport_Aerospike_XmlHeaderContainsExpectedArrays()
    {
        var contour = MakeAerospikeContour();
        var thermal = MakeSyntheticAerospikeThermal(contour);
        using var tmp = TestTempFile.WithUniqueName("aerofields", "vti");
        CfdFieldExport.WriteAerospike(tmp.Path, contour, thermal,
            grid: new CfdFieldGrid(32, 16, 16, 30.0));

        byte[] head = new byte[2048];
        using (var fs = File.OpenRead(tmp.Path))
        {
            int n = fs.Read(head, 0, head.Length);
            Array.Resize(ref head, n);
        }
        string xml = Encoding.ASCII.GetString(head);
        Assert.Contains("VTKFile", xml);
        Assert.Contains("ImageData", xml);
        Assert.Contains("solid_domain", xml);
        Assert.Contains("fluid_domain", xml);
        Assert.Contains("wall_temperature_K", xml);
        Assert.Contains("velocity_init_ms", xml);
        Assert.Contains("NumberOfComponents=\"3\"", xml);
    }

    [Fact]
    public void CfdFieldExport_Aerospike_RejectsTooSmallGrid()
    {
        var contour = MakeAerospikeContour();
        var thermal = MakeSyntheticAerospikeThermal(contour);
        Assert.Throws<ArgumentException>(() =>
            CfdFieldExport.WriteAerospike("nope.vti", contour, thermal,
                grid: new CfdFieldGrid(Nx: 2, Ny: 2, Nz: 2, TransverseHalfWidth_mm: 30.0)));
    }

    [Fact]
    public void CfdFieldExport_Aerospike_RejectsMismatchedThermalStations()
    {
        var contour = MakeAerospikeContour();
        // Craft a thermal result with the wrong station count.
        var badThermal = new AerospikeThermalResult(
            GasSideWallT_K:        new double[contour.Stations.Length + 3],
            CoolantBulkT_K:        new double[contour.Stations.Length + 3],
            HeatFlux_Wm2:          new double[contour.Stations.Length + 3],
            PeakGasSideWallT_K:    800.0,
            PeakStation_X_mm:      10.0,
            CoolantOutletT_K:      300.0,
            CoolantPressureDrop_Pa: 1e6,
            TotalHeatLoad_W:       10e3,
            Warnings:              Array.Empty<string>());
        Assert.Throws<ArgumentException>(() =>
            CfdFieldExport.WriteAerospike("nope.vti", contour, badThermal,
                grid: new CfdFieldGrid(32, 16, 16, 30.0)));
    }

    [Fact]
    public void CfdFieldExport_Aerospike_AcceptsNullThermal()
    {
        // Pre-thermal CFD handoff is valid — wall_temperature_K writes as zeros.
        var contour = MakeAerospikeContour();
        using var tmp = TestTempFile.WithUniqueName("aerofields", "vti");
        var stats = CfdFieldExport.WriteAerospike(tmp.Path, contour, thermal: null,
            grid: new CfdFieldGrid(16, 12, 12, 30.0));
        Assert.True(stats.FileBytes > 0);
        Assert.True(stats.SolidVoxelCount > 0);
    }

    [Fact]
    public void CfdFieldExport_Aerospike_DefaultGridExceedsThroatRadius()
    {
        var contour = MakeAerospikeContour();
        var g = CfdFieldGrid.DefaultAerospike(contour);
        Assert.True(g.TransverseHalfWidth_mm > contour.ThroatOuterRadius_mm,
            "Default aerospike grid must leave free-stream margin outside the throat lip.");
        Assert.True(g.Nx >= 64 && g.Ny >= 16 && g.Nz >= 16);
    }

    [Fact]
    public void AerospikeContour_StationAt_ReturnsNearestIndex()
    {
        var contour = MakeAerospikeContour();
        // Pick a known station, query its X, expect that index back.
        int midIdx = contour.Stations.Length / 2;
        double xQuery = contour.Stations[midIdx].X_mm;
        Assert.Equal(midIdx, contour.StationAt(xQuery));
        // x=0 should land on the throat (index 0 in the Angelino builder).
        Assert.Equal(0, contour.StationAt(0.0));
    }

    // ══════════════════════ A4 — BuildOrientationAdvisor ══════════════════════

    [Fact]
    public void Advisor_ReturnsBestOrientation_WithRationale()
    {
        var r = BuildOrientationAdvisor.Analyze(
            MakeContour(), MakeChannels(), outerJacketThickness_mm: 2.0);

        Assert.NotNull(r.Best);
        Assert.Equal(2, r.Ranked.Count);          // two axial candidates
        Assert.Contains(r.Best, r.Ranked);
        Assert.False(string.IsNullOrWhiteSpace(r.RationaleText));
        Assert.False(string.IsNullOrWhiteSpace(r.RecommendedBuildOrientation));
    }

    [Fact]
    public void Advisor_BestCandidateIsAtLeastAsGoodAsRunnerUp()
    {
        var r = BuildOrientationAdvisor.Analyze(
            MakeContour(), MakeChannels(), outerJacketThickness_mm: 2.0);

        // Primary sort: lower unprintable count wins.
        Assert.True(r.Best.UnprintableStationCount
                    <= r.Ranked[^1].UnprintableStationCount,
            $"Best {r.Best.UnprintableStationCount} > Worst {r.Ranked[^1].UnprintableStationCount}");
    }

    [Fact]
    public void Advisor_UnprintableByRegionCoversAllRegions()
    {
        var r = BuildOrientationAdvisor.Analyze(
            MakeContour(), MakeChannels(), outerJacketThickness_mm: 2.0);

        foreach (ChamberRegion region in Enum.GetValues<ChamberRegion>())
            Assert.True(r.UnprintableByRegion.ContainsKey(region),
                $"Missing region {region} in UnprintableByRegion map.");
    }

    [Fact]
    public void Advisor_SupportVolumeIsNonNegative()
    {
        var r = BuildOrientationAdvisor.Analyze(
            MakeContour(), MakeChannels(), outerJacketThickness_mm: 2.0);
        Assert.True(r.Best.EstimatedSupportVolume_cm3 >= 0);
        foreach (var c in r.Ranked)
            Assert.True(c.EstimatedSupportVolume_cm3 >= 0);
    }

    [Fact]
    public void Advisor_RejectsNullContour()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BuildOrientationAdvisor.Analyze(null!, MakeChannels(), 2.0));
    }

    // ══════════════════════ A4 — PrinterParameterPresets ══════════════════════

    [Fact]
    public void PrinterParameterPresets_CatalogIsComplete()
    {
        // Every (machine, material) pair must resolve — forcing function
        // to keep the catalog in sync with enum additions.
        foreach (var m in PrinterParameterPresets.AllMachines)
        foreach (var mat in PrinterParameterPresets.AllMaterials)
        {
            var p = PrinterParameterPresets.Get(m, mat);
            Assert.Equal(m, p.Machine);
            Assert.Equal(mat, p.Material);
            Assert.True(p.LaserPower_W > 0);
            Assert.True(p.ScanSpeed_mms > 0);
            Assert.True(p.LayerThickness_mm > 0);
            Assert.True(p.MinimumFeature_mm > 0);
            Assert.False(string.IsNullOrWhiteSpace(p.CitationNote));
        }
    }

    [Fact]
    public void PrinterParameterPresets_JsonIncludesKeyFields()
    {
        var p = PrinterParameterPresets.Get(LpbfMachine.EosM290, LpbfMaterial.CuCrZr);
        string json = PrinterParameterPresets.ToJson(p);

        // PR-2 namespace rename (2026-04-30): printer-preset schema tag
        // is preserved as the literal "RegenChamberDesigner.PrinterParameterPreset/1"
        // (per Sprint 0 PR-2 plan magic-string preservation list) so the
        // test still asserts the unchanged on-disk schema string.
        Assert.Contains("\"schema\":\"RegenChamberDesigner.PrinterParameterPreset/1\"", json);
        Assert.Contains("\"machine\":\"EosM290\"", json);
        Assert.Contains("\"material\":\"CuCrZr\"", json);
        Assert.Contains("\"laser_power_W\"", json);
        Assert.Contains("\"scan_speed_mms\"", json);
        Assert.Contains("\"layer_thickness_mm\"", json);
        Assert.Contains("\"citation\"", json);
        Assert.Contains("\"advisory\"", json);
    }

    [Fact]
    public void PrinterParameterPresets_JsonRoundTripsToFile()
    {
        var p = PrinterParameterPresets.Get(
            LpbfMachine.NikonSlmNxg600, LpbfMaterial.GRCop42);
        using var tmp = TestTempFile.WithUniqueName("params", "json");
        PrinterParameterPresets.WriteJsonFile(p, tmp.Path);
        Assert.True(File.Exists(tmp.Path));
        string content = File.ReadAllText(tmp.Path);
        Assert.Contains("\"machine\":\"NikonSlmNxg600\"", content);
        Assert.Contains("\"material\":\"GRCop42\"", content);
    }

    [Theory]
    [InlineData(0, LpbfMaterial.GRCop42)]
    [InlineData(1, LpbfMaterial.CuCrZr)]
    [InlineData(3, LpbfMaterial.Inconel718)]
    public void PrinterParameterPresets_WallMaterialMapping(int wallIdx, LpbfMaterial expected)
    {
        Assert.Equal(expected, PrinterParameterPresets.FromWallMaterialIndex(wallIdx));
    }

    [Fact]
    public void PrinterParameterPresets_WallMaterialMappingReturnsNullForUncovered()
    {
        // Inconel 625 (index 2) has no LPBF preset today.
        Assert.Null(PrinterParameterPresets.FromWallMaterialIndex(2));
    }

    [Fact]
    public void PrinterParameterPresets_MinimumFeatureSizesAreReasonable()
    {
        // Sanity: none of the presets claim sub-0.2 mm features (unreal
        // for powder-bed metal) or above 1 mm (we'd have bigger problems).
        foreach (var p in PrinterParameterPresets.All)
            Assert.InRange(p.MinimumFeature_mm, 0.2, 1.0);
    }

    // ══════════════════════ A3 + A4 integration ══════════════════════

    [Fact]
    public void Advisor_IsConsistentWithAutoSeederOutput()
    {
        // AutoSeeder hand-off: seed → contour → advisor returns without
        // exception. Forcing function — if the seeder ever starts
        // emitting contour-incompatible values, this test fires.
        var seed = AutoSeeder.Seed(new EngineSpec(
            Combustion.PropellantPair.LOX_CH4, 10_000, 7e6, 10.0));
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:        6.0,
            contractionRatio:       seed.Design.ContractionRatio,
            expansionRatio:         seed.Design.ExpansionRatio,
            characteristicLength_m: seed.Design.CharacteristicLength_m,
            thetaN_deg:             seed.Design.BellEntranceAngle_deg,
            thetaE_deg:             seed.Design.BellExitAngle_deg,
            bellLengthFraction:     seed.Design.BellLengthFraction,
            stationCount:           seed.Design.ContourStationCount);
        var channels = new ChannelSchedule(
            ChannelCount:              seed.Design.ChannelCount,
            RibThickness_mm:           seed.Design.RibThickness_mm,
            GasSideWallThickness_mm:   seed.Design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: seed.Design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm:  seed.Design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm:    seed.Design.ChannelHeightExit_mm);
        var advisor = BuildOrientationAdvisor.Analyze(
            contour, channels, seed.Design.OuterJacketThickness_mm);
        Assert.NotNull(advisor.Best);
        Assert.NotNull(advisor.RecommendedBuildOrientation);
    }
}
