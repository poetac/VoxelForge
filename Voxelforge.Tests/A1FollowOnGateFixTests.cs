// A1FollowOnGateFixTests.cs — Discipline tests for the two gate corrections
// shipped as A1-follow-on (2026-04-28):
//
//   Fix 1 — INJECTOR_FACE_T_EXCEEDED now fires at a fixed 1200 K (IN625/SS
//   injector face) rather than the Cu-alloy inner liner MaxServiceTemp
//   (1000 K for GRCop-42, 800 K for CuCrZr).  The old behaviour caused
//   93 % of SA candidates to fail the gate even though their predicted face
//   temperatures are physically compatible with a typical IN625 face plate.
//
//   Fix 2 — StructuralCheck.Evaluate now accepts an optional jacketMaterial
//   parameter.  When provided, the yield comparison uses a load-weighted
//   composite yield:
//     σ_y_eff = (σ_y_inner × t_inner + σ_y_jacket × t_jacket) / t_eff
//   The jacket temperature is approximated by the local coolant bulk temp.
//   Without jacketMaterial the pre-fix single-wall behaviour is preserved
//   bit-identically (backwards-compat).
//
// References:
//   SpaceX Merlin injector face material — IN625 per FAA/FCC filings.
//   Sutton §6.4 — injector face plate material selection for LRE.
//   Hibbeler "Mechanics of Materials" 10e §8.3 — composite cylinder yield.

using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Voxelforge.Structure;
using Xunit;

namespace Voxelforge.Tests;

public class A1FollowOnGateFixTests
{
    // ─────────────────────────────────────────────────────────────────────
    //  Fix 1 — Injector face temperature gate
    // ─────────────────────────────────────────────────────────────────────

    // Shared lazily-generated base result used for face-gate injection tests.
    // Uses the same default conditions as FeasibilityGateTests so the result
    // is already partially gate-clean; we patch the specific fields we care
    // about with `with` expressions.
    private static RegenGenerationResult? _baseCache;
    private static readonly object _baseLock = new();

    private static RegenGenerationResult BaseResult()
    {
        lock (_baseLock)
            return _baseCache ??= RegenChamberOptimization.GenerateWith(
                new OperatingConditions
                {
                    Thrust_N               = 2224.0,
                    ChamberPressure_Pa     = 6.9e6,
                    MixtureRatio           = 3.3,
                    CoolantInletTemp_K     = 150.0,
                    CoolantInletPressure_Pa = 12e6,
                    WallMaterialIndex      = 0,   // GRCop-42 (MaxServiceTemp = 1000 K)
                    PropellantPair         = PropellantPair.LOX_CH4,
                },
                new RegenChamberDesign
                {
                    IncludeManifolds      = false,
                    IncludePorts          = false,
                    IncludeInjectorFlange = false,
                    ContourStationCount   = 60,
                });
    }

