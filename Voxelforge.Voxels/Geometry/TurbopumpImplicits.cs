// TurbopumpImplicits.cs — Implicit SDF primitives for the single-stage
// centrifugal turbopump geometry. Composable with the existing
// ChamberImplicits / AerospikeImplicits primitives through PicoGK's
// IImplicit interface.
//
// What a real turbopump has + what we model here
// ──────────────────────────────────────────────
// A production LOX/LCH4 turbopump is a multi-month mechanical-design
// effort. The "rocket engine CAD shorthand" version:
//
//   1. Inducer — axial screw-like bladed rotor at the pump inlet that
//      raises local static pressure enough for the main impeller to
//      avoid cavitation. Modeled as a helical swept primitive with a
//      small blade count (typically 3).
//
//   2. Impeller — centrifugal rotor with backward-curved blades that
//      accelerates the fluid radially outward. Modeled as a disc +
//      radial-blade array.
//
//   3. Volute — spiral chamber surrounding the impeller that collects
//      the accelerated flow and routes it into the discharge pipe.
//      Modeled as a torus with a growing cross-section (Archimedean
//      spiral).
//
// What we do NOT model here
// ─────────────────────────
//   • Turbine wheel (drives the impeller from the preburner exhaust)
//     lives in a separate companion module (TurbineImplicits).
//   • Bearings, seals, coolant passages, balance cavities — internal
//     detail that LPBF shops own at the CAD level; we emit the
//     external envelope only.
//   • Shaft critical-speed / bearing loads — structural analysis
//     separate from the geometry-emit pipeline.
//   • Multi-stage impellers — single stage covers the canonical 20 kN
//     → 500 kN envelope we target.
//
// Scale convention
// ────────────────
// Impeller outer radius is derived from the pump's head rise and RPM
// via the Euler turbomachinery equation; inducer / volute are scaled
// proportionally. All SDFs are in millimetres (PicoGK grid convention).
//
// Distance reporting
// ──────────────────
// Same convention as ChamberImplicits / AerospikeImplicits: SDF is
// negative inside the solid phase, positive outside, and the
// Euclidean-distance approximation is tight near the surface.

using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// Helical inducer rotor — axial screw at the pump inlet. Axis
/// aligned with +Z (shaft convention; chamber axis is +X so the
/// turbopump sits on a different coordinate frame). Inside the
/// inducer body the SDF is negative.
/// </summary>
public sealed class InducerImplicit : IImplicit
{
    private readonly float _rHub;            // shaft-side radius
    private readonly float _rTip;            // blade-tip radius
    private readonly float _zMin, _zMax;      // axial extent
    private readonly int _bladeCount;
    private readonly float _pitch;            // axial advance per revolution (mm)
    private readonly float _bladeThickness;   // radial thickness (mm)

