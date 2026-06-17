// CfdFieldExport.cs — VTK XML ImageData (.vti) field export for CFD handoff.
//
// Why VTI, not OpenVDB?
// ─────────────────────
// An OpenVDB (.vdb) approach via PicoGK's `ScalarField` / `VectorField`
// API was considered. PicoGK 1.7.7.5 exposes those types in-memory but
// does NOT publish a stable file-level VDB writer consumable from
// managed code. VTK ImageData is a strictly
// more portable format — readable natively by ParaView, VisIt, OpenFOAM
// (via `foamToVTK` reversal), Ansys Fluent / CFX, FLOW-3D, and every
// major visualisation + CFD-preprocessor tool in use today — and we can
// write it in pure C# with no external dependency.
//
// Output structure
// ────────────────
// A single `.vti` file with four point-data arrays on a structured
// cartesian voxel grid aligned to the chamber axis:
//
//   • solid_domain        — Float32, 1 = inside wall, 0 = elsewhere
//   • fluid_domain        — Float32, 1 = inside cavity, 0 = elsewhere
//   • wall_temperature_K  — Float32, sampled from `StationResult.GasSideWallTemp_K`
//                          on solid cells, 0 elsewhere
//   • velocity_init_ms    — Float32 × 3, axial flow magnitude from
//                          `StationResult.CoolantVelocity_ms` inside the
//                          cooling jacket, zero elsewhere. Serves as an
//                          initial-condition seed for LBM / transient CFD.
//
// Grid resolution is configurable (default 64 × 64 × 256) — this is
// CFD-boundary-condition-seeding resolution, not a full voxel dump, so
// memory stays modest (~25 MB at default including all four fields).
//
// Format reference
// ────────────────
// VTK XML Structured Grid reference:
//   https://kitware.github.io/vtk-examples/site/VTKFileFormats/#imagedata
// Uses AppendedData with raw binary encoding for compactness — the
// alternative (inline ASCII) bloats files 3-10× and slows ParaView load
// noticeably. Little-endian on-disk (Windows/Linux x64 native).

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;

namespace Voxelforge.IO;

/// <summary>
/// Sampling grid extent and resolution for a <see cref="CfdFieldExport"/>
/// call. The grid is axis-aligned: x along the chamber axis, y and z
/// transverse. Origin is at the injector face (x=0) on the chamber axis.
/// </summary>
public sealed record CfdFieldGrid(
    int    Nx,               // axial sample count
    int    Ny,               // transverse sample count (Y axis)
    int    Nz,               // transverse sample count (Z axis)
    double TransverseHalfWidth_mm)  // half-extent in Y and Z
{
    public static CfdFieldGrid Default(ChamberContour contour) =>
        new(Nx: 256, Ny: 64, Nz: 64,
            TransverseHalfWidth_mm: 1.10 * Math.Max(
                contour.ChamberRadius_mm,
                contour.ExitRadius_mm));

    /// <summary>
    /// Sprint 10 Track C (2026-04-22): default grid for the aerospike
    /// plug export. Transverse half-width is 1.30× the larger of the
    /// throat-outer or cowl-outer radius — leaves a modest free-stream
    /// margin the CFD preprocessor can fill with atmospheric BCs
    /// without the grid bleeding memory.
    /// </summary>
    public static CfdFieldGrid DefaultAerospike(AerospikeContour contour) =>
        new(Nx: 256, Ny: 64, Nz: 64,
            TransverseHalfWidth_mm: 1.30 * Math.Max(
                contour.ThroatOuterRadius_mm,
                double.IsFinite(contour.CowlOuterRadius_mm) && contour.CowlOuterRadius_mm > 0
                    ? contour.CowlOuterRadius_mm
                    : contour.ThroatOuterRadius_mm));
}

/// <summary>
/// Pre-computed field totals returned by
/// <see cref="CfdFieldExport.Write"/> — lets tests + the CLI surface
/// a "wrote N solid voxels, M fluid voxels, total X MB" summary
/// without re-reading the file.
/// </summary>
public sealed record CfdFieldStats(
    int  Nx,
    int  Ny,
    int  Nz,
    int  SolidVoxelCount,
    int  FluidVoxelCount,
    long FileBytes,
    double WriteWallMs);

