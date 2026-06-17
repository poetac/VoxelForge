// MarineDesign.cs — central design record for the marine pillar.
//
// Implements IEngineDesign with Family = EngineFamilies.Marine.
// Analogous to AirbreathingEngineDesign on the air-breathing side.
// Pillar spec: Voxelforge/docs/pillar-specs/marine-displacement.md.

using System;
using Voxelforge.Engines;

namespace Voxelforge.Marine;

/// <summary>
/// Design parameters for a marine hull candidate. Wave 1 covers the
/// fully-submerged AUV mid-body (<see cref="MarineKind.AuvMidBody"/>):
/// a Myring-faired cylindrical pressure hull parameterised by length,
/// diameter, fairing fractions, wall thickness, material, and depth
/// rating. Wave 2 adds <see cref="HullFamily.CylindricalHemi"/>.
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="MarineKind.AuvMidBody"/> for M1.</param>
/// <param name="Length_m">Overall hull length [m].</param>
/// <param name="Diameter_m">Maximum hull diameter [m].</param>
/// <param name="NoseFairingFraction">
/// Nose fairing length as a fraction of total hull length [-].
/// Myring (1976) nose profile uses n=2.0. Ignored by <see cref="HullFamily.CylindricalHemi"/>.
/// </param>
/// <param name="TailFairingFraction">
/// Tail fairing length as a fraction of total hull length [-].
/// Myring (1976) tail profile uses m=1.5, p=0.5. Ignored by <see cref="HullFamily.CylindricalHemi"/>.
/// </param>
/// <param name="WallThickness_m">
/// Pressure hull shell wall thickness [m].
/// Must satisfy ASME BPVC §VIII Div 1 UG-28 at the target depth.
/// </param>
/// <param name="MaterialIndex">
/// Material selection: 0 = Ti-6Al-4V, 1 = Al-6061, 2 = AISI-316L (LPBF).
/// </param>
/// <param name="DepthRating_m">Target operating depth rating [m].</param>
/// <param name="HullFamily">Hull geometry family (default: <see cref="HullFamily.Myring"/>).</param>
public sealed record MarineDesign(
    MarineKind Kind,
    double Length_m,
    double Diameter_m,
    double NoseFairingFraction,
    double TailFairingFraction,
    double WallThickness_m,
    int MaterialIndex,
    double DepthRating_m,
    HullFamily HullFamily = HullFamily.Myring) : IEngineDesign
{
    /// <inheritdoc />
    public string Family => EngineFamilies.Marine;

    /// <summary>Nose fairing length [m] = Length_m × NoseFairingFraction.</summary>
    public double NoseLength_m => Length_m * NoseFairingFraction;

    /// <summary>Tail fairing length [m] = Length_m × TailFairingFraction.</summary>
    public double TailLength_m => Length_m * TailFairingFraction;

    /// <summary>Cylindrical mid-body length [m] = Length − Nose − Tail.</summary>
    public double MidBodyLength_m => Length_m - NoseLength_m - TailLength_m;

    /// <summary>Hull fineness ratio L/D [-].</summary>
    public double FinenessRatio => Diameter_m > 0 ? Length_m / Diameter_m : double.NaN;

    // ── Wave-3 SurfaceHull (Planing) fields (Sprint M.W3) ────────────────
    //
    // Per ADR-026 D3, planing-hull-specific knobs ride on this record as
    // init-only properties with NaN/sentinel defaults rather than living
    // on a per-kind subtype. AuvMidBody designs ignore all of these.
    // SurfaceHull designs must populate them. Schema marine v2 → v3
    // identity migration leaves them at default for round-tripped AUV
    // designs.

    /// <summary>
    /// Beam at midship [m] (planing hull). SA design variable 1 of 5 for
    /// SurfaceHull. Bounds 1.5 – 6.0 m for the recreational planing
    /// cluster. Defaults to <see cref="double.NaN"/> for AUV kinds.
    /// </summary>
    public double BeamMidship_m { get; init; } = double.NaN;

    /// <summary>
    /// Deadrise angle β [°] at midship — the V-bottom transverse angle.
    /// SA design variable 2 of 5. Bounds 5–25° (deep-V offshore boats sit
    /// at 22–24°; flat-bottom skiffs at 5–10°). Defaults to NaN for AUV.
    /// </summary>
    public double DeadriseAngle_deg { get; init; } = double.NaN;

    /// <summary>
    /// Mass displacement Δ [kg] (vessel weight including payload). SA
    /// design variable 3 of 5. Bounds 500 – 50 000 kg for the typical
    /// planing cluster. Defaults to NaN for AUV.
    /// </summary>
    public double MassDisplacement_kg { get; init; } = double.NaN;

    /// <summary>
    /// Freeboard height [m] above the design waterline. SA design variable
    /// 4 of 5. Bounds 0.3 – 1.5 m. Defaults to NaN for AUV.
    /// </summary>
    public double FreeboardHeight_m { get; init; } = double.NaN;

    /// <summary>
    /// Longitudinal CG location as a fraction of LWL aft of bow [-]. SA
    /// design variable 5 of 5. Bounds 0.40 – 0.60 (centreline; aft-leaning
    /// for planing efficiency). Drives Savitsky lift balance via the LCG-
    /// to-CP moment arm. Defaults to NaN for AUV.
    /// </summary>
    public double LongitudinalCgFraction { get; init; } = double.NaN;

    // ── Wave-3 DisplacementSurface fields (Sprint M.W4) ──────────────────
    //
    // Per ADR-026 D3, displacement-surface-specific knobs ride on this
    // record as init-only properties with NaN defaults. Other kinds ignore.
    // Schema marine v3 → v4 identity migration leaves them at default for
    // round-tripped AUV / Planing designs.

    /// <summary>
    /// Beam at waterline B [m]. Displacement-surface hull only — other
    /// kinds ignore. SA design variable 1 of 4 for DisplacementSurface.
    /// Bounds 3 – 25 m (cargo / fishing / motor-vessel cluster). Distinct
    /// from <see cref="BeamMidship_m"/> (planing) because the displacement
    /// hull's geometry parameters cluster differently. Defaults to NaN.
    /// </summary>
    public double BeamWaterline_m { get; init; } = double.NaN;

    /// <summary>
    /// Design draft T [m] (mean draft at the displacement waterline).
    /// Displacement-surface only. SA design variable 2 of 4. Bounds
    /// 1.0 – 12 m. Defaults to NaN.
    /// </summary>
    public double DraftDesign_m { get; init; } = double.NaN;

    /// <summary>
    /// Block coefficient C_b [-] = ∇ / (LWL · B · T). Displacement-surface
    /// only. SA design variable 3 of 4. Bounds 0.40 – 0.85 (slender ferries
    /// at the low end; fat bulk carriers at the high end). Defaults to NaN.
    /// </summary>
    public double BlockCoefficient { get; init; } = double.NaN;

    /// <summary>
    /// Mass displacement Δ [kg] (vessel + cargo). Displacement-surface only.
    /// SA design variable 4 of 4. Bounds 50 000 – 50 000 000 kg
    /// (small craft to mid-size bulk carrier). Defaults to NaN.
    /// </summary>
    public double DisplacementMass_kg { get; init; } = double.NaN;

    // ── Wave-3 DisplacementSurface — semi-displacement extension (Sprint M.W5) ──
    //
    // Schema marine v4 → v5 identity migration: 1 new init-only bool defaults
    // to false. Round-tripped Wave-1/W2/W3/W4 designs keep the prior
    // displacement-only behaviour bit-identically.

    /// <summary>
    /// Enable the Holtrop semi-displacement Froude-band correction (Sprint
    /// M.W5). When false (the default — bit-identical to Sprint M.W4), the
    /// model is hard-gated to Fn ∈ [0.05, 0.40] and the wave-making term
    /// uses the displacement-only fit. When true, the model:
    /// (a) loosens the upper Froude hard ceiling to 0.55;
    /// (b) applies a smoothed transition multiplier on R_W for Fn ∈
    ///     [0.30, 0.55] that captures the dynamic-lift-induced wave-making
    ///     mitigation observed in the semi-displacement cluster (Holtrop
    ///     1984 high-Fn correction band); and
    /// (c) flips the <c>HOLTROP_SEMI_DISPLACEMENT_REGIME</c> advisory gate
    ///     when Fn > 0.30. Below Fn = 0.30 the model is bit-identical to
    ///     the Sprint M.W4 behaviour. Other hull kinds ignore this flag.
    /// </summary>
    public bool EnableSemiDisplacementCorrection { get; init; } = false;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a numeric field is non-positive, NaN, or outside its
    /// allowable range — Length_m, Diameter_m, WallThickness_m, DepthRating_m,
    /// MaterialIndex, NoseFairingFraction, TailFairingFraction (Myring +
    /// CylindricalHemi); BeamMidship_m, DeadriseAngle_deg, MassDisplacement_kg,
    /// FreeboardHeight_m, LongitudinalCgFraction (Planing); BeamWaterline_m,
    /// DraftDesign_m, BlockCoefficient, DisplacementMass_kg
    /// (DisplacementSurface).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a cross-field constraint is violated —
    /// NoseFairingFraction + TailFairingFraction ≥ 1.0 (no mid-body) for
    /// Myring hulls, or Diameter_m ≥ Length_m for CylindricalHemi hulls — or
    /// when <see cref="HullFamily"/> is an unrecognised enum value.
    /// </exception>
    public void ValidateSelf()
    {
        if (double.IsNaN(Length_m) || Length_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(Length_m),
                $"Length_m={Length_m:F4} must be > 0.");

        switch (HullFamily)
        {
            case HullFamily.Myring:
                if (double.IsNaN(Diameter_m) || Diameter_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(Diameter_m),
                        $"Diameter_m={Diameter_m:F4} must be > 0.");
                if (double.IsNaN(WallThickness_m) || WallThickness_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(WallThickness_m),
                        $"WallThickness_m={WallThickness_m:F4} must be > 0.");
                if (MaterialIndex < 0 || MaterialIndex > 2)
                    throw new ArgumentOutOfRangeException(nameof(MaterialIndex),
                        $"MaterialIndex={MaterialIndex} must be 0 (Ti-6Al-4V), 1 (Al-6061), or 2 (AISI-316L).");
                if (double.IsNaN(DepthRating_m) || DepthRating_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(DepthRating_m),
                        $"DepthRating_m={DepthRating_m:F4} must be > 0.");
                if (double.IsNaN(NoseFairingFraction)
                    || NoseFairingFraction <= 0 || NoseFairingFraction >= 1)
                    throw new ArgumentOutOfRangeException(nameof(NoseFairingFraction),
                        $"NoseFairingFraction={NoseFairingFraction:F4} must be in (0, 1).");
                if (double.IsNaN(TailFairingFraction)
                    || TailFairingFraction <= 0 || TailFairingFraction >= 1)
                    throw new ArgumentOutOfRangeException(nameof(TailFairingFraction),
                        $"TailFairingFraction={TailFairingFraction:F4} must be in (0, 1).");
                if (NoseFairingFraction + TailFairingFraction >= 1.0)
                    throw new ArgumentException(
                        $"NoseFairingFraction ({NoseFairingFraction:F4}) + "
                      + $"TailFairingFraction ({TailFairingFraction:F4}) must be < 1.0 "
                      + "so the hull has a mid-body.");
                break;
            case HullFamily.CylindricalHemi:
                if (double.IsNaN(Diameter_m) || Diameter_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(Diameter_m),
                        $"Diameter_m={Diameter_m:F4} must be > 0.");
                if (double.IsNaN(WallThickness_m) || WallThickness_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(WallThickness_m),
                        $"WallThickness_m={WallThickness_m:F4} must be > 0.");
                if (MaterialIndex < 0 || MaterialIndex > 2)
                    throw new ArgumentOutOfRangeException(nameof(MaterialIndex),
                        $"MaterialIndex={MaterialIndex} must be 0 (Ti-6Al-4V), 1 (Al-6061), or 2 (AISI-316L).");
                if (double.IsNaN(DepthRating_m) || DepthRating_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(DepthRating_m),
                        $"DepthRating_m={DepthRating_m:F4} must be > 0.");
                if (Diameter_m >= Length_m)
                    throw new ArgumentException(
                        $"CylindricalHemi hull requires Diameter_m ({Diameter_m:F4}) < Length_m ({Length_m:F4}) "
                      + "so both hemispherical endcaps fit within the hull length.");
                break;
            case HullFamily.Planing:
                // SurfaceHull (Planing) — AUV-specific positional fields are
                // ignored; the planing-specific init-only fields must be
                // populated and physically reasonable.
                if (double.IsNaN(BeamMidship_m) || BeamMidship_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(BeamMidship_m),
                        $"Planing hull BeamMidship_m={BeamMidship_m:F4} must be > 0.");
                if (double.IsNaN(DeadriseAngle_deg)
                    || DeadriseAngle_deg < 0 || DeadriseAngle_deg > 45)
                    throw new ArgumentOutOfRangeException(nameof(DeadriseAngle_deg),
                        $"Planing hull DeadriseAngle_deg={DeadriseAngle_deg:F2} must be in [0, 45].");
                if (double.IsNaN(MassDisplacement_kg) || MassDisplacement_kg <= 0)
                    throw new ArgumentOutOfRangeException(nameof(MassDisplacement_kg),
                        $"Planing hull MassDisplacement_kg={MassDisplacement_kg:F3} must be > 0.");
                if (double.IsNaN(FreeboardHeight_m) || FreeboardHeight_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(FreeboardHeight_m),
                        $"Planing hull FreeboardHeight_m={FreeboardHeight_m:F4} must be > 0.");
                if (double.IsNaN(LongitudinalCgFraction)
                    || LongitudinalCgFraction <= 0 || LongitudinalCgFraction >= 1)
                    throw new ArgumentOutOfRangeException(nameof(LongitudinalCgFraction),
                        $"Planing hull LongitudinalCgFraction={LongitudinalCgFraction:F4} must be in (0, 1).");
                break;
            case HullFamily.DisplacementSurface:
                // DisplacementSurface — Holtrop-Mennen regime. AUV-positional
                // fields ignored; the 4 displacement-specific fields must be
                // populated.
                if (double.IsNaN(BeamWaterline_m) || BeamWaterline_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(BeamWaterline_m),
                        $"Displacement hull BeamWaterline_m={BeamWaterline_m:F4} must be > 0.");
                if (double.IsNaN(DraftDesign_m) || DraftDesign_m <= 0)
                    throw new ArgumentOutOfRangeException(nameof(DraftDesign_m),
                        $"Displacement hull DraftDesign_m={DraftDesign_m:F4} must be > 0.");
                if (double.IsNaN(BlockCoefficient)
                    || BlockCoefficient < 0.40 || BlockCoefficient > 0.85)
                    throw new ArgumentOutOfRangeException(nameof(BlockCoefficient),
                        $"Displacement hull BlockCoefficient={BlockCoefficient:F4} must be in [0.40, 0.85].");
                if (double.IsNaN(DisplacementMass_kg) || DisplacementMass_kg <= 0)
                    throw new ArgumentOutOfRangeException(nameof(DisplacementMass_kg),
                        $"Displacement hull DisplacementMass_kg={DisplacementMass_kg:F3} must be > 0.");
                break;
            default:
                throw new ArgumentException(
                    $"Unknown HullFamily '{HullFamily}'.", nameof(HullFamily));
        }
    }
}
