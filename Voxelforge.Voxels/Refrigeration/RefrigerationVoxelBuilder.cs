// RefrigerationVoxelBuilder.cs — Sprint A.81 (C.2) heat-pump /
// refrigeration assembly voxel builder. Framing-B Phase 3 voxel-pipeline
// backfill on the Refrigeration pillar (Wave-1 internal namespace
// `Voxelforge.Refrigeration`, algebraic-only since RFG.W1 / RFG.W2; this
// is the first geometry surface).
//
// Generates a three-subassembly heat-pump body from a RefrigerationDesign:
//
//   ── Topology ──
//   Three coaxial sub-envelopes along the +X axis, butted end-to-end:
//
//     [−]  evaporator coil envelope (annular shell)   ← cold-side
//          |
//          compressor (solid cylinder)                ← centred on x = 0
//          |
//     [+]  condenser  coil envelope (annular shell)   ← hot-side
//
//   The compressor is centred on the origin
//   (x ∈ [−L_comp/2, +L_comp/2]).
//   The condenser sits at x ∈ [+L_comp/2, +L_comp/2 + L_cond] (hot side).
//   The evaporator sits at x ∈ [−L_comp/2 − L_evap, −L_comp/2] (cold side).
//
//   Each coil envelope is a thick annulus — a tractable "mass-consistent
//   envelope" representation of the volume swept by a helical /
//   serpentine tube bundle. True helical-coil geometry is deferred to a
//   future RFG.W3+ refinement; the annular envelope mirrors the A.70
//   Tankage approach (single-shell representation of a real-world tube
//   bundle) and is sufficient for downstream LPBF / packaging / mass
//   estimation.
//
//   ── Dimensional fields are derived from cluster norms ──
//   The Wave-1/Wave-2 RefrigerationDesign surface is purely thermo-
//   dynamic (W_compressor, T_cold, T_hot, refrigerant, mode) — it
//   does not yet expose compressor displacement, coil tube OD, or
//   coil bundle dimensions. The voxel builder anchors dimensional
//   fields to cluster norms documented per-field below.
//
//   ── Compressor sizing (1-10 kW W_compressor class) ──
//   Hermetic-rotary / scroll residential compressors used by R-744
//   heat-pump water heaters (Sanden ECO-CUTE TR-series), residential
//   split AC (Daikin / Mitsubishi 1-3 kW class), and small commercial
//   refrigeration cluster span:
//     - OD:     50-150 mm        (75 mm cluster mid at 1 kW shaft)
//     - Length: 80-250 mm        (120 mm cluster mid at 1 kW shaft)
//   Within the cluster, the compressor envelope volume scales close to
//   linearly with shaft power (volumetric displacement at fixed RPM).
//   We use a R ∝ W^(1/3) scaling anchored at R = 37.5 mm @ 1 kW (i.e.
//   75 mm OD, mid of cluster) and a 1.6:1 length-to-OD aspect ratio:
//     R_compressor_mm = 37.5 · (W_compressor_W / 1000)^(1/3)
//     L_compressor_mm = 3.2 · R_compressor_mm    (= 1.6 · OD)
//
//   ── Condenser / evaporator coil sizing ──
//   The coil-bundle annulus represents the volume swept by the actual
//   tube coil (typically 6-12 mm OD copper tubing for residential
//   units; cluster mid ≈ 9.5 mm). For a tight serpentine / helical pack
//   the bundle is ~30 mm radial thickness (3 tube-diameters worth of
//   coverage). The bundle wraps around the compressor with its inner
//   radius matching the compressor outer radius (coaxial nesting):
//     R_coil_inner_mm = R_compressor_mm
//     R_coil_outer_mm = R_compressor_mm + 30                (cluster mid)
//
//   Coil length scales with heat-exchange duty:
//     • Condenser Q_hot  = RefrigerationSolver.Solve(design).HotSideHeatDelivery_W
//     • Evaporator Q_cold = RefrigerationSolver.Solve(design).ColdSideHeatRemoval_W
//   Residential heat-pump coil cluster: ~ 200 mm bundle length at
//   3.5 kW heat duty (Sanden ECO-CUTE GUS-A45HOL gas-cooler ~ 250 mm
//   active length at 4.5 kW). We use:
//     L_coil_mm = 200 · √(Q_W / 3500)
//
//   ── Coordinate convention ──
//   +X is the assembly axis. Compressor centred at the origin; the
//   condenser sits on the +X side (hot reservoir → useful output in
//   heating mode), evaporator on the -X side (cold reservoir →
//   useful output in cooling mode). Same +X axis-of-symmetry
//   convention as Flywheel A.67 and Tankage A.70.
//
//   ── Hollow-vs-solid voxel topology ──
//   The compressor is a SOLID cylinder. Each coil envelope is a thick
//   annulus with axially-open ends (the coil bundle is genuinely a
//   bundle of tubes wound through air, not a closed cavity), so
//   `AnnulusImplicit` renders correctly per the same pattern as the
//   Flywheel ThinRim and the cylinder-only Tankage path. No PicoGK
//   2.0.0 closed-cavity flood-fill workaround needed here — the
//   envelope is genuinely open at both ends.
//
//   ── Wall-safe smoothing cap (PicoGK pitfall #1) ──
//   Smoothen(d) destroys features < 2d. The thinnest feature is
//   min(coil bundle thickness, compressor OD/2, individual envelope
//   length). We cap d at 25 % of that floor and only smoothen if
//   d ≥ 0.02 mm (consistent with FlywheelVoxelBuilder /
//   TankageVoxelBuilder).
//
//   ── Validation surface ──
//   RefrigerationDesign.ValidateSelf() throws on non-positive
//   reservoir temperatures, T_hot ≤ T_cold, non-positive
//   W_compressor, and the None sentinel on Mode / Refrigerant.
//   The voxel builder propagates these.
//
// References (cluster anchors):
//   ASHRAE Handbook — Refrigeration (2022), chap 3 (CO₂ Systems),
//     chap 12 (Compressors), chap 23 (Air-Cooled Condensers).
//   Sanden Holdings ECO-CUTE GUS Series Service Manual (2018 ed.)
//     — TR-series rotary compressor + gas-cooler dimensions.
//   Stoecker W.F. (1998). "Industrial Refrigeration Handbook,"
//     chap 4 (Compressor sizing), chap 6 (Condenser design).
//   Cengel Y., Boles M. (2014). "Thermodynamics: An Engineering
//     Approach," 8th ed., chap 11 (refrigeration cycles).