    public InducerImplicit(
        float rHub_mm, float rTip_mm,
        float zMin_mm, float zMax_mm,
        int bladeCount = 3,
        float pitch_mm = 20f,
        float bladeThickness_mm = 2.5f)
    {
        if (rTip_mm <= rHub_mm)
            throw new System.ArgumentException("rTip must exceed rHub");
        if (zMax_mm <= zMin_mm)
            throw new System.ArgumentException("zMax must exceed zMin");
        _rHub = rHub_mm;
        _rTip = rTip_mm;
        _zMin = zMin_mm;
        _zMax = zMax_mm;
        _bladeCount = System.Math.Max(1, bladeCount);
        _pitch = MathF.Max(pitch_mm, 1f);
        _bladeThickness = MathF.Max(bladeThickness_mm, 0.5f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial clip.
        if (p.Z < _zMin) return _zMin - p.Z;
        if (p.Z > _zMax) return p.Z - _zMax;

        // Cylindrical coordinates (shaft along Z).
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float theta = MathF.Atan2(p.Y, p.X);

        // Hub is a solid cylinder.
        float dHubOut = r - _rHub;
        if (dHubOut < 0) return dHubOut;          // inside hub → negative

        // Outside the blade-swept envelope — tip radius.
        if (r > _rTip)
            return r - _rTip;

        // Helical blade at each angular index: the k-th blade passes
        // through θ_k(z) = 2π·k/N + 2π·(z − zMin)/pitch. Distance from
        // the sample point to the nearest blade is the arc-distance to
        // whichever θ_k lands closest at this z.
        float zRel = p.Z - _zMin;
        float advance = 2f * MathF.PI * zRel / _pitch;
        float bestArc = float.PositiveInfinity;
        for (int k = 0; k < _bladeCount; k++)
        {
            float thetaBlade = 2f * MathF.PI * k / _bladeCount + advance;
            float dTheta = theta - thetaBlade;
            while (dTheta > MathF.PI)  dTheta -= 2f * MathF.PI;
            while (dTheta < -MathF.PI) dTheta += 2f * MathF.PI;
            float arc = MathF.Abs(dTheta) * r;
            if (arc < bestArc) bestArc = arc;
        }

        // Inside a blade if arc distance < half-thickness.
        float halfT = _bladeThickness * 0.5f;
        if (bestArc < halfT)
        {
            // Distance to blade surface is min of (half-T − arc)
            // radial-cap and axial-cap distances.
            float dRadial = MathF.Min(r - _rHub, _rTip - r);
            float dAxial  = MathF.Min(p.Z - _zMin, _zMax - p.Z);
            return -MathF.Min(MathF.Min(halfT - bestArc, dRadial), dAxial);
        }
        // Outside blade and outside hub — positive distance to nearest
        // blade edge (approximation — ignore radial/axial caps).
        return bestArc - halfT;
    }
}

/// <summary>
/// Backward-curved centrifugal impeller. Shaft aligned with +Z; disc
/// lies in the XY plane at z ∈ [zMin, zMax]. Inside the impeller
/// body (hub disc + blade array) the SDF is negative.
/// </summary>
public sealed class ImpellerImplicit : IImplicit
{
    private readonly float _rHub;
    private readonly float _rTip;
    private readonly float _zMin, _zMax;
    private readonly float _hubDiscThickness;
    private readonly int _bladeCount;
    private readonly float _bladeThickness;
    private readonly float _backwardAngle_rad;  // blade sweepback at exit

