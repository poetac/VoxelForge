// TurbopropFixture_T56A15.cs — Allison T56-A-15 turboprop validation fixture.
//
// The T56-A-15 powers the Lockheed C-130 Hercules. This fixture validates
// the turboprop cycle solver against open-literature cruise performance data.
//
// Design point: cruise at 17,000 ft (5182 m), Mach 0.58.
//
// Published reference values (open literature):
//   Shaft power:  ≈ 3660 kW (≈ 4910 shp) — Jane's All the World's Aircraft /
//                 Allison Gas Turbine publications. The 4910 "shp" figure refers
//                 to shaft horsepower at the gearbox output (4910 × 745.7 W =
//                 3661 kW, consistent with the 3660 kW figure).
//   π_c:          ≈ 9.25 (compressor pressure ratio, T56 specification)
//   ṁ_a:          ≈ 14 kg/s (inlet air mass flow, estimated from engine sizing)
//   T_t4:         ≈ 1350 K (turbine inlet temperature, T56 open-literature estimate)
//   Net thrust:   > 10 kN at cruise (propeller + residual jet combined)
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Turboprop variant. ADR-036 § Air-breathing pillar does NOT
// carry a dedicated turboprop row — this fixture covers an ADR-036 GAP.
// Bands inherit from the gas-turbine / turboshaft cluster regime (±25 %
// performance, ±15 % station state). Per-quantity rationale:
//   Shaft power: ±25 % — the Jones-style constant-cp model with parametric
//     stand-in compressor/turbine maps introduces ~15-20 % systematic error
//     vs real maps; the ±25 % band accommodates inlet sizing uncertainty as
//     well. The primary metric: solver produces the right order of magnitude.
//   Net thrust: > 10 kN lower bound — the T56-A-15 at cruise produces
//     roughly 15-20 kN net (propeller dominant); 10 kN is a conservative floor.
//   Feasibility: design must pass all hard gates at the stated design point.
//
// Source: Jane's All the World's Aircraft (various editions), Allison Engine
//   Company T56 specification sheets (open-literature, declassified).

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Validation;

/// <summary>
/// Calibration fixture for the Allison T56-A-15 turboprop.
/// Validates turboprop cycle solver shaft power and net thrust at the
/// C-130 cruise design point (17,000 ft, Mach 0.58, Jet-A fuel).
/// </summary>
public sealed class TurbopropFixture_T56A15
{
    // ── Published reference values ────────────────────────────────────────────
    // Allison T56-A-15 at cruise (17,000 ft / 5182 m, Mach 0.58).
    // Source: Jane's All the World's Aircraft (multiple editions),
    //         Allison Engine Company public specifications.

    private const double Published_ShaftPower_W = 3_660_000.0;   // 3660 kW (≈ 4910 shp)
    private const double MinNetThrust_N         = 10_000.0;       // ≥ 10 kN combined thrust
    private const double Tolerance_ShaftPower   = 0.25;           // ±25 % (constant-cp model)

    // ── Design parameters ─────────────────────────────────────────────────────
    // Inlet area sized for ṁ_a ≈ 14 kg/s at M_face=0.5, 5182 m standard atm.
    // π_c = 9.25 (T56 published), φ = 0.30 → T_t4 ≈ 1350 K.
    // PropellerPowerExtraction_frac = 0.89 (Mattingly §8, T56-class).
    //
    // Note: inlet area 0.055 m² gives ṁ ≈ 14 kg/s at 5182 m / Mach 0.5 face.
    // (ρ_5182m ≈ 0.736 kg/m³, a ≈ 321 m/s, M_face=0.5, V_face≈160 m/s →
    //  ṁ ≈ 0.736 × 160 × 0.055 ≈ 6.5 kg/s; actual T56 ṁ ≈ 14 kg/s suggests
    //  larger effective area — inlet capture area on the nacelle is distinct
    //  from the compressor face area. Use 0.115 m² for compressor face to
    //  match the mass-flow parameterisation of the existing solver stand-in.)

