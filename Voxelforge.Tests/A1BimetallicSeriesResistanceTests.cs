// A1BimetallicSeriesResistanceTests.cs — coverage for the GRCop-42 /
// Inconel-625 composite material's revised properties (A1 / ID-5,
// 2026-04-27, with Z1 E-modulus hot-fix 2026-04-28). Pre-A1 the
// composite used area-weighted blends which over-credited the high-
// conductivity / high-strength layer. Post-A1 + Z1:
//   • conductivity → SERIES (heat flow normal to wall, resistances stack)
//   • E modulus    → PARALLEL / Voigt (hoop strain compatibility, stiffnesses add)
//   • yield        → MIN of layers (worst ply governs)
// All at the assumed 25 % liner / 75 % jacket thickness ratio.
//
// Z3-M2 (2026-04-29): bond-zone shear stress advisory tests appended.

using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Voxelforge.Structure;

namespace Voxelforge.Tests;

public class A1BimetallicSeriesResistanceTests
{
    private const double LinerFraction = 0.25;
    private const double JacketFraction = 0.75;

    // Bulk layer values used in the composition (must match WallMaterial.cs
    // constants — these are the input to the series / parallel formulas).
    private const double K_GRCop42_Cold = 326.0;
    private const double K_GRCop42_Hot  = 285.0;
    private const double K_IN625_Cold   = 10.0;
    private const double K_IN625_Hot    = 19.0;
    private const double SigmaY_GRCop42_Cold = 230.0;
    private const double SigmaY_GRCop42_Hot  = 180.0;
    private const double SigmaY_IN625_Cold   = 520.0;
    private const double SigmaY_IN625_Hot    = 450.0;
    private const double E_GRCop42_Cold = 127.0;
    private const double E_GRCop42_Hot  = 100.0;
    private const double E_IN625_Cold   = 208.0;
    private const double E_IN625_Hot    = 165.0;

    // Heat flow is NORMAL to the wall (each layer is a thermal resistance
    // in series along the heat-flow direction).
    private static double SeriesK(double t1, double k1, double t2, double k2)
        => 1.0 / (t1 / k1 + t2 / k2);

    // Hoop strain is ALONG the wall (both layers elongate together by the
    // same ε_θ; force balance P·r = ε_θ·(E₁·t₁ + E₂·t₂) ⇒ stiffnesses add
    // by area-weighted layer fraction). Voigt average.
    private static double ParallelE(double t1, double e1, double t2, double e2)
        => t1 * e1 + t2 * e2;

    [Fact]
    public void GRCop42_Inconel625_ConductivityCold_MatchesSeriesStack()
    {
        var w = WallMaterials.GRCop42_Inconel625();
        double expected = SeriesK(LinerFraction, K_GRCop42_Cold, JacketFraction, K_IN625_Cold);
        Assert.Equal(expected, w.ConductivityCold_WmK, precision: 3);
        // Sanity: series result is dominated by the LOW-k IN625 jacket;
        // expect ≈ 13 W/m·K, NOT 263 like the old parallel blend.
        Assert.InRange(w.ConductivityCold_WmK, 12.0, 14.0);
    }

    [Fact]
    public void GRCop42_Inconel625_ConductivityHot_MatchesSeriesStack()
    {
        var w = WallMaterials.GRCop42_Inconel625();
        double expected = SeriesK(LinerFraction, K_GRCop42_Hot, JacketFraction, K_IN625_Hot);
        Assert.Equal(expected, w.ConductivityHot_WmK, precision: 3);
        Assert.InRange(w.ConductivityHot_WmK, 22.0, 27.0);
    }

    [Fact]
    public void GRCop42_Inconel625_YieldStrength_IsMinimumOfLayers()
    {
        var w = WallMaterials.GRCop42_Inconel625();
        // Pre-A1: weighted blend → ~462 MPa cold, ~396 MPa hot.
        // Post-A1: min-of-layers → 230 MPa cold (GRCop-42), 180 MPa hot.
        Assert.Equal(System.Math.Min(SigmaY_GRCop42_Cold, SigmaY_IN625_Cold),
                     w.YieldStrengthCold_MPa);
        Assert.Equal(System.Math.Min(SigmaY_GRCop42_Hot, SigmaY_IN625_Hot),
                     w.YieldStrengthHot_MPa);
    }

