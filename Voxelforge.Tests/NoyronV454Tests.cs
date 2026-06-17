// NoyronV454Tests.cs — Tier C4 follow-on tests. Covers two additions:
//   (1) FFSC dual-preburner split: SuggestOxRichPreburnerMr,
//       PreburnerChamber.SizeFfscDual, and the
//       RegenGenerationResult.OxidizerPreburner field.
//   (2) MONOLITHIC_BODY_INTERSECTION feasibility gate:
//       MonolithicFeasibility.Evaluate.
//
// Pure-math forcing-function tests; no PicoGK init required.

using System.Linq;
using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Turbopump;
using Xunit;

namespace Voxelforge.Tests;

public class NoyronV454FfscDualTests
{
    // ─── SuggestOxRichPreburnerMr ─────────────────────────────────

    [Theory]
    [InlineData(PropellantPair.LOX_CH4, 10.0)]
    [InlineData(PropellantPair.LOX_H2,  50.0)]
    [InlineData(PropellantPair.LOX_RP1, 10.0)]
    public void SuggestOxRichPreburnerMr_IsAboveStoichiometric(PropellantPair pair, double lowerBound)
    {
        double mr = PreburnerChamber.SuggestOxRichPreburnerMr(pair);
        Assert.True(mr > lowerBound, $"expected ox-rich MR > {lowerBound}, got {mr}");
    }

    [Fact]
    public void SuggestOxRichPreburnerMr_IsFarAboveFuelRich()
    {
        // Fuel-rich MR ≈ 0.5-0.8, ox-rich MR ≈ 25-150 — ratio should be > 20×.
        foreach (var pair in new[] { PropellantPair.LOX_CH4, PropellantPair.LOX_H2, PropellantPair.LOX_RP1 })
        {
            double frMr = PreburnerChamber.SuggestPreburnerMr(EngineCycle.FullFlow, pair);
            double orMr = PreburnerChamber.SuggestOxRichPreburnerMr(pair);
            Assert.True(orMr / frMr > 20,
                $"{pair}: ox-rich {orMr} vs fuel-rich {frMr} (ratio {orMr/frMr:F1}×) — expected > 20×");
        }
    }

    // ─── SizeFfscDual ─────────────────────────────────────────────

    [Fact]
    public void SizeFfscDual_WithDefaultMrs_ReturnsBothPreburners()
    {
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            pair:                  PropellantPair.LOX_CH4,
            fuelRichMr:            0,     // auto = Suggest
            oxRichMr:              0,     // auto = SuggestOxRich
            preburnerPc_Pa:        15e6,
            totalFuelMassFlow_kgs: 2.0,
            totalOxMassFlow_kgs:   7.0);