    private static readonly AirbreathingEngineDesign T56A15Design = new(
        Kind:                    AirbreathingEngineKind.Turboprop,
        InletThroatArea_m2:      0.115,    // compressor-face proxy for ṁ_a sizing
        CombustorArea_m2:        0.10,
        CombustorLength_m:       0.35,
        NozzleThroatArea_m2:     0.055,
        NozzleExitArea_m2:       0.070,
        EquivalenceRatio:        0.30,     // φ = 0.30 → T_t4 ≈ 1350 K at π_c=9.25
        CompressorPressureRatio: 9.25)
    {
        PropellerPowerExtraction_frac = 0.89,   // T56-class FPT fraction
    };

    // Cruise conditions: 17,000 ft ≈ 5182 m, Mach 0.58, Jet-A fuel
    private static readonly FlightConditions T56A15Cruise = new(
        Altitude_m:  5182.0,
        MachNumber:  0.58,
        Fuel:        AirbreathingFuel.JetA);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void T56A15_Cruise_IsFeasible()
    {
        var result = AirbreathingOptimization.GenerateWith(T56A15Design, T56A15Cruise);
        Assert.True(result.IsFeasible,
            "T56-A-15 fixture should be feasible at cruise design point; "
          + "hard violations: "
          + string.Join(", ", System.Linq.Enumerable.Select(
                result.Violations, v => v.ConstraintId)));
    }

    [Fact]
    public void T56A15_Cruise_ShaftPower_WithinTolerance()
    {
        // Published: ≈ 3660 kW shaft power at cruise.
        // Tolerance: ±25 % (open-literature preliminary-design model).
        var result = AirbreathingOptimization.GenerateWith(T56A15Design, T56A15Cruise);
        AssertWithinFraction(
            "ShaftPower_W",
            Published_ShaftPower_W,
            result.ShaftPower_W,
            Tolerance_ShaftPower);
    }

    [Fact]
    public void T56A15_Cruise_NetThrust_AboveMinimum()
    {
        // T56-A-15 at cruise delivers ≥ 10 kN combined (propeller + residual jet).
        // The propeller is dominant; the residual nozzle contributes ~3-8 %.
        var result = AirbreathingOptimization.GenerateWith(T56A15Design, T56A15Cruise);
        Assert.True(result.Stations.ThrustNet_N >= MinNetThrust_N,
            $"T56-A-15 net thrust {result.Stations.ThrustNet_N:F0} N is below the "
          + $"minimum expected {MinNetThrust_N:F0} N at cruise.");
    }

    [Fact]
    public void T56A15_Cruise_ShaftPower_IsPositive()
    {
        var result = AirbreathingOptimization.GenerateWith(T56A15Design, T56A15Cruise);
        Assert.True(result.ShaftPower_W > 0.0,
            $"ShaftPower_W = {result.ShaftPower_W:F0} W should be > 0 for turboprop.");
    }

    [Fact]
    public void T56A15_Cruise_TurbineInletT_WithinPublishedBand()
    {
        // Published T_t4 ≈ 1350 K for T56-A-15 mil-power.
        // ±20 % tolerance for parametric stand-in model.
        var result = AirbreathingOptimization.GenerateWith(T56A15Design, T56A15Cruise);
        double T_t4 = result.Stations.Station(4).StagnationT_K;
        AssertWithinFraction("Station4_T_t4_K", 1350.0, T_t4, 0.20);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static void AssertWithinFraction(
        string propertyName, double expected, double actual, double fraction)
    {
        double relError = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(relError <= fraction,
            $"{propertyName}: actual={actual:G5}, expected={expected:G5}, "
          + $"relative error={relError:P1} exceeds tolerance ±{fraction:P0}. "
          + $"Source: Jane's / Allison T56-A-15 open-literature specifications.");
    }
}
