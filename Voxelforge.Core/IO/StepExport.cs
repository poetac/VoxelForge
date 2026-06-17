// StepExport.cs — Pure C# STEP AP214 exporter for the chamber analytical solid.
//
// Writes ISO 10303-21 (STEP) with AUTOMOTIVE_DESIGN schema containing a
// MANIFOLD_SOLID_BREP built from:
//   • Inner lateral face  — SURFACE_OF_REVOLUTION of the gas-side meridian
//   • Outer lateral face  — SURFACE_OF_REVOLUTION of the jacket-outer meridian
//   • Start endcap        — PLANE annular face at X = 0 (injector face)
//   • Exit endcap         — PLANE annular face at X = TotalLength_mm
//
// The solid does NOT include channel micro-geometry (that lives in the STL/3MF
// voxel-build pipeline). It gives LPBF service bureaus an analytic envelope for
// support generation, CMM inspection, and tolerance-stack analysis.
//
// No external NuGet packages — zero new dependencies on Core. Follows the same
// from-scratch pattern as ThreeMFExport.cs and CfdFieldExport.cs.
// Apache 2.0 clean (avoids OpenCASCADE LGPL-2.1 binding).
//
// References:
//   ISO 10303-1  (Overview)
//   ISO 10303-21 (Physical file format, STEP-ASCII)
//   ISO 10303-42 (Geometric and topological representation — AUTOMOTIVE_DESIGN)
//   Pratt, "Introduction to ISO 10303 — the STEP standard for product data exchange"
//   J. STEP Tools, Inc., STEP Application Handbook, Rev 3.0 (2006)

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Chamber;
using Voxelforge.Optimization;

namespace Voxelforge.IO;

/// <summary>
/// Statistics returned by a successful STEP export.
/// </summary>
public sealed record StepExportStats(
    int EntityCount,
    int ProfileStationCount,
    long FileBytes,
    double ElapsedMs);

/// <summary>
/// Writes a STEP AP214 solid representing the chamber analytical geometry
/// (inner + outer wall surfaces of revolution + two annular endcaps).
/// </summary>
public static class StepExport
{
    public const string ExportSchemaVersion = "v1.0 (2026-04-30)";

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Write a STEP AP214 solid from a pre-computed <paramref name="innerContour"/>
    /// and the <paramref name="design"/> that provides wall-thickness parameters.
    /// </summary>
    /// <param name="stepPath">Destination file path (created / overwritten).</param>
    /// <param name="innerContour">Gas-side meridian contour.</param>
    /// <param name="design">Design record; supplies GasSideWallThickness, channel
    ///   heights, and OuterJacketThickness for the outer profile offset.</param>
    /// <param name="gitSha">Optional 40-hex SHA embedded in FILE_DESCRIPTION.
    ///   Pass null to omit (useful for deterministic unit-test output).</param>
    /// <param name="gateManifest">Optional gate-pass string from
    ///   <see cref="ExportMetadata.GatePassManifest"/>. Pass null to omit.</param>
    public static StepExportStats SaveFromContour(
        string stepPath,
        ChamberContour innerContour,
        RegenChamberDesign design,
        string? gitSha = null,
        string? gateManifest = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepPath);
        ArgumentNullException.ThrowIfNull(innerContour);
        ArgumentNullException.ThrowIfNull(design);

        if (innerContour.Stations.Length < 2)
            throw new ArgumentException("Contour must have at least 2 stations.", nameof(innerContour));

        var sw = Stopwatch.StartNew();

        // Build outer profile (radial offset of inner profile by wall-stack depth).
        double[] outerR = BuildOuterProfile(innerContour, design);

        var writer = new StepWriter();
        int entityCount = BuildEntities(writer, innerContour, outerR, gitSha, gateManifest, stepPath);

        Directory.CreateDirectory(Path.GetDirectoryName(stepPath) ?? ".");
        writer.WriteFile(stepPath);

