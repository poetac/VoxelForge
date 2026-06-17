// AnalyticalPreviewMesh.cs — Generate a low-triangle binary STL
// directly from a `ChamberContour`
// and optional flange + channel dimensions, WITHOUT going through PicoGK
// voxels. 100–1000× faster than a Fast-preview voxel render; loses
// manufacturability checks (voxel-adequacy gate + min-feature + overhang)
// by design, so the output is strictly for visual / quick-iteration
// inspection and must never be shipped as the manufacturing artifact.
//
// Topology
// ────────
// The preview mesh is a surface-of-revolution around the x-axis formed
// from N axial stations × M azimuthal slices. Each (i, j) station/slice
// quad is split into two triangles. Optional features layered on top:
//
//   • Injector flange disc   — two concentric rings at x = 0 and
//     x = flange_thickness, capped with triangle fans.
//   • Mounting flange disc   — same pattern at x = total_length.
//   • Channel trenches       — radial rectangular ribs cut into the
//     outer shell as a visual proxy for the regen channel pattern.
//     Rendered as simple rectangular prisms abutting the outer shell;
//     NOT boolean-subtracted (that would require voxels). They appear
//     as raised ribs rather than grooves — the preview's purpose is to
//     show count + spacing, not geometric fidelity.
//
// All outputs use the same binary STL structure consumed by `StlWelder`,
// so downstream tooling (tile welder, UI preview, MeshLab) treats the
// file identically to a full voxel-rendered export.

using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

/// <summary>
/// Input bundle for the analytical preview mesh. Keep the surface area
/// minimal — this path intentionally ignores channel topology, film
/// cooling, igniter, purge, gimbal, and STL-import features because
/// they cannot be rendered without the voxel pipeline.
/// </summary>
public sealed record AnalyticalPreviewOptions(
    ChamberContour Contour,
    int            AzimuthalSlices = 48,
    bool           IncludeInjectorFlange = true,
    double         InjectorFlangeThickness_mm = 8.0,
    double         InjectorFlangeOuterRadiusFactor = 1.25,
    bool           IncludeMountingFlange = false,
    double         MountingFlangeThickness_mm = 6.0,
    int            ChannelCount = 0,
    double         RibThickness_mm = 0.8,
    double         ChannelHeightAverage_mm = 2.0,
    double         OuterJacketThickness_mm = 2.0);

/// <summary>Structured output from <see cref="AnalyticalPreviewMesh.Build"/>.</summary>
public sealed record AnalyticalPreviewResult(
    StlTriangle[] Triangles,
    int           ShellTriangleCount,
    int           InjectorFlangeTriangleCount,
    int           MountingFlangeTriangleCount,
    int           ChannelRibTriangleCount,
    double        BuildWallMs)
{
    public int TotalTriangleCount => Triangles.Length;
}

/// <summary>
/// Pure-math analytical STL generator. No PicoGK dependency; safe to
/// call from any thread; synchronous.
/// </summary>
public static class AnalyticalPreviewMesh
{
    /// <summary>Default azimuthal slice count. 48 is plenty for a preview.</summary>
    public const int DefaultAzimuthalSlices = 48;

    /// <summary>Maximum accepted azimuthal slice count (prevents runaway).</summary>
    public const int MaxAzimuthalSlices = 256;

