// HeatPipeSolver.cs — Sprint HP.W1 closed-form heat-pipe performance
// snapshot.
//
// Stateless, allocation-free, deterministic. The Wave-1 model treats
// the heat pipe as a black-box high-effective-conductivity thermal
// path with a capillary-driven maximum-throughput limit:
//
//   Q_max        = q_capillary_per_area · A_cross   [W]
//   margin       = Q_max / Q                        [dimensionless]
//   R_thermal    = L / (k_eff · A_cross)            [K/W]
//   ΔT           = Q · R_thermal                    [K]
//
// Per-fluid + per-wick capillary-limit physics (Chi correlation, sonic
// limit, entrainment limit, boiling limit) is deferred to HP.W2.
//
// References:
//   Chi S.W. (1976). "Heat Pipe Theory and Practice." McGraw-Hill.
//   Faghri A. (2016). "Heat Pipe Science and Technology," 2nd ed.
//   NASA TP-3326 (1995). "Heat Pipe Design Handbook."

using System;

namespace Voxelforge.HeatPipe;

/// <summary>
/// Closed-form heat-pipe performance snapshot solver (Sprint HP.W1).
/// </summary>
internal static class HeatPipeSolver
{
    /// <summary>
    /// Solve the heat-pipe snapshot at the design operating point.
    /// </summary>
    internal static HeatPipeResult Solve(HeatPipeDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = HeatPipeFluidRegistry.For(design.Fluid);

        double A_cross = design.CrossSectionArea_m2;
        double Q_max = props.CapillaryLimitPerArea_W_m2 * A_cross;
        double margin = Q_max / design.HeatThroughput_W;
        double R_thermal = design.Length_m
                         / (props.EffectiveAxialConductivity_W_mK * A_cross);
        double deltaT = design.HeatThroughput_W * R_thermal;
        bool inEnvelope = design.OperatingTemperature_K >= props.OperatingTempMin_K
                       && design.OperatingTemperature_K <= props.OperatingTempMax_K;

        // Sprint HP.W2 — additional per-limit transports.
        double Q_sonic       = props.SonicLimitPerArea_W_m2       * A_cross;
        double Q_entrain     = props.EntrainmentLimitPerArea_W_m2 * A_cross;
        double Q_governing   = Math.Min(Q_max, Math.Min(Q_sonic, Q_entrain));
        double governingMargin = Q_governing / design.HeatThroughput_W;

        return new HeatPipeResult(
            CapillaryLimit_W:                    Q_max,
            CapillaryMargin:                     margin,
            ThermalResistance_K_W:               R_thermal,
            EndToEndDeltaT_K:                    deltaT,
            OperatingTemperatureInValidEnvelope: inEnvelope,
            SonicLimit_W:                        Q_sonic,
            EntrainmentLimit_W:                  Q_entrain,
            GoverningLimit_W:                    Q_governing,
            GoverningMargin:                     governingMargin);
    }

    /// <summary>
    /// Compute the maximum heat throughput (capillary limit) for a heat
    /// pipe of given fluid + cross-sectional area. Public-static helper
    /// for sizing studies.
    /// </summary>
    internal static double ComputeMaximumHeatThroughput(
        HeatPipeFluid fluid,
        double internalDiameter_m)
    {
        if (internalDiameter_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(internalDiameter_m),
                "D must be > 0.");
        var props = HeatPipeFluidRegistry.For(fluid);
        double A = Math.PI * internalDiameter_m * internalDiameter_m * 0.25;
        return props.CapillaryLimitPerArea_W_m2 * A;
    }
}
