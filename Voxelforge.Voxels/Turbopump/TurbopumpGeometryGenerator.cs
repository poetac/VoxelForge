// TurbopumpGeometryGenerator.cs — Parametric turbopump geometry from
// a `PumpSizing` result.
//
// What this does
// ──────────────
// Given the `FeedSystem.TurbopumpSizing.Size(...)` output (shaft
// power + RPM + head rise + NPSHR), emit the external voxel envelope
// of the turbopump as an `IImplicit` that the monolithic-engine
// builder can voxelise alongside the chamber / injector / feed-line
// bodies.
//
// Sizing logic (Sutton RPE 9e §10.4 + Karassik Pump Handbook 4e)
// ──────────────────────────────────────────────────────────────
//   • Impeller tip radius R_2 from Euler head equation:
//       U_2 = √(g·H / ψ)   where ψ = head coefficient, typical 0.45
//       R_2 = U_2 / ω      where ω = 2π · RPM / 60
//   • Impeller hub radius R_1 ≈ 0.35 · R_2 (Karassik rule of thumb).
//   • Inducer tip radius ≈ 1.10 · R_1 (inducer sits upstream, slightly
//     larger diameter to seed the impeller inlet flow).
//   • Inducer axial length ≈ R_2 (typical aspect).
//   • Impeller axial thickness ≈ 0.25 · R_2.
//   • Volute minor-axis radius starts at r_tip · 0.18 at θ=0 and grows
//     linearly to r_tip · 0.45 at θ=2π (Archimedean spiral growth).
//   • Casing wall thickness 3 mm (LPBF-printable at typical pump
//     scales).
//   • Blade counts: inducer 3, impeller 8 (Karassik §2.5 standard for
//     single-stage centrifugal LRE pumps).
//
// Physical reality check
// ──────────────────────
// This generator produces a geometrically plausible first-cut. A
// production turbopump needs CFD + rotordynamic analysis + bearing
// load + thermal analysis + material-compatibility sign-off before
// committing to metal. The output STL is <b>demo- and LPBF-shop-
// reviewable</b>, not flight-qualifiable.
//
// Scope: N-stage centrifugal (N ∈ [1, 4]), axial, and mixed-flow
// pump types deferred beyond centrifugal. The voxel geometry honours
// the SA-promoted PumpStageCount so that changes to it actually
// change what gets voxelised. The turbine wheel (driven by preburner
// exhaust) lives in the companion TurbineGeometryGenerator.

using PicoGK;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;

namespace Voxelforge.Turbopump;

/// <summary>
/// Parametric turbopump geometry summary. Attached to
/// <see cref="FeedSystem.TurbopumpResult.Geometry"/> when the
/// geometry generator runs; null on PressureFed or when the generator
/// is skipped.
/// <para>
/// Sprint 3 polish (2026-04-22) added <see cref="StageCount"/> and
/// <see cref="InterstageGap_mm"/> for N-stage centrifugal pumps. The
/// single-stage default (<see cref="StageCount"/> = 1,
/// <see cref="InterstageGap_mm"/> = 0) preserves pre-Sprint-3 numerical
/// behaviour bit-identically. Impeller hub / tip radii are PER-STAGE;
/// all N stages use identical dimensions because they're each sized
/// against the same per-stage head.
/// </para>
/// </summary>
// TurbopumpGeometry record was extracted to Voxelforge.Core/Turbopump/
// as part of A1.

/// <summary>
/// Pure-math turbopump geometry generator. Deterministic; thread-
/// safe; no PicoGK / filesystem dependency in the sizing path.
/// PicoGK only enters when <see cref="BuildImplicit"/> is called.
/// </summary>
public static class TurbopumpGeometryGenerator
{
    /// <summary>Head coefficient ψ for Euler-head tip-speed inversion (Karassik).</summary>
    public const double HeadCoefficient = 0.45;

    /// <summary>Standard gravity (m/s²) — used to turn head (m) into ΔP.</summary>
    public const double g0 = 9.80665;

    /// <summary>Impeller hub / tip radius ratio (Karassik §2.5).</summary>
    public const double HubToTipRatio = 0.35;

    /// <summary>Inducer tip / impeller hub ratio (inducer slightly bigger).</summary>
    public const double InducerToHubRatio = 1.10;

    /// <summary>Impeller thickness / tip-radius ratio.</summary>
    public const double ImpellerThicknessRatio = 0.25;