    /// <summary>
    /// Build a revolving-surface preview mesh from a Rao contour and
    /// (optionally) flange + channel dimensions. Returns a
    /// <see cref="StlTriangle"/> array ready to pass to
    /// <see cref="StlWelder.Write(string, System.Collections.Generic.IReadOnlyList{StlTriangle}, string)"/>.
    /// </summary>
    public static AnalyticalPreviewResult Build(AnalyticalPreviewOptions opts)
    {
        if (opts is null) throw new ArgumentNullException(nameof(opts));
        if (opts.Contour is null) throw new ArgumentException("Contour required", nameof(opts));
        if (opts.AzimuthalSlices < 6 || opts.AzimuthalSlices > MaxAzimuthalSlices)
            throw new ArgumentOutOfRangeException(
                nameof(opts.AzimuthalSlices),
                $"AzimuthalSlices must be in [6, {MaxAzimuthalSlices}], got {opts.AzimuthalSlices}.");

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        var stations = opts.Contour.Stations;
        int M = opts.AzimuthalSlices;
        int N = stations.Length;

        // Outer-shell radius at station i. We inflate the wall radius by
        // (wall + jacket) so the preview reflects the outside surface of
        // the chamber, not the inner gas-side contour.
        double wallThickness = Math.Max(opts.OuterJacketThickness_mm, 0.5);

        double[] rOuter = new double[N];
        for (int i = 0; i < N; i++)
            rOuter[i] = stations[i].R_mm + wallThickness;

        var tris = new List<StlTriangle>(capacity: N * M * 2 + 512);

        // ── Shell revolving surface ────────────────────────────────
        for (int i = 0; i < N - 1; i++)
        {
            double x0 = stations[i].X_mm;
            double x1 = stations[i + 1].X_mm;
            double r0 = rOuter[i];
            double r1 = rOuter[i + 1];
            for (int j = 0; j < M; j++)
            {
                double a0 = (2.0 * Math.PI * j) / M;
                double a1 = (2.0 * Math.PI * ((j + 1) % M)) / M;
                double cos0 = Math.Cos(a0), sin0 = Math.Sin(a0);
                double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1);

                var v00 = new float[] { (float)x0, (float)(r0 * cos0), (float)(r0 * sin0) };
                var v01 = new float[] { (float)x0, (float)(r0 * cos1), (float)(r0 * sin1) };
                var v10 = new float[] { (float)x1, (float)(r1 * cos0), (float)(r1 * sin0) };
                var v11 = new float[] { (float)x1, (float)(r1 * cos1), (float)(r1 * sin1) };

                // Two triangles per quad, outward-facing (right-hand rule).
                tris.Add(MakeTriangle(v00, v10, v11));
                tris.Add(MakeTriangle(v00, v11, v01));
            }
        }
        int shellTris = tris.Count;

        // ── End caps at x = 0 (injector face) and x = total (exit) ─
        // Cap with a fan to (x, 0, 0). The inner-bore cavity is not
        // rendered — the preview is a solid-looking surface.
        int injFlangeTris = 0;
        int mountFlangeTris = 0;
        double r0Cap  = rOuter[0];
        double rEndCap = rOuter[N - 1];
        double x0Cap = stations[0].X_mm;
        double xEnd  = stations[N - 1].X_mm;
        AppendAnnularDisc(tris, x0Cap, centerRadius: 0.0, outerRadius: r0Cap,
                          M, facingNegativeX: true);
        AppendAnnularDisc(tris, xEnd, centerRadius: 0.0, outerRadius: rEndCap,
                          M, facingNegativeX: false);

        // ── Injector flange ring ──────────────────────────────────
        if (opts.IncludeInjectorFlange && opts.InjectorFlangeThickness_mm > 0)
        {
            double flangeOuterR = r0Cap * Math.Max(opts.InjectorFlangeOuterRadiusFactor, 1.05);
            double xBack  = x0Cap - opts.InjectorFlangeThickness_mm;
            int before = tris.Count;
            AppendCylinder(tris, xMin: xBack, xMax: x0Cap,
                           rInner: r0Cap, rOuter: flangeOuterR, M);
            injFlangeTris = tris.Count - before;
        }

        // ── Mounting flange ring ──────────────────────────────────
        if (opts.IncludeMountingFlange && opts.MountingFlangeThickness_mm > 0)
        {
            double flangeOuterR = rEndCap * 1.15;
            double xFwd  = xEnd + opts.MountingFlangeThickness_mm;
            int before = tris.Count;
            AppendCylinder(tris, xMin: xEnd, xMax: xFwd,
                           rInner: rEndCap, rOuter: flangeOuterR, M);
            mountFlangeTris = tris.Count - before;
        }

        // ── Channel rib ridges (visual proxy only) ────────────────
        int ribTris = 0;
        if (opts.ChannelCount > 0 && opts.RibThickness_mm > 0)
        {
            int before = tris.Count;
            AppendChannelRibs(tris, stations, rOuter, M,
                              opts.ChannelCount, opts.RibThickness_mm,
                              opts.ChannelHeightAverage_mm);
            ribTris = tris.Count - before;
        }

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        double ms = (t1 - t0) / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;

