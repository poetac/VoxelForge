// IAirbreathingCycleSolver.cs — registry shape for per-kind solvers.
//
// Parallel to ICycleSolver on the rocket side. Distinct interface
// (not a shared "ICycleSolver" between rocket + air-breathing) per the
// parallel-pillar architectural decision:
// the rocket interface assumes a preburner topology; air-breathing's
// Brayton-cycle topology doesn't generalise to that shape. Unifying
// the two interfaces happens *after* both pillars are concrete (rule
// of three).

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Cycle-balance solver for one air-breathing engine kind. Each kind
/// (ramjet, turbojet, turbofan, scramjet, RBCC) ships exactly one
/// implementation registered in <see cref="AirbreathingCycleSolvers"/>.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe and deterministic — invoked
/// from <c>MultiChainOptimizer</c> across N concurrent chains and
/// must produce bit-identical results given identical inputs.
/// </remarks>
public interface IAirbreathingCycleSolver
{
    /// <summary>
    /// The engine kind this solver handles. The dispatcher
    /// (<see cref="AirbreathingCycleSolvers.Get"/>) keys on this.
    /// </summary>
    AirbreathingEngineKind Kind { get; }

    /// <summary>
    /// Solve the cycle for one design + flight-conditions pair.
    /// Returns the station-numbered thermodynamic state plus optional
    /// turbomachinery diagnostics (surge / choke margins, corrected
    /// mass flow) when the underlying maps populate them.
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="design"/>'s
    /// <see cref="AirbreathingEngineDesign.Kind"/> doesn't match
    /// <see cref="Kind"/>.
    /// </exception>
    CycleSolveResult Solve(AirbreathingEngineDesign design, FlightConditions cond);
}
