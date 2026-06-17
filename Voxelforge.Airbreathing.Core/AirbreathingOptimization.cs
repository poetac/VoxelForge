// AirbreathingOptimization.cs — top-level entry point for the
// air-breathing pillar.
//
// Parallel to RegenChamberOptimization.GenerateWith on the rocket
// side. Sprint A1 ships the GenerateWith dispatch shape — solve cycle
// → evaluate gates → return result. Sprint A4+ wires real solvers in.
// IObjective wrapper for SA / CMA-ES / NSGA-II consumption ships in
// A5.

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing;

/// <summary>
/// Top-level orchestration for the air-breathing pillar.
/// </summary>
public static class AirbreathingOptimization
{
    /// <summary>
    /// Solve a single design + flight-conditions pair end-to-end.
    /// Dispatches to the correct cycle solver, evaluates feasibility
    /// gates, and returns the consolidated <see cref="AirbreathingResult"/>.
    /// </summary>
    /// <exception cref="System.NotSupportedException">
    /// Thrown when the design's <see cref="AirbreathingEngineDesign.Kind"/>
    /// has no cycle solver registered (per Sprint A1: every kind).
    /// </exception>
    public static AirbreathingResult GenerateWith(
        AirbreathingEngineDesign design,
        FlightConditions cond)
    {
        var solver = AirbreathingCycleSolvers.Get(design.Kind);
        var solveResult = solver.Solve(design, cond);
        var (gates, advisories) = AirbreathingFeasibility.Evaluate(
            design, cond, solveResult.Stations,
            solveResult.CompressorDiagnostics,
            solveResult.TurbineDiagnostics);
        return new AirbreathingResult(
            Design: design,
            Conditions: cond,
            Stations: solveResult.Stations,
            Violations: gates.Violations,
            IsFeasible: gates.IsFeasible)
        {
            Advisories = advisories,
            ShaftPower_W = solveResult.ShaftPower_W,
            ThermalEfficiency = solveResult.ThermalEfficiency,
            SpecificWork_Jkg = solveResult.SpecificWork_Jkg,
            EstimatedBuzzFrequency_Hz = solveResult.EstimatedBuzzFrequency_Hz,
        };
    }
}