    public ImpellerImplicit(
        float rHub_mm, float rTip_mm,
        float zMin_mm, float zMax_mm,
        int bladeCount = 8,
        float bladeThickness_mm = 2.5f,
        float backwardAngleDeg = 30f)
    {
        if (rTip_mm <= rHub_mm)
            throw new System.ArgumentException("rTip must exceed rHub");
        if (zMax_mm <= zMin_mm)
            throw new System.ArgumentException("zMax must exceed zMin");
        _rHub = rHub_mm;
        _rTip = rTip_mm;
        _zMin = zMin_mm;
        _zMax = zMax_mm;
        _hubDiscThickness = (_zMax - _zMin) * 0.35f;
        _bladeCount = System.Math.Max(1, bladeCount);
        _bladeThickness = MathF.Max(bladeThickness_mm, 1.0f);
        _backwardAngle_rad = backwardAngleDeg * MathF.PI / 180f;
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Axial clip.
        if (p.Z < _zMin) return _zMin - p.Z;
        if (p.Z > _zMax) return p.Z - _zMax;

        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        // Hub disc — always-solid region at z near zMin with r ∈ [0, rTip].
        float zRelFromHub = p.Z - _zMin;
        bool inHubDisc = (zRelFromHub <= _hubDiscThickness) && (r <= _rTip);
        if (inHubDisc)
        {
            float dTop = _hubDiscThickness - zRelFromHub;
            float dRadial = _rTip - r;
            return -MathF.Min(dTop, dRadial);
        }

        // Above the hub disc, inside r ∈ [rHub, rTip], check for blade.
        if (r < _rHub) return _rHub - r;    // positive (in shaft cavity)
        if (r > _rTip) return r - _rTip;    // positive (outside impeller envelope)

        float theta = MathF.Atan2(p.Y, p.X);
        // Backward-curved blade: at radius r, the blade's nominal
        // angular position offsets from its root by Δθ = log(r/rHub) ·
        // tan(β). This is the standard logarithmic-spiral family.
        float lnFrac = MathF.Log(MathF.Max(r, 1e-3f) / _rHub);
        float thetaOffset = lnFrac * MathF.Tan(_backwardAngle_rad);

        float bestArc = float.PositiveInfinity;
        for (int k = 0; k < _bladeCount; k++)
        {
            float thetaRoot = 2f * MathF.PI * k / _bladeCount;
            float thetaBlade = thetaRoot - thetaOffset;   // backward = subtract
            float dTheta = theta - thetaBlade;
            while (dTheta > MathF.PI)  dTheta -= 2f * MathF.PI;
            while (dTheta < -MathF.PI) dTheta += 2f * MathF.PI;
            float arc = MathF.Abs(dTheta) * r;
            if (arc < bestArc) bestArc = arc;
        }

        float halfT = _bladeThickness * 0.5f;
        if (bestArc < halfT)
        {
            float dAxial = MathF.Min(p.Z - _zMin, _zMax - p.Z);
            return -MathF.Min(halfT - bestArc, dAxial);
        }
        return bestArc - halfT;
    }
}

/// <summary>
/// Volute — spiral collection chamber wrapping the impeller. Modeled
/// as a torus whose minor-axis radius grows linearly with the
/// wrap angle (Archimedean spiral), producing the characteristic
/// snail-shell cross-section. Shaft aligned with +Z; volute lies in
/// the plane of the impeller. Inside the volute cavity the SDF is
/// negative.
///
/// The volute here is the <b>cavity</b> (the empty space where fluid
/// flows); to produce a printable shell the caller `BoolSubtract`s
/// this from a solid cylindrical casing.
/// </summary>
public sealed class VoluteImplicit : IImplicit
{
    private readonly float _rTipImpeller;         // impeller tip radius (volute inner edge)
    private readonly float _rMinor0;              // volute cross-section radius at θ=0
    private readonly float _growthPerRevolution;  // additional minor radius per 2π (mm)
    private readonly float _zMin, _zMax;
    private readonly float _gapFromImpeller;       // radial gap between impeller tip and volute inner wall (mm)

    public VoluteImplicit(
        float rTipImpeller_mm,
        float rMinor0_mm,
        float growthPerRevolution_mm,
        float zMin_mm, float zMax_mm,
        float gapFromImpeller_mm = 2.0f)
    {
        if (rTipImpeller_mm <= 0)
            throw new System.ArgumentException("rTipImpeller must be positive");
        if (rMinor0_mm <= 0)
            throw new System.ArgumentException("rMinor0 must be positive");
        if (zMax_mm <= zMin_mm)
            throw new System.ArgumentException("zMax must exceed zMin");
        _rTipImpeller = rTipImpeller_mm;
        _rMinor0 = rMinor0_mm;
        _growthPerRevolution = MathF.Max(growthPerRevolution_mm, 0f);
        _zMin = zMin_mm;
        _zMax = zMax_mm;
        _gapFromImpeller = MathF.Max(gapFromImpeller_mm, 0.1f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float theta = MathF.Atan2(p.Y, p.X);
        if (theta < 0) theta += 2f * MathF.PI;     // [0, 2π)

        // Volute cross-section centre at angle θ: radius = r_impeller +
        // gap + current minor radius. Current minor radius grows linearly
        // with θ.
        float minorR = _rMinor0 + _growthPerRevolution * theta / (2f * MathF.PI);
        float centreR = _rTipImpeller + _gapFromImpeller + minorR;
        float centreZ = 0.5f * (_zMin + _zMax);

        // Distance in the (r, z) meridional plane from the centre of the
        // volute cross-section.
        float dR = r - centreR;
        float dZ = p.Z - centreZ;
        float dist2d = MathF.Sqrt(dR * dR + dZ * dZ);

        // Inside the volute tube if dist2d < minorR.
        return dist2d - minorR;
    }
}

/// <summary>
/// Composite N-stage turbopump body: inducer + N impellers + volute
/// casing as a single SDF. Voxelise once to produce the full pump
/// voxel shell.
/// <para>
/// Sprint 3 polish (2026-04-22) — the original single-impeller field
/// was replaced with <see cref="_impellers"/> (an array). The
/// single-impeller constructor overload routes to the array path with
/// a one-element array, so pre-Sprint-3 callers keep producing
/// identical voxelisations.
/// </para>
/// </summary>
public sealed class TurbopumpAssemblyImplicit : IImplicit
{
    private readonly InducerImplicit _inducer;
    private readonly ImpellerImplicit[] _impellers;
    // Solid casing (shell of revolution around the volute) from which
    // the volute cavity is subtracted. Modeled here as an outer
    // cylinder; callers can compose additional shapes after voxelise.
    private readonly float _casingRadius;
    private readonly float _casingZMin, _casingZMax;
    private readonly VoluteImplicit _voluteCavity;

