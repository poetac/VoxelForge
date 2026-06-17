// RealGasGammaSolver.cs — Public solver shell over PropellantTables for
// computing mixture γ(T), cp(T), μ(T), MW for a resistojet inlet stream.
//
// Pure functions over the static lookup table. Solver shape mirrors
// the other three (electrothermal, isentropic-nozzle, radiation) so the
// pillar's solver layer reads uniformly.
//
// Citations:
//   NIST Chemistry WebBook (per-species property pages).
//   Sutton/Biblarz §16.2 (mixture cp via mass-fraction averaging).

using Voxelforge.ElectricPropulsion.Thermo;

namespace Voxelforge.ElectricPropulsion.Solvers;

/// <summary>
/// Real-gas γ / cp / μ / MW lookup over the four-species inlet
/// composition. Wraps <see cref="PropellantTables"/> with a stable
/// public surface.
/// </summary>
public static class RealGasGammaSolver
{
    /// <summary>
    /// Bulk-gas γ for the given inlet composition at temperature
    /// <paramref name="T_K"/>. Mass-averaged cp + cv, γ = cp/cv.
    /// </summary>
    public static double Gamma(PropellantInletComposition composition, double T_K)
        => PropellantTables.MixtureGamma(composition, T_K);

    /// <summary>
    /// Bulk-gas cp [J/(kg·K)] for the given inlet composition at
    /// temperature <paramref name="T_K"/>.
    /// </summary>
    public static double Cp(PropellantInletComposition composition, double T_K)
        => PropellantTables.MixtureCp(composition, T_K);

    /// <summary>
    /// Bulk-gas dynamic viscosity μ [Pa·s] for the given inlet composition
    /// at temperature <paramref name="T_K"/>.
    /// </summary>
    public static double Mu(PropellantInletComposition composition, double T_K)
        => PropellantTables.MixtureMu(composition, T_K);

    /// <summary>
    /// Mole-averaged molar mass [kg/mol] of the inlet composition.
    /// Independent of temperature.
    /// </summary>
    public static double MolarMass(PropellantInletComposition composition)
        => PropellantTables.MixtureMW(composition);

    /// <summary>
    /// Specific gas constant R = R_universal / MW [J/(kg·K)].
    /// </summary>
    public static double R_specific(PropellantInletComposition composition)
        => PropellantTables.R_universal / PropellantTables.MixtureMW(composition);

    /// <summary>
    /// Decomposition temperature limit for the mixture [K]. Composition
    /// dominated by NH₃ inherits ~1100 K; H₂-rich mixtures reach 3500 K.
    /// Used by the <c>RESISTOJET_PROPELLANT_DECOMPOSITION</c> Hard gate.
    /// </summary>
    public static double DecompositionLimit_K(PropellantInletComposition composition)
        => PropellantTables.MixtureDecompositionLimit_K(composition);
}
