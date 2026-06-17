// InjectorFaceImport.cs — Load an external STL and union it into the
// chamber as the injector face plate.
//
// Usage: the user points at a binary STL of their injector element pattern
// (coaxial, impinging, pintle, or whatever arrangement their injector team
// has designed elsewhere — FreeCAD, SolidWorks, Onshape, PrintLab, etc.).
//
// The STL's bounding box is examined. The user specifies where the "front"
// of their STL (the face that touches combustion) sits in the chamber's
// coordinate system — typically at x = −flangeThickness so the injector
// face seats flush with the injector flange's upstream face.
//
// We scale (if requested) and translate the mesh once, voxelize it, then
// BoolAdd it to the chamber body. No boolean cuts are performed through
// the resulting combined shape — if the STL already has drilled orifices,
// those will simply be voids in the final voxel body (correct behaviour).
//
// Safety rules enforced:
//   • STL that voxelizes to an empty Voxels is silently skipped with a
//     warning (unlikely to be what the user wanted).
//   • STL whose bounding box is entirely outside the render bounds is
//     skipped (the injector wasn't translated into the chamber domain).
//   • No attempt is made to clip or repair the STL — it's the user's job
//     to deliver a watertight mesh. PicoGK's voxelizer will accept some
//     non-manifold input but results are not guaranteed.
//
// Nothing in this module runs on the UI thread; it's called from the task
// thread like every other voxel op.

using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

// InjectorFaceImportOptions was moved to Voxelforge.Core/Geometry/
// InjectorFaceImportOptions.cs as part of ADR-021 (Sprint A-3). The
// PicoGK-typed result + the static class remain here.

public sealed record InjectorFaceImportResult(
    Voxels? Voxels,                     // null if skipped
    string  Message,
    int     TriangleCount,
    Vector3 PlacedMinCorner,
    Vector3 PlacedMaxCorner);

public static class InjectorFaceImport
{
    public static InjectorFaceImportResult Load(
        InjectorFaceImportOptions opt,
        BBox3 chamberBounds)
    {
        if (!opt.Enabled || string.IsNullOrWhiteSpace(opt.StlPath))
            return new InjectorFaceImportResult(null, "Injector STL disabled.", 0,
                Vector3.Zero, Vector3.Zero);

        if (!System.IO.File.Exists(opt.StlPath))
            return new InjectorFaceImportResult(null,
                $"STL file not found: {opt.StlPath}", 0, Vector3.Zero, Vector3.Zero);

        Mesh mesh;
        try
        {
            mesh = Mesh.mshFromStlFile(opt.StlPath);
        }
        catch (Exception ex)
        {
            return new InjectorFaceImportResult(null,
                $"STL load failed: {ex.Message}", 0, Vector3.Zero, Vector3.Zero);
        }

        int nTri = mesh.nTriangleCount();
        if (nTri == 0)
            return new InjectorFaceImportResult(null, "STL has zero triangles.", 0,
                Vector3.Zero, Vector3.Zero);

        // Compute bounding box of mesh to determine translation.
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < nTri; i++)
        {
            mesh.GetTriangle(i, out Vector3 a, out Vector3 b, out Vector3 c);
            min = Vector3.Min(min, a); min = Vector3.Min(min, b); min = Vector3.Min(min, c);
            max = Vector3.Max(max, a); max = Vector3.Max(max, b); max = Vector3.Max(max, c);
        }

        float sx = (float)opt.UniformScale;
        float sy = (float)opt.UniformScale;
        float sz = (float)opt.UniformScale;

        // Compute offset: minX of scaled mesh should land at opt.OffsetX_mm.
        float tx = (float)opt.OffsetX_mm - min.X * sx;
        float ty = opt.AutoCenterYZ ? -0.5f * (min.Y + max.Y) * sy : 0f;
        float tz = opt.AutoCenterYZ ? -0.5f * (min.Z + max.Z) * sz : 0f;

        var scale = new Vector3(sx, sy, sz);
        var offset = new Vector3(tx, ty, tz);

        var transformed = mesh.mshCreateTransformed(scale, offset);

        Vector3 placedMin = new(min.X * sx + tx, min.Y * sy + ty, min.Z * sz + tz);
        Vector3 placedMax = new(max.X * sx + tx, max.Y * sy + ty, max.Z * sz + tz);

        // Sanity-check overlap with chamber bounds.
        bool anyInside = !(placedMax.X < chamberBounds.vecMin.X || placedMin.X > chamberBounds.vecMax.X
                        || placedMax.Y < chamberBounds.vecMin.Y || placedMin.Y > chamberBounds.vecMax.Y
                        || placedMax.Z < chamberBounds.vecMin.Z || placedMin.Z > chamberBounds.vecMax.Z);
        if (!anyInside)
            return new InjectorFaceImportResult(null,
                "STL placed entirely outside chamber bounds — check OffsetX/AutoCenterYZ.",
                nTri, placedMin, placedMax);

        Voxels vox;
        try
        {
            vox = new Voxels(transformed);
        }
        catch (Exception ex)
        {
            return new InjectorFaceImportResult(null,
                $"Voxelization failed: {ex.Message}", nTri, placedMin, placedMax);
        }

        vox.CalculateProperties(out float volume, out _);
        if (volume < 1.0f)
            return new InjectorFaceImportResult(null,
                $"Voxelized STL has near-zero volume ({volume:F2} mm³) — check scale.",
                nTri, placedMin, placedMax);

        return new InjectorFaceImportResult(
            Voxels: vox,
            Message: $"Loaded {nTri:N0} triangles, voxel volume {volume:F0} mm³, "
                   + $"placed at X∈[{placedMin.X:F1},{placedMax.X:F1}].",
            TriangleCount: nTri,
            PlacedMinCorner: placedMin,
            PlacedMaxCorner: placedMax);
    }
}
