// ResistojetConditions.cs — operating conditions for a resistojet.
//
// Wave-1 sub-step uses a kind-specific conditions record (Resistojet
// only). When Wave-2 lands, the conditions record either generalises
// (kind-discriminated like AirbreathingEngineDesign) or each variant
// gets its own — decided by the Team-P plasma-state audit per
// ADR-026 §6.

using Voxelforge.Engines;

namespace Voxelforge.ElectricPropulsion;

/// <summary>
/// Operating conditions for a resistojet evaluation. Sized analogue of
/// the rocket-side <c>OperatingConditions</c> and the airbreathing-side
/// <c>FlightConditions</c>.
/// </summary>
/// <param name="BusVoltage_V">
/// Spacecraft bus voltage [V]. Standard values: 28 V (small-sat / military),
/// 50 V (commsat), 100 V (large GEO bus). Drives heater-coil resistance
/// design but not directly consumed by the lumped 0-D physics solvers.
/// </param>
/// <param name="BusPower_W_avail">
/// Maximum continuous bus power available to the thruster [W]. Used as
/// the dynamic upper-bound clip on
/// <see cref="ElectricPropulsionEngineDesign.HeaterPower_W"/> at SA
/// bind-time (per pillar spec §2 — not a feasibility gate).
/// </param>
/// <param name="AmbientPressure_Pa">
/// Ambient pressure [Pa]. <c>0.0</c> for vacuum operation;
/// non-zero only for ground vacuum-chamber test conditions or a
/// hypothetical in-atmosphere demonstrator. Drives the choking criterion
/// in <c>IsentropicNozzleSolver</c> + the radiation T_∞ in
/// <c>RadiationLossSolver</c>.
/// </param>
/// <param name="Propellant">Propellant choice — see <see cref="Propellant"/>.</param>
/// <param name="InletTemperature_K">
/// Pre-heater gas temperature [K]. For hydrazine decomposed via Shell-405
/// catalyst, the standard value is ~900 K; for other propellants, the
/// regulator + manifold temperature (typically 280–320 K).
/// </param>
/// <param name="InletComposition">
/// Mole-fraction breakdown of the inlet stream. See
/// <see cref="PropellantInletComposition"/> for canonical values.
/// </param>
public sealed record ResistojetConditions(
    double                       BusVoltage_V,
    double                       BusPower_W_avail,
    double                       AmbientPressure_Pa,
    Propellant                   Propellant,
    double                       InletTemperature_K,
    PropellantInletComposition   InletComposition) : IEngineConditions
{
    /// <inheritdoc />
    public string Family => EngineFamilies.ElectricPropulsion;
}
