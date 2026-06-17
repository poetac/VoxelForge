// NoyronV460Tests.cs — Tier C3 polish forcing-function suite for the
// shaft bending critical speed advisory.
//
// Coverage
// ────────
//   • ShaftCriticalSpeed.Estimate — null inputs (missing pump or
//     turbine geometry) return null; degenerate dimensions return null;
//     nominal inputs populate every field.
//   • First-natural-frequency math — closed-form check against hand-
//     solved Euler-Bernoulli fixed-fixed eigenvalue for a uniform
//     circular cross-section shaft.
//   • Whirl-band semantics — `WhirlOk == true` well outside ±20 %;
//     `WhirlOk == false` inside ±20 % on both subcritical + supercritical
//     sides; margin sign matches operating point.
//   • Monotonicity — thinner shaft lowers RPM_crit; longer shaft lowers
//     RPM_crit.
//   • ShaftCriticalSpeed.FormatWarning — null on whirl-OK inputs;
//     populated string mentioning "critical" + margin sign + label on
//     band-violating inputs.
//   • TurbopumpResult.FuelShaft / .OxShaft — default null; round-trip
//     via `with { }`.
//   • GenerateWith integration — StagedCombustion with
//     IncludeTurbopumpGeometry = true populates both FuelShaft and
//     OxShaft; warnings list includes the shaft entry whenever either
//     side lands in the band.
//   • GenerateWith NO-geometry gate — IncludeTurbopumpGeometry = false
//     leaves FuelShaft / OxShaft null (shaft estimation requires the
//     geometry records).
//   • Constants sanity — E, ρ, β₁L, WhirlBandHalfWidth, ShaftDiameterFraction
//     match file-level documentation.
//
// Pure-math tests; no PicoGK Library init.

using System;
using System.Linq;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Voxelforge.Turbopump;
using Xunit;

namespace Voxelforge.Tests;

public class NoyronV460ShaftCriticalSpeedTests
{
    private static TurbopumpGeometry MakePumpGeom(
        double impHub = 12, double impTip = 30, double totalLen = 80) => new(
        ImpellerHubRadius_mm:      impHub,
        ImpellerTipRadius_mm:      impTip,
        ImpellerThickness_mm:      impTip * 0.25,
        ImpellerBladeCount:        8,
        InducerHubRadius_mm:       impHub * 0.4,
        InducerTipRadius_mm:       impHub * 1.10,
        InducerLength_mm:          impTip,
        InducerBladeCount:         3,
        VoluteMinorRadiusStart_mm: impTip * 0.18,
        VoluteMinorRadiusEnd_mm:   impTip * 0.45,
        CasingOuterRadius_mm:      impTip * 1.8,
        CasingLength_mm:           totalLen * 0.6,
        TotalLength_mm:            totalLen,
        EstimatedMass_g:           800,
        Notes:                     "test-pump");

    private static TurbineGeometry MakeTurbineGeom(
        double wheelHub = 14, double wheelTip = 25, double totalLen = 35) => new(
        WheelHubRadius_mm:    wheelHub,
        WheelTipRadius_mm:    wheelTip,
        WheelThickness_mm:    wheelTip * 0.20,
        WheelBladeCount:      36,
        StatorInnerRadius_mm: wheelHub,
        StatorOuterRadius_mm: wheelTip,
        StatorAxialHeight_mm: wheelTip * 0.35,
        StatorVaneCount:      24,
        NozzleThroatArea_mm2: 60.0,
        HousingOuterRadius_mm:wheelTip + 5,
        TotalLength_mm:       totalLen,
        EstimatedMass_g:      500,
        Notes:                "test-turbine");

    // ══════════════════ null / degenerate inputs ══════════════════

    [Fact]
    public void Estimate_NullPump_ReturnsNull()
    {
        Assert.Null(ShaftCriticalSpeed.Estimate("fuel", null, MakeTurbineGeom(), 25_000));
    }

    [Fact]
    public void Estimate_NullTurbine_ReturnsNull()
    {
        Assert.Null(ShaftCriticalSpeed.Estimate("fuel", MakePumpGeom(), null, 25_000));
    }

