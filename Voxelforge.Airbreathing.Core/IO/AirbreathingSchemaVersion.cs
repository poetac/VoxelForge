namespace Voxelforge.Airbreathing.IO;

internal static class AirbreathingSchemaVersion
{
    /// <summary>
    /// All accepted schema versions on read.
    ///   v11 (Sprint A.W3) — adds 4 init-only LACE fields
    ///     (PrecoolerEffectiveness, LH2MassFlow_kgs, LaceChamberPressure_bar,
    ///     LaceAirToFuelRatio) with 0.0 defaults; identity migration since
    ///     the LACE pipeline only runs when AirbreathingEngineKind =
    ///     LiquidAirCycle.
    ///   v12 (Sprint A.W4) — adds 4 init-only RDE numeric fields
    ///     (RdePressureGainRatio, RdeAnnularOuterDiameter_m,
    ///     RdeAnnularInnerDiameter_m, RdeAnnularLength_m) + 1 int field
    ///     (RdeWaveCount) with 0/0.0 defaults; identity migration since the
    ///     RDE pipeline only runs when AirbreathingEngineKind =
    ///     RotatingDetonation.
    /// </summary>
    internal const string Current = "v12";
    internal static readonly string[] Known = { "v1", "v2", "v3", "v4", "v5", "v6", "v7", "v8", "v9", "v10", "v11", "v12" };
    internal static bool IsSupported(string version) => Array.IndexOf(Known, version) >= 0;
}