    // Build a gate-evaluable result with all non-face gates cleared and the
    // face temperature set to a specified value.
    private static RegenGenerationResult MakeFaceResult(double tFace_K)
    {
        var r   = BaseResult();
        var mat = WallMaterials.All[0];  // GRCop-42, MaxServiceTemp = 1000 K
        var ch4 = MethaneFluid.Instance;

        return r with
        {
            Thermal = r.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,
                WallTempExceedsLimit = false,
                CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0,
            },
            Stress = r.Stress with
            {
                MinSafetyFactor = 2.5,
                YieldExceeded   = false,
            },
            Manufacturing = r.Manufacturing with
            {
                MinFeatureSize_mm = 0.55,
                FeatureSizeOK     = true,
            },
            Stability = r.Stability with
            {
                Composite       = StabilityRating.Pass,
                CompositeReason = "test-injected",
            },
            IgniterType = Geometry.IgniterType.SparkTorch,
            Contour = r.Contour with { CharacteristicLength_m = 1.10 },
            InjectorFace = new InjectorFaceResult(
                TFace_K:          tFace_K,
                TAwCore_K:        2800,
                TPropAvg_K:       150,
                HeatFlux_Wm2:     5e6,
                HGasSide_Wm2K:    8000,
                HPropSide_Wm2K:   500,
                FaceArea_cm2:     10.0,
                BoreAreaFraction: 0.08,
                Method:           "test",
                Warnings:         System.Array.Empty<string>()),
        };
    }

    [Fact]
    public void InjectorFaceGate_DoesNotFire_At1100K_ForGRCop42Wall()
    {
        // GRCop-42 MaxServiceTemp = 1000 K — the old gate would fire at
        // 1100 K.  The new fixed 1200 K limit means 1100 K passes.
        var gate = FeasibilityGate.Evaluate(MakeFaceResult(1100));
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "INJECTOR_FACE_T_EXCEEDED");
    }

    [Fact]
    public void InjectorFaceGate_Fires_Above1200K()
    {
        // 1250 K > 1200 K → gate must fire.
        var gate = FeasibilityGate.Evaluate(MakeFaceResult(1250));
        Assert.Contains(gate.Violations,
            v => v.ConstraintId == "INJECTOR_FACE_T_EXCEEDED");
    }

    [Fact]
    public void InjectorFaceGate_ViolationLimit_Reported_As1200K()
    {
        // The Limit field in the violation must be the new 1200 K threshold
        // so the diagnostic message is correct.
        var gate = FeasibilityGate.Evaluate(MakeFaceResult(1300));
        var v = Assert.Single(gate.Violations,
            x => x.ConstraintId == "INJECTOR_FACE_T_EXCEEDED");
        Assert.Equal(1200.0, v.Limit, precision: 0);
    }

    [Fact]
    public void InjectorFaceGate_DoesNotFire_JustBelow1200K()
    {
        // 1199 K < 1200 K → gate must not fire.
        var gate = FeasibilityGate.Evaluate(MakeFaceResult(1199));
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "INJECTOR_FACE_T_EXCEEDED");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Fix 2 — StructuralCheck composite yield
    //
    //  Build synthetic solver outputs (no real solver run needed) following
    //  the same pattern as StructuralCheckSprintGPrimeTests.
    // ─────────────────────────────────────────────────────────────────────

    private static StationResult MakeStation(int idx, double R_mm, double mach,
        double pCoolant_Pa, double T_bulk_K = 290,
        double Twg_K = 800, double Twc_K = 600)
        => new(
            Index: idx, X_mm: idx * 10.0, R_mm: R_mm,
            AreaRatioToThroat: 1.0, Mach: mach,
            StaticTemp_K: 2500, AdiabaticWallTemp_K: 3500,
            EffectiveRecoveryTemp_K: 3500, FilmEffectiveness: 0,
            HeatFlux_Wm2: 50e6, h_g_Wm2K: 35000, h_c_Wm2K: 50000,
            GasSideWallTemp_K: Twg_K, CoolantSideWallTemp_K: Twc_K,
            WallRadialProfile_K: new[] { Twg_K, Twc_K },
            AxialConductionFlux_Wm2: 0,
            CoolantBulkTemp_K: T_bulk_K,
            CoolantBulkPressure_Pa: pCoolant_Pa,
            CoolantVelocity_ms: 50, Reynolds: 1e6, PrandtlBulk: 0.7,
            ChannelWidth_mm: 3, ChannelHeight_mm: 2,
            HydraulicDiameter_mm: 2.4, PressureGradient_Pam: 1e5);

    private static RegenSolverOutputs MakeOutputs(StationResult[] stations)
        => new(
            Stations: stations,
            PeakGasSideWallT_K: 800, PeakCoolantSideWallT_K: 600,
            PeakStationIndex: 0, CoolantInletT_K: 25, CoolantOutletT_K: 350,
            CoolantInletP_Pa: 16e6, CoolantOutletP_Pa: 5e6,
            CoolantPressureDrop_Pa: 4e6, TotalHeatLoad_W: 30e6,
            TotalWettedArea_mm2: 1e5, ThroatHeatFlux_Wm2: 50e6,
            WallTempExceedsLimit: false, WallMarginK: 300,
            FilmMassFlow_kgs: 0, IspPenaltyFraction: 0,
            AxialConductionRMS_Wm2: 0,
            Diagnostics: new SolverDiagnostics(0, 0, 0, 0, true),
            Warnings: System.Array.Empty<string>());

    [Fact]
    public void CompositeYield_IncreasesMinSafetyFactor_WhenJacketProvided()
    {
        // Station: large R + high coolant pressure → hoop-dominated in
        // inner-liner-only mode.  IN625 jacket has ~2.7× GRCop-42 yield,
        // so composite SF must be significantly higher.
        var s = MakeStation(0, R_mm: 100, mach: 0.0, pCoolant_Pa: 12e6,
            T_bulk_K: 280, Twg_K: 750, Twc_K: 600);
        var outputs = MakeOutputs(new[] { s });
        double Pc = 7e6;

        var singleWall = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: Pc, outerJacketThickness_mm: 2.5,
            gasGamma: 1.2);

        var compositeWall = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: Pc, outerJacketThickness_mm: 2.5,
            gasGamma: 1.2, jacketMaterial: WallMaterials.Inconel625);

        Assert.True(compositeWall.MinSafetyFactor > singleWall.MinSafetyFactor,
            $"Composite SF {compositeWall.MinSafetyFactor:F3} should exceed "
          + $"single-wall SF {singleWall.MinSafetyFactor:F3}.");
    }

    [Fact]
    public void CompositeYield_NullJacketMaterial_PreservesLegacyBehavior()
    {
        // jacketMaterial: null (default) → bit-identical to calling without
        // the parameter (backwards-compat contract).
        var s = MakeStation(0, R_mm: 90, mach: 0.5, pCoolant_Pa: 12e6,
            T_bulk_K: 290, Twg_K: 800, Twc_K: 600);
        var outputs = MakeOutputs(new[] { s });

        var withoutParam = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 4e6, outerJacketThickness_mm: 2.5,
            gasGamma: 1.2);

        var withNullJacket = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 4e6, outerJacketThickness_mm: 2.5,
            gasGamma: 1.2, jacketMaterial: null);

        Assert.Equal(withoutParam.MinSafetyFactor,
            withNullJacket.MinSafetyFactor, precision: 6);
        Assert.Equal(withoutParam.PeakHoop_MPa,
            withNullJacket.PeakHoop_MPa, precision: 4);
    }

    [Fact]
    public void CompositeYield_ZeroJacketThickness_CollapseToInnerAlone()
    {
        // When outerJacketThickness_mm = 0, the composite formula should be
        // a no-op (t_jacket = 0 → weighted avg reduces to inner yield alone).
        var s = MakeStation(0, R_mm: 90, mach: 0.0, pCoolant_Pa: 12e6,
            T_bulk_K: 290, Twg_K: 800, Twc_K: 600);
        var outputs = MakeOutputs(new[] { s });

        var noJacketWithMat = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 4e6, outerJacketThickness_mm: 0.0,
            gasGamma: 1.2, jacketMaterial: WallMaterials.Inconel625);

        var noJacketNoMat = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 4e6, outerJacketThickness_mm: 0.0,
            gasGamma: 1.2);

        Assert.Equal(noJacketNoMat.MinSafetyFactor,
            noJacketWithMat.MinSafetyFactor, precision: 6);
    }

    [Fact]
    public void CompositeYield_HigherJacketYield_DominatesAtLargeJacketFraction()
    {
        // With 10 mm IN625 jacket and 1 mm GRCop liner: effective yield is
        // dominated by IN625 (cold 520 MPa vs GRCop cold 230 MPa).  The
        // composite SF must be higher than with a weak 10 mm jacket.
        var s = MakeStation(0, R_mm: 120, mach: 0.0, pCoolant_Pa: 12e6,
            T_bulk_K: 280, Twg_K: 300, Twc_K: 300);  // cold temps → cold yield
        var outputs = MakeOutputs(new[] { s });

        var weakJacket = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 4e6, outerJacketThickness_mm: 10.0,
            gasGamma: 0.0,
            jacketMaterial: WallMaterials.CuCrZr);   // CuCrZr cold yield 280 MPa (PH-32 LPBF-derated, was 350)

        var strongJacket = StructuralCheck.Evaluate(
            outputs, WallMaterials.GRCop42, gasSideWallThickness_mm: 1.0,
            chamberPressure_Pa: 4e6, outerJacketThickness_mm: 10.0,
            gasGamma: 0.0,
            jacketMaterial: WallMaterials.Inconel625); // IN625 cold yield 520 MPa

        Assert.True(strongJacket.MinSafetyFactor > weakJacket.MinSafetyFactor,
            $"IN625 jacket SF {strongJacket.MinSafetyFactor:F3} should exceed "
          + $"CuCrZr jacket SF {weakJacket.MinSafetyFactor:F3}.");
    }
}
