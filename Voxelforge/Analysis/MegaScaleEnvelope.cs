// MegaScaleEnvelope.cs — Meganewton-class voxel-build envelope
// analyser. Given a thrust class and a machine RAM budget, returns
// recommended (voxel_mm, tile_count, resource_mode) so a user can
// skip the manual trial + error on large-thrust designs.
//
// Why this exists
// ───────────────
// Earlier robustness work gave the tool the building blocks for
// large-thrust work (memory projection gate, auto-coarsen, axial
// tiling, subprocess isolation). What was missing was a CURATED MAP
// from "I want 100 kN" to "voxel 0.8 mm, tiles 4, Balanced budget"
// so users don't have to iterate manually. This module provides that
// map based on the cube-root projection math used by the memory
// projection gate, with safety factors tuned for the current 96 GB
// reference workstation tier (ADR-006 amendment 2026-05-17).
//
// Target hardware envelope (reference)
// ────────────────────────────────────
//   8 GB  → up to ~5 kN reliably at 0.4 mm voxel; Quiet mode
//   16 GB → up to ~20 kN reliably at 0.4 mm voxel; Balanced mode
//   32 GB → up to ~50 kN reliably at 0.4 mm voxel; Balanced mode
//   64 GB → up to ~100 kN at 0.4 mm; ~200 kN at 0.8 mm via tiling;
//            ~500 kN at 1.2 mm via tiling + Maximum mode
//            (historical anchor — see PresetsReferenceWorkstation)
//   96 GB → up to ~100 kN at 0.35 mm; ~200 kN at 0.7 mm via tiling;
//            ~500 kN at 1.05 mm via tiling + Maximum mode
//            (current canonical tier — see PresetsCurrent)
//   128 GB → up to ~500 kN at 0.6 mm with tiling; ~1 MN at 0.8 mm
//
// The canonical presets target the 96 GB current workstation tier
// (PresetsCurrent). The 64 GB anchor remains accessible as
// PresetsReferenceWorkstation for back-compat. Users on other
// hardware should call `Recommend(thrust, budgetBytes)` which
// recomputes the curve against their actual budget via cube-root
// scaling from the 96 GB anchor.

namespace Voxelforge.Analysis;

/// <summary>
/// Budgets — aligned to <see cref="UI.ResourceMode"/> (Quiet / Balanced
/// / Maximum) but expressed as a hint for CLI / headless callers that
/// don't link to the UI assembly. "Maximum" is uncapped — the caller
/// should set the Resource Budget to "Maximum" or route through the
/// STL-export subprocess so a native OOM doesn't take down the main app.
/// </summary>
public enum RecommendedResourceMode
{
    Quiet    = 0,
    Balanced = 1,
    Maximum  = 2,
}

/// <summary>
/// Output of <see cref="MegaScaleEnvelope.Recommend"/>. All fields
/// carry enough detail for the caller to directly configure a
/// build + report the projection rationale.
/// </summary>
public sealed record MegaScaleRecommendation(
    double                  Thrust_N,
    double                  VoxelSize_mm,
    int                     TileCount,
    bool                    EnableAutoCoarsen,
    RecommendedResourceMode ResourceMode,
    long                    ProjectedBudgetBytes,
    long                    ProjectedPeakBytes,
    bool                    Feasible,
    string                  Rationale);

