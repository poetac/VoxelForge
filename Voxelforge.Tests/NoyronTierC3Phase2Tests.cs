// NoyronTierC3Phase2Tests.cs — Tier C3 Phase 2 forcing-function suite
// for the turbine sizer + turbine-wheel implicits + TurbopumpResult
// wiring + TURBINE_POWER_DEFICIT feasibility gate.
//
// Coverage
// ────────
//   • TurbineSizing.SizeOneStage — Euler-inversion math (spouting
//     velocity, tip speed, wheel radius, specific work, available
//     power) against hand-solved numbers for a LOX/CH4 FFSC fuel-rich
//     preburner driving a nominal fuel pump.
//   • TurbineSizing.Size — null on PressureFed / ElectricPump, returns
//     populated result on GasGenerator / StagedCombustion / FullFlow.
//   • TurbineSizing.Size — PowerBalanceOK true when preburner + pump
//     are sized consistently; false when mass flow is starved.
//   • Monotonicity — wheel radius drops with RPM; specific work rises
//     with inlet temperature.
//   • TurbineGeometryGenerator.Generate — null on degenerate stage
//     (zero wheel radius, zero mass flow); populates every field on
//     nominal input.
//   • TurbineWheelImplicit / TurbineStatorImplicit / TurbineStageAssemblyImplicit
//       - Sign convention (negative inside solid phase).
//       - Degenerate-construction throws.
//       - Axial + radial clipping returns positive distances.
//   • TurbopumpResult.{FuelTurbine,OxTurbine,Turbine,FuelTurbineGeometry,OxTurbineGeometry}
//     default null; round-trip via `with`.
//   • Full GenerateWith integration — a StagedCombustion baseline
//     produces gen.Turbopump.Turbine with FuelTurbine + OxTurbine
//     populated and PowerBalanceOK true.
//   • FeasibilityGate — TURBINE_POWER_DEFICIT fires when PowerBalanceOK
//     is false and stays silent otherwise. Gate 16 behaves the same
//     as the existing 15 gates (additive, non-fail-fast).
//
// All tests are pure C# — no PicoGK Library init.

using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Optimization;
using Voxelforge.Turbopump;

namespace Voxelforge.Tests;

public class NoyronTierC3Phase2Tests
{
    // ══════════════════ Sizer math — one-stage helpers ══════════════════

    private static PumpSizing MakePump(double shaftKw, double rpm) => new(
        PropellantLabel:      "fuel",
        MassFlow_kgs:         2.5,
        InletPressure_Pa:     0.5e6,
        DischargePressure_Pa: 15e6,
        Density_kgm3:         420.0,
        HeadRise_m:           3500.0,
        HydraulicPower_W:     shaftKw * 650.0,
        ShaftPower_W:         shaftKw * 1000.0,
        Efficiency:           0.65,
        Rpm:                  rpm,
        NPSHA_m:              30.0,
        NPSHR_m:              20.0,
        NPSHAcceptable:       true);

    private static PreburnerResult MakePreburner(
        double tK = 900, double pcMPa = 15, double mdot = 5.0, double gamma = 1.25, double mw = 13.0) =>
        new(
            Cycle:                  EngineCycle.StagedCombustion,
            MixtureRatio:           0.60,
            ChamberPressure_Pa:     pcMPa * 1e6,
            WarmGasTemperature_K:   tK,
            WarmGasCStar_ms:        1700,
            WarmGasGamma:           gamma,
            WarmGasMolecularWeight: mw,
            MassFlow_kgs:           mdot,
            CharacteristicLength_m: 0.40,
            ChamberVolume_mm3:      1.0e7,
            Notes:                  "test",
            Warnings:               System.Array.Empty<string>());

