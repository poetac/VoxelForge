// NoyronV459Tests.cs — polish-sprint forcing-function suite. Covers
// three additions:
//
//   (A) Turbine-wheel envelope fields on `MonolithicBodyEnvelopes` +
//       evaluator handling: tubes that clip the housing cylinder on the
//       negative-Z half of the pump origin now flag
//       MONOLITHIC_BODY_INTERSECTION with body label "fuel turbine" /
//       "ox turbine"; clear tubes still pass.
//
//   (B) Tube-vs-tube intersection check in
//       `MonolithicFeasibility.Evaluate`: two tubes that cross in
//       space (no shared endpoint) emit MONOLITHIC_TUBE_INTERSECTION;
//       tubes that share a branch-joint endpoint are whitelisted;
//       well-separated tubes stay feasible.
//
//   (C) Rim-stress advisory in `TurbineSizing.SizeOneStage`:
//       `TurbineStage.RimStress_Pa`, `.RimStressAllowable_Pa`, `.RimStressOk`
//       are populated; `TurbineSizing.Size` emits an advisory warning
//       when the rim stress exceeds Inconel-718 yield ÷ safety factor 2.
//       Advisory only — PowerBalanceOK and FeasibilityGate unchanged.
//
// Pure-math tests; no PicoGK Library init.

using System.Linq;
using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Turbopump;
using Xunit;

namespace Voxelforge.Tests;

// ═══════════════════════════════════════════════════════════════════
// (A) turbine-wheel envelope on the monolithic body-intersection gate
// ═══════════════════════════════════════════════════════════════════

public class NoyronV459TurbineEnvelopeTests
{
    private static TurbineGeometry MakeTurbine(
        double rTip = 25, double rHousing = 30, double totalLen = 30) => new(
            WheelHubRadius_mm:    rTip * 0.55,
            WheelTipRadius_mm:    rTip,
            WheelThickness_mm:    rTip * 0.20,
            WheelBladeCount:      36,
            StatorInnerRadius_mm: rTip * 0.55,
            StatorOuterRadius_mm: rTip,
            StatorAxialHeight_mm: rTip * 0.35,
            StatorVaneCount:      24,
            NozzleThroatArea_mm2: 60.0,
            HousingOuterRadius_mm:rHousing,
            TotalLength_mm:       totalLen,
            EstimatedMass_g:      500,
            Notes:                "test");

    [Fact]
    public void Envelopes_DefaultTurbineFields_AreNull()
    {
        var env = new MonolithicBodyEnvelopes(
            100, 400, null, Vector3.Zero, null, Vector3.Zero, null, Vector3.Zero);
        Assert.Null(env.FuelTurbineGeometry);
        Assert.Null(env.OxTurbineGeometry);
    }

    [Fact]
    public void Evaluate_TubeThroughFuelTurbineBody_ReportsViolation()
    {
        // Fuel pump origin at world (50, 80, -40); turbine total length
        // 30 mm, so the turbine envelope sits at z ∈ [−70, −40] around
        // (50, 80). A tube running across (50, 80, −55) clips it.
        var pumpOrigin = new Vector3(50, 80, -40);
        var turbine = MakeTurbine(rHousing: 30, totalLen: 30);

        var tube = new FeedTube(
            Label:          "bad-turbine-tube",
            Start_mm:       new Vector3(50, 280, -55),
            Corner_mm:      null,
            End_mm:         new Vector3(50, -280, -55),
            OuterRadius_mm: 6.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.StagedCombustion, new[] { tube }, 0, 0, "");

        var env = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 40,    ChamberLength_mm: 400,
            FuelPumpGeometry:      null,  FuelPumpOrigin:   pumpOrigin,
            OxPumpGeometry:        null,  OxPumpOrigin:     Vector3.Zero,
            PreburnerGeometry:     null,  PreburnerOrigin:  Vector3.Zero,
            FuelTurbineGeometry:   turbine,
            OxTurbineGeometry:     null);

        var gate = MonolithicFeasibility.Evaluate(layout, env);

