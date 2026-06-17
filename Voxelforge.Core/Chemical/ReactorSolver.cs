// ReactorSolver.cs — Sprint CHM.W1 closed-form ideal-reactor solver.
//
// Stateless, allocation-free, deterministic. Models a first-order
// irreversible reaction A → B in either a CSTR or a PFR at constant
// temperature. The rate constant is Arrhenius:
//
//   k(T) = A · exp(−E_a / (R · T))
//
// Conversion at residence time τ = V/Q in dimensionless Damkohler
// number Da = k·τ:
//
//   CSTR: X = Da / (1 + Da)
//   PFR:  X = 1 − exp(−Da)
//
// At any positive Da the PFR has higher conversion than the CSTR; the
// gap collapses at Da → 0 (both give X → 0) and Da → ∞ (both → 1).
//
// References:
//   Levenspiel O. (1999). "Chemical Reaction Engineering," 3rd ed.,
//     chaps 4–5 (ideal-reactor design equations).
//   Fogler H.S. (2020). "Elements of Chemical Reaction Engineering,"
//     5th ed., chap 4.

using System;

namespace Voxelforge.Chemical;

/// <summary>
/// Closed-form ideal first-order reactor performance snapshot solver
/// (Sprint CHM.W1).
/// </summary>
internal static class ReactorSolver
{
    /// <summary>Universal gas constant [J/(mol·K)].</summary>
    internal const double R_J_molK = 8.31446;

    /// <summary>
    /// Solve the chemical reactor snapshot at the design operating point.
    /// </summary>
    internal static ReactorResult Solve(ReactorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // 1. Arrhenius rate constant.
        double k = design.ArrheniusPreExponential_per_s
                 * Math.Exp(-design.ActivationEnergy_J_mol
                           / (R_J_molK * design.OperatingTemperature_K));

        // 2. Time / residence time + Damkohler.
        //    For CSTR + PFR: τ = V/Q is the residence time and drives Da.
        //    For Batch: the elapsed time t replaces τ in the Damkohler
        //    formula — Batch is a closed system with no inflow.
        double tau = design.Kind == ReactorKind.Batch
            ? design.BatchElapsedTime_s
            : design.ReactorVolume_m3 / design.VolumetricFlowRate_m3s;
        // For 1st-order: Da_1 = k·τ; for 2nd-order: Da_2 = k·C_A0·τ. The
        // generalised Damkohler used for the conversion closed-form is
        // chosen per-order below.
        double Da_first  = k * tau;
        double Da_second = k * design.InletConcentration_mol_m3 * tau;

        // 3. Conversion per (reactor topology, reaction order).
        double X = design.Order switch
        {
            ReactionOrder.First     => ComputeFirstOrderConversion(design.Kind, Da_first),
            ReactionOrder.SecondInA => ComputeSecondOrderConversion(design.Kind, Da_second),
            // NotSupportedException (vs InvalidOperationException) per house
            // style (#576 / #558 PR-F): the object isn't in a bad state, the
            // enum variant just isn't implemented yet.
            _ => throw new NotSupportedException(
                     $"Unhandled ReactionOrder '{design.Order}'."),
        };
        double Da = design.Order == ReactionOrder.First ? Da_first : Da_second;

        // 4. Outlet concentration + product formation rate.
        double C_A_out = design.InletConcentration_mol_m3 * (1.0 - X);
        double n_dot_B = design.VolumetricFlowRate_m3s
                       * design.InletConcentration_mol_m3 * X;

        return new ReactorResult(
            RateConstant_per_s:           k,
            ResidenceTime_s:              tau,
            DamkohlerNumber:              Da,
            Conversion:                   X,
            OutletConcentration_mol_m3:   C_A_out,
            ProductFormationRate_mol_s:   n_dot_B);
    }

    /// <summary>
    /// Compute the rate constant at an arbitrary temperature for the
    /// given Arrhenius parameters. Public-static helper for
    /// temperature-sweep studies.
    /// </summary>
    internal static double ComputeArrheniusRateConstant(
        double preExponential_per_s,
        double activationEnergy_J_mol,
        double temperature_K)
    {
        if (preExponential_per_s <= 0)
            throw new ArgumentOutOfRangeException(nameof(preExponential_per_s),
                "A must be > 0.");
        if (activationEnergy_J_mol < 0)
            throw new ArgumentOutOfRangeException(nameof(activationEnergy_J_mol),
                "E_a must be ≥ 0.");
        if (temperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(temperature_K),
                "T must be > 0.");
        return preExponential_per_s
             * Math.Exp(-activationEnergy_J_mol / (R_J_molK * temperature_K));
    }

    /// <summary>
    /// Compute conversion X for a first-order reaction at the given
    /// Damkohler number Da_1 = k·τ in the specified reactor topology
    /// (Sprint CHM.W2 helper).
    /// </summary>
    internal static double ComputeFirstOrderConversion(
        ReactorKind kind, double damkohler)
    {
        if (damkohler < 0)
            throw new ArgumentOutOfRangeException(nameof(damkohler),
                "Da must be ≥ 0.");
        return kind switch
        {
            // CSTR (steady-state, perfect mixing): X = Da/(1+Da).
            ReactorKind.Cstr  => damkohler / (1.0 + damkohler),
            // PFR (steady-state, no axial mixing): X = 1 − exp(−Da).
            // Batch with elapsed-time t: X(t) = 1 − exp(−k·t) — identical
            // closed-form as PFR (both integrate the 1st-order ODE
            // dC/dx = -k·C from C_A0 at x = 0 to C_A at x = τ or t = t).
            ReactorKind.Pfr   => 1.0 - Math.Exp(-damkohler),
            ReactorKind.Batch => 1.0 - Math.Exp(-damkohler),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    $"Unhandled ReactorKind '{kind}'."),
        };
    }

    /// <summary>
    /// Compute conversion X for a second-order (in A) reaction at the
    /// given Damkohler number Da_2 = k·C_A0·τ (Sprint CHM.W2 helper).
    /// </summary>
    internal static double ComputeSecondOrderConversion(
        ReactorKind kind, double damkohler)
    {
        if (damkohler < 0)
            throw new ArgumentOutOfRangeException(nameof(damkohler),
                "Da must be ≥ 0.");
        return kind switch
        {
            // CSTR (steady-state): Da·X² − (1 + 2·Da)·X + Da = 0
            //   → X = ((1 + 2·Da) − √(1 + 4·Da)) / (2·Da) for Da > 0,
            //   → X = 0 at Da = 0 (Taylor limit).
            ReactorKind.Cstr => damkohler <= 1e-12
                ? 0.0
                : ((1.0 + 2.0 * damkohler) - Math.Sqrt(1.0 + 4.0 * damkohler))
                    / (2.0 * damkohler),
            // PFR / Batch (closed-form integration of dC/dx = -k·C²):
            //   C_A = C_A0 / (1 + k·C_A0·τ) → X = Da / (1 + Da).
            ReactorKind.Pfr   => damkohler / (1.0 + damkohler),
            ReactorKind.Batch => damkohler / (1.0 + damkohler),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
                    $"Unhandled ReactorKind '{kind}'."),
        };
    }
}