        Assert.NotNull(fr);
        Assert.NotNull(or);
        Assert.Equal(EngineCycle.FullFlow, fr.Cycle);
        Assert.Equal(EngineCycle.FullFlow, or.Cycle);
    }

    [Fact]
    public void SizeFfscDual_FuelRichIsFuelRich_OxRichIsOxRich()
    {
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            PropellantPair.LOX_CH4, 0, 0, 15e6, 2.0, 7.0);

        Assert.True(fr.MixtureRatio < 1.0,
            $"fuel-rich MR should be < 1, got {fr.MixtureRatio}");
        Assert.True(or.MixtureRatio > 10.0,
            $"ox-rich MR should be > 10, got {or.MixtureRatio}");
        Assert.True(or.MixtureRatio > fr.MixtureRatio * 10,
            "ox-rich MR should be far above fuel-rich MR");
    }

    [Fact]
    public void SizeFfscDual_NotesDistinguishSides()
    {
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            PropellantPair.LOX_CH4, 0, 0, 15e6, 2.0, 7.0);

        Assert.Contains("fuel-rich",   fr.Notes);
        Assert.Contains("ox-rich",     or.Notes);
        Assert.Contains("fuel pump",   fr.Notes);
        Assert.Contains("ox pump",     or.Notes);
    }

    [Fact]
    public void SizeFfscDual_RespectsExplicitMrOverride()
    {
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            pair:                  PropellantPair.LOX_CH4,
            fuelRichMr:            0.55,    // explicit
            oxRichMr:              40.0,    // explicit
            preburnerPc_Pa:        15e6,
            totalFuelMassFlow_kgs: 2.0,
            totalOxMassFlow_kgs:   7.0);

        Assert.Equal(0.55, fr.MixtureRatio, precision: 4);
        Assert.Equal(40.0, or.MixtureRatio, precision: 2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void SizeFfscDual_NonPositiveFuelFlow_Throws(double badFlow)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PreburnerChamber.SizeFfscDual(
                PropellantPair.LOX_CH4, 0, 0, 15e6, badFlow, 7.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    public void SizeFfscDual_NonPositiveOxFlow_Throws(double badFlow)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PreburnerChamber.SizeFfscDual(
                PropellantPair.LOX_CH4, 0, 0, 15e6, 2.0, badFlow));
    }

    [Fact]
    public void SizeFfscDual_NonPositivePc_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            PreburnerChamber.SizeFfscDual(
                PropellantPair.LOX_CH4, 0, 0, 0, 2.0, 7.0));
    }

    [Fact]
    public void SizeFfscDual_MassFlowSplitConservesMass()
    {
        // Exact 2×2 solve (see PreburnerChamber.SizeFfscDual).
        // For M_fuel=2, M_ox=7, MR_fr=0.6, MR_or=35, MR_overall=3.5:
        //   f_fr = 2 · (35 − 3.5) / (35 − 0.6)       ≈ 1.8314
        //   f_or = 2 − 1.8314                         ≈ 0.1686
        //   o_fr = 0.6 · 1.8314                       ≈ 1.0988
        //   o_or = 35  · 0.1686                       ≈ 5.9012
        //   FR_mdot = f_fr + o_fr                     ≈ 2.9302
        //   OR_mdot = f_or + o_or                     ≈ 6.0698
        // Sum = 9.0 kg/s == M_fuel + M_ox            ← mass conservation.
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            PropellantPair.LOX_CH4, 0.6, 35.0, 15e6, 2.0, 7.0);

        Assert.Equal(2.9302, fr.MassFlow_kgs, precision: 3);
        Assert.Equal(6.0698, or.MassFlow_kgs, precision: 3);
        // Exact mass conservation: preburner mdots sum to total propellant.
        Assert.Equal(2.0 + 7.0, fr.MassFlow_kgs + or.MassFlow_kgs, precision: 6);
    }

    [Theory]
    [InlineData(0.6, 35.0, 3.5)]   // LOX/CH4 Raptor-like
    [InlineData(0.8, 150.0, 6.0)]  // LOX/H2 SSME-like
    [InlineData(0.4, 25.0, 2.3)]   // LOX/RP1 RD-180-like
    public void SizeFfscDual_PerPreburnerMrMatchesTarget(double frMr, double orMr, double overallMr)
    {
        // By construction each preburner's effective MR (its ox mdot
        // divided by its fuel mdot) equals the target MR passed in.
        double mFuel = 1.0;
        double mOx   = overallMr * mFuel;
        var (fr, or) = PreburnerChamber.SizeFfscDual(
            PropellantPair.LOX_CH4, frMr, orMr, 15e6, mFuel, mOx);

        Assert.Equal(frMr, fr.MixtureRatio, precision: 3);
        Assert.Equal(orMr, or.MixtureRatio, precision: 3);
        Assert.Equal(mFuel + mOx, fr.MassFlow_kgs + or.MassFlow_kgs, precision: 6);
    }

    [Fact]
    public void SingleCallSize_WithFullFlow_SuppressesMvpWarning_WhenFlagSet()
    {
        var result = PreburnerChamber.Size(
            cycle:                        EngineCycle.FullFlow,
            pair:                         PropellantPair.LOX_CH4,
            preburnerMr:                  0.6,
            preburnerPc_Pa:               15e6,
            turbineMassFlow_kgs:          3.0,
            suppressFfscSingleMvpWarning: true);

        Assert.NotNull(result);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("single-preburner"));
    }

    [Fact]
    public void SingleCallSize_WithFullFlow_EmitsMvpWarning_ByDefault()
    {
        var result = PreburnerChamber.Size(
            cycle:               EngineCycle.FullFlow,
            pair:                PropellantPair.LOX_CH4,
            preburnerMr:         0.6,
            preburnerPc_Pa:      15e6,
            turbineMassFlow_kgs: 3.0);

        Assert.NotNull(result);
        Assert.Contains(result.Warnings, w =>
            w.Contains("SizeFfscDual", System.StringComparison.Ordinal));
    }
}

