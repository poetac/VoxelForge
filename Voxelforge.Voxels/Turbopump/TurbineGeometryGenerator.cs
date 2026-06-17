// TurbineGeometryGenerator.cs — Parametric geometry for a
// single-stage impulse turbine wheel + upstream stator ring, paired
// with the pump geometry on the common shaft.
//
// Sizing logic (Sutton RPE 9e §10.5 + Dixon & Hall 7e §9.3)
// ─────────────────────────────────────────────────────────
//   • Wheel tip radius comes from the `TurbineStage.WheelRadius_mm`
//     already computed by `TurbineSizing` (U = 0.5·C₀ Euler inversion
//     at the imposed shaft RPM).
//   • Hub radius — 0.55 × tip radius is the median for single-stage
//     impulse LRE wheels (Huzel & Huang §6.5).
//   • Wheel axial thickness — 0.20 × tip radius (same source).
//   • Stator ring sits upstream of the wheel with a small axial gap.
//     Stator OR matches wheel OR; stator IR = wheel hub to form an
//     annular nozzle ring. Stator axial height = 0.35 × tip radius.
//   • Nozzle throat area — sized from mass-flow conservation:
//       A_throat = ṁ / (ρ · C_throat)
//     ρ and sonic speed come from the preburner warm-gas state.
//     This is informational on the geometry record; the wheel's
//     SDF is independent of A_throat.
//   • Housing OR — wheel OR + 5 mm wall-clearance margin so the wheel
//     clears the turbine scroll.
//
// Coordinate convention
// ─────────────────────
// The turbine sits on the common shaft opposite the pump inducer.
// With the pump anchored at z ∈ [0, pump.TotalLength], the turbine
// is placed at z ∈ [−stator_h − wheel_h, 0] so the shaft spans both
// ends. The `BuildImplicit` composite emits the stator ring + wheel
// in the turbine's local (negative-z) half; callers translate into
// the engine-bundle frame.
//
// Physical reality check
// ──────────────────────
// Same caveat as `TurbopumpGeometryGenerator`: the output is a
// geometrically plausible first-cut for LPBF-shop review, not a
// flight-qualifiable article. A production turbine needs a full CFD
// pass + rotordynamic sign-off + thermal analysis before committing
// to metal.

using System;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;

namespace Voxelforge.Turbopump;

/// <summary>
/// Parametric turbine-stage geometry summary. Attached to
/// <see cref="TurbopumpResult.FuelTurbineGeometry"/> /
/// <see cref="TurbopumpResult.OxTurbineGeometry"/> when the generator
/// runs.
/// </summary>
// TurbineGeometry record extracted to Voxelforge.Core/Turbopump/
// as part of A1.

/// <summary>
/// Pure-math turbine geometry generator. Deterministic; thread-safe;
/// no PicoGK / filesystem dependency in the sizing path. PicoGK only
/// enters when <see cref="BuildImplicit"/> is called.
/// </summary>
public static class TurbineGeometryGenerator
{
    /// <summary>Hub / tip radius ratio for single-stage impulse LRE wheels (Huzel &amp; Huang §6.5).</summary>
    public const double WheelHubToTipRatio = 0.55;

    /// <summary>Wheel axial thickness / tip-radius ratio (Huzel &amp; Huang §6.5).</summary>
    public const double WheelThicknessRatio = 0.20;

    /// <summary>Stator axial height / wheel tip-radius ratio.</summary>
    public const double StatorHeightRatio = 0.35;

    /// <summary>Radial wall clearance (mm) between wheel tip and housing.</summary>
    public const double HousingRadialClearance_mm = 5.0;

    /// <summary>Axial gap (mm) between stator trailing edge and wheel leading edge.</summary>
    public const double StatorWheelAxialGap_mm = 2.5;

    /// <summary>Universal gas constant (J/(kmol·K)) — throat-area sizing.</summary>
    public const double R_universal = 8314.5;

    /// <summary>GRCop-42 density for analytical mass estimate (g/cm³), matching pump side.</summary>
    public const double RotorMaterialDensity_gcm3 = 8.9;

    // ── Internal voxel-construction defaults (BuildImplicit) + mass tuning ──
    // Extracted from inline literals 2026-04-28 (T7 magic-number cleanup).
    // Kept private because they're SDF-builder defaults / mass-estimate
    // tuning factors rather than externally-citeable Huzel & Huang ratios.

    /// <summary>Wheel shaft-hub hole area as a fraction of hub-disc area — bored-out region for the shaft on a real impulse wheel.</summary>
    private const double WheelShaftHoleAreaFraction = 0.3;

    /// <summary>Stator vane material fill fraction within the annular ring — accounts for vane volume vs. open-passage volume.</summary>
    private const double StatorVaneAreaFillFraction = 0.4;

    /// <summary>Wheel blade thickness (mm) — LPBF-printable lower bound for impulse-wheel blades at LRE turbine scales.</summary>
    private const double WheelBladeThickness_mm = 2.0;

    /// <summary>Stator vane thickness (mm) — same LPBF lower bound, slightly thinner than the wheel blades since the stator vanes don't carry rotor stress.</summary>
    private const double StatorVaneThickness_mm = 1.5;

