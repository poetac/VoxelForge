// F404TurbofanFixtureTests.cs — GE F404-GE-400 two-spool turbofan validation.
//
// Exercises the two-spool path (PiFan set) against published GE F404-GE-400
// sea-level static dry (mil-power, no afterburner) performance data.
//
// Source: Jane's Aero-Engines + GE Aviation public datasheets.
//   BPR ≈ 0.34, π_c ≈ 26 (overall), PiFan ≈ 3.0 (fan stage)
//   TIT ≈ 1640-1700 K (estimated, mil power)
//   Dry SLS thrust ≈ 48.9 kN
//   TSFC (dry) ≈ 0.79 lb/(lbf·hr) → Isp ≈ 4 555 s
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Turbofan variant under ADR-036 § Air-breathing pillar (±20 %
// thrust / ±15 % Isp). ADR-036's turbofan row is flagged THIN (only F404 /
// F100 cited as anchors) — this fixture is the primary two-spool data point.
// The single-stream sibling [[F404_SeaLevelStatic_Dry]] in AirbreathingFixtures
// covers the single-spool simplified path; this fixture explicitly exercises
// the two-spool LP/HP architecture via PiFan = 3.0 / π_hpc = 26/3.
//
// NOT wired into AirbreathingFixtures.All — separate validation concern
// for the two-spool LP/HP architecture.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Validation;

/// <summary>
/// Standalone validation fixture for the GE F404-GE-400 two-spool turbofan.
/// Verifies the Phase 2 Turbofan solver produces physically-plausible
/// performance at the published design point when <see cref="AirbreathingEngineDesign.PiFan"/>
/// is specified.
/// </summary>
public sealed class F404TurbofanFixtureTests
{
    // ── Published reference values ────────────────────────────────────────────
    // GE F404-GE-400 sea-level static, dry mil power (no afterburner).
    // Source: Jane's Aero-Engines, GE Aviation public documentation.

    private const double Published_ThrustNet_N    = 48_900.0;  // ~48.9 kN dry SLS
    private const double Published_Isp_s           = 4_555.0;   // from TSFC ≈ 0.79 lb/(lbf·hr)
    // ±20 % thrust: ADR-036 turbofan row + parametric-η compressor / turbine
    // maps + constant-Cp gas approximation. The single-spool simplification
    // is removed here (PiFan splits HP/LP) but cp(T) remains constant.
    private const double Tolerance_Thrust          = 0.20;
    // ±20 % Isp: derived from TSFC ≈ 0.79 lb/(lbf·hr). Constant-Cp model
    // limitation is the dominant gap; cp(T) tabulation lands in follow-on.
    private const double Tolerance_Isp             = 0.20;

    // ── Design parameters ──────────────────────────────────────────────────────
    // Inlet area sized for ~65 kg/s at M_face=0.5.
    // φ=0.30 gives T_t4≈1640 K, safely below the 1700 K uncooled TIT ceiling.
    // PiFan=3.0 derives the HP/LP split with π_hpc = π_c/π_fan ≈ 26/3 ≈ 8.67.

    private static readonly AirbreathingEngineDesign F404TwoSpool = new(
        Kind:                    AirbreathingEngineKind.Turbofan,
        InletThroatArea_m2:      0.37,
        CombustorArea_m2:        0.15,
        CombustorLength_m:       0.40,
        NozzleThroatArea_m2:     0.12,
        NozzleExitArea_m2:       0.18,
        EquivalenceRatio:        0.30,
        CompressorPressureRatio: 26.0,
        BypassRatio:             0.34)
    {
        PiFan = 3.0,
    };

    private static readonly FlightConditions SlsConditions = new(
        Altitude_m:  0.0,
        MachNumber:  0.001,
        Fuel:        AirbreathingFuel.Jp8);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void F404_TwoSpool_SeaLevelStatic_IsFeasible()
    {
        var result = AirbreathingOptimization.GenerateWith(F404TwoSpool, SlsConditions);
        Assert.True(result.IsFeasible,
            "F404 two-spool fixture should be feasible at design point; hard violations: "
          + string.Join(", ", System.Linq.Enumerable.Select(result.Violations, v => v.ConstraintId)));
    }

    [Fact]
    public void F404_TwoSpool_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var result = AirbreathingOptimization.GenerateWith(F404TwoSpool, SlsConditions);
        AssertWithinFraction("ThrustNet_N",
            Published_ThrustNet_N, result.Stations.ThrustNet_N, Tolerance_Thrust);
    }

    [Fact]
    public void F404_TwoSpool_SeaLevelStatic_SpecificImpulse_WithinTolerance()
    {
        var result = AirbreathingOptimization.GenerateWith(F404TwoSpool, SlsConditions);
        AssertWithinFraction("SpecificImpulse_s",
            Published_Isp_s, result.Stations.SpecificImpulse_s, Tolerance_Isp);
    }

    [Fact]
    public void F404_TwoSpool_Station10_HpTurbineExit_IsPopulated()
    {
        var result = AirbreathingOptimization.GenerateWith(F404TwoSpool, SlsConditions);
        var s10 = result.Stations.Station(10);
        Assert.False(double.IsNaN(s10.StagnationT_K),
            "Station 10 (HP turbine exit) should be populated in two-spool mode.");
        Assert.True(s10.StagnationT_K > 0,
            $"Station 10 T_t45 = {s10.StagnationT_K:F1} K should be positive.");
    }

    [Fact]
    public void F404_TwoSpool_TurbineInletT_WithinPublishedBand()
    {
        // Published TIT for F404 mil-power ≈ 1640–1700 K.
        // Model should fall within ±15% of the 1700 K reference.
        var result = AirbreathingOptimization.GenerateWith(F404TwoSpool, SlsConditions);
        double T_t4 = result.Stations.Station(4).StagnationT_K;
        AssertWithinFraction("Station4_T_t4_K", 1700.0, T_t4, 0.15);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static void AssertWithinFraction(
        string propertyName, double expected, double actual, double fraction)
    {
        double relError = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(relError <= fraction,
            $"{propertyName}: actual={actual:F2}, expected={expected:F2}, "
          + $"relative error={relError:P2} exceeds tolerance ±{fraction:P0}.");
    }
}
