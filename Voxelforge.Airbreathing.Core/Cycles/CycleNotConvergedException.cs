namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Thrown by <see cref="TurbofanCycleSolver.SolveAtOperatingPoint"/> when
/// the shaft-balance Newton loop exceeds its iteration limit without
/// converging to the required residual tolerance.
/// </summary>
/// <remarks>
/// Inherits <see cref="System.InvalidOperationException"/> to match the
/// rest of the repo's typed-exception hierarchy
/// (<c>CyclicComponentNetworkException</c>, <c>UnsupportedSchemaException</c>,
/// <c>MemoryBudgetExceededException</c>, <c>UnsupportedPropellantException</c>).
/// Pre-#558 PR-F this inherited <see cref="System.Exception"/> directly,
/// which was the sole outlier in the repo.
/// </remarks>
public sealed class CycleNotConvergedException : System.InvalidOperationException
{
    /// <summary>Number of Newton iterations attempted.</summary>
    public int Iterations { get; }

    /// <summary>
    /// Normalised shaft-balance residual at the final iteration
    /// (|R| / W_nominal).
    /// </summary>
    public double FinalResidual { get; }

    public CycleNotConvergedException(int iterations, double finalResidual)
        : base($"Turbofan operating-point Newton loop failed to converge in "
             + $"{iterations} iterations; final residual = {finalResidual:G4}.")
    {
        Iterations = iterations;
        FinalResidual = finalResidual;
    }
}