    [Fact]
    public void Estimate_ZeroRpm_ReturnsNull()
    {
        Assert.Null(ShaftCriticalSpeed.Estimate(
            "fuel", MakePumpGeom(), MakeTurbineGeom(), operatingRpm: 0));
    }

    [Fact]
    public void Estimate_ZeroHubRadius_ReturnsNull()
    {
        var degeneratePump = MakePumpGeom(impHub: 0);
        Assert.Null(ShaftCriticalSpeed.Estimate(
            "fuel", degeneratePump, MakeTurbineGeom(), 25_000));
    }

    [Fact]
    public void Estimate_ZeroPumpLength_ReturnsNull()
    {
        var degeneratePump = MakePumpGeom(totalLen: 0);
        Assert.Null(ShaftCriticalSpeed.Estimate(
            "fuel", degeneratePump, MakeTurbineGeom(), 25_000));
    }

    // ══════════════════ populated fields ══════════════════

    [Fact]
    public void Estimate_PopulatesAllFields()
    {
        var r = ShaftCriticalSpeed.Estimate(
            "fuel", MakePumpGeom(), MakeTurbineGeom(), operatingRpm: 25_000);
        Assert.NotNull(r);
        Assert.Equal("fuel", r!.Label);
        Assert.True(r.ShaftLength_mm > 0);
        Assert.True(r.ShaftDiameter_mm > 0);
        Assert.Equal(ShaftCriticalSpeed.InconelYoungsModulus_Pa, r.MaterialYoungsModulus_Pa);
        Assert.Equal(ShaftCriticalSpeed.InconelDensity_kgm3, r.MaterialDensity_kgm3);
        Assert.True(r.FirstCriticalFrequency_Hz > 0);
        Assert.True(r.FirstCriticalRpm > 0);
        Assert.Equal(25_000, r.OperatingRpm);
    }

    [Fact]
    public void Estimate_ShaftLengthIsPumpPlusBearingPlusTurbine()
    {
        var pump = MakePumpGeom(totalLen: 80);
        var turb = MakeTurbineGeom(totalLen: 35);
        var r = ShaftCriticalSpeed.Estimate("fuel", pump, turb, 25_000);
        Assert.Equal(80 + ShaftCriticalSpeed.BearingMargin_mm + 35, r!.ShaftLength_mm, 5);
    }

    [Fact]
    public void Estimate_ShaftDiameterIsFractionOfMinHubDiameter()
    {
        // impHub=12 mm → 24 mm diameter; wheelHub=14 mm → 28 mm diameter.
        // Min hub diameter = 24 mm → shaft = 0.70 × 24 = 16.8 mm.
        var r = ShaftCriticalSpeed.Estimate(
            "fuel",
            MakePumpGeom(impHub: 12),
            MakeTurbineGeom(wheelHub: 14),
            25_000);
        double expected = ShaftCriticalSpeed.ShaftDiameterFraction * 2.0 * 12.0;
        Assert.Equal(expected, r!.ShaftDiameter_mm, 6);
    }

    // ══════════════════ closed-form first-frequency check ═════════