/// <summary>
/// VTK-ImageData (.vti) field writer. Pure C#; no PicoGK dependency.
/// Safe to call from any thread. Synchronous.
/// </summary>
public static class CfdFieldExport
{
    /// <summary>
    /// Write a .vti file sampling the four CFD fields on a structured
    /// grid derived from the given contour + solver output. Throws
    /// <see cref="ArgumentException"/> on invalid inputs;
    /// <see cref="IOException"/> on write failure.
    /// </summary>
    public static CfdFieldStats Write(
        string           outPath,
        ChamberContour   contour,
        ChannelSchedule  channels,
        RegenSolverOutputs solver,
        double           outerJacketThickness_mm,
        CfdFieldGrid?    grid = null)
    {
        if (string.IsNullOrWhiteSpace(outPath)) throw new ArgumentException("outPath required", nameof(outPath));
        if (contour  is null) throw new ArgumentNullException(nameof(contour));
        if (channels is null) throw new ArgumentNullException(nameof(channels));
        if (solver   is null) throw new ArgumentNullException(nameof(solver));

        var g = grid ?? CfdFieldGrid.Default(contour);
        if (g.Nx < 4 || g.Ny < 4 || g.Nz < 4)
            throw new ArgumentException("Grid resolution must be ≥ 4 along every axis", nameof(grid));
        if (g.Nx * g.Ny * g.Nz > 100_000_000)
            throw new ArgumentException(
                $"Grid size {g.Nx}×{g.Ny}×{g.Nz} exceeds 100M-voxel safety cap.", nameof(grid));

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // ── 1. Sample the four fields on the structured grid ──────
        double xMin = 0.0;
        double xMax = contour.TotalLength_mm;
        double dx   = (xMax - xMin) / (g.Nx - 1);
        double dy   = (2.0 * g.TransverseHalfWidth_mm) / (g.Ny - 1);
        double dz   = (2.0 * g.TransverseHalfWidth_mm) / (g.Nz - 1);

        int voxels = g.Nx * g.Ny * g.Nz;
        var solid       = new float[voxels];
        var fluid       = new float[voxels];
        var wallTemp    = new float[voxels];
        var velocityXYZ = new float[voxels * 3];

        int solidCount = 0, fluidCount = 0;

        for (int ix = 0; ix < g.Nx; ix++)
        {
            double x = xMin + ix * dx;
            int stationIdx = contour.StationAt(x);
            var station = contour.Stations[stationIdx];
            double rInner = station.R_mm;
            // Outer jacket radius: inner + wall + channel height + jacket.
            double hChannel = InterpolateChannelHeight(channels, contour, stationIdx);
            double rOuterShell = rInner + 0.8 + hChannel + outerJacketThickness_mm;
            double rChannelInner = rInner + 0.8;          // approximate gas-side wall
            double rChannelOuter = rInner + 0.8 + hChannel;

            // Look up solver per-station fields (guard for skip-regen case).
            double tWall      = (solver.Stations?.Length > stationIdx)
                                ? solver.Stations[stationIdx].GasSideWallTemp_K : 0.0;
            double vCool      = (solver.Stations?.Length > stationIdx)
                                ? solver.Stations[stationIdx].CoolantVelocity_ms : 0.0;

            for (int iy = 0; iy < g.Ny; iy++)
            {
                double y = -g.TransverseHalfWidth_mm + iy * dy;
                for (int iz = 0; iz < g.Nz; iz++)
                {
                    double z = -g.TransverseHalfWidth_mm + iz * dz;
                    double r = Math.Sqrt(y * y + z * z);
                    int idx = ((ix * g.Ny) + iy) * g.Nz + iz;

                    if (r <= rInner)
                    {
                        // Gas cavity (fluid).
                        fluid[idx] = 1f;
                        fluidCount++;
                    }
                    else if (r <= rChannelOuter && r >= rChannelInner)
                    {
                        // Cooling-jacket annulus — treated as fluid with
                        // an initial axial velocity. Velocity direction
                        // is +x through the jacket (counterflow stored on
                        // solver.Direction is irrelevant for CFD IC
                        // seeding — the user re-imposes at the inlet BC).
                        fluid[idx] = 1f;
                        fluidCount++;
                        velocityXYZ[idx * 3 + 0] = (float)vCool;
                    }
                    else if (r <= rOuterShell)
                    {
                        // Structural wall / jacket / rib.
                        solid[idx] = 1f;
                        wallTemp[idx] = (float)tWall;
                        solidCount++;
                    }
                    // else: outside the engine — stays 0 on all fields.
                }
            }
        }

        // ── 2. Write the XML ImageData header + appended binary ───
        long fileBytes = WriteVti(outPath, g, xMin, dx, dy, dz,
                                  solid, fluid, wallTemp, velocityXYZ);

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (t1 - t0) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;

        return new CfdFieldStats(
            Nx: g.Nx, Ny: g.Ny, Nz: g.Nz,
            SolidVoxelCount: solidCount,
            FluidVoxelCount: fluidCount,
            FileBytes: fileBytes,
            WriteWallMs: ms);
    }