        Assert.False(gate.IsFeasible);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "MONOLITHIC_BODY_INTERSECTION"
              && v.Description.Contains("fuel turbine"));
    }

    [Fact]
    public void Evaluate_TubeThroughOxTurbineBody_ReportsViolation()
    {
        var oxPumpOrigin = new Vector3(50, -80, -40);
        var turbine = MakeTurbine(rHousing: 30, totalLen: 30);

        var tube = new FeedTube(
            Label:          "bad-ox-turbine-tube",
            Start_mm:       new Vector3(50, 200, -55),
            Corner_mm:      null,
            End_mm:         new Vector3(50, -200, -55),
            OuterRadius_mm: 6.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.FullFlow, new[] { tube }, 0, 0, "");

        var env = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 40,    ChamberLength_mm: 400,
            FuelPumpGeometry:      null,  FuelPumpOrigin:   Vector3.Zero,
            OxPumpGeometry:        null,  OxPumpOrigin:     oxPumpOrigin,
            PreburnerGeometry:     null,  PreburnerOrigin:  Vector3.Zero,
            FuelTurbineGeometry:   null,
            OxTurbineGeometry:     turbine);

        var gate = MonolithicFeasibility.Evaluate(layout, env);

        Assert.False(gate.IsFeasible);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "MONOLITHIC_BODY_INTERSECTION"
              && v.Description.Contains("ox turbine"));
    }

    [Fact]
    public void Evaluate_TubeClearOfTurbineEnvelope_IsFeasible()
    {
        // Turbine at (50, 80, −40) with z-extent [−70, −40]; tube far
        // below at z=−200 never clips it.
        var pumpOrigin = new Vector3(50, 80, -40);
        var turbine = MakeTurbine(rHousing: 30, totalLen: 30);

        var tube = new FeedTube(
            Label:          "clear-tube",
            Start_mm:       new Vector3(-200, 300, -200),
            Corner_mm:      null,
            End_mm:         new Vector3(-200, 500, -200),
            OuterRadius_mm: 6.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.StagedCombustion, new[] { tube }, 0, 0, "");

        var env = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 40,    ChamberLength_mm: 400,
            FuelPumpGeometry:      null,  FuelPumpOrigin:   pumpOrigin,
            OxPumpGeometry:        null,  OxPumpOrigin:     Vector3.Zero,
            PreburnerGeometry:     null,  PreburnerOrigin:  Vector3.Zero,
            FuelTurbineGeometry:   turbine,
            OxTurbineGeometry:     null);

        var gate = MonolithicFeasibility.Evaluate(layout, env);

        Assert.True(gate.IsFeasible,
            $"clear tube should not flag: {string.Join(";", gate.Violations.Select(v => v.Description))}");
    }

    [Fact]
    public void Evaluate_TurbineOriginDerivedBelowPumpOrigin()
    {
        // Proves the world-frame z-base derivation in Evaluate:
        // turbineOrigin.Z = pumpOrigin.Z − turbine.TotalLength_mm.
        // Tube sample at z just BELOW pumpOrigin.Z but ABOVE the
        // turbine z-base should be INSIDE the envelope.
        var pumpOrigin = new Vector3(0, 0, 0);
        var turbine = MakeTurbine(rHousing: 20, totalLen: 40);

        // Tube runs through the turbine cross-section at z=-20 along
        // the y axis. Endpoints at y=±100 sit outside the 20 mm radius
        // (no endpoint-whitelist false positive); interior samples at
        // t=4/9 and t=5/9 land at y=±11.1 which IS inside the radius.
        var tube = new FeedTube(
            Label:          "through-middle",
            Start_mm:       new Vector3(0, 100, -20),
            Corner_mm:      null,
            End_mm:         new Vector3(0, -100, -20),
            OuterRadius_mm: 5.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.StagedCombustion, new[] { tube }, 0, 0, "");

        var env = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 1,     ChamberLength_mm: 1,
            FuelPumpGeometry:      null,  FuelPumpOrigin:   pumpOrigin,
            OxPumpGeometry:        null,  OxPumpOrigin:     Vector3.Zero,
            PreburnerGeometry:     null,  PreburnerOrigin:  Vector3.Zero,
            FuelTurbineGeometry:   turbine,
            OxTurbineGeometry:     null);

        var gate = MonolithicFeasibility.Evaluate(layout, env);
        Assert.False(gate.IsFeasible);
    }
}

// ═══════════════════════════════════════════════════════════════════
// (B) tube-vs-tube intersection check
// ═══════════════════════════════════════════════════════════════════