    [Fact]
    public void Estimate_FirstCriticalFrequency_MatchesEulerBernoulliEigenvalue()
    {
        // Hand-solve for pump totalLen=80, turb totalLen=35, impHub=12,
        // wheelHub=14: L = 80 + 20 + 35 = 135 mm = 0.135 m.
        // d = 0.70 × 2 × 12 = 16.8 mm = 0.0168 m.
        // E·d²/(16ρ) = 200e9 × 0.0168² / (16 × 8190) = 431.05 m²/s²
        // √(...) = 20.77 m/s ...wait, that's not right dimensionally.
        // Actually √(E·d²/(16ρ)) has units of m²·(m/s) — let's redo.
        // E [Pa = N/m² = kg·m⁻¹·s⁻²]
        // d² [m²]
        // ρ [kg/m³]
        // E·d² / ρ → (kg·m⁻¹·s⁻²)·(m²)/(kg/m³) = m⁴/s² → √ = m²/s
        // So √(E·d²/(16·ρ)) has units m²/s ✓
        // ω = (4.73/L)² · √(...) → (1/m²)·(m²/s) = 1/s ✓
        //
        // Numbers:
        //   4.73 / 0.135 = 35.04 1/m
        //   (4.73/L)² = 1227.5 1/m²
        //   E·d²/(16·ρ) = 200e9 × (0.0168)² / (16 × 8190)
        //               = 200e9 × 2.8224e-4 / 131040
        //               = 5.6448e7 / 131040 = 430.76 m²/s²... hmm.
        //   Actually: 2.8224e-4 × 200e9 = 5.6448e7
        //             5.6448e7 / 131040 = 430.76
        //   √430.76 = 20.76 m²/s (units as above)
        //   ω_n = 1227.5 × 20.76 = 25480 rad/s
        //   f_n = 25480 / (2π) = 4055 Hz
        //   RPM_crit = 60 × 4055 = 243,300 rpm
        //
        // That's a very high number, which reflects the stiff shaft
        // relative to its short length — exactly right for a small
        // turbopump.
        var r = ShaftCriticalSpeed.Estimate(
            "fuel",
            MakePumpGeom(impHub: 12, impTip: 30, totalLen: 80),
            MakeTurbineGeom(wheelHub: 14, wheelTip: 25, totalLen: 35),
            25_000);

        double L_m = 0.135;
        double d_m = 0.70 * 2.0 * 12.0 * 1e-3;
        double expectedOmega = Math.Pow(ShaftCriticalSpeed.FixedFixedBeta1_L / L_m, 2)
                             * Math.Sqrt(ShaftCriticalSpeed.InconelYoungsModulus_Pa * d_m * d_m
                                       / (16.0 * ShaftCriticalSpeed.InconelDensity_kgm3));
        double expectedRpm = 60.0 * expectedOmega / (2.0 * Math.PI);
        Assert.InRange(r!.FirstCriticalRpm / expectedRpm, 0.99, 1.01);
    }

    // ══════════════════ whirl-band semantics ══════════════════

    [Fact]
    public void Estimate_WhirlOk_True_WellBelowCritical()
    {
        // Pick a stiff, short shaft so RPM_crit is high; a modest
        // operating RPM (25 k) sits well below.
        var r = ShaftCriticalSpeed.Estimate(
            "fuel", MakePumpGeom(), MakeTurbineGeom(), operatingRpm: 25_000);
        Assert.NotNull(r);
        Assert.True(r!.FirstCriticalRpm > 0);
        if (r.FirstCriticalRpm > 25_000 * 1.25)
        {
            Assert.True(r.WhirlOk);
            Assert.True(r.WhirlSafetyMargin > 0);
        }
    }

    [Fact]
    public void Estimate_WhirlOk_False_InSubcriticalBand()
    {
        // Contrive a long, thin shaft (low RPM_crit); pick operating
        // RPM inside ±20 % below. We'll compute RPM_crit first then
        // pick op = 0.90 × crit.
        var pump = MakePumpGeom(impHub: 4, impTip: 10, totalLen: 200);
        var turb = MakeTurbineGeom(wheelHub: 4, wheelTip: 10, totalLen: 60);
        var seed = ShaftCriticalSpeed.Estimate("fuel", pump, turb, operatingRpm: 1);
        Assert.NotNull(seed);
        double opRpm = 0.90 * seed!.FirstCriticalRpm;

        var r = ShaftCriticalSpeed.Estimate("fuel", pump, turb, opRpm);
        Assert.NotNull(r);
        Assert.False(r!.WhirlOk);
        Assert.InRange(r.WhirlSafetyMargin, 0.0, 0.20);    // subcritical but in-band
    }

    [Fact]
    public void Estimate_WhirlOk_False_InSupercriticalBand()
    {
        // Same long-thin shaft, operating RPM = 1.15 × crit → supercritical
        // within ±20 % (margin = -15 %).
        var pump = MakePumpGeom(impHub: 4, impTip: 10, totalLen: 200);
        var turb = MakeTurbineGeom(wheelHub: 4, wheelTip: 10, totalLen: 60);
        var seed = ShaftCriticalSpeed.Estimate("fuel", pump, turb, operatingRpm: 1);
        double opRpm = 1.15 * seed!.FirstCriticalRpm;

        var r = ShaftCriticalSpeed.Estimate("fuel", pump, turb, opRpm);
        Assert.False(r!.WhirlOk);
        Assert.InRange(r.WhirlSafetyMargin, -0.20, 0.0);   // supercritical but in-band
    }

