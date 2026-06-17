// MemoryProjectionGate.cs — preflight memory-budget check. Answers
// "will this voxel build fit inside the user's configured memory cap
// before we actually allocate?"
//
// Why separate from `VoxelAdequacyGate`: that gate is pure geometry
// (feature size / voxel pitch) and fires AFTER geometry is generated.
// This gate is pre-allocation — it runs on the user-set (or
// session-fixed) voxel size, bounding-box estimate, and sparsity
// factor, so the main app / StlExporter can short-circuit BEFORE
// `new Voxels(...)` and surface a clear "would need 14 GB, budget
// 8 GB" message.
//
// The sparsity factor is the single empirical knob — OpenVDB's
// sparse tree compresses the dense-equivalent voxel count by
// roughly 30-60 % on a typical chamber shape (most voxels are
// outside the thin shell). We default to 0.50 (conservative for
// safety), with the intent that a future Benchmarks calibration
// run will pin it tighter.

namespace Voxelforge.Analysis;

public enum MemoryProjectionLevel
{
    Pass = 0,
    /// <summary>Projected 70-100 % of budget — warn but don't block.</summary>
    Warning = 1,
    /// <summary>Projected > 100 % of budget — block.</summary>
    Fail = 2,
}

public sealed record MemoryProjection(
    long                  ProjectedBytes,
    long                  BudgetBytes,
    double                FractionOfBudget,
    MemoryProjectionLevel Level,
    string                Message);

/// <summary>
/// Thrown by <see cref="MemoryProjectionGate.EnsureFits"/>
/// when the projected voxel footprint exceeds the configured memory
/// budget. Caught at the Program.cs / UI boundary so the app fails
/// gracefully instead of allocating, thrashing pagefile for hours, and
/// eventually OS-killing the process. The field <see cref="SuggestedVoxelSize_mm"/>
/// carries a coarser voxel that WOULD fit (cube-root scaling), so the
/// user can click Generate again without re-guessing.
/// </summary>
public sealed class MemoryBudgetExceededException : System.InvalidOperationException
{
    public long                  ProjectedBytes      { get; }
    public long                  BudgetBytes         { get; }
    public double                FractionOfBudget    { get; }
    public double                RequestedVoxel_mm   { get; }
    public double                SuggestedVoxel_mm   { get; }

    public MemoryBudgetExceededException(
        MemoryProjection p, double requestedVoxel_mm, double suggestedVoxel_mm, string message)
        : base(message)
    {
        ProjectedBytes    = p.ProjectedBytes;
        BudgetBytes       = p.BudgetBytes;
        FractionOfBudget  = p.FractionOfBudget;
        RequestedVoxel_mm = requestedVoxel_mm;
        SuggestedVoxel_mm = suggestedVoxel_mm;
    }
}

public static class MemoryProjectionGate
{
    /// <summary>
    /// Bytes per voxel used in the projection. Four bytes — OpenVDB
    /// FloatGrid. Multiplied by the sparsity factor and the dense-
    /// equivalent voxel count to give a conservative footprint.
    /// </summary>
    public const int BytesPerVoxel = 4;

    /// <summary>
    /// Fraction of the dense-equivalent voxel count that actually
    /// ends up allocated in the sparse tree. 0.50 is a conservative
    /// default (real chambers typically hit 0.30-0.40, but the
    /// sweep across all stages includes temp inner/outer grids + N
    /// channel grids simultaneously during the loop). Exposed as a
    /// parameter so the Benchmarks app can calibrate.
    /// </summary>
    public const double DefaultSparsityFactor = 0.50;

    /// <summary>Warning threshold as a fraction of the budget.</summary>
    public const double WarningFraction = 0.70;

