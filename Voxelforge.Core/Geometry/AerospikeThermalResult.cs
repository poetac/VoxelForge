namespace Voxelforge.Geometry;

public sealed record AerospikeThermalResult(
    double[] GasSideWallT_K,           // peak plug-surface wall T per station
    double[] CoolantBulkT_K,
    double[] HeatFlux_Wm2,
    double   PeakGasSideWallT_K,
    double   PeakStation_X_mm,
    double   CoolantOutletT_K,
    double   CoolantPressureDrop_Pa,
    double   TotalHeatLoad_W,
    string[] Warnings,
    // Defaulted so every existing call site keeps compiling with
    // named arguments.
    int      CavitationRiskStationCount = 0,
    double   MinCoolantPressure_Pa      = 0.0,
    // PH-42 (#187, 2026-04-29): informational notes on solver
    // assumptions distinct from <see cref="Warnings"/> (which fires only
    // when something went wrong). The cooling solver always emits a note
    // about the Angelino linear-Prandtl-Meyer approximation backing the
    // local Mach march along the plug surface — accuracy ±25 % per
    // station, near-zero on plug-integral wetted-area heat load. Slated
    // for replacement by the CFD-derived M(x) table once T2.3 (#160) ships.
    string[]? Notes                     = null);
