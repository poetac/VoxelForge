// NuclearConstraintIds.cs — canonical string constants for every nuclear gate ID.
// No magic strings in gate predicates. Prefixed NTR_* per ADR-026.

namespace Voxelforge.Nuclear.Optimization;

internal static class NuclearConstraintIds
{
    // ── Hard gates ─────────────────────────────────────────────────────────────

    /// <summary>Core exit temperature above 3000 K — UO2-cermet fuel centerline limit.</summary>
    internal const string ReactorOvertemp = "NTR_REACTOR_OVERTEMP";

    /// <summary>Volumetric heat flux above 4 GW/m³ — NERVA historical maximum.</summary>
    internal const string ThermalFluxExceeded = "NTR_THERMAL_FLUX_EXCEEDED";

    /// <summary>Chamber pressure below 30 bar — regen jacket inlet pressure floor.</summary>
    internal const string ChamberPressureTooLow = "NTR_CHAMBER_PRESSURE_TOO_LOW";

    // ── Advisory gates ─────────────────────────────────────────────────────────

    /// <summary>k_eff heuristic outside criticality band [0.99, 1.05].</summary>
    internal const string KEff_OutOfBand = "NTR_K_EFF_OUT_OF_BAND";

    /// <summary>Fuel loading fraction > 0.80 — UO2-cermet / Inconel CTE mismatch risk.</summary>
    internal const string FuelCTEMismatch = "NTR_FUEL_CTE_MISMATCH";

    /// <summary>Regen nozzle wall temperature exceeds Inconel 718 service limit.</summary>
    internal const string RegenCoolingBudget = "NTR_REGEN_COOLING_BUDGET";

    // ── Wave-2 fuel-pin gates (Sprint NU.W2) ───────────────────────────────────

    /// <summary>Peak fuel-pin centreline T exceeds UO2-cermet melting/fission-release limit.</summary>
    internal const string FuelPinOvertemp = "NTR_FUEL_PIN_OVERTEMP";

    /// <summary>Pin outer-surface T excessive — chemical compatibility / fuel-clad bond risk.</summary>
    internal const string FuelPinSurfaceOvertemp = "NTR_FUEL_PIN_SURFACE_OVERTEMP";

    /// <summary>Hot-channel factor above the typical envelope (1.0 – 1.8).</summary>
    internal const string HotChannelFactorExcessive = "NTR_HOT_CHANNEL_FACTOR_EXCESSIVE";

    /// <summary>Per-pin power above the NERVA-A6 fuel-element envelope.</summary>
    internal const string PerPinPowerAboveBand = "NTR_PER_PIN_POWER_ABOVE_BAND";

    /// <summary>Pin diameter:pitch ratio outside the typical hex-array packing band.</summary>
    internal const string PinPitchRatioOutOfBand = "NTR_PIN_PITCH_RATIO_OUT_OF_BAND";

    // ── Wave-3 bimodal NTR gates (Sprint NU.W3) ───────────────────────────────

    /// <summary>Brayton turbine inlet T above refractory-blade limit (~1500 K).</summary>
    internal const string BraytonTurbineOvertemp = "NTR_BIMODAL_BRAYTON_TURBINE_OVERTEMP";

    /// <summary>Alternator RPM outside [10k, 100k] hard band.</summary>
    internal const string AlternatorRpmOutOfBand = "NTR_BIMODAL_ALTERNATOR_RPM_OUT_OF_BAND";

    /// <summary>Brayton thermal efficiency below 0.15 cluster floor.</summary>
    internal const string BraytonThermalEfficiencyLow = "NTR_BIMODAL_BRAYTON_THERMAL_EFFICIENCY_LOW";

    /// <summary>Reactor power tap to Brayton above 95 % — no thrust headroom.</summary>
    internal const string BraytonReactorTapExcessive = "NTR_BIMODAL_REACTOR_TAP_EXCESSIVE";
}
