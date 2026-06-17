// InjectorElementTests.cs — Contract tests for the injector-element library
// (UPGRADE 2). Covers orifice sizing physics, element types, pattern record,
// film-fraction override, and report export integration.
//
// Preliminary-design fidelity — sizing is single-phase incompressible
// (Q = Cd·A·√(2·ΔP/ρ)). No breakup, spray, or combustion model.

using Voxelforge.Combustion;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.IO;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class InjectorElementTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────

    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N              = 2224.0,         // 500 lbf
        ChamberPressure_Pa    = 6.9e6,          // 1 000 psia
        MixtureRatio          = 3.3,
        CoolantInletTemp_K    = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex     = 1,
        PropellantPair        = PropellantPair.LOX_CH4,
    };

    /// <summary>
    /// Build the standard sizing inputs for one element at 20% dP/Pc
    /// using the LOX/CH4 reference densities and symmetric 0.01 kg/s per element.
    /// </summary>
    private static SizingInputs MakeInputs(
        double oxFlow  = 0.01,
        double fuFlow  = 0.01,
        double dP_Pa   = 0.20 * 6.9e6,
        double oxRho   = OrificeModel.ReferenceDensity_kgm3.LOX,
        double fuRho   = OrificeModel.ReferenceDensity_kgm3.LCH4)
        => new SizingInputs(
            DeltaPInj_Pa:           dP_Pa,
            OxDensity_kgm3:         oxRho,
            FuelDensity_kgm3:       fuRho,
            OxFlowPerElement_kgs:   oxFlow,
            FuelFlowPerElement_kgs: fuFlow,
            CdOx:   OrificeModel.DefaultCd,
            CdFuel: OrificeModel.DefaultCd);

    // ─────────────────────────────────────────────────────────────────
    //  1. OrificeModel closed-form consistency
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Round-trip: area → diameter → area must equal itself.
    /// Also verifies A = ṁ / (Cd·√(2·ρ·ΔP)) against the direct formula.
    /// </summary>
    [Fact]
    public void OrificeModel_AreaAndDiameterAreConsistent()
    {
        double mDot = 0.015;
        double dP   = 1.38e6;     // 20% of 6.9 MPa
        double rho  = OrificeModel.ReferenceDensity_kgm3.LOX;
        double cd   = OrificeModel.DefaultCd;

        double area_mm2 = OrificeModel.OrificeArea_mm2(mDot, dP, rho, cd);
        double diam_mm  = OrificeModel.OrificeDiameter_mm(mDot, dP, rho, cd);

        // diameter reconstructed from area must agree within 0.01 %
        double area_from_diam = Math.PI * diam_mm * diam_mm / 4.0;
        Assert.Equal(area_mm2, area_from_diam, precision: 4);

        // closed-form check: A = ṁ / (Cd·√(2·ρ·ΔP))
        double expected_m2 = mDot / (cd * Math.Sqrt(2.0 * rho * dP));
        Assert.Equal(expected_m2 * 1e6, area_mm2, precision: 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  2. CoaxElement sizes both annuli and produces non-zero results
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CoaxElement_SizesBothOrificesToPositiveAreas()
    {
        var elem = InjectorElementFactory.Create("Coax");
        Assert.True(elem.IsImplemented, "CoaxElement must report IsImplemented = true");

        var result = elem.Size(MakeInputs());

        Assert.True(result.OxOrificeArea_mm2  > 0, "Ox area must be positive");
        Assert.True(result.FuelOrificeArea_mm2 > 0, "Fuel area must be positive");
        Assert.True(result.OxEquivDiameter_mm  > 0);
        Assert.True(result.FuelEquivDiameter_mm > 0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  3. CoaxElement — fuel annulus area is larger than ox post for
    //     symmetric mass flows because CH4 (ρ=430) is less dense than
    //     LOX (ρ=1140), so the fuel orifice must be bigger.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CoaxElement_FuelAreaLargerThanOxForSameFlowLowDensityFuel()
    {
        var elem   = InjectorElementFactory.Create("Coax");
        var result = elem.Size(MakeInputs(
            oxFlow: 0.01, fuFlow: 0.01,
            oxRho:  OrificeModel.ReferenceDensity_kgm3.LOX,
            fuRho:  OrificeModel.ReferenceDensity_kgm3.LCH4));

        // A ∝ 1/√ρ at equal flow; LCH4 is ~2.66× less dense → √2.66 ≈ 1.63× bigger
        Assert.True(result.FuelOrificeArea_mm2 > result.OxOrificeArea_mm2,
            $"Fuel area ({result.FuelOrificeArea_mm2:F3} mm²) should exceed ox area "
          + $"({result.OxOrificeArea_mm2:F3} mm²) when fuel is less dense at equal flow.");
    }

    // ─────────────────────────────────────────────────────────────────
    //  4. ImpingingDoubletElement sizes correctly and has expected
    //     velocity ratio at equal density/flow conditions.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ImpingingDoublet_VelocityRatioEqualsOneAtSymmetricConditions()
    {
        var elem = InjectorElementFactory.Create("ImpingingDoublet");
        Assert.True(elem.IsImplemented);

        // Equal density and equal flow → V_fuel / V_ox should = 1.0
        // V = Cd·√(2·ΔP/ρ); with equal Cd, ρ, ΔP the velocities are equal
        var result = elem.Size(MakeInputs(
            oxFlow: 0.01, fuFlow: 0.01,
            oxRho: 1000.0, fuRho: 1000.0));   // force equal densities

        Assert.Equal(1.0, result.VelocityRatio, precision: 4);
    }

    // ─────────────────────────────────────────────────────────────────
    //  5. Formerly-stub elements (Pintle/Showerhead/Swirl) are now all
    //     implemented (TIER B.7, 2026-04-21). They report IsImplemented=true
    //     and return sensible positive orifice areas for a baseline input.
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pintle")]
    [InlineData("Showerhead")]
    [InlineData("Swirl")]
    public void FormerlyStubElements_AreNowImplemented(string type)
    {
        var elem = InjectorElementFactory.Create(type);
        Assert.True(elem.IsImplemented,
            $"{type} should now report IsImplemented = true (promoted from stub in TIER B.7).");
        var r = elem.Size(MakeInputs());
        Assert.True(r.OxOrificeArea_mm2 > 0,   $"{type} ox area must be positive.");
        Assert.True(r.FuelOrificeArea_mm2 > 0, $"{type} fuel area must be positive.");
    }

    // ─────────────────────────────────────────────────────────────────
    //  6. InjectorPattern.SizePattern aggregates per-element results
    //     and flow-split check should be ≈ 1.0 for a valid Coax pattern.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void InjectorPattern_FlowSplitCheckIsNearUnityForCoax()
    {
        var pattern = InjectorPattern.DefaultCoax(elementCount: 20);
        double dP_Pa   = 0.20 * 6.9e6;
        double oxRho   = OrificeModel.ReferenceDensity_kgm3.LOX;
        double fuRho   = OrificeModel.ReferenceDensity_kgm3.LCH4;
        double mDotTot = 1.0;     // kg/s total
        double mDotOx  = mDotTot / (1.0 + 1.0 / 3.3);   // rough MR=3.3 split
        double mDotFu  = mDotTot - mDotOx;

        var sizing = pattern.SizePattern(mDotOx, mDotFu, dP_Pa, oxRho, fuRho);

        Assert.True(sizing.ElementCount == 20);
        Assert.True(sizing.TotalOxArea_mm2   > 0);
        Assert.True(sizing.TotalFuelArea_mm2 > 0);
        // Flow split check: predicted total / target total — must be within 2%
        Assert.InRange(sizing.FlowSplitCheck, 0.98, 1.02);
    }

    // ─────────────────────────────────────────────────────────────────
    //  7. RegenGenerationResult carries InjectorPattern + InjectorSizing
    //     fields after GenerateWith when a pattern is set.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_PopulatesInjectorSizingWhenPatternIsSet()
    {
        var cond = DefaultConditions();
        var design = new RegenChamberDesign
        {
            // Disable voxel build to keep test fast
            IncludeManifolds   = false,
            IncludePorts       = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
            InjectorElementPattern = InjectorPattern.DefaultCoax(elementCount: 16),
        };

        var result = RegenChamberOptimization.GenerateWith(cond, design);

        Assert.NotNull(result.InjectorPattern);
        Assert.NotNull(result.InjectorSizing);
        Assert.Equal("Coax", result.InjectorPattern!.ElementType);
        Assert.Equal(16, result.InjectorSizing!.ElementCount);
        Assert.True(result.InjectorSizing.TotalOxArea_mm2   > 0);
        Assert.True(result.InjectorSizing.TotalFuelArea_mm2 > 0);
        Assert.InRange(result.InjectorSizing.FlowSplitCheck, 0.98, 1.02);
    }

    // ─────────────────────────────────────────────────────────────────
    //  8. When no pattern is set, both fields are null — no regression.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateWith_InjectorFieldsAreNullWhenNoPatternSet()
    {
        var cond   = DefaultConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
            // InjectorElementPattern = null (default)
        };

        var result = RegenChamberOptimization.GenerateWith(cond, design);

        Assert.Null(result.InjectorPattern);
        Assert.Null(result.InjectorSizing);
    }

    // ─────────────────────────────────────────────────────────────────
    //  9. Report export includes injector section when pattern is present.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ReportExport_IncludesInjectorSectionWhenPatternSet()
    {
        var cond   = DefaultConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
            InjectorElementPattern = InjectorPattern.DefaultImpinging(elementCount: 24),
        };

        var result = RegenChamberOptimization.GenerateWith(cond, design);
        string report = ReportExport.Build(result);

        Assert.Contains("INJECTOR ELEMENT PATTERN",  report);
        Assert.Contains("ImpingingDoublet",           report);
        Assert.Contains("Per-element Ox area",        report);
        Assert.Contains("Per-element Fu area",        report);
        Assert.Contains("Flow split check",           report);
    }

    // ─────────────────────────────────────────────────────────────────
    //  10. Film-fraction override: OuterRowFilmFraction > 0 activates
    //      film cooling in the solver result (CoolantOutletT is lower
    //      than with no film because less fuel flows the jacket).
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FilmFraction_ReducesCoolantJacketFlowAndChangesOutletTemp()
    {
        var cond = DefaultConditions();

        // Baseline: no pattern (film disabled)
        var baseDesign = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
        };

        // Film variant: 10 % fuel bleed via outer row
        var filmDesign = baseDesign with
        {
            InjectorElementPattern = new InjectorPattern
            {
                ElementType          = "Coax",
                ElementCount         = 20,
                OuterRowFilmFraction = 0.10,
            },
        };

        var baseResult = RegenChamberOptimization.GenerateWith(cond, baseDesign);
        var filmResult = RegenChamberOptimization.GenerateWith(cond, filmDesign);

        // With less coolant in the jacket the outlet temperature should be higher
        // (same heat load, lower flow) — or at minimum the two results differ.
        // We only assert direction: film-cooled outlet ≥ baseline outlet because
        // the heat load per unit flow rises.
        Assert.True(
            filmResult.Thermal.CoolantOutletT_K >= baseResult.Thermal.CoolantOutletT_K,
            $"Film design coolant outlet T ({filmResult.Thermal.CoolantOutletT_K:F1} K) "
          + $"should be ≥ baseline ({baseResult.Thermal.CoolantOutletT_K:F1} K) "
          + "due to reduced jacket flow.");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Sprint 18 (2026-04-23) — Pintle injector surface
    //
    //  PintleElement was implemented in Tier B.7 but its knobs lived
    //  as instance properties on the element class (i.e. not SA-tunable,
    //  not UI-settable, not persistable). Sprint 18 moves the knobs to
    //  InjectorPattern → SizingInputs → PintleElement.Size(), adds a
    //  DefaultPintle() factory, and wires two new feasibility gates
    //  (PINTLE_BLOCKAGE_OUT_OF_BAND, PINTLE_TMR_OUT_OF_BAND) for the
    //  Dressler stable-combustion + mixing-quality bands.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultPintle_FactoryReturnsSingleCentralElement()
    {
        var pattern = InjectorPattern.DefaultPintle();
        Assert.Equal("Pintle", pattern.ElementType);
        Assert.Equal(1, pattern.ElementCount);
        Assert.Equal(InjectorFaceLayout.Central, pattern.FaceLayout);
        Assert.Equal(12.0, pattern.PintleDiameter_mm);
        Assert.Equal(18,   pattern.PintleSleeveHoleCount);
        Assert.Equal(0.60, pattern.PintleBlockageFractionTarget);
    }

    [Fact]
    public void Pintle_SizingUsesPatternKnobs_NotElementDefaults()
    {
        // Pre-Sprint-18 the three knobs (PintleDiameter_mm,
        // PintleSleeveHoleCount, PintleBlockageFractionTarget) lived as
        // instance properties on PintleElement. Post-Sprint-18 they come
        // via SizingInputs. Verify a non-default PintleDiameter flows
        // through to the sized blockage factor.
        var pattern = InjectorPattern.DefaultPintle() with
        {
            PintleDiameter_mm     = 20.0,      // up from 12.0 default
            PintleSleeveHoleCount = 24,        // up from 18 default
        };

        var sizing = pattern.SizePattern(
            totalOxFlow_kgs:   0.5,
            totalFuelFlow_kgs: 0.2,
            deltaPInj_Pa:      1.4e6,
            oxDensity_kgm3:    OrificeModel.ReferenceDensity_kgm3.LOX,
            fuelDensity_kgm3:  OrificeModel.ReferenceDensity_kgm3.LCH4);

        // First note string must contain the pattern's PintleDiameter_mm
        // to prove the Size() routine read from SizingInputs, not from a
        // stale PintleElement instance-default.
        Assert.Contains("Pintle Ø = 20.0 mm", sizing.PerElementResult.Notes[0]);
        // Second note string must reflect the non-default sleeve count.
        Assert.Contains("24 sleeve holes", sizing.PerElementResult.Notes[1]);
    }

    [Fact]
    public void Pintle_InBandDesign_FeasibilityGatesSilent()
    {
        // A well-tuned LOX/CH4 pintle at ~20 kN lands inside both the
        // blockage band (Dressler 0.40-0.85) and the TMR band (0.2-4.0).
        // No Pintle-specific violations should appear.
        //
        // Note: DefaultPintle's 12 mm post is tuned for sub-kN; at 20 kN
        // the per-element fuel flow demands a larger pintle to keep
        // blockage in-band (BL = N·d_sleeve / (π·D_pintle); small
        // D_pintle drives BL toward 1.0). 25 mm works for the 20 kN
        // LOX/CH4 reference.
        var cond = DefaultConditions() with
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
            InjectorElementPattern = InjectorPattern.DefaultPintle() with
            {
                PintleDiameter_mm     = 25.0,  // scaled for 20 kN class
                PintleSleeveHoleCount = 18,
            },
        };

        var gen  = RegenChamberOptimization.GenerateWith(cond, design);
        var feas = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "PINTLE_BLOCKAGE_OUT_OF_BAND");
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "PINTLE_TMR_OUT_OF_BAND");

        // Sanity: the sized blockage + TMR ARE populated on a pintle design.
        var per = gen.InjectorSizing!.PerElementResult;
        Assert.InRange(per.PintleBlockageFraction,
            FeasibilityGate.PintleBlockageFloor,
            FeasibilityGate.PintleBlockageCeiling);
        Assert.InRange(per.MomentumRatio,
            FeasibilityGate.PintleTmrFloor,
            FeasibilityGate.PintleTmrCeiling);
    }

    [Fact]
    public void Pintle_UndersizedPost_BlockageGateFires()
    {
        // Tiny pintle diameter with default 18 sleeve holes pushes
        // blockage above the 0.85 ceiling (sleeve holes account for
        // most of the circumference of a small post). Gate fires.
        var cond = DefaultConditions() with
        {
            Thrust_N           = 20_000,
            ChamberPressure_Pa = 7e6,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
            InjectorElementPattern = InjectorPattern.DefaultPintle() with
            {
                PintleDiameter_mm     = 3.0,   // too small for 20 kN flow
                PintleSleeveHoleCount = 18,
            },
        };

        var gen  = RegenChamberOptimization.GenerateWith(cond, design);
        var feas = FeasibilityGate.Evaluate(gen);

        Assert.Contains(feas.Violations,
            v => v.ConstraintId == "PINTLE_BLOCKAGE_OUT_OF_BAND");
    }

    [Fact]
    public void NonPintleElement_PintleGatesStaySilent()
    {
        // Regression guard: a Coax pattern must NEVER trip a Pintle gate
        // even though the sized OrificeResult is populated. Non-pintle
        // elements leave PintleBlockageFraction at 0 which the gate
        // logic short-circuits on.
        var cond   = DefaultConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds      = false,
            IncludePorts          = false,
            IncludeInjectorFlange = false,
            ContourStationCount   = 60,
            InjectorElementPattern = InjectorPattern.DefaultCoax(elementCount: 20),
        };

        var gen  = RegenChamberOptimization.GenerateWith(cond, design);
        var feas = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "PINTLE_BLOCKAGE_OUT_OF_BAND");
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "PINTLE_TMR_OUT_OF_BAND");
    }
}
