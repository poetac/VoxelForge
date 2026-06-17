// AirbreathingLaceFeasibilityTests.cs — Sprint A.W3 LACE gate tests.
// Covers 4 hard + 2 advisory LACE gates and cross-kind isolation.

using System.Linq;
using Voxelforge.Airbreathing;
using Voxelforge.Optimization;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingLaceFeasibilityTests
{
    private static AirbreathingEngineDesign BaselineLace() => new(
        Kind: AirbreathingEngineKind.LiquidAirCycle,
        InletThroatArea_m2:  0.50,
        CombustorArea_m2:    0.30,
        CombustorLength_m:   0.50,
        NozzleThroatArea_m2: 0.05,
        NozzleExitArea_m2:   1.50,
        EquivalenceRatio:    0.0)
    {
        PrecoolerEffectiveness  = 0.95,
        LH2MassFlow_kgs         = 4.0,
        LaceChamberPressure_bar = 70.0,
        LaceAirToFuelRatio      = 8.0,
    };

    private static FlightConditions LaceConditions()
        => new(Altitude_m: 25_000.0, MachNumber: 5.0, Fuel: AirbreathingFuel.H2);

    private static bool Has(FeasibilityViolation[] v, string id)
        => v.Any(x => x.ConstraintId == id);

    private static bool Has(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    // ── Baseline ────────────────────────────────────────────────────────

    [Fact]
    public void Baseline_LaceRb545LikeDesign_IsFeasible()
    {
        var r = AirbreathingOptimization.GenerateWith(BaselineLace(), LaceConditions());
        Assert.True(r.IsFeasible,
            $"RB-545-style LACE baseline should pass; saw violations: "
          + string.Join(", ", r.Violations.Select(v => v.ConstraintId)));
    }

    // ── LACE_PRECOOLER_EFFECTIVENESS_LOW (hard) ─────────────────────────

    [Fact]
    public void PrecoolerEffectivenessLow_BelowFloor_FiresHardGate()
    {
        // ε=0.50 — well below 0.70 hard floor.
        var bad = BaselineLace() with { PrecoolerEffectiveness = 0.50 };
        var r = AirbreathingOptimization.GenerateWith(bad, LaceConditions());
        Assert.True(Has(r.Violations, "LACE_PRECOOLER_EFFECTIVENESS_LOW"));
    }

    // ── LACE_AIR_LIQUEFACTION_INSUFFICIENT (hard) ───────────────────────

    [Fact]
    public void AirLiquefactionInsufficient_LowEffectiveness_FiresHardGate()
    {
        // ε=0.72: passes hard ε floor but T_out at Mach-5 inlet stays
        // > 95 K target. Both ε and liquefaction gates may fire — we
        // just check the liquefaction one.
        var bad = BaselineLace() with { PrecoolerEffectiveness = 0.72 };
        var r = AirbreathingOptimization.GenerateWith(bad, LaceConditions());
        Assert.True(Has(r.Violations, "LACE_AIR_LIQUEFACTION_INSUFFICIENT"));
    }

    // ── LACE_AIR_TO_FUEL_OUT_OF_BAND (hard) ─────────────────────────────

    [Fact]
    public void AirToFuelOutOfBand_TooRich_FiresHardGate()
    {
        var bad = BaselineLace() with { LaceAirToFuelRatio = 1.0 };  // < 2.0
        var r = AirbreathingOptimization.GenerateWith(bad, LaceConditions());
        Assert.True(Has(r.Violations, "LACE_AIR_TO_FUEL_OUT_OF_BAND"));
    }

    [Fact]
    public void AirToFuelOutOfBand_TooLean_FiresHardGate()
    {
        var bad = BaselineLace() with { LaceAirToFuelRatio = 100.0 };  // > 50.0
        var r = AirbreathingOptimization.GenerateWith(bad, LaceConditions());
        Assert.True(Has(r.Violations, "LACE_AIR_TO_FUEL_OUT_OF_BAND"));
    }

    // ── LACE_CHAMBER_PRESSURE_OUT_OF_BAND (hard) ────────────────────────

    [Fact]
    public void ChamberPressureOutOfBand_TooLow_FiresHardGate()
    {
        var bad = BaselineLace() with { LaceChamberPressure_bar = 10.0 };  // < 20
        var r = AirbreathingOptimization.GenerateWith(bad, LaceConditions());
        Assert.True(Has(r.Violations, "LACE_CHAMBER_PRESSURE_OUT_OF_BAND"));
    }

    [Fact]
    public void ChamberPressureOutOfBand_TooHigh_FiresHardGate()
    {
        var bad = BaselineLace() with { LaceChamberPressure_bar = 300.0 };  // > 250
        var r = AirbreathingOptimization.GenerateWith(bad, LaceConditions());
        Assert.True(Has(r.Violations, "LACE_CHAMBER_PRESSURE_OUT_OF_BAND"));
    }

    // ── LACE_AIR_TO_FUEL_OUT_OF_ADVISORY (advisory) ─────────────────────

    [Fact]
    public void AirToFuelOutOfAdvisory_AboveSweetSpot_FiresAdvisory()
    {
        // MR=40 passes hard band [2, 50] but exits cluster sweet spot [5, 35].
        var advisory = BaselineLace() with { LaceAirToFuelRatio = 40.0 };
        var r = AirbreathingOptimization.GenerateWith(advisory, LaceConditions());
        Assert.True(Has(r.Advisories, "LACE_AIR_TO_FUEL_OUT_OF_ADVISORY"));
    }

    // ── LACE_PRECOOLER_FROST_LINE_RISK (advisory) ───────────────────────

    [Fact]
    public void PrecoolerFrostLineRisk_OutletInFrostBand_FiresAdvisory()
    {
        // ε that puts T_out in [95, 220] K range. At Mach 5, T_t1 ≈ 1300 K.
        // For T_out = 150 K: ε = (1300 - 150) / (1300 - 25) = 0.902.
        // But that ALSO satisfies liquefaction (we want T_out > 95 K AND <
        // 220 K for the frost gate to fire). 150 K > 95 K so liquefaction
        // gate fires too — accept either-or here.
        // Actually let me pick T_out = 150 K which means ε ≈ 0.902 — and
        // both LACE_AIR_LIQUEFACTION_INSUFFICIENT and LACE_PRECOOLER_FROST_LINE_RISK
        // can fire. Just check the advisory fires.
        var advisory = BaselineLace() with { PrecoolerEffectiveness = 0.902 };
        var r = AirbreathingOptimization.GenerateWith(advisory, LaceConditions());
        Assert.True(Has(r.Advisories, "LACE_PRECOOLER_FROST_LINE_RISK"));
    }

    // ── Cross-kind isolation ────────────────────────────────────────────

    [Fact]
    public void LaceGates_DoNotFire_OnRamjetDesign()
    {
        var ram = new AirbreathingEngineDesign(
            Kind: AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:  0.10,
            CombustorArea_m2:    0.30,
            CombustorLength_m:   0.50,
            NozzleThroatArea_m2: 0.0848,
            NozzleExitArea_m2:   0.20,
            EquivalenceRatio:    0.40);
        var cond = new FlightConditions(Altitude_m: 12_000.0, MachNumber: 2.0, Fuel: AirbreathingFuel.H2);

        var r = AirbreathingOptimization.GenerateWith(ram, cond);
        Assert.False(Has(r.Violations, "LACE_PRECOOLER_EFFECTIVENESS_LOW"));
        Assert.False(Has(r.Violations, "LACE_AIR_LIQUEFACTION_INSUFFICIENT"));
        Assert.False(Has(r.Violations, "LACE_AIR_TO_FUEL_OUT_OF_BAND"));
        Assert.False(Has(r.Violations, "LACE_CHAMBER_PRESSURE_OUT_OF_BAND"));
        Assert.False(Has(r.Advisories, "LACE_AIR_TO_FUEL_OUT_OF_ADVISORY"));
        Assert.False(Has(r.Advisories, "LACE_PRECOOLER_FROST_LINE_RISK"));
    }
}
