// HydroTurbineResult.cs — Sprint HE.W1 solver output.

namespace Voxelforge.Hydroelectric;

/// <summary>
/// Solve-time outputs for a hydroelectric turbine snapshot at the
/// design head + flow rate (Sprint HE.W1 scaffold).
/// </summary>
/// <param name="HydraulicPower_W">P_hydraulic = ρ · g · Q · H [W] — the full
/// available potential-energy flux.</param>
/// <param name="HydraulicEfficiency">η_turbine [-] — fraction of P_hydraulic
/// captured at the runner shaft. From the per-kind cluster registry, with
/// an out-of-envelope head producing a reduced efficiency.</param>
/// <param name="GeneratorEfficiency">η_generator [-] echoed from input.</param>
/// <param name="OverallEfficiency">η_overall = η_turbine · η_generator [-].</param>
/// <param name="ShaftPower_W">P_shaft = η_turbine · P_hydraulic [W].</param>
/// <param name="ElectricalPower_W">P_elec = η_overall · P_hydraulic [W] —
/// the grid-bus output.</param>
/// <param name="HeadInValidEnvelope">Whether the head sits inside the kind's
/// per-kind validity band (informational; the solver still produces output
/// for out-of-envelope designs but with the cluster η reduced).</param>
internal sealed record HydroTurbineResult(
    double HydraulicPower_W,
    double HydraulicEfficiency,
    double GeneratorEfficiency,
    double OverallEfficiency,
    double ShaftPower_W,
    double ElectricalPower_W,
    bool   HeadInValidEnvelope);
