// CfdFieldExportPointOrderingTests.cs — regression tests for two red-team
// round-4 findings in CfdFieldExport.cs:
//
//   1. VTK point data was written z-fastest (idx = (ix*Ny+iy)*Nz+iz) instead
//      of the x-fastest order its own WholeExtent/Piece header declares
//      (VTK ImageData convention: pointId = i + j*dimX + k*dimX*dimY). Since
//      the production default grid has Nx=256 != Nz=64, this silently
//      scrambled every exported field's spatial layout, not merely relabeled
//      axes.
//   2. The gas-side wall thickness was hardcoded to the literal 0.8 instead
//      of reading channels.GasSideWallThickness_mm — invisible to the
//      existing (Windows-leg) test suite because its fixture happens to use
//      the 0.8 mm default, but wrong for any other value.
//
// CfdFieldExport itself is pure C# (no PicoGK dependency, per its own header
// comment) so this runs directly on the Linux CI leg rather than needing a
// hand-mirrored invariant.

using System;
using System.IO;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class CfdFieldExportPointOrderingTests
{
    private static RegenSolverOutputs MakeSyntheticSolverOutputs(ChamberContour contour)
    {
        int n = contour.Stations.Length;
        var stations = new StationResult[n];
        for (int i = 0; i < n; i++)
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
                GasSideWallTemp_K:          800.0 + 5.0 * i,
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
            PeakStationIndex:           n / 2,
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

    // Locates the start of the raw-binary AppendedData block: the byte
    // right after the leading '_' the VTK format requires following
    // `<AppendedData encoding="raw">`.
    private static int FindAppendedDataStart(byte[] file)
    {
        byte[] marker = System.Text.Encoding.ASCII.GetBytes("<AppendedData encoding=\"raw\">\n_");
        int idx = file.AsSpan().IndexOf(marker.AsSpan());
        if (idx < 0) throw new InvalidOperationException("AppendedData marker not found in .vti file.");
        return idx + marker.Length;
    }

    // Reads one UInt32-count-prefixed float32 array (CfdFieldExport's
    // WriteAppendedFloatArray format) and advances the cursor past it.
    private static float[] ReadFloatArray(byte[] file, ref int cursor, int expectedFloatCount)
    {
        uint byteCount = BitConverter.ToUInt32(file, cursor);
        cursor += sizeof(uint);
        int expectedBytes = expectedFloatCount * sizeof(float);
        if (byteCount != (uint)expectedBytes)
            throw new InvalidOperationException(
                $"Expected {expectedBytes} bytes ({expectedFloatCount} floats) but header says {byteCount}.");
        var result = new float[expectedFloatCount];
        Buffer.BlockCopy(file, cursor, result, 0, expectedBytes);
        cursor += expectedBytes;
        return result;
    }

    [Fact]
    public void Write_PointOrderIsXFastest_FluidCavityAtTransverseCenterHoldsForEveryStation()
    {
        // Deliberately asymmetric grid (Nx != Nz) so a z-fastest vs
        // x-fastest mixup produces a detectably wrong result rather than a
        // harmless relabeling. Ny/Nz odd so the transverse-center grid
        // index lands on y=0/z=0 exactly (no floating-point rounding).
        const int nx = 8, ny = 5, nz = 7;
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 2.0, contractionRatio: 9.0, expansionRatio: 8.0,
            characteristicLength_m: 1.1, stationCount: 60);
        var channels = new ChannelSchedule(
            ChannelCount: 40, RibThickness_mm: 0.8, GasSideWallThickness_mm: 0.8,
            ChannelHeightAtChamber_mm: 2.5, ChannelHeightAtThroat_mm: 1.5, ChannelHeightAtExit_mm: 2.0);
        var solver = MakeSyntheticSolverOutputs(contour);

        string path = Path.Combine(Path.GetTempPath(), $"cfd-order-{Guid.NewGuid():N}.vti");
        try
        {
            CfdFieldExport.Write(path, contour, channels, solver, outerJacketThickness_mm: 2.0,
                grid: new CfdFieldGrid(nx, ny, nz, TransverseHalfWidth_mm: 20.0));

            byte[] file = File.ReadAllBytes(path);
            int cursor = FindAppendedDataStart(file);
            int voxels = nx * ny * nz;
            var solid = ReadFloatArray(file, ref cursor, voxels); // unused here, but must be consumed in order
            var fluid = ReadFloatArray(file, ref cursor, voxels);
            _ = solid;

            int centerIy = (ny - 1) / 2; // y = 0 exactly
            int centerIz = (nz - 1) / 2; // z = 0 exactly

            // At the chamber axis (y=z=0), r=0 is inside the gas cavity for
            // every station along the whole contour (R_mm > 0 everywhere on
            // a physically valid chamber) -- so fluid must be 1 at every ix
            // IF the writer's flat index matches VTK's x-fastest
            // convention. Under the pre-fix z-fastest indexing (nx=8 !=
            // nz=7 here), the same flat position instead holds whatever the
            // writer computed for a different, generally off-axis
            // (ix',iy',iz') triple -- which is essentially never inside the
            // cavity.
            for (int ix = 0; ix < nx; ix++)
            {
                int idx = ix + nx * (centerIy + ny * centerIz);
                Assert.True(fluid[idx] == 1f,
                    $"Expected fluid=1 on the chamber axis at station ix={ix} (x-fastest flat index {idx}); " +
                    $"got {fluid[idx]} -- point ordering does not match VTK's x-fastest convention.");
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_UsesDesignsActualGasSideWallThickness_NotHardcoded0p8mm()
    {
        // R_c = throatRadius_mm * sqrt(contractionRatio) = 2.0 * sqrt(9.0)
        // = 6.0 mm exactly, so the barrel station's (ix=0) cavity radius is
        // a known round number and a probe point can land on the grid
        // exactly (no floating-point rounding).
        const int nx = 4, ny = 41, nz = 5;
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: 2.0, contractionRatio: 9.0, expansionRatio: 8.0,
            characteristicLength_m: 1.1, stationCount: 60);
        Assert.Equal(6.0, contour.Stations[0].R_mm, 9);

        // Gas-side wall = 2.0 mm (not the 0.8 mm the old hardcoded literal
        // happened to match). A probe at r = 7.0 mm -- 1 mm into the true
        // [6.0, 8.0] wall band -- is solid under the real 2 mm wall. Under
        // the old bug's hardcoded 0.8 mm wall, the channel would instead
        // start at r = 6.8 mm, so the same r = 7.0 mm probe would already
        // be inside the fluid cooling channel.
        var channels = new ChannelSchedule(
            ChannelCount: 40, RibThickness_mm: 0.8, GasSideWallThickness_mm: 2.0,
            ChannelHeightAtChamber_mm: 2.5, ChannelHeightAtThroat_mm: 1.5, ChannelHeightAtExit_mm: 2.0);
        var solver = MakeSyntheticSolverOutputs(contour);

        string path = Path.Combine(Path.GetTempPath(), $"cfd-wall-{Guid.NewGuid():N}.vti");
        try
        {
            CfdFieldExport.Write(path, contour, channels, solver, outerJacketThickness_mm: 2.0,
                grid: new CfdFieldGrid(nx, ny, nz, TransverseHalfWidth_mm: 20.0));

            byte[] file = File.ReadAllBytes(path);
            int cursor = FindAppendedDataStart(file);
            int voxels = nx * ny * nz;
            var solid = ReadFloatArray(file, ref cursor, voxels);

            // ny=41, TransverseHalfWidth=20 -> dy = 40/40 = 1.0 mm/step
            // exactly. iy=27 -> y = -20 + 27*1.0 = 7.0 mm. nz odd -> the
            // center iz gives z = 0 exactly, so r = |y| = 7.0 mm.
            const int iy = 27;
            int iz = (nz - 1) / 2;
            const int ix = 0; // injector face: barrel station, R_mm = R_c = 6.0 exactly
            int idx = ix + nx * (iy + ny * iz);

            Assert.True(solid[idx] == 1f,
                $"Expected solid=1 at r=7.0mm (1mm into the true 2mm gas-side wall band [6,8]); " +
                $"got {solid[idx]} -- CfdFieldExport is not reading ChannelSchedule.GasSideWallThickness_mm.");
        }
        finally { File.Delete(path); }
    }
}