    /// <summary>Volute minor-axis start / tip-radius ratio.</summary>
    public const double VoluteMinorStartRatio = 0.18;

    /// <summary>Volute minor-axis end (θ = 2π) / tip-radius ratio.</summary>
    public const double VoluteMinorEndRatio = 0.45;

    /// <summary>Casing wall thickness (mm) — LPBF-printable at pump scale.</summary>
    public const double CasingWallThickness_mm = 3.0;

    /// <summary>Standard inducer blade count (Karassik §2.5).</summary>
    public const int InducerBladeCount = 3;

    /// <summary>Standard single-stage centrifugal impeller blade count.</summary>
    public const int ImpellerBladeCount = 8;

    /// <summary>GRCop-42 density for analytical mass estimate (g/cm³).</summary>
    public const double RotorMaterialDensity_gcm3 = 8.9;

    /// <summary>
    /// Sprint 3 polish (2026-04-22) — axial gap (mm) between stacked
    /// impellers on a multi-stage centrifugal pump. Represents the
    /// crossover / diffuser passage on a real pump; simplified here to
    /// a straight axial gap for voxel purposes. 5 mm matches typical
    /// LPBF-printable interstage wall + passage at LRE pump scales.
    /// </summary>
    public const double InterstageGap_mm = 5.0;

    // ── Internal voxel-construction defaults (BuildImplicit) ──
    // Extracted from inline literals 2026-04-28 (T7 magic-number cleanup).
    // Kept private because they describe the SDF-builder defaults rather
    // than the externally-citeable Karassik sizing ratios above. Generate()
    // mass-estimate constants share this space for the same reason.

    /// <summary>Inducer hub / tip radius ratio — 0.4× tip is the upper limit before the inducer's leading-edge stalls (Brennen 1994 §4.4).</summary>
    private const double InducerHubToTipRatio = 0.40;

    /// <summary>Impeller blade thickness (mm) — LPBF-printable lower bound at LRE pump scales.</summary>
    private const double ImpellerBladeThickness_mm = 2.5;

    /// <summary>Impeller blade backward-sweep angle (deg) — single-stage centrifugal LRE design point (Karassik §2.5).</summary>
    private const double ImpellerBackwardSweep_deg = 30.0;

    /// <summary>Inducer blade pitch / inducer length — typical helix wrap for cavitation-tolerant inducers (Brennen §4.4).</summary>
    private const double InducerPitchToLengthRatio = 0.6;

    /// <summary>Inducer blade thickness (mm) — same LPBF lower bound as the impeller.</summary>
    private const double InducerBladeThickness_mm = 2.5;

    /// <summary>Volute cavity axial padding (mm) past the impeller faces — 1 mm clearance to avoid SDF intersection at the impeller-cavity boundary.</summary>
    private const double VoluteAxialPad_mm = 1.0;

    /// <summary>Radial gap (mm) between the impeller tip and the volute cavity — diffusion-band before the spiral collector.</summary>
    private const double VoluteImpellerGap_mm = 2.0;

