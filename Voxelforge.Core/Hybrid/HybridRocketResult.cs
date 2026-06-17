// HybridRocketResult.cs — Sprint R.W2 hybrid-rocket solver output.

namespace Voxelforge.Hybrid;

/// <summary>
/// Solve-time outputs for a hybrid rocket motor (Sprint R.W2 scaffold).
/// Snapshot quantities at a specified port radius (initial / mid /
/// final). The classical Marxman fit is integrated against time in a
/// follow-on sprint; for now the solver provides a closed-form snapshot.
/// </summary>
/// <param name="PortRadius_m">Port radius at the solve snapshot [m].</param>
/// <param name="OxidiserMassFlux_kgm2s">G_ox = ṁ_ox / (π · R²) [kg/(m²·s)].</param>
/// <param name="RegressionRate_ms">r_dot = a · G_ox^n [m/s].</param>
/// <param name="FuelMassFlow_kgs">ṁ_fuel = ρ_fuel · 2π · R · L · r_dot [kg/s].</param>
/// <param name="TotalMassFlow_kgs">ṁ_total = ṁ_ox + ṁ_fuel [kg/s].</param>
/// <param name="OxidiserFuelRatio">O/F = ṁ_ox / ṁ_fuel [-].</param>
/// <param name="CharacteristicVelocity_ms">c* [m/s]. Cluster-fit value for LOX/HTPB at the design O/F.</param>
/// <param name="ThrustCoefficient">C_F [-]. Cluster-fit value for the given ε.</param>
/// <param name="VacuumIsp_s">I_sp (vacuum) = c*·C_F / g0 [s].</param>
/// <param name="VacuumThrust_N">F (vacuum) = ṁ_total · I_sp · g0 [N].</param>
internal sealed record HybridRocketResult(
    double PortRadius_m,
    double OxidiserMassFlux_kgm2s,
    double RegressionRate_ms,
    double FuelMassFlow_kgs,
    double TotalMassFlow_kgs,
    double OxidiserFuelRatio,
    double CharacteristicVelocity_ms,
    double ThrustCoefficient,
    double VacuumIsp_s,
    double VacuumThrust_N);
