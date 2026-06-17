// Phase1CompletionTests.cs — Contract tests for:
//   • Purge ports + PurgeFlowModel + PURGE_FLOW_INSUFFICIENT gate
//   • Inlet dome + DomeHydraulics
//   • Gimbal mount configurations + structural-confidence coupling
// plus the schema v6 → v7 migration that carries all the new fields
// on OperatingConditions / RegenChamberDesign.

using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Structure;

namespace Voxelforge.Tests;

public class Phase1CompletionTests
{
    // ─────────────────────────────────────────────────────────────────
    //  DomeHydraulics
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DomeHydraulics_ZeroMassFlow_ReturnsZeroLoss()
    {
        var spec = new DomeSpec(
            DomeDepth_mm: 10, DomeRadius_mm: 25,
            InletDiameter_mm: 8, IncludeAntiVortexBaffle: false);
        var r = DomeHydraulics.Compute(spec, massFlow_kgs: 0, density_kgm3: 1000);
        Assert.Equal(0.0, r.TotalDP_Pa, precision: 6);
    }

    [Fact]
    public void DomeHydraulics_Baffle_IncreasesDP()
    {
        double rho = 1000, mdot = 0.2;   // 200 g/s water-ish
        var specPlain = new DomeSpec(10, 25, 8, IncludeAntiVortexBaffle: false);
        var specBaffled = specPlain with { IncludeAntiVortexBaffle = true };
        var plain = DomeHydraulics.Compute(specPlain, mdot, rho);
        var baff  = DomeHydraulics.Compute(specBaffled, mdot, rho);
        Assert.True(baff.TotalDP_Pa > plain.TotalDP_Pa,
            $"Baffle should raise ΔP. plain={plain.TotalDP_Pa:F1}, baff={baff.TotalDP_Pa:F1}");
    }

    [Fact]
    public void DomeHydraulics_SmallerInlet_GivesHigherExpansionLoss()
    {
        double rho = 1000, mdot = 0.2;
        var wide   = new DomeSpec(10, 25, InletDiameter_mm: 12, IncludeAntiVortexBaffle: false);
        var narrow = wide with { InletDiameter_mm = 4 };
        var wideR   = DomeHydraulics.Compute(wide,   mdot, rho);
        var narrowR = DomeHydraulics.Compute(narrow, mdot, rho);
        Assert.True(narrowR.ExpansionDP_Pa > wideR.ExpansionDP_Pa);
        Assert.True(narrowR.InletVelocity_ms > wideR.InletVelocity_ms);
    }

