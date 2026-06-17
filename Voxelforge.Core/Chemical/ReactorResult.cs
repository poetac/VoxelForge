// ReactorResult.cs — Sprint CHM.W1 solver output.

namespace Voxelforge.Chemical;

/// <summary>
/// Solve-time outputs for an ideal first-order reactor snapshot
/// (Sprint CHM.W1).
/// </summary>
/// <param name="RateConstant_per_s">k [1/s] from Arrhenius at the design T.</param>
/// <param name="ResidenceTime_s">τ = V / Q [s].</param>
/// <param name="DamkohlerNumber">Da₁ = k · τ [-] — dimensionless rate
/// product. Conversion is a closed-form function of Da only.</param>
/// <param name="Conversion">X [-] = (C_A0 − C_A) / C_A0 ∈ [0, 1].</param>
/// <param name="OutletConcentration_mol_m3">C_A [mol/m³] = C_A0 · (1 − X).</param>
/// <param name="ProductFormationRate_mol_s">ṅ_B = Q · C_A0 · X [mol/s].</param>
internal sealed record ReactorResult(
    double RateConstant_per_s,
    double ResidenceTime_s,
    double DamkohlerNumber,
    double Conversion,
    double OutletConcentration_mol_m3,
    double ProductFormationRate_mol_s);