    [Fact]
    public void GRCop42_Inconel625_ElasticModulus_MatchesParallelStack()
    {
        // Z1 hot-fix (2026-04-28): bonded composite cylinder under hoop
        // tension uses the Voigt / parallel average, NOT series. Force
        // balance: P·r = σ_liner·t_liner + σ_jacket·t_jacket
        //              = ε_θ·(E_liner·t_liner + E_jacket·t_jacket)
        // ⇒ E_eff = f_liner·E_liner + f_jacket·E_jacket.
        var w = WallMaterials.GRCop42_Inconel625();
        double expectedCold = ParallelE(LinerFraction, E_GRCop42_Cold, JacketFraction, E_IN625_Cold);
        double expectedHot  = ParallelE(LinerFraction, E_GRCop42_Hot,  JacketFraction, E_IN625_Hot);
        Assert.Equal(expectedCold, w.ElasticModulusCold_GPa, precision: 3);
        Assert.Equal(expectedHot,  w.ElasticModulusHot_GPa,  precision: 3);
        // Sanity: parallel result is HIGHER than what series gave us
        // pre-Z1 (~179 / 142 GPa) — closer to the jacket-dominated bulk
        // since IN625 has higher E and 75 % thickness.
        Assert.InRange(w.ElasticModulusCold_GPa, 185.0, 190.0); // 187.75
        Assert.InRange(w.ElasticModulusHot_GPa,  146.0, 152.0); // 148.75
    }

    [Fact]
    public void GRCop42_Inconel625_TempInterpolationStillMonotonic()
    {
        // The corrected k_eff is much LOWER than pre-A1 but should still
        // increase with temperature (IN625's k rises faster with T than
        // GRCop-42's drops, so the series result is rising).
        var w = WallMaterials.GRCop42_Inconel625();
        Assert.True(w.ConductivityHot_WmK > w.ConductivityCold_WmK,
            $"k(900K)={w.ConductivityHot_WmK} should be > k(300K)={w.ConductivityCold_WmK}");
        // Yield decreases with T (both layers do, so min does).
        Assert.True(w.YieldStrengthHot_MPa < w.YieldStrengthCold_MPa);
        // E decreases with T.
        Assert.True(w.ElasticModulusHot_GPa < w.ElasticModulusCold_GPa);
    }

    [Fact]
    public void GRCop42_Inconel625_AreaWeightedBlends_PreservedForBulkProps()
    {
        // Density / specific heat / cost / melting point / CTE — these
        // remain area-weighted blends because they're bulk integrals,
        // not stack-direction physics.
        var w = WallMaterials.GRCop42_Inconel625();
        Assert.InRange(w.Density_kgm3, 8500, 8550);   // ≈ 8519
        Assert.Equal(465, w.SpecificHeat_Jkg);
        Assert.Equal(11.0, w.PrintCostPerCm3_USD, precision: 6);
        Assert.Equal(1515, w.MeltingPoint_K);
        Assert.Equal(0.25 * 17.5e-6 + 0.75 * 12.8e-6, w.CTE_perK, precision: 9);
    }

    [Fact]
    public void GRCop42_Inconel625_StillRegisteredInWallMaterialsAll()
    {
        // Sanity: the entry hasn't been removed from the All array.
        Assert.Contains(WallMaterials.All, w => w.Name.Contains("bimetallic"));
    }

    // Z2 #10 (2026-04-29): linerFraction is now a method parameter.
    // Default 0.25 preserves the historical pre-Z2.10 ratio bit-identically;
    // alternative ratios recompute every composite property at the supplied
    // fraction. These tests pin the parametric behaviour so a future
    // simplification regression is caught at unit-test time.