using System;
using System.Numerics;
using PicoGK;
using Voxelforge.Geometry;

namespace Voxelforge.Refrigeration;

/// <summary>
/// PicoGK voxel builder for a heat-pump / refrigeration assembly
/// (Sprint A.81 / C.2). Companion to <see cref="RefrigerationSolver"/> —
/// turns a <see cref="RefrigerationDesign"/> into a printable
/// three-subassembly envelope (compressor + condenser coil + evaporator
/// coil). Dimensional fields are anchored to cluster norms because the
/// Wave-1/Wave-2 RefrigerationDesign surface is purely thermodynamic;
/// see file header for the anchor justification.
/// </summary>
internal static class RefrigerationVoxelBuilder
{
    /// <summary>
    /// Compressor outer-radius anchor at 1 kW shaft input [mm]. Cluster
    /// mid of hermetic-rotary / scroll residential compressors used by
    /// the Sanden ECO-CUTE TR-series (R-744 transcritical), Daikin /
    /// Mitsubishi 1-3 kW split-AC scroll cluster, and small commercial
    /// refrigeration. Cluster OD spans 50-150 mm at the 1-10 kW class;
    /// 75 mm (= 37.5 mm radius) sits dead-centre at 1 kW shaft.
    /// </summary>
    internal const double CompressorRadiusAnchor1kW_mm = 37.5;

    /// <summary>
    /// Compressor length / outer-diameter aspect ratio. Cluster mid for
    /// hermetic-rotary residential compressors (Sanden TR / Bristol /
    /// Tecumseh). L/OD spans 1.0-2.5 across the cluster; 1.6 sits
    /// dead-centre.
    /// </summary>
    internal const double CompressorLengthToDiameterRatio = 1.6;

    /// <summary>
    /// Coil-bundle radial thickness [mm]. Cluster mid for residential
    /// heat-pump / refrigeration coils: 3 tube-diameters worth of
    /// helical / serpentine pack at the 6-12 mm cluster-mid 9.5 mm OD
    /// tubing (Sanden ECO-CUTE gas-cooler + outdoor evaporator).
    /// Spans 20-50 mm across the residential cluster; 30 mm at
    /// dead-centre.
    /// </summary>
    internal const double CoilBundleRadialThickness_mm = 30.0;

    /// <summary>
    /// Coil envelope length anchor at 3.5 kW heat-exchange duty [mm].
    /// Cluster mid for residential heat-pump coils: Sanden ECO-CUTE
    /// GUS-A45HOL gas-cooler active length ~ 250 mm at 4.5 kW heat
    /// delivery; outdoor evaporator coil ~ 200 mm at 3.5 kW heat
    /// extraction. Cluster spans 100-400 mm at the 1-10 kW class.
    /// </summary>
    internal const double CoilLengthAnchor3p5kW_mm = 200.0;

