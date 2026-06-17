// Phase3SprintTests.cs — Contract tests for:
//   • Ablative / film-only cooling variant + ABLATIVE_BURNTHROUGH gate
//   • Chilldown transient (Chen / Shah two-phase, lumped-jacket MVP)
//   • Start transient simulator (valve ramp → dome fill → Pc rise → ignition)
//   • Turbopump sizing stub + NPSH_INSUFFICIENT gate
//
// All four items are additive: every new field defaults to a value
// that reproduces legacy behaviour (no analysis attached, no gate
// fired). Schema bumps v8 → v9 → v10 → v11 → v12 carry the new
// fields; every migration is identity because the defaults are
// safe.

using Voxelforge.FeedSystem;
using Voxelforge.HeatTransfer;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class Phase3SprintTests
{
    // ─────────────────────────────────────────────────────────────────
    //  AblativeAnalysis material library
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AblativeMaterials_CoverAllEnumValues()
    {
        foreach (AblativeMaterial m in System.Enum.GetValues<AblativeMaterial>())
            Assert.True(AblativeMaterials.All.ContainsKey(m), $"Missing spec for {m}.");
    }

    [Fact]
    public void AblativeMaterials_None_RecessionRateIsZero()
    {
        double r = AblativeMaterials.RecessionRate_mmps(AblativeMaterial.None, 5e6);
        Assert.Equal(0.0, r, precision: 9);
    }

    [Theory]
    [InlineData(AblativeMaterial.SilicaPhenolic)]
    [InlineData(AblativeMaterial.CarbonPhenolic)]
    [InlineData(AblativeMaterial.GraphitePyrolytic)]
    public void AblativeMaterials_RecessionRate_RisesWithHeatFlux(AblativeMaterial m)
    {
        double low  = AblativeMaterials.RecessionRate_mmps(m, 1e6);
        double high = AblativeMaterials.RecessionRate_mmps(m, 8e6);
        Assert.True(high > low, $"Higher q should give higher ṙ for {m}. low={low:F4} high={high:F4}");
    }

    [Fact]
    public void AblativeMaterials_PowerLaw_ClosedFormAtRefFlux()
    {
        // At q = q_ref (1 MW/m²) the recession rate should equal the
        // material's coefficient A — power-law exponent drops out.
        var spec = AblativeMaterials.SpecFor(AblativeMaterial.SilicaPhenolic);
        double r = AblativeMaterials.RecessionRate_mmps(
            AblativeMaterial.SilicaPhenolic, AblativeMaterials.ReferenceHeatFlux_Wm2);
        Assert.Equal(spec.RecessionCoefficient_mmps, r, precision: 6);
    }

    [Fact]
    public void AblativeMaterials_GraphiteRecedesSlowestAtSameQ()
    {
        const double q = 5e6;
        double silica   = AblativeMaterials.RecessionRate_mmps(AblativeMaterial.SilicaPhenolic,    q);
        double carbon   = AblativeMaterials.RecessionRate_mmps(AblativeMaterial.CarbonPhenolic,    q);
        double graphite = AblativeMaterials.RecessionRate_mmps(AblativeMaterial.GraphitePyrolytic, q);
        Assert.True(graphite < carbon && carbon < silica,
            $"Expected graphite < carbon < silica recession at {q:E1} W/m². "
            + $"Got silica={silica:F4} carbon={carbon:F4} graphite={graphite:F4}");
    }

    [Fact]
    public void AblativeAnalysis_None_ReturnsNull()
    {
        var (cond, design) = Baseline();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Null(gen.Ablative);
    }

    [Fact]
    public void AblativeAnalysis_SilicaPhenolic_AttachesResult()
    {
        var (cond, design) = Baseline();
        design = design with
        {
            AblativeMaterial = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm = 8.0,
            AblativeBurnDuration_s = 30,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Ablative);
        Assert.Equal(AblativeMaterial.SilicaPhenolic, gen.Ablative!.Material);
        Assert.True(gen.Ablative.Stations.Length > 0);
    }

    [Fact]
    public void AblativeAnalysis_BurnthroughFlagged_OnThinLinerLongBurn()
    {
        // 0.5 mm liner, 90 s burn at LOX/CH4 default conditions — should
        // burnthrough on at least the throat station for any of the
        // char-forming materials.
        var (cond, design) = Baseline();
        design = design with
        {
            AblativeMaterial = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm = 0.5,
            AblativeBurnDuration_s = 90,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Ablative);
        Assert.False(gen.Ablative!.IsAcceptable,
            "Expected ABLATIVE_BURNTHROUGH at 0.5 mm liner / 90 s burn.");
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "ABLATIVE_BURNTHROUGH");
    }

    [Fact]
    public void AblativeAnalysis_LongBurn_ScalesRecessionLinearly()
    {
        var (cond, design) = Baseline();
        design = design with
        {
            AblativeMaterial = AblativeMaterial.CarbonPhenolic,
            AblativeThickness_mm = 50.0,           // big enough to never burn through
            AblativeBurnDuration_s = 30,
        };
        var gen30 = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var gen60 = RegenChamberOptimization.GenerateWith(
            cond, design with { AblativeBurnDuration_s = 60 }, skipVoxelGeometry: true);
        // Constant-q assumption ⇒ recession scales linearly with t.
        Assert.Equal(2.0,
            gen60.Ablative!.MaxRecession_mm / System.Math.Max(gen30.Ablative!.MaxRecession_mm, 1e-9),
            precision: 2);
    }

    [Fact]
    public void AblativeAnalysis_BurnDurationZero_ReturnsZeroRecession()
    {
        var dummy = SyntheticThermal(stationCount: 5, q_Wm2: 5e6);
        var r = AblativeAnalysis.Run(
            material:           AblativeMaterial.SilicaPhenolic,
            thermal:            dummy,
            initialThickness_mm:5,
            burnDuration_s:     0);
        Assert.NotNull(r);
        Assert.Equal(0.0, r!.MaxRecession_mm, precision: 6);
        Assert.True(r.IsAcceptable);
    }

    [Fact]
    public void AblativeAnalysis_LongBurnExceedsServiceLimit_AddsWarning()
    {
        var dummy = SyntheticThermal(stationCount: 5, q_Wm2: 1e6);
        var spec = AblativeMaterials.SpecFor(AblativeMaterial.SilicaPhenolic);
        var r = AblativeAnalysis.Run(
            material:           AblativeMaterial.SilicaPhenolic,
            thermal:            dummy,
            initialThickness_mm:200,                            // big enough to not burn through
            burnDuration_s:     spec.MaxBurnDuration_s + 30);
        Assert.NotNull(r);
        Assert.Contains(r!.Warnings, w => w.Contains("constant-q assumption"));
    }

    // ─────────────────────────────────────────────────────────────────
    //  ChannelTopology.None (ablative-only variant)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ChannelTopology_None_YieldsZeroCoolantFlow()
    {
        var (cond, design) = Baseline();
        design = design with { ChannelTopology = ChannelTopology.None };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Equal(0.0, gen.Thermal.CoolantPressureDrop_Pa, precision: 6);
        Assert.Equal(cond.CoolantInletTemp_K, gen.Thermal.CoolantOutletT_K, precision: 6);
        Assert.Equal(cond.CoolantInletPressure_Pa, gen.Thermal.CoolantOutletP_Pa, precision: 6);
        foreach (var s in gen.Thermal.Stations)
        {
            Assert.Equal(0.0, s.CoolantVelocity_ms, precision: 6);
            Assert.Equal(0.0, s.h_c_Wm2K, precision: 6);
        }
    }

    [Fact]
    public void ChannelTopology_None_PinsPeakWallAboveServiceLimit()
    {
        var (cond, design) = Baseline();
        design = design with { ChannelTopology = ChannelTopology.None };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var mat = HeatTransfer.WallMaterials.All[cond.WallMaterialIndex];
        Assert.True(gen.Thermal.PeakGasSideWallT_K > mat.MaxServiceTemp_K,
            $"Peak wall T {gen.Thermal.PeakGasSideWallT_K:F0} K should sit above "
          + $"service limit {mat.MaxServiceTemp_K:F0} K so WALL_TEMP fires.");
        Assert.True(gen.Thermal.WallTempExceedsLimit);
    }

    [Fact]
    public void ChannelTopology_None_WithAblative_AttachesAblativeResult()
    {
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology       = ChannelTopology.None,
            AblativeMaterial      = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm  = 8.0,
            AblativeBurnDuration_s = 30,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Ablative);
        Assert.Equal(AblativeMaterial.SilicaPhenolic, gen.Ablative!.Material);
        Assert.True(gen.Ablative.Stations.Length > 0,
            "Ablative recession integral must still see a per-station heat-flux profile even "
          + "when the regen march is skipped.");
        // Gas-side heat flux stamped by the ablative-only solver path should be
        // non-zero across most stations — otherwise the recession integrates to 0.
        int nonZero = 0;
        foreach (var s in gen.Ablative.Stations)
            if (s.HeatFlux_Wm2 > 0) nonZero++;
        Assert.True(nonZero > gen.Ablative.Stations.Length / 2,
            "Expected positive heat flux on most stations; got " + nonZero);
    }

    [Fact]
    public void ChannelTopology_None_WithoutAblative_WallTempGateFires()
    {
        // No ablative + no regen channels ⇒ nothing protecting the wall.
        // WALL_TEMP must fire so the infeasibility is surfaced to the user.
        var (cond, design) = Baseline();
        design = design with { ChannelTopology = ChannelTopology.None };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "WALL_TEMP");
    }

    [Fact]
    public void ChannelTopology_None_ThinAblative_BurnthroughGateFires()
    {
        // Topology = None + under-sized ablative ⇒ ABLATIVE_BURNTHROUGH joins
        // the WALL_TEMP gate. Both are hard violations.
        var (cond, design) = Baseline();
        design = design with
        {
            ChannelTopology        = ChannelTopology.None,
            AblativeMaterial       = AblativeMaterial.SilicaPhenolic,
            AblativeThickness_mm   = 0.5,
            AblativeBurnDuration_s = 90,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Ablative);
        Assert.False(gen.Ablative!.IsAcceptable);
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "ABLATIVE_BURNTHROUGH");
    }

    [Fact]
    public void ChannelTopology_None_DoesNotAffectAxialOrHelicalPaths()
    {
        // Guard-rail: Axial + Helical topologies must keep producing a real
        // coolant march with non-zero ΔP / coolant ΔT / bulk-T rise — the
        // None branch is purely additive.
        var (cond, design) = Baseline();
        var axial = RegenChamberOptimization.GenerateWith(
            cond, design with { ChannelTopology = ChannelTopology.Axial },
            skipVoxelGeometry: true);
        var helix = RegenChamberOptimization.GenerateWith(
            cond, design with { ChannelTopology = ChannelTopology.Helical, HelixPitchAngle_deg = 15 },
            skipVoxelGeometry: true);
        Assert.True(axial.Thermal.CoolantPressureDrop_Pa > 0);
        Assert.True(helix.Thermal.CoolantPressureDrop_Pa > 0);
        Assert.True(axial.Thermal.CoolantOutletT_K > cond.CoolantInletTemp_K);
        Assert.True(helix.Thermal.CoolantOutletT_K > cond.CoolantInletTemp_K);
    }

    // ─────────────────────────────────────────────────────────────────
    //  ChilldownTransient lumped-jacket integrator
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ChilldownTransient_IsCryogenic_RecognisesCH4AndH2()
    {
        Assert.True(ChilldownTransient.IsCryogenic("CH4"));
        Assert.True(ChilldownTransient.IsCryogenic("H2"));
        Assert.False(ChilldownTransient.IsCryogenic("RP-1"));
    }

    [Fact]
    public void ChilldownTransient_TimeConstant_MatchesClosedForm()
    {
        // τ = m·cp / (h·A). Pick clean integers and verify exactly.
        var inp = new ChilldownInputs(
            WallMass_kg:              2.0,
            WallArea_m2:              0.10,
            WallSpecificHeat_Jkg:     400,
            InitialWallTemp_K:        298,
            CoolantSaturationTemp_K:  150,
            CoolantMassFlow_kgs:      0.20,
            TwoPhaseHTC_Wm2K:         5000,
            DoneDeltaT_K:             50,
            WallElasticModulus_Pa:    100e9,
            WallCTE_perK:             16e-6,
            MaxTime_s:                60);
        double expectedTau = 2.0 * 400.0 / (5000.0 * 0.10);     // = 1.6 s
        var r = ChilldownTransient.Run(inp);
        Assert.Equal(expectedTau, r.TimeConstant_s, precision: 6);
    }

    [Fact]
    public void ChilldownTransient_Time_MatchesNegativeTauLnFormula()
    {
        var inp = new ChilldownInputs(
            WallMass_kg: 2.0, WallArea_m2: 0.10, WallSpecificHeat_Jkg: 400,
            InitialWallTemp_K: 298, CoolantSaturationTemp_K: 150,
            CoolantMassFlow_kgs: 0.20, TwoPhaseHTC_Wm2K: 5000,
            DoneDeltaT_K: 50, WallElasticModulus_Pa: 100e9,
            WallCTE_perK: 16e-6, MaxTime_s: 60);
        var r = ChilldownTransient.Run(inp);
        // dT0 = 148, dTdone = 50 → expected = -1.6 · ln(50/148)
        double expected = -1.6 * System.Math.Log(50.0 / 148.0);
        Assert.Equal(expected, r.TimeToChill_s, precision: 4);
    }

    [Fact]
    public void ChilldownTransient_AlreadyCold_ReportsZeroTime()
    {
        var inp = new ChilldownInputs(
            WallMass_kg: 1.0, WallArea_m2: 0.10, WallSpecificHeat_Jkg: 400,
            InitialWallTemp_K: 100,                  // cold
            CoolantSaturationTemp_K: 150,
            CoolantMassFlow_kgs: 0.10, TwoPhaseHTC_Wm2K: 5000,
            DoneDeltaT_K: 50, WallElasticModulus_Pa: 100e9,
            WallCTE_perK: 16e-6, MaxTime_s: 60);
        var r = ChilldownTransient.Run(inp);
        Assert.Equal(0.0, r.TimeToChill_s, precision: 6);
        Assert.True(r.IsAcceptable);
    }

    [Fact]
    public void ChilldownTransient_HighHTC_ChillsFasterThanLowHTC()
    {
        var baseIn = new ChilldownInputs(
            WallMass_kg: 5.0, WallArea_m2: 0.20, WallSpecificHeat_Jkg: 400,
            InitialWallTemp_K: 298, CoolantSaturationTemp_K: 150,
            CoolantMassFlow_kgs: 0.20, TwoPhaseHTC_Wm2K: 2000,
            DoneDeltaT_K: 50, WallElasticModulus_Pa: 100e9,
            WallCTE_perK: 16e-6, MaxTime_s: 600);
        var slow = ChilldownTransient.Run(baseIn);
        var fast = ChilldownTransient.Run(baseIn with { TwoPhaseHTC_Wm2K = 10_000 });
        Assert.True(fast.TimeToChill_s < slow.TimeToChill_s,
            $"5× higher HTC should drop chilldown time. slow={slow.TimeToChill_s:F1} fast={fast.TimeToChill_s:F1}");
    }

    [Fact]
    public void ChilldownTransient_LongTime_ExceedsBudget_FlagsUnacceptable()
    {
        var inp = new ChilldownInputs(
            WallMass_kg: 50.0,                       // huge thermal mass
            WallArea_m2: 0.10, WallSpecificHeat_Jkg: 400,
            InitialWallTemp_K: 298, CoolantSaturationTemp_K: 150,
            CoolantMassFlow_kgs: 0.10, TwoPhaseHTC_Wm2K: 1000,
            DoneDeltaT_K: 5, WallElasticModulus_Pa: 100e9,
            WallCTE_perK: 16e-6, MaxTime_s: 5);      // tight budget
        var r = ChilldownTransient.Run(inp);
        Assert.False(r.IsAcceptable);
        Assert.Contains(r.Warnings, w => w.Contains("budget"));
    }

    [Fact]
    public void GenerateWith_ChilldownOptIn_CryogenicPair_AttachesResult()
    {
        var (cond, design) = Baseline();
        cond = cond with { IncludeChilldownTransient = true };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Chilldown);
        Assert.True(gen.Chilldown!.TimeConstant_s > 0);
    }

    [Fact]
    public void GenerateWith_ChilldownOptIn_NonCryoPair_ReturnsNull()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            IncludeChilldownTransient = true,
            PropellantPair = Combustion.PropellantPair.LOX_RP1,
            MixtureRatio = 2.5,
            CoolantInletTemp_K = 290,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Null(gen.Chilldown);
    }

    [Fact]
    public void GenerateWith_ChilldownDefault_LeavesResultNull()
    {
        // Default: IncludeChilldownTransient = false, no analysis attached.
        var (cond, design) = Baseline();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Null(gen.Chilldown);
    }

    // ─────────────────────────────────────────────────────────────────
    //  StartTransientSim (lumped 0-D)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void StartTransient_ValveRamp_LinearlyHitsFullOpen()
    {
        var inp = StartInputs();
        var r = Combustion.StartTransientSim.Run(inp);
        // Final sample valve position should be 1.0; first should be 0.0.
        Assert.Equal(0.0, r.Samples[0].ValvePosition, precision: 6);
        Assert.Equal(1.0, r.Samples[^1].ValvePosition, precision: 4);
    }

    [Fact]
    public void StartTransient_DomeFills_BeforeSteadyInjection()
    {
        var inp = StartInputs();
        var r = Combustion.StartTransientSim.Run(inp);
        // Find first sample where dome is reported full.
        int idx = System.Array.FindIndex(r.Samples, s => s.DomeFillFraction >= 0.999);
        Assert.True(idx > 0, "Dome should require finite time to fill.");
    }

    [Fact]
    public void StartTransient_LongIgniterDelay_PoolsUnburnedPropellant()
    {
        var early = Combustion.StartTransientSim.Run(StartInputs() with { IgniterDelay_s = 0.01 });
        var late  = Combustion.StartTransientSim.Run(StartInputs() with { IgniterDelay_s = 0.20 });
        Assert.True(late.UnburnedMassAtIgnition_kg > early.UnburnedMassAtIgnition_kg,
            $"Late ignition should pool more unburned propellant. early={early.UnburnedMassAtIgnition_kg:E3} late={late.UnburnedMassAtIgnition_kg:E3}");
    }

    [Fact]
    public void StartTransient_LateIgniterDelay_FlagsHardStartRisk()
    {
        // Long delay → big pool → big spike on light.
        var inp = StartInputs() with { IgniterDelay_s = 0.30 };
        var r = Combustion.StartTransientSim.Run(inp);
        Assert.True(r.HardStartRisk,
            $"Expected HARD_START at 300 ms igniter delay. overshoot={r.PeakPressureOvershoot * 100:F0}%");
    }

    [Fact]
    public void StartTransient_TightStart_AvoidsHardStart()
    {
        // Igniter fires immediately → no pool → clean start.
        var inp = StartInputs() with { IgniterDelay_s = 0.0, ValveOpenTime_s = 0.05 };
        var r = Combustion.StartTransientSim.Run(inp);
        Assert.False(r.HardStartRisk,
            $"Tight start should not flag hard start. overshoot={r.PeakPressureOvershoot * 100:F0}%");
    }

    [Fact]
    public void StartTransient_TimeTo90Pc_IsFinite_OnNominalStart()
    {
        var inp = StartInputs();
        var r = Combustion.StartTransientSim.Run(inp);
        Assert.True(double.IsFinite(r.TimeTo90Pc_s),
            "Sim should reach 90 % Pc within the simulation duration.");
        Assert.True(r.TimeTo90Pc_s > 0);
    }

    [Fact]
    public void StartTransient_DegenerateInputs_ReturnsEmptyResult()
    {
        var bad = StartInputs() with { ChamberVolume_m3 = 0 };
        var r = Combustion.StartTransientSim.Run(bad);
        Assert.Empty(r.Samples);
        Assert.Contains(r.Warnings, w => w.Contains("skipped"));
    }

    [Fact]
    public void StartTransient_TimeConstant_FollowsClosedForm()
    {
        var inp = StartInputs();
        var r = Combustion.StartTransientSim.Run(inp);
        double expected = inp.ChamberVolume_m3 / (inp.CStar_ms * inp.ThroatArea_m2);
        Assert.Equal(expected, r.ChamberFillTimeConstant_s, precision: 6);
    }

    [Fact]
    public void GenerateWith_StartTransientOptIn_AttachesResult()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            IncludeStartTransient = true,
            StartValveOpenTime_s = 0.10,
            StartIgniterDelay_s = 0.05,
            StartSimulationDuration_s = 0.5,
            StartSimulationTimeStep_s = 0.001,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.StartTransient);
        Assert.True(gen.StartTransient!.Samples.Length > 100);
    }

    [Fact]
    public void GenerateWith_StartTransientDefault_LeavesResultNull()
    {
        var (cond, design) = Baseline();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Null(gen.StartTransient);
    }

    [Fact]
    public void Gate_FiresOn_HardStart_FromGenerateWith()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            IncludeStartTransient = true,
            StartIgniterDelay_s = 0.50,                        // very late
            StartValveOpenTime_s = 0.05,
            StartSimulationDuration_s = 1.0,
            StartSimulationTimeStep_s = 0.001,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "HARD_START_RISK");
    }

    // ─────────────────────────────────────────────────────────────────
    //  TurbopumpSizing + NPSH gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Turbopump_PressureFed_ReturnsNoOpResult()
    {
        var (cond, design) = Baseline();
        cond = cond with { EngineCycle = FeedSystem.EngineCycle.PressureFed };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Null(gen.Turbopump);
    }

    [Theory]
    [InlineData(FeedSystem.EngineCycle.GasGenerator)]
    [InlineData(FeedSystem.EngineCycle.ElectricPump)]
    [InlineData(FeedSystem.EngineCycle.OpenExpander)]
    public void Turbopump_PumpFedCycles_AttachResult(FeedSystem.EngineCycle cycle)
    {
        var (cond, design) = Baseline();
        cond = cond with { EngineCycle = cycle };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Turbopump);
        Assert.NotNull(gen.Turbopump!.FuelPump);
        Assert.NotNull(gen.Turbopump.OxPump);
        Assert.Equal(cycle, gen.Turbopump.Cycle);
    }

    [Fact]
    public void Turbopump_HeadRise_FollowsPressureRiseOverDensity()
    {
        var inp_cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
            EngineCycle = FeedSystem.EngineCycle.GasGenerator,
            PumpInletPressure_Pa = 0.5e6,
            PumpDischargePressure_Pa = 15e6,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        var gen = RegenChamberOptimization.GenerateWith(inp_cond, design, skipVoxelGeometry: true);
        var pump = gen.Turbopump!.FuelPump!;
        // Δh = (15 − 0.5) MPa / (ρ · g) ≈ 14.5e6 / (~430 · 9.807) ≈ 3440 m for LCH4.
        // Just check the head is in the same order-of-magnitude band.
        Assert.True(pump.HeadRise_m > 100, $"Head should be > 100 m for 14.5 MPa rise. got {pump.HeadRise_m:F0}");
    }

    [Fact]
    public void Turbopump_LowInletPressure_TripsNPSH()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            EngineCycle = FeedSystem.EngineCycle.ElectricPump,
            PumpInletPressure_Pa = 0.05e6,    // 50 kPa — below LCH4 vapour pressure
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Turbopump);
        Assert.False(gen.Turbopump!.NPSHFeasible);
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "NPSH_INSUFFICIENT");
    }

    [Fact]
    public void Turbopump_AdequateInletPressure_DoesNotTripNPSH()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            EngineCycle = FeedSystem.EngineCycle.GasGenerator,
            PumpInletPressure_Pa = 1.0e6,    // 1 MPa — well above any vapour pressure
        };
        // Sprint 30 (PH-2): Thoma NPSHR scales with RPM·√Q. At LRE
        // RPMs the no-inducer S_s = 8 500 produces NPSHR > NPSHA even
        // at 1 MPa inlet, matching real-world inducer-mandatory LRE
        // practice. Set HasInducer to model the typical pump that
        // would actually run at this design point.
        design = design with { HasInducer = true };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.Turbopump);
        Assert.True(gen.Turbopump!.NPSHFeasible,
            $"NPSHA={gen.Turbopump.FuelPump?.NPSHA_m:F1}m vs NPSHR={gen.Turbopump.FuelPump?.NPSHR_m:F1}m. "
          + "If this fails after Sprint 30, the Thoma S_s constants or pump RPM may need re-calibration.");
    }

    [Fact]
    public void Turbopump_ElectricPump_ReportsConverterMass()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            EngineCycle = FeedSystem.EngineCycle.ElectricPump,
            PumpInletPressure_Pa = 1e6,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.True(gen.Turbopump!.EstimatedDryMass_kg > 0,
            "Electric-pump cycle should report a non-zero converter mass.");
        Assert.Contains("kg/kW", gen.Turbopump.Notes);
    }

    [Fact]
    public void Turbopump_GasGenerator_ReportsZeroConverterMass()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            EngineCycle = FeedSystem.EngineCycle.GasGenerator,
            PumpInletPressure_Pa = 1e6,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        // GasGenerator + OpenExpander get power from the propellants
        // themselves; no electrical converter mass.
        Assert.Equal(0.0, gen.Turbopump!.EstimatedDryMass_kg, precision: 6);
    }

    [Fact]
    public void Turbopump_TotalShaft_MatchesSumOfPumps()
    {
        var (cond, design) = Baseline();
        cond = cond with
        {
            EngineCycle = FeedSystem.EngineCycle.GasGenerator,
            PumpInletPressure_Pa = 1e6,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var pump = gen.Turbopump!;
        double sum = pump.FuelPump!.ShaftPower_W + pump.OxPump!.ShaftPower_W;
        Assert.Equal(sum, pump.TotalShaftPower_W, precision: 1);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static Combustion.StartTransientInputs StartInputs() => new(
        ValveOpenTime_s:            0.10,
        IgniterDelay_s:             0.05,
        DomeVolume_m3:              5e-5,
        DomePropellantDensity_kgm3: 800,
        SteadyMassFlow_kgs:         0.5,
        ChamberVolume_m3:           5e-4,
        CStar_ms:                   1850,
        ThroatArea_m2:              5e-5,
        ChamberPressure_Pa:         6.9e6,
        SimulationDuration_s:       0.5,
        TimeStep_s:                 0.001,
        HardStartFactor:            0.5);

    private static (OperatingConditions cond, RegenChamberDesign design) Baseline()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        return (cond, design);
    }

    /// <summary>
    /// Build a <see cref="RegenSolverOutputs"/> with N constant-q
    /// stations for unit-testing analysis modules in isolation.
    /// </summary>
    private static RegenSolverOutputs SyntheticThermal(int stationCount, double q_Wm2)
    {
        var stations = new StationResult[stationCount];
        for (int i = 0; i < stationCount; i++)
        {
            stations[i] = new StationResult(
                Index: i, X_mm: i * 10.0, R_mm: 20.0,
                AreaRatioToThroat: 1.0, Mach: 0.5,
                StaticTemp_K: 3000, AdiabaticWallTemp_K: 3500,
                EffectiveRecoveryTemp_K: 3500, FilmEffectiveness: 0,
                HeatFlux_Wm2: q_Wm2,
                h_g_Wm2K: 5000, h_c_Wm2K: 30_000,
                GasSideWallTemp_K: 800, CoolantSideWallTemp_K: 500,
                WallRadialProfile_K: new[] { 800.0, 700, 600, 550, 500 },
                AxialConductionFlux_Wm2: 0,
                CoolantBulkTemp_K: 200, CoolantBulkPressure_Pa: 10e6,
                CoolantVelocity_ms: 30, Reynolds: 1e6, PrandtlBulk: 1.0,
                ChannelWidth_mm: 1.0, ChannelHeight_mm: 2.0, HydraulicDiameter_mm: 1.5,
                PressureGradient_Pam: 1e6);
        }
        var diag = new SolverDiagnostics(0, 0, 0, 0, true);
        return new RegenSolverOutputs(
            Stations: stations,
            PeakGasSideWallT_K: 800, PeakCoolantSideWallT_K: 500,
            PeakStationIndex: 0,
            CoolantInletT_K: 150, CoolantOutletT_K: 300,
            CoolantInletP_Pa: 12e6, CoolantOutletP_Pa: 9e6,
            CoolantPressureDrop_Pa: 3e6,
            TotalHeatLoad_W: 1e5, TotalWettedArea_mm2: 5e4,
            ThroatHeatFlux_Wm2: q_Wm2,
            WallTempExceedsLimit: false, WallMarginK: 200,
            FilmMassFlow_kgs: 0, IspPenaltyFraction: 0,
            AxialConductionRMS_Wm2: 0,
            Diagnostics: diag,
            Warnings: System.Array.Empty<string>());
    }
}
