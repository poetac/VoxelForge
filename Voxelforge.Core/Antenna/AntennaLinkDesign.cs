// AntennaLinkDesign.cs — Sprint ANT.W1 RF-link design record.
// Sprint ANT.W3: added Modulation + BandwidthOccupancy_Hz +
// ReceiverNoiseFigure_dB so the link-budget solver can compute a
// receiver sensitivity floor + achieved Eb/N₀ in addition to the
// Wave-1 Friis snapshot. Defaults preserve every pre-ANT.W3 test
// (QPSK uncoded at 1 MHz BW + 3 dB NF lands in the same numerical
// regime as the implicit Wave-1 link).
// Sprint ANT.W2: added propagation-environment + system-loss-budget
// fields (ElevationAngle_deg, RainRate_mmPerHr, PointingLoss_dB,
// PolarisationMismatch_dB, CableConnectorLoss_dB). All optional with
// physically representative defaults; pre-ANT.W2 call sites compile
// unchanged. The solver uses these to compute rain attenuation (P.838-3),
// atmospheric absorption (P.676-12), and a LinkClosureMargin_dB gate.
// Sprint ANT.W4: added HelicalTurns, HelicalCircumference_rel,
// HelicalTurnSpacing_rel to parameterise the Kraus end-fire helical
// gain formula for the new Helical AntennaKind.
// Sprint ANT.W5: added OrbitalAltitude_km + RainRate0p01pct_mmPerHr
// for the ElevationSweepSolver contact-window + statistical-margin path.
// Sprint ANT.W6: added PrintMaterialKind + SubstrateThickness_mm +
// PatchWidth_mm + PatchLength_mm for the patch voxel builder and
// printability gates.
// Sprint ANT.W7: added HelicalCoilDiameter_mm + YagiElementSpacing_mm
// for the geometry → RF coupling advisory gate.

using System;
using Voxelforge.Optimization;

namespace Voxelforge.Antenna;