    [Theory]
    [InlineData(0.20)]
    [InlineData(0.25)]   // historical default
    [InlineData(0.40)]
    [InlineData(0.50)]
    public void GRCop42_Inconel625_Conductivity_VariesByLinerFraction(double linerFraction)
    {
        double jacketFraction = 1.0 - linerFraction;
        var w = WallMaterials.GRCop42_Inconel625(linerFraction);
        double expectedCold = SeriesK(linerFraction, K_GRCop42_Cold,
                                       jacketFraction, K_IN625_Cold);
        double expectedHot  = SeriesK(linerFraction, K_GRCop42_Hot,
                                       jacketFraction, K_IN625_Hot);
        Assert.Equal(expectedCold, w.ConductivityCold_WmK, precision: 3);
        Assert.Equal(expectedHot,  w.ConductivityHot_WmK,  precision: 3);
    }

    [Theory]
    [InlineData(0.20)]
    [InlineData(0.25)]   // historical default
    [InlineData(0.40)]
    [InlineData(0.50)]
    public void GRCop42_Inconel625_ElasticModulus_VariesByLinerFraction(double linerFraction)
    {
        double jacketFraction = 1.0 - linerFraction;
        var w = WallMaterials.GRCop42_Inconel625(linerFraction);
        double expectedCold = ParallelE(linerFraction, E_GRCop42_Cold,
                                         jacketFraction, E_IN625_Cold);
        double expectedHot  = ParallelE(linerFraction, E_GRCop42_Hot,
                                         jacketFraction, E_IN625_Hot);
        Assert.Equal(expectedCold, w.ElasticModulusCold_GPa, precision: 3);
        Assert.Equal(expectedHot,  w.ElasticModulusHot_GPa,  precision: 3);
    }

    [Fact]
    public void GRCop42_Inconel625_Yield_IsRatioInvariant()
    {
        // Yield = min(layer_yields). Doesn't depend on the ratio because
        // GRCop-42 always sets the floor (230 MPa cold < 520 MPa IN625;
        // 180 MPa hot < 450 MPa IN625). Pin invariance across fractions.
        foreach (double linerFraction in new[] { 0.10, 0.25, 0.40, 0.75 })
        {
            var w = WallMaterials.GRCop42_Inconel625(linerFraction);
            Assert.Equal(SigmaY_GRCop42_Cold, w.YieldStrengthCold_MPa);
            Assert.Equal(SigmaY_GRCop42_Hot,  w.YieldStrengthHot_MPa);
        }
    }