    /// <summary>
    /// Single-stage convenience constructor (kept for pre-Sprint-3
    /// callers). Delegates to the N-stage constructor with a
    /// one-element impeller array.
    /// </summary>
    public TurbopumpAssemblyImplicit(
        InducerImplicit inducer,
        ImpellerImplicit impeller,
        VoluteImplicit voluteCavity,
        float casingRadius_mm,
        float casingZMin_mm,
        float casingZMax_mm)
        : this(inducer, new[] { impeller }, voluteCavity,
               casingRadius_mm, casingZMin_mm, casingZMax_mm)
    {
    }

    /// <summary>
    /// N-stage constructor (Sprint 3 polish, 2026-04-22). Impellers
    /// are supplied in axial order from inducer-facing to discharge-
    /// facing. The array must contain at least one element; callers
    /// stack them with <see cref="TurbopumpGeometryGenerator.InterstageGap_mm"/>
    /// axial spacing.
    /// </summary>
    public TurbopumpAssemblyImplicit(
        InducerImplicit inducer,
        ImpellerImplicit[] impellers,
        VoluteImplicit voluteCavity,
        float casingRadius_mm,
        float casingZMin_mm,
        float casingZMax_mm)
    {
        if (impellers is null || impellers.Length == 0)
            throw new System.ArgumentException(
                "TurbopumpAssemblyImplicit needs at least one impeller.",
                nameof(impellers));
        _inducer = inducer;
        _impellers = impellers;
        _voluteCavity = voluteCavity;
        _casingRadius = casingRadius_mm;
        _casingZMin = casingZMin_mm;
        _casingZMax = casingZMax_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        // Inducer + all impellers are solid (rotating) bodies — union.
        float dRotor = _inducer.fSignedDistance(p);
        for (int i = 0; i < _impellers.Length; i++)
            dRotor = MathF.Min(dRotor, _impellers[i].fSignedDistance(p));

        // Casing outer envelope — a cylinder clipped to the axial extent.
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float dAxial = MathF.Max(_casingZMin - p.Z, p.Z - _casingZMax);
        float dCasingRadial = r - _casingRadius;
        float dCasingOut = MathF.Max(dAxial, dCasingRadial);
        // Inside casing (solid) unless we're in the volute cavity.
        float dVolCavity = _voluteCavity.fSignedDistance(p);
        float dCasing = dCasingOut;
        if (dCasingOut < 0 && dVolCavity < 0)
        {
            // In the volute cavity — hollow. SDF positive (outside casing material).
            dCasing = -dVolCavity;    // distance to casing wall = distance to cavity boundary
        }

        // Union rotor ∪ casing.
        return MathF.Min(dRotor, dCasing);
    }
}