        return new AnalyticalPreviewResult(
            Triangles:                    tris.ToArray(),
            ShellTriangleCount:           shellTris,
            InjectorFlangeTriangleCount:  injFlangeTris,
            MountingFlangeTriangleCount:  mountFlangeTris,
            ChannelRibTriangleCount:      ribTris,
            BuildWallMs:                  ms);
    }

    /// <summary>
    /// Convenience wrapper: build + write to disk in one call. Returns
    /// the build result so callers can inspect triangle counts + timing.
    /// </summary>
    public static AnalyticalPreviewResult BuildAndWrite(
        AnalyticalPreviewOptions opts, string outPath, string headerTag = "")
    {
        var result = Build(opts);
        // PR-2 namespace rename (2026-04-30): default STL header tag
        // kept as the literal "RegenChamberDesigner analytical preview"
        // so existing STL files round-trip without forcing consumers
        // that key off the header to update.
        StlWelder.Write(outPath, result.Triangles,
            headerTag: string.IsNullOrEmpty(headerTag)
                ? "RegenChamberDesigner analytical preview"
                : headerTag);
        return result;
    }

    // ───────────────────────── helpers ─────────────────────────

    private static StlTriangle MakeTriangle(float[] a, float[] b, float[] c)
    {
        // Compute outward normal via cross product (right-hand rule).
        float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
        float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
        float nx = uy * vz - uz * vy;
        float ny = uz * vx - ux * vz;
        float nz = ux * vy - uy * vx;
        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len > 1e-12f) { nx /= len; ny /= len; nz /= len; }
        return new StlTriangle(
            nx, ny, nz,
            a[0], a[1], a[2],
            b[0], b[1], b[2],
            c[0], c[1], c[2]);
    }

    /// <summary>
    /// Emit an annular disc as a triangle fan at constant x. If
    /// <paramref name="centerRadius"/> is 0 the disc is a full cap.
    /// </summary>
    private static void AppendAnnularDisc(
        List<StlTriangle> tris, double x, double centerRadius, double outerRadius,
        int M, bool facingNegativeX)
    {
        for (int j = 0; j < M; j++)
        {
            double a0 = (2.0 * Math.PI * j) / M;
            double a1 = (2.0 * Math.PI * ((j + 1) % M)) / M;
            double cos0 = Math.Cos(a0), sin0 = Math.Sin(a0);
            double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1);

            var vo0 = new float[] { (float)x, (float)(outerRadius * cos0), (float)(outerRadius * sin0) };
            var vo1 = new float[] { (float)x, (float)(outerRadius * cos1), (float)(outerRadius * sin1) };

            if (centerRadius <= 1e-6)
            {
                var vc = new float[] { (float)x, 0f, 0f };
                // Winding: outward normal points along ±x depending on cap face.
                if (facingNegativeX) tris.Add(MakeTriangle(vc, vo1, vo0));
                else                  tris.Add(MakeTriangle(vc, vo0, vo1));
            }
            else
            {
                var vi0 = new float[] { (float)x, (float)(centerRadius * cos0), (float)(centerRadius * sin0) };
                var vi1 = new float[] { (float)x, (float)(centerRadius * cos1), (float)(centerRadius * sin1) };
                if (facingNegativeX)
                {
                    tris.Add(MakeTriangle(vi0, vo1, vo0));
                    tris.Add(MakeTriangle(vi0, vi1, vo1));
                }
                else
                {
                    tris.Add(MakeTriangle(vi0, vo0, vo1));
                    tris.Add(MakeTriangle(vi0, vo1, vi1));
                }
            }
        }
    }

    /// <summary>
    /// Emit an annular-cross-section cylinder: inner + outer revolving
    /// walls + front + back end-rings. Used for flange geometry.
    /// </summary>
    private static void AppendCylinder(
        List<StlTriangle> tris, double xMin, double xMax,
        double rInner, double rOuter, int M)
    {
        for (int j = 0; j < M; j++)
        {
            double a0 = (2.0 * Math.PI * j) / M;
            double a1 = (2.0 * Math.PI * ((j + 1) % M)) / M;
            double cos0 = Math.Cos(a0), sin0 = Math.Sin(a0);
            double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1);

            // Outer wall (faces outward).
            var vout00 = new float[] { (float)xMin, (float)(rOuter * cos0), (float)(rOuter * sin0) };
            var vout01 = new float[] { (float)xMin, (float)(rOuter * cos1), (float)(rOuter * sin1) };
            var vout10 = new float[] { (float)xMax, (float)(rOuter * cos0), (float)(rOuter * sin0) };
            var vout11 = new float[] { (float)xMax, (float)(rOuter * cos1), (float)(rOuter * sin1) };
            tris.Add(MakeTriangle(vout00, vout10, vout11));
            tris.Add(MakeTriangle(vout00, vout11, vout01));

            // Inner wall (faces inward).
            var vin00 = new float[] { (float)xMin, (float)(rInner * cos0), (float)(rInner * sin0) };
            var vin01 = new float[] { (float)xMin, (float)(rInner * cos1), (float)(rInner * sin1) };
            var vin10 = new float[] { (float)xMax, (float)(rInner * cos0), (float)(rInner * sin0) };
            var vin11 = new float[] { (float)xMax, (float)(rInner * cos1), (float)(rInner * sin1) };
            tris.Add(MakeTriangle(vin00, vin11, vin10));
            tris.Add(MakeTriangle(vin00, vin01, vin11));

            // Back end ring (x=xMin, facing -X).
            tris.Add(MakeTriangle(vin00, vout01, vout00));
            tris.Add(MakeTriangle(vin00, vin01, vout01));

            // Front end ring (x=xMax, facing +X).
            tris.Add(MakeTriangle(vin10, vout10, vout11));
            tris.Add(MakeTriangle(vin10, vout11, vin11));
        }
    }

    /// <summary>
    /// Visual proxy for regen channels: raised radial ribs on the
    /// outer shell. Ribs are small rectangular prisms at evenly
    /// spaced azimuths that taper with the contour. NOT geometrically
    /// subtractive — the preview skips boolean ops by design.
    /// </summary>
    private static void AppendChannelRibs(
        List<StlTriangle> tris, ContourStation[] stations, double[] rOuter,
        int M, int channelCount, double ribThickness_mm, double channelHeight_mm)
    {
        if (channelCount <= 0) return;
        int N = stations.Length;
        if (N < 2) return;

        double ribHalfAngular =
            (ribThickness_mm * 0.5)
            / Math.Max(rOuter[0], 1e-3); // rough half-angle subtended by rib
        double ribOuterExtent_mm = Math.Max(channelHeight_mm * 0.25, 0.2);

        for (int k = 0; k < channelCount; k++)
        {
            double centerAngle = (2.0 * Math.PI * k) / channelCount;
            double aLeft  = centerAngle - ribHalfAngular;
            double aRight = centerAngle + ribHalfAngular;
            double cosL = Math.Cos(aLeft),  sinL = Math.Sin(aLeft);
            double cosR = Math.Cos(aRight), sinR = Math.Sin(aRight);

            // Two long triangle strips along the chamber: inner rib face
            // on outer shell, outer rib face at rOuter + extent.
            for (int i = 0; i < N - 1; i++)
            {
                double x0 = stations[i].X_mm;
                double x1 = stations[i + 1].X_mm;
                double rI0 = rOuter[i];
                double rI1 = rOuter[i + 1];
                double rO0 = rI0 + ribOuterExtent_mm;
                double rO1 = rI1 + ribOuterExtent_mm;

                var a = new float[] { (float)x0, (float)(rO0 * cosL), (float)(rO0 * sinL) };
                var b = new float[] { (float)x0, (float)(rO0 * cosR), (float)(rO0 * sinR) };
                var c = new float[] { (float)x1, (float)(rO1 * cosL), (float)(rO1 * sinL) };
                var d = new float[] { (float)x1, (float)(rO1 * cosR), (float)(rO1 * sinR) };
                // Rib outer (top) face
                tris.Add(MakeTriangle(a, c, d));
                tris.Add(MakeTriangle(a, d, b));
                // Rib side faces would double triangle count; skip them —
                // preview only cares about seeing ribs exist, count-wise.
            }
        }
    }
}