    /// <summary>
    /// Generate turbopump geometry parameters from a sized
    /// <see cref="PumpSizing"/> record. Returns null when the pump
    /// is degenerate (zero RPM or zero head).
    /// <para>
    /// Sprint 3 (2026-04-22): reads <see cref="PumpSizing.HeadPerStage_m"/>
    /// and <see cref="PumpSizing.StageCount"/> to produce per-stage
    /// impeller dimensions. For <c>StageCount = 1</c> the tip radius +
    /// RPM + total length collapse to the pre-Sprint-3 single-stage
    /// geometry bit-identically. For N &gt; 1 each stage is sized against
    /// <c>H_total / N</c> of head (RPM is already per-stage-tuned inside
    /// <see cref="FeedSystem.TurbopumpSizing.SizeOnePump"/>), so the
    /// per-stage impeller radius is <c>R_tip_N = R_tip_1 · N^(1/4)</c>
    /// via Euler head × specific-speed composition. N impellers stack
    /// axially with <see cref="InterstageGap_mm"/> crossover between
    /// adjacent stages.
    /// </para>
    /// </summary>
    public static TurbopumpGeometry? Generate(PumpSizing pump)
    {
        if (pump is null) throw new System.ArgumentNullException(nameof(pump));
        if (pump.Rpm <= 0 || pump.HeadRise_m <= 0) return null;

        int stageCount = System.Math.Clamp(pump.StageCount,
            FeedSystem.TurbopumpSizing.MinStageCount,
            FeedSystem.TurbopumpSizing.MaxStageCount);

        // Per-stage head drives the per-stage Euler tip-speed. For
        // Sprint-3-unaware callers that build a PumpSizing positionally
        // without setting HeadPerStage_m, fall back to HeadRise_m (the
        // single-stage equivalent).
        double hStage_m = pump.HeadPerStage_m > 0
            ? pump.HeadPerStage_m
            : pump.HeadRise_m;

        // Tip speed from Euler: U_2 = √(g · h_stage / ψ)
        double tipSpeed_ms = System.Math.Sqrt(g0 * hStage_m / HeadCoefficient);
        // RPM → angular velocity ω
        double omega_rads = 2.0 * System.Math.PI * pump.Rpm / 60.0;
        if (omega_rads <= 0) return null;
        // Impeller tip radius (m → mm) — per-stage (all stages identical).
        double rTip_m = tipSpeed_ms / omega_rads;
        double rTip_mm = rTip_m * 1000.0;
        double rHub_mm = HubToTipRatio * rTip_mm;
        double impThickness_mm = ImpellerThicknessRatio * rTip_mm;

        double inducerTip_mm = InducerToHubRatio * rHub_mm;
        double inducerHub_mm = InducerHubToTipRatio * inducerTip_mm;
        double inducerLength_mm = rTip_mm;          // L ≈ R_2

        double voluteMinorStart_mm = VoluteMinorStartRatio * rTip_mm;
        double voluteMinorEnd_mm   = VoluteMinorEndRatio * rTip_mm;
        double casingOuterR_mm = rTip_mm + voluteMinorEnd_mm * 2.0 + CasingWallThickness_mm;

        // Casing axial length wraps all N stages + interstage gaps +
        // volute fore/aft clearance. For N = 1 the `interstageTotal`
        // term is zero, collapsing to the pre-Sprint-3 formula.
        double interstageTotal_mm = (stageCount - 1) * InterstageGap_mm;
        double stagesStackLength_mm = stageCount * impThickness_mm + interstageTotal_mm;
        double casingLength_mm = stagesStackLength_mm + 2.0 * voluteMinorEnd_mm;
        double totalLength_mm  = inducerLength_mm + casingLength_mm;

        // Mass estimate — inducer + N impellers + casing shell. Each
        // stage's impeller has the same per-stage dimensions, so the
        // rotor volume scales linearly in stageCount.
        double impellerVol_mm3 = System.Math.PI
            * rTip_mm * rTip_mm * impThickness_mm;
        double inducerVol_mm3 = System.Math.PI
            * inducerTip_mm * inducerTip_mm * inducerLength_mm;
        double rotorVol_mm3 = inducerVol_mm3 + stageCount * impellerVol_mm3;
        double casingShellVol_mm3 = 2.0 * System.Math.PI * casingOuterR_mm
                                  * CasingWallThickness_mm * casingLength_mm;
        double totalVol_mm3 = rotorVol_mm3 + casingShellVol_mm3;
        double mass_g = totalVol_mm3 * 1e-3 * RotorMaterialDensity_gcm3;

        string notes = stageCount == 1
            ? $"Single-stage centrifugal. R_tip={rTip_mm:F1} mm, "
            + $"RPM={pump.Rpm:F0}, U_2={tipSpeed_ms:F1} m/s, "
            + $"head={pump.HeadRise_m:F0} m."
            : $"{stageCount}-stage centrifugal. R_tip={rTip_mm:F1} mm (per stage), "
            + $"RPM={pump.Rpm:F0}, U_2={tipSpeed_ms:F1} m/s, "
            + $"head/stage={hStage_m:F0} m, total head={pump.HeadRise_m:F0} m, "
            + $"interstage gap={InterstageGap_mm:F1} mm.";

        return new TurbopumpGeometry(
            ImpellerHubRadius_mm:       rHub_mm,
            ImpellerTipRadius_mm:       rTip_mm,
            ImpellerThickness_mm:       impThickness_mm,
            ImpellerBladeCount:         ImpellerBladeCount,
            InducerHubRadius_mm:        inducerHub_mm,
            InducerTipRadius_mm:        inducerTip_mm,
            InducerLength_mm:           inducerLength_mm,
            InducerBladeCount:          InducerBladeCount,
            VoluteMinorRadiusStart_mm:  voluteMinorStart_mm,
            VoluteMinorRadiusEnd_mm:    voluteMinorEnd_mm,
            CasingOuterRadius_mm:       casingOuterR_mm,
            CasingLength_mm:            casingLength_mm,
            TotalLength_mm:             totalLength_mm,
            EstimatedMass_g:            mass_g,
            Notes:                      notes,
            StageCount:                 stageCount,
            InterstageGap_mm:           stageCount > 1 ? InterstageGap_mm : 0.0);
    }