    /// <summary>
    /// Reference shaft power used to anchor the compressor OD scaling
    /// law [W]. Compressor OD scales as W^(1/3) (volumetric displacement
    /// at fixed RPM scales linearly with shaft power; the envelope
    /// radius is therefore the cube root).
    /// </summary>
    internal const double CompressorPowerReference_W = 1000.0;

    /// <summary>
    /// Reference heat duty used to anchor the coil-length scaling law
    /// [W]. Coil length scales as Q^(1/2) (heat-exchange area scales
    /// linearly with Q at fixed temperature glide; length grows as the
    /// square root of area for a roughly square wrap envelope).
    /// </summary>
    internal const double CoilHeatDutyReference_W = 3500.0;

    /// <summary>
    /// Wall-safe smoothing radius cap per PicoGK pitfall #1
    /// (<c>Smoothen(d)</c> destroys features &lt; 2d). 25 % of the
    /// minimum-feature dimension keeps the coil-bundle thickness and
    /// compressor envelope intact. Consistent with
    /// <see cref="Voxelforge.Flywheel.FlywheelVoxelBuilder"/> and
    /// <see cref="Voxelforge.Tankage.TankageVoxelBuilder"/>.
    /// </summary>
    internal const double SmoothingFeatureFraction = 0.25;

    /// <summary>
    /// Build the heat-pump assembly voxel body for <paramref name="design"/>.
    /// </summary>
    /// <param name="design">Validated refrigeration / heat-pump design
    ///   record. Must satisfy <see cref="RefrigerationDesign.ValidateSelf"/>.</param>
    /// <param name="voxelSize_mm">PicoGK voxel grid size in mm. Used only
    ///   for the wall-safe smoothing cap and the bounding-box padding.
    ///   The caller is responsible for constructing the ambient
    ///   <c>Library</c> at the matching voxel size.</param>
    /// <returns>Geometry summary + voxel handle.</returns>
    /// <exception cref="ArgumentNullException">design is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">voxelSize_mm is
    ///   non-positive.</exception>
    /// <exception cref="ArgumentException">design fails ValidateSelf —
    ///   propagated from the design record (None sentinel on Mode /
    ///   Refrigerant, non-positive reservoir temperatures, T_hot ≤
    ///   T_cold, non-positive W_compressor).</exception>
    internal static RefrigerationGeometryResult Build(
        RefrigerationDesign design, double voxelSize_mm)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (double.IsNaN(voxelSize_mm) || voxelSize_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(voxelSize_mm),
                $"voxelSize_mm={voxelSize_mm:F4} must be > 0.");
        design.ValidateSelf();

        // ── 1. Resolve thermodynamic quantities via the solver ────────
        // The solver gives us Q_cold and Q_hot from the same (T_cold,
        // T_hot, refrigerant, W_compressor) tuple. Coil lengths scale
        // with the matching heat duty.
        RefrigerationResult cycle = RefrigerationSolver.Solve(design);
        double Q_hot_W  = cycle.HotSideHeatDelivery_W;
        double Q_cold_W = cycle.ColdSideHeatRemoval_W;

        // ── 2. Compressor envelope (cluster-anchored) ─────────────────
        // R ∝ W^(1/3) anchored at 37.5 mm @ 1 kW.
        double powerRatio = design.CompressorPowerInput_W / CompressorPowerReference_W;
        double R_comp_mm  = CompressorRadiusAnchor1kW_mm * Math.Cbrt(powerRatio);
        double L_comp_mm  = CompressorLengthToDiameterRatio * 2.0 * R_comp_mm;

        // ── 3. Coil envelopes (cluster-anchored) ──────────────────────
        // Inner radius matches the compressor outer radius (coaxial
        // nesting). Bundle thickness is a fixed cluster-mid 30 mm. Coil
        // length scales as √(Q / 3500 W).
        double R_coil_inner_mm = R_comp_mm;
        double R_coil_outer_mm = R_comp_mm + CoilBundleRadialThickness_mm;
        double L_cond_mm = CoilLengthAnchor3p5kW_mm * Math.Sqrt(Q_hot_W  / CoilHeatDutyReference_W);
        double L_evap_mm = CoilLengthAnchor3p5kW_mm * Math.Sqrt(Q_cold_W / CoilHeatDutyReference_W);

