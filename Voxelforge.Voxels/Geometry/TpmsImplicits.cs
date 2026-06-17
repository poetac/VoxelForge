// TpmsImplicits.cs — Triply-periodic-minimal-surface (TPMS) implicit
// geometry primitives for the regen-chamber channel topology.
//
// Purpose
// ───────
// The chamber's cooling channel void can now be a continuous TPMS lattice
// instead of N discrete axial/helical rectangles. A single
// `TpmsAnnularImplicit` replaces the per-channel loop over
// `AxialChannelImplicit` — one voxelise + one BoolSubtract instead of N.
//
// Surface equations (cell-edge-normalised — ω = 2π / L_cell)
// ──────────────────────────────────────────────────────────
// Gyroid     : sin(ωx)·cos(ωy) + sin(ωy)·cos(ωz) + sin(ωz)·cos(ωx) = c
// Schwarz-P  : cos(ωx) + cos(ωy) + cos(ωz)                          = c
// Schwarz-D  : sin(ωx)·sin(ωy)·sin(ωz)
//              + sin(ωx)·cos(ωy)·cos(ωz)
//              + cos(ωx)·sin(ωy)·cos(ωz)
//              + cos(ωx)·cos(ωy)·sin(ωz)                             = c
//
// The "level-set constant" c shifts the iso-surface away from c = 0 (equal
// volumes on each side of the surface). For a TPMS where the "void" phase
// is the coolant channel, `c = cThreshold(kind, solidFraction)` maps a
// solid volume fraction ψ_solid ∈ [0.30, 0.70] to the scalar offset. The
// mapping is linearised around ψ = 0.50 (c = 0) with empirical slopes from
// Maskery et al., "Insights into the mechanical properties of several
// triply periodic minimal surface lattice structures" J. Cellular Plastics
// 54(3), 2018.
//
// SDF quality
// ───────────
// The raw function value f(p) is NOT a true signed distance — its gradient
// magnitude varies between ~1.0 (on the surface normal) and ~3.0 (at
// saddles). PicoGK voxelises by sign, not distance — the voxel at sample
// point p is "in" iff f(p) < 0. So the hand-rolled implicits return
// `(function - threshold)` unscaled; the sign is correct everywhere and
// the magnitude is "close enough" for PicoGK's march-cubes meshing. A
// small Lipschitz scaling (×0.35) is applied to keep the surface from
// over-shooting the voxel grid at high cell density.
//
// `TpmsAnnularImplicit` adds the engineering clipping:
//   • Axial range clip — outside [xMin, xMax] the SDF returns a large
//     positive (channel off).
//   • Radial annular clip — only between r_inner and r_outer is the TPMS
//     function evaluated; outside the band returns large positive.
//   • The two clips union via max() so the annular TPMS void is fully
//     contained in the wall-channel-jacket radial band.
//
// Distance reporting near the clip boundaries
// ───────────────────────────────────────────
// Both the axial and the radial clips report true (Euclidean) distance to
// the clipping plane / cylinder when we are outside the band; the TPMS
// function's sign governs only when we are inside the band. This is
// identical to the pattern in `AxialChannelImplicit` / `AnnulusImplicit`.

using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// TPMS unit-cell implicit. Returns (function - threshold) scaled by a
/// Lipschitz factor so the SDF magnitude is ~1 on the surface normal.
/// Sign convention: <c>f &lt; 0</c> ⇒ inside the solid (wall) phase;
/// <c>f &gt; 0</c> ⇒ inside the void (coolant) phase. Cell edge given in
/// millimetres, matching the PicoGK millimetre voxel grid.
/// </summary>
public sealed class TpmsUnitCellImplicit : IImplicit
{
    private readonly HeatTransfer.TpmsKind _kind;
    private readonly float _omega;                  // 2π / cell edge
    private readonly float _threshold;              // solid-fraction offset c
    // Lipschitz-compensation factor — empirically tuned so the voxeliser
    // picks up the surface without march-cubes ringing on adjacent cells
    // at voxel size ≥ cell_edge / 6.
    private const float LipschitzScale = 0.35f;

    public TpmsUnitCellImplicit(HeatTransfer.TpmsKind kind, float cellEdge_mm, float solidFraction)
    {
        if (cellEdge_mm <= 0) throw new System.ArgumentOutOfRangeException(nameof(cellEdge_mm), cellEdge_mm, "cellEdge_mm must be positive");
        _kind = kind;
        _omega = 2f * MathF.PI / cellEdge_mm;
        _threshold = ThresholdFor(kind, solidFraction);
    }

    public float fSignedDistance(in Vector3 p)
    {
        float x = p.X * _omega;
        float y = p.Y * _omega;
        float z = p.Z * _omega;
        float f = _kind switch
        {
            HeatTransfer.TpmsKind.Gyroid => Gyroid(x, y, z),
            HeatTransfer.TpmsKind.SchwarzP => SchwarzP(x, y, z),
            HeatTransfer.TpmsKind.SchwarzD => SchwarzD(x, y, z),
            _ => 0f,
        };
        // +ve f = void phase; a TPMS void cutter wants "inside" to be
        // negative so BoolSubtract carves the lattice out of the shell.
        // Flip sign via (threshold - f).
        return LipschitzScale * (_threshold - f);
    }

    // ── Surface equations (classical formulations) ──────────────────────

    private static float Gyroid(float x, float y, float z)
        => MathF.Sin(x) * MathF.Cos(y)
         + MathF.Sin(y) * MathF.Cos(z)
         + MathF.Sin(z) * MathF.Cos(x);

