// StirlingResult.cs — Sprint STR.W1 solver output.

namespace Voxelforge.Stirling;

/// <summary>
/// Solve-time outputs for a Stirling-engine snapshot (Sprint STR.W1).
/// </summary>
/// <remarks>
/// <b>Accuracy caveat:</b> the Wave-1 mean-effective-pressure fit
/// over-predicts free-piston Stirling power by <b>10–100×</b>, so the
/// power-bearing outputs (<c>IndicatedPower_W</c>, <c>HeatInputRate_W</c>,
/// <c>HeatRejectionRate_W</c>) are order-of-magnitude only — not validated
/// numbers. See LIMITATIONS.md, "Validated free-piston Stirling output".
/// No defensible validation fixture lands until the MEP model is refined.
/// </remarks>
/// <param name="CarnotEfficiency">η_Carnot = 1 − T_cold / T_hot [-].</param>
/// <param name="IndicatedEfficiency">η_indicated = η_2nd · η_Carnot [-].</param>
/// <param name="MeanEffectivePressure_Pa">BMEP-equivalent [Pa] —
/// scaffold heuristic: 0.5 · P_mean (Schmidt-style indicated-work-
/// fraction; real Stirling MEP is 0.3-0.7 of P_mean depending on
/// configuration + dead volume).</param>
/// <param name="WorkPerCycle_J">W = MEP · V_swept [J/cycle].</param>
/// <param name="IndicatedPower_W">P_indicated = W · f [W]. Order-of-magnitude
/// only — the Wave-1 MEP fit over-predicts free-piston power 10–100× (see
/// LIMITATIONS.md and the type-level accuracy caveat).</param>
/// <param name="HeatInputRate_W">Q_hot = P_indicated / η_indicated [W].</param>
/// <param name="HeatRejectionRate_W">Q_cold = Q_hot − P_indicated [W].</param>
internal sealed record StirlingResult(
    double CarnotEfficiency,
    double IndicatedEfficiency,
    double MeanEffectivePressure_Pa,
    double WorkPerCycle_J,
    double IndicatedPower_W,
    double HeatInputRate_W,
    double HeatRejectionRate_W);
