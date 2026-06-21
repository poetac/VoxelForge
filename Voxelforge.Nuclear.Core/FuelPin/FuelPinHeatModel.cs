// FuelPinHeatModel.cs — Sprint NU.W2 lumped-radial heat-conduction model for
// NERVA-class fuel pins.
//
// Stateless, allocation-free, deterministic. Computes peak fuel-pin
// centreline temperature given the reactor's total thermal power, fuel-pin
// geometry, coolant inlet/exit state, and a hot-channel factor.
//
// Physics summary (cylindrical UO₂-cermet fuel pin, axial-uniform power):
//
//   Volumetric heat source:
//     q''' = P_reactor / V_total_fuel             [W/m³]
//
//   Per-pin power:
//     Q_pin = q''' · V_pin = q''' · π·(d/2)²·L
//
//   Surface heat flux (with hot-channel factor F_hc applied):
//     q'' = F_hc · Q_pin / (π·d·L)               [W/m²]
//
//   Coolant-side heat transfer (Dittus-Boelter, H₂ on smooth tubes):
//     Nu = 0.023 · Re^0.8 · Pr^0.4
//     h_cool = Nu · k_H2 / D_h
//
//   Wall-to-coolant ΔT:
//     ΔT_wc = q'' / h_cool
//
//   Pin centreline-to-surface ΔT (1-D radial conduction in a cylinder with
//   uniform heat generation):
//     ΔT_cs = q''' · r²_pin / (4 · k_UO2)
//
//   Peak fuel centreline temperature:
//     T_peak = T_coolant_exit + ΔT_wc + ΔT_cs
//
// Property anchors (UO₂-cermet, NERVA-class):
//   k_UO2_cermet ≈ 16 W/(m·K) at 2500 K — substantially above pure UO₂
//   (~3 W/(m·K)) because the W or Mo-matrix dispersion phase dominates
//   conductivity. Source: Bennett 1972 + Lyon NASA-CR-72757.
//
// H₂ coolant properties at the per-pin coolant temperature use
// LH2ThermalProperties (already in Voxelforge.Combustion).
//
// Validation tolerance per ADR-029 D4 generalised: ±10 % T_peak. The hot-
// channel factor F_hc captures axial + radial power peaking; cluster value
// F_hc ≈ 1.40 (NERVA NRX-A6 measured peak/avg).
//
// Limitations: lumped (no axial march), uniform power (no axial cosine),
// adiabatic outer-element boundary (heat losses to graphite matrix
// neglected at this fidelity).

using System;
using Voxelforge.Combustion;

namespace Voxelforge.Nuclear.FuelPin;

/// <summary>
/// Output of the fuel-pin heat-conduction model.
/// </summary>
/// <param name="PeakFuelCenterlineTemp_K">Peak fuel-pin centreline temperature T_peak [K].</param>
/// <param name="PinSurfaceTemp_K">Pin outer-surface temperature T_surf [K].</param>
/// <param name="CoolantExitTemp_K">Coolant exit temperature T_cool,exit [K].</param>
/// <param name="WallToCoolantDeltaT_K">Wall-to-coolant ΔT_wc [K].</param>
/// <param name="CenterlineToSurfaceDeltaT_K">Centreline-to-surface ΔT_cs [K].</param>
/// <param name="HeatFluxSurface_W_m2">Surface heat flux q'' (with F_hc applied) [W/m²].</param>
/// <param name="VolumetricHeatSource_W_m3">Volumetric heat source q''' [W/m³].</param>
/// <param name="PerPinPower_W">Power per pin Q_pin [W].</param>
/// <param name="ConvectiveHtc_W_m2K">Dittus-Boelter HTC h_cool [W/(m²·K)].</param>
/// <param name="HotChannelFactor">Applied F_hc [-].</param>
public sealed record FuelPinHeatResult(
    double PeakFuelCenterlineTemp_K,
    double PinSurfaceTemp_K,
    double CoolantExitTemp_K,
    double WallToCoolantDeltaT_K,
    double CenterlineToSurfaceDeltaT_K,
    double HeatFluxSurface_W_m2,
    double VolumetricHeatSource_W_m3,
    double PerPinPower_W,
    double ConvectiveHtc_W_m2K,
    double HotChannelFactor);

