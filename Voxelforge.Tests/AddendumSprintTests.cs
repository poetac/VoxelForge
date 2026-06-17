// AddendumSprintTests.cs — Contract tests covering:
//   • Igniter boss + IGNITER_ENERGY_INSUFFICIENT gate
//   • Umbilical / QD standards library
//   • Feed-system ΔP stackup + FEED_PRESSURE_INSUFFICIENT gate
//
// All three items hang off OperatingConditions / RegenChamberDesign
// records with safe defaults so existing saved designs round-trip
// unchanged. The feed stackup is opt-in (TankUllagePressure_Pa > 0).

using Voxelforge.FeedSystem;
using Voxelforge.Geometry;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class AddendumSprintTests
{
    // ─────────────────────────────────────────────────────────────────
    //  Igniter boss + ignition-energy gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IgniterPresets_CoverAllEnumValues()
    {
        foreach (IgniterType t in System.Enum.GetValues<IgniterType>())
            Assert.True(IgniterPresets.All.ContainsKey(t), $"Missing preset for {t}.");
    }

    [Theory]
    [InlineData(IgniterType.None,                  true)]
    [InlineData(IgniterType.SparkTorch,            true)]
    [InlineData(IgniterType.AugmentedSpark,        true)]
    [InlineData(IgniterType.PyrotechnicCartridge,  true)]
    public void IgniterPresets_AllRealTypesMeetJannafFloor(IgniterType t, bool expected)
    {
        Assert.Equal(expected, IgniterPresets.MeetsMinimumEnergy(t));
    }

    [Fact]
    public void RegenChamberDesign_DefaultsTo_NoIgniter()
    {
        var d = new RegenChamberDesign();
        Assert.Equal(IgniterType.None, d.IgniterType);
        Assert.Equal(0.0, d.IgniterRadialFraction, precision: 6);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Umbilical / QD standards
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UmbilicalStandards_CoverAllEnumValues()
    {
        foreach (UmbilicalStandard s in System.Enum.GetValues<UmbilicalStandard>())
            Assert.True(UmbilicalStandards.All.ContainsKey(s), $"Missing preset for {s}.");
    }

    [Fact]
    public void UmbilicalStandards_None_ReturnsZeroDP()
    {
        double dp = UmbilicalStandards.NominalDeltaP_Pa(UmbilicalStandard.None, 0.1, 1000);
        Assert.Equal(0.0, dp, precision: 6);
    }

    [Fact]
    public void UmbilicalStandards_LargerBoreGivesLowerDP_AtSameFlow()
    {
        double rho = 1000, flow = 0.5;   // 0.5 kg/s water
        double smallDP = UmbilicalStandards.NominalDeltaP_Pa(UmbilicalStandard.AN_MS33656_06, flow, rho);
        double bigDP   = UmbilicalStandards.NominalDeltaP_Pa(UmbilicalStandard.Cryo_QD_Three_Quarter, flow, rho);
        Assert.True(smallDP > bigDP,
            $"AN-06 (Ø8 mm) should have higher ΔP than 3/4\" QD (Ø18 mm). small={smallDP:E2} big={bigDP:E2}");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Feed-system ΔP stackup
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LineLoss_DarcyWeisbach_MatchesClosedForm_InLaminarRegime()
    {
        // Laminar: f = 64/Re, ΔP = 32·μ·v·L/D² (Hagen-Poiseuille).
        // Pick a tiny flow so Re << 2300 in a 4-mm tube.
        double L = 1.0, D = 0.004;
        double rho = 1000, mu = 1e-3;
        double mDot = 0.002;                         // 2 g/s
        double dp = LineLoss.FrictionDP(L, D, mDot, rho, mu);
        double v  = mDot / (rho * System.Math.PI * D * D / 4);
        double expected = 32.0 * mu * v * L / (D * D);
        // Allow 10 % tolerance — Darcy branch uses Re check at 2300 and
        // the closed form is only exact in the strict laminar limit.
        Assert.InRange(dp / expected, 0.85, 1.15);
    }

    [Fact]
    public void LineLoss_ReturnsZero_OnDegenerateInputs()
    {
        Assert.Equal(0, LineLoss.FrictionDP(0, 0.01, 0.1, 1000));
        Assert.Equal(0, LineLoss.FrictionDP(1, 0.01, 0,    1000));
        Assert.Equal(0, LineLoss.FrictionDP(1, 0,    0.1,  1000));
    }

    [Fact]
    public void ValveCv_WaterOneGpm_OneCv_GivesOnePsi()
    {
        // Definition sanity: 1 gpm of water through Cv=1 → 1 psi.
        // 1 gpm = 6.309e-5 m³/s → m_dot = 0.06309 kg/s for water (SG=1).
        double mDot = 0.06309, rho = 1000, cv = 1.0;
        double dp = ValveCv.DeltaP(cv, mDot, rho);
        Assert.InRange(dp / 6894.76, 0.98, 1.02);   // ≈ 1 psi
    }

    [Fact]
    public void FeedStackup_SkippedWhen_TankUllageIsZero()
    {
        var (cond, design) = BaselineNoStackup();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.Null(gen.FeedStackup);
    }

    [Fact]
    public void FeedStackup_RunsWhen_TankUllageIsPositive()
    {
        var (cond, design) = BaselineNoStackup();
        cond = cond with { TankUllagePressure_Pa = 1.5e7 };   // 15 MPa ullage
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.FeedStackup);
        Assert.True(gen.FeedStackup!.Segments.Length >= 5,
            "Expected ≥ 5 stackup segments (ullage + line + jacket + filter + umbilical + valve + dome + injector).");
        Assert.Equal(cond.TankUllagePressure_Pa, gen.FeedStackup.TankUllagePressure_Pa, precision: 0);
    }

    [Fact]
    public void FeedStackup_FlagsInfeasibleWhenUllageTooLow()
    {
        // Ullage barely above chamber target — after any ΔP the feed
        // stackup cannot make rated Pc. Gate should fire.
        var (cond, design) = BaselineNoStackup();
        cond = cond with { TankUllagePressure_Pa = cond.ChamberPressure_Pa + 1e4 };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.FeedStackup);
        Assert.False(gen.FeedStackup!.IsFeasible);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "FEED_PRESSURE_INSUFFICIENT");
    }

    [Fact]
    public void FeedStackup_MarginFraction_IsCorrect()
    {
        var (cond, design) = BaselineNoStackup();
        cond = cond with { TankUllagePressure_Pa = 1.5e7 };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var s = gen.FeedStackup!;
        double expected = (s.PredictedChamberPressure_Pa - s.TargetChamberPressure_Pa) / s.TargetChamberPressure_Pa;
        Assert.Equal(expected, s.MarginFraction, precision: 6);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Sprint 19 (2026-04-23) — Pressure-fed polish
    //  Blow-down mode + PressurefedPresets.SmallThrust() factory.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sprint19_BlowDown_ZeroFinalPressure_SkipsEndOfBurnStackup()
    {
        // Default (BlowDownFinalPressure_Pa = 0) = regulated mode;
        // end-of-burn fields stay zero / feasible.
        var (cond, design) = BaselineNoStackup();
        cond = cond with { TankUllagePressure_Pa = 1.5e7 };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var s = gen.FeedStackup!;
        Assert.Equal(0.0, s.EndOfBurnTankPressure_Pa);
        Assert.Equal(0.0, s.EndOfBurnPredictedChamberPressure_Pa);
        Assert.True(s.EndOfBurnIsFeasible);   // default-true short-circuits the gate
    }

    [Fact]
    public void Sprint19_BlowDown_RunsEndOfBurnStackupWhenEnabled()
    {
        // 15 MPa start → 10 MPa end-of-burn. Both endpoints above the
        // 6.9 MPa default chamber target so both should be feasible.
        var (cond, design) = BaselineNoStackup();
        cond = cond with
        {
            TankUllagePressure_Pa    = 1.5e7,
            BlowDownFinalPressure_Pa = 1.0e7,
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var s = gen.FeedStackup!;

        Assert.Equal(1.0e7, s.EndOfBurnTankPressure_Pa, precision: 0);
        Assert.True(s.EndOfBurnPredictedChamberPressure_Pa > 0);
        Assert.True(s.EndOfBurnIsFeasible);
        // End-of-burn predicted Pc must equal EOB tank P minus same ΔP sum
        // as start-of-burn (within rounding).
        double totalDP = s.TankUllagePressure_Pa - s.PredictedChamberPressure_Pa;
        double expected = s.EndOfBurnTankPressure_Pa - totalDP;
        Assert.Equal(expected, s.EndOfBurnPredictedChamberPressure_Pa, precision: 0);
    }

    [Fact]
    public void Sprint19_BlowDown_InfeasibleEndOfBurn_GateFires()
    {
        // Start-of-burn feasible at 15 MPa but the blow-down decay to
        // 7 MPa only barely exceeds the 6.9 MPa target — after any feed-
        // system ΔP the EOB prediction falls below target. Gate must
        // fire AND the start-of-burn FEED_PRESSURE_INSUFFICIENT gate
        // must NOT fire (the classic "engine starts fine but can't
        // finish the burn" failure mode).
        var (cond, design) = BaselineNoStackup();
        cond = cond with
        {
            TankUllagePressure_Pa    = 1.5e7,                      // 15 MPa start (healthy)
            BlowDownFinalPressure_Pa = cond.ChamberPressure_Pa + 1e4,  // barely above target
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var s = gen.FeedStackup!;

        Assert.True(s.IsFeasible,
            "Start-of-burn stackup should be feasible at 15 MPa.");
        Assert.False(s.EndOfBurnIsFeasible,
            "End-of-burn stackup should trip at final tank P ≈ Pc.");

        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "BLOW_DOWN_INSUFFICIENT");
        Assert.DoesNotContain(score.FeasibilityViolations,
            v => v.ConstraintId == "FEED_PRESSURE_INSUFFICIENT");
    }

    [Fact]
    public void Sprint19_SmallThrustPreset_SetsPressureFedDefaults()
    {
        var cond = PressurefedPresets.SmallThrust(thrust_N: 250.0);
        Assert.Equal(EngineCycle.PressureFed, cond.EngineCycle);
        Assert.Equal(250.0, cond.Thrust_N);
        // Tank ullage set to 1.5 × chamber P (blow-down start).
        Assert.Equal(1.5 * cond.ChamberPressure_Pa, cond.TankUllagePressure_Pa, precision: 0);
        // Blow-down final set to 1.1 × chamber P (workable EOB margin).
        Assert.Equal(1.1 * cond.ChamberPressure_Pa, cond.BlowDownFinalPressure_Pa, precision: 0);
        // Feed-line geometry scaled down from defaults (small-thrust).
        Assert.InRange(cond.FeedLineDiameter_mm, 2.0, 8.0);
        Assert.InRange(cond.MainValveCv, 0.1, 2.0);
        // Coolant inlet pressure matches tank ullage (no pump).
        Assert.Equal(cond.TankUllagePressure_Pa, cond.CoolantInletPressure_Pa);
    }

    [Fact]
    public void Sprint19_SmallThrustPreset_RejectsNonPositiveThrust()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PressurefedPresets.SmallThrust(thrust_N: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PressurefedPresets.SmallThrust(thrust_N: -10.0));
    }

    [Fact]
    public void Sprint19_SchemaV15_RoundTripsBlowDownField()
    {
        // Persistence round-trip: a design with blow-down configured
        // saves + loads without losing the new BlowDownFinalPressure_Pa
        // field. Schema cascade: Sprint 19 originally bumped v13 → v14
        // on its own branch; after Sprint 20 (dual-bell) merged first
        // and claimed v14, Sprint 19 cascaded to v14 → v15 (identity
        // migration) on rebase.
        var cond = PressurefedPresets.SmallThrust(thrust_N: 300.0);
        var design = new RegenChamberDesign();

        using var tmp = TestTempFile.Create();
        Voxelforge.IO.DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = Voxelforge.IO.DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(Voxelforge.IO.DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.Equal(cond.BlowDownFinalPressure_Pa,
                     loaded.Conditions!.BlowDownFinalPressure_Pa, precision: 0);
        Assert.Equal(cond.TankUllagePressure_Pa,
                     loaded.Conditions.TankUllagePressure_Pa, precision: 0);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Cross-cutting
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OperatingConditions_NewFields_HaveSafeDefaults()
    {
        var c = new OperatingConditions();
        Assert.Equal(0.0, c.TankUllagePressure_Pa);   // opt-out by default
        Assert.True(c.FeedLineLength_m    > 0);
        Assert.True(c.FeedLineDiameter_mm > 0);
        Assert.True(c.MainValveCv         > 0);
        Assert.True(c.FilterDeltaP_Pa    >= 0);
        Assert.Equal(UmbilicalStandard.None, c.UmbilicalStandard);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design) BaselineNoStackup()
    {
        var cond = new OperatingConditions
        {
            PropellantPair = Combustion.PropellantPair.LOX_CH4,
            // TankUllagePressure_Pa defaults to 0 → stackup opted-out
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = 40,
        };
        return (cond, design);
    }
}