public class NoyronV459TubeVsTubeTests
{
    private static MonolithicBodyEnvelopes EmptyEnvelopes() => new(
        ChamberOuterRadius_mm: 1,     ChamberLength_mm: 1,
        FuelPumpGeometry:      null,  FuelPumpOrigin:   Vector3.Zero,
        OxPumpGeometry:        null,  OxPumpOrigin:     Vector3.Zero,
        PreburnerGeometry:     null,  PreburnerOrigin:  Vector3.Zero);

    [Fact]
    public void TwoTubesCrossingAtMidpoint_ReportsTubeIntersection()
    {
        var a = new FeedTube(
            Label:          "tube-a",
            Start_mm:       new Vector3(-100, 0, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(100, 0, 0),
            OuterRadius_mm: 5.0);
        var b = new FeedTube(
            Label:          "tube-b",
            Start_mm:       new Vector3(0, -100, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(0, 100, 0),
            OuterRadius_mm: 5.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { a, b }, 0, 0, "");

        var gate = MonolithicFeasibility.Evaluate(layout, EmptyEnvelopes());

        Assert.False(gate.IsFeasible);
        var v = gate.Violations.FirstOrDefault(x => x.ConstraintId == "MONOLITHIC_TUBE_INTERSECTION");
        Assert.NotNull(v);
        Assert.Contains("tube-a", v!.Description);
        Assert.Contains("tube-b", v!.Description);
    }

    [Fact]
    public void ParallelTubesSeparatedByMoreThanRadii_AreFeasible()
    {
        // Two parallel tubes with a 30 mm gap; radii 5 each, clearance 2,
        // so the threshold is 12 mm. 30 mm > 12 mm → no violation.
        var a = new FeedTube(
            Label:          "a",
            Start_mm:       new Vector3(-50, 0, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(50, 0, 0),
            OuterRadius_mm: 5.0);
        var b = new FeedTube(
            Label:          "b",
            Start_mm:       new Vector3(-50, 30, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(50, 30, 0),
            OuterRadius_mm: 5.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { a, b }, 0, 0, "");

        var gate = MonolithicFeasibility.Evaluate(layout, EmptyEnvelopes());
        Assert.True(gate.IsFeasible);
    }

    [Fact]
    public void TubesSharingBranchJointEndpoint_AreWhitelisted()
    {
        // Two tubes both terminating at (0, 0, 0) — legitimate branch
        // joint (e.g. pump-discharge tee into the main manifold node).
        // Expected: the endpoint-whitelist suppresses the false
        // positive from the near-zero gap at the shared endpoint.
        var a = new FeedTube(
            Label:          "a",
            Start_mm:       new Vector3(100, 0, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(0, 0, 0),
            OuterRadius_mm: 5.0);
        var b = new FeedTube(
            Label:          "b",
            Start_mm:       new Vector3(0, 0, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(0, 100, 0),
            OuterRadius_mm: 5.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { a, b }, 0, 0, "");

        var gate = MonolithicFeasibility.Evaluate(layout, EmptyEnvelopes());
        Assert.True(gate.IsFeasible,
            $"shared-endpoint pair should not flag: {string.Join(";", gate.Violations.Select(v => v.Description))}");
    }

    [Fact]
    public void BentTubesCrossingOnSecondLeg_ReportViolation()
    {
        // Tube A goes from (100, 50, 0) → corner (0, 50, 0) → (0, 50,
        // -100). Tube B is horizontal at y=50, z=-50. A's first leg
        // runs at y=50, z=0 (50 mm away from B); A's second leg is
        // vertical at y=50, x=0 and passes through (0, 50, -50) which
        // is exactly on B's line — so the pair clashes on the second
        // leg. Proves the multi-leg iteration in the closest-point
        // loop.
        var a = new FeedTube(
            Label:          "a",
            Start_mm:       new Vector3(100, 50, 0),
            Corner_mm:      new Vector3(0,   50, 0),
            End_mm:         new Vector3(0,   50, -100),
            OuterRadius_mm: 5.0);
        var b = new FeedTube(
            Label:          "b",
            Start_mm:       new Vector3(-30, 50, -50),
            Corner_mm:      null,
            End_mm:         new Vector3(30, 50, -50),
            OuterRadius_mm: 5.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { a, b }, 0, 0, "");

        var gate = MonolithicFeasibility.Evaluate(layout, EmptyEnvelopes());

        Assert.False(gate.IsFeasible);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "MONOLITHIC_TUBE_INTERSECTION");
    }

    [Fact]
    public void ThreeTubesCrossingEachOther_EmitOnePairEach()
    {
        // Three tubes crossing at a common point (0,0,0)-ish but NOT
        // sharing that endpoint. Expect at most 3 violations (one per
        // unordered pair), never 6 (no duplicates).
        var a = new FeedTube(
            Label: "a",
            Start_mm: new Vector3(-100, 0, 0), Corner_mm: null,
            End_mm:   new Vector3(100, 0, 0),  OuterRadius_mm: 4.0);
        var b = new FeedTube(
            Label: "b",
            Start_mm: new Vector3(0, -100, 0), Corner_mm: null,
            End_mm:   new Vector3(0, 100, 0),  OuterRadius_mm: 4.0);
        var c = new FeedTube(
            Label: "c",
            Start_mm: new Vector3(0, 0, -100), Corner_mm: null,
            End_mm:   new Vector3(0, 0, 100),  OuterRadius_mm: 4.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { a, b, c }, 0, 0, "");

        var gate = MonolithicFeasibility.Evaluate(layout, EmptyEnvelopes());

        var tubePairViolations = gate.Violations
            .Where(v => v.ConstraintId == "MONOLITHIC_TUBE_INTERSECTION")
            .ToArray();
        Assert.Equal(3, tubePairViolations.Length);
    }

    [Fact]
    public void SingleTube_NoPairSweep()
    {
        var a = new FeedTube(
            Label:          "lone",
            Start_mm:       new Vector3(0, 0, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(100, 0, 0),
            OuterRadius_mm: 5.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { a }, 0, 0, "");

        var gate = MonolithicFeasibility.Evaluate(layout, EmptyEnvelopes());
        Assert.True(gate.IsFeasible);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "MONOLITHIC_TUBE_INTERSECTION");
    }
}

// ═══════════════════════════════════════════════════════════════════
// (C) rim-stress advisory on TurbineStage
// ═══════════════════════════════════════════════════════════════════

public class NoyronV459TurbineRimStressTests
{
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
        double tK = 900, double pcMPa = 15, double mdot = 5.0,
        double gamma = 1.25, double mw = 13.0) =>
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
    public void SizeOneStage_PopulatesRimStressFields()
    {
        var pump = MakePump(shaftKw: 50, rpm: 25_000);
        var pre = MakePreburner();

        var stage = TurbineSizing.SizeOneStage(
            label: "fuel", pump: pump, preburner: pre,
            turbineMassFlow: 5.0, backPressure: 15e6 * 1.10);

        Assert.True(stage.RimStress_Pa > 0);
        Assert.True(stage.RimStressAllowable_Pa > 0);
        Assert.Equal(
            TurbineSizing.MaterialYieldStress_Pa / TurbineSizing.StressSafetyFactor,
            stage.RimStressAllowable_Pa,
            precision: 3);
    }

    [Fact]
    public void SizeOneStage_RimStressMatchesTimoshenkoFormula()
    {
        var pump = MakePump(shaftKw: 50, rpm: 25_000);
        var pre = MakePreburner();

        var stage = TurbineSizing.SizeOneStage(
            label: "fuel", pump: pump, preburner: pre,
            turbineMassFlow: 5.0, backPressure: 15e6 * 1.10);

        double expected = (3.0 + TurbineSizing.PoissonRatio) / 8.0
                        * TurbineSizing.RotorMaterialDensity_kgm3
                        * stage.TipSpeed_ms * stage.TipSpeed_ms;
        Assert.Equal(expected, stage.RimStress_Pa, precision: 2);
    }

    [Fact]
    public void SizeOneStage_LowRpm_RimStressOkIsTrue()
    {
        // Low RPM → low U_tip → stress well below allowable.
        var pump = MakePump(shaftKw: 30, rpm: 15_000);
        var pre = MakePreburner(tK: 800, pcMPa: 10);

        var stage = TurbineSizing.SizeOneStage(
            label: "fuel", pump: pump, preburner: pre,
            turbineMassFlow: 4.0, backPressure: 10e6 * 1.10);

        Assert.True(stage.RimStressOk);
        Assert.True(stage.RimStress_Pa < stage.RimStressAllowable_Pa);
        Assert.DoesNotContain("OVERSPEED", stage.Notes);
    }

    [Fact]
    public void SizeOneStage_VeryHighUTip_RimStressOkIsFalse()
    {
        // Contrive an extreme case: very high preburner temperature +
        // low back-pressure → large spouting velocity + large U_tip =
        // 0.5·C₀. Using ambient back-pressure + 1500 K drives stress
        // above Inconel-718 yield / SF.
        var pump = MakePump(shaftKw: 200, rpm: 60_000);
        var pre = MakePreburner(tK: 1500, pcMPa: 15, mdot: 8, gamma: 1.25, mw: 13);

        var stage = TurbineSizing.SizeOneStage(
            label: "fuel", pump: pump, preburner: pre,
            turbineMassFlow: 8.0,
            backPressure: 1.01325e5,   // ambient → large pressure ratio
            efficiency: 0.60);

        Assert.False(stage.RimStressOk);
        Assert.True(stage.RimStress_Pa > stage.RimStressAllowable_Pa);
        Assert.Contains("OVERSPEED", stage.Notes);
    }

    [Fact]
    public void Size_EmitsRimStressWarning_OnOverspeed()
    {
        // Build a cycle where U_tip crosses the allowable: GG cycle
        // with high-T preburner and huge expansion ratio.
        var pump = MakePump(shaftKw: 200, rpm: 60_000);
        var pre = MakePreburner(tK: 1500, pcMPa: 15, mdot: 10, gamma: 1.25, mw: 13);

        var result = TurbineSizing.Size(
            cycle:                  EngineCycle.GasGenerator,
            mainChamberPressure_Pa: 10e6,
            fuelPump:               pump,
            oxPump:                 null,
            fuelPreburner:          pre,
            oxPreburner:            null);

        Assert.NotNull(result);
        Assert.Contains(result!.Warnings, w => w.Contains("rim stress"));
    }

    [Fact]
    public void Size_DoesNotEmitRimStressWarning_OnNominalCase()
    {
        var pump = MakePump(shaftKw: 50, rpm: 25_000);
        var pre = MakePreburner();

        var result = TurbineSizing.Size(
            cycle:                  EngineCycle.StagedCombustion,
            mainChamberPressure_Pa: 15e6,
            fuelPump:               pump,
            oxPump:                 null,
            fuelPreburner:          pre,
            oxPreburner:            null);

        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Warnings, w => w.Contains("rim stress"));
    }

    [Fact]
    public void Constants_MatchPublishedValues()
    {
        Assert.Equal(8900.0, TurbineSizing.RotorMaterialDensity_kgm3);
        Assert.Equal(2.0, TurbineSizing.StressSafetyFactor);
        Assert.Equal(0.30, TurbineSizing.PoissonRatio);
        Assert.True(TurbineSizing.MaterialYieldStress_Pa >= 1.0e9);
    }

    [Fact]
    public void TurbineStage_DefaultRimFields_AreZero()
    {
        // Backward-compat: callers constructing via positional 20-arg
        // form keep the legacy zero defaults.
        var stage = new TurbineStage(
            Label: "x", MassFlow_kgs: 1, InletTemperature_K: 900, InletPressure_Pa: 1e6,
            OutletPressure_Pa: 1e5, Gamma: 1.25, MolecularWeight_gmol: 20, Cp_Jkg_K: 1500,
            Efficiency: 0.6, IsentropicSpecificWork_Jkg: 1e5, ActualSpecificWork_Jkg: 6e4,
            SpoutingVelocity_ms: 400, TipSpeed_ms: 200, WheelRadius_mm: 30, Rpm: 20000,
            BladeCount: 36, StatorVaneCount: 24, RequiredShaftPower_W: 50e3,
            AvailableShaftPower_W: 60e3, PowerSufficient: true, Notes: "-");

        Assert.Equal(0.0, stage.RimStress_Pa);
        Assert.Equal(0.0, stage.RimStressAllowable_Pa);
        Assert.True(stage.RimStressOk);
    }
}
