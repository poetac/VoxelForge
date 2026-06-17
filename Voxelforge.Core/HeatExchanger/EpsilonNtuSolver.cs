// EpsilonNtuSolver.cs — Sprint HX.W1 closed-form ε-NTU solver for
// counterflow plate-fin heat exchangers.
//
// Stateless, allocation-free, deterministic. Sizes the heat duty +
// outlet temperatures + pressure drops using the classical ε-NTU
// method (Kays & London 1984 chap 2) coupled with Kays-London offset-
// strip fin j-factor + f-factor cluster correlations.
//
//   ε(NTU, C_r) = (1 − exp(−NTU·(1 − C_r))) / (1 − C_r · exp(−NTU·(1 − C_r)))
//                                                           [counterflow]
//   Q          = ε · C_min · (T_hot_in − T_cold_in)
//   T_hot_out  = T_hot_in  − Q / C_hot
//   T_cold_out = T_cold_in + Q / C_cold
//
//   j-factor (offset-strip fin, Kays-London cluster):
//       j ≈ 0.6 · Re^(-0.4)
//   f-factor (offset-strip fin):
//       f ≈ 9.0 · Re^(-0.4)
//   h_side    = j · ρ · v · cp · Pr^(-2/3)
//   ΔP_side   = f · (L / D_h) · 0.5 · ρ · v²
//
// Both sides use the same fin geometry — a Wave-1 simplification. The
// solver computes per-side flow area, hydraulic diameter, Reynolds,
// velocity, then plugs into the j/f correlations to get U and ΔP.
//
// References:
//   Kays W.M., London A.L. (1984). "Compact Heat Exchangers," 3rd ed.
//   Shah R.K., Sekulić D.P. (2003). "Fundamentals of Heat Exchanger
//     Design," chaps 3 + 7.

using System;

namespace Voxelforge.HeatExchanger;

/// <summary>
/// Closed-form ε-NTU solver for a counterflow plate-fin heat exchanger
/// (Sprint HX.W1).
/// </summary>
internal static class EpsilonNtuSolver
{
    /// <summary>Air-cluster Prandtl number — used both sides (Wave-1).</summary>
    internal const double PrandtlNumber = 0.72;

    /// <summary>Kays-London offset-strip fin j-factor coefficient
    /// <c>j = JFactorCoefficient · Re^(JFactorExponent)</c>.</summary>
    internal const double JFactorCoefficient = 0.60;

    /// <summary>Kays-London offset-strip fin j-factor exponent.</summary>
    internal const double JFactorExponent = -0.40;

    /// <summary>Kays-London offset-strip fin f-factor coefficient.</summary>
    internal const double FFactorCoefficient = 9.0;

    /// <summary>Kays-London offset-strip fin f-factor exponent.</summary>
    internal const double FFactorExponent = -0.40;

    /// <summary>
    /// Solve the plate-fin HX performance snapshot at the design
    /// operating point.
    /// </summary>
    /// <param name="design">Validated plate-fin design record.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static PlateFinResult Solve(PlateFinDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        // 1. Capacity-rate framework.
        double C_hot  = design.HotMassFlow_kgs  * design.HotCp_JkgK;
        double C_cold = design.ColdMassFlow_kgs * design.ColdCp_JkgK;
        double C_min  = Math.Min(C_hot, C_cold);
        double C_max  = Math.Max(C_hot, C_cold);
        double C_r    = C_max > 0 ? C_min / C_max : 0.0;

        // 2. Per-side flow geometry. The block stacks one hot channel
        //    + one cold channel per pair of plates; total core-height
        //    is split 50/50 between hot and cold sides.
        int passes   = Math.Max(1, (int)(design.CoreHeight_m / (2.0 * design.PlateSpacing_m)));
        double finChannelWidth_m = design.FinPitch_m - design.FinThickness_m;
        int finsPerSide          = Math.Max(1, (int)(design.CoreWidth_m / design.FinPitch_m));
        // Per-side flow area (all parallel channels, summed).
        double sideFlowArea_m2 = finsPerSide
                               * finChannelWidth_m
                               * design.PlateSpacing_m
                               * passes;
        // Hydraulic diameter D_h = 4·A_flow_one_channel / Perimeter_one_channel
        //                       = 2·b·c / (b + c) where b = plate spacing, c = fin channel width.
        double hydraulicDiameter_m =
            (2.0 * design.PlateSpacing_m * finChannelWidth_m)
            / (design.PlateSpacing_m + finChannelWidth_m);

