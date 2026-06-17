// ITurbineMap.cs — turbine performance abstraction.
//
// Sibling to ICompressorMap. Same Sprint A7 parametric Jones-style
// stand-in pattern; real maps deferred. Cycle solver uses the
// abstraction so the swap when real maps land is mechanical.

namespace Voxelforge.Airbreathing.Cycles;

/// <summary>
/// Turbine performance lookup. Inputs: inlet stagnation state +
/// required shaft work to drive the compressor (per unit total mass
/// flow). Output: outlet stagnation state + isentropic efficiency.
/// </summary>
public interface ITurbineMap
{
    /// <summary>
    /// Compute turbine outlet stagnation state given the work it must
    /// extract from the gas to drive the compressor. The implementation
    /// solves for T_t5 from energy balance, then back-derives P_t5 via
    /// the actual-vs-isentropic temperature ratio + isentropic efficiency.
    /// </summary>
    /// <param name="inletStagnationT_K">Combustor exit T_t4 [K].</param>
    /// <param name="inletStagnationP_Pa">Combustor exit P_t4 [Pa].</param>
    /// <param name="requiredSpecificWork_J_kg_total">
    /// Work required from each unit kg of total turbine mass flow (i.e.
    /// already divided by ṁ_total = (1+f)·ṁ_a).
    /// </param>
    /// <returns>Outlet stagnation state + isentropic efficiency η_t.</returns>
    TurbinePoint Operate(double inletStagnationT_K, double inletStagnationP_Pa, double requiredSpecificWork_J_kg_total);
}

/// <summary>
/// Turbine outlet point. The cycle solver consumes this directly.
/// </summary>
/// <param name="OutletStagnationT_K">T_t5 [K].</param>
/// <param name="OutletStagnationP_Pa">P_t5 [Pa].</param>
/// <param name="IsentropicEfficiency">Isentropic efficiency η_t (0, 1].</param>
/// <param name="ExtractedSpecificWork_J_kg_total">
/// Work extracted per kg total mass flow [J/kg]. Equals the
/// requested work for a properly-sized turbine; may differ if the
/// implementation models off-design behaviour.
/// </param>
/// <remarks>
/// The optional <see cref="Diagnostics"/> field carries map-derived
/// diagnostics (corrected mass flow + surge/choke margins). Stand-in
/// maps without an off-design envelope leave it null.
/// </remarks>
public readonly record struct TurbinePoint(
    double OutletStagnationT_K,
    double OutletStagnationP_Pa,
    double IsentropicEfficiency,
    double ExtractedSpecificWork_J_kg_total)
{
    /// <summary>
    /// Optional map-derived diagnostics. Null for stand-in turbines.
    /// </summary>
    public MapInfo? Diagnostics { get; init; }
}

/// <summary>
/// Parametric Jones-style turbine: fixed isentropic efficiency,
/// no off-design behaviour. Sprint A7 default.
/// </summary>
/// <param name="IsentropicEfficiency">
/// Constant η_t. Production single-spool axial turbines cluster
/// around 0.85-0.92; 0.90 is the standard preliminary-design value
/// (Mattingly Appendix B turbojet examples).
/// </param>
public sealed record ConstantEfficiencyTurbineMap(double IsentropicEfficiency)
    : ITurbineMap
{
    /// <summary>
    /// Default Mattingly preliminary-design value 0.90.
    /// </summary>
    public static readonly ConstantEfficiencyTurbineMap Default = new(0.90);

    /// <inheritdoc />
    public TurbinePoint Operate(double inletStagnationT_K, double inletStagnationP_Pa, double requiredSpecificWork_J_kg_total)
    {
        double gamma = Voxelforge.Airbreathing.Thermo.IdealGasAir.Gamma;
        double cp = Voxelforge.Airbreathing.Thermo.IdealGasAir.Cp_J_kg_K;

        // Energy balance: T_t4 − T_t5 = W_required / cp
        double dT_actual = requiredSpecificWork_J_kg_total / cp;
        double T_actual_outlet = inletStagnationT_K - dT_actual;

        if (T_actual_outlet <= 0.0)
            throw new System.InvalidOperationException(
                $"Turbine outlet T = {T_actual_outlet:F1} K (≤ 0) — required work "
              + $"{requiredSpecificWork_J_kg_total:F0} J/kg exceeds available "
              + $"enthalpy at T_t4 = {inletStagnationT_K:F1} K.");

        // Isentropic outlet T accounts for the efficiency penalty:
        //   η_t = (T_t4 − T_t5_actual) / (T_t4 − T_t5_isentropic)
        //   T_t5_isentropic = T_t4 − (T_t4 − T_t5_actual) / η_t
        double dT_isentropic = dT_actual / IsentropicEfficiency;
        double T_isentropic_outlet = inletStagnationT_K - dT_isentropic;

        // Pressure ratio from isentropic step: P_t5/P_t4 = (T_t5_isentropic/T_t4)^(γ/(γ-1))
        double P_outlet = inletStagnationP_Pa
                        * System.Math.Pow(T_isentropic_outlet / inletStagnationT_K, gamma / (gamma - 1.0));

        return new TurbinePoint(
            OutletStagnationT_K:                T_actual_outlet,
            OutletStagnationP_Pa:               P_outlet,
            IsentropicEfficiency:               IsentropicEfficiency,
            ExtractedSpecificWork_J_kg_total:   requiredSpecificWork_J_kg_total);
    }
}
