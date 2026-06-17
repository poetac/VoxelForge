// PumpMountFlange.cs — Pump-mount flange geometry. Earlier monolithic
// engine compositions left the pump casing floating next to the
// chamber with feed tubes as the only structural interface. In
// reality pumps attach to the engine via a rigid mounting flange
// that transfers thrust + torque loads from the impeller casing into
// the chamber side-wall.
//
// What this models
// ────────────────
// A pancake flange: thin annular disc concentric with the pump shaft
// axis (+Z), sitting at the pump's upstream end (the inlet side), with
// a bolt-circle pattern for future mechanical attachment. The flange
// stub projects radially by a fixed amount toward the chamber so the
// monolithic engine builder can `BoolAdd` the flange body and let the
// pump casing merge with the chamber side-wall as a fillet-like
// geometric feature.
//
// Scope
// ─────
//   • Pancake flange only — no gusseted / scalloped flange designs.
//   • Bolt-hole bores ARE modelled (through-holes aligned with +Z).
//   • No seal groove — the flange mates flat to the chamber with an
//     inline SLM-printed gasket face; sealing surface detail is
//     a future follow-on.
//   • The flange is a single body that sits at the pump's inlet side
//     (lowest Z in the pump's local frame). Caller places it via
//     OffsetImplicit + rotation if needed.
//
// Convention
// ──────────
// Axis along +Z (matches TurbopumpImplicits shaft convention). Inner
// radius = pump casing outer radius (so flange-disc hugs casing);
// outer radius = casing-outer + radial projection. Bolt circle at the
// midpoint of inner/outer radii.

using System.Numerics;
using PicoGK;

namespace Voxelforge.Turbopump;

/// <summary>
/// Derived mount-flange geometry record. Pure data — no PicoGK
/// dependency. Caller wraps <see cref="PumpMountFlange.BuildImplicit"/>
/// in an <c>OffsetImplicit</c> to position the flange in world space.
/// </summary>
public sealed record PumpMountFlangeGeometry(
    double InnerRadius_mm,            // hugs pump casing outer
    double OuterRadius_mm,            // inner + radial projection
    double Thickness_mm,              // axial thickness
    int    BoltCount,
    double BoltCircleRadius_mm,
    double BoltHoleDiameter_mm,
    double EstimatedMass_g,
    string Notes);

/// <summary>
/// Pure-math mount-flange generator. Thread-safe; no PicoGK or
/// filesystem side-effects.
/// </summary>
public static class PumpMountFlange
{
    /// <summary>Default radial projection beyond casing OD (mm).</summary>
    public const double DefaultRadialProjection_mm = 12.0;

    /// <summary>Default flange axial thickness (mm). 6 mm gives comfortable LPBF printability at 0.4 mm voxel.</summary>
    public const double DefaultThickness_mm = 6.0;

    /// <summary>Default bolt count — 8 is the canonical single-pump flange pattern.</summary>
    public const int DefaultBoltCount = 8;

    /// <summary>Default bolt hole Ø (mm). Matches M6 clearance (6.4 mm).</summary>
    public const double DefaultBoltHoleDiameter_mm = 6.4;

    /// <summary>GRCop-42 density (g/cm³). Matches other C-tier bodies.</summary>
    public const double MaterialDensity_gcm3 = 8.9;

