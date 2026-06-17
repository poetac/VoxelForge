// InjectorFaceThermalUnitTests.cs — Tech-debt T6 (2026-04-28).
//
// Pure-math unit tests for the new physics-only entry point
// `InjectorFaceThermal.Estimate(InjectorFaceGeometry, double?)` and the
// `RegenGenerationResult.ToInjectorFaceGeometry()` adapter. Pre-T6 the
// only way to exercise the solver was a full `GenerateWith` round-trip;
// these tests construct hand-built geometry records so they catch
// regressions in the lumped equilibrium math without paying the
// orchestrator's cost. xUnit-safe: no `PicoGK.Library` instantiation
// (CLAUDE.md pitfall #8).

using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class InjectorFaceThermalUnitTests
{
    /// <summary>
    /// Build a Merlin-class synthetic geometry — enough fields populated
    /// that the lumped solver runs end-to-end. Override `elementType` /
    /// `elementCount` / `fuelVelocity_ms` for variation tests.
    /// </summary>
    private static InjectorFaceGeometry BuildSyntheticGeometry(
        string elementType = "Coax",
        int elementCount = 20,
        double fuelVelocity_ms = 25.0,
        double oxVelocity_ms = 22.0)
    {
        var perElement = new OrificeResult(
            OxOrificeArea_mm2:    4.0,
            FuelOrificeArea_mm2:  3.0,
            OxVelocity_ms:        oxVelocity_ms,
            FuelVelocity_ms:      fuelVelocity_ms,
            VelocityRatio:        fuelVelocity_ms / System.Math.Max(oxVelocity_ms, 1e-9),
            MomentumRatio:        1.0,
            Notes:                System.Array.Empty<string>(),
            PintleBlockageFraction: 0.0);
        var sizing = new PatternSizingResult(
            ElementCount:      elementCount,
            PerElementResult:  perElement,
            TotalOxArea_mm2:   4.0 * elementCount,
            TotalFuelArea_mm2: 3.0 * elementCount,
            FlowSplitCheck:    1.0,
            Warnings:          System.Array.Empty<string>());
        var pattern = new InjectorPattern
        {
            ElementType  = elementType,
            ElementCount = elementCount,
        };

        return new InjectorFaceGeometry(
            ChamberRadius_mm:        50.0,
            H_g_x0_Wm2K:             25_000.0,
            T_aw_x0_K:                2_800.0,
            T_film_face_x0_K:         1_500.0,
            PropellantPair:           PropellantPair.LOX_CH4,
            CoolantInletTemp_K:       150.0,
            CoolantInletPressure_Pa:  12.0e6,
            WallMaterialIndex:        1,
            OxidizerMassFlow_kgs:     1.15,
            FuelMassFlow_kgs:         0.35,
            TotalMassFlow_kgs:        1.50,
            Pattern:                  pattern,
            Sizing:                   sizing);
    }

    [Fact]
    public void EstimateOnSyntheticGeometry_LandsInPublishedFaceTBand()
    {
        var geom = BuildSyntheticGeometry();
        var result = InjectorFaceThermal.Estimate(geom);

        Assert.InRange(result.TFace_K, 400.0, 2_000.0);
        Assert.True(result.HeatFlux_Wm2 > 0,
            $"net face flux must be positive (gas-side hotter than face); got {result.HeatFlux_Wm2}");
        Assert.True(result.HGasSide_Wm2K == 25_000.0,
            "gas-side HTC echoes the input H_g_x0_Wm2K verbatim");
        Assert.Equal(System.Math.PI * 50.0 * 50.0 * 1e-2, result.FaceArea_cm2, precision: 6);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void PintleVsCoaxSameHotSide_PintleHasLowerTFace()
    {
        // Pintle's MixingLayerEffectivenessFor returns 0.80 (heavier film
        // attenuation) vs Coax's 0.65. With identical h_g, T_aw, T_film,
        // and propellant flow, a higher attenuation drives T_aw lower
        // (closer to T_film_face), which lowers the equilibrium T_face.
        // This isolates ElementType -> mixingEff path through the solver.
        var coaxGeom = BuildSyntheticGeometry(elementType: "Coax", elementCount: 20);
        var pintleGeom = BuildSyntheticGeometry(elementType: "Pintle", elementCount: 20);

        var coaxResult = InjectorFaceThermal.Estimate(coaxGeom);
        var pintleResult = InjectorFaceThermal.Estimate(pintleGeom);

        Assert.True(pintleResult.TFace_K < coaxResult.TFace_K,
            $"Pintle (η_film=0.80) must yield T_face < Coax (η_film=0.65). "
            + $"Got pintle={pintleResult.TFace_K:F1} K vs coax={coaxResult.TFace_K:F1} K.");
    }

    [Fact]
    public void DegenerateBoreVelocity_FiresFloorWarning()
    {
        // L5 (post-Phase-6 logical-error audit) floor: bore velocity below
        // 10 m/s indicates a degenerate upstream sizer; T_face uses the
        // floored velocity but emits a warning so the regression is visible.
        var geom = BuildSyntheticGeometry(fuelVelocity_ms: 0.5, oxVelocity_ms: 0.0);
        var result = InjectorFaceThermal.Estimate(geom);

        Assert.Contains(result.Warnings, w =>
            w.Contains("below 10 m/s floor", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ToInjectorFaceGeometry_ReturnsNullWhenNoInjectorPattern()
    {
        // MinimalDesign carries no InjectorElementPattern, so the
        // generated result has gen.InjectorPattern == null. The adapter
        // must short-circuit and return null instead of throwing or
        // populating a half-constructed record.
        var cond = new OperatingConditions
        {
            Thrust_N = 2_224.0, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
            CoolantInletTemp_K = 150, CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex = 1, PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 60,
        };
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: true);

        Assert.Null(gen.InjectorPattern);
        Assert.Null(gen.ToInjectorFaceGeometry());
    }

    [Fact]
    public void ToInjectorFaceGeometry_PopulatesAllFieldsFromGeneratedResult()
    {
        // Round-trip: generate a real result with an injector pattern,
        // derive the geometry record, and assert every projected field
        // matches the source. Catches future refactors that swap two
        // accidentally-identically-typed fields in the adapter.
        var cond = new OperatingConditions
        {
            Thrust_N = 2_224.0, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
            CoolantInletTemp_K = 150, CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex = 1, PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 60,
            InjectorElementPattern = InjectorPattern.DefaultCoax(18),
        };
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: true);

        var geom = gen.ToInjectorFaceGeometry();
        Assert.NotNull(geom);

        Assert.Equal(gen.Contour.ChamberRadius_mm,         geom!.ChamberRadius_mm);
        Assert.Equal(gen.Thermal.Stations[0].h_g_Wm2K,     geom.H_g_x0_Wm2K);
        Assert.Equal(gen.Thermal.Stations[0].AdiabaticWallTemp_K,    geom.T_aw_x0_K);
        Assert.Equal(gen.Thermal.Stations[0].EffectiveRecoveryTemp_K, geom.T_film_face_x0_K);
        Assert.Equal(gen.Conditions.PropellantPair,        geom.PropellantPair);
        Assert.Equal(gen.Conditions.CoolantInletTemp_K,    geom.CoolantInletTemp_K);
        Assert.Equal(gen.Conditions.CoolantInletPressure_Pa, geom.CoolantInletPressure_Pa);
        Assert.Equal(gen.Conditions.WallMaterialIndex,     geom.WallMaterialIndex);
        Assert.Equal(gen.Derived.OxidizerMassFlow_kgs,     geom.OxidizerMassFlow_kgs);
        Assert.Equal(gen.Derived.FuelMassFlow_kgs,         geom.FuelMassFlow_kgs);
        Assert.Equal(gen.Derived.TotalMassFlow_kgs,        geom.TotalMassFlow_kgs);
        Assert.Same(gen.InjectorPattern,                   geom.Pattern);
        Assert.Same(gen.InjectorSizing,                    geom.Sizing);
    }

    [Fact]
    public void EstimateViaAdapter_MatchesEstimateViaResult_BitIdentical()
    {
        // Behaviour-preservation: GenerateWith's call site already routes
        // through the adapter, so result.InjectorFace must equal
        // Estimate(result.ToInjectorFaceGeometry()). This pin defends
        // against a future refactor that calls Estimate via a different
        // path (e.g. a direct hand-built geometry vs. the adapter) and
        // accidentally drifts the result.
        var cond = new OperatingConditions
        {
            Thrust_N = 2_224.0, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
            CoolantInletTemp_K = 150, CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex = 1, PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 60,
            InjectorElementPattern = InjectorPattern.DefaultCoax(18),
        };
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: true);

        var geom = gen.ToInjectorFaceGeometry();
        Assert.NotNull(geom);
        var direct = InjectorFaceThermal.Estimate(geom!);

        Assert.NotNull(gen.InjectorFace);
        Assert.Equal(gen.InjectorFace!.TFace_K, direct.TFace_K);
        Assert.Equal(gen.InjectorFace.HeatFlux_Wm2, direct.HeatFlux_Wm2);
        Assert.Equal(gen.InjectorFace.BoreAreaFraction, direct.BoreAreaFraction);
    }

    // ──────────────── PH-36 (2026-04-29): per-pair oxidizer T ────────────────

    [Fact]
    public void PH36_DefaultOxidizerInjectionT_LoxPairsAt90K()
    {
        // Implemented LOX-based pairs share the LOX boiling point.
        Assert.Equal(90.18, InjectorFaceThermal.DefaultOxidizerInjectionT_K(PropellantPair.LOX_CH4), precision: 2);
        Assert.Equal(90.18, InjectorFaceThermal.DefaultOxidizerInjectionT_K(PropellantPair.LOX_H2),  precision: 2);
        Assert.Equal(90.18, InjectorFaceThermal.DefaultOxidizerInjectionT_K(PropellantPair.LOX_RP1), precision: 2);
    }

    [Fact]
    public void PH36_DefaultOxidizerInjectionT_StorablesAtRoomT()
    {
        // Storable hypergolics deliver oxidizer at room temperature.
        Assert.True(InjectorFaceThermal.DefaultOxidizerInjectionT_K(PropellantPair.N2O4_MMH) > 250.0);
        Assert.True(InjectorFaceThermal.DefaultOxidizerInjectionT_K(PropellantPair.H2O2_RP1) > 250.0);
    }

    [Fact]
    public void PH36_OxidizerInletTempOverride_ShiftsTPropAvg()
    {
        // Override > 0 short-circuits the per-pair default. Warmer oxidizer
        // (e.g. preburner-fed staged combustion) raises T_prop_avg, which
        // raises T_face. Lumped solver: cold-side reference T enters the
        // h_g · T_aw + h_back · T_prop_avg numerator linearly.
        var coldGeom = BuildSyntheticGeometry();   // OxidizerInletTemp_K = 0 → 90.18 K
        var warmGeom = coldGeom with { OxidizerInletTemp_K = 500.0 };

        var cold = InjectorFaceThermal.Estimate(coldGeom);
        var warm = InjectorFaceThermal.Estimate(warmGeom);

        Assert.True(warm.TPropAvg_K > cold.TPropAvg_K,
            $"warm-ox T_prop_avg ({warm.TPropAvg_K:F0} K) should exceed cold-ox "
          + $"({cold.TPropAvg_K:F0} K) when OxidizerInletTemp_K is bumped 90 → 500 K.");
        // T_face also shifts upward — coupled through the h_g·T_aw +
        // h_back·T_prop_avg equilibrium.
        Assert.True(warm.TFace_K > cold.TFace_K);
    }

    // ──────────────── PH-35 (2026-04-29): face material T-limit override ────────────────

    [Fact]
    public void PH35_DefaultMaxServiceT_Is1200K()
    {
        // No override → result.MaxServiceTemp_K = DefaultInjectorFaceMaxTemp_K (1200 K).
        var geom = BuildSyntheticGeometry();
        var result = InjectorFaceThermal.Estimate(geom);
        Assert.Equal(InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K, result.MaxServiceTemp_K, precision: 3);
        Assert.Equal(1200.0, result.MaxServiceTemp_K, precision: 3);
    }

    [Fact]
    public void PH35_OverridePropagatesToResult()
    {
        // SS316L brazed face on a CuCrZr liner runs ~1100 K limit. Override
        // surfaces on the result so FeasibilityGate's INJECTOR_FACE_T_EXCEEDED
        // checks against the design-specific limit, not the IN625 default.
        var geom = BuildSyntheticGeometry() with { InjectorFaceMaxTemp_K_Override = 1100.0 };
        var result = InjectorFaceThermal.Estimate(geom);
        Assert.Equal(1100.0, result.MaxServiceTemp_K, precision: 3);
    }

    // ──────────────── Z3-F4 (2026-04-29): Mach-aware mixing-layer eff ────────────────

    [Fact]
    public void Z3F4_LowMachReturnsBaseEffectiveness()
    {
        // M ≤ M_ref (0.10) → no attenuation; result equals the legacy
        // per-element-type baseline.
        Assert.Equal(0.65, InjectorFaceThermal.MixingLayerEffectivenessFor("Coax", 0.05), precision: 4);
        Assert.Equal(0.65, InjectorFaceThermal.MixingLayerEffectivenessFor("Coax", 0.10), precision: 4);
        Assert.Equal(0.80, InjectorFaceThermal.MixingLayerEffectivenessFor("Pintle", 0.05), precision: 4);
    }

    [Fact]
    public void Z3F4_HighMachAttenuatesEffectiveness()
    {
        // M = 0.30 → factor = 1 − 0.5·0.20 = 0.90; η_Coax ≈ 0.585.
        // M = 0.50 → factor = 1 − 0.5·0.40 = 0.80; η_Coax ≈ 0.520.
        double eta_M30 = InjectorFaceThermal.MixingLayerEffectivenessFor("Coax", 0.30);
        double eta_M50 = InjectorFaceThermal.MixingLayerEffectivenessFor("Coax", 0.50);
        Assert.Equal(0.65 * 0.90, eta_M30, precision: 3);
        Assert.Equal(0.65 * 0.80, eta_M50, precision: 3);
        Assert.True(eta_M50 < eta_M30);
    }

    [Fact]
    public void Z3F4_PathologicalMachClampsAtFloor()
    {
        // M → 1.0 attenuation = 1 − 0.5·0.9 = 0.55, but the floor 0.5 clamps.
        // M → 5.0 stays at floor.
        double eta_M50 = InjectorFaceThermal.MixingLayerEffectivenessFor("Coax", 5.0);
        Assert.Equal(0.65 * 0.50, eta_M50, precision: 3);
    }

    [Fact]
    public void Z3F4_GeomChamberMachShiftsTFace()
    {
        // High-M geom (small ε_c) attenuates η → less film protection
        // → higher T_aw entering equilibrium → higher T_face.
        var lowMach = BuildSyntheticGeometry() with { ChamberMach = 0.10 };
        var highMach = BuildSyntheticGeometry() with { ChamberMach = 0.40 };

        var lowResult = InjectorFaceThermal.Estimate(lowMach);
        var highResult = InjectorFaceThermal.Estimate(highMach);

        Assert.True(highResult.TFace_K > lowResult.TFace_K,
            $"high-Mach geom (M=0.40) should yield higher T_face than low-Mach "
          + $"(M=0.10) due to η attenuation. Got high={highResult.TFace_K:F1} K vs "
          + $"low={lowResult.TFace_K:F1} K.");
    }

    [Fact]
    public void PH35_OverrideFlowsThroughGenerateWithToFaceResult()
    {
        // End-to-end: setting OperatingConditions.InjectorFaceMaxTemp_K_Override
        // makes its way through ToInjectorFaceGeometry → Estimate →
        // result.MaxServiceTemp_K → FeasibilityGate.
        var cond = new OperatingConditions
        {
            Thrust_N = 2_224.0, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
            CoolantInletTemp_K = 150, CoolantInletPressure_Pa = 12e6,
            WallMaterialIndex = 1, PropellantPair = PropellantPair.LOX_CH4,
            InjectorFaceMaxTemp_K_Override = 1050.0,   // tighter SS304-class limit
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 60,
            InjectorElementPattern = InjectorPattern.DefaultCoax(18),
        };
        var gen = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true, skipMfgAnalysis: true);

        Assert.NotNull(gen.InjectorFace);
        Assert.Equal(1050.0, gen.InjectorFace!.MaxServiceTemp_K, precision: 3);
    }
}
