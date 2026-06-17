// CycleSolveResult.cs — bundled output from one cycle solve.
//
// Sprint A7 had Solve() return just StationMap. The post-A7 follow-on
// adds compressor + turbine map diagnostics (surge margin, corrected
// mass flow, choke margin) which are turbomachinery-component
// properties — not station-numbered properties — so they don't fit on
// StationMap. They ride on this CycleSolveResult instead.
//
// Diagnostics are nullable: ramjet has no rotating turbomachinery
// (both null); turbojet stand-in maps don't model off-design (both
// null); turbojet J85-class maps populate both with their map-derived
// MapInfo.

using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Bundled output from <see cref="IAirbreathingCycleSolver.Solve"/>.
/// Carries the station-numbered thermodynamic state plus optional
/// turbomachinery-component diagnostics from compressor + turbine
/// maps.
/// </summary>
/// <param name="Stations">Station-by-station thermodynamic state.</param>
/// <param name="CompressorDiagnostics">
/// Compressor map's off-design diagnostics — surge margin, corrected
/// mass flow, choke margin. Null when the engine has no compressor
/// (ramjet) or when the compressor map is a stand-in that doesn't
/// model off-design behaviour (<see cref="ConstantEfficiencyCompressorMap"/>).
/// </param>
/// <param name="TurbineDiagnostics">
/// Turbine map's off-design diagnostics. Null when the engine has no
/// turbine (ramjet) or when the turbine map is a stand-in.
/// </param>
public sealed record CycleSolveResult(
    StationMap Stations,
    MapInfo? CompressorDiagnostics,
    MapInfo? TurbineDiagnostics)
{
    /// <summary>
    /// Net shaft power output [W]. Non-zero only for
    /// <see cref="AirbreathingEngineKind.GasTurbine"/>; all propulsive
    /// cycle solvers (ramjet, turbojet, turbofan, scramjet, RBCC) leave
    /// this at its default of 0.
    /// </summary>
    public double ShaftPower_W { get; init; } = 0.0;

    /// <summary>
    /// Cycle thermal efficiency η_th = W_net / Q_fuel [-].
    /// Non-zero only for <see cref="AirbreathingEngineKind.GasTurbine"/>.
    /// </summary>
    public double ThermalEfficiency { get; init; } = 0.0;

    /// <summary>
    /// Specific work W_net / ṁ_air [J/kg].
    /// Non-zero only for <see cref="AirbreathingEngineKind.GasTurbine"/>.
    /// </summary>
    public double SpecificWork_Jkg { get; init; } = 0.0;

    /// <summary>
    /// Estimated buzz (acoustic) frequency [Hz] from
    /// <see cref="HalfWavePipeAcousticCalculator.CombinedFrequency_Hz"/> with
    /// variant dispatch (Standard reed-valve closed-open vs Valveless
    /// Lockwood-Hiller open-open). Non-NaN only for
    /// <see cref="AirbreathingEngineKind.Pulsejet"/>; all other kinds leave
    /// this at NaN.
    /// </summary>
    public double EstimatedBuzzFrequency_Hz { get; init; } = double.NaN;
}