    /// <summary>
    /// Project the working-set footprint for a voxel build with
    /// bounding-box dimensions (Lx, Ly, Lz mm) and the given voxel
    /// size. Returns a <see cref="MemoryProjection"/> with the
    /// Pass/Warning/Fail level based on <paramref name="budgetBytes"/>.
    /// </summary>
    public static MemoryProjection Project(
        double boundingLx_mm,
        double boundingLy_mm,
        double boundingLz_mm,
        double voxelSize_mm,
        long   budgetBytes,
        double sparsityFactor = DefaultSparsityFactor)
    {
        if (voxelSize_mm <= 0)
            return new MemoryProjection(0, budgetBytes, 0, MemoryProjectionLevel.Pass,
                "Voxel size not specified — projection skipped.");
        if (budgetBytes <= 0)
            return new MemoryProjection(0, 0, 0, MemoryProjectionLevel.Pass,
                "No memory budget set — projection skipped (use ResourceMode != Maximum to enable).");

        double sparsity = System.Math.Clamp(sparsityFactor, 0.05, 1.0);
        long nx = (long)(boundingLx_mm / voxelSize_mm);
        long ny = (long)(boundingLy_mm / voxelSize_mm);
        long nz = (long)(boundingLz_mm / voxelSize_mm);
        long dense = System.Math.Max(1, nx) * System.Math.Max(1, ny) * System.Math.Max(1, nz);
        long projected = (long)(dense * sparsity * BytesPerVoxel);

        double frac = (double)projected / budgetBytes;
        MemoryProjectionLevel level =
            frac > 1.0            ? MemoryProjectionLevel.Fail :
            frac > WarningFraction ? MemoryProjectionLevel.Warning :
                                     MemoryProjectionLevel.Pass;

        string msg = level switch
        {
            MemoryProjectionLevel.Fail =>
                $"Projected {projected / 1_048_576:N0} MB > budget {budgetBytes / 1_048_576:N0} MB "
                + $"at voxel {voxelSize_mm:F2} mm. Coarsen voxel size or raise the Resource Budget cap.",
            MemoryProjectionLevel.Warning =>
                $"Projected {projected / 1_048_576:N0} MB ({frac*100:F0} % of budget) at voxel {voxelSize_mm:F2} mm. "
                + "Close to the cap — expect the subprocess to degrade other apps.",
            _ =>
                $"Projected {projected / 1_048_576:N0} MB ({frac*100:F0} % of budget) at voxel {voxelSize_mm:F2} mm.",
        };

        return new MemoryProjection(projected, budgetBytes, frac, level, msg);
    }

    /// <summary>
    /// Throw <see cref="MemoryBudgetExceededException"/>
    /// if the projection lands in <see cref="MemoryProjectionLevel.Fail"/>.
    /// Returns the (possibly Pass / Warning) projection otherwise so
    /// callers can log the fraction-of-budget without re-projecting.
    /// Pass <c>budgetBytes &lt;= 0</c> (e.g. Maximum resource mode) to
    /// skip the gate entirely — the projection still runs informationally.
    /// </summary>
    public static MemoryProjection EnsureFits(
        double boundingLx_mm,
        double boundingLy_mm,
        double boundingLz_mm,
        double voxelSize_mm,
        long   budgetBytes,
        double sparsityFactor = DefaultSparsityFactor)
    {
        var p = Project(boundingLx_mm, boundingLy_mm, boundingLz_mm,
                        voxelSize_mm, budgetBytes, sparsityFactor);
        if (p.Level != MemoryProjectionLevel.Fail) return p;

        double suggested = SuggestCoarserVoxel(
            boundingLx_mm, boundingLy_mm, boundingLz_mm,
            voxelSize_mm, budgetBytes, sparsityFactor);
        string msg = p.Message
            + $"  Try voxel ≥ {suggested:F2} mm or raise the Resource Budget cap.";
        throw new MemoryBudgetExceededException(p, voxelSize_mm, suggested, msg);
    }