/// <summary>
/// Design parameters for a point-to-point RF link (Sprint ANT.W1
/// scaffold, ANT.W3 extension). Both ends are characterised by their
/// antenna topology + (if parabolic) dish diameter + aperture
/// efficiency. ANT.W3 adds the modulation/FEC tag + receiver-bandwidth
/// + noise-figure trio so the solver can compute a sensitivity floor.
/// </summary>
/// <param name="TransmitAntennaKind">Tx antenna topology.</param>
/// <param name="ReceiveAntennaKind">Rx antenna topology.</param>
/// <param name="Frequency_Hz">f [Hz] — carrier frequency.</param>
/// <param name="TransmitPower_W">P_tx [W] — at the Tx antenna feed.</param>
/// <param name="LinkDistance_m">R [m] — slant range from Tx to Rx.</param>
/// <param name="TransmitDishDiameter_m">D_tx [m] — only used if Tx is
/// ParabolicDish. Ignored for IdealIsotropic / HalfWaveDipole.</param>
/// <param name="ReceiveDishDiameter_m">D_rx [m] — only used if Rx is
/// ParabolicDish.</param>
/// <param name="DishApertureEfficiency">η_aperture ∈ (0, 1] [-]. Modern
/// shaped-feed dishes cluster 0.6-0.8.</param>
/// <param name="Modulation">ANT.W3 — categorical modulation + FEC tag.
/// Default <see cref="ModulationScheme.QpskUncoded"/>; required Eb/N₀
/// is looked up via <see cref="ModulationSchemeTable.RequiredEbN0_dB"/>.</param>
/// <param name="BandwidthOccupancy_Hz">ANT.W3 — receiver noise
/// bandwidth [Hz]. Default 1 MHz (typical commercial telemetry
/// channel). Drives the thermal-noise floor via
/// <see cref="ReceiverSensitivityCalculator.ThermalNoiseFloor_dBm"/>.</param>
/// <param name="ReceiverNoiseFigure_dB">ANT.W3 — receiver noise figure
/// [dB]. Default 3 dB (typical X/Ku-band LNA + downconverter chain).</param>
/// <param name="ElevationAngle_deg">ANT.W2 — slant-path elevation above
/// the local horizon [°]. Drives both the rain slant-path length model
/// (ITU-R P.618-13) and the atmospheric absorption path (ITU-R P.676-12).
/// Default 10° (low-elevation edge of typical satellite-service mask).</param>
/// <param name="RainRate_mmPerHr">ANT.W2 — specific rain rate [mm/hr].
/// 0 = clear sky (default). Typical: 5 (light), 25 (heavy), 50 (intense).
/// ITU-R P.838-3 horizontal polarization model is used.</param>
/// <param name="PointingLoss_dB">ANT.W2 — combined Tx + Rx pointing-error
/// budget [dB]. Includes mechanical-pointing error + refraction offset.
/// Default 0.5 dB (typical for a tracked ground station).</param>
/// <param name="PolarisationMismatch_dB">ANT.W2 — polarisation mismatch
/// loss [dB]. 0 = perfectly matched (default); 3 dB = circular-vs-linear
/// worst case.</param>
/// <param name="CableConnectorLoss_dB">ANT.W2 — combined feedline +
/// connector insertion loss [dB]. Default 0.5 dB (short LNA-to-feed
/// run). Higher for long waveguide runs.</param>
/// <param name="HelicalTurns">ANT.W4 — number of helix turns N for the
/// Kraus end-fire formula G = 15·N·(C/λ)²·(S/λ). Used when either
/// endpoint is <see cref="AntennaKind.Helical"/>. Default 10 (mid-gain
/// end-fire helix; G ≈ 15.7 dBi at Kraus-optimal C/λ=1, S/λ=0.25).</param>
/// <param name="HelicalCircumference_rel">ANT.W4 — helix circumference as
/// a multiple of wavelength (C/λ). Kraus end-fire mode requires
/// C/λ ∈ [0.75, 1.33] (Kraus 1988 §7-4). Default 1.0 (optimal).</param>
/// <param name="HelicalTurnSpacing_rel">ANT.W4 — helix turn spacing as a
/// multiple of wavelength (S/λ). Default 0.25 (Kraus optimal, pitch angle
/// α ≈ 14°). Increasing S/λ above 0.5 reduces the end-fire mode gain.</param>
/// <param name="OrbitalAltitude_km">ANT.W5 — circular-orbit altitude [km]
/// above Earth's surface. Used by <see cref="ElevationSweepSolver"/> to
/// compute the Keplerian period and contact-window duration. Default 550
/// (Starlink Gen-1 shell; LEO). Set to a GEO altitude (35 786 km) for
/// fixed-satellite service designs.</param>
/// <param name="RainRate0p01pct_mmPerHr">ANT.W5 — rain rate [mm/hr]
/// exceeded 0.01 % of time (≈ 87.6 h/year) at the ground-station site,
/// used as the anchor for the ITU-R P.837 power-law exceedance CDF. 0 =
/// clear sky / no rain-margin statistics (default). Typical mid-latitude
/// (ITU zone K/L) value ≈ 63 mm/hr; tropical (zone P) ≈ 145 mm/hr.</param>
/// <param name="PrintMaterialKind">ANT.W6 — 3D-printing material for the
/// antenna voxel builder. Drives the minimum-feature gate
/// (<see cref="AntennaConstraintIds.WireTooThin"/>) and the patch
/// substrate permittivity for ANT.W7 resonance coupling. Default
/// <see cref="PrintMaterial.Lpbf316L"/> (laser powder-bed fusion 316L
/// stainless steel — typical for structural metallic antennas).</param>
/// <param name="SubstrateThickness_mm">ANT.W6 — patch substrate
/// thickness [mm]. Used by the Bahl-Trivedi effective-permittivity
/// formula for resonant frequency computation (ANT.W7). Default 1.6 mm
/// (standard PCB-class thickness; typical FR4 / SLA-Rogers laminates).
/// Set to 0 to disable substrate-thickness checks.</param>
/// <param name="PatchWidth_mm">ANT.W6 — physical patch width [mm]. 0
/// (default) = auto-compute from frequency and
/// <see cref="PrintMaterialKind"/> via the Bahl-Trivedi width formula.
/// A non-zero value is used directly and triggers the ANT.W7 resonance
/// mismatch check if it deviates from the auto-computed value by &gt; 5 %.
/// </param>
/// <param name="PatchLength_mm">ANT.W6 — physical patch length [mm]. 0
/// (default) = auto-compute. Non-zero → ANT.W7 resonance check.</param>
/// <param name="HelicalCoilDiameter_mm">ANT.W7 — physical helix coil
/// diameter [mm] (outer diameter of the helical wire coil, measured from
/// one wire centre to the opposite wire centre). 0 (default) = use the
/// C/λ parametric design variables only (no geometry-coupling check).
/// Non-zero → AntennaSolver checks whether π·D/λ matches
/// <see cref="HelicalCircumference_rel"/> within 5 %
/// (gate <see cref="AntennaConstraintIds.GeometryRfMismatch"/>).</param>
/// <param name="YagiElementSpacing_mm">ANT.W7 — physical director-element
/// spacing [mm]. 0 (default) = no coupling check. Non-zero → AntennaSolver
/// validates spacing/λ ∈ [0.1, 0.5] (Yagi-Uda optimal range;
/// gate <see cref="AntennaConstraintIds.GeometryRfMismatch"/>).</param>
internal sealed record AntennaLinkDesign(
    AntennaKind TransmitAntennaKind,
    AntennaKind ReceiveAntennaKind,
    double Frequency_Hz,
    double TransmitPower_W,
    double LinkDistance_m,
    double TransmitDishDiameter_m      = 0.0,
    double ReceiveDishDiameter_m       = 0.0,
    double DishApertureEfficiency      = 0.65,
    ModulationScheme Modulation        = ModulationScheme.QpskUncoded,
    double BandwidthOccupancy_Hz       = 1.0e6,
    double ReceiverNoiseFigure_dB      = 3.0,
    double ElevationAngle_deg          = 10.0,
    double RainRate_mmPerHr            = 0.0,
    double PointingLoss_dB             = 0.5,
    double PolarisationMismatch_dB     = 0.0,
    double CableConnectorLoss_dB       = 0.5,
    int    HelicalTurns                = 10,
    double HelicalCircumference_rel    = 1.0,
    double HelicalTurnSpacing_rel      = 0.25,
    // ANT.W5 — orbital + statistical margin fields.
    double OrbitalAltitude_km          = 550.0,
    double RainRate0p01pct_mmPerHr     = 0.0,
    // ANT.W6 — print-material + substrate fields.
    PrintMaterial PrintMaterialKind    = PrintMaterial.Lpbf316L,
    double SubstrateThickness_mm       = 1.6,
    double PatchWidth_mm               = 0.0,
    double PatchLength_mm              = 0.0,
    // ANT.W7 — geometry → RF coupling fields.
    double HelicalCoilDiameter_mm      = 0.0,
    double YagiElementSpacing_mm       = 0.0)
{
    /// <summary>
    /// ANT.W3 — categorical modulation scheme exposed as an SA
    /// design dimension. The attribute lets <see cref="DesignVariableRegistry"/>
    /// discover the property; the SA hot path samples the integer
    /// <see cref="ModulationSchemeIndex"/> dim, which is mirrored to
    /// this enum field by <see cref="WithModulationIndex"/>.
    /// <para>
    /// The single-dim categorical encoding (0..Count-1 with integer
    /// rounding) is the simplest way to make a non-numeric choice an
    /// SA-search variable without introducing a new attribute shape —
    /// it slots into the existing <c>ConvertForProperty</c> int path.
    /// </para>
    /// </summary>
    [SaDesignVariable(index: 0, min: 0.0, max: ModulationSchemeTable.Count - 1)]
    public int ModulationSchemeIndex => ModulationSchemeTable.ToIndex(Modulation);

    /// <summary>
    /// Return a clone of this design with <see cref="Modulation"/>
    /// replaced by the scheme that <paramref name="index"/> identifies.
    /// Wraps <see cref="ModulationSchemeTable.FromIndex"/> for SA
    /// Unpack call sites — the binder's int-typed slot maps to this
    /// helper to recover the categorical state from the sampled dim.
    /// </summary>
    public AntennaLinkDesign WithModulationIndex(int index)
        => this with { Modulation = ModulationSchemeTable.FromIndex(index) };

    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentException">
    /// When a sentinel <see cref="AntennaKind.None"/> is used for either
    /// endpoint (categorical failure).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When any numeric field is NaN, non-positive, or out of its physical
    /// range — frequency, transmit power, link distance, aperture efficiency,
    /// bandwidth, noise figure, or a parabolic-dish endpoint whose
    /// diameter is &lt;= 0.
    /// </exception>
    public void ValidateSelf()
    {
        if (TransmitAntennaKind == AntennaKind.None)
            throw new ArgumentException(
                "TransmitAntennaKind must be set (None sentinel is reserved).",
                nameof(TransmitAntennaKind));
        if (ReceiveAntennaKind == AntennaKind.None)
            throw new ArgumentException(
                "ReceiveAntennaKind must be set (None sentinel is reserved).",
                nameof(ReceiveAntennaKind));
        if (double.IsNaN(Frequency_Hz) || Frequency_Hz <= 0)
            throw new ArgumentOutOfRangeException(nameof(Frequency_Hz),
                $"Frequency_Hz={Frequency_Hz:E3} must be > 0.");
        if (double.IsNaN(TransmitPower_W) || TransmitPower_W <= 0)
            throw new ArgumentOutOfRangeException(nameof(TransmitPower_W),
                $"TransmitPower_W={TransmitPower_W:F3} must be > 0.");
        if (double.IsNaN(LinkDistance_m) || LinkDistance_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(LinkDistance_m),
                $"LinkDistance_m={LinkDistance_m:E3} must be > 0.");
        if (double.IsNaN(DishApertureEfficiency)
         || DishApertureEfficiency <= 0
         || DishApertureEfficiency > 1.0)
            throw new ArgumentOutOfRangeException(nameof(DishApertureEfficiency),
                $"DishApertureEfficiency={DishApertureEfficiency:F3} must be in (0, 1].");
        if (TransmitAntennaKind == AntennaKind.ParabolicDish
         && (double.IsNaN(TransmitDishDiameter_m) || TransmitDishDiameter_m <= 0))
            throw new ArgumentOutOfRangeException(nameof(TransmitDishDiameter_m),
                $"TransmitDishDiameter_m={TransmitDishDiameter_m:F3} must be > 0 "
              + "for ParabolicDish Tx.");
        if (ReceiveAntennaKind == AntennaKind.ParabolicDish
         && (double.IsNaN(ReceiveDishDiameter_m) || ReceiveDishDiameter_m <= 0))
            throw new ArgumentOutOfRangeException(nameof(ReceiveDishDiameter_m),
                $"ReceiveDishDiameter_m={ReceiveDishDiameter_m:F3} must be > 0 "
              + "for ParabolicDish Rx.");
        if (double.IsNaN(BandwidthOccupancy_Hz) || BandwidthOccupancy_Hz <= 0)
            throw new ArgumentOutOfRangeException(nameof(BandwidthOccupancy_Hz),
                $"BandwidthOccupancy_Hz={BandwidthOccupancy_Hz:E3} must be > 0.");
        if (double.IsNaN(ReceiverNoiseFigure_dB))
            throw new ArgumentOutOfRangeException(nameof(ReceiverNoiseFigure_dB),
                $"ReceiverNoiseFigure_dB={ReceiverNoiseFigure_dB:F3} must be a "
              + "real number (was NaN).");
        // ANT.W2 — propagation environment + system loss budget.
        if (double.IsNaN(ElevationAngle_deg) || ElevationAngle_deg <= 0 || ElevationAngle_deg > 90)
            throw new ArgumentOutOfRangeException(nameof(ElevationAngle_deg),
                $"ElevationAngle_deg={ElevationAngle_deg:F2} must be in (0, 90].");
        if (double.IsNaN(RainRate_mmPerHr) || RainRate_mmPerHr < 0)
            throw new ArgumentOutOfRangeException(nameof(RainRate_mmPerHr),
                $"RainRate_mmPerHr={RainRate_mmPerHr:F1} must be ≥ 0 (0 = clear sky).");
        if (double.IsNaN(PointingLoss_dB) || PointingLoss_dB < 0)
            throw new ArgumentOutOfRangeException(nameof(PointingLoss_dB),
                $"PointingLoss_dB={PointingLoss_dB:F2} must be ≥ 0.");
        if (double.IsNaN(PolarisationMismatch_dB) || PolarisationMismatch_dB < 0)
            throw new ArgumentOutOfRangeException(nameof(PolarisationMismatch_dB),
                $"PolarisationMismatch_dB={PolarisationMismatch_dB:F2} must be ≥ 0.");
        if (double.IsNaN(CableConnectorLoss_dB) || CableConnectorLoss_dB < 0)
            throw new ArgumentOutOfRangeException(nameof(CableConnectorLoss_dB),
                $"CableConnectorLoss_dB={CableConnectorLoss_dB:F2} must be ≥ 0.");
        // ANT.W4 — helical antenna parameters.
        if (HelicalTurns < 1)
            throw new ArgumentOutOfRangeException(nameof(HelicalTurns),
                $"HelicalTurns={HelicalTurns} must be ≥ 1.");
        if (double.IsNaN(HelicalCircumference_rel) || HelicalCircumference_rel <= 0)
            throw new ArgumentOutOfRangeException(nameof(HelicalCircumference_rel),
                $"HelicalCircumference_rel={HelicalCircumference_rel:F3} must be > 0.");
        if (double.IsNaN(HelicalTurnSpacing_rel) || HelicalTurnSpacing_rel <= 0)
            throw new ArgumentOutOfRangeException(nameof(HelicalTurnSpacing_rel),
                $"HelicalTurnSpacing_rel={HelicalTurnSpacing_rel:F3} must be > 0.");
        // ANT.W5 — orbital + statistical margin parameters.
        if (double.IsNaN(OrbitalAltitude_km) || OrbitalAltitude_km <= 0)
            throw new ArgumentOutOfRangeException(nameof(OrbitalAltitude_km),
                $"OrbitalAltitude_km={OrbitalAltitude_km:F1} must be > 0.");
        if (double.IsNaN(RainRate0p01pct_mmPerHr) || RainRate0p01pct_mmPerHr < 0)
            throw new ArgumentOutOfRangeException(nameof(RainRate0p01pct_mmPerHr),
                $"RainRate0p01pct_mmPerHr={RainRate0p01pct_mmPerHr:F1} must be ≥ 0.");
        // ANT.W6 — print-material + substrate parameters.
        if (double.IsNaN(SubstrateThickness_mm) || SubstrateThickness_mm < 0)
            throw new ArgumentOutOfRangeException(nameof(SubstrateThickness_mm),
                $"SubstrateThickness_mm={SubstrateThickness_mm:F3} must be ≥ 0.");
        if (double.IsNaN(PatchWidth_mm) || PatchWidth_mm < 0)
            throw new ArgumentOutOfRangeException(nameof(PatchWidth_mm),
                $"PatchWidth_mm={PatchWidth_mm:F3} must be ≥ 0.");
        if (double.IsNaN(PatchLength_mm) || PatchLength_mm < 0)
            throw new ArgumentOutOfRangeException(nameof(PatchLength_mm),
                $"PatchLength_mm={PatchLength_mm:F3} must be ≥ 0.");
        // ANT.W7 — geometry → RF coupling parameters.
        if (double.IsNaN(HelicalCoilDiameter_mm) || HelicalCoilDiameter_mm < 0)
            throw new ArgumentOutOfRangeException(nameof(HelicalCoilDiameter_mm),
                $"HelicalCoilDiameter_mm={HelicalCoilDiameter_mm:F3} must be ≥ 0.");
        if (double.IsNaN(YagiElementSpacing_mm) || YagiElementSpacing_mm < 0)
            throw new ArgumentOutOfRangeException(nameof(YagiElementSpacing_mm),
                $"YagiElementSpacing_mm={YagiElementSpacing_mm:F3} must be ≥ 0.");
    }
}
