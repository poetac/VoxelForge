namespace Voxelforge.Optimization;

public sealed record RegenScoreResult(
    double TotalScore,
    double PeakWallT_K,
    double WallTMargin_K,
    double CoolantDP_Pa,
    double CoolantDP_Fraction,    // ΔP / P_c
    double CoolantTOut_K,
    double TotalHeatLoad_W,
    double ThroatHeatFlux_Wm2,
    double Mass_g,
    double Cost_USD,
    double MinFeatureSize_mm,
    double MinSafetyFactor,
    bool WallTExceeded,
    bool YieldExceeded,
    bool InfeasibleFeature,
    string[] Warnings,
    FeasibilityViolation[] FeasibilityViolations,
    // Structured severity grading for each warning. Kept alongside
    // the legacy flat Warnings string array so all existing tests +
    // external tooling keep working. The UI reads this when present
    // to render a colour-coded three-column table.
    SolverWarning[]? StructuredWarnings = null);
