// WingSparResult.cs — Sprint AS.W1 solver output.

namespace Voxelforge.Aerostructures;

/// <summary>
/// Solve-time outputs for an Euler-Bernoulli wing-spar snapshot
/// (Sprint AS.W1).
/// </summary>
/// <param name="SectionArea_m2">A [m²] — cross-section area.</param>
/// <param name="SecondMomentOfArea_m4">I_xx [m⁴] — about the chord axis.</param>
/// <param name="SectionModulus_m3">S = I_xx / c [m³] where c = h/2.</param>
/// <param name="MaximumBendingMoment_Nm">M_max = n · w · L² / 2 [N·m]
/// at the root of a cantilever under uniformly-distributed load.</param>
/// <param name="MaximumBendingStress_Pa">σ_max = M_max / S [Pa].</param>
/// <param name="TipDeflection_m">δ_tip = n · w · L⁴ / (8 · E · I) [m]
/// for a cantilever under UDL.</param>
/// <param name="SafetyFactor">SF = σ_yield / σ_max [-]. FAR Part 23 ≥
/// 1.5 ultimate-to-limit; civil construction often ≥ 2.0.</param>
/// <param name="SparMass_kg">m = ρ · A · L [kg] (single half-span).</param>
internal sealed record WingSparResult(
    double SectionArea_m2,
    double SecondMomentOfArea_m4,
    double SectionModulus_m3,
    double MaximumBendingMoment_Nm,
    double MaximumBendingStress_Pa,
    double TipDeflection_m,
    double SafetyFactor,
    double SparMass_kg);
