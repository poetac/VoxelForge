// TurbineImplicits.cs — Implicit SDF primitives for the single-stage
// impulse turbine wheel + upstream stator ring. Mirrors the pump
// implicits (ImpellerImplicit / InducerImplicit / VoluteImplicit):
// axis aligned with +Z, negative SDF inside the solid phase,
// tight-to-surface Euclidean approximation.
//
// Geometry model
// ──────────────
// Wheel: hub disc (0 ≤ r ≤ r_hub) fills the full axial extent; blades
// are radial (no backward sweep — this is an impulse wheel) and
// spaced at 2π/N. Stator: annular ring (r ∈ [r_inner, r_outer])
// with vane blades at 2π/M angular spacing.
//
// Axial convention
// ────────────────
// The caller places the turbine on the common shaft opposite the
// pump inducer. Stator sits at low-Z, wheel at high-Z; the
// `TurbineStageAssemblyImplicit` composite unions both with a
// housing cylinder.

using System.Numerics;
using PicoGK;

namespace Voxelforge.Geometry;

/// <summary>
/// Single-stage impulse turbine wheel. Axis aligned with +Z; hub disc
/// is always solid, radial blades fill the tip annulus r ∈ [r_hub,
/// r_tip]. Inside the wheel body the SDF is negative.
/// </summary>
public sealed class TurbineWheelImplicit : IImplicit
{
    private readonly float _rHub;
    private readonly float _rTip;
    private readonly float _zMin, _zMax;
    private readonly int _bladeCount;
    private readonly float _bladeThickness;

    public TurbineWheelImplicit(
        float rHub_mm, float rTip_mm,
        float zMin_mm, float zMax_mm,
        int bladeCount = 36,
        float bladeThickness_mm = 2.0f)
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
        _bladeThickness = MathF.Max(bladeThickness_mm, 0.5f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        if (p.Z < _zMin) return _zMin - p.Z;
        if (p.Z > _zMax) return p.Z - _zMax;

        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);

        // Hub disc — solid inside r ≤ rHub across the full axial extent.
        if (r <= _rHub)
        {
            float dRadial = _rHub - r;
            float dAxial = MathF.Min(p.Z - _zMin, _zMax - p.Z);
            return -MathF.Min(dRadial, dAxial);
        }
        if (r > _rTip) return r - _rTip;

        // Radial blade annulus — impulse blades are straight radial
        // (no backward sweep). Arc distance to nearest blade centre.
        float theta = MathF.Atan2(p.Y, p.X);
        float bestArc = float.PositiveInfinity;
        for (int k = 0; k < _bladeCount; k++)
        {
            float thetaBlade = 2f * MathF.PI * k / _bladeCount;
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
            float dRadial = MathF.Min(r - _rHub, _rTip - r);
            return -MathF.Min(MathF.Min(halfT - bestArc, dAxial), dRadial);
        }
        return bestArc - halfT;
    }
}

/// <summary>
/// Upstream stator (nozzle) ring. Annular disc (r ∈ [r_inner,
/// r_outer]) with vane blades at 2π/M angular spacing. Inside the
/// stator body (ring + vanes) the SDF is negative.
/// </summary>
public sealed class TurbineStatorImplicit : IImplicit
{
    private readonly float _rInner;
    private readonly float _rOuter;
    private readonly float _zMin, _zMax;
    private readonly int _vaneCount;
    private readonly float _vaneThickness;
    private readonly float _ringFrac;   // ring thickness as fraction of (r_outer − r_inner)

    public TurbineStatorImplicit(
        float rInner_mm, float rOuter_mm,
        float zMin_mm, float zMax_mm,
        int vaneCount = 24,
        float vaneThickness_mm = 1.5f,
        float ringThicknessFraction = 0.15f)
    {
        if (rOuter_mm <= rInner_mm)
            throw new System.ArgumentException("rOuter must exceed rInner");
        if (zMax_mm <= zMin_mm)
            throw new System.ArgumentException("zMax must exceed zMin");
        _rInner = rInner_mm;
        _rOuter = rOuter_mm;
        _zMin = zMin_mm;
        _zMax = zMax_mm;
        _vaneCount = System.Math.Max(1, vaneCount);
        _vaneThickness = MathF.Max(vaneThickness_mm, 0.5f);
        _ringFrac = MathF.Max(ringThicknessFraction, 0.05f);
    }