        sw.Stop();
        var fi = new FileInfo(stepPath);
        return new StepExportStats(entityCount, innerContour.Stations.Length, fi.Length, sw.Elapsed.TotalMilliseconds);
    }

    // -----------------------------------------------------------------------
    // Outer-profile computation
    // -----------------------------------------------------------------------

    private static double[] BuildOuterProfile(ChamberContour c, RegenChamberDesign d)
    {
        var stations = c.Stations;
        int n = stations.Length;
        var outerR = new double[n];

        // Identify axial extents for the Converging and Bell sections
        // so we can linearly interpolate channel height within each.
        double xConvStart = c.ChamberLength_mm; // converging begins here (approx)
        double xThroat    = stations[c.ThroatIndex].X_mm;
        double xTotal     = c.TotalLength_mm;

        double hChamber = d.ChannelHeightChamber_mm;
        double hThroat  = d.ChannelHeightThroat_mm;
        double hExit    = d.ChannelHeightExit_mm;

        for (int i = 0; i < n; i++)
        {
            double channelH = stations[i].Region switch
            {
                ChamberRegion.Barrel =>
                    hChamber,
                ChamberRegion.Converging =>
                    Lerp(hChamber, hThroat,
                        Clamp01((stations[i].X_mm - xConvStart) / Math.Max(xThroat - xConvStart, 1e-9))),
                ChamberRegion.ThroatArc =>
                    hThroat,
                // All bell sub-regions lerp from throat to exit.
                _ =>
                    Lerp(hThroat, hExit,
                        Clamp01((stations[i].X_mm - xThroat) / Math.Max(xTotal - xThroat, 1e-9)))
            };

            outerR[i] = stations[i].R_mm
                        + d.GasSideWallThickness_mm
                        + channelH
                        + d.OuterJacketThickness_mm;
        }

        return outerR;
    }

    private static double Lerp(double a, double b, double t) => a + t * (b - a);
    private static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    // -----------------------------------------------------------------------
    // Entity graph construction
    // -----------------------------------------------------------------------

    private static int BuildEntities(
        StepWriter w,
        ChamberContour c,
        double[] outerR,
        string? gitSha,
        string? gateManifest,
        string stepPath)
    {
        var stations = c.Stations;
        int n = stations.Length;

        // ---- Application context boilerplate ----
        int appCtx = w.Emit("APPLICATION_CONTEXT('core data for automotive mechanical design processes')");
        int appDef = w.Emit($"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{appCtx})");

        // ---- Units (mm, radians, steradians) ----
        int lenUnit = w.Emit("(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.))");
        int angUnit = w.Emit("(NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.))");
        int saUnit  = w.Emit("(NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT())");
        int uncert  = w.Emit($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.0E-07),#{lenUnit},'distance_accuracy_value','Maximum Tolerance applied to model')");

        // ---- Product identity ----
        int pCtx = w.Emit($"PRODUCT_CONTEXT('',#{appCtx},'mechanical')");

        string metaDesc = BuildMetaDescription(gitSha, gateManifest);
        int prod = w.Emit($"PRODUCT('chamber','Voxelforge Chamber','{metaDesc}',(#{pCtx}))");
        int pForm = w.Emit($"PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE('','',#{prod},.NOT_KNOWN.)");
        int pdCtx = w.Emit($"PRODUCT_DEFINITION_CONTEXT('part definition',#{appCtx},'design')");
        int pDef  = w.Emit($"PRODUCT_DEFINITION('design','',#{pForm},#{pdCtx})");
        int pShape= w.Emit($"PRODUCT_DEFINITION_SHAPE('','',#{pDef})");

        // ---- Representation context (mm units) ----
        int repCtx = w.Emit(
            $"( GEOMETRIC_REPRESENTATION_CONTEXT(3) " +
            $"GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{uncert})) " +
            $"GLOBAL_UNIT_ASSIGNED_CONTEXT((#{lenUnit},#{angUnit},#{saUnit})) " +
            $"REPRESENTATION_CONTEXT('VFX1','3D SPACE') )");

        // ---- Shared axis/direction primitives ----
        int origin    = w.EmitCartesianPoint(0, 0, 0);
        int xDir      = w.EmitDirection(1, 0, 0);  // revolution axis = X
        int yDir      = w.EmitDirection(0, 1, 0);  // reference direction for circles
        int negXDir   = w.EmitDirection(-1, 0, 0);
        int posXDir   = xDir;                       // alias for clarity

        // AXIS1_PLACEMENT for SURFACE_OF_REVOLUTION — shared by inner & outer
        int revAxis1  = w.Emit($"AXIS1_PLACEMENT('',#{origin},#{xDir})");

        // ---- Inner profile POLYLINE ----
        // Profile points lie in the XY half-plane: (X_mm, R_mm, 0).
        int[] innerPts = new int[n];
        for (int i = 0; i < n; i++)
            innerPts[i] = w.EmitCartesianPoint(stations[i].X_mm, stations[i].R_mm, 0.0);
        int innerPolyline = w.EmitPolyline(innerPts);

        // ---- Outer profile POLYLINE ----
        int[] outerPts = new int[n];
        for (int i = 0; i < n; i++)
            outerPts[i] = w.EmitCartesianPoint(stations[i].X_mm, outerR[i], 0.0);
        int outerPolyline = w.EmitPolyline(outerPts);

        // ---- SURFACE_OF_REVOLUTION (inner and outer) ----
        int innerSOR = w.Emit($"SURFACE_OF_REVOLUTION('',#{innerPolyline},#{revAxis1})");
        int outerSOR = w.Emit($"SURFACE_OF_REVOLUTION('',#{outerPolyline},#{revAxis1})");

        // ---- Seam edge LINE geometry ----
        // The seam lies at u=0 (reference direction = +Y), so seam points are
        // the profile points themselves (same X, Y=R, Z=0).
        // We use a VECTOR from the first to the last profile point for the seam line direction.
        // Each seam edge is represented as a LINE_CURVE (start point + direction vector).
        // Since it's a multi-segment polyline we reuse the POLYLINE entity as the seam curve.
        // In AP214, a POLYLINE is a valid CURVE for EDGE_CURVE geometry.
        int innerSeamCurve = innerPolyline; // reuse the same POLYLINE at u=0
        int outerSeamCurve = outerPolyline;

        // ---- Seam vertex points (at start and exit of each seam) ----
        // The seam vertex lies at (X, R, 0) — the point where the profile curve
        // intersects the reference half-plane (u=0).
        int vInnerStart_cp = innerPts[0];      // inner, injector end
        int vInnerExit_cp  = innerPts[n - 1];  // inner, exit end
        int vOuterStart_cp = outerPts[0];
        int vOuterExit_cp  = outerPts[n - 1];

        int vInnerStart = w.Emit($"VERTEX_POINT('',#{vInnerStart_cp})");
        int vInnerExit  = w.Emit($"VERTEX_POINT('',#{vInnerExit_cp})");
        int vOuterStart = w.Emit($"VERTEX_POINT('',#{vOuterStart_cp})");
        int vOuterExit  = w.Emit($"VERTEX_POINT('',#{vOuterExit_cp})");

        // ---- Seam EDGE_CURVEs ----
        // Run from seam-start vertex to seam-exit vertex along the meridian.
        int innerSeamEdge = w.Emit($"EDGE_CURVE('',#{vInnerStart},#{vInnerExit},#{innerSeamCurve},.T.)");
        int outerSeamEdge = w.Emit($"EDGE_CURVE('',#{vOuterStart},#{vOuterExit},#{outerSeamCurve},.T.)");

        // ---- Circle edges — 4 total ----
        // Each circle is a full 360° closed loop with start vertex = end vertex (the seam vertex).
        // Circles lie in the YZ plane (normal = X-axis) at their respective axial positions.

        double rInnerStart = stations[0].R_mm;
        double rInnerExit  = stations[n - 1].R_mm;
        double rOuterStart = outerR[0];
        double rOuterExit  = outerR[n - 1];
        double xStart      = stations[0].X_mm;
        double xExit       = stations[n - 1].X_mm;

        int circInnerStart = EmitCircle(w, xStart, rInnerStart, xDir, yDir);
        int circInnerExit  = EmitCircle(w, xExit,  rInnerExit,  xDir, yDir);
        int circOuterStart = EmitCircle(w, xStart, rOuterStart, xDir, yDir);
        int circOuterExit  = EmitCircle(w, xExit,  rOuterExit,  xDir, yDir);

        // EDGE_CURVEs for circles (closed: start vertex == end vertex).
        int ecInnerStart = w.Emit($"EDGE_CURVE('',#{vInnerStart},#{vInnerStart},#{circInnerStart},.T.)");
        int ecInnerExit  = w.Emit($"EDGE_CURVE('',#{vInnerExit}, #{vInnerExit}, #{circInnerExit}, .T.)");
        int ecOuterStart = w.Emit($"EDGE_CURVE('',#{vOuterStart},#{vOuterStart},#{circOuterStart},.T.)");
        int ecOuterExit  = w.Emit($"EDGE_CURVE('',#{vOuterExit}, #{vOuterExit}, #{circOuterExit}, .T.)");

        // ---- Plane surfaces for endcaps ----
        int startPlane = EmitPlane(w, xStart, negXDir, yDir);  // normal = -X (outward from injector face)
        int exitPlane  = EmitPlane(w, xExit,  posXDir, yDir);  // normal = +X (outward from exit face)

        // ---- Build the 4 faces ----
        //
        // Face 1: Outer lateral face
        //   EDGE_LOOP: [OC_outer_start .T., outer_seam .T., OC_outer_exit .F., outer_seam .F.]
        int outerLateralFace = BuildLateralFace(w,
            ecOuterStart, outerSeamEdge, ecOuterExit,
            surfaceId: outerSOR, sameSense: true);

        // Face 2: Inner lateral face
        //   Same pattern but sameSense=false (normal points inward toward solid interior).
        int innerLateralFace = BuildLateralFace(w,
            ecInnerStart, innerSeamEdge, ecInnerExit,
            surfaceId: innerSOR, sameSense: false);

        // Face 3: Start endcap (annular ring at X=xStart)
        //   Outer bound = outer start circle (reversed so normal = -X outward)
        //   Inner bound = inner start circle (forward, hole)
        int startFace = BuildEndcapFace(w,
            outerCircleEdge: ecOuterStart, outerCircleForward: false,
            innerCircleEdge: ecInnerStart, innerCircleForward: true,
            planeId: startPlane, sameSense: true);

        // Face 4: Exit endcap (annular ring at X=xExit)
        //   Outer bound = outer exit circle (forward so normal = +X outward)
        //   Inner bound = inner exit circle (reversed, hole)
        int exitFace = BuildEndcapFace(w,
            outerCircleEdge: ecOuterExit, outerCircleForward: true,
            innerCircleEdge: ecInnerExit, innerCircleForward: false,
            planeId: exitPlane, sameSense: true);

        // ---- CLOSED_SHELL and MANIFOLD_SOLID_BREP ----
        int shell  = w.Emit($"CLOSED_SHELL('',(#{outerLateralFace},#{innerLateralFace},#{startFace},#{exitFace}))");
        int solid  = w.Emit($"MANIFOLD_SOLID_BREP('chamber',#{shell})");

        // ---- ADVANCED_BREP_SHAPE_REPRESENTATION ----
        int abrep  = w.Emit($"ADVANCED_BREP_SHAPE_REPRESENTATION('',(#{solid}),#{repCtx})");

        // ---- Shape definition representation link ----
        w.Emit($"SHAPE_DEFINITION_REPRESENTATION(#{pShape},#{abrep})");

        _ = appDef; // referenced in DATA block

        return w.EntityCount;
    }

    // -----------------------------------------------------------------------
    // Face builders
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a lateral ADVANCED_FACE bounded by a start circle, a seam edge (forward),
    /// an exit circle (reversed), and the seam edge again (reversed).
    /// This is the standard AP214 topology for a full 360° surface of revolution.
    /// </summary>
    private static int BuildLateralFace(
        StepWriter w,
        int startCircleEdge,
        int seamEdge,
        int exitCircleEdge,
        int surfaceId,
        bool sameSense)
    {
        int oe1 = w.Emit($"ORIENTED_EDGE('',*,*,#{startCircleEdge},.T.)");
        int oe2 = w.Emit($"ORIENTED_EDGE('',*,*,#{seamEdge},.T.)");
        int oe3 = w.Emit($"ORIENTED_EDGE('',*,*,#{exitCircleEdge},.F.)");
        int oe4 = w.Emit($"ORIENTED_EDGE('',*,*,#{seamEdge},.F.)");
        int loop = w.Emit($"EDGE_LOOP('',(#{oe1},#{oe2},#{oe3},#{oe4}))");
        int bound = w.Emit($"FACE_OUTER_BOUND('',#{loop},.T.)");
        return w.Emit($"ADVANCED_FACE('',(#{bound}),#{surfaceId},.{BoolStr(sameSense)}.)");
    }

    /// <summary>
    /// Build an annular ADVANCED_FACE on a PLANE with one outer circle bound
    /// and one inner circle hole bound.
    /// </summary>
    private static int BuildEndcapFace(
        StepWriter w,
        int outerCircleEdge, bool outerCircleForward,
        int innerCircleEdge, bool innerCircleForward,
        int planeId,
        bool sameSense)
    {
        // Outer boundary (FACE_OUTER_BOUND)
        int oeOuter = w.Emit($"ORIENTED_EDGE('',*,*,#{outerCircleEdge},.{BoolStr(outerCircleForward)}.)");
        int outerLoop  = w.Emit($"EDGE_LOOP('',(#{oeOuter}))");
        int outerBound = w.Emit($"FACE_OUTER_BOUND('',#{outerLoop},.T.)");

        // Inner boundary (FACE_BOUND — the hole, orientation .F. = inward)
        int oeInner = w.Emit($"ORIENTED_EDGE('',*,*,#{innerCircleEdge},.{BoolStr(innerCircleForward)}.)");
        int innerLoop  = w.Emit($"EDGE_LOOP('',(#{oeInner}))");
        int innerBound = w.Emit($"FACE_BOUND('',#{innerLoop},.F.)");

        return w.Emit($"ADVANCED_FACE('',(#{outerBound},#{innerBound}),#{planeId},.{BoolStr(sameSense)}.)");
    }

    // -----------------------------------------------------------------------
    // Geometry helpers
    // -----------------------------------------------------------------------

    /// <summary>Emit a CIRCLE at the given axial position with AXIS2_PLACEMENT_3D.</summary>
    private static int EmitCircle(StepWriter w, double x, double r, int axisDir, int refDir)
    {
        int pt  = w.EmitCartesianPoint(x, 0, 0);
        int a2p = w.Emit($"AXIS2_PLACEMENT_3D('',#{pt},#{axisDir},#{refDir})");
        return w.Emit($"CIRCLE('',#{a2p},{F(r)})");
    }

    /// <summary>Emit a PLANE with AXIS2_PLACEMENT_3D.</summary>
    private static int EmitPlane(StepWriter w, double x, int normalDir, int refDir)
    {
        int pt  = w.EmitCartesianPoint(x, 0, 0);
        int a2p = w.Emit($"AXIS2_PLACEMENT_3D('',#{pt},#{normalDir},#{refDir})");
        return w.Emit($"PLANE('',#{a2p})");
    }

    // -----------------------------------------------------------------------
    // Utilities
    // -----------------------------------------------------------------------

    private static string BuildMetaDescription(string? gitSha, string? gateManifest)
    {
        var parts = new List<string>
        {
            $"schema={ExportMetadata.SchemaVersion}"
        };
        if (gitSha is not null)
            parts.Add($"git={gitSha[..Math.Min(7, gitSha.Length)]}");
        if (gateManifest is not null)
            parts.Add($"gates={gateManifest}");
        return string.Join(" | ", parts);
    }

    private static string BoolStr(bool v) => v ? "T" : "F";

    /// <summary>Format a double in STEP-compatible decimal notation.</summary>
    private static string F(double v) =>
        v.ToString("G8", CultureInfo.InvariantCulture);

    // -----------------------------------------------------------------------
    // StepWriter — sequential entity ID allocation + ISO 10303-21 serialisation
    // -----------------------------------------------------------------------

    private sealed class StepWriter
    {
        private int _nextId = 1;
        private readonly StringBuilder _sb = new(1 << 20); // 1 MB initial

        public int EntityCount => _nextId - 1;

        /// <summary>Assign the next ID, append the DATA line, return the ID.</summary>
        public int Emit(string entityBody)
        {
            int id = _nextId++;
            _sb.Append('#').Append(id).Append('=').Append(entityBody).Append(";\n");
            return id;
        }

        public int EmitCartesianPoint(double x, double y, double z)
            => Emit($"CARTESIAN_POINT('',({F(x)},{F(y)},{F(z)}))");

        public int EmitDirection(double x, double y, double z)
            => Emit($"DIRECTION('',({F(x)},{F(y)},{F(z)}))");

        public int EmitPolyline(int[] pointIds)
        {
            var refs = string.Join(",", pointIds.Select(id => $"#{id}"));
            return Emit($"POLYLINE('',({refs}))");
        }

        /// <summary>Write the complete ISO 10303-21 file to disk.</summary>
        public void WriteFile(string path)
        {
            using var writer = new StreamWriter(path, append: false, Encoding.ASCII);

            writer.WriteLine("ISO-10303-21;");
            writer.WriteLine("HEADER;");
            writer.WriteLine($"FILE_DESCRIPTION(('Voxelforge Chamber STEP Export {ExportSchemaVersion}'),'2;1');");
            // Omit path from FILE_NAME so file content is path-independent (deterministic).
            writer.WriteLine($"FILE_NAME('','',(''),(''),'Voxelforge STEP Export {ExportSchemaVersion}','','');");
            writer.WriteLine("FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));");
            writer.WriteLine("ENDSEC;");
            writer.WriteLine("DATA;");
            writer.Write(_sb);
            writer.WriteLine("ENDSEC;");
            writer.Write("END-ISO-10303-21;");
        }

        private static string F(double v) =>
            v.ToString("G8", CultureInfo.InvariantCulture);
    }
}