        // 3. Per-side hydrodynamics + heat-transfer coefficient.
        var hot  = ComputeSideCoefficients(design.HotMassFlow_kgs,
                                           design.HotDensity_kgm3,
                                           design.HotViscosity_PaS,
                                           design.HotCp_JkgK,
                                           sideFlowArea_m2,
                                           hydraulicDiameter_m,
                                           design.CoreLength_m);
        var cold = ComputeSideCoefficients(design.ColdMassFlow_kgs,
                                           design.ColdDensity_kgm3,
                                           design.ColdViscosity_PaS,
                                           design.ColdCp_JkgK,
                                           sideFlowArea_m2,
                                           hydraulicDiameter_m,
                                           design.CoreLength_m);

        // 3b. Sprint HX.W2 — fin-efficiency correction. When the design
        //     opts in, multiply each side's h by η_fin = tanh(m·L)/(m·L)
        //     with m = √(2h / (k·t_fin)) and L = PlateSpacing/2 (fin runs
        //     from plate to mid-channel, treats both plate halves
        //     symmetrically). When the flag is off, η_fin = 1 (bit-
        //     identical HX.W1 behaviour).
        double finHalfHeight_m = 0.5 * design.PlateSpacing_m;
        double hotFinEff = design.EnableFinEfficiencyCorrection
            ? ComputeFinEfficiency(hot.HeatTransferCoefficient_W_m2K,
                                   design.FinThermalConductivity_WmK,
                                   design.FinThickness_m,
                                   finHalfHeight_m)
            : 1.0;
        double coldFinEff = design.EnableFinEfficiencyCorrection
            ? ComputeFinEfficiency(cold.HeatTransferCoefficient_W_m2K,
                                   design.FinThermalConductivity_WmK,
                                   design.FinThickness_m,
                                   finHalfHeight_m)
            : 1.0;
        double hHotEff  = hot.HeatTransferCoefficient_W_m2K  * hotFinEff;
        double hColdEff = cold.HeatTransferCoefficient_W_m2K * coldFinEff;

        // 4. Overall U from per-side h's (with fin-efficiency-corrected
        //    h when HX.W2 active; clean-wall + no-fouling Wave-1
        //    simplification retained).
        double U = 1.0 / (1.0 / hHotEff + 1.0 / hColdEff);

        // 5. Total wetted heat-transfer area. Sum of fin-side perimeters
        //    × length × number of channels × 2 (per-pass) × passes.
        double singleChannelPerimeter_m = 2.0 * (design.PlateSpacing_m + finChannelWidth_m);
        double totalArea_m2 = finsPerSide
                            * passes
                            * 2.0                       // hot + cold panel pair
                            * singleChannelPerimeter_m
                            * design.CoreLength_m;

        // 6. NTU + ε. Counterflow ε(NTU, C_r) closed form.
        double UA  = U * totalArea_m2;
        double NTU = C_min > 0 ? UA / C_min : 0.0;
        double epsilon = ComputeCounterflowEffectiveness(NTU, C_r);

        // 7. Heat duty + outlet temperatures.
        double deltaT_in = design.HotInletTemperature_K - design.ColdInletTemperature_K;
        double Q = epsilon * C_min * deltaT_in;
        double T_hot_out  = design.HotInletTemperature_K  - Q / C_hot;
        double T_cold_out = design.ColdInletTemperature_K + Q / C_cold;

