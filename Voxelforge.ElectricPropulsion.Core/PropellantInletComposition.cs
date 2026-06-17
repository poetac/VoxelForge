// PropellantInletComposition.cs — post-catalyst gas composition entering
// the resistojet heater.
//
// Carries mole fractions for the three species relevant to flown-fuel
// catalyst products (NH₃, N₂, H₂). For non-decomposed pure propellants
// (NH3 / H2 / H2O), the composition is a unit vector on the corresponding
// species; for hydrazine decomposed via Shell-405, the canonical
// composition is roughly 32 % NH₃ / 24 % N₂ / 44 % H₂ at 900 K bed exit
// (NASA TM-2002-211314 §3, Table 3.1).

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Mole-fraction breakdown of post-catalyst propellant entering the
/// resistojet heater. Sum of fractions must equal 1.0 within tolerance
/// (validated by <see cref="ValidateOrThrow"/>).
/// </summary>
/// <param name="NH3MoleFraction">Mole fraction of NH₃ (ammonia) in the inlet stream.</param>
/// <param name="N2MoleFraction">Mole fraction of N₂ (nitrogen) in the inlet stream.</param>
/// <param name="H2MoleFraction">Mole fraction of H₂ (hydrogen) in the inlet stream.</param>
/// <param name="H2OMoleFraction">Mole fraction of H₂O (water vapor) in the inlet stream.</param>
/// <remarks>
/// The four-species coverage is sufficient for Wave-1's four propellant
/// entries: pure NH3 (1, 0, 0, 0), N2H4 catalyst products (0.32, 0.24, 0.44, 0),
/// pure H2 (0, 0, 1, 0), pure H2O (0, 0, 0, 1).
/// </remarks>
public sealed record PropellantInletComposition(
    double NH3MoleFraction,
    double N2MoleFraction,
    double H2MoleFraction,
    double H2OMoleFraction)
{
    /// <summary>
    /// Canonical post-Shell-405 hydrazine catalyst-bed exit composition at
    /// 900 K. Per NASA TM-2002-211314 §3 Table 3.1.
    /// </summary>
    public static readonly PropellantInletComposition Hydrazine_Shell405 = new(
        NH3MoleFraction: 0.32,
        N2MoleFraction:  0.24,
        H2MoleFraction:  0.44,
        H2OMoleFraction: 0.00);

    /// <summary>Pure ammonia stream (no catalyst).</summary>
    public static readonly PropellantInletComposition PureNH3 = new(1.0, 0.0, 0.0, 0.0);

    /// <summary>Pure hydrogen stream.</summary>
    public static readonly PropellantInletComposition PureH2  = new(0.0, 0.0, 1.0, 0.0);

    /// <summary>Pure water-vapor stream.</summary>
    public static readonly PropellantInletComposition PureH2O = new(0.0, 0.0, 0.0, 1.0);

    /// <summary>
    /// Sum of all mole fractions. Should be 1.0 within
    /// <see cref="MoleFractionTolerance"/>.
    /// </summary>
    public double Sum => NH3MoleFraction + N2MoleFraction + H2MoleFraction + H2OMoleFraction;

    /// <summary>Tolerance band around 1.0 for the mole-fraction sum.</summary>
    public const double MoleFractionTolerance = 1e-6;

    /// <summary>
    /// Throws <see cref="System.ArgumentOutOfRangeException"/> if any
    /// fraction is negative or if the sum deviates from 1.0 by more
    /// than <see cref="MoleFractionTolerance"/>.
    /// </summary>
    public void ValidateOrThrow()
    {
        if (NH3MoleFraction < 0 || N2MoleFraction < 0 || H2MoleFraction < 0 || H2OMoleFraction < 0)
            throw new System.ArgumentOutOfRangeException(
                nameof(NH3MoleFraction),
                "All mole fractions must be non-negative.");
        if (System.Math.Abs(Sum - 1.0) > MoleFractionTolerance)
            throw new System.ArgumentOutOfRangeException(
                nameof(Sum),
                $"Mole fractions must sum to 1.0; got {Sum:F6}.");
    }
}