/// <summary>
/// Cylindrical fuel-pin heat-conduction model. Mirror of
/// <see cref="Voxelforge.Nuclear.NtrCycleSolver"/> for the per-pin thermal
/// path.
/// </summary>
public static class FuelPinHeatModel
{
    /// <summary>
    /// UO₂-cermet (W or Mo matrix) effective thermal conductivity at ~2500 K
    /// [W/(m·K)]. Order-of-magnitude higher than pure UO₂'s ~3 W/(m·K)
    /// because the metal matrix dispersion phase dominates conductivity.
    /// Source: Bennett 1972; Lyon NASA-CR-72757.
    /// </summary>
    /// <remarks>
    /// Sprint NU.W4 generalised the per-pin model to three fuel materials
    /// (<see cref="NuclearFuelMaterial.UO2Cermet"/>,
    /// <see cref="NuclearFuelMaterial.UC2Graphite"/>,
    /// <see cref="NuclearFuelMaterial.UNRefractory"/>); this constant is
    /// kept for backwards compat with Wave-2 callers that don't pass a
    /// material override.
    /// </remarks>
    public const double FuelThermalConductivity_WmK = 16.0;

    /// <summary>
    /// Default hot-channel factor F_hc capturing axial + radial power
    /// peaking. NERVA NRX-A6 measured ≈ 1.40 (Bennett 1972).
    /// </summary>
    public const double DefaultHotChannelFactor = 1.40;