    /// <summary>
    /// Size a mount flange from the pump's casing outer radius. Other
    /// dimensions default to LPBF-printable values unless overridden.
    /// </summary>
    /// <param name="casingOuterRadius_mm">Pump casing OD (drives flange ID).</param>
    /// <param name="radialProjection_mm">Flange OD − pump OD; 0 = default.</param>
    /// <param name="thickness_mm">Axial thickness; 0 = default.</param>
    /// <param name="boltCount">Bolt count; &lt; 2 snaps to default 8.</param>
    /// <param name="boltHoleDiameter_mm">Bolt through-hole Ø; 0 = default 6.4 mm (M6 clearance).</param>
    public static PumpMountFlangeGeometry Size(
        double casingOuterRadius_mm,
        double radialProjection_mm = 0,
        double thickness_mm = 0,
        int    boltCount = 0,
        double boltHoleDiameter_mm = 0)
    {
        if (casingOuterRadius_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(casingOuterRadius_mm),
                "pump casing radius must be positive");

        double proj  = radialProjection_mm > 0 ? radialProjection_mm : DefaultRadialProjection_mm;
        double thk   = thickness_mm        > 0 ? thickness_mm        : DefaultThickness_mm;
        int    bolts = boltCount           > 1 ? boltCount           : DefaultBoltCount;
        double bolt  = boltHoleDiameter_mm > 0 ? boltHoleDiameter_mm : DefaultBoltHoleDiameter_mm;

        double innerR = casingOuterRadius_mm;
        double outerR = innerR + proj;
        double boltCircleR = 0.5 * (innerR + outerR);

        if (bolt >= proj * 0.9)
            throw new System.ArgumentOutOfRangeException(nameof(boltHoleDiameter_mm),
                $"bolt hole Ø {bolt} mm would exceed 90% of the {proj} mm flange projection");

        // Mass: annulus volume minus bolt holes.
        double annulusVol_mm3 = System.Math.PI * (outerR * outerR - innerR * innerR) * thk;
        double boltVol_mm3 = bolts * System.Math.PI * (bolt / 2) * (bolt / 2) * thk;
        double netVol_mm3 = System.Math.Max(annulusVol_mm3 - boltVol_mm3, 0);
        double mass_g = netVol_mm3 * 1e-3 * MaterialDensity_gcm3;

        string notes = $"Pancake flange: ID={innerR:F1} mm, OD={outerR:F1} mm, "
                     + $"t={thk:F1} mm, {bolts} × Ø{bolt:F1} mm on Ø{2*boltCircleR:F1} mm "
                     + $"bolt circle. Estimated mass {mass_g:F0} g.";

        return new PumpMountFlangeGeometry(
            InnerRadius_mm:       innerR,
            OuterRadius_mm:       outerR,
            Thickness_mm:         thk,
            BoltCount:            bolts,
            BoltCircleRadius_mm:  boltCircleR,
            BoltHoleDiameter_mm:  bolt,
            EstimatedMass_g:      mass_g,
            Notes:                notes);
    }

    /// <summary>
    /// Build a PicoGK <see cref="IImplicit"/> for the flange geometry.
    /// Axis is +Z; flange sits at <c>z ∈ [0, thickness]</c> in its
    /// local frame. Caller wraps in <c>OffsetImplicit</c> to position.
    /// </summary>
    public static IImplicit BuildImplicit(PumpMountFlangeGeometry geom)
    {
        if (geom is null) throw new System.ArgumentNullException(nameof(geom));
        return new PumpMountFlangeImplicit(geom);
    }
}

/// <summary>
/// Annular disc + N bolt holes SDF. Axis along +Z. Flange occupies
/// <c>z ∈ [0, thickness]</c>, <c>r ∈ [innerR, outerR]</c>, with
/// cylindrical through-holes at bolt-circle positions. Inside the
/// solid material the SDF is negative.
/// </summary>
public sealed class PumpMountFlangeImplicit : IImplicit
{
    private readonly float _innerR;
    private readonly float _outerR;
    private readonly float _thickness;
    private readonly int _boltCount;
    private readonly float _boltCircleR;
    private readonly float _boltHoleR;

    public PumpMountFlangeImplicit(PumpMountFlangeGeometry geom)
    {
        _innerR = (float)geom.InnerRadius_mm;
        _outerR = (float)geom.OuterRadius_mm;
        _thickness = (float)geom.Thickness_mm;
        _boltCount = geom.BoltCount;
        _boltCircleR = (float)geom.BoltCircleRadius_mm;
        _boltHoleR = (float)(geom.BoltHoleDiameter_mm * 0.5);
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Annular disc SDF.
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float dAxial = MathF.Max(-p.Z, p.Z - _thickness);   // 0 when inside [0, t]
        float dInner = _innerR - r;                          // 0 when r = innerR
        float dOuter = r - _outerR;                          // 0 when r = outerR
        float dRadial = MathF.Max(dInner, dOuter);
        float dDisc = MathF.Max(dAxial, dRadial);            // negative inside the annular disc

        if (dDisc >= 0) return dDisc;   // outside the disc

        // Inside the disc — check bolt holes. For each bolt position,
        // compute its cylindrical SDF; a positive min-over-bolts means
        // the point is outside every hole (solid flange); a negative
        // value means we're inside some hole (cavity).
        float worstBoreInside = float.NegativeInfinity;
        for (int k = 0; k < _boltCount; k++)
        {
            float theta = 2f * MathF.PI * k / _boltCount;
            float bx = _boltCircleR * MathF.Cos(theta);
            float by = _boltCircleR * MathF.Sin(theta);
            float dx = p.X - bx;
            float dy = p.Y - by;
            float boreDist = MathF.Sqrt(dx * dx + dy * dy) - _boltHoleR;
            if (-boreDist > worstBoreInside) worstBoreInside = -boreDist;
        }
        if (worstBoreInside > 0)
            // Inside a bolt hole — void. SDF positive (flange thickness − hole cavity).
            return worstBoreInside;
        // Solid flange material.
        return dDisc;
    }
}
