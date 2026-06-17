// ReactorDesign.cs — Sprint CHM.W1 chemical-reactor design record.
//
// Wave-1 models a single first-order irreversible reaction A → B with
// Arrhenius temperature dependence on the rate constant. Sized to
// bracket the methyl-acetate hydrolysis Levenspiel textbook example
// + industrial polymerization-class scale.

using System;

namespace Voxelforge.Chemical;

/// <summary>
/// Design parameters for an ideal first-order chemical reactor
/// (Sprint CHM.W1 scaffold).
/// </summary>
/// <param name="Kind">Reactor topology — CSTR or PFR.</param>
/// <param name="ReactorVolume_m3">V [m³].</param>
/// <param name="VolumetricFlowRate_m3s">Q [m³/s] — feed flow rate at
/// the inlet.</param>
/// <param name="InletConcentration_mol_m3">C_A0 [mol/m³] — feed
/// concentration of species A.</param>
/// <param name="OperatingTemperature_K">T [K] — reactor isothermal
/// operating temperature.</param>
/// <param name="ArrheniusPreExponential_per_s">A [1/s] — Arrhenius
/// pre-exponential factor for the first-order rate constant.</param>
/// <param name="ActivationEnergy_J_mol">E_a [J/mol] — Arrhenius
/// activation energy.</param>
internal sealed record ReactorDesign(
    ReactorKind Kind,
    double ReactorVolume_m3,
    double VolumetricFlowRate_m3s,
    double InletConcentration_mol_m3,
    double OperatingTemperature_K,
    double ArrheniusPreExponential_per_s,
    double ActivationEnergy_J_mol)
{
    // ── Wave-2 fields (Sprint CHM.W2) ───────────────────────────────────
    //
    // All new fields are init-only with backwards-compat defaults so that
    // Wave-1 (CHM.W1) callers see bit-identical behaviour.

    /// <summary>
    /// Reaction order (Sprint CHM.W2). Defaults to
    /// <see cref="ReactionOrder.First"/> for backwards-compat with
    /// CHM.W1 (first-order A → B). Set to
    /// <see cref="ReactionOrder.SecondInA"/> for r = k · C_A² kinetics.
    /// </summary>
    public ReactionOrder Order { get; init; } = ReactionOrder.First;

    /// <summary>
    /// Batch reactor elapsed time t [s] (Sprint CHM.W2). Used only when
    /// <c>Kind = Batch</c>. Ignored by CSTR / PFR (which use τ = V/Q
    /// instead). Defaults to 0; the validator demands &gt; 0 for Batch.
    /// </summary>
    public double BatchElapsedTime_s { get; init; } = 0.0;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">
    /// When <see cref="Kind"/> is the reserved <c>None</c> sentinel
    /// (categorical failure).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When any numeric field is NaN, non-positive (volume, flow, inlet
    /// concentration, temperature, pre-exponential), negative (activation
    /// energy), or — for <see cref="ReactorKind.Batch"/> — when the batch
    /// elapsed time is &lt;= 0.
    /// </exception>
    public void ValidateSelf()
    {
        if (Kind == ReactorKind.None)
            throw new ArgumentException(
                "Kind must be set (None sentinel is reserved).", nameof(Kind));
        if (double.IsNaN(ReactorVolume_m3) || ReactorVolume_m3 <= 0)
            throw new ArgumentOutOfRangeException(nameof(ReactorVolume_m3),
                $"ReactorVolume_m3={ReactorVolume_m3:E3} must be > 0.");
        if (double.IsNaN(VolumetricFlowRate_m3s) || VolumetricFlowRate_m3s <= 0)
            throw new ArgumentOutOfRangeException(nameof(VolumetricFlowRate_m3s),
                $"VolumetricFlowRate_m3s={VolumetricFlowRate_m3s:E3} must be > 0.");
        if (double.IsNaN(InletConcentration_mol_m3) || InletConcentration_mol_m3 <= 0)
            throw new ArgumentOutOfRangeException(nameof(InletConcentration_mol_m3),
                $"InletConcentration_mol_m3={InletConcentration_mol_m3:E3} must be > 0.");
        if (double.IsNaN(OperatingTemperature_K) || OperatingTemperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(OperatingTemperature_K),
                $"OperatingTemperature_K={OperatingTemperature_K:F1} must be > 0.");
        if (double.IsNaN(ArrheniusPreExponential_per_s) || ArrheniusPreExponential_per_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(ArrheniusPreExponential_per_s),
                $"ArrheniusPreExponential_per_s={ArrheniusPreExponential_per_s:E3} must be > 0.");
        if (double.IsNaN(ActivationEnergy_J_mol) || ActivationEnergy_J_mol < 0)
            throw new ArgumentOutOfRangeException(nameof(ActivationEnergy_J_mol),
                $"ActivationEnergy_J_mol={ActivationEnergy_J_mol:F1} must be ≥ 0.");
        if (Kind == ReactorKind.Batch
         && (double.IsNaN(BatchElapsedTime_s) || BatchElapsedTime_s <= 0))
            throw new ArgumentOutOfRangeException(nameof(BatchElapsedTime_s),
                $"BatchElapsedTime_s={BatchElapsedTime_s:F1} must be > 0 for "
              + "Batch reactors.");
    }
}
