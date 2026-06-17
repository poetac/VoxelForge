// CoolantProperties.cs — Public facade for coolant property lookup.
//
// Original methane-only implementation has moved to MethaneFluid.cs;
// this file is now a thin delegator that:
//
//   • Preserves the legacy `CoolantProperties.Methane(T, P)` and
//     `TemperatureFromEnthalpy(h)` entry points so all call sites
//     keep compiling without a one-shot refactor.
//   • Exposes the new fluid-agnostic `State(fluid, T, P)` and
//     `TemperatureFromEnthalpy(fluid, h)` entry points that the
//     thermal solver uses now that propellant-pair selection controls
//     which fluid fills the regen jacket.
//
// The `CoolantState` record is unchanged so downstream consumers
// (correlations, solver, UI) are untouched.

using Voxelforge.Engines;

namespace Voxelforge.Coolant;

/// <summary>
/// Thermophysical state of a regen-coolant fluid at a point. All SI
/// units: T [K], P [Pa], ρ [kg/m³], Cp [J/(kg·K)], μ [Pa·s], k [W/(m·K)].
/// Implements <see cref="IThermodynamicState"/> explicitly so callers using
/// the concrete type are unaffected.
/// </summary>
public readonly record struct CoolantState(
    double T_K,
    double P_Pa,
    double Density_kgm3,
    double Cp_Jkg,
    double Viscosity_PaS,
    double Conductivity_WmK,
    double Prandtl,
    double Enthalpy_Jkg) : IThermodynamicState
{
    double IThermodynamicState.Temperature_K => T_K;
    double IThermodynamicState.Pressure_Pa   => P_Pa;
    double IThermodynamicState.Enthalpy_Jkg  => Enthalpy_Jkg;
    double IThermodynamicState.Entropy_JkgK  => 0.0;  // not tracked in coolant model
    double IThermodynamicState.Density_kgm3  => Density_kgm3;
}

public static class CoolantProperties
{
    // ── Back-compat constants (methane). Retained because a few
    // existing call sites in the solver still reference them.
    public const double MethaneCriticalT = 190.56;
    public const double MethaneCriticalP = 4.599e6;
    public const double MethaneMW        = 16.04;
    public const double MethaneR         = 518.3;
    public const double MethaneReferenceP = 10e6;

    // ── New fluid-agnostic API ────────────────────────────────

    public static CoolantState State(ICoolantFluid fluid, double T_K, double P_Pa)
        => fluid.GetState(T_K, P_Pa);

    public static double TemperatureFromEnthalpy(ICoolantFluid fluid, double h_Jkg)
        => fluid.TemperatureFromEnthalpy(h_Jkg);

    public static bool IsInPseudocriticalRegion(ICoolantFluid fluid, double T_K, double P_Pa)
        => fluid.IsInPseudocriticalRegion(T_K, P_Pa);

    // ── Legacy methane shims ──────────────────────────────────
    // New code should call State(fluid, T, P) instead.

    public static CoolantState Methane(double T_K, double P_Pa)
        => MethaneFluid.Instance.GetState(T_K, P_Pa);

    public static double TemperatureFromEnthalpy(double h_Jkg)
        => MethaneFluid.Instance.TemperatureFromEnthalpy(h_Jkg);

    public static bool IsInPseudocriticalRegion(double T_K, double P_Pa)
        => MethaneFluid.Instance.IsInPseudocriticalRegion(T_K, P_Pa);
}
