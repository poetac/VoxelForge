// NuclearGates.cs — feasibility gate evaluation for the nuclear thermal pillar.
//
// Mirrors MarineGates.cs: standalone static evaluator called from
// NuclearOptimization.GenerateWith. NOT wired into the shared GateRegistry
// (whose Emit signature is rocket-specific).
//
// Gate census: 3 Hard + 3 Advisory = 6 total.
//
// Hard gates  — violations.Count > 0 → IsFeasible = false.
// Advisory    — returned separately; never flip IsFeasible.
//
// References:
//   Bennett, R.G. (1972). NERVA Program Summary. AIAA-72-1161.
//   Borowski et al. (2012). Nuclear Thermal Propulsion (NTP). AIAA-2012-3889.
//   Illes & Ohler (1998). Frozen-flow efficiency for H2 at ε~100.
//   NERVA NRX-A6 ground test data (1969, Jackass Flats NV).
//   ADR-026: Gate IDs prefixed NTR_* (pillar discriminator).

using System;
using System.Collections.Generic;
using Voxelforge.Nuclear.Brayton;
using Voxelforge.Nuclear.FuelPin;
using Voxelforge.Optimization;

namespace Voxelforge.Nuclear.Optimization;

/// <summary>
/// Evaluates all nuclear thermal feasibility gates for a given design and
/// cycle result.
/// </summary>
internal static class NuclearGates
{
    // Thresholds ---------------------------------------------------------------

    private const double CoreExitTempHardMax_K  = 3000.0;   // UO2-cermet fuel centerline limit
    private const double ThermalFluxHardMax_MWm3 = 4000.0;  // NERVA historical maximum ~4 GW/m³
    private const double ChamberPressureHardMin_bar = 30.0;  // regen jacket inlet pressure floor

    private const double KEff_BandLow  = 0.99;  // heuristic criticality band
    private const double KEff_BandHigh = 1.05;

    private const double FuelCTE_FractionThreshold = 0.80;  // UO2-cermet / Inconel CTE mismatch risk

    // ── Wave-2 fuel-pin thresholds (Sprint NU.W2) ─────────────────────────────

    /// <summary>UO₂-cermet centreline melt/fission-release hard limit [K].</summary>
    internal const double FuelPinCenterlineHardMax_K = 3200.0;

    /// <summary>Pin outer-surface chemical-compatibility hard limit [K].</summary>
    internal const double FuelPinSurfaceHardMax_K    = 2800.0;

    /// <summary>Hot-channel factor advisory envelope upper edge.</summary>
    internal const double HotChannelFactorAdvMax     = 1.80;

    /// <summary>Per-pin power advisory ceiling [W] — derived from the
    /// NERVA NRX-A6 ~1.6 kW/pin nominal point + ~50 % design headroom.</summary>
    internal const double PerPinPowerAdvMax_W        = 2400.0;

    /// <summary>Pin pitch/diameter ratio advisory low edge.</summary>
    internal const double PinPitchRatioAdvMin        = 1.05;

    /// <summary>Pin pitch/diameter ratio advisory high edge.</summary>
    internal const double PinPitchRatioAdvMax        = 1.80;

    // ── Wave-3 bimodal NTR thresholds (Sprint NU.W3) ─────────────────────────

    /// <summary>Brayton turbine inlet T hard limit [K] — Inconel-718 / Mo-Re refractory turbine blade.</summary>
    internal const double BraytonTurbineInletTempHardMax_K = 1500.0;

    /// <summary>Alternator RPM hard band low edge — below this, generator efficiency collapses.</summary>
    internal const double AlternatorRpmHardMin = 10_000.0;

    /// <summary>Alternator RPM hard band high edge — above this, blade-tip Mach + bearing life become unbounded.</summary>
    internal const double AlternatorRpmHardMax = 100_000.0;

    /// <summary>Brayton-thermal-efficiency advisory floor — space cluster anchor 0.20 (SP-100 design).</summary>
    internal const double BraytonThermalEfficiencyAdvMin = 0.15;