public class NoyronV454BodyIntersectionTests
{
    // ─── MonolithicFeasibility.Evaluate ───────────────────────────

    [Fact]
    public void Evaluate_NullLayout_Throws()
    {
        var envelopes = new MonolithicBodyEnvelopes(
            100, 300, null, Vector3.Zero, null, Vector3.Zero, null, Vector3.Zero);
        Assert.Throws<System.ArgumentNullException>(() =>
            MonolithicFeasibility.Evaluate(null!, envelopes));
    }

    [Fact]
    public void Evaluate_NullEnvelopes_Throws()
    {
        var layout = new FeedManifoldLayout(
            EngineCycle.PressureFed, System.Array.Empty<FeedTube>(), 0, 0, "");
        Assert.Throws<System.ArgumentNullException>(() =>
            MonolithicFeasibility.Evaluate(layout, null!));
    }

    [Fact]
    public void Evaluate_EmptyLayout_IsFeasible()
    {
        var layout = new FeedManifoldLayout(
            EngineCycle.PressureFed, System.Array.Empty<FeedTube>(), 0, 0, "");
        var envelopes = new MonolithicBodyEnvelopes(
            100, 300, null, Vector3.Zero, null, Vector3.Zero, null, Vector3.Zero);

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);

        Assert.True(gate.IsFeasible);
        Assert.Empty(gate.Violations);
    }

    [Fact]
    public void Evaluate_TubePassingThroughChamberMidpoint_ReportsViolation()
    {
        // Tube runs from (150, 300, 0) to (150, -300, 0) — straight
        // through the middle of a chamber of radius 50 mm, length 400 mm
        // at origin. Midpoint = (150, 0, 0) is clearly inside.
        var tube = new FeedTube(
            Label:          "bad-tube",
            Start_mm:       new Vector3(150, 300, 0),
            Corner_mm:      null,
            End_mm:         new Vector3(150, -300, 0),
            OuterRadius_mm: 8.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { tube }, 0, 0, "");

        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 50,
            ChamberLength_mm:      400,
            FuelPumpGeometry:      null,
            FuelPumpOrigin:        Vector3.Zero,
            OxPumpGeometry:        null,
            OxPumpOrigin:          Vector3.Zero,
            PreburnerGeometry:     null,
            PreburnerOrigin:       Vector3.Zero);

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);

        Assert.False(gate.IsFeasible);
        Assert.Single(gate.Violations);
        Assert.Equal("MONOLITHIC_BODY_INTERSECTION", gate.Violations[0].ConstraintId);
        Assert.Contains("chamber", gate.Violations[0].Description);
    }

    [Fact]
    public void Evaluate_TubeClearOfAllBodies_IsFeasible()
    {
        // Tube runs far from any body envelope.
        var tube = new FeedTube(
            Label:          "clear-tube",
            Start_mm:       new Vector3(-200, 300, -400),
            Corner_mm:      null,
            End_mm:         new Vector3(-200, 500, -400),
            OuterRadius_mm: 8.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.PressureFed, new[] { tube }, 0, 0, "");

        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 50,
            ChamberLength_mm:      400,
            FuelPumpGeometry:      null,
            FuelPumpOrigin:        Vector3.Zero,
            OxPumpGeometry:        null,
            OxPumpOrigin:          Vector3.Zero,
            PreburnerGeometry:     null,
            PreburnerOrigin:       Vector3.Zero);

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);

        Assert.True(gate.IsFeasible);
    }

    [Fact]
    public void Evaluate_DischargeTubeTerminatingAtInjectorDome_IsFeasible()
    {
        // Simulates the standard fuel-discharge: ends AT the
        // injector dome (origin). Its second-leg midpoint falls inside
        // the chamber cylinder near the X=0 face — but the endpoint
        // whitelist should suppress the violation.
        var tube = new FeedTube(
            Label:          "fuel-discharge",
            Start_mm:       new Vector3(50, 80, 100),
            Corner_mm:      new Vector3(50,  0, 100),
            End_mm:         new Vector3(0,   0, 0),         // injector dome
            OuterRadius_mm: 8.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.GasGenerator, new[] { tube }, 0, 0, "");

        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 50,
            ChamberLength_mm:      400,
            FuelPumpGeometry:      null,
            FuelPumpOrigin:        Vector3.Zero,
            OxPumpGeometry:        null,
            OxPumpOrigin:          Vector3.Zero,
            PreburnerGeometry:     null,
            PreburnerOrigin:       Vector3.Zero);

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);

        Assert.True(gate.IsFeasible,
            $"tube ending at chamber face should be accepted: {string.Join("; ", gate.Violations.Select(v => v.Description))}");
    }

    [Fact]
    public void Evaluate_TubeThroughPreburnerBody_ReportsViolation()
    {
        // Preburner centred at (200, 200, 0), radius 30, length 60,
        // axis along +X. Tube crosses its midsection.
        var preGeom = PreburnerVoxel.Size(
            new PreburnerResult(
                EngineCycle.StagedCombustion, 0.6, 15e6, 900, 1200, 1.2, 20, 3.0,
                0.40, 2_000_000, "", System.Array.Empty<string>()));

        var tube = new FeedTube(
            Label:          "bad-preburner-tube",
            Start_mm:       new Vector3(230, 200, -200),
            Corner_mm:      null,
            End_mm:         new Vector3(230, 200, 200),
            OuterRadius_mm: 8.0);

        var layout = new FeedManifoldLayout(
            EngineCycle.StagedCombustion, new[] { tube }, 0, 0, "");

        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm: 40,
            ChamberLength_mm:      400,
            FuelPumpGeometry:      null,
            FuelPumpOrigin:        Vector3.Zero,
            OxPumpGeometry:        null,
            OxPumpOrigin:          Vector3.Zero,
            PreburnerGeometry:     preGeom,
            PreburnerOrigin:       new Vector3(200, 200, 0));

        var gate = MonolithicFeasibility.Evaluate(layout, envelopes);

        Assert.False(gate.IsFeasible);
        Assert.Contains(gate.Violations, v => v.Description.Contains("preburner"));
    }

    [Fact]
    public void DefaultClearance_IsPositive()
    {
        Assert.True(MonolithicFeasibility.DefaultClearance_mm > 0);
    }

    // ─── RegenGenerationResult.OxidizerPreburner wiring ─────────

    [Fact]
    public void RegenGenerationResult_HasOxidizerPreburnerProperty()
    {
        // Verify the FFSC dual-preburner field shape without instantiating the heavy
        // record. The property must be a PreburnerResult? (nullable).
        var prop = typeof(Voxelforge.Optimization.RegenGenerationResult)
            .GetProperty("OxidizerPreburner");
        Assert.NotNull(prop);
        Assert.Equal(typeof(PreburnerResult), prop!.PropertyType);

        // Also verify the sibling Preburner field is still present.
        var siblingProp = typeof(Voxelforge.Optimization.RegenGenerationResult)
            .GetProperty("Preburner");
        Assert.NotNull(siblingProp);
        Assert.Equal(typeof(PreburnerResult), siblingProp!.PropertyType);
    }
}