    /// <summary>
    /// Build a PicoGK-ready <see cref="TurbopumpAssemblyImplicit"/>
    /// from the given geometry parameters. The pump is oriented
    /// along +Z with the inducer at lowest Z and the final-stage
    /// impeller + volute at highest Z. Call this only on the task
    /// thread (PicoGK singleton convention).
    /// <para>
    /// Sprint 3 polish (2026-04-22): emits <see cref="TurbopumpGeometry.StageCount"/>
    /// impellers stacked axially with <see cref="InterstageGap_mm"/> gaps.
    /// The volute cavity wraps the LAST (discharge-facing) stage.
    /// <c>StageCount = 1</c> collapses to the pre-Sprint-3 axial layout
    /// bit-identically.
    /// </para>
    /// </summary>
    public static TurbopumpAssemblyImplicit BuildImplicit(TurbopumpGeometry geom)
    {
        if (geom is null) throw new System.ArgumentNullException(nameof(geom));

        int N = System.Math.Max(1, geom.StageCount);
        float gap = (float)geom.InterstageGap_mm;
        float thickness = (float)geom.ImpellerThickness_mm;

        float zInducerMin = 0f;
        float zInducerMax = (float)geom.InducerLength_mm;

        // Stack N impellers axially, starting immediately after the inducer.
        var impellers = new ImpellerImplicit[N];
        float zStageMin = zInducerMax;
        for (int i = 0; i < N; i++)
        {
            float zStageMax = zStageMin + thickness;
            impellers[i] = new ImpellerImplicit(
                rHub_mm: (float)geom.ImpellerHubRadius_mm,
                rTip_mm: (float)geom.ImpellerTipRadius_mm,
                zMin_mm: zStageMin,
                zMax_mm: zStageMax,
                bladeCount: geom.ImpellerBladeCount,
                bladeThickness_mm: (float)ImpellerBladeThickness_mm,
                backwardAngleDeg: (float)ImpellerBackwardSweep_deg);
            zStageMin = zStageMax + gap;
        }
        // zStageMin now points JUST past the last stage's gap (which
        // doesn't exist for the last stage); pull back by `gap` to land
        // on the last-stage discharge face.
        float zLastImpellerMax = zStageMin - gap;

        float zCasingMin = zInducerMax - (float)geom.VoluteMinorRadiusEnd_mm;
        float zCasingMax = zLastImpellerMax + (float)geom.VoluteMinorRadiusEnd_mm;

        var inducer = new InducerImplicit(
            rHub_mm: (float)geom.InducerHubRadius_mm,
            rTip_mm: (float)geom.InducerTipRadius_mm,
            zMin_mm: zInducerMin,
            zMax_mm: zInducerMax,
            bladeCount: geom.InducerBladeCount,
            pitch_mm: (float)(geom.InducerLength_mm * InducerPitchToLengthRatio),
            bladeThickness_mm: (float)InducerBladeThickness_mm);

        // Volute cavity wraps the LAST-stage impeller — the discharge
        // collector is always at the aft end of a multi-stage LRE pump.
        float zLastImpellerMin = zLastImpellerMax - thickness;
        var volute = new VoluteImplicit(
            rTipImpeller_mm: (float)geom.ImpellerTipRadius_mm,
            rMinor0_mm: (float)geom.VoluteMinorRadiusStart_mm,
            growthPerRevolution_mm: (float)(geom.VoluteMinorRadiusEnd_mm - geom.VoluteMinorRadiusStart_mm),
            zMin_mm: zLastImpellerMin - (float)VoluteAxialPad_mm,
            zMax_mm: zLastImpellerMax + (float)VoluteAxialPad_mm,
            gapFromImpeller_mm: (float)VoluteImpellerGap_mm);

        return new TurbopumpAssemblyImplicit(
            inducer:         inducer,
            impellers:       impellers,
            voluteCavity:    volute,
            casingRadius_mm: (float)geom.CasingOuterRadius_mm,
            casingZMin_mm:   zCasingMin,
            casingZMax_mm:   zCasingMax);
    }
}
