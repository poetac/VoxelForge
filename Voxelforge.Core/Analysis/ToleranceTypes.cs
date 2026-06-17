namespace Voxelforge.Analysis;

/// <summary>
/// Records used by the tolerance Monte-Carlo sweep. Extracted to Core in A1
/// because RegenChamberOptimization (in Voxels) needs them for type
/// signatures, while the static ToleranceAnalysis class itself stays in
/// App because it depends on the orchestrator.
/// </summary>
public sealed record ToleranceInputs(
    int SampleCount = 400,
    double WallThicknessTolerance_mm = 0.10,     // ±3σ band (LPBF typical)
    double ChannelHeightTolerance_mm = 0.10,
    double RibThicknessTolerance_mm = 0.10,
    double JacketThicknessTolerance_mm = 0.10,
    int RandomSeed = 1);

public sealed record ToleranceQuantile(
    double P10, double P50, double P90, double P99);

public sealed record ToleranceResult(
    int SampleCount,
    ToleranceQuantile PeakWallT_K,
    ToleranceQuantile MinSafetyFactor,
    ToleranceQuantile CoolantPressureDrop_Pa,
    ToleranceQuantile CoolantOutletT_K,
    ToleranceQuantile ThroatHeatFlux_Wm2,
    int YieldExceededCount,
    int WallTLimitExceededCount,
    double MeanComputeTime_ms,
    string[] Warnings,
    // Hash of the (cond, design) this sweep was run against. Compare to
    // current gen.DesignHash to detect staleness.
    string DesignHash = "",
    // Per-sample raw draws so the ToleranceHistogramPanel can render an
    // actual distribution (not just the p10/p50/p90/p99 summary). Kept
    // as trailing optional fields so legacy tests constructing
    // ToleranceResult inline continue to compile. Null when the sweep
    // was run in a context that doesn't need the raw data.
    double[]? Samples_PeakWallT_K = null,
    double[]? Samples_MinSafetyFactor = null,
    double[]? Samples_CoolantPressureDrop_Pa = null,
    double[]? Samples_CoolantOutletT_K = null,
    double[]? Samples_ThroatHeatFlux_Wm2 = null);