    [Theory]
    [InlineData(0.0)]    // boundary: pure jacket — degenerate
    [InlineData(-0.1)]   // negative
    [InlineData(1.0)]    // boundary: pure liner — degenerate
    [InlineData(1.5)]    // above 1
    public void GRCop42_Inconel625_RejectsOutOfRangeLinerFraction(double linerFraction)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => WallMaterials.GRCop42_Inconel625(linerFraction));
    }

    [Fact]
    public void GRCop42_Inconel625_DefaultParameter_BitIdenticalToExplicit025()
    {
        // Back-compat regression guard: the no-arg default must produce
        // the same composite as explicit 0.25.
        var defaultMat  = WallMaterials.GRCop42_Inconel625();
        var explicitMat = WallMaterials.GRCop42_Inconel625(0.25);
        Assert.Equal(defaultMat.ConductivityCold_WmK,  explicitMat.ConductivityCold_WmK,  precision: 9);
        Assert.Equal(defaultMat.ConductivityHot_WmK,   explicitMat.ConductivityHot_WmK,   precision: 9);
        Assert.Equal(defaultMat.YieldStrengthCold_MPa, explicitMat.YieldStrengthCold_MPa);
        Assert.Equal(defaultMat.YieldStrengthHot_MPa,  explicitMat.YieldStrengthHot_MPa);
        Assert.Equal(defaultMat.ElasticModulusCold_GPa, explicitMat.ElasticModulusCold_GPa, precision: 9);
        Assert.Equal(defaultMat.ElasticModulusHot_GPa,  explicitMat.ElasticModulusHot_GPa,  precision: 9);
        Assert.Equal(defaultMat.CTE_perK, explicitMat.CTE_perK, precision: 12);
        Assert.Equal(defaultMat.Density_kgm3, explicitMat.Density_kgm3);
    }

    // ──────────────── Z3-m1 (2026-04-29): per-layer T conductivity ────────────────

    [Fact]
    public void Z3m1_PureMaterialHasZeroLinerFraction()
    {
        // Pure-material walls advertise LinerFraction = 0 so the
        // RegenCoolingSolver per-layer-T dispatch stays on the legacy
        // single-T path for them.
        Assert.Equal(0.0, WallMaterials.GRCop42.LinerFraction, precision: 6);
        Assert.Equal(0.0, WallMaterials.CuCrZr.LinerFraction, precision: 6);
        Assert.Equal(0.0, WallMaterials.Inconel625.LinerFraction, precision: 6);
        Assert.Equal(0.0, WallMaterials.Inconel718.LinerFraction, precision: 6);
    }

    [Fact]
    public void Z3m1_BimetallicCarriesLinerFractionFromConstructor()
    {
        // The bimetallic composite forwards the construction parameter
        // so the RegenCoolingSolver per-layer-T helper can split the
        // wall thickness correctly.
        Assert.Equal(0.25, WallMaterials.GRCop42_Inconel625().LinerFraction, precision: 6);
        Assert.Equal(0.40, WallMaterials.GRCop42_Inconel625(0.40).LinerFraction, precision: 6);
        Assert.Equal(0.10, WallMaterials.GRCop42_Inconel625(0.10).LinerFraction, precision: 6);
    }

    [Fact]
    public void Z3m1_PureMaterialConductivityAt_PreservesPreZ3Behaviour()
    {
        // Pure-material `ConductivityAt(T)` is unchanged — same Lerp
        // between 300 K cold + 900 K hot anchors. The Z3-m1 dispatch
        // only kicks in for bimetallic walls.
        var grcop = WallMaterials.GRCop42;
        Assert.Equal(grcop.ConductivityCold_WmK, grcop.ConductivityAt(300), precision: 6);
        Assert.Equal(grcop.ConductivityHot_WmK,  grcop.ConductivityAt(900), precision: 6);
        // Mid-range linear interpolation
        double mid = 0.5 * (grcop.ConductivityCold_WmK + grcop.ConductivityHot_WmK);
        Assert.Equal(mid, grcop.ConductivityAt(600), precision: 3);
    }

    // ──────────────── Z3-M2 (2026-04-29): bond-zone shear stress ─────────────

    // GRCop-42 liner CTE vs IN625 jacket CTE.
    private const double CTE_GRCop42 = 17.5e-6;   // /K
    private const double CTE_IN625   = 12.8e-6;   // /K

    // Build minimal solver outputs with a single station at the specified
    // wall temperatures. Mirrors the helper pattern in
    // StructuralCheckSprintGPrimeTests.
    private static RegenSolverOutputs MakeBimetallicSolver(
        double Twg_K, double Twc_K, double Tcoolant_K = 290, double R_mm = 20)
    {
        var station = new StationResult(
            Index: 0, X_mm: 0, R_mm: R_mm, AreaRatioToThroat: 1.0, Mach: 0.3,
            StaticTemp_K: 2500, AdiabaticWallTemp_K: 3000,
            EffectiveRecoveryTemp_K: 3000, FilmEffectiveness: 0,
            HeatFlux_Wm2: 40e6, h_g_Wm2K: 25000, h_c_Wm2K: 30000,
            GasSideWallTemp_K: Twg_K, CoolantSideWallTemp_K: Twc_K,
            WallRadialProfile_K: new[] { Twg_K, Twc_K },
            AxialConductionFlux_Wm2: 0,
            CoolantBulkTemp_K: Tcoolant_K, CoolantBulkPressure_Pa: 15e6,
            CoolantVelocity_ms: 50, Reynolds: 1e6, PrandtlBulk: 0.7,
            ChannelWidth_mm: 3, ChannelHeight_mm: 2,
            HydraulicDiameter_mm: 2.4, PressureGradient_Pam: 1e5);
        return new RegenSolverOutputs(
            Stations: new[] { station },
            PeakGasSideWallT_K: Twg_K, PeakCoolantSideWallT_K: Twc_K,
            PeakStationIndex: 0, CoolantInletT_K: 200,
            CoolantOutletT_K: Tcoolant_K, CoolantInletP_Pa: 16e6,
            CoolantOutletP_Pa: 14e6, CoolantPressureDrop_Pa: 2e6,
            TotalHeatLoad_W: 20e6, TotalWettedArea_mm2: 5e4,
            ThroatHeatFlux_Wm2: 40e6, WallTempExceedsLimit: false,
            WallMarginK: 200, FilmMassFlow_kgs: 0, IspPenaltyFraction: 0,
            AxialConductionRMS_Wm2: 0,
            Diagnostics: new SolverDiagnostics(0, 0, 0, 0, true),
            Warnings: System.Array.Empty<string>());
    }

    [Fact]
    public void Z3M2_SingleMaterial_BondZoneShear_IsZero()
    {
        // No jacketMaterial supplied → bond-zone shear not computed; stays 0.
        var solver = MakeBimetallicSolver(Twg_K: 900, Twc_K: 300);
        var summary = StructuralCheck.Evaluate(
            solver: solver,
            wall: WallMaterials.GRCop42,
            gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 6.9e6,
            gasGamma: 1.2);
        Assert.Equal(0.0, summary.BondZoneShearStress_MPa, precision: 9);
        Assert.Equal(0.0, summary.BondZoneShearRatio,       precision: 9);
    }

    [Fact]
    public void Z3M2_Bimetallic_HighDeltaT_BondZoneShearExceedsThreshold()
    {
        // GRCop-42 liner + IN625 jacket at T_wg=900 K, T_wc=300 K (ΔT=600 K).
        // τ = |Δα| × ΔT × E_eff ≈ 4.7e-6 × 600 × ~146 GPa × 1000 ≈ 412 MPa.
        // σ_y_composite × 0.5 ≈ ~217 MPa → ratio > 1 → advisory fires.
        var solver  = MakeBimetallicSolver(Twg_K: 900, Twc_K: 300);
        var liner   = WallMaterials.GRCop42;
        var jacket  = WallMaterials.Inconel625;
        var summary = StructuralCheck.Evaluate(
            solver: solver,
            wall: liner,
            gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 6.9e6,
            gasGamma: 1.2,
            outerJacketThickness_mm: 3.0,
            jacketMaterial: jacket);
        // Bond-zone shear must be positive and exceed the threshold.
        Assert.True(summary.BondZoneShearStress_MPa > 0,
            $"Expected positive bond-zone shear; got {summary.BondZoneShearStress_MPa:F1} MPa");
        Assert.True(summary.BondZoneShearRatio > 1.0,
            $"Expected ratio > 1 (advisory fires); got ratio {summary.BondZoneShearRatio:F2}");
        // Sanity-range: formula gives ~390–430 MPa for these inputs.
        Assert.InRange(summary.BondZoneShearStress_MPa, 350, 500);
    }

    [Fact]
    public void Z3M2_Bimetallic_LowDeltaT_BondZoneShearBelowThreshold()
    {
        // Same bimetallic pair but ΔT = 40 K (near-isothermal operation).
        // τ ≈ 4.7e-6 × 40 × ~166 GPa × 1000 ≈ 31 MPa.
        // σ_y_composite × 0.5 ≈ ~223 MPa → ratio ≈ 0.14 → advisory silent.
        var solver  = MakeBimetallicSolver(Twg_K: 340, Twc_K: 300);
        var liner   = WallMaterials.GRCop42;
        var jacket  = WallMaterials.Inconel625;
        var summary = StructuralCheck.Evaluate(
            solver: solver,
            wall: liner,
            gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 6.9e6,
            gasGamma: 1.2,
            outerJacketThickness_mm: 3.0,
            jacketMaterial: jacket);
        Assert.True(summary.BondZoneShearStress_MPa > 0,
            "Even low-ΔT bimetallic should compute a non-zero shear.");
        Assert.True(summary.BondZoneShearRatio < 1.0,
            $"Expected ratio < 1 (advisory silent); got ratio {summary.BondZoneShearRatio:F2}");
    }

    [Fact]
    public void Z3M2_GateKindIsAdvisoryHeuristic()
    {
        Assert.Equal(GateKind.AdvisoryHeuristic,
                     FeasibilityGate.GetGateKind("BIMETALLIC_BOND_ZONE_SHEAR"));
    }

    [Fact]
    public void Z3M2_Gate_FiresWhenRatioAboveOne()
    {
        // Direct injection of BondZoneShearStress > 0 and ratio > 1 should
        // produce exactly one BIMETALLIC_BOND_ZONE_SHEAR violation.
        // We reuse the high-ΔT solver result and build a minimal
        // RegenGenerationResult via `with`-injection from a real GenerateWith.
        // Use a cached base so the test avoids re-running the thermal solver.
        var solver  = MakeBimetallicSolver(Twg_K: 900, Twc_K: 300);
        var summary = StructuralCheck.Evaluate(
            solver: solver,
            wall: WallMaterials.GRCop42,
            gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 6.9e6,
            gasGamma: 1.2,
            outerJacketThickness_mm: 3.0,
            jacketMaterial: WallMaterials.Inconel625);
        // Confirm pre-condition: ratio > 1 so the gate can fire.
        Assert.True(summary.BondZoneShearRatio > 1.0);

        // Inject the computed summary into a safe base result so all other
        // gates are clear; the only violation must be BIMETALLIC_BOND_ZONE_SHEAR.
        var safe = Z3M2SafeBase();
        var result = safe with { Stress = safe.Stress with
        {
            BondZoneShearStress_MPa = summary.BondZoneShearStress_MPa,
            BondZoneShearRatio      = summary.BondZoneShearRatio,
        }};
        var gate = FeasibilityGate.Evaluate(result);
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "BIMETALLIC_BOND_ZONE_SHEAR");
    }

    [Fact]
    public void Z3M2_Gate_SilentWhenSingleMaterial()
    {
        // BondZoneShearStress_MPa == 0 → gate stays silent on single-material.
        var safe = Z3M2SafeBase();
        var result = safe with { Stress = safe.Stress with
        {
            BondZoneShearStress_MPa = 0.0,
            BondZoneShearRatio      = 0.0,
        }};
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "BIMETALLIC_BOND_ZONE_SHEAR");
    }

    // Cached gate-clean base result for gate injection tests.
    private static RegenGenerationResult? _z3m2SafeBase;
    private static readonly object _z3m2SafeLock = new();
    private static RegenGenerationResult Z3M2SafeBase()
    {
        lock (_z3m2SafeLock)
        {
            if (_z3m2SafeBase != null) return _z3m2SafeBase;
            var cond = new OperatingConditions
            {
                Thrust_N               = 2224.0,
                ChamberPressure_Pa     = 6.9e6,
                MixtureRatio           = 3.3,
                CoolantInletTemp_K     = 150.0,
                CoolantInletPressure_Pa = 12e6,
                WallMaterialIndex      = 0,  // GRCop-42
                PropellantPair         = Combustion.PropellantPair.LOX_CH4,
            };
            var r = RegenChamberOptimization.GenerateWith(
                cond,
                new RegenChamberDesign
                {
                    IncludeManifolds      = false,
                    IncludePorts          = false,
                    IncludeInjectorFlange = false,
                    ContourStationCount   = 60,
                });
            var mat = WallMaterials.All[cond.WallMaterialIndex];
            var ch4 = Coolant.MethaneFluid.Instance;
            _z3m2SafeBase = r with
            {
                Thermal = r.Thermal with
                {
                    PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,
                    WallTempExceedsLimit = false,
                    CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0,
                },
                Stress = r.Stress with
                {
                    MinSafetyFactor         = 2.5,
                    YieldExceeded           = false,
                    BondZoneShearStress_MPa = 0.0,
                    BondZoneShearRatio      = 0.0,
                },
                Manufacturing = r.Manufacturing with
                {
                    MinFeatureSize_mm = 0.55,
                    FeatureSizeOK     = true,
                },
                Stability = r.Stability with
                {
                    Composite       = Combustion.Stability.StabilityRating.Pass,
                    CompositeReason = "z3m2-test",
                },
                IgniterType   = Geometry.IgniterType.SparkTorch,
                Contour       = r.Contour with { CharacteristicLength_m = 1.10 },
                BurstMarginFactor = 3.0,
            };
            return _z3m2SafeBase;
        }
    }
}