    /// <summary>
    /// Generate turbine geometry from a sized <see cref="TurbineStage"/>.
    /// Returns null on degenerate input (zero wheel radius, zero mass
    /// flow).
    /// </summary>
    public static TurbineGeometry? Generate(TurbineStage stage)
    {
        if (stage is null) throw new ArgumentNullException(nameof(stage));
        if (stage.WheelRadius_mm <= 0 || stage.MassFlow_kgs <= 0) return null;

        double rTip_mm = stage.WheelRadius_mm;
        double rHub_mm = WheelHubToTipRatio * rTip_mm;
        double wheelThickness_mm = WheelThicknessRatio * rTip_mm;

        double statorInner_mm = rHub_mm;
        double statorOuter_mm = rTip_mm;
        double statorHeight_mm = StatorHeightRatio * rTip_mm;

        // Nozzle throat area from mass flow: A = ṁ / (ρ · C_sonic).
        // Perfect-gas density at preburner state: ρ = P · MW / (R · T).
        double rhoInlet = stage.InletPressure_Pa * stage.MolecularWeight_gmol * 1e-3
                        / (R_universal / stage.MolecularWeight_gmol * stage.MolecularWeight_gmol * 1e-3 * stage.InletTemperature_K);
        // Simplify: ρ = P / (R_specific · T), R_specific = R_univ / MW (kg/kmol)
        double rSpecific = R_universal / Math.Max(stage.MolecularWeight_gmol, 1e-3);
        rhoInlet = stage.InletPressure_Pa / (rSpecific * stage.InletTemperature_K);
        double cSonic = Math.Sqrt(stage.Gamma * rSpecific * stage.InletTemperature_K);
        double throatArea_m2 = stage.MassFlow_kgs / Math.Max(rhoInlet * cSonic, 1e-9);
        double throatArea_mm2 = throatArea_m2 * 1e6;

        double housingOuter_mm = rTip_mm + HousingRadialClearance_mm;
        double totalLength_mm = statorHeight_mm + StatorWheelAxialGap_mm + wheelThickness_mm;

        // Mass estimate — solid-disc wheel minus shaft-hub hole +
        // stator annular ring.
        double wheelVol_mm3 = Math.PI * (rTip_mm * rTip_mm - rHub_mm * rHub_mm * WheelShaftHoleAreaFraction)
                            * wheelThickness_mm;
        double statorVol_mm3 = Math.PI
            * (statorOuter_mm * statorOuter_mm - statorInner_mm * statorInner_mm)
            * statorHeight_mm * StatorVaneAreaFillFraction;
        double mass_g = (wheelVol_mm3 + statorVol_mm3) * 1e-3 * RotorMaterialDensity_gcm3;

        string notes = $"Single-stage impulse, R_tip={rTip_mm:F1} mm, "
                     + $"{stage.BladeCount} blades / {stage.StatorVaneCount} stator vanes, "
                     + $"throat {throatArea_mm2:F1} mm².";

        return new TurbineGeometry(
            WheelHubRadius_mm:    rHub_mm,
            WheelTipRadius_mm:    rTip_mm,
            WheelThickness_mm:    wheelThickness_mm,
            WheelBladeCount:      stage.BladeCount,
            StatorInnerRadius_mm: statorInner_mm,
            StatorOuterRadius_mm: statorOuter_mm,
            StatorAxialHeight_mm: statorHeight_mm,
            StatorVaneCount:      stage.StatorVaneCount,
            NozzleThroatArea_mm2: throatArea_mm2,
            HousingOuterRadius_mm:housingOuter_mm,
            TotalLength_mm:       totalLength_mm,
            EstimatedMass_g:      mass_g,
            Notes:                notes);
    }

    /// <summary>
    /// Build a PicoGK-ready <see cref="TurbineStageAssemblyImplicit"/>
    /// from the given geometry. The stage is oriented along the shaft
    /// axis with the stator at lowest Z and the wheel at highest Z.
    /// Call this only on the task thread (PicoGK singleton convention).
    /// </summary>
    public static TurbineStageAssemblyImplicit BuildImplicit(TurbineGeometry geom)
    {
        if (geom is null) throw new ArgumentNullException(nameof(geom));

        float zStatorMin = 0f;
        float zStatorMax = (float)geom.StatorAxialHeight_mm;
        float zWheelMin  = zStatorMax + (float)StatorWheelAxialGap_mm;
        float zWheelMax  = zWheelMin + (float)geom.WheelThickness_mm;

        var wheel = new TurbineWheelImplicit(
            rHub_mm:          (float)geom.WheelHubRadius_mm,
            rTip_mm:          (float)geom.WheelTipRadius_mm,
            zMin_mm:          zWheelMin,
            zMax_mm:          zWheelMax,
            bladeCount:       geom.WheelBladeCount,
            bladeThickness_mm:(float)WheelBladeThickness_mm);

        var stator = new TurbineStatorImplicit(
            rInner_mm:        (float)geom.StatorInnerRadius_mm,
            rOuter_mm:        (float)geom.StatorOuterRadius_mm,
            zMin_mm:          zStatorMin,
            zMax_mm:          zStatorMax,
            vaneCount:        geom.StatorVaneCount,
            vaneThickness_mm: (float)StatorVaneThickness_mm);

        return new TurbineStageAssemblyImplicit(
            stator:            stator,
            wheel:             wheel,
            housingRadius_mm:  (float)geom.HousingOuterRadius_mm,
            housingZMin_mm:    zStatorMin,
            housingZMax_mm:    zWheelMax);
    }
}
