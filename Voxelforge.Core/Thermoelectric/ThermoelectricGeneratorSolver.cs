// ThermoelectricGeneratorSolver.cs — Sprint TEG.W1 closed-form TEG
// performance snapshot.
//
// Stateless, allocation-free, deterministic. Uses the canonical
// figure-of-merit formula relating dimensionless ZT to maximum
// achievable conversion efficiency under matched-load operation:
//
//   η_TEG = η_Carnot · (√(1+ZT) − 1) / (√(1+ZT) + T_cold/T_hot)
//
//   η_Carnot = 1 − T_cold/T_hot
//
// At ZT → ∞, η_TEG → η_Carnot. At ZT → 0, η_TEG → 0. Real materials
// span ZT ∈ [0.5, 2.5] with cluster mid-band 1.0-1.5.
//
// References:
//   Goldsmid H.J. (2010). "Introduction to Thermoelectricity," chap 1.
//   Snyder G.J., Toberer E.S. (2008). "Complex thermoelectric
//     materials." Nature Materials 7.
//   Bennett G.L. (2006). "Space Nuclear Power: Opening the Final
//     Frontier" — RTG cluster history.

using System;

namespace Voxelforge.Thermoelectric;

/// <summary>
/// Closed-form thermoelectric-generator performance snapshot solver
/// (Sprint TEG.W1).
/// </summary>
internal static class ThermoelectricGeneratorSolver
{
    /// <summary>
    /// Solve the TEG performance snapshot at the design operating point.
    /// </summary>
    internal static ThermoelectricGeneratorResult Solve(
        ThermoelectricGeneratorDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();

        var props = ThermoelectricMaterialRegistry.For(design.Material);

        double tempRatio    = design.ColdSideTemperature_K / design.HotSideTemperature_K;
        double eta_Carnot   = 1.0 - tempRatio;
        double sqrtFactor   = Math.Sqrt(1.0 + props.FigureOfMerit_ZT);
        double eta_TEG      = eta_Carnot * (sqrtFactor - 1.0) / (sqrtFactor + tempRatio);
        double P_elec       = eta_TEG * design.HotSideHeatInput_W;
        double Q_cold       = design.HotSideHeatInput_W - P_elec;
        bool inEnvelope     = design.HotSideTemperature_K >= props.MinHotSideTemperature_K
                           && design.HotSideTemperature_K <= props.MaxHotSideTemperature_K;

        return new ThermoelectricGeneratorResult(
            CarnotEfficiency:                    eta_Carnot,
            ConversionEfficiency:                eta_TEG,
            ElectricPowerOutput_W:               P_elec,
            HeatRejectedToColdSide_W:            Q_cold,
            HotSideTemperatureInValidEnvelope:   inEnvelope);
    }