    [Fact]
    public void FeedStackup_UsesDomeHydraulics_WhenDomeDepthPositive()
    {
        var (cond, design) = Baseline();
        cond = cond with { TankUllagePressure_Pa = 1.5e7 };

        var noDome    = design with { FuelDomeDepth_mm = 0 };
        var withDome  = design with { FuelDomeDepth_mm = 12, DomeInletDiameter_mm = 6 };

        var genNoDome   = RegenChamberOptimization.GenerateWith(cond, noDome,    skipVoxelGeometry: true);
        var genWithDome = RegenChamberOptimization.GenerateWith(cond, withDome,  skipVoxelGeometry: true);

        // Find the dome segment in each and verify the label changed.
        Assert.NotNull(genNoDome.FeedStackup);
        Assert.NotNull(genWithDome.FeedStackup);
        Assert.Contains(genNoDome.FeedStackup!.Segments,
            s => s.Name.Contains("approx", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(genWithDome.FeedStackup!.Segments,
            s => s.Name.Contains("fuel") && !s.Name.Contains("approx", System.StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────────────────────────
    //  GimbalMount
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GimbalMount_FixedFlange_ReportsInfiniteStiffness()
    {
        var r = GimbalMount.Evaluate(MountConfiguration.FixedFlange,
                                     thrust_N: 500, material: WallMaterials.CuCrZr);
        Assert.True(double.IsPositiveInfinity(r.Stiffness_Nm_per_rad));
        Assert.True(r.StressAcceptable);
    }

    [Theory]
    [InlineData(MountConfiguration.PinJointGimbal)]
    [InlineData(MountConfiguration.CardanGimbal)]
    [InlineData(MountConfiguration.FlexureGimbal)]
    public void GimbalMount_RealConfigs_ReportFinitePositiveStiffness(MountConfiguration config)
    {
        var r = GimbalMount.Evaluate(config, thrust_N: 500, material: WallMaterials.CuCrZr);
        Assert.True(double.IsFinite(r.Stiffness_Nm_per_rad));
        Assert.True(r.Stiffness_Nm_per_rad > 0);
        Assert.True(r.BearingStress_MPa > 0);
    }

    [Fact]
    public void GimbalMount_PinJoint_StiffnessExceedsCardan_AtSameThrust()
    {
        // Cardan is two stages in series, so for equal pin geometry it
        // should have roughly half the stiffness of a single pin joint.
        var pin    = GimbalMount.Evaluate(MountConfiguration.PinJointGimbal,
                                          thrust_N: 1000, material: WallMaterials.CuCrZr);
        var cardan = GimbalMount.Evaluate(MountConfiguration.CardanGimbal,
                                          thrust_N: 1000, material: WallMaterials.CuCrZr);
        Assert.True(pin.Stiffness_Nm_per_rad > cardan.Stiffness_Nm_per_rad);
    }

    [Fact]
    public void GimbalMount_LargeThrust_FlagsUnacceptable_OnWeakerMaterial()
    {
        // 50 kN on a CuCrZr pin joint of Ø10×20 mm → bearing stress
        // 50000 / (0.010·0.020) = 2.5e8 Pa = 250 MPa. With σ_y ≈ 280
        // MPa that's marginal. Bump thrust to 150 kN to overwhelm.
        var r = GimbalMount.Evaluate(MountConfiguration.PinJointGimbal,
                                     thrust_N: 150_000, material: WallMaterials.CuCrZr);
        Assert.False(r.StressAcceptable);
    }

    [Fact]
    public void GenerateWith_Gimbal_SurfacesResult()
    {
        var (cond, design) = Baseline();
        design = design with { MountConfiguration = MountConfiguration.PinJointGimbal };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.GimbalMount);
        Assert.Equal(MountConfiguration.PinJointGimbal, gen.GimbalMount!.Configuration);
    }

    // ─────────────────────────────────────────────────────────────────
    //  PurgeFlowModel + PURGE_FLOW_INSUFFICIENT gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PurgeFlow_None_ReturnsZeroFlow()
    {
        var port = new PurgePort(PurgeLocation.ChamberPrePurge, PurgeFluid.None,
                                 MassFlow_kgs: 0.01, InletPressure_Pa: 5e6,
                                 BoreDiameter_mm: 2.0);
        var r = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 6.9e6);
        Assert.Equal(0.0, r.ActualMassFlow_kgs, precision: 6);
    }

    [Fact]
    public void PurgeFlow_InletAtOrBelowChamber_FailsToFlow()
    {
        var port = new PurgePort(PurgeLocation.ChamberPrePurge, PurgeFluid.GN2,
                                 MassFlow_kgs: 0.01, InletPressure_Pa: 5e6,
                                 BoreDiameter_mm: 2.0);
        // Chamber P above inlet P → cannot flow in.
        var r = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 7e6);
        Assert.Equal(0.0, r.ActualMassFlow_kgs, precision: 6);
        Assert.False(r.MeetsRequestedFlow);
    }

    [Fact]
    public void PurgeFlow_HighInletPressure_MeetsRequestedFlow()
    {
        // GN2 at 20 MPa through a 3 mm orifice against 6.9 MPa chamber
        // should deliver plenty of flow for a 0.005 kg/s request.
        var port = new PurgePort(PurgeLocation.InjectorDomeOx, PurgeFluid.GN2,
                                 MassFlow_kgs: 0.005, InletPressure_Pa: 20e6,
                                 BoreDiameter_mm: 3.0);
        var r = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 6.9e6);
        Assert.True(r.MeetsRequestedFlow,
            $"Expected 20 MPa GN2 through Ø3 mm to deliver ≥ 0.005 kg/s; got {r.ActualMassFlow_kgs:E2}");
    }

    [Fact]
    public void Gate_FiresOn_UnderSizedPurgePort()
    {
        var (cond, design) = Baseline();
        // Tiny bore can't meet a large mass-flow request at a reasonable inlet P.
        design = design with
        {
            PurgePorts = new[]
            {
                new PurgePort(PurgeLocation.InjectorDomeOx, PurgeFluid.GN2,
                              MassFlow_kgs: 0.5, InletPressure_Pa: 10e6,
                              BoreDiameter_mm: 0.5),
            },
        };
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);
        Assert.Contains(score.FeasibilityViolations,
            v => v.ConstraintId == "PURGE_FLOW_INSUFFICIENT");
    }

    [Fact]
    public void Gate_Skipped_WhenNoPurgePortsConfigured()
    {
        var (cond, design) = Baseline();
        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        Assert.NotNull(gen.PurgeResults);
        Assert.Empty(gen.PurgeResults!);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Schema migration v6 → v7
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_CurrentIsV7()
    {
        // Schema was later bumped to v8 for filter presets; this Phase-1
        // test just confirms v7 is still in the migration chain.
        Assert.Contains("v7", DesignPersistence.KnownSchemas);
    }

    [Fact]
    public void Schema_KnownSchemas_CoverV4ThroughV7()
    {
        Assert.Contains("v4", DesignPersistence.KnownSchemas);
        Assert.Contains("v5", DesignPersistence.KnownSchemas);
        Assert.Contains("v6", DesignPersistence.KnownSchemas);
        Assert.Contains("v7", DesignPersistence.KnownSchemas);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Cross-cutting: defaults preserve legacy behaviour
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RegenChamberDesign_NewFields_DefaultToNoOp()
    {
        var d = new RegenChamberDesign();
        Assert.Equal(0.0, d.FuelDomeDepth_mm);
        Assert.Equal(0.0, d.OxDomeDepth_mm);
        Assert.False(d.IncludeAntiVortexBaffle);
        Assert.Equal(MountConfiguration.FixedFlange, d.MountConfiguration);
        Assert.Empty(d.PurgePorts);
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

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
}