    /// <summary>
    /// Solve the per-pin centreline temperature for the given reactor +
    /// fuel-element geometry.
    /// </summary>
    /// <param name="reactorThermalPower_W">Total reactor power P [W].</param>
    /// <param name="fuelElementCount">Number of fuel elements in the core N_elem.</param>
    /// <param name="hexGeometry">Per-element hex-array geometry.</param>
    /// <param name="fuelPinLength_m">Per-pin axial length L_pin [m].</param>
    /// <param name="coolantMassFlow_kgs">Total coolant mass flow ṁ [kg/s] (split equally across all pins' subchannels).</param>
    /// <param name="coolantInletTemp_K">Coolant inlet temperature T_in [K].</param>
    /// <param name="coolantInletPressure_Pa">Coolant inlet pressure [Pa] — used as the energy-balance reference (not consumed at this fidelity).</param>
    /// <param name="hotChannelFactor">Optional F_hc override; <see cref="double.NaN"/> uses <see cref="DefaultHotChannelFactor"/>.</param>
    /// <param name="fuelMaterial">
    /// Sprint NU.W4 — fuel material discriminator. Drives the per-material
    /// thermal conductivity used in the centreline-to-surface ΔT_cs term.
    /// <see cref="NuclearFuelMaterial.None"/> (default) selects UO₂-cermet
    /// constants for backwards compat with Wave-2 callers.
    /// </param>
    /// <returns>Solved heat state.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="hexGeometry"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any required numeric argument is NaN or non-positive,
    /// <paramref name="fuelElementCount"/> is &lt; 1, or
    /// <paramref name="hotChannelFactor"/> is finite and non-positive.
    /// </exception>
    public static FuelPinHeatResult Solve(
        double                 reactorThermalPower_W,
        int                    fuelElementCount,
        HexArrayGeometryResult hexGeometry,
        double                 fuelPinLength_m,
        double                 coolantMassFlow_kgs,
        double                 coolantInletTemp_K,
        double                 coolantInletPressure_Pa,
        double                 hotChannelFactor = double.NaN,
        NuclearFuelMaterial    fuelMaterial = NuclearFuelMaterial.None)
    {
        if (double.IsNaN(reactorThermalPower_W) || reactorThermalPower_W <= 0)
            throw new ArgumentOutOfRangeException(nameof(reactorThermalPower_W),
                $"ReactorThermalPower_W {reactorThermalPower_W:F1} must be > 0 W.");
        if (fuelElementCount < 1)
            throw new ArgumentOutOfRangeException(nameof(fuelElementCount),
                $"FuelElementCount {fuelElementCount} must be ≥ 1.");
        ArgumentNullException.ThrowIfNull(hexGeometry);
        // Pins must not overlap: PinPitch_mm > PinDiameter_mm. Otherwise the
        // triangular sub-channel flow area A = (√3/4)·p² − (π/8)·d² collapses to
        // ≤ 0; the sub-channel mass flux G = ṁ/A would then be floored to a
        // garbage value, silently over-cooling the pin (vanishing wall ΔT) and
        // letting an impossible geometry slip past the centreline-overtemp gate.
        // NuclearThermalDesign enforces the same relation up front; this guards
        // direct callers of the model and HexArrayGeometry.Resolve — neither of
        // which validates pin overlap.
        if (double.IsNaN(hexGeometry.PinPitch_mm) || double.IsNaN(hexGeometry.PinDiameter_mm)
            || hexGeometry.PinPitch_mm <= hexGeometry.PinDiameter_mm)
            throw new ArgumentOutOfRangeException(nameof(hexGeometry),
                $"HexArray PinPitch_mm {hexGeometry.PinPitch_mm:F3} must exceed PinDiameter_mm "
              + $"{hexGeometry.PinDiameter_mm:F3} (pins cannot overlap); the triangular sub-channel "
              + "flow area would be non-positive.");
        if (double.IsNaN(fuelPinLength_m) || fuelPinLength_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(fuelPinLength_m),
                $"FuelPinLength_m {fuelPinLength_m:F4} must be > 0 m.");
        if (double.IsNaN(coolantMassFlow_kgs) || coolantMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(coolantMassFlow_kgs),
                $"CoolantMassFlow_kgs {coolantMassFlow_kgs:F3} must be > 0 kg/s.");
        if (double.IsNaN(coolantInletTemp_K) || coolantInletTemp_K <= 0)
            throw new ArgumentOutOfRangeException(nameof(coolantInletTemp_K),
                $"CoolantInletTemp_K {coolantInletTemp_K:F1} must be > 0 K.");
        if (double.IsNaN(coolantInletPressure_Pa) || coolantInletPressure_Pa <= 0)
            throw new ArgumentOutOfRangeException(nameof(coolantInletPressure_Pa),
                $"CoolantInletPressure_Pa {coolantInletPressure_Pa:F0} must be > 0 Pa.");
        if (!double.IsNaN(hotChannelFactor) && hotChannelFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(hotChannelFactor),
                $"HotChannelFactor {hotChannelFactor:F3} must be NaN or > 0.");

        double F_hc = double.IsNaN(hotChannelFactor)
            ? DefaultHotChannelFactor
            : hotChannelFactor;

        // ── 1. Per-pin power + volumetric heat source. ──────────────────────
        int totalPins = fuelElementCount * hexGeometry.PinCount;
        if (totalPins < 1)
            throw new InvalidOperationException(
                $"TotalPins resolved to {totalPins} from hex geometry × elements.");
        double Q_pin = reactorThermalPower_W / totalPins;
        double r_pin = 0.5 * hexGeometry.PinDiameter_mm * 1e-3;       // [m]
        double V_pin = Math.PI * r_pin * r_pin * fuelPinLength_m;
        double qVol  = Q_pin / V_pin;                                  // q''' [W/m³]

        // ── 2. Coolant exit temperature from energy balance. ────────────────
        // Single-pass energy balance: P = ṁ · cp · ΔT. cp is gentle over the
        // NERVA H₂ range, so use cp at the mean temperature.
        double cp_inlet = LH2ThermalProperties.Cp_J_kgK(coolantInletTemp_K);
        double T_exit_guess = coolantInletTemp_K
            + reactorThermalPower_W / (coolantMassFlow_kgs * cp_inlet);
        for (int iter = 0; iter < 8; iter++)
        {
            double T_mean    = 0.5 * (coolantInletTemp_K + T_exit_guess);
            double cp_mean   = LH2ThermalProperties.Cp_J_kgK(T_mean);
            double T_exit_new = coolantInletTemp_K
                + reactorThermalPower_W / (coolantMassFlow_kgs * cp_mean);
            if (Math.Abs(T_exit_new - T_exit_guess) < 0.5) { T_exit_guess = T_exit_new; break; }
            T_exit_guess = T_exit_new;
        }
        double T_cool_exit = T_exit_guess;

        // ── 3. Surface heat flux (with hot-channel factor). ─────────────────
        double q_surf = F_hc * Q_pin / (Math.PI * (hexGeometry.PinDiameter_mm * 1e-3) * fuelPinLength_m);

        // ── 4. Coolant-side HTC (Dittus-Boelter). ──────────────────────────
        // Per-pin mass-flow rate ṁ_pin = ṁ_total / totalPins; subchannel
        // mass-flux G = ṁ_pin / A_subchannel.
        double mDot_pin = coolantMassFlow_kgs / totalPins;
        double A_sub_m2 = SubChannelFlowArea_m2(hexGeometry);
        double G        = mDot_pin / Math.Max(A_sub_m2, 1e-12);          // [kg/(m²·s)]
        double T_film   = 0.5 * (T_cool_exit + (T_cool_exit + 200.0)); // crude film T anchor
        double mu       = LH2ThermalProperties.Viscosity_PaS(T_film);
        double k        = LH2ThermalProperties.Conductivity_WmK(T_film);
        double Pr       = LH2ThermalProperties.Prandtl(T_film);
        double D_h_m    = hexGeometry.ChannelHydraulicDiameter_mm * 1e-3;
        double Re       = G * D_h_m / Math.Max(mu, 1e-12);
        double Nu       = Re > 1e3
            ? 0.023 * Math.Pow(Re, 0.8) * Math.Pow(Pr, 0.4)
            : 4.36;  // laminar limit
        double h_cool   = Nu * k / Math.Max(D_h_m, 1e-9);

        // ── 5. Pin surface + centreline temperatures. ──────────────────────
        // Sprint NU.W4: per-material thermal conductivity. None defaults to
        // UO₂-cermet to preserve Wave-2 caller behaviour bit-identically.
        double k_fuel = NuclearFuelMaterials.For(fuelMaterial).ThermalConductivity_WmK;
        double dT_wc = q_surf / Math.Max(h_cool, 1e-9);
        double dT_cs = qVol * r_pin * r_pin / (4.0 * k_fuel);
        double T_surf = T_cool_exit + dT_wc;
        double T_peak = T_surf + dT_cs;

        return new FuelPinHeatResult(
            PeakFuelCenterlineTemp_K:   T_peak,
            PinSurfaceTemp_K:           T_surf,
            CoolantExitTemp_K:          T_cool_exit,
            WallToCoolantDeltaT_K:      dT_wc,
            CenterlineToSurfaceDeltaT_K: dT_cs,
            HeatFluxSurface_W_m2:       q_surf,
            VolumetricHeatSource_W_m3:  qVol,
            PerPinPower_W:              Q_pin,
            ConvectiveHtc_W_m2K:        h_cool,
            HotChannelFactor:           F_hc);
    }

    /// <summary>
    /// Triangular sub-channel flow area in m² (3 pins of the hex array
    /// bounding one triangular sub-channel; pitch p, diameter d):
    ///   A = (√3/4) · p² − (π/8) · d²
    /// </summary>
    private static double SubChannelFlowArea_m2(HexArrayGeometryResult g)
    {
        double pm = g.PinPitch_mm * 1e-3;
        double dm = g.PinDiameter_mm * 1e-3;
        return (Math.Sqrt(3.0) / 4.0) * pm * pm
             - (Math.PI / 8.0) * dm * dm;
    }
}
