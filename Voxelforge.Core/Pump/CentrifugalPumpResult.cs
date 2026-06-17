// CentrifugalPumpResult.cs — Sprint PMP.W1 solver output.

namespace Voxelforge.Pump;

/// <summary>
/// Solve-time outputs for a centrifugal pump snapshot at the design
/// operating point (Sprint PMP.W1 scaffold).
/// </summary>
/// <param name="HydraulicPower_W">P_hyd = ρ·g·Q·H [W] — useful work
/// delivered to the fluid.</param>
/// <param name="ShaftPowerInput_W">P_shaft = P_hyd / η [W] — at the
/// motor coupling.</param>
/// <param name="SpecificSpeedSI">N_s = ω·√Q / (g·H)^0.75 [-] (SI form).
/// Radial-flow centrifugals cluster N_s ∈ [0.2, 1.0]; mixed-flow ∈
/// [1.0, 2.5]; axial-flow > 2.5.</param>
/// <param name="NetPositiveSuctionHeadAvailable_m">NPSH_a [m] = (P_in −
/// p_v)/(ρg) − z_lift − h_f.</param>
/// <param name="NetPositiveSuctionHeadRequired_m">NPSH_r [m] — cluster
/// fit from specific speed + head rise (cavitation-onset margin).</param>
/// <param name="CavitationMargin_m">NPSH_a − NPSH_r [m]. Positive =
/// safe; negative = cavitation imminent (gate worthy).</param>
internal sealed record CentrifugalPumpResult(
    double HydraulicPower_W,
    double ShaftPowerInput_W,
    double SpecificSpeedSI,
    double NetPositiveSuctionHeadAvailable_m,
    double NetPositiveSuctionHeadRequired_m,
    double CavitationMargin_m);