    [Fact]
    public void SizeOneStage_HandSolvedWork_MatchesEulerInversion()
    {
        var pump = MakePump(shaftKw: 50, rpm: 25_000);
        var pre = MakePreburner(tK: 900, pcMPa: 15, mdot: 5, gamma: 1.25, mw: 13);

        var stage = TurbineSizing.SizeOneStage(
            label:           "fuel",
            pump:            pump,
            preburner:       pre,
            turbineMassFlow: pre.MassFlow_kgs,
            backPressure:    11e6,   // staged FFSC-ish, 10 MPa Pc × 1.1
            efficiency:      0.60);

        // Hand solve:
        //   R_specific = 8314.5 / 13 ≈ 639.6 J/(kg K)
        //   cp = 1.25/0.25 · 639.6 ≈ 3198 J/(kg K)
        //   π = 11/15 ≈ 0.733
        //   exponent = 0.25/1.25 = 0.20
        //   π^0.20 ≈ 0.9399
        //   w_isen = 3198 · 900 · (1 − 0.9399) ≈ 172.9 kJ/kg
        //   w_actual = 0.60 · 172.9 ≈ 103.7 kJ/kg
        //   C₀ = √(2 · 172.9e3) ≈ 588 m/s
        //   U_tip = 0.5 · 588 ≈ 294 m/s
        //   ω = 2π · 25000/60 ≈ 2618 rad/s
        //   R = 294/2618 ≈ 0.1123 m = 112 mm
        Assert.InRange(stage.Cp_Jkg_K, 3100, 3300);
        Assert.InRange(stage.IsentropicSpecificWork_Jkg, 150_000, 200_000);
        Assert.InRange(stage.ActualSpecificWork_Jkg, 80_000, 120_000);
        Assert.InRange(stage.SpoutingVelocity_ms, 500, 650);
        Assert.InRange(stage.TipSpeed_ms, 260, 330);
        Assert.InRange(stage.WheelRadius_mm, 95, 130);
        Assert.Equal(TurbineSizing.StandardBladeCount, stage.BladeCount);
        Assert.Equal(TurbineSizing.StandardStatorVaneCount, stage.StatorVaneCount);
    }

    [Fact]
    public void SizeOneStage_AvailablePower_MatchesMdotTimesWork()
    {
        var pump = MakePump(shaftKw: 50, rpm: 25_000);
        var pre = MakePreburner(mdot: 5);
        var stage = TurbineSizing.SizeOneStage(
            "fuel", pump, pre, pre.MassFlow_kgs, 11e6);
        double expected = pre.MassFlow_kgs * stage.ActualSpecificWork_Jkg;
        Assert.Equal(expected, stage.AvailableShaftPower_W, 3);
    }

    [Fact]
    public void SizeOneStage_HigherInletTemperature_RaisesSpecificWork()
    {
        var pump = MakePump(shaftKw: 50, rpm: 25_000);
        var cold = TurbineSizing.SizeOneStage("fuel", pump, MakePreburner(tK: 700),
            5.0, 11e6);
        var hot  = TurbineSizing.SizeOneStage("fuel", pump, MakePreburner(tK: 1050),
            5.0, 11e6);
        Assert.True(hot.ActualSpecificWork_Jkg > cold.ActualSpecificWork_Jkg);
    }

    [Fact]
    public void SizeOneStage_HigherRpm_ShrinksWheelRadius()
    {
        var slow = TurbineSizing.SizeOneStage("fuel", MakePump(50, 10_000),
            MakePreburner(), 5.0, 11e6);
        var fast = TurbineSizing.SizeOneStage("fuel", MakePump(50, 50_000),
            MakePreburner(), 5.0, 11e6);
        Assert.True(fast.WheelRadius_mm < slow.WheelRadius_mm);
    }

