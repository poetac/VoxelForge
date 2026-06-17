// LinearAerospikeImplicits.cs — Sprint 26 follow-on (2026-04-24):
// SDF primitives for the linear (extruded-rectangular) aerospike
// topology. Sibling file to AerospikeImplicits.cs — kept separate so
// the linear pipeline can evolve without touching the axisymmetric
// primitives, and so the ownership boundary matches the one-branch-
// per-file rule that Sprint 21's CycleSolver refactor established.
//
// Three primitives
// ────────────────
//   • RectangularPlugImplicit — extrusion-of-revolution along ±Z of
//     the plug half-height profile h(x) taken from
//     AerospikeContour.Stations[i].R_inner_mm. Plug cross-section at
//     each axial station is a rectangle
//       |y| ≤ h(x)   AND   |z| ≤ W/2
//     where W = contour.PlugWidth_mm. Inside the plug body the SDF is
//     negative; outside positive. Handles the flat-base truncation
//     the same way RevolvedPlugImplicit does (cap at xMax).
//   • RectangularThroatSlotsImplicit — the two slots flanking the plug
//     at the throat plane. Each slot is a thin rectangular prism
//     between plug-half-height + gap and plug-half-height + gap +
//     slot-height, spanning the plug transverse width. Used as the
//     mass source that keeps the linear-aerospike chamber closed at
//     the throat before the plume exits between the two slots.
//     Currently a cosmetic placeholder — the voxelised engine shell
//     does not model the rectangular combustion chamber manifold (a
//     Sprint-28+ follow-on once a real XRS-2200-class design is on
//     the roadmap).
//   • LinearAerospikeAssemblyImplicit — composite SDF that unions the
//     pre-throat chamber (kept circular for Sprint-26 scope
//     containment, matches BuildLinearPhysicsOnly's assumption) with
//     the plug. One voxelise, one STL body.
//
// Distance quality
// ────────────────
// Same convention as AerospikeImplicits.cs and ChamberImplicits.cs:
// inside the solid phase the SDF is negative; outside the solid
// phase the SDF is a conservative upper bound on the Euclidean
// distance to the surface. PicoGK's marching-cubes meshing picks up
// the surface from the sign crossing; magnitude quality only matters
// within ±voxel of the surface.

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;

namespace Voxelforge.Geometry;

/// <summary>
/// Sprint 26 follow-on (2026-04-24): extruded-rectangular plug SDF.
/// The plug's axial half-height profile h(x) is taken from the
/// supplied <see cref="AerospikeContour"/> (where the
/// <c>R_inner_mm</c> field is reinterpreted as plug half-height on
/// the <see cref="AerospikeContour.IsLinear"/>=true path). The plug
/// is extruded symmetrically in Z between −W/2 and +W/2, where
/// <c>W = contour.PlugWidth_mm</c>. Inside the plug body the SDF is
/// negative; outside positive. Plug axial extent is
/// <c>[offsetX_mm, offsetX_mm + PlugTruncatedLength_mm]</c>; the
/// trailing face is a flat truncation (orthogonal cut at x = xMax).
/// </summary>
public sealed class RectangularPlugImplicit : IImplicit
{
    private readonly float[] _x;          // axial stations
    private readonly float[] _h;          // plug half-height at each station
    private readonly float _xMin;
    private readonly float _xMax;
    private readonly float _halfWidth;    // W/2
    private readonly float _baseH;        // half-height at truncation

