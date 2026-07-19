// StirlingResult.cs — Sprint STR.W1 solver output. Issue #84 (STR.W2):
// MEP model refined to West's number correlation; the STR.W1
// 10-100x-over-prediction accuracy caveat is resolved (see
// StirlingSolver.cs file header for the calibration + cross-check).

namespace Voxelforge.Stirling;

/// <summary>
/// Solve-time outputs for a Stirling-engine snapshot.
/// </summary>
/// <param name="CarnotEfficiency">η_Carnot = 1 − T_cold / T_hot [-].</param>
/// <param name="IndicatedEfficiency">η_indicated = η_2nd · η_Carnot [-].</param>
/// <param name="MeanEffectivePressure_Pa">Mean effective pressure [Pa] —
/// West's number correlation: Wn · P_mean · τ · fluidFactor, where
/// τ = (T_hot−T_cold)/(T_hot+T_cold) and Wn ≈ 0.25 (see StirlingSolver.cs
/// for the full derivation and calibration cross-check).</param>
/// <param name="WorkPerCycle_J">W = MEP · V_swept [J/cycle].</param>
/// <param name="IndicatedPower_W">P_indicated = W · f [W].</param>
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
