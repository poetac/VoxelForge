// PvPanelDesign.cs — Sprint PV.W1 photovoltaic panel design record.
//
// Sized to bracket SunPower Maxeon X22-class panels (96 series-
// connected mono-Si cells, ~ 1.55 m² aperture, ~ 360 W STC rated).
// Wave-1 baseline is a single-string panel; future Wave-2 will admit
// multi-string panels for partial-shading studies.

using System;

namespace Voxelforge.Photovoltaic;

/// <summary>
/// Design parameters for a photovoltaic panel (Sprint PV.W1 scaffold).
/// Standalone — does not integrate with the <c>IEngine&lt;,,&gt;</c>
/// stack yet (deferred to a future PV.W2 sprint).
/// </summary>
/// <param name="CellType">Cell technology — drives STC properties.</param>
/// <param name="CellsInSeries">Number of cells series-connected in the string [-].</param>
/// <param name="StringsInParallel">Number of parallel strings [-] (Wave-1 typically 1).</param>
/// <param name="CellArea_cm2">Aperture area of one cell [cm²].</param>
/// <param name="Irradiance_W_m2">Plane-of-array irradiance G [W/m²]. STC = 1000.</param>
/// <param name="CellTemperature_C">Cell temperature [°C]. STC = 25.</param>
internal sealed record PvPanelDesign(
    PhotovoltaicCellType CellType,
    int    CellsInSeries,
    int    StringsInParallel,
    double CellArea_cm2,
    double Irradiance_W_m2,
    double CellTemperature_C)
{
    /// <summary>Total cell count = N_series × N_parallel.</summary>
    public int TotalCellCount => CellsInSeries * StringsInParallel;

    /// <summary>
    /// Sprint PV.W2. Rear-side irradiance gain factor φ [-] = G_rear /
    /// G_front. Typical mounting + ground-albedo cluster: 0.05-0.30.
    /// Defaults to 0 (monofacial PV.W1 baseline → bit-identical
    /// behaviour).
    /// </summary>
    public double RearSideIrradianceGain { get; init; } = 0.0;

    /// <summary>
    /// Sprint PV.W2. Bifaciality factor β [-] — rear-side I_sc / front-
    /// side I_sc at the same G. HJT cells ≈ 0.90; PERC ≈ 0.70;
    /// mono-PERC ≈ 0. Multiplied with RearSideIrradianceGain to give
    /// the effective P boost: P_bifacial = P_front · (1 + φ · β).
    /// Ignored when φ = 0 (PV.W1 default).
    /// </summary>
    public double BifacialityFactor { get; init; } = 0.0;

    /// <summary>Panel aperture area [m²].</summary>
    public double PanelArea_m2 => TotalCellCount * CellArea_cm2 * 1e-4;

    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (CellType == PhotovoltaicCellType.None)
            throw new ArgumentException(
                "CellType must be set (None sentinel is reserved).", nameof(CellType));
        if (CellsInSeries <= 0)
            throw new ArgumentException("CellsInSeries must be > 0.", nameof(CellsInSeries));
        if (StringsInParallel <= 0)
            throw new ArgumentException("StringsInParallel must be > 0.", nameof(StringsInParallel));
        if (CellArea_cm2 <= 0)
            throw new ArgumentException("CellArea_cm2 must be > 0.", nameof(CellArea_cm2));
        if (Irradiance_W_m2 < 0)
            throw new ArgumentException("Irradiance_W_m2 must be ≥ 0 (negative is non-physical).",
                nameof(Irradiance_W_m2));
        if (CellTemperature_C < -50 || CellTemperature_C > 150)
            throw new ArgumentException(
                $"CellTemperature_C must be in [-50, 150]; got {CellTemperature_C}.",
                nameof(CellTemperature_C));
        if (RearSideIrradianceGain < 0 || RearSideIrradianceGain > 1.0)
            throw new ArgumentException(
                "RearSideIrradianceGain must be in [0, 1].",
                nameof(RearSideIrradianceGain));
        if (BifacialityFactor < 0 || BifacialityFactor > 1.0)
            throw new ArgumentException(
                "BifacialityFactor must be in [0, 1].",
                nameof(BifacialityFactor));
    }
}