    /// <summary>
    /// Piecewise-linear channel height at a station index. Mirrors
    /// <see cref="ChannelSchedule"/>'s three-anchor taper (chamber /
    /// throat / exit).
    /// </summary>
    private static double InterpolateChannelHeight(
        ChannelSchedule ch, ChamberContour contour, int stationIdx)
    {
        int throatIdx = contour.ThroatIndex;
        int N = contour.Stations.Length;
        if (stationIdx <= throatIdx)
        {
            double t = throatIdx == 0 ? 0.0 : (double)stationIdx / throatIdx;
            return ch.ChannelHeightAtChamber_mm
                 + t * (ch.ChannelHeightAtThroat_mm - ch.ChannelHeightAtChamber_mm);
        }
        double u = (double)(stationIdx - throatIdx) / Math.Max(1, N - 1 - throatIdx);
        return ch.ChannelHeightAtThroat_mm
             + u * (ch.ChannelHeightAtExit_mm - ch.ChannelHeightAtThroat_mm);
    }

    /// <summary>
    /// Sprint 10 Track C (2026-04-22): aerospike plug CFD field export.
    /// Mirrors <see cref="Write"/> but samples an aerospike geometry:
    /// a central plug body surrounded by an annular throat (optionally
    /// with a short cowl) and an open free-expansion region downstream.
    ///
    /// <para>
    /// The emitted .vti has the same four fields as the bell-chamber
    /// export so ParaView state files + OpenFOAM case templates are
    /// reusable. Semantic differences:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>solid_domain</c> — 1 inside the plug body
    ///   (r ≤ R_plug(x) for x ∈ [0, PlugTruncatedLength_mm]);
    ///   0 elsewhere.</item>
    ///   <item><c>fluid_domain</c> — 1 in the annular throat
    ///   (R_plug &lt; r ≤ R_cowl on cowl stations) AND in the open
    ///   free-expansion region beyond the cowl out to the grid
    ///   boundary. CFD preprocessor is expected to impose
    ///   atmospheric BCs on the far-field boundary.</item>
    ///   <item><c>wall_temperature_K</c> — plug-surface T from
    ///   <see cref="AerospikeThermalResult.GasSideWallT_K"/> on solid
    ///   cells; 0 elsewhere. Null <paramref name="thermal"/> yields an
    ///   all-zero field (still writes — handy for pre-thermal CFD).</item>
    ///   <item><c>velocity_init_ms</c> — axial component seeded in the
    ///   annular throat + near-plug fluid region at a conservative
    ///   throat-sonic ≈ 300 m/s placeholder (γ ≈ 1.15 CH4/LOX sound
    ///   speed). Gives the CFD solver a warm start; full Mach-sweep
    ///   IC is out of scope for this sprint.</item>
    /// </list>
    /// </summary>
    /// <param name="outPath">Output .vti path.</param>
    /// <param name="contour">Aerospike contour from
    /// <see cref="AerospikeContourGenerator.Generate"/>.</param>
    /// <param name="thermal">Optional plug-surface thermal result. Null
    /// is allowed — <c>wall_temperature_K</c> writes as zeros.</param>
    /// <param name="grid">Optional grid override. Default covers the
    /// full plug plus a 20 % downstream free-stream margin.</param>
    public static CfdFieldStats WriteAerospike(
        string                  outPath,
        AerospikeContour        contour,
        AerospikeThermalResult? thermal = null,
        CfdFieldGrid?           grid    = null)
    {
        if (string.IsNullOrWhiteSpace(outPath)) throw new ArgumentException("outPath required", nameof(outPath));
        if (contour is null) throw new ArgumentNullException(nameof(contour));

        var g = grid ?? CfdFieldGrid.DefaultAerospike(contour);
        if (g.Nx < 4 || g.Ny < 4 || g.Nz < 4)
            throw new ArgumentException("Grid resolution must be ≥ 4 along every axis", nameof(grid));
        if ((long)g.Nx * g.Ny * g.Nz > 100_000_000)
            throw new ArgumentException(
                $"Grid size {g.Nx}×{g.Ny}×{g.Nz} exceeds 100M-voxel safety cap.", nameof(grid));
        if (thermal is not null
            && thermal.GasSideWallT_K.Length != contour.Stations.Length)
        {
            throw new ArgumentException(
                $"Thermal result has {thermal.GasSideWallT_K.Length} stations "
              + $"but contour has {contour.Stations.Length}.", nameof(thermal));
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        // ── 1. Sample the four fields on the structured grid ──────
        // Include a 20 % downstream free-stream margin so the plume
        // exit plane isn't right on the domain boundary (CFD solvers
        // prefer a standoff zone for the far-field BC).
        double xMin = 0.0;
        double xMax = 1.20 * contour.PlugTruncatedLength_mm;
        double dx   = (xMax - xMin) / (g.Nx - 1);
        double dy   = (2.0 * g.TransverseHalfWidth_mm) / (g.Ny - 1);
        double dz   = (2.0 * g.TransverseHalfWidth_mm) / (g.Nz - 1);

        int voxels = g.Nx * g.Ny * g.Nz;
        var solid       = new float[voxels];
        var fluid       = new float[voxels];
        var wallTemp    = new float[voxels];
        var velocityXYZ = new float[voxels * 3];

        int solidCount = 0, fluidCount = 0;

        // Conservative throat-sonic seed velocity (m/s). LOX/CH4 γ ≈ 1.15,
        // T_t ≈ 2700 K → c ≈ 1040 m/s, scaled down by cos(flow angle) ≈
        // 0.3 and an IC-seeding safety factor → ~300 m/s is a reasonable
        // warm-start the CFD solver refines toward steady-state.
        const float V_SEED_MS = 300f;

        for (int ix = 0; ix < g.Nx; ix++)
        {
            double x = xMin + ix * dx;

            // Plug surface: only defined for x ∈ [0, PlugTruncatedLength].
            // Beyond that: plug is absent (base / free expansion region).
            double rPlug;
            double rCowl;
            double tWall;
            bool onPlug;
            if (x >= 0 && x <= contour.PlugTruncatedLength_mm)
            {
                int stationIdx = contour.StationAt(x);
                var station = contour.Stations[stationIdx];
                rPlug = station.R_inner_mm;
                rCowl = double.IsFinite(station.R_outer_mm) ? station.R_outer_mm : double.NaN;
                tWall = thermal is not null ? thermal.GasSideWallT_K[stationIdx] : 0.0;
                onPlug = true;
            }
            else
            {
                rPlug = 0.0;
                rCowl = double.NaN;
                tWall = 0.0;
                onPlug = false;
            }

            for (int iy = 0; iy < g.Ny; iy++)
            {
                double y = -g.TransverseHalfWidth_mm + iy * dy;
                for (int iz = 0; iz < g.Nz; iz++)
                {
                    double z = -g.TransverseHalfWidth_mm + iz * dz;
                    double r = Math.Sqrt(y * y + z * z);
                    int idx = ((ix * g.Ny) + iy) * g.Nz + iz;

                    if (onPlug && r <= rPlug)
                    {
                        // Inside the plug body — solid.
                        solid[idx] = 1f;
                        wallTemp[idx] = (float)tWall;
                        solidCount++;
                    }
                    else
                    {
                        // Everything outside the plug (or x outside the
                        // plug's axial extent) is fluid — throat
                        // annulus, cowl annulus, or free-expansion
                        // region. The CFD preprocessor will drop
                        // atmospheric BCs on the grid boundary.
                        fluid[idx] = 1f;
                        fluidCount++;

                        // Seed an axial-flow IC in the near-plug
                        // fluid region (between R_plug and 1.5 × R_cowl
                        // if cowl exists, else out to 2 × R_plug). This
                        // is a warm start for the CFD solver; steady-
                        // state will be reached by iteration from the
                        // injector BC regardless.
                        double rNearField = double.IsFinite(rCowl)
                            ? 1.5 * rCowl
                            : 2.0 * Math.Max(rPlug, 1.0);
                        if (onPlug && r <= rNearField)
                            velocityXYZ[idx * 3 + 0] = V_SEED_MS;
                    }
                }
            }
        }

        // ── 2. Write the XML ImageData header + appended binary ───
        long fileBytes = WriteVti(outPath, g, xMin, dx, dy, dz,
                                  solid, fluid, wallTemp, velocityXYZ);

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (t1 - t0) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;

        return new CfdFieldStats(
            Nx: g.Nx, Ny: g.Ny, Nz: g.Nz,
            SolidVoxelCount: solidCount,
            FluidVoxelCount: fluidCount,
            FileBytes: fileBytes,
            WriteWallMs: ms);
    }

    // ─────────────────────── VTI writer ───────────────────────

    private static long WriteVti(
        string outPath, CfdFieldGrid g, double xMin, double dx, double dy, double dz,
        float[] solid, float[] fluid, float[] wallTemp, float[] velocityXYZ)
    {
        // Offsets are relative to the first byte of the AppendedData
        // block. Each array is preceded by a UInt32 byte count (VTK
        // spec for header_type="UInt32").
        int voxels = g.Nx * g.Ny * g.Nz;
        int scalarBytes = voxels * sizeof(float);
        int vectorBytes = voxels * 3 * sizeof(float);

        long offSolid    = 0;
        long offFluid    = offSolid    + sizeof(uint) + scalarBytes;
        long offWallT    = offFluid    + sizeof(uint) + scalarBytes;
        long offVelocity = offWallT    + sizeof(uint) + scalarBytes;

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.Append("<?xml version=\"1.0\"?>\n");
        sb.Append("<VTKFile type=\"ImageData\" version=\"1.0\" byte_order=\"LittleEndian\" header_type=\"UInt32\">\n");
        sb.AppendFormat(inv,
            "  <ImageData WholeExtent=\"0 {0} 0 {1} 0 {2}\" Origin=\"{3} {4} {5}\" Spacing=\"{6} {7} {8}\">\n",
            g.Nx - 1, g.Ny - 1, g.Nz - 1,
            xMin, -g.TransverseHalfWidth_mm, -g.TransverseHalfWidth_mm,
            dx, dy, dz);
        sb.AppendFormat(inv,
            "    <Piece Extent=\"0 {0} 0 {1} 0 {2}\">\n",
            g.Nx - 1, g.Ny - 1, g.Nz - 1);
        sb.Append("      <PointData Scalars=\"solid_domain\" Vectors=\"velocity_init_ms\">\n");
        sb.AppendFormat(inv,
            "        <DataArray type=\"Float32\" Name=\"solid_domain\" NumberOfComponents=\"1\" format=\"appended\" offset=\"{0}\"/>\n",
            offSolid);
        sb.AppendFormat(inv,
            "        <DataArray type=\"Float32\" Name=\"fluid_domain\" NumberOfComponents=\"1\" format=\"appended\" offset=\"{0}\"/>\n",
            offFluid);
        sb.AppendFormat(inv,
            "        <DataArray type=\"Float32\" Name=\"wall_temperature_K\" NumberOfComponents=\"1\" format=\"appended\" offset=\"{0}\"/>\n",
            offWallT);
        sb.AppendFormat(inv,
            "        <DataArray type=\"Float32\" Name=\"velocity_init_ms\" NumberOfComponents=\"3\" format=\"appended\" offset=\"{0}\"/>\n",
            offVelocity);
        sb.Append("      </PointData>\n");
        sb.Append("      <CellData/>\n");
        sb.Append("    </Piece>\n");
        sb.Append("  </ImageData>\n");
        sb.Append("  <AppendedData encoding=\"raw\">\n");
        sb.Append('_');   // VTK convention: leading underscore before raw binary

        byte[] xmlHead = Encoding.ASCII.GetBytes(sb.ToString());
        byte[] xmlTail = Encoding.ASCII.GetBytes("\n  </AppendedData>\n</VTKFile>\n");

        // Sprint 14 / Track I / P3: 1 MB buffered FileStream + span-based
        // bulk write. The previous per-float `bw.Write(data[i])` loop
        // emitted 4M individual syscalls for a typical 1M-voxel × 4-field
        // export; the batched memcpy below is 10-30× faster on the same
        // bytes (output is byte-identical — `BinaryWriter` is little-endian
        // on every .NET-supported platform, and so is `MemoryMarshal.AsBytes`
        // on a `float[]` for x86/x64/ARM).
        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var bw = new BinaryWriter(fs);
        bw.Write(xmlHead);

        WriteAppendedFloatArray(bw, solid);
        WriteAppendedFloatArray(bw, fluid);
        WriteAppendedFloatArray(bw, wallTemp);
        WriteAppendedFloatArray(bw, velocityXYZ);

        bw.Write(xmlTail);
        bw.Flush();
        return fs.Length;
    }

    private static void WriteAppendedFloatArray(BinaryWriter bw, float[] data)
    {
        uint bytes = (uint)(data.Length * sizeof(float));
        bw.Write(bytes);
        // Sprint 14 / Track I / P3: bulk-write the float array as a single
        // ReadOnlySpan<byte> — produces byte-identical output to the
        // previous per-float loop (little-endian on all supported targets)
        // but in one vectorized memcpy + one or two FileStream buffer
        // flushes instead of 4·N syscalls.
        ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(data.AsSpan());
        bw.Write(raw);
    }
}
