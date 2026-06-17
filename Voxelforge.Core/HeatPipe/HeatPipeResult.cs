// HeatPipeResult.cs — Sprint HP.W1 solver output.

namespace Voxelforge.HeatPipe;

/// <summary>
/// Solve-time outputs for a heat-pipe snapshot at the design operating
/// point (Sprint HP.W1).
/// </summary>
/// <param name="CapillaryLimit_W">Q_max [W] = q_capillary · A_cross —
/// the maximum heat-flux limit before wick dry-out.</param>
/// <param name="CapillaryMargin">Q_max / Q [-] — utilisation safety
/// margin. &gt; 1 means safe; ≤ 1 means at-or-past dry-out.</param>
/// <param name="ThermalResistance_K_W">R_thermal [K/W] = L / (k_eff · A).
/// The end-to-end resistance from evaporator to condenser.</param>
/// <param name="EndToEndDeltaT_K">ΔT_end-to-end = Q · R_thermal [K].
/// Typically 1-10 K for moderate heat fluxes — much smaller than the
/// equivalent solid-copper rod ΔT.</param>
/// <param name="OperatingTemperatureInValidEnvelope">Whether T_operating
/// sits inside the fluid's per-cluster validity envelope.</param>
/// <param name="SonicLimit_W">Sprint HP.W2. q_sonic · A_cross [W] —
/// the sonic-choke limit on vapour velocity. Defaults to +∞ for
/// HP.W1 bit-identity.</param>
/// <param name="EntrainmentLimit_W">Sprint HP.W2. q_entrain · A_cross
/// [W] — the droplet-entrainment limit. Defaults to +∞ for HP.W1
/// bit-identity.</param>
/// <param name="GoverningLimit_W">Sprint HP.W2. min(capillary, sonic,
/// entrainment) [W] — the binding constraint at the operating point.</param>
/// <param name="GoverningMargin">Sprint HP.W2. GoverningLimit / Q [-].
/// &gt; 1 means safe; ≤ 1 means at-or-past the binding-constraint
/// dry-out / choke / entrainment failure.</param>
internal sealed record HeatPipeResult(
    double CapillaryLimit_W,
    double CapillaryMargin,
    double ThermalResistance_K_W,
    double EndToEndDeltaT_K,
    bool   OperatingTemperatureInValidEnvelope,
    double SonicLimit_W       = double.PositiveInfinity,
    double EntrainmentLimit_W = double.PositiveInfinity,
    double GoverningLimit_W   = double.PositiveInfinity,
    double GoverningMargin    = double.PositiveInfinity);