    [Fact]
    public void Estimate_WhirlOk_True_WellAboveCritical()
    {
        var pump = MakePumpGeom(impHub: 4, impTip: 10, totalLen: 200);
        var turb = MakeTurbineGeom(wheelHub: 4, wheelTip: 10, totalLen: 60);
        var seed = ShaftCriticalSpeed.Estimate("fuel", pump, turb, operatingRpm: 1);
        double opRpm = 1.50 * seed!.FirstCriticalRpm;

        var r = ShaftCriticalSpeed.Estimate("fuel", pump, turb, opRpm);
        Assert.True(r!.WhirlOk);
        Assert.True(r.WhirlSafetyMargin < -0.20);
    }

    [Fact]
    public void Estimate_NotesContainSubcriticalLabelWhenBelowCritical()
    {
        var r = ShaftCriticalSpeed.Estimate(
            "fuel", MakePumpGeom(), MakeTurbineGeom(), operatingRpm: 10_000);
        Assert.Contains("subcritical", r!.Notes);
    }

    // ══════════════════ monotonicity ══════════════════

    [Fact]
    public void Estimate_ThinnerShaft_LowersCriticalRpm()
    {
        var thick = ShaftCriticalSpeed.Estimate(
            "fuel",
            MakePumpGeom(impHub: 20, impTip: 30),   // thicker hub ⇒ thicker shaft
            MakeTurbineGeom(wheelHub: 20, wheelTip: 30),
            25_000);
        var thin = ShaftCriticalSpeed.Estimate(
            "fuel",
            MakePumpGeom(impHub: 6, impTip: 30),
            MakeTurbineGeom(wheelHub: 6, wheelTip: 30),
            25_000);
        Assert.True(thin!.FirstCriticalRpm < thick!.FirstCriticalRpm);
    }

    [Fact]
    public void Estimate_LongerShaft_LowersCriticalRpm()
    {
        var shortShaft = ShaftCriticalSpeed.Estimate(
            "fuel",
            MakePumpGeom(totalLen: 40),
            MakeTurbineGeom(totalLen: 20),
            25_000);
        var longShaft = ShaftCriticalSpeed.Estimate(
            "fuel",
            MakePumpGeom(totalLen: 200),
            MakeTurbineGeom(totalLen: 80),
            25_000);
        Assert.True(longShaft!.FirstCriticalRpm < shortShaft!.FirstCriticalRpm);
    }

    // ══════════════════ FormatWarning ══════════════════

    [Fact]
    public void FormatWarning_NullOnWhirlOkResult()
    {
        var r = ShaftCriticalSpeed.Estimate(
            "fuel", MakePumpGeom(), MakeTurbineGeom(), operatingRpm: 25_000);
        if (r!.WhirlOk)
            Assert.Null(ShaftCriticalSpeed.FormatWarning(r));
    }

    [Fact]
    public void FormatWarning_PopulatesOnBandHit()
    {
        var pump = MakePumpGeom(impHub: 4, impTip: 10, totalLen: 200);
        var turb = MakeTurbineGeom(wheelHub: 4, wheelTip: 10, totalLen: 60);
        var seed = ShaftCriticalSpeed.Estimate("fuel", pump, turb, operatingRpm: 1);
        double opRpm = 0.92 * seed!.FirstCriticalRpm;
        var r = ShaftCriticalSpeed.Estimate("fuel", pump, turb, opRpm);

        Assert.False(r!.WhirlOk);
        var w = ShaftCriticalSpeed.FormatWarning(r);
        Assert.NotNull(w);
        Assert.Contains("fuel", w!);
        Assert.Contains("critical", w);
        Assert.Contains("whirl band", w);
    }