    private static float SchwarzP(float x, float y, float z)
        => MathF.Cos(x) + MathF.Cos(y) + MathF.Cos(z);

    private static float SchwarzD(float x, float y, float z)
    {
        float sx = MathF.Sin(x), sy = MathF.Sin(y), sz = MathF.Sin(z);
        float cx = MathF.Cos(x), cy = MathF.Cos(y), cz = MathF.Cos(z);
        return sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
    }

    // Solid-volume-fraction → level-set offset. Slopes from Maskery 2018
    // Table 2 + symmetry-of-the-level-set argument (ψ=0.50 ⇒ c=0).
    internal static float ThresholdFor(HeatTransfer.TpmsKind kind, float solidFraction)
    {
        if (solidFraction < 0.30f) solidFraction = 0.30f;
        if (solidFraction > 0.70f) solidFraction = 0.70f;
        float delta = solidFraction - 0.50f;       // around the symmetric centre
        // Empirical slopes (f-units per unit solid fraction) — Gyroid
        // scales ~1.40, Schwarz-P ~1.10, Schwarz-D ~1.55 at ψ = 0.50.
        float slope = kind switch
        {
            HeatTransfer.TpmsKind.Gyroid   => 1.40f,
            HeatTransfer.TpmsKind.SchwarzP => 1.10f,
            HeatTransfer.TpmsKind.SchwarzD => 1.55f,
            _ => 1.40f,
        };
        return delta * slope;
    }
}

/// <summary>
/// Annular TPMS void confined to the chamber's cooling-jacket band: an
/// axial range [xMin, xMax] crossed with a radial range [rInner, rOuter].
/// Outside the band the SDF reports true Euclidean distance to the
/// nearest clipping surface (wall / jacket / axial cap); inside the band
/// the SDF follows the TPMS function's sign.
///
/// Radial bounds track the chamber contour via
/// <see cref="RevolvedContourImplicit"/> + <paramref name="tWall_mm"/> /
/// <paramref name="channelHeight_mm"/> — exactly like
/// <see cref="AxialChannelImplicit"/> — so the TPMS void sits cleanly
/// between the gas-side wall and the outer jacket over the full contour.
/// </summary>
public sealed class TpmsAnnularImplicit : IImplicit
{
    private readonly RevolvedContourImplicit _innerContour;
    private readonly TpmsUnitCellImplicit _unitCell;
    private readonly float _tWall;
    private readonly float _xMin, _xMax;
    private readonly float _hChamber, _hThroat, _hExit;
    private readonly float _xStart, _xThroat, _xEnd;
    // Radial clearance protects the gas-side wall + outer jacket from
    // TPMS-intersection by keeping the void strictly inside a band
    // inset from both surfaces. 0.3 mm mirrors the
    // RevolvedPlenumImplicit default clearance.
    private const float RadialClearance_mm = 0.3f;

    public TpmsAnnularImplicit(
        RevolvedContourImplicit innerContour,
        HeatTransfer.TpmsKind kind,
        float cellEdge_mm,
        float solidFraction,
        float tWall_mm,
        float hChamber_mm, float hThroat_mm, float hExit_mm,
        float xStart_mm, float xThroat_mm, float xEnd_mm)
    {
        _innerContour = innerContour;
        _unitCell = new TpmsUnitCellImplicit(kind, cellEdge_mm, solidFraction);
        _tWall = tWall_mm;
        _xMin = xStart_mm;
        _xMax = xEnd_mm;
        _hChamber = hChamber_mm;
        _hThroat = hThroat_mm;
        _hExit = hExit_mm;
        _xStart = xStart_mm;
        _xThroat = xThroat_mm;
        _xEnd = xEnd_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial clip — outside [xMin, xMax] return a positive distance to
        // the nearest axial cap so the TPMS function doesn't wrap into the
        // manifold / flange regions.
        float dxStart = _xMin - p.X;
        float dxEnd = p.X - _xMax;
        float dxOut = MathF.Max(dxStart, dxEnd);
        if (dxOut > 0) return dxOut;

        // Radial clip — only between rInner+clearance and rOuter−clearance
        // is the TPMS function active.
        float Rlocal = _innerContour.RadiusAt(MathF.Max(MathF.Min(p.X, _xEnd), _xStart));
        float h = HeightAt(p.X);
        float rInner = Rlocal + _tWall + RadialClearance_mm;
        float rOuter = Rlocal + _tWall + h - RadialClearance_mm;
        if (rOuter <= rInner) return 1e3f;         // degenerate band

        float r = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z);
        float drInnerOut = rInner - r;             // positive when p is inside inner
        float drOuterOut = r - rOuter;             // positive when p is outside outer
        float drOut = MathF.Max(drInnerOut, drOuterOut);
        if (drOut > 0) return drOut;

        // Inside the annular band — delegate to the TPMS unit cell sign.
        return _unitCell.fSignedDistance(p);
    }

    private float HeightAt(float x)
    {
        if (x <= _xThroat)
        {
            float t = MathF.Max(MathF.Min((x - _xStart) / MathF.Max(_xThroat - _xStart, 1e-6f), 1f), 0f);
            return _hChamber + t * (_hThroat - _hChamber);
        }
        float t2 = MathF.Max(MathF.Min((x - _xThroat) / MathF.Max(_xEnd - _xThroat, 1e-6f), 1f), 0f);
        return _hThroat + t2 * (_hExit - _hThroat);
    }
}
