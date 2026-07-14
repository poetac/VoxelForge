// AerospikeGateWiringTests.cs — Red-team round 4: RegenChamberOptimization
// .Evaluate only called FeasibilityGate.Evaluate (the regen-family gate
// system), never AerospikeFeasibility.Evaluate, so the 5 aerospike-specific
// gates (plug wall temp, coolant cavitation, element clearance, injector
// face temp, linear aspect ratio) were never actually checked when an
// aerospike-topology design went through the main GenerateWith -> Evaluate
// SA scoring path — only the standalone AerospikeOptimization.BuildAndEvaluate
// convenience method invoked them. Confirmed here by injecting a hot-wall
// AerospikeThermalResult onto an otherwise-normal regen baseline via `with`,
// mirroring FeasibilityGateTests' SafeResult() injection pattern and
// NoyronTierC1Phase2Tests' AerospikeFeasibility fixture construction.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class AerospikeGateWiringTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 1,   // CuCrZr: MaxServiceTemp_K = 800 K
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    private static RegenGenerationResult BaselineResult() =>
        RegenChamberOptimization.GenerateWith(
            DefaultConditions(),
            new RegenChamberDesign
            {
                IncludeManifolds      = false,
                IncludePorts          = false,
                IncludeInjectorFlange = false,
                ContourStationCount   = 60,
            });

    private static AerospikeBuildResult MakeBuildResultWithThermal(AerospikeThermalResult thermal)
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        return new AerospikeBuildResult(
            Voxels:                 null,
            Contour:                contour,
            ThroatOuterRadius_mm:   30.0,
            ThroatInnerRadius_mm:   12.0,
            PlugTruncatedLength_mm: contour.PlugTruncatedLength_mm,
            ChamberRadius_mm:       75.0,
            ChamberLength_mm:       90.0,
            TotalLength_mm:         90.0 + contour.PlugTruncatedLength_mm,
            TotalDiameter_mm:       150.0,
            SolidVolume_mm3:        100_000,
            EstimatedMass_g:        800,
            Description:            "test fixture",
            Thermal:                thermal);
    }

    private static AerospikeThermalResult HotWallThermal() => new(
        GasSideWallT_K:         new[] { 500.0, 1200.0, 1300.0 },
        CoolantBulkT_K:         new[] { 120.0, 160.0, 200.0 },
        HeatFlux_Wm2:           new[] { 1e6, 5e6, 6e6 },
        PeakGasSideWallT_K:     1300.0,  // CuCrZr limit 800 K -> fail
        PeakStation_X_mm:       22.0,
        CoolantOutletT_K:       210.0,
        CoolantPressureDrop_Pa: 1e5,
        TotalHeatLoad_W:        10000,
        Warnings:               System.Array.Empty<string>());

    private static AerospikeThermalResult CoolWallThermal() => new(
        GasSideWallT_K:         new[] { 500.0, 550.0, 600.0 },
        CoolantBulkT_K:         new[] { 120.0, 160.0, 200.0 },
        HeatFlux_Wm2:           new[] { 1e6, 1.5e6, 2e6 },
        PeakGasSideWallT_K:     600.0,   // CuCrZr limit 800 K -> pass
        PeakStation_X_mm:       20.0,
        CoolantOutletT_K:       200.0,
        CoolantPressureDrop_Pa: 1e5,
        TotalHeatLoad_W:        5000,
        Warnings:               System.Array.Empty<string>());

    [Fact]
    public void Evaluate_AerospikeHotWall_FiresAerospikePlugWallTempViolation()
    {
        var withHotAerospike = BaselineResult() with { Aerospike = MakeBuildResultWithThermal(HotWallThermal()) };

        var score = RegenChamberOptimization.Evaluate(withHotAerospike, RegenChamberOptimization.Profiles[0]);

        Assert.Contains(score.FeasibilityViolations, v => v.ConstraintId == "AEROSPIKE_PLUG_WALL_TEMP");
    }

    [Fact]
    public void Evaluate_AerospikeCoolWall_DoesNotFireAerospikeGates()
    {
        var withCoolAerospike = BaselineResult() with { Aerospike = MakeBuildResultWithThermal(CoolWallThermal()) };

        var score = RegenChamberOptimization.Evaluate(withCoolAerospike, RegenChamberOptimization.Profiles[0]);

        Assert.DoesNotContain(score.FeasibilityViolations,
            v => v.ConstraintId.StartsWith("AEROSPIKE_", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_NonAerospikeBaseline_UnaffectedByAerospikeGateCheck()
    {
        // gen.Aerospike stays null for a regular (non-aerospike) design — the
        // new AerospikeFeasibility.Evaluate call must not fire, throw, or
        // otherwise disturb the existing regen-gate violation set.
        var baseline = BaselineResult();
        Assert.Null(baseline.Aerospike);

        var score = RegenChamberOptimization.Evaluate(baseline, RegenChamberOptimization.Profiles[0]);

        Assert.DoesNotContain(score.FeasibilityViolations,
            v => v.ConstraintId.StartsWith("AEROSPIKE_", System.StringComparison.Ordinal));
    }
}