    /// <summary>Reactor power tap ratio (Brayton / total) advisory ceiling — above this, no thrust headroom.</summary>
    internal const double BraytonReactorTapRatioAdvMax = 0.95;

    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate all nuclear gates. Returns (hard violations, advisory violations).
    /// </summary>
    internal static (IReadOnlyList<FeasibilityViolation> Violations,
                     IReadOnlyList<FeasibilityViolation> Advisories)
        Evaluate(
            NuclearThermalDesign  design,
            NtrCycleResult        cycle,
            bool                  regenWallExceedsLimit,
            FuelPinHeatResult?    pinHeat = null,
            BraytonGasLoopResult? brayton = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(cycle);

        var violations = new List<FeasibilityViolation>();
        var advisories = new List<FeasibilityViolation>();

        // ── Hard gates ────────────────────────────────────────────────────────

        // NTR_REACTOR_OVERTEMP — UO2-cermet fuel centerline limit 3000 K.
        if (cycle.CoreExitTemp_K > CoreExitTempHardMax_K)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.ReactorOvertemp,
                Description:  $"Core exit temperature {cycle.CoreExitTemp_K:F1} K exceeds "
                            + $"UO2-cermet fuel centerline limit of {CoreExitTempHardMax_K:F0} K.",
                ActualValue:  cycle.CoreExitTemp_K,
                Limit:        CoreExitTempHardMax_K));
        }

        // NTR_THERMAL_FLUX_EXCEEDED — Sprint NU.W5 generalisation: per-tier
        // maximum power density. LEU 50 MW/m³ / HALEU 500 MW/m³ / HEU
        // 4000 MW/m³ (NERVA historical). UraniumEnrichment.None maps to HEU
        // for backwards compat with Wave-1/W2/W3/W4 designs.
        var tier = UraniumEnrichmentTiers.For(design.EnrichmentTier);
        double tierLimit_MWm3 = tier.MaxVolumetricHeatFlux_MWm3;
        if (!double.IsNaN(cycle.VolumetricHeatFlux_MWm3) &&
            cycle.VolumetricHeatFlux_MWm3 > tierLimit_MWm3)
        {
            string tierLabel = design.EnrichmentTier == UraniumEnrichment.None
                ? "HEU (backwards-compat default)"
                : design.EnrichmentTier.ToString();
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.ThermalFluxExceeded,
                Description:  $"Volumetric heat flux {cycle.VolumetricHeatFlux_MWm3:F0} MW/m³ "
                            + $"exceeds {tierLabel} ceiling of {tierLimit_MWm3:F0} MW/m³.",
                ActualValue:  cycle.VolumetricHeatFlux_MWm3,
                Limit:        tierLimit_MWm3));
        }

        // NTR_CHAMBER_PRESSURE_TOO_LOW — regen jacket inlet pressure floor.
        if (design.ChamberPressure_bar < ChamberPressureHardMin_bar)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.ChamberPressureTooLow,
                Description:  $"Chamber pressure {design.ChamberPressure_bar:F1} bar is below "
                            + $"regen jacket inlet pressure floor of {ChamberPressureHardMin_bar:F0} bar.",
                ActualValue:  design.ChamberPressure_bar,
                Limit:        ChamberPressureHardMin_bar));
        }

        // ── Advisory gates ────────────────────────────────────────────────────

        // NTR_K_EFF_OUT_OF_BAND — heuristic criticality band [0.99, 1.05].
        if (cycle.KEff < KEff_BandLow || cycle.KEff > KEff_BandHigh)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.KEff_OutOfBand,
                Description:  $"k_eff heuristic {cycle.KEff:F4} is outside criticality "
                            + $"advisory band [{KEff_BandLow}, {KEff_BandHigh}].",
                ActualValue:  cycle.KEff,
                Limit:        cycle.KEff < KEff_BandLow ? KEff_BandLow : KEff_BandHigh));
        }

        // NTR_FUEL_CTE_MISMATCH — UO2-cermet/Inconel CTE mismatch risk above 0.80.
        if (design.FuelLoadingFraction > FuelCTE_FractionThreshold)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.FuelCTEMismatch,
                Description:  $"Fuel loading fraction {design.FuelLoadingFraction:F3} exceeds "
                            + $"UO2-cermet/Inconel CTE mismatch advisory threshold of "
                            + $"{FuelCTE_FractionThreshold}.",
                ActualValue:  design.FuelLoadingFraction,
                Limit:        FuelCTE_FractionThreshold));
        }

        // NTR_REGEN_COOLING_BUDGET — regen nozzle wall T exceeds Inconel 718 limit.
        if (regenWallExceedsLimit)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.RegenCoolingBudget,
                Description:  "Regen nozzle solver reports peak gas-side wall temperature "
                            + "above Inconel 718 service limit.",
                ActualValue:  double.NaN,
                Limit:        double.NaN));
        }

        // ── Wave-2 fuel-pin gates (Sprint NU.W2) ──────────────────────────────
        // Only run when the per-pin model produced a result; Wave-1 designs
        // (pinHeat = null) skip the entire fuel-pin gate block.
        if (pinHeat is not null)
        {
            EvaluateFuelPinOvertemp(design, pinHeat, violations);
            EvaluateFuelPinSurfaceOvertemp(pinHeat, violations);
            EvaluateHotChannelFactorExcessive(pinHeat, advisories);
            EvaluatePerPinPowerAboveBand(pinHeat, advisories);
            EvaluatePinPitchRatioOutOfBand(design, advisories);
        }

        // ── Wave-3 bimodal gates (Sprint NU.W3) ──────────────────────────────
        // Only run when the Brayton model produced a result; non-bimodal
        // designs (brayton = null) skip the entire bimodal gate block.
        if (brayton is not null)
        {
            EvaluateBraytonTurbineInletOvertemp(brayton, violations);
            EvaluateAlternatorRpmOutOfBand(design, violations);
            EvaluateBraytonThermalEfficiencyLow(brayton, advisories);
            EvaluateBraytonReactorTapExcessive(brayton, design, advisories);
        }

        return (violations.AsReadOnly(), advisories.AsReadOnly());
    }

    // ── Wave-3 bimodal gate helpers (Sprint NU.W3) ───────────────────────────

    private static void EvaluateBraytonTurbineInletOvertemp(
        BraytonGasLoopResult brayton,
        List<FeasibilityViolation> violations)
    {
        if (brayton.TurbineInletTemp_K > BraytonTurbineInletTempHardMax_K)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.BraytonTurbineOvertemp,
                Description:  $"Brayton turbine inlet T = {brayton.TurbineInletTemp_K:F0} K "
                            + $"exceeds refractory-blade hard limit {BraytonTurbineInletTempHardMax_K:F0} K. "
                            + $"Reduce ReactorPowerToBrayton or upgrade blade material.",
                ActualValue:  brayton.TurbineInletTemp_K,
                Limit:        BraytonTurbineInletTempHardMax_K));
        }
    }

    private static void EvaluateAlternatorRpmOutOfBand(
        NuclearThermalDesign design,
        List<FeasibilityViolation> violations)
    {
        if (design.AlternatorRpm < AlternatorRpmHardMin
            || design.AlternatorRpm > AlternatorRpmHardMax)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.AlternatorRpmOutOfBand,
                Description:  $"Alternator RPM = {design.AlternatorRpm:F0} outside hard band "
                            + $"[{AlternatorRpmHardMin:F0}, {AlternatorRpmHardMax:F0}]. "
                            + "Below lower edge: alternator efficiency collapses. Above upper "
                            + "edge: blade-tip Mach + bearing life unbounded.",
                ActualValue:  design.AlternatorRpm,
                Limit:        design.AlternatorRpm < AlternatorRpmHardMin
                                ? AlternatorRpmHardMin
                                : AlternatorRpmHardMax));
        }
    }

    private static void EvaluateBraytonThermalEfficiencyLow(
        BraytonGasLoopResult brayton,
        List<FeasibilityViolation> advisories)
    {
        if (brayton.ThermalEfficiency < BraytonThermalEfficiencyAdvMin)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.BraytonThermalEfficiencyLow,
                Description:  $"Brayton thermal efficiency η = {brayton.ThermalEfficiency:F3} "
                            + $"below {BraytonThermalEfficiencyAdvMin:F2} advisory floor. "
                            + "Raise turbine inlet T or upgrade recuperator effectiveness "
                            + "(SP-100 cluster anchor 0.20).",
                ActualValue:  brayton.ThermalEfficiency,
                Limit:        BraytonThermalEfficiencyAdvMin));
        }
    }

    private static void EvaluateBraytonReactorTapExcessive(
        BraytonGasLoopResult brayton,
        NuclearThermalDesign design,
        List<FeasibilityViolation> advisories)
    {
        if (design.ReactorThermalPower_MW <= 0) return;
        double tapRatio = brayton.ReactorPowerToBrayton_MW / design.ReactorThermalPower_MW;
        if (tapRatio > BraytonReactorTapRatioAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.BraytonReactorTapExcessive,
                Description:  $"Brayton reactor-power tap ratio = {tapRatio:F3} above "
                            + $"{BraytonReactorTapRatioAdvMax:F2} ceiling. The Brayton loop is "
                            + "consuming nearly all reactor power; no thrust headroom remains "
                            + "for hybrid-mode operation.",
                ActualValue:  tapRatio,
                Limit:        BraytonReactorTapRatioAdvMax));
        }
    }

    // ── Wave-2 fuel-pin gate helpers (Sprint NU.W2) ───────────────────────────

    private static void EvaluateFuelPinOvertemp(
        NuclearThermalDesign design,
        FuelPinHeatResult pinHeat,
        List<FeasibilityViolation> violations)
    {
        // Sprint NU.W4 — per-material centreline temperature limit. UO₂-
        // cermet (default) at 3200 K; UC₂-graphite at 3500 K; UN-refractory
        // at 2800 K. None sentinel maps to UO₂-cermet for Wave-2 compat.
        var mat = NuclearFuelMaterials.For(design.FuelMaterial);
        double limit = mat.CenterlineTempLimit_K;
        if (pinHeat.PeakFuelCenterlineTemp_K > limit)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.FuelPinOvertemp,
                Description:  $"Peak fuel-pin centreline {pinHeat.PeakFuelCenterlineTemp_K:F0} K "
                            + $"exceeds {design.FuelMaterial} hard limit {limit:F0} K. "
                            + "Reduce reactor power, increase pin diameter, add fuel elements, "
                            + "or upgrade material.",
                ActualValue:  pinHeat.PeakFuelCenterlineTemp_K,
                Limit:        limit));
        }
    }

    private static void EvaluateFuelPinSurfaceOvertemp(
        FuelPinHeatResult pinHeat,
        List<FeasibilityViolation> violations)
    {
        if (pinHeat.PinSurfaceTemp_K > FuelPinSurfaceHardMax_K)
        {
            violations.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.FuelPinSurfaceOvertemp,
                Description:  $"Pin outer surface {pinHeat.PinSurfaceTemp_K:F0} K exceeds "
                            + $"chemical-compatibility limit {FuelPinSurfaceHardMax_K:F0} K. "
                            + "Increase coolant mass flow or pin diameter.",
                ActualValue:  pinHeat.PinSurfaceTemp_K,
                Limit:        FuelPinSurfaceHardMax_K));
        }
    }

    private static void EvaluateHotChannelFactorExcessive(
        FuelPinHeatResult pinHeat,
        List<FeasibilityViolation> advisories)
    {
        if (pinHeat.HotChannelFactor > HotChannelFactorAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.HotChannelFactorExcessive,
                Description:  $"Hot-channel factor F_hc = {pinHeat.HotChannelFactor:F2} exceeds "
                            + $"typical envelope upper edge {HotChannelFactorAdvMax:F2}. "
                            + "Re-design radial/axial power profile.",
                ActualValue:  pinHeat.HotChannelFactor,
                Limit:        HotChannelFactorAdvMax));
        }
    }

    private static void EvaluatePerPinPowerAboveBand(
        FuelPinHeatResult pinHeat,
        List<FeasibilityViolation> advisories)
    {
        if (pinHeat.PerPinPower_W > PerPinPowerAdvMax_W)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.PerPinPowerAboveBand,
                Description:  $"Per-pin power Q_pin = {pinHeat.PerPinPower_W:F0} W exceeds "
                            + $"NERVA-class envelope ceiling {PerPinPowerAdvMax_W:F0} W. "
                            + "Add fuel elements / pins.",
                ActualValue:  pinHeat.PerPinPower_W,
                Limit:        PerPinPowerAdvMax_W));
        }
    }

    private static void EvaluatePinPitchRatioOutOfBand(
        NuclearThermalDesign design,
        List<FeasibilityViolation> advisories)
    {
        if (double.IsNaN(design.FuelPinDiameter_mm) || design.FuelPinDiameter_mm <= 0) return;
        if (double.IsNaN(design.FuelPinPitch_mm)    || design.FuelPinPitch_mm    <= 0) return;
        double ratio = design.FuelPinPitch_mm / design.FuelPinDiameter_mm;
        if (ratio < PinPitchRatioAdvMin || ratio > PinPitchRatioAdvMax)
        {
            advisories.Add(new FeasibilityViolation(
                ConstraintId: NuclearConstraintIds.PinPitchRatioOutOfBand,
                Description:  $"Pin pitch/diameter ratio {ratio:F2} outside the typical "
                            + $"hex-array packing band [{PinPitchRatioAdvMin:F2}, "
                            + $"{PinPitchRatioAdvMax:F2}]. Ratio < 1.05 chokes coolant "
                            + "subchannels; > 1.80 wastes core volume.",
                ActualValue:  ratio,
                Limit:        ratio < PinPitchRatioAdvMin ? PinPitchRatioAdvMin : PinPitchRatioAdvMax));
        }
    }
}