        // ── 4. Axial layout (compressor centred on origin) ────────────
        double halfLcomp_mm = 0.5 * L_comp_mm;
        // Condenser sits on the +X (hot) side; evaporator on -X (cold) side.
        double xCondMin_mm = +halfLcomp_mm;
        double xCondMax_mm = +halfLcomp_mm + L_cond_mm;
        double xEvapMin_mm = -halfLcomp_mm - L_evap_mm;
        double xEvapMax_mm = -halfLcomp_mm;

        double overall_mm = L_comp_mm + L_cond_mm + L_evap_mm;
        double halfOverall_mm = 0.5 * overall_mm;

        // ── 5. Bounding box (axisymmetric on +X) ──────────────────────
        float halfOverall_f = (float)halfOverall_mm;
        float Router_f      = (float)R_coil_outer_mm;
        float pad_mm        = (float)Math.Max(2.0 * voxelSize_mm, 1.0);

        // The assembly is NOT centred on the origin in X (the compressor
        // is, but the coil envelopes are asymmetric in cooling-only vs
        // heating-only operating regimes — Q_hot ≠ Q_cold in general).
        // Use the actual extents.
        float xMin_f = (float)Math.Min(xEvapMin_mm, -halfLcomp_mm);
        float xMax_f = (float)Math.Max(xCondMax_mm, +halfLcomp_mm);
        var bounds = new BBox3(
            new Vector3(xMin_f - pad_mm, -Router_f - pad_mm, -Router_f - pad_mm),
            new Vector3(xMax_f + pad_mm,  Router_f + pad_mm,  Router_f + pad_mm));

        // ── 6. Build the three sub-envelopes ──────────────────────────
        // Compressor: solid cylinder via AnnulusImplicit(rInner=0).
        var compressorImpl = new AnnulusImplicit(
            xMin:   -(float)halfLcomp_mm,
            xMax:   +(float)halfLcomp_mm,
            rInner: 0f,
            rOuter: (float)R_comp_mm);
        Voxels assembly = LibraryScope.MakeVoxels(compressorImpl, bounds);

        // Condenser: thick annular shell on +X side.
        var condenserImpl = new AnnulusImplicit(
            xMin:   (float)xCondMin_mm,
            xMax:   (float)xCondMax_mm,
            rInner: (float)R_coil_inner_mm,
            rOuter: (float)R_coil_outer_mm);
        Voxels condenser = LibraryScope.MakeVoxels(condenserImpl, bounds);
        assembly.BoolAdd(condenser);

        // Evaporator: thick annular shell on -X side.
        var evaporatorImpl = new AnnulusImplicit(
            xMin:   (float)xEvapMin_mm,
            xMax:   (float)xEvapMax_mm,
            rInner: (float)R_coil_inner_mm,
            rOuter: (float)R_coil_outer_mm);
        Voxels evaporator = LibraryScope.MakeVoxels(evaporatorImpl, bounds);
        assembly.BoolAdd(evaporator);

        // ── 7. Wall-safe smoothing (PicoGK pitfall #1) ────────────────
        // Smoothen(d) destroys features < 2d → cap at 25 % of the
        // thinnest feature dimension. Candidates:
        //   - Coil bundle radial thickness (30 mm cluster)
        //   - Compressor outer radius
        //   - Individual envelope axial extents (smallest of L_comp,
        //     L_cond, L_evap)
        // Skip below 0.02 mm (sub-voxel noise floor) — consistent with
        // FlywheelVoxelBuilder / TankageVoxelBuilder.
        double minFeature_mm = Math.Min(
            Math.Min(CoilBundleRadialThickness_mm, R_comp_mm),
            Math.Min(L_comp_mm, Math.Min(L_cond_mm, L_evap_mm)));
        double safeSmooth_mm = SmoothingFeatureFraction * minFeature_mm;
        if (safeSmooth_mm >= 0.02)
            assembly.Smoothen((float)safeSmooth_mm);

        return new RefrigerationGeometryResult(
            CompressorOuterRadius_mm: R_comp_mm,
            CompressorLength_mm:      L_comp_mm,
            CondenserInnerRadius_mm:  R_coil_inner_mm,
            CondenserOuterRadius_mm:  R_coil_outer_mm,
            CondenserLength_mm:       L_cond_mm,
            EvaporatorInnerRadius_mm: R_coil_inner_mm,
            EvaporatorOuterRadius_mm: R_coil_outer_mm,
            EvaporatorLength_mm:      L_evap_mm,
            OverallLength_mm:         overall_mm,
            Voxels:                   new PicoGKVoxelHandle(assembly));
    }
}
