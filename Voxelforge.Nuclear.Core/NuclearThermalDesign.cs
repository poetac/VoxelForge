// NuclearThermalDesign.cs — central design record for the nuclear pillar.
//
// Implements IEngineDesign with Family = EngineFamilies.Nuclear.
// Analogous to AirbreathingEngineDesign on the air-breathing side.
// Pillar spec: Voxelforge/docs/pillar-specs/nuclear-propulsion.md.
//
// SA vector (6 dims) — manual Pack/Unpack in NtrObjective:
//   0  ReactorThermalPower_MW   [50, 2000]
//   1  PropellantMassFlow_kgs   [1, 50]
//   2  ChamberPressure_bar      [25, 80]
//   3  ThroatRadius_mm          [5, 200]
//   4  ExpansionRatio           [20, 200]
//   5  RegenChannelDepth_mm     [0.5, 5.0]

using System;
using Voxelforge.Engines;

namespace Voxelforge.Nuclear;

/// <summary>
/// Design parameters for a NERVA-class solid-core NTR candidate.
/// Wave-1 covers <see cref="NuclearKind.NervaSolidCore"/>.
/// </summary>
/// <param name="Kind">Sub-variant — <see cref="NuclearKind.NervaSolidCore"/> for Wave-1.</param>
/// <param name="ReactorThermalPower_MW">Reactor total thermal power [MW]. SA dim 0.</param>
/// <param name="ReactorCoreLength_mm">Reactor core axial length [mm].</param>
/// <param name="ReactorCoreDiameter_mm">Reactor core outer diameter [mm].</param>
/// <param name="FuelLoadingFraction">
/// UO₂-cermet fuel volume fraction in the core matrix [-]. Range [0.60, 0.85].
/// Above 0.80 risks Inconel/UO₂ CTE mismatch during thermal cycling.
/// </param>
/// <param name="PropellantMassFlow_kgs">LH₂ mass flow rate [kg/s]. SA dim 1.</param>
/// <param name="ChamberPressure_bar">Nozzle stagnation pressure [bar]. SA dim 2.</param>
/// <param name="ThroatRadius_mm">Nozzle throat radius [mm]. SA dim 3.</param>
/// <param name="ExpansionRatio">Nozzle exit / throat area ratio [-]. SA dim 4.</param>
/// <param name="NozzleLength_mm">
/// Nozzle total length from throat to exit plane [mm].
/// Derived from <see cref="ExpansionRatio"/> and <see cref="ThroatRadius_mm"/>
/// in practice; stored as a parameter for direct override.
/// </param>
/// <param name="RegenChannelDepth_mm">Regen cooling channel height [mm]. SA dim 5.</param>
/// <param name="RegenChannelCount">Number of axial regen cooling channels.</param>
/// <param name="NozzleWallThickness_mm">Nozzle gas-side wall thickness [mm].</param>
/// <param name="NozzleChannelWidth_mm">Regen channel width [mm].</param>
/// <param name="NozzleManifoldDepth_mm">Manifold collector depth [mm].</param>
public sealed record NuclearThermalDesign(
    NuclearKind Kind,
    // ── Reactor (SA dim 0) ─────────────────────────────────────────────────────
    double ReactorThermalPower_MW,
    double ReactorCoreLength_mm,
    double ReactorCoreDiameter_mm,
    double FuelLoadingFraction,
    // ── Propellant + nozzle (SA dims 1-4) ─────────────────────────────────────
    double PropellantMassFlow_kgs,
    double ChamberPressure_bar,
    double ThroatRadius_mm,
    double ExpansionRatio,
    double NozzleLength_mm,
    // ── Regen cooling (SA dim 5) ───────────────────────────────────────────────
    double RegenChannelDepth_mm,
    double RegenChannelCount,
    // ── LPBF nozzle geometry ───────────────────────────────────────────────────
    double NozzleWallThickness_mm,
    double NozzleChannelWidth_mm,
    double NozzleManifoldDepth_mm) : IEngineDesign
{
    /// <inheritdoc />
    public string Family => EngineFamilies.Nuclear;

    /// <summary>Reactor core volume [m³] = π × (D/2)² × L.</summary>
    public double ReactorCoreVolume_m3
    {
        get
        {
            double r_m = ReactorCoreDiameter_mm * 0.5e-3;
            double l_m = ReactorCoreLength_mm * 1e-3;
            return Math.PI * r_m * r_m * l_m;
        }
    }

    /// <summary>Volumetric heat flux [MW/m³] = P_MW / V_core.</summary>
    public double VolumetricHeatFlux_MWm3
        => ReactorCoreVolume_m3 > 0 ? ReactorThermalPower_MW / ReactorCoreVolume_m3 : double.NaN;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any numeric field is NaN, non-positive, or outside its
    /// allowed range — including the activated Wave-2 fuel-pin fields when
    /// the per-pin model is requested. The pin-pitch / pin-diameter
    /// cross-field check also throws here.
    /// </exception>
    public void ValidateSelf()
    {
        if (double.IsNaN(ReactorThermalPower_MW) || ReactorThermalPower_MW <= 0)
            throw new ArgumentOutOfRangeException(nameof(ReactorThermalPower_MW),
                $"ReactorThermalPower_MW {ReactorThermalPower_MW:F3} must be > 0 MW_th.");
        if (double.IsNaN(PropellantMassFlow_kgs) || PropellantMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(PropellantMassFlow_kgs),
                $"PropellantMassFlow_kgs {PropellantMassFlow_kgs:F3} must be > 0 kg/s.");
        if (double.IsNaN(ChamberPressure_bar) || ChamberPressure_bar <= 0)
            throw new ArgumentOutOfRangeException(nameof(ChamberPressure_bar),
                $"ChamberPressure_bar {ChamberPressure_bar:F3} must be > 0 bar.");
        if (double.IsNaN(ThroatRadius_mm) || ThroatRadius_mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(ThroatRadius_mm),
                $"ThroatRadius_mm {ThroatRadius_mm:F3} must be > 0 mm.");
        if (double.IsNaN(ExpansionRatio) || ExpansionRatio <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(ExpansionRatio),
                $"ExpansionRatio {ExpansionRatio:F3} must be > 1.");
        if (double.IsNaN(FuelLoadingFraction) || FuelLoadingFraction < 0.0 || FuelLoadingFraction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(FuelLoadingFraction),
                $"FuelLoadingFraction {FuelLoadingFraction:F3} must be in [0, 1].");
        if (double.IsNaN(RegenChannelCount) || RegenChannelCount < 1)
            throw new ArgumentOutOfRangeException(nameof(RegenChannelCount),
                $"RegenChannelCount {RegenChannelCount:F0} must be ≥ 1.");

        // Wave-2 fuel-pin fields (Sprint NU.W2). When the fuel-pin model is
        // requested via finite values, validate consistency; NaN defaults
        // skip the per-pin model entirely (Wave-1 lumped-only).
        if (!double.IsNaN(FuelPinDiameter_mm) || !double.IsNaN(FuelPinPitch_mm)
            || FuelElementCount > 0 || FuelPinHexRings > 0)
        {
            if (double.IsNaN(FuelPinDiameter_mm) || FuelPinDiameter_mm <= 0)
                throw new ArgumentOutOfRangeException(nameof(FuelPinDiameter_mm),
                    $"FuelPinDiameter_mm {FuelPinDiameter_mm:F3} must be > 0 mm "
                  + "when the per-pin model is activated.");
            if (double.IsNaN(FuelPinPitch_mm) || FuelPinPitch_mm <= 0)
                throw new ArgumentOutOfRangeException(nameof(FuelPinPitch_mm),
                    $"FuelPinPitch_mm {FuelPinPitch_mm:F3} must be > 0 mm "
                  + "when the per-pin model is activated.");
            if (FuelPinPitch_mm <= FuelPinDiameter_mm)
                throw new ArgumentOutOfRangeException(nameof(FuelPinPitch_mm),
                    $"FuelPinPitch_mm {FuelPinPitch_mm:F3} mm must exceed "
                  + $"FuelPinDiameter_mm {FuelPinDiameter_mm:F3} mm for valid hex packing.");
            if (FuelElementCount < 1)
                throw new ArgumentOutOfRangeException(nameof(FuelElementCount),
                    $"FuelElementCount {FuelElementCount} must be ≥ 1 "
                  + "when the per-pin model is activated.");
            if (FuelPinHexRings < 1)
                throw new ArgumentOutOfRangeException(nameof(FuelPinHexRings),
                    $"FuelPinHexRings {FuelPinHexRings} must be ≥ 1 "
                  + "when the per-pin model is activated.");
            if (double.IsNaN(FuelPinLength_m) || FuelPinLength_m <= 0)
                throw new ArgumentOutOfRangeException(nameof(FuelPinLength_m),
                    $"FuelPinLength_m {FuelPinLength_m:F4} must be > 0 m "
                  + "when the per-pin model is activated.");
        }
    }

    // ── Wave-2 fuel-pin fields (Sprint NU.W2) ───────────────────────────
    //
    // Per ADR-026 D3, fuel-pin geometry rides on the design record as init-
    // only properties with NaN/sentinel defaults. Wave-1 (lumped reactor)
    // ignores all of these — the per-pin model only runs when the four
    // required fields are populated.
    //
    // Schema nuclear v1 → v2 identity migration leaves them at default for
    // round-tripped Wave-1 designs.

    /// <summary>
    /// Fuel-pin outer diameter [mm]. Drives the per-pin heat-conduction
    /// model. Wave-1 default <see cref="double.NaN"/> (per-pin model off).
    /// NERVA NRX-A6 cluster: ≈ 2.5 mm.
    /// </summary>
    public double FuelPinDiameter_mm { get; init; } = double.NaN;

    /// <summary>
    /// Centre-to-centre pin pitch in the hex array [mm]. Must exceed
    /// <see cref="FuelPinDiameter_mm"/>. NERVA NRX-A6 cluster: ≈ 3.2 mm.
    /// </summary>
    public double FuelPinPitch_mm { get; init; } = double.NaN;

    /// <summary>
    /// Number of concentric pin rings in the hex element (excluding the
    /// centre pin). 0 = 1 pin, 1 = 7 pins, 2 = 19 pins, 3 = 37 pins.
    /// NERVA NRX-A6 cluster: 2 rings (19 pins per element).
    /// </summary>
    public int FuelPinHexRings { get; init; } = 0;

    /// <summary>
    /// Number of hexagonal fuel elements in the reactor core. NERVA NRX-A6
    /// cluster: ≈ 564 elements (the count Bennett 1972 reports for the
    /// flight-weight design point). Default 0 (per-pin model off).
    /// </summary>
    public int FuelElementCount { get; init; } = 0;

    /// <summary>
    /// Per-pin axial length [m]. Typically = ReactorCoreLength_mm/1000 but
    /// can be overridden when the pin extends only over the active fuel
    /// region (NERVA reflector / shield don't extend the pin).
    /// </summary>
    public double FuelPinLength_m { get; init; } = double.NaN;

    /// <summary>
    /// Optional hot-channel factor F_hc override (axial + radial power-
    /// peaking factor). NaN uses
    /// <see cref="FuelPin.FuelPinHeatModel.DefaultHotChannelFactor"/> = 1.40
    /// (NERVA NRX-A6 cluster). Set when fixture-derived calibration data
    /// exists.
    /// </summary>
    public double FuelPinHotChannelFactor { get; init; } = double.NaN;

    /// <summary>
    /// Sprint NU.W4 — fuel material discriminator. Drives the per-pin
    /// model's thermal conductivity + the centreline-temperature hard
    /// gate limit. <see cref="NuclearFuelMaterial.None"/> (default) maps
    /// to UO₂-cermet for backwards compat with Wave-1 / Wave-2 designs.
    /// </summary>
    public NuclearFuelMaterial FuelMaterial { get; init; } = NuclearFuelMaterial.None;

    /// <summary>
    /// Sprint NU.W5 — uranium enrichment tier discriminator. Drives the
    /// per-tier maximum volumetric heat flux that the
    /// <c>NTR_THERMAL_FLUX_EXCEEDED</c> gate tolerates.
    /// <see cref="UraniumEnrichment.None"/> (default) maps to HEU
    /// (4000 MW/m³ ceiling) for backwards compat with Wave-1/W2/W3/W4
    /// designs that pre-date the tier discriminator.
    /// </summary>
    public UraniumEnrichment EnrichmentTier { get; init; } = UraniumEnrichment.None;

    // ── Wave-3 bimodal NTR fields (Sprint NU.W3) ────────────────────────
    //
    // Bimodal NTR adds a closed-cycle He Brayton loop alongside the
    // standard LH₂ thrust pipeline. The Brayton loop produces electric
    // power; the design knobs below configure the loop and the operating
    // mode. Only meaningful when <see cref="Kind"/> = BimodalNtr; ignored
    // otherwise.
    //
    // Schema nuclear v2 → v3 identity migration leaves them at default
    // for round-tripped NervaSolidCore designs.

    /// <summary>
    /// Bimodal operating mode. Only meaningful when
    /// <see cref="Kind"/> = <see cref="NuclearKind.BimodalNtr"/>.
    /// Default <see cref="BimodalMode.Thrust"/> reproduces NervaSolidCore
    /// behaviour bit-identically.
    /// </summary>
    public BimodalMode BimodalMode { get; init; } = BimodalMode.Thrust;

    /// <summary>
    /// Electric-power output target [kW_e]. Bimodal NTR only — other kinds
    /// ignore. Cluster envelope 10–500 kWe (SP-100 anchor ~100 kWe).
    /// Default 0.0 (pure thrust mode, no Brayton loop).
    /// </summary>
    public double ElectricPowerTarget_kWe { get; init; } = 0.0;

    /// <summary>
    /// Brayton He-loop turbine inlet temperature T_hot [K]. Bimodal NTR
    /// only — other kinds ignore. Cluster envelope 1100–1400 K (SP-100
    /// anchor ~1300 K). Default 0.0.
    /// </summary>
    public double BraytonTurbineInletTemp_K { get; init; } = 0.0;

    /// <summary>
    /// Brayton He-loop high-side working-fluid pressure [bar]. Bimodal NTR
    /// only — other kinds ignore. Cluster envelope 5–15 MPa (SP-100 anchor
    /// ~12 MPa = 120 bar). Default 0.0.
    /// </summary>
    public double BraytonHePressure_bar { get; init; } = 0.0;

    /// <summary>
    /// Brayton alternator shaft design RPM. Bimodal NTR only — other kinds
    /// ignore. Cluster envelope 30 000–60 000 RPM (high-speed permanent-
    /// magnet alternators standard for space-Brayton). Default 0.0.
    /// </summary>
    public double AlternatorRpm { get; init; } = 0.0;

    /// <summary>
    /// Optional recuperator effectiveness ε_recup ∈ [0, 1] override.
    /// Bimodal NTR only — other kinds ignore. NaN uses
    /// <see cref="Brayton.BraytonGasLoopSolver.DefaultRecuperatorEffectiveness"/>
    /// = 0.90 cluster anchor.
    /// </summary>
    public double BraytonRecuperatorEffectiveness { get; init; } = double.NaN;
}