    public RectangularPlugImplicit(AerospikeContour contour, float offsetX_mm = 0f)
    {
        if (!contour.IsLinear)
            throw new System.ArgumentException(
                "RectangularPlugImplicit requires a linear contour "
              + "(AerospikeContour.IsLinear == true). Use RevolvedPlugImplicit for "
              + "the axisymmetric topology.",
                nameof(contour));
        if (contour.PlugWidth_mm <= 0)
            throw new System.ArgumentException(
                $"RectangularPlugImplicit requires PlugWidth_mm > 0; got {contour.PlugWidth_mm}.",
                nameof(contour));

        int n = contour.Stations.Length;
        _x = new float[n];
        _h = new float[n];
        for (int i = 0; i < n; i++)
        {
            _x[i] = (float)contour.Stations[i].X_mm + offsetX_mm;
            _h[i] = (float)contour.Stations[i].R_inner_mm;
        }
        _xMin = _x[0];
        _xMax = _x[^1];
        _halfWidth = 0.5f * (float)contour.PlugWidth_mm;
        _baseH = _h[^1];
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial position outside the plug's extent → distance to the
        // nearest end cap. Ahead of throat (x < xMin) is "outside";
        // behind truncation (x > xMax) is "outside".
        if (p.X < _xMin)
        {
            float dx = _xMin - p.X;
            float dy = MathF.Max(MathF.Abs(p.Y) - _h[0], 0f);
            float dz = MathF.Max(MathF.Abs(p.Z) - _halfWidth, 0f);
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        if (p.X > _xMax)
        {
            float dx = p.X - _xMax;
            float dy = MathF.Max(MathF.Abs(p.Y) - _baseH, 0f);
            float dz = MathF.Max(MathF.Abs(p.Z) - _halfWidth, 0f);
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Inside the plug's axial extent — rectangular cross-section
        // at this station is (2·hLocal) × (2·halfWidth). Classic axis-
        // aligned-box signed distance in (y, z) then compose with the
        // axial truncation distance for the interior case.
        float hLocal = InterpH(p.X);
        float qy = MathF.Abs(p.Y) - hLocal;
        float qz = MathF.Abs(p.Z) - _halfWidth;

        // Outside the cross-section (either qy > 0 or qz > 0): signed
        // distance is √(max(qy,0)² + max(qz,0)²) + min(max(qy,qz), 0).
        // That single-term closed form is the standard 2-D box SDF.
        float outsideY = MathF.Max(qy, 0f);
        float outsideZ = MathF.Max(qz, 0f);
        float outsideDist = MathF.Sqrt(outsideY * outsideY + outsideZ * outsideZ);
        float insideDist = MathF.Min(MathF.Max(qy, qz), 0f);
        float crossSectionSdf = outsideDist + insideDist;

        if (crossSectionSdf > 0f)
        {
            // Outside the cross-section at this station → we are
            // outside the plug entirely. The axial distance to the
            // plug surface is the cross-section SDF (the plug tapers
            // gently enough along x that we treat the nearest-surface
            // as "radial" at the local station).
            return crossSectionSdf;
        }

        // Inside the cross-section → inside the plug body. Distance to
        // the nearest wall is the minimum of the cross-section interior
        // SDF magnitude and the axial distance to the truncation cap.
        float dAxialToTrunc = _xMax - p.X;
        return -MathF.Min(-crossSectionSdf, dAxialToTrunc);
    }

    private float InterpH(float x)
    {
        // Axial stations are strictly ascending; linear scan is fine
        // for <= 100 stations (same rationale as RevolvedPlugImplicit
        // which also chose not to binary-search despite the P4 audit).
        for (int i = 0; i < _x.Length - 1; i++)
        {
            if (x >= _x[i] && x <= _x[i + 1])
            {
                float t = (x - _x[i]) / MathF.Max(_x[i + 1] - _x[i], 1e-6f);
                return _h[i] + t * (_h[i + 1] - _h[i]);
            }
        }
        return x < _xMin ? _h[0] : _baseH;
    }
}

/// <summary>
/// Sprint 26 follow-on (2026-04-24): composite SDF for a linear-
/// aerospike engine shell. Unions the pre-throat combustion chamber
/// (circular, consistent with <see cref="AerospikeBuilder.BuildLinearPhysicsOnly"/>
/// which sizes the chamber radius from contraction × throat-area and
/// treats it as axisymmetric for Sprint-26 scope containment) with
/// the rectangular plug. One voxelise per design, one STL body out.
/// <para>
/// The XRS-2200 has a rectangular-manifold chamber above each slot
/// rather than a circular chamber; replacing the circular chamber
/// SDF here with a rectangular one is a Sprint-28+ follow-on. Both
/// approaches produce a printable single-body STL; the circular
/// chamber just puts slightly more material aft of the throat.
/// </para>
/// </summary>
public sealed class LinearAerospikeAssemblyImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _chamberOuter;
    private readonly float _chamberXMin, _chamberXMax;
    private readonly RectangularPlugImplicit _plug;

    public LinearAerospikeAssemblyImplicit(
        RevolvedContourImplicit chamberOuter,
        float chamberXMin_mm, float chamberXMax_mm,
        RectangularPlugImplicit plug)
    {
        _chamberOuter = chamberOuter;
        _chamberXMin = chamberXMin_mm;
        _chamberXMax = chamberXMax_mm;
        _plug = plug;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float dChamber = ChamberSdf(p);
        float dPlug = _plug.fSignedDistance(p);
        return MathF.Min(dChamber, dPlug);
    }

    private float ChamberSdf(in Vector3 p)
    {
        if (p.X < _chamberXMin || p.X > _chamberXMax)
        {
            float dx = System.Math.Max(_chamberXMin - p.X, p.X - _chamberXMax);
            return dx;
        }
        return _chamberOuter.fSignedDistance(p);
    }
}