    [Fact]
    public void FormatWarning_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ShaftCriticalSpeed.FormatWarning(null!));
    }

    // ══════════════════ TurbopumpResult integration ══════════════════

    [Fact]
    public void TurbopumpResult_ShaftFields_DefaultNull()
    {
        var r = new TurbopumpResult(
            Cycle:               EngineCycle.PressureFed,
            FuelPump:            null,
            OxPump:              null,
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            Array.Empty<string>(),
            Notes:               "");
        Assert.Null(r.FuelShaft);
        Assert.Null(r.OxShaft);
    }

    [Fact]
    public void TurbopumpResult_ShaftFields_RoundTripViaWith()
    {
        var r = new TurbopumpResult(
            Cycle:               EngineCycle.StagedCombustion,
            FuelPump:            null,
            OxPump:              null,
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            Array.Empty<string>(),
            Notes:               "");
        var shaft = new ShaftCriticalSpeedResult(
            "fuel", 120, 16, 200e9, 8190, 3000, 180_000, 25_000, 0.86, true, "n");
        var r2 = r with { FuelShaft = shaft };
        Assert.Same(shaft, r2.FuelShaft);
    }

    // ══════════════════ End-to-end GenerateWith ══════════════════

    [Fact]
    public void GenerateWith_StagedCombustion_WithGeometry_PopulatesShafts()
    {
        var cond = new OperatingConditions() with
        {
            EngineCycle               = EngineCycle.StagedCombustion,
            IncludeTurbopumpGeometry  = true,
        };
        var gen = RegenChamberOptimization.GenerateWith(
            cond, new RegenChamberDesign(),
            turbopumpGenerator: new Voxelforge.Turbopump.TurbopumpGeneratorAdapter(),
            turbineGenerator:   new Voxelforge.Turbopump.TurbineGeneratorAdapter());
        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelShaft);
        Assert.NotNull(gen.Turbopump.OxShaft);
        Assert.True(gen.Turbopump.FuelShaft!.FirstCriticalRpm > 0);
    }

    [Fact]
    public void GenerateWith_StagedCombustion_NoGeometry_LeavesShaftsNull()
    {
        // Without IncludeTurbopumpGeometry, no pump/turbine geometry is
        // generated → shaft estimator has nothing to work with.
        var cond = new OperatingConditions() with
        {
            EngineCycle              = EngineCycle.StagedCombustion,
            IncludeTurbopumpGeometry = false,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        Assert.NotNull(gen.Turbopump);
        Assert.Null(gen.Turbopump!.FuelShaft);
        Assert.Null(gen.Turbopump.OxShaft);
    }

    [Fact]
    public void GenerateWith_PressureFed_LeavesShaftsNull()
    {
        // Pressure-fed has no turbopump at all → no shaft to size. Either
        // Turbopump is null outright, or (if a future cycle variant does
        // emit a degenerate Turbopump record) both shafts must stay null.
        var cond = new OperatingConditions() with
        {
            EngineCycle              = EngineCycle.PressureFed,
            IncludeTurbopumpGeometry = true,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        if (gen.Turbopump is not null)
        {
            Assert.Null(gen.Turbopump.FuelShaft);
            Assert.Null(gen.Turbopump.OxShaft);
        }
    }

    // ══════════════════ constants sanity ══════════════════

    [Fact]
    public void Constants_MatchDocumentedValues()
    {
        Assert.Equal(200.0e9, ShaftCriticalSpeed.InconelYoungsModulus_Pa);
        Assert.Equal(8190.0, ShaftCriticalSpeed.InconelDensity_kgm3);
        Assert.Equal(4.73004, ShaftCriticalSpeed.FixedFixedBeta1_L, 5);
        Assert.Equal(0.20, ShaftCriticalSpeed.WhirlBandHalfWidth);
        Assert.Equal(0.70, ShaftCriticalSpeed.ShaftDiameterFraction);
        Assert.Equal(20.0, ShaftCriticalSpeed.BearingMargin_mm);
    }

    [Fact]
    public void GenerateWith_StagedCombustionWithGeometry_WarningsListStableRegardlessOfShaftBand()
    {
        var cond = new OperatingConditions() with
        {
            EngineCycle              = EngineCycle.StagedCombustion,
            IncludeTurbopumpGeometry = true,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        // Warnings are a string[]; shaft warnings only appear when the
        // band is violated. Just assert the invariant that the presence
        // of a shaft-warning string matches WhirlOk==false on either side.
        bool anyShaftBandHit =
            (gen.Turbopump?.FuelShaft is { WhirlOk: false })
            || (gen.Turbopump?.OxShaft is { WhirlOk: false });
        bool anyShaftWarning = gen.Turbopump!.Warnings.Any(w => w.Contains("shaft bending critical"));
        Assert.Equal(anyShaftBandHit, anyShaftWarning);
    }
}