    /// <summary>
    /// Sprint TEG.W2. Compute the conversion efficiency of a two-stage
    /// segmented stack: high-T segment (SiGe/PbTe) on the hot side
    /// joined to a low-T segment (Bi₂Te₃) on the cold side. Each stage
    /// operates over a sub-range [T_intermediate, T_hot] and [T_cold,
    /// T_intermediate].
    ///
    /// <para>
    /// <strong>Cascade formula (correct for series heat engines).</strong>
    /// Heat flows through the high-T stage first; the unconverted fraction
    /// (1 − η_high) enters the low-T stage:
    ///   Q_cold = Q_hot · (1 − η_high) · (1 − η_low)
    ///   η_seg  = 1 − (1 − η_high) · (1 − η_low)
    ///          = η_high + η_low − η_high · η_low
    /// This is mathematically guaranteed to be ≥ max(η_high, η_low) for any
    /// individually-feasible stage efficiencies, so a properly-segmented
    /// stack always beats either single stage that spans only part of the
    /// temperature gradient at the stage's ZT.
    /// </para>
    /// <para>
    /// The previous (buggy) implementation used a ΔT-fraction-weighted
    /// average of η_high and η_low, which under-counted the stack and
    /// produced η_seg &lt; η_single — the inverse of the physical truth.
    /// Segmented stacks consistently outperform single-stage by ~30 % at
    /// large T_hot/T_cold ratios; the ΔT-weighted form averaged the
    /// stages instead. Fixed under #548-E.
    /// </para>
    /// </summary>
    /// <param name="highTemperatureMaterial">Hot-side segment material.</param>
    /// <param name="lowTemperatureMaterial">Cold-side segment material.</param>
    /// <param name="hotSideTemperature_K">T_hot [K].</param>
    /// <param name="intermediateTemperature_K">T_intermediate [K] — the
    /// junction between hot + cold segments. Must satisfy T_cold &lt;
    /// T_int &lt; T_hot.</param>
    /// <param name="coldSideTemperature_K">T_cold [K].</param>
    /// <returns>Effective η [-] for the segmented stack.</returns>
    internal static double ComputeSegmentedStackEfficiency(
        ThermoelectricMaterial highTemperatureMaterial,
        ThermoelectricMaterial lowTemperatureMaterial,
        double hotSideTemperature_K,
        double intermediateTemperature_K,
        double coldSideTemperature_K)
    {
        if (intermediateTemperature_K <= coldSideTemperature_K
         || intermediateTemperature_K >= hotSideTemperature_K)
            throw new ArgumentOutOfRangeException(nameof(intermediateTemperature_K),
                $"T_int ({intermediateTemperature_K:F1}) must satisfy T_cold "
              + $"({coldSideTemperature_K:F1}) < T_int < T_hot "
              + $"({hotSideTemperature_K:F1}).");

        var highProps = ThermoelectricMaterialRegistry.For(highTemperatureMaterial);
        var lowProps  = ThermoelectricMaterialRegistry.For(lowTemperatureMaterial);
        double eta_high = ComputeFigureOfMeritEfficiency(
            highProps.FigureOfMerit_ZT, hotSideTemperature_K, intermediateTemperature_K);
        double eta_low = ComputeFigureOfMeritEfficiency(
            lowProps.FigureOfMerit_ZT, intermediateTemperature_K, coldSideTemperature_K);

        // Cascade η for series heat engines:
        //   η_seg = 1 − (1 − η_high) · (1 − η_low)
        //         = η_high + η_low − η_high · η_low
        return 1.0 - (1.0 - eta_high) * (1.0 - eta_low);
    }

    /// <summary>
    /// Compute the canonical figure-of-merit conversion efficiency from
    /// (ZT, T_hot, T_cold). Public-static for tests + future ZT-sweep
    /// studies.
    /// </summary>
    /// <param name="figureOfMerit_ZT">ZT [-]. Must be ≥ 0.</param>
    /// <param name="hotSideTemperature_K">T_hot [K].</param>
    /// <param name="coldSideTemperature_K">T_cold [K]. Must be < T_hot.</param>
    /// <returns>η_TEG ∈ [0, η_Carnot].</returns>
    internal static double ComputeFigureOfMeritEfficiency(
        double figureOfMerit_ZT,
        double hotSideTemperature_K,
        double coldSideTemperature_K)
    {
        if (figureOfMerit_ZT < 0)
            throw new ArgumentOutOfRangeException(nameof(figureOfMerit_ZT),
                "ZT must be ≥ 0.");
        if (hotSideTemperature_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(hotSideTemperature_K),
                "T_hot must be > 0.");
        if (coldSideTemperature_K >= hotSideTemperature_K)
            throw new ArgumentOutOfRangeException(nameof(coldSideTemperature_K),
                "T_cold must be < T_hot.");

        double tempRatio  = coldSideTemperature_K / hotSideTemperature_K;
        double eta_Carnot = 1.0 - tempRatio;
        double sqrtFactor = Math.Sqrt(1.0 + figureOfMerit_ZT);
        return eta_Carnot * (sqrtFactor - 1.0) / (sqrtFactor + tempRatio);
    }
}