/// <summary>
/// Pure-math meganewton-class envelope analyser. No PicoGK dependency;
/// thread-safe; deterministic.
/// </summary>
public static class MegaScaleEnvelope
{
    /// <summary>
    /// Historical 64 GB reference-workstation budget in bytes —
    /// ADR-006's original calibration anchor. Equivalent to selecting
    /// "Balanced" ResourceMode on that machine (50 % of system RAM).
    /// Retained for back-compat; new callers should target
    /// <see cref="Budget_Current_Balanced"/>.
    /// </summary>
    public const long Budget_ReferenceWorkstation_Balanced = 32L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Historical 64 GB workstation in "Maximum" mode (uncapped, but
    /// the practical working limit is ~58 GB leaving OS + app overhead).
    /// </summary>
    public const long Budget_ReferenceWorkstation_Maximum = 58L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Current 96 GB DDR5 workstation budget in bytes — the canonical
    /// target tier as of 2026-05-17 (see ADR-006 amendment). Equivalent
    /// to selecting "Balanced" ResourceMode on that machine (50 % of
    /// system RAM).
    /// </summary>
    public const long Budget_Current_Balanced = 48L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Current 96 GB workstation in "Maximum" mode (uncapped, but the
    /// practical working limit is ~87 GB leaving OS + app overhead).
    /// </summary>
    public const long Budget_Current_Maximum = 87L * 1024L * 1024L * 1024L;

    /// <summary>
    /// Historical preset table for the 64 GB reference workstation —
    /// ADR-006's original calibration anchor. Retained for back-compat
    /// and as the derivation source for <see cref="PresetsCurrent"/>.
    /// Derived from <see cref="MemoryProjectionGate.Project"/> at
    /// empirically representative bbox sizes per thrust class (bbox ≈
    /// 0.18 m + 0.12 × √(thrust/1 kN) meters in length by 0.06 m +
    /// 0.04 × √(thrust/1 kN) meters diameter), with a 1.5× safety
    /// factor on top of the default 0.50 sparsity to account for the
    /// temp grids during the voxel build (shell + channel stamping).
    /// Entries are sorted by thrust ascending.
    /// </summary>
    public static readonly (double Thrust_N, double Voxel_mm, int Tiles,
                            RecommendedResourceMode Mode)[] PresetsReferenceWorkstation =
    {
        (    500.0, 0.40, 1, RecommendedResourceMode.Balanced),
        (  2_224.0, 0.40, 1, RecommendedResourceMode.Balanced),
        ( 10_000.0, 0.40, 1, RecommendedResourceMode.Balanced),
        ( 20_000.0, 0.50, 2, RecommendedResourceMode.Balanced),
        ( 50_000.0, 0.60, 2, RecommendedResourceMode.Balanced),
        (100_000.0, 0.80, 4, RecommendedResourceMode.Balanced),
        (200_000.0, 1.00, 6, RecommendedResourceMode.Maximum),
        (500_000.0, 1.20, 8, RecommendedResourceMode.Maximum),
       (1_000_000.0, 1.50, 10, RecommendedResourceMode.Maximum),
       (2_000_000.0, 2.00, 16, RecommendedResourceMode.Maximum),
    };

    /// <summary>
    /// Canonical preset table for the 96 GB current workstation tier.
    /// Voxel sizes derived analytically from
    /// <see cref="PresetsReferenceWorkstation"/> via cube-root scaling
    /// at the budget ratio (32 GB → 48 GB Balanced):
    /// voxel_96 = voxel_64 × (32/48)^(1/3) ≈ voxel_64 × 0.874,
    /// rounded up to 0.01 mm (matching <see cref="Recommend"/>'s
    /// rounding convention). Tile counts and resource modes carried
    /// over conservatively — the larger budget tolerates them and the
    /// finer voxel does most of the resolution gain. See ADR-006
    /// amendment 2026-05-17 for derivation rationale.
    /// </summary>
    public static readonly (double Thrust_N, double Voxel_mm, int Tiles,
                            RecommendedResourceMode Mode)[] PresetsCurrent =
    {
        (    500.0, 0.35, 1, RecommendedResourceMode.Balanced),
        (  2_224.0, 0.35, 1, RecommendedResourceMode.Balanced),
        ( 10_000.0, 0.35, 1, RecommendedResourceMode.Balanced),
        ( 20_000.0, 0.44, 2, RecommendedResourceMode.Balanced),
        ( 50_000.0, 0.53, 2, RecommendedResourceMode.Balanced),
        (100_000.0, 0.70, 4, RecommendedResourceMode.Balanced),
        (200_000.0, 0.88, 6, RecommendedResourceMode.Maximum),
        (500_000.0, 1.05, 8, RecommendedResourceMode.Maximum),
       (1_000_000.0, 1.32, 10, RecommendedResourceMode.Maximum),
       (2_000_000.0, 1.75, 16, RecommendedResourceMode.Maximum),
    };

    /// <summary>Thrust floor accepted by <see cref="Recommend"/>.</summary>
    public const double MinThrust_N = 100.0;

    /// <summary>Thrust ceiling accepted by <see cref="Recommend"/>.</summary>
    public const double MaxThrust_N = 5_000_000.0;

    /// <summary>
    /// Return a recommended (voxel, tiles, mode) for the given thrust
    /// class on a specified RAM budget. When
    /// <paramref name="budgetBytes"/> is
    /// <see cref="Budget_Current_Balanced"/> the canonical 96 GB
    /// presets are used verbatim; otherwise the recommendation is
    /// rescaled using the cube-root memory-projection math.
    /// </summary>
    public static MegaScaleRecommendation Recommend(
        double thrust_N,
        long   budgetBytes = Budget_Current_Balanced,
        double autoCoarsenThreshold = 0.70)
    {
        if (thrust_N < MinThrust_N || thrust_N > MaxThrust_N)
            throw new ArgumentOutOfRangeException(nameof(thrust_N),
                $"Thrust {thrust_N:F0} N out of envelope [{MinThrust_N}, {MaxThrust_N}] N.");
        if (budgetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(budgetBytes),
                "Budget must be positive.");

        // Start from the 96 GB current-workstation preset for this thrust class.
        var preset = PickPresetBracket(thrust_N);

        // Rescale voxel for the actual budget using cube-root memory
        // math: a larger budget permits a finer voxel. Anchor is the
        // current-workstation Balanced budget, since PresetsCurrent
        // was calibrated at that point.
        double scaleFactor = Math.Pow((double)Budget_Current_Balanced / budgetBytes,
                                       1.0 / 3.0);
        double scaledVoxel = preset.Voxel_mm * scaleFactor;
        // Clamp to the PicoGK supported range.
        scaledVoxel = Math.Clamp(scaledVoxel, 0.05, 2.0);
        // Round UP to 2 decimal places so the recommendation is clean.
        scaledVoxel = Math.Ceiling(scaledVoxel * 100.0) / 100.0;

        // Tile count scales linearly with cube-root because tiles
        // reduce per-tile memory by dividing the X extent. Keep the
        // preset value for simplicity; future work can refine.
        int tiles = preset.Tiles;

        // Auto-coarsen enabled when thrust > 50 kN — if the user's
        // actual bounding box is larger than the preset assumes, the
        // gate trips and we fall back to a coarser voxel automatically.
        bool enableAutoCoarsen = thrust_N > 50_000.0;

        // Estimate peak memory using a representative bbox for this
        // thrust class so the user sees a concrete number, not just a
        // preset label.
        var estBbox = EstimateBoundingBox(thrust_N);
        var projection = MemoryProjectionGate.Project(
            estBbox.Lx_mm, estBbox.Ly_mm, estBbox.Lz_mm,
            voxelSize_mm: scaledVoxel,
            budgetBytes:   budgetBytes);

        bool feasible = projection.Level != MemoryProjectionLevel.Fail;

        string rationale = feasible
            ? $"Thrust {thrust_N / 1000.0:F1} kN → voxel {scaledVoxel:F2} mm × {tiles} tiles "
              + $"({preset.Mode} budget). Projected peak {projection.ProjectedBytes / (1024.0 * 1024 * 1024):F1} "
              + $"GB / {budgetBytes / (1024.0 * 1024 * 1024):F1} GB "
              + $"({projection.FractionOfBudget * 100:F0} %)."
              + (enableAutoCoarsen
                 ? "  Auto-coarsen enabled as a safety net."
                 : "")
            : $"Thrust {thrust_N / 1000.0:F1} kN INFEASIBLE at {scaledVoxel:F2} mm on "
              + $"{budgetBytes / (1024.0 * 1024 * 1024):F1} GB budget. Projected {projection.ProjectedBytes / (1024.0 * 1024 * 1024):F1} GB. "
              + "Consider a coarser voxel, more tiles, or a larger budget.";

        return new MegaScaleRecommendation(
            Thrust_N:             thrust_N,
            VoxelSize_mm:         scaledVoxel,
            TileCount:            tiles,
            EnableAutoCoarsen:    enableAutoCoarsen,
            ResourceMode:         preset.Mode,
            ProjectedBudgetBytes: budgetBytes,
            ProjectedPeakBytes:   projection.ProjectedBytes,
            Feasible:             feasible,
            Rationale:            rationale);
    }

    /// <summary>
    /// Representative chamber bounding box for a thrust class. Used
    /// by the envelope estimator + the pre-flight indicator. Scaling
    /// follows the Rao-contour + typical mounting-flange allowance
    /// observed across the implemented pairs; accuracy ±20 % which
    /// is plenty for projection purposes.
    /// </summary>
    public static BoundingBox EstimateBoundingBox(double thrust_N)
    {
        // Heuristic: chamber characteristic length grows as √thrust
        // (throat area scales with thrust, diameter with √area).
        double lengthFactor = 0.18 + 0.12 * Math.Sqrt(thrust_N / 1000.0);  // metres
        double radialFactor = 0.03 + 0.03 * Math.Sqrt(thrust_N / 1000.0);  // metres radius

        return new BoundingBox(
            Lx_mm: lengthFactor * 1000.0,
            Ly_mm: 2.0 * radialFactor * 1000.0,
            Lz_mm: 2.0 * radialFactor * 1000.0);
    }

    /// <summary>Axis-aligned bounding box in mm.</summary>
    public readonly record struct BoundingBox(double Lx_mm, double Ly_mm, double Lz_mm);

    /// <summary>
    /// Pick the preset bracket for a given thrust. Returns the
    /// FIRST preset whose thrust is ≥ the requested thrust, so the
    /// recommendation is always on the safe side (i.e. coarser voxel,
    /// more tiles than a perfectly-fit interpolation would give).
    /// </summary>
    internal static (double Thrust_N, double Voxel_mm, int Tiles, RecommendedResourceMode Mode)
        PickPresetBracket(double thrust_N)
    {
        foreach (var p in PresetsCurrent)
            if (p.Thrust_N >= thrust_N) return p;
        return PresetsCurrent[^1];
    }

    /// <summary>
    /// Sweep description for the Benchmarks <c>--mega-sweep</c> CLI
    /// mode. Each entry pairs a thrust class with its recommended
    /// configuration so the harness can drive <c>GenerateWithAutoCoarsen</c>
    /// + STL export at each point and write a baseline JSONL row.
    /// </summary>
    public sealed record SweepPoint(
        double                  Thrust_N,
        double                  VoxelSize_mm,
        int                     TileCount,
        RecommendedResourceMode ResourceMode);

    /// <summary>
    /// Build the canonical sweep for a given budget. Includes every
    /// preset whose thrust fits under <see cref="MaxThrust_N"/>.
    /// </summary>
    public static SweepPoint[] BuildSweep(long budgetBytes = Budget_Current_Balanced)
    {
        var result = new List<SweepPoint>();
        foreach (var p in PresetsCurrent)
        {
            if (p.Thrust_N < MinThrust_N || p.Thrust_N > MaxThrust_N) continue;
            var rec = Recommend(p.Thrust_N, budgetBytes);
            if (rec.Feasible)
                result.Add(new SweepPoint(rec.Thrust_N, rec.VoxelSize_mm,
                                           rec.TileCount, rec.ResourceMode));
        }
        return result.ToArray();
    }
}
