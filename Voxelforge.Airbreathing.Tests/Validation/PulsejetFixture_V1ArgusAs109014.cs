// PulsejetFixture_V1ArgusAs109014.cs — V-1 / Argus As 109-014 published-
// engine validation fixture (sub-step 1a.5 polish, issue #415).
//
// Reference engine: Argus As 109-014 valved pulsejet, as fitted to the
// Fieseler Fi 103 (V-1) flying bomb. Sea-level static test conditions per
// NACA RM E50A04 (Cleveland-instrumented tests).
//
// Published design-point (sea-level static):
//   Thrust        ≈ 3 000 N   (some sources 2 700–3 300 N depending on fuel
//                              grade and ambient temperature)
//   Buzz frequency ≈ 47 Hz   (as-measured; Helmholtz lumped model gives
//                              ~21 Hz — 55 % gap documented below)
//   Isp            ≈ 2 700 s  (air-breathing convention, F/(ṁ_fuel·g₀))
//   SFC            ≈ 3.78 × 10⁻⁵ kg/(N·s) (derived from Isp)
//
// Design parameters match AirbreathingFixtures.FockeWulfV1_Pulsejet exactly
// so both fixture approaches exercise the same design point.
//
// Calibration note — pulse-rate test:
//   HalfWavePipeAcousticCalculator with V-1 geometry and cycle-solver T_t4
//   uses c_eff = √(c_cold · c_hot) ≈ 612 m/s → f_QW ≈ 45 Hz vs measured
//   47 Hz (4.3 % gap). The quarter-wave mode dominates because the tube-
//   length ratio r = L/√(V/A) ≈ 2.4 > QuarterWaveDominanceRatio = 2.0
//   (Foa §11.3). Tolerance tightened ±20 % → ±10 % with this estimator.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Pulsejet variant under ADR-036 § Air-breathing pillar (±25 %
// thrust / ±20 % Isp default). Tightened thrust band to ±20 % per ADR-036
// D3.2 — solver η_vol = 0.14 is calibrated to exactly this V-1 reference
// point (NACA RM E50A04 Cleveland tests). Pulse-rate band tightened to ±10 %
// after HalfWavePipeAcousticCalculator switched to c_eff = √(c_cold · c_hot)
// quarter-wave estimator (matches V-1 measured 47 Hz within 4.3 %).
// Per-band rationale on each constant below.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Atmosphere;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Thermo;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Validation;

/// <summary>
/// Published-engine validation for the Argus As 109-014 pulsejet (V-1 buzz bomb).
/// Sea-level static, JP-8 fuel.
/// </summary>
public sealed class PulsejetFixture_V1ArgusAs109014
{
    // ── Reference values ──────────────────────────────────────────────────────
    // Source: NACA RM E50A04 (Cleveland static tests) + Foa 1960 §11.3.

    private const double Published_ThrustNet_N  = 3_000.0;
    private const double Published_BuzzFreq_Hz  = 47.0;
    private const double Published_Isp_s        = 2_700.0;

    // SFC = 1 / (Isp · g₀) in kg/(N·s)
    private static readonly double Published_Sfc_kg_N_s =
        1.0 / (Published_Isp_s * StandardAtmosphere.G0_m_s2);

    // ±20 % thrust: solver η_vol = 0.14 calibrated to the V-1 sea-level
    // static reference point; ADR-036 pulsejet default ±25 % tightened
    // per D3.2 by the explicit calibration.
    private const double Tolerance_Thrust    = 0.20;
    // ±30 % SFC: compounds thrust + fuel-flow band; widened from ±20 %
    // because the SFC computation propagates both uncertainties.
    private const double Tolerance_Sfc       = 0.30;
    // ±10 % pulse-rate: quarter-wave dominant mode (tube-length ratio
    // r ≈ 2.4 > 2.0 per Foa 1960 §11.3); c_eff = √(c_cold · c_hot)
    // estimator gives f_QW ≈ 45 Hz vs measured 47 Hz (4.3 % gap).
    private const double Tolerance_PulseRate = 0.10;

    // ── Design / conditions ───────────────────────────────────────────────────
    // Identical to AirbreathingFixtures.FockeWulfV1_Pulsejet (lines 991-1003).