    /// <summary>
    /// Cheap pre-flight projection from design-level inputs (no voxel
    /// build, no physics). Builds the chamber contour (sub-ms) + channel
    /// schedule, then reuses the bbox math from the main-path gate to
    /// project the voxel-grid footprint. The result powers the status-bar
    /// memory indicator so the user sees "Next Generate → 8.2 GB / 16 GB
    /// budget" live as they adjust thrust / voxel / material inputs — no
    /// need to click Generate and wait for the gate to trip. Thread-safe
    /// pure function.
    /// </summary>
    public static MemoryProjection ProjectPreflight(
        Voxelforge.Optimization.OperatingConditions cond,
        Voxelforge.Optimization.RegenChamberDesign  design,
        double voxelSize_mm,
        long   budgetBytes,
        double sparsityFactor = DefaultSparsityFactor)
    {
        if (voxelSize_mm <= 0)
            return new MemoryProjection(0, budgetBytes, 0, MemoryProjectionLevel.Pass,
                "No voxel size selected.");
        try
        {
            var gas = Voxelforge.Combustion.PropellantTables.Lookup(
                cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
            var derived = Voxelforge.Optimization.RegenChamberOptimization
                                              .ComputeDerived(cond, gas, design);
            var contour = Voxelforge.Chamber.ChamberContourGenerator.Generate(
                throatRadius_mm:         derived.ThroatRadius_mm,
                contractionRatio:        design.ContractionRatio,
                expansionRatio:          design.ExpansionRatio,
                characteristicLength_m:  design.CharacteristicLength_m,
                thetaN_deg:              design.BellEntranceAngle_deg,
                thetaE_deg:              design.BellExitAngle_deg,
                bellLengthFraction:      design.BellLengthFraction,
                stationCount:            design.ContourStationCount,
                dualBell:                design.IncludeDualBell,
                seaLevelExpansionRatio:  design.SeaLevelExpansionRatio,
                inflectionAngleDeg:      design.InflectionAngle_deg);

            double maxR_mm = 0.0;
            foreach (var s in contour.Stations)
                if (s.R_mm > maxR_mm) maxR_mm = s.R_mm;

            double outerWall_mm = maxR_mm
                + design.GasSideWallThickness_mm
                + System.Math.Max(design.ChannelHeightChamber_mm,
                      System.Math.Max(design.ChannelHeightThroat_mm, design.ChannelHeightExit_mm))
                + 2.0;
            double flangeR_mm = outerWall_mm + 10.0;
            double bboxLx_mm  = contour.TotalLength_mm
                              + (design.IncludeInjectorFlange ? design.InjectorFlangeThickness_mm : 0.0)
                              + (design.IncludeMountingFlange ? design.MountingFlangeThickness_mm : 0.0)
                              + 30.0;
            double bboxLy_mm = 2.0 * (flangeR_mm + 5.0);
            double bboxLz_mm = bboxLy_mm;

            return Project(bboxLx_mm, bboxLy_mm, bboxLz_mm, voxelSize_mm, budgetBytes, sparsityFactor);
        }
        catch (System.Exception)
        {
            // Unsupported propellant pair / malformed design / etc. — we
            // don't want the status-bar indicator to throw in the user's
            // face, so fall through to "unknown projection" Pass.
            return new MemoryProjection(0, budgetBytes, 0, MemoryProjectionLevel.Pass,
                "Projection unavailable (design inputs incomplete).");
        }
    }

    /// <summary>
    /// Back-solve for the coarsest voxel size that fits inside
    /// <paramref name="budgetBytes"/> using the dense-voxel cube-root
    /// relation. Rounds UP to 2 decimal places (mm) so the result is
    /// user-friendly and conservative against the sparsity estimate.
    /// Returns <see cref="double.NaN"/> if no valid voxel exists (budget
    /// is zero or bbox is degenerate).
    /// </summary>
    public static double SuggestCoarserVoxel(
        double boundingLx_mm,
        double boundingLy_mm,
        double boundingLz_mm,
        double voxelSize_mm,
        long   budgetBytes,
        double sparsityFactor = DefaultSparsityFactor)
    {
        if (budgetBytes <= 0 || voxelSize_mm <= 0) return double.NaN;
        if (boundingLx_mm <= 0 || boundingLy_mm <= 0 || boundingLz_mm <= 0)
            return double.NaN;

        double sparsity = System.Math.Clamp(sparsityFactor, 0.05, 1.0);

        // Projected bytes ∝ 1 / voxelSize³ (linear dims bbox / voxel).
        // So: voxel_safe = voxel_requested × (projected / budget)^(1/3).
        var p = Project(boundingLx_mm, boundingLy_mm, boundingLz_mm,
                        voxelSize_mm, budgetBytes, sparsity);
        if (p.ProjectedBytes <= 0) return double.NaN;

        // Target: land at 80 % of budget (not 100 %) so the user has
        // headroom for tolerance sweep + SA batch overlap.
        double targetFrac = 0.80;
        double ratio = (double)p.ProjectedBytes / (budgetBytes * targetFrac);
        double coarser = voxelSize_mm * System.Math.Pow(ratio, 1.0 / 3.0);

        // Round UP to 2 decimal places (0.01 mm) so the suggestion is
        // readable and comfortably inside the target.
        return System.Math.Ceiling(coarser * 100.0) / 100.0;
    }
}