    public float fSignedDistance(in Vector3 p)
    {
        if (p.Z < _zMin) return _zMin - p.Z;
        if (p.Z > _zMax) return p.Z - _zMax;

        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        if (r < _rInner) return _rInner - r;
        if (r > _rOuter) return r - _rOuter;

        // Outer rim ring — solid for the outermost `_ringFrac` of the
        // radial extent.
        float ringInnerR = _rOuter - (_rOuter - _rInner) * _ringFrac;
        if (r > ringInnerR)
        {
            float dAxial = MathF.Min(p.Z - _zMin, _zMax - p.Z);
            float dRadial = MathF.Min(r - ringInnerR, _rOuter - r);
            return -MathF.Min(dAxial, dRadial);
        }

        // Vanes fill r ∈ [rInner, ringInnerR] on each spoke angle.
        float theta = MathF.Atan2(p.Y, p.X);
        float bestArc = float.PositiveInfinity;
        for (int k = 0; k < _vaneCount; k++)
        {
            float thetaVane = 2f * MathF.PI * k / _vaneCount;
            float dTheta = theta - thetaVane;
            while (dTheta > MathF.PI)  dTheta -= 2f * MathF.PI;
            while (dTheta < -MathF.PI) dTheta += 2f * MathF.PI;
            float arc = MathF.Abs(dTheta) * r;
            if (arc < bestArc) bestArc = arc;
        }
        float halfT = _vaneThickness * 0.5f;
        if (bestArc < halfT)
        {
            float dAxial = MathF.Min(p.Z - _zMin, _zMax - p.Z);
            float dRadial = MathF.Min(r - _rInner, ringInnerR - r);
            return -MathF.Min(MathF.Min(halfT - bestArc, dAxial), dRadial);
        }
        return bestArc - halfT;
    }
}

/// <summary>
/// Composite stator + wheel + housing turbine stage. Voxelise once
/// to produce the full turbine voxel shell.
/// </summary>
public sealed class TurbineStageAssemblyImplicit : IImplicit
{
    private readonly TurbineStatorImplicit _stator;
    private readonly TurbineWheelImplicit _wheel;
    private readonly float _housingRadius;
    private readonly float _housingZMin, _housingZMax;

    public TurbineStageAssemblyImplicit(
        TurbineStatorImplicit stator,
        TurbineWheelImplicit wheel,
        float housingRadius_mm,
        float housingZMin_mm,
        float housingZMax_mm)
    {
        _stator = stator;
        _wheel = wheel;
        _housingRadius = housingRadius_mm;
        _housingZMin = housingZMin_mm;
        _housingZMax = housingZMax_mm;
    }

    public float fSignedDistance(in Vector3 p)
    {
        float dStator = _stator.fSignedDistance(p);
        float dWheel  = _wheel.fSignedDistance(p);
        float dRotor  = MathF.Min(dStator, dWheel);

        // Housing — thin cylindrical shell one voxel thick at
        // _housingRadius, between [housingZMin, housingZMax].
        float r = MathF.Sqrt(p.X * p.X + p.Y * p.Y);
        float dAxial = MathF.Max(_housingZMin - p.Z, p.Z - _housingZMax);
        // 2 mm shell thickness — matches LPBF printable wall.
        float rShellIn = _housingRadius - 2f;
        float dRadial;
        if (r > _housingRadius)      dRadial = r - _housingRadius;
        else if (r < rShellIn)       dRadial = rShellIn - r;
        else                         dRadial = -MathF.Min(r - rShellIn, _housingRadius - r);
        float dHousing = MathF.Max(dAxial, dRadial);

        return MathF.Min(dRotor, dHousing);
    }
}