    private static AirbreathingEngineDesign V1Design() =>
        new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:      0.030,
            CombustorArea_m2:        0.075,
            CombustorLength_m:       0.80,
            NozzleThroatArea_m2:     0.025,
            NozzleExitArea_m2:       0.040,
            EquivalenceRatio:        0.95,
            CompressorPressureRatio: 1.0)
        {
            PulsejetTubeLength_m    = 3.40,
            PulsejetIntakeArea_m2   = 0.030,
            PulsejetTailpipeArea_m2 = 0.040,
        };

    private static readonly FlightConditions SlsJp8 =
        new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void V1_Thrust_WithinTwentyPercent()
    {
        var result = AirbreathingOptimization.GenerateWith(V1Design(), SlsJp8);
        AssertWithinFraction(
            "ThrustNet_N",
            Published_ThrustNet_N,
            result.Stations.ThrustNet_N,
            Tolerance_Thrust);
    }

    /// <summary>
    /// Pulse-rate validation using the buzz frequency surfaced on
    /// <see cref="AirbreathingResult.EstimatedBuzzFrequency_Hz"/> (issue #451).
    /// The V-1 (Standard reed-valve) tube-length ratio r ≈ 2.4 > 2.0, so the
    /// closed-open quarter-wave mode dominates: c_eff = √(c_cold · c_hot) ≈ 612 m/s
    /// → f_QW ≈ 45 Hz vs published 47 Hz (4.3 % gap, within ±10 %).
    /// </summary>
    [Fact]
    public void V1_PulseRate_WithinTwentyPercent()
    {
        var result = AirbreathingOptimization.GenerateWith(V1Design(), SlsJp8);
        double f_Hz = result.EstimatedBuzzFrequency_Hz;

        Assert.False(double.IsNaN(f_Hz), "EstimatedBuzzFrequency_Hz must be populated for Pulsejet kind.");
        Assert.InRange(f_Hz, 1.0, 500.0);
        AssertWithinFraction("PulseRate_Hz", Published_BuzzFreq_Hz, f_Hz, Tolerance_PulseRate);
    }

    [Fact]
    public void V1_SFC_WithinBand()
    {
        // SFC (specific fuel consumption) = ṁ_fuel / F_net, in kg/(N·s).
        // Derived from published Isp ≈ 2 700 s: SFC = 1/(Isp·g₀) ≈ 3.78 × 10⁻⁵.
        // Tolerance ±30 % because SFC compounds thrust-error and fuel-flow-error.
        var result = AirbreathingOptimization.GenerateWith(V1Design(), SlsJp8);
        double thrustNet = result.Stations.ThrustNet_N;
        double mdotFuel  = result.Stations.FuelMassFlow_kg_s;

        Assert.True(thrustNet > 0.0 && mdotFuel > 0.0,
            $"Non-positive thrust ({thrustNet:G4} N) or fuel flow ({mdotFuel:G4} kg/s); "
            + "SFC undefined.");

        double sfc = mdotFuel / thrustNet;
        AssertWithinFraction("SFC_kg_N_s", Published_Sfc_kg_N_s, sfc, Tolerance_Sfc);
    }

    [Fact]
    public void V1_FeasibleAt1AtmStatic()
    {
        // V-1 nominal design point must clear both pulsejet gates:
        //   PULSEJET_BLOWOUT_LEAN   (hard) — f > 0.030 at φ=0.95 / JP-8
        //   PULSEJET_ACOUSTIC_OVERPRESSURE (advisory) — P_peak/P_steady ≤ 1.30
        var result = AirbreathingOptimization.GenerateWith(V1Design(), SlsJp8);

        Assert.True(result.IsFeasible,
            "V-1 nominal design should be feasible at sea-level static; "
            + "hard violations: "
            + string.Join(", ", System.Linq.Enumerable.Select(result.Violations,
                v => v.ConstraintId)));

        Assert.Empty(result.Violations);
    }

    [Fact]
    public void V1_Deterministic()
    {
        // Two identical invocations must produce bit-identical output.
        var design = V1Design();
        var r1 = AirbreathingOptimization.GenerateWith(design, SlsJp8);
        var r2 = AirbreathingOptimization.GenerateWith(design, SlsJp8);

        Assert.Equal(r1.Stations.ThrustNet_N,        r2.Stations.ThrustNet_N);
        Assert.Equal(r1.Stations.SpecificImpulse_s,  r2.Stations.SpecificImpulse_s);
        Assert.Equal(r1.Stations.FuelMassFlow_kg_s,  r2.Stations.FuelMassFlow_kg_s);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static void AssertWithinFraction(
        string propertyName, double expected, double actual, double fraction)
    {
        double relError = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(relError <= fraction,
            $"{propertyName}: actual={actual:G6}, expected={expected:G6}, "
          + $"relative error={relError:P2} exceeds tolerance ±{fraction:P0}. "
          + $"Source: NACA RM E50A04 + Foa 1960 §11.3.");
    }
}
