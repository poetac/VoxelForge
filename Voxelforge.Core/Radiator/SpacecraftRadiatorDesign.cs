// SpacecraftRadiatorDesign.cs — Sprint RAD.W1 spacecraft-radiator
// design record.
//
// Sized to bracket the ISS PV-side radiator panel cluster (~ 30 m²
// per panel, ε ≈ 0.85, T_panel ≈ 320 K, T_sink ≈ 240 K, ~ 270 W/m²
// net rejection).

using System;

namespace Voxelforge.Radiator;

/// <summary>
/// Design parameters for a spacecraft flat-panel radiator (Sprint
/// RAD.W1 scaffold). Standalone — does not integrate with the
/// <c>IEngine&lt;,,&gt;</c> stack yet.
/// </summary>
/// <param name="Kind">Sub-variant.</param>
/// <param name="PanelArea_m2">Single-side radiating area A [m²]. For two-
/// sided panels, double this value before passing in.</param>
/// <param name="OperatingTemperature_K">Panel temperature T_panel [K].</param>
/// <param name="SinkTemperature_K">Effective radiative sink T_sink [K]
/// (deep space ≈ 3 K; LEO with full Earth-IR contribution ≈ 240-270 K).</param>
/// <param name="Emissivity">Radiative emissivity ε ∈ (0, 1] [-]. White
/// paint ≈ 0.85; OSR (optical solar reflector) ≈ 0.79; bare aluminium
/// ≈ 0.05.</param>
/// <param name="SolarAbsorptivity">Solar absorptivity α ∈ [0, 1] [-]
/// against the incident solar flux. White paint ≈ 0.20; OSR ≈ 0.08.</param>
/// <param name="IncidentSolarFlux_W_m2">Direct + reflected solar
/// irradiance on the panel [W/m²]. 1361 (solar constant) when sun-
/// facing; 0 when in eclipse / shaded.</param>
internal sealed record SpacecraftRadiatorDesign(
    RadiatorKind Kind,
    double PanelArea_m2,
    double OperatingTemperature_K,
    double SinkTemperature_K,
    double Emissivity,
    double SolarAbsorptivity,
    double IncidentSolarFlux_W_m2)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    public void ValidateSelf()
    {
        if (Kind == RadiatorKind.None)
            throw new ArgumentException(
                $"Kind must be FlatPanel or TwoSidedDeployable; got {Kind}.",
                nameof(Kind));
        if (PanelArea_m2 <= 0)
            throw new ArgumentException("PanelArea_m2 must be > 0.", nameof(PanelArea_m2));
        if (OperatingTemperature_K <= 0)
            throw new ArgumentException("OperatingTemperature_K must be > 0.",
                nameof(OperatingTemperature_K));
        if (SinkTemperature_K < 0)
            throw new ArgumentException("SinkTemperature_K must be ≥ 0.",
                nameof(SinkTemperature_K));
        if (Emissivity <= 0 || Emissivity > 1.0)
            throw new ArgumentException(
                "Emissivity must be in (0, 1].", nameof(Emissivity));
        if (SolarAbsorptivity < 0 || SolarAbsorptivity > 1.0)
            throw new ArgumentException(
                "SolarAbsorptivity must be in [0, 1].", nameof(SolarAbsorptivity));
        if (IncidentSolarFlux_W_m2 < 0)
            throw new ArgumentException(
                "IncidentSolarFlux_W_m2 must be ≥ 0 (negative is non-physical).",
                nameof(IncidentSolarFlux_W_m2));
    }
}