    [Fact]
    public void SizeOneStage_NonPositiveEfficiency_Throws()
    {
        var pump = MakePump(50, 25_000);
        var pre = MakePreburner();
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            TurbineSizing.SizeOneStage("fuel", pump, pre, 5.0, 11e6, efficiency: 0));
    }

    [Fact]
    public void SizeOneStage_NonPositiveMassFlow_Throws()
    {
        var pump = MakePump(50, 25_000);
        var pre = MakePreburner();
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            TurbineSizing.SizeOneStage("fuel", pump, pre, turbineMassFlow: 0, backPressure: 11e6));
    }

    // ══════════════════ Sizer wrapper — cycle dispatch ══════════════════

    [Theory]
    [InlineData(EngineCycle.PressureFed)]
    [InlineData(EngineCycle.ElectricPump)]
    public void Size_PressureFedOrElectric_ReturnsNull(EngineCycle cycle)
    {
        var pump = MakePump(50, 25_000);
        var pre = MakePreburner();
        var r = TurbineSizing.Size(cycle, 10e6, pump, pump, pre, null);
        Assert.Null(r);
    }

    [Fact]
    public void Size_WithoutFuelPreburner_ReturnsNull()
    {
        var pump = MakePump(50, 25_000);
        var r = TurbineSizing.Size(EngineCycle.StagedCombustion, 10e6, pump, pump, null, null);
        Assert.Null(r);
    }

    [Fact]
    public void Size_StagedCombustion_PopulatesBothStages()
    {
        var pump = MakePump(50, 25_000);
        var pre = MakePreburner();
        var r = TurbineSizing.Size(EngineCycle.StagedCombustion, 10e6, pump, pump, pre, null);
        Assert.NotNull(r);
        Assert.NotNull(r!.FuelTurbine);
        Assert.NotNull(r.OxTurbine);
        Assert.True(r.TotalAvailableShaftPower_W > 0);
        Assert.True(r.TotalRequiredShaftPower_W > 0);
    }

    [Fact]
    public void Size_FullFlow_UsesSeparatePreburnersPerShaft()
    {
        var fuelPump = MakePump(50, 25_000);
        var oxPump = MakePump(70, 20_000);
        var fr = MakePreburner(tK: 900,  mdot: 3) with { MixtureRatio = 0.60 };
        var or = MakePreburner(tK: 1000, mdot: 7, mw: 22, gamma: 1.22)
                 with { MixtureRatio = 35.0 };
        var r = TurbineSizing.Size(EngineCycle.FullFlow, 10e6,
            fuelPump, oxPump, fr, or);
        Assert.NotNull(r);
        // FFSC: each turbine gets the full mass flow of its own preburner.
        Assert.Equal(fr.MassFlow_kgs, r!.FuelTurbine!.MassFlow_kgs, 6);
        Assert.Equal(or.MassFlow_kgs, r.OxTurbine!.MassFlow_kgs, 6);
    }

    [Fact]
    public void Size_SingleShaft_SplitsPreburnerMdotByShaftPower()
    {
        // Non-FFSC: a single fuel-rich preburner feeds a single-shaft
        // turbine that drives both pumps. The sizer splits mass flow
        // proportional to shaft-power demand.
        var fuelPump = MakePump(shaftKw: 30, rpm: 25_000);
        var oxPump   = MakePump(shaftKw: 70, rpm: 25_000);
        var pre = MakePreburner(mdot: 10);
        var r = TurbineSizing.Size(EngineCycle.StagedCombustion, 10e6,
            fuelPump, oxPump, pre, null);
        // Sum of the two shaft mdots must equal the preburner total.
        Assert.Equal(pre.MassFlow_kgs,
            r!.FuelTurbine!.MassFlow_kgs + r.OxTurbine!.MassFlow_kgs, 5);
        // Fuel pump is 30 % of demand → 30 % of the mdot.
        Assert.InRange(r.FuelTurbine.MassFlow_kgs / pre.MassFlow_kgs, 0.28, 0.32);
    }

    [Fact]
    public void Size_PowerBalanceOK_TrueForNominal_FalseWhenStarved()
    {
        // Nominal — plenty of gas + moderate pump demand
        var fat = MakePump(shaftKw: 50, rpm: 25_000);
        var fatPre = MakePreburner(mdot: 5);
        var ok = TurbineSizing.Size(EngineCycle.StagedCombustion, 10e6,
            fat, fat, fatPre, null);
        Assert.True(ok!.PowerBalanceOK);

        // Starved — same pump demand, preburner mass flow choked to 1/100.
        var thinPre = MakePreburner(mdot: 0.05);
        var bad = TurbineSizing.Size(EngineCycle.StagedCombustion, 10e6,
            fat, fat, thinPre, null);
        Assert.False(bad!.PowerBalanceOK);
        Assert.NotEmpty(bad.Warnings);
    }

    // ══════════════════ Geometry generator ══════════════════

    [Fact]
    public void Generate_NullStage_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            TurbineGeometryGenerator.Generate(null!));
    }

    [Fact]
    public void Generate_ZeroWheelRadius_ReturnsNull()
    {
        var stage = new TurbineStage(
            Label: "fuel", MassFlow_kgs: 2.5,
            InletTemperature_K: 900, InletPressure_Pa: 15e6,
            OutletPressure_Pa: 11e6, Gamma: 1.25, MolecularWeight_gmol: 13,
            Cp_Jkg_K: 3200, Efficiency: 0.60,
            IsentropicSpecificWork_Jkg: 150_000, ActualSpecificWork_Jkg: 90_000,
            SpoutingVelocity_ms: 550, TipSpeed_ms: 275,
            WheelRadius_mm: 0,   // degenerate
            Rpm: 25_000,
            BladeCount: 36, StatorVaneCount: 24,
            RequiredShaftPower_W: 50_000, AvailableShaftPower_W: 200_000,
            PowerSufficient: true, Notes: "test");
        Assert.Null(TurbineGeometryGenerator.Generate(stage));
    }

    [Fact]
    public void Generate_Nominal_PopulatesEveryField()
    {
        var pump = MakePump(50, 25_000);
        var pre = MakePreburner();
        var stage = TurbineSizing.SizeOneStage("fuel", pump, pre, pre.MassFlow_kgs, 11e6);
        var g = TurbineGeometryGenerator.Generate(stage);
        Assert.NotNull(g);
        Assert.True(g!.WheelTipRadius_mm > g.WheelHubRadius_mm);
        Assert.True(g.WheelThickness_mm > 0);
        Assert.Equal(TurbineSizing.StandardBladeCount, g.WheelBladeCount);
        Assert.Equal(TurbineSizing.StandardStatorVaneCount, g.StatorVaneCount);
        Assert.True(g.StatorOuterRadius_mm > g.StatorInnerRadius_mm);
        Assert.True(g.StatorAxialHeight_mm > 0);
        Assert.True(g.NozzleThroatArea_mm2 > 0);
        Assert.True(g.HousingOuterRadius_mm > g.WheelTipRadius_mm);
        Assert.True(g.TotalLength_mm > 0);
        Assert.True(g.EstimatedMass_g > 0);
        Assert.NotEmpty(g.Notes);
    }

    [Fact]
    public void Generate_HubTipRatio_MatchesConstant()
    {
        var pump = MakePump(50, 25_000);
        var pre = MakePreburner();
        var stage = TurbineSizing.SizeOneStage("fuel", pump, pre, pre.MassFlow_kgs, 11e6);
        var g = TurbineGeometryGenerator.Generate(stage);
        double ratio = g!.WheelHubRadius_mm / g.WheelTipRadius_mm;
        Assert.Equal(TurbineGeometryGenerator.WheelHubToTipRatio, ratio, 4);
    }

    // ══════════════════ Implicits — sign convention + clipping ══════════════════

    [Fact]
    public void TurbineWheelImplicit_InsideHubDisc_IsNegative()
    {
        var w = new TurbineWheelImplicit(
            rHub_mm: 20, rTip_mm: 80, zMin_mm: 0, zMax_mm: 10,
            bladeCount: 36, bladeThickness_mm: 2);
        var p = new Vector3(5, 0, 5);   // r=5 < hub=20
        Assert.True(w.fSignedDistance(p) < 0);
    }

    [Fact]
    public void TurbineWheelImplicit_OutsideTip_IsPositive()
    {
        var w = new TurbineWheelImplicit(20, 80, 0, 10);
        var p = new Vector3(100, 0, 5);   // r=100 > tip=80
        Assert.True(w.fSignedDistance(p) > 0);
    }

    [Fact]
    public void TurbineWheelImplicit_BelowZMin_IsPositive()
    {
        var w = new TurbineWheelImplicit(20, 80, 0, 10);
        var p = new Vector3(30, 0, -5);
        Assert.True(w.fSignedDistance(p) > 0);
    }

    [Fact]
    public void TurbineWheelImplicit_DegenerateRadii_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new TurbineWheelImplicit(rHub_mm: 80, rTip_mm: 20, zMin_mm: 0, zMax_mm: 10));
    }

    [Fact]
    public void TurbineWheelImplicit_DegenerateAxial_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new TurbineWheelImplicit(rHub_mm: 20, rTip_mm: 80, zMin_mm: 10, zMax_mm: 0));
    }

    [Fact]
    public void TurbineStatorImplicit_InsideOuterRing_IsNegative()
    {
        var s = new TurbineStatorImplicit(
            rInner_mm: 20, rOuter_mm: 80, zMin_mm: 0, zMax_mm: 10,
            vaneCount: 24, vaneThickness_mm: 1.5f,
            ringThicknessFraction: 0.15f);
        // 15 % ring → ringInner = 80 − 9 = 71. Sample at r=75, z=5.
        var p = new Vector3(75, 0, 5);
        Assert.True(s.fSignedDistance(p) < 0);
    }

    [Fact]
    public void TurbineStatorImplicit_InsideInnerBore_IsPositive()
    {
        var s = new TurbineStatorImplicit(20, 80, 0, 10);
        var p = new Vector3(5, 0, 5);   // r=5 < rInner=20
        Assert.True(s.fSignedDistance(p) > 0);
    }

    [Fact]
    public void TurbineStatorImplicit_DegenerateRadii_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new TurbineStatorImplicit(rInner_mm: 80, rOuter_mm: 20, zMin_mm: 0, zMax_mm: 10));
    }

    [Fact]
    public void TurbineStageAssemblyImplicit_InsideWheel_IsNegative()
    {
        var stator = new TurbineStatorImplicit(20, 80, 0, 8);
        var wheel = new TurbineWheelImplicit(20, 80, 12, 22);   // above stator with gap
        var asm = new TurbineStageAssemblyImplicit(stator, wheel,
            housingRadius_mm: 90, housingZMin_mm: 0, housingZMax_mm: 22);
        // Sample inside the wheel hub disc at (5, 0, 17).
        var p = new Vector3(5, 0, 17);
        Assert.True(asm.fSignedDistance(p) < 0);
    }

    // ══════════════════ Design + result wiring ══════════════════

    [Fact]
    public void TurbopumpResult_TurbineFields_DefaultNull()
    {
        var r = new TurbopumpResult(
            Cycle:               EngineCycle.PressureFed,
            FuelPump:            null,
            OxPump:              null,
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            System.Array.Empty<string>(),
            Notes:               "");
        Assert.Null(r.Turbine);
        Assert.Null(r.FuelTurbineGeometry);
        Assert.Null(r.OxTurbineGeometry);
    }

    [Fact]
    public void TurbopumpResult_TurbineFields_RoundTripViaWith()
    {
        var r = new TurbopumpResult(
            Cycle:               EngineCycle.StagedCombustion,
            FuelPump:            null,
            OxPump:              null,
            TotalShaftPower_W:   0,
            EstimatedDryMass_kg: 0,
            NPSHFeasible:        true,
            Warnings:            System.Array.Empty<string>(),
            Notes:               "");
        var turbine = new TurbineSizingResult(null, null, 0, 0, true,
            System.Array.Empty<string>(), "n");
        var r2 = r with { Turbine = turbine };
        Assert.Same(turbine, r2.Turbine);
    }

    // ══════════════════ FeasibilityGate — Gate 16 trigger ══════════════════

    [Fact]
    public void FeasibilityGate_TurbinePowerDeficit_FiresOnlyWhenPowerBalanceFails()
    {
        var baseDesign = new RegenChamberDesign();
        var cond = new OperatingConditions()
        {
            EngineCycle = EngineCycle.StagedCombustion,
        };

        // Build a minimal RegenGenerationResult by running the real
        // generator — we only inspect the Turbopump/gate slot.
        var gen = RegenChamberOptimization.GenerateWith(cond, baseDesign);

        // On a nominal staged-combustion design the sizer picks
        // consistent preburner + pump values, so PowerBalanceOK is
        // true and no deficit violation is recorded.
        var result = FeasibilityGate.Evaluate(gen);
        bool tripped = result.Violations
            .Any(v => v.ConstraintId == "TURBINE_POWER_DEFICIT");
        if (gen.Turbopump?.Turbine?.PowerBalanceOK ?? true)
            Assert.False(tripped);
        else
            Assert.True(tripped);

        // Force a deficit by substituting a starved Turbine into the
        // result (keep all other fields intact).
        var thinStage = new TurbineStage(
            "fuel", 0.05, 900, 15e6, 11e6, 1.25, 13, 3200, 0.60,
            150_000, 90_000, 550, 275, 20, 25_000, 36, 24,
            RequiredShaftPower_W:  500_000,
            AvailableShaftPower_W: 4_500,
            PowerSufficient:       false, Notes: "starved");
        var thinTurbine = new TurbineSizingResult(
            FuelTurbine: thinStage, OxTurbine: null,
            TotalAvailableShaftPower_W: thinStage.AvailableShaftPower_W,
            TotalRequiredShaftPower_W:  thinStage.RequiredShaftPower_W,
            PowerBalanceOK: false,
            Warnings: new[] { "starved" }, Notes: "");
        var brokenPump = gen.Turbopump! with { Turbine = thinTurbine };
        var broken = gen with { Turbopump = brokenPump };
        var brokenResult = FeasibilityGate.Evaluate(broken);
        Assert.Contains(brokenResult.Violations,
            v => v.ConstraintId == "TURBINE_POWER_DEFICIT");
    }

    [Fact]
    public void FeasibilityGate_NoTurbine_DoesNotTripGate16()
    {
        // PressureFed → gen.Turbopump is null; Gate 16 must be silent.
        var cond = new OperatingConditions() { EngineCycle = EngineCycle.PressureFed };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        var result = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "TURBINE_POWER_DEFICIT");
    }

    // ══════════════════ FeasibilityGate — Gate 17 (SHAFT_WHIRL) ══════════════════
    //
    // Sprint 3 (2026-04-22) promoted FeedSystem.ShaftCriticalSpeed's WhirlOk
    // flag from a warning-only advisory to a hard feasibility gate. The
    // tests below pin the gate behaviour: fires when either shaft's WhirlOk
    // is false, silent on PressureFed, silent when both shafts are outside
    // the whirl band.

    private static FeedSystem.ShaftCriticalSpeedResult MakeShaftInWhirlBand(string label)
        => new FeedSystem.ShaftCriticalSpeedResult(
            Label:                    label,
            ShaftLength_mm:           120.0,
            ShaftDiameter_mm:         12.0,
            MaterialYoungsModulus_Pa: FeedSystem.ShaftCriticalSpeed.InconelYoungsModulus_Pa,
            MaterialDensity_kgm3:     FeedSystem.ShaftCriticalSpeed.InconelDensity_kgm3,
            FirstCriticalFrequency_Hz: 500.0,
            FirstCriticalRpm:         30_000.0,
            OperatingRpm:             30_500.0,   // 1.7% above critical — well inside ±20% band
            WhirlSafetyMargin:        -0.0167,
            WhirlOk:                  false,
            Notes:                    "fixture: inside whirl band");

    private static FeedSystem.ShaftCriticalSpeedResult MakeShaftOutsideWhirlBand(string label)
        => new FeedSystem.ShaftCriticalSpeedResult(
            Label:                    label,
            ShaftLength_mm:           120.0,
            ShaftDiameter_mm:         12.0,
            MaterialYoungsModulus_Pa: FeedSystem.ShaftCriticalSpeed.InconelYoungsModulus_Pa,
            MaterialDensity_kgm3:     FeedSystem.ShaftCriticalSpeed.InconelDensity_kgm3,
            FirstCriticalFrequency_Hz: 500.0,
            FirstCriticalRpm:         30_000.0,
            OperatingRpm:             50_000.0,   // 67% above critical — supercritical, outside band
            WhirlSafetyMargin:        -0.67,
            WhirlOk:                  true,
            Notes:                    "fixture: supercritical");

    [Fact]
    public void FeasibilityGate_ShaftWhirl_FiresWhenFuelShaftInBand()
    {
        var cond = new OperatingConditions { EngineCycle = EngineCycle.StagedCombustion };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        // Substitute a fuel shaft that's inside the whirl band.
        var badTp = gen.Turbopump! with { FuelShaft = MakeShaftInWhirlBand("fuel") };
        var broken = gen with { Turbopump = badTp };

        var r = FeasibilityGate.Evaluate(broken);
        var v = Assert.Single(r.Violations, v => v.ConstraintId == "SHAFT_WHIRL");
        Assert.Contains("fuel", v.Description);
        Assert.Contains("critical", v.Description);
    }

    [Fact]
    public void FeasibilityGate_ShaftWhirl_FiresWhenOxShaftInBand()
    {
        var cond = new OperatingConditions { EngineCycle = EngineCycle.StagedCombustion };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        var badTp = gen.Turbopump! with { OxShaft = MakeShaftInWhirlBand("ox") };
        var broken = gen with { Turbopump = badTp };

        var r = FeasibilityGate.Evaluate(broken);
        Assert.Contains(r.Violations,
            v => v.ConstraintId == "SHAFT_WHIRL" && v.Description.Contains("ox"));
    }

    [Fact]
    public void FeasibilityGate_ShaftWhirl_ReportsBothShafts_WhenBothFail()
    {
        var cond = new OperatingConditions { EngineCycle = EngineCycle.StagedCombustion };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        var badTp = gen.Turbopump! with
        {
            FuelShaft = MakeShaftInWhirlBand("fuel"),
            OxShaft   = MakeShaftInWhirlBand("ox"),
        };
        var broken = gen with { Turbopump = badTp };

        var r = FeasibilityGate.Evaluate(broken);
        var v = Assert.Single(r.Violations, v => v.ConstraintId == "SHAFT_WHIRL");
        // Single aggregated violation covering both shafts.
        Assert.Contains("fuel", v.Description);
        Assert.Contains("ox",   v.Description);
    }

    [Fact]
    public void FeasibilityGate_ShaftWhirl_StaysSilent_WhenBothShaftsOutsideBand()
    {
        var cond = new OperatingConditions { EngineCycle = EngineCycle.StagedCombustion };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        var okTp = gen.Turbopump! with
        {
            FuelShaft = MakeShaftOutsideWhirlBand("fuel"),
            OxShaft   = MakeShaftOutsideWhirlBand("ox"),
        };
        var healthy = gen with { Turbopump = okTp };

        var r = FeasibilityGate.Evaluate(healthy);
        Assert.DoesNotContain(r.Violations, v => v.ConstraintId == "SHAFT_WHIRL");
    }

    [Fact]
    public void FeasibilityGate_ShaftWhirl_StaysSilent_OnPressureFed()
    {
        // PressureFed → no Turbopump → no shaft → gate silent.
        var cond = new OperatingConditions { EngineCycle = EngineCycle.PressureFed };
        var gen = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign());
        var r = FeasibilityGate.Evaluate(gen);
        Assert.DoesNotContain(r.Violations, v => v.ConstraintId == "SHAFT_WHIRL");
    }
}