        return new PlateFinResult(
            CapacityRateMin_WK:                    C_min,
            CapacityRateRatio:                     C_r,
            NumberOfTransferUnits:                 NTU,
            Effectiveness:                         epsilon,
            OverallHeatTransferCoefficient_W_m2K:  U,
            HotSideHTC_W_m2K:                      hHotEff,
            ColdSideHTC_W_m2K:                     hColdEff,
            HeatDuty_W:                            Q,
            HotOutletTemperature_K:                T_hot_out,
            ColdOutletTemperature_K:               T_cold_out,
            HotPressureDrop_Pa:                    hot.PressureDrop_Pa,
            ColdPressureDrop_Pa:                   cold.PressureDrop_Pa,
            HotReynolds:                           hot.Reynolds,
            ColdReynolds:                          cold.Reynolds,
            HotFinEfficiency:                      hotFinEff,
            ColdFinEfficiency:                     coldFinEff);
    }

    /// <summary>
    /// Compute the 1-D fin efficiency η_fin = tanh(mL) / (mL) with
    /// m = √(2h / (k · t_fin)). Public-static for tests.
    /// </summary>
    /// <param name="heatTransferCoefficient_W_m2K">h [W/(m²·K)] on the
    /// fin surface (bare convective coefficient before correction).</param>
    /// <param name="finThermalConductivity_WmK">k_fin [W/(m·K)].</param>
    /// <param name="finThickness_m">t_fin [m].</param>
    /// <param name="finHalfHeight_m">L = PlateSpacing/2 [m] — half the
    /// channel height because the fin is plate-mounted both ends.</param>
    /// <returns>η_fin ∈ (0, 1]. Approaches 1 for short / thick / high-k
    /// fins; approaches 0 for tall / thin / low-k fins.</returns>
    internal static double ComputeFinEfficiency(
        double heatTransferCoefficient_W_m2K,
        double finThermalConductivity_WmK,
        double finThickness_m,
        double finHalfHeight_m)
    {
        if (heatTransferCoefficient_W_m2K <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(heatTransferCoefficient_W_m2K),
                "h must be > 0.");
        if (finThermalConductivity_WmK <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(finThermalConductivity_WmK),
                "k_fin must be > 0.");
        if (finThickness_m <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(finThickness_m),
                "t_fin must be > 0.");
        if (finHalfHeight_m <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(finHalfHeight_m),
                "L must be > 0.");

        double m = Math.Sqrt(2.0 * heatTransferCoefficient_W_m2K
                           / (finThermalConductivity_WmK * finThickness_m));
        double mL = m * finHalfHeight_m;
        // tanh(mL)/(mL) — limit at mL=0 is 1; numerically Math.Tanh(0)/0
        // would be 0/0, but we guard with a small-mL Taylor series.
        if (mL < 1e-6) return 1.0 - (mL * mL) / 3.0;
        return Math.Tanh(mL) / mL;
    }

    /// <summary>
    /// Compute the counterflow ε-NTU effectiveness. Public-static helper
    /// for tests + future cross-flow / parallel-flow generalisations.
    /// </summary>
    /// <param name="ntu">Number of transfer units [-]. Must be ≥ 0.</param>
    /// <param name="capacityRateRatio">C_r = C_min/C_max ∈ [0, 1] [-].</param>
    /// <returns>ε ∈ [0, 1].</returns>
    internal static double ComputeCounterflowEffectiveness(double ntu, double capacityRateRatio)
    {
        if (ntu < 0.0)
            throw new ArgumentOutOfRangeException(nameof(ntu), "NTU must be ≥ 0.");
        if (capacityRateRatio < 0.0 || capacityRateRatio > 1.0)
            throw new ArgumentOutOfRangeException(nameof(capacityRateRatio),
                "C_r must be in [0, 1].");

        // Asymptote at C_r = 1 (balanced flow): ε = NTU / (1 + NTU).
        const double balancedFlowTolerance = 1e-9;
        if (1.0 - capacityRateRatio < balancedFlowTolerance)
            return ntu / (1.0 + ntu);

        double expTerm = Math.Exp(-ntu * (1.0 - capacityRateRatio));
        return (1.0 - expTerm) / (1.0 - capacityRateRatio * expTerm);
    }

    private readonly record struct SideCoefficients(
        double Reynolds,
        double Velocity_ms,
        double HeatTransferCoefficient_W_m2K,
        double PressureDrop_Pa);

    private static SideCoefficients ComputeSideCoefficients(
        double massFlow_kgs,
        double density_kgm3,
        double viscosity_PaS,
        double cp_JkgK,
        double flowArea_m2,
        double hydraulicDiameter_m,
        double channelLength_m)
    {
        double massFlux_kgm2s   = massFlow_kgs / flowArea_m2;          // G = ṁ/A
        double velocity_ms      = massFlux_kgm2s / density_kgm3;
        double reynolds         = massFlux_kgm2s * hydraulicDiameter_m / viscosity_PaS;

        // j-factor → St·Pr^(2/3) = j → h = j·G·cp·Pr^(-2/3).
        double j = JFactorCoefficient * Math.Pow(reynolds, JFactorExponent);
        double h = j * massFlux_kgm2s * cp_JkgK
                 * Math.Pow(PrandtlNumber, -2.0 / 3.0);

        // f-factor → ΔP = f · (L/D_h) · 0.5 · ρ · v².
        double f = FFactorCoefficient * Math.Pow(reynolds, FFactorExponent);
        double dynamicPressure = 0.5 * density_kgm3 * velocity_ms * velocity_ms;
        double pressureDrop_Pa = f * (channelLength_m / hydraulicDiameter_m) * dynamicPressure;

        return new SideCoefficients(reynolds, velocity_ms, h, pressureDrop_Pa);
    }
}
