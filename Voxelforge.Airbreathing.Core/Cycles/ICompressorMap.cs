// ICompressorMap.cs — compressor performance abstraction.
//
// Open research item: compressor
// map data sourcing is sparse-public, so Sprint A7 ships parametric
// stand-in maps (Jones-style constant-η + constant-π). Real maps
// (NPSS-compatible MAP files, Honeywell / Pratt & Whitney public-
// release engine specs) land in a follow-on sprint when the J85 ±20 %
// fixture tolerance gets tightened.
//
// The abstraction is what survives — implementations swap underneath
// without touching the cycle solver.

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Compressor performance lookup. Maps an inlet stagnation state +
/// design pressure ratio to the corresponding outlet stagnation state
/// + isentropic efficiency.
/// </summary>
public interface ICompressorMap
{
    /// <summary>
    /// Compute compressor outlet stagnation state.
    /// </summary>
    /// <param name="inletStagnationT_K">Inlet (face) stagnation temperature [K].</param>
    /// <param name="inletStagnationP_Pa">Inlet stagnation pressure [Pa].</param>
    /// <param name="pressureRatio">Total-to-total pressure ratio π_c (≥ 1).</param>
    /// <returns>Outlet stagnation state + isentropic efficiency η_c.</returns>
    CompressorPoint Operate(double inletStagnationT_K, double inletStagnationP_Pa, double pressureRatio);
}

/// <summary>
/// Compressor outlet point. The cycle solver consumes this directly.
/// </summary>
/// <param name="OutletStagnationT_K">T_t3 [K].</param>
/// <param name="OutletStagnationP_Pa">P_t3 [Pa] (= π_c · P_t2).</param>
/// <param name="IsentropicEfficiency">Isentropic efficiency η_c (0, 1].</param>
/// <param name="SpecificWork_J_kg">Specific compressor work cp · (T_t3 − T_t2) [J/kg air].</param>
/// <remarks>
/// The optional <see cref="Diagnostics"/> field carries map-derived
/// diagnostics (surge margin, corrected mass flow, choke margin) when
/// the underlying map can compute them. Stand-in maps that don't model
/// off-design behaviour leave it null.
/// </remarks>
public readonly record struct CompressorPoint(
    double OutletStagnationT_K,
    double OutletStagnationP_Pa,
    double IsentropicEfficiency,
    double SpecificWork_J_kg)
{
    /// <summary>
    /// Optional map-derived diagnostics. Null when the underlying map
    /// is a stand-in that doesn't model surge / choke envelope.
    /// </summary>
    public MapInfo? Diagnostics { get; init; }
}

/// <summary>
/// Off-design map diagnostics. Attached to <see cref="CompressorPoint"/>
/// (and <see cref="TurbinePoint"/>) by table-based maps that model the
/// surge / choke envelope. Cycle solvers forward these to the gate
/// evaluator without staging through the station map (turbomachinery-
/// specific, not station-numbered).
/// </summary>
/// <param name="SurgeMargin">
/// Fractional surge margin <c>(π_surge − π_op) / π_surge</c>.
/// Positive when below surge, zero exactly on the surge line, negative
/// above surge (operating point physically infeasible). Industry
/// preliminary-design floor is ~0.10 (10 % margin).
/// </param>
/// <param name="CorrectedMassFlow_kg_s">
/// Corrected mass flow at the operating point [kg/s · √K / Pa pseudo-
/// units]. Used as a diagnostic + as the abscissa for surge / choke-
/// line lookups.
/// </param>
/// <param name="ChokeMarginRel">
/// Fractional position relative to the choke line, <c>ṁ_op / ṁ_choke</c>
/// at the operating speed. Less than 1 = below choke; equal to 1 =
/// at choke; greater than 1 = past choke (infeasible).
/// </param>
public sealed record MapInfo(
    double SurgeMargin,
    double CorrectedMassFlow_kg_s,
    double ChokeMarginRel);

/// <summary>
/// Parametric Jones-style compressor: fixed isentropic efficiency,
/// no off-design behaviour. Sprint A7 default.
/// </summary>
/// <param name="IsentropicEfficiency">
/// Constant η_c. Production single-spool axial compressors cluster
/// around 0.82-0.88; 0.85 is the standard preliminary-design value
/// (Mattingly Appendix B turbojet examples).
/// </param>
public sealed record ConstantEfficiencyCompressorMap(double IsentropicEfficiency)
    : ICompressorMap
{
    /// <summary>
    /// Default Mattingly preliminary-design value 0.85.
    /// </summary>
    public static readonly ConstantEfficiencyCompressorMap Default = new(0.85);

    /// <inheritdoc />
    public CompressorPoint Operate(double inletStagnationT_K, double inletStagnationP_Pa, double pressureRatio)
    {
        if (pressureRatio < 1.0)
            throw new System.ArgumentOutOfRangeException(nameof(pressureRatio),
                $"Compressor pressure ratio {pressureRatio} must be ≥ 1.");

        double gamma = Voxelforge.Airbreathing.Thermo.IdealGasAir.Gamma;
        double cp = Voxelforge.Airbreathing.Thermo.IdealGasAir.Cp_J_kg_K;

        // Isentropic outlet T = T_t2 · π_c^((γ-1)/γ)
        double T_isentropic = inletStagnationT_K * System.Math.Pow(pressureRatio, (gamma - 1.0) / gamma);

        // Actual outlet T accounts for irreversibility:
        //   η_c = (T_t3_isentropic − T_t2) / (T_t3_actual − T_t2)
        //   T_t3_actual = T_t2 + (T_t3_isentropic − T_t2) / η_c
        double T_actual = inletStagnationT_K
                        + (T_isentropic - inletStagnationT_K) / IsentropicEfficiency;

        double P_outlet = inletStagnationP_Pa * pressureRatio;
        double specificWork = cp * (T_actual - inletStagnationT_K);

        return new CompressorPoint(
            OutletStagnationT_K:    T_actual,
            OutletStagnationP_Pa:   P_outlet,
            IsentropicEfficiency:   IsentropicEfficiency,
            SpecificWork_J_kg:      specificWork);
    }
}
