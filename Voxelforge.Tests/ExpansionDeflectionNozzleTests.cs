// ExpansionDeflectionNozzleTests.cs — E-D nozzle physics model (OOB-13, issue #213).
//
// Covers:
//   · Topology classification + predicate correctness (IsExpansionDeflection, IsChannelStyle, etc.)
//   · Cowl radius inflation: contour ThroatRadius_mm ≈ round-throat radius × 1/√(1−0.40²)
//   · Advisory gate EXPANSION_DEFLECTION_PLUG_CLEARANCE fires on small designs
//   · Gate registration (GateRegistry.ById, severity = Advisory, kind = ManufacturabilityFloor)
//   · Schema v24 → v25 identity migration round-trip
//
// Voxel geometry tests deliberately absent — PicoGK + xUnit pitfall (#8 in CLAUDE.md).

using System.IO;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class ExpansionDeflectionNozzleTests
{
    // ── Topology classification ──────────────────────────────────────────

    [Fact]
    public void ClassifyFamily_ExpansionDeflection_IsExpanderDeflector()
        => Assert.Equal(ChannelTopologyDispatcher.Family.ExpanderDeflector,
            ChannelTopologyDispatcher.ClassifyFamily(ChannelTopology.ExpansionDeflection));

    [Fact]
    public void IsExpansionDeflection_TrueOnlyForEdTopology()
    {
        Assert.True(ChannelTopologyDispatcher.IsExpansionDeflection(ChannelTopology.ExpansionDeflection));
        Assert.False(ChannelTopologyDispatcher.IsExpansionDeflection(ChannelTopology.Aerospike));
        Assert.False(ChannelTopologyDispatcher.IsExpansionDeflection(ChannelTopology.LinearAerospike));
        Assert.False(ChannelTopologyDispatcher.IsExpansionDeflection(ChannelTopology.Axial));
        Assert.False(ChannelTopologyDispatcher.IsExpansionDeflection(ChannelTopology.None));
    }

    [Fact]
    public void IsAerospike_FalseForExpansionDeflection()
        => Assert.False(ChannelTopologyDispatcher.IsAerospike(ChannelTopology.ExpansionDeflection),
            "E-D nozzle is not in the aerospike family — it uses the regen bell pipeline.");

    [Fact]
    public void IsChannelStyle_TrueForExpansionDeflection()
        => Assert.True(ChannelTopology.ExpansionDeflection.IsChannelStyle(),
            "E-D outer bell runs a regen channel phase — Fast Preview must not cloak it to None.");

    [Fact]
    public void HasChannelPhase_TrueForExpansionDeflection()
        => Assert.True(ChannelTopologyDispatcher.HasChannelPhase(ChannelTopology.ExpansionDeflection),
            "HasChannelPhase must agree with IsChannelStyle for E-D topology.");

    // ── Cowl radius inflation ────────────────────────────────────────────

    // EdCowlRadiusMultiplier = 1 / √(1 − 0.40²) ≈ 1.09109 (Angelino inner/outer 40 %)
    private static readonly double ExpectedMultiplier =
        1.0 / System.Math.Sqrt(1.0 - 0.40 * 0.40);

    private static (OperatingConditions cond, RegenChamberDesign design) SmallDesign() =>
        (new OperatingConditions
         {
             Thrust_N              = 500,
             ChamberPressure_Pa    = 6.9e6,
             MixtureRatio          = 3.3,
             PropellantPair        = PropellantPair.LOX_CH4,
             CoolantInletTemp_K    = 150,
             CoolantInletPressure_Pa = 12e6,
         },
         new RegenChamberDesign
         {
             ChannelTopology      = ChannelTopology.ExpansionDeflection,
             ContourStationCount  = 40,
             IncludeManifolds     = false,
             IncludePorts         = false,
             IncludeInjectorFlange = false,
         });

    [Fact]
    public void GenerateWith_EdTopology_InflatesCowlRadiusByExpectedFactor()
    {
        // The contour ThroatRadius_mm for E-D must be the standard round-throat
        // radius multiplied by 1/√(1−0.40²) ≈ 1.0911 so the annular area equals
        // the full round-throat area (same thrust / Pc / Cf relationship).
        var (cond, design) = SmallDesign();

        // Reference: compute the round-throat radius independently.
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        double expectedCowl = derived.ThroatRadius_mm * ExpectedMultiplier;

        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        Assert.InRange(gen.Contour.ThroatRadius_mm,
            expectedCowl * 0.999, expectedCowl * 1.001);
    }

    [Fact]
    public void GenerateWith_NonEdTopology_DoesNotInflateCowlRadius()
    {
        // Bell (None) must pass the throat radius through unchanged.
        var (cond, baseDes) = SmallDesign();
        var design = baseDes with { ChannelTopology = ChannelTopology.None };

        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);

        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        Assert.InRange(gen.Contour.ThroatRadius_mm,
            derived.ThroatRadius_mm * 0.999, derived.ThroatRadius_mm * 1.001);
    }

    [Fact]
    public void GenerateWith_EdTopology_ReturnsCowlRadiusLargerThanRoundThroat()
    {
        // Sanity: E-D cowl radius must be strictly larger than the round-throat radius.
        var (cond, design) = SmallDesign();
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);

        var gen = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);

        Assert.True(gen.Contour.ThroatRadius_mm > derived.ThroatRadius_mm,
            $"E-D cowl radius {gen.Contour.ThroatRadius_mm:F3} must exceed round-throat "
          + $"radius {derived.ThroatRadius_mm:F3} mm.");
    }

    // ── Advisory gate ────────────────────────────────────────────────────

    [Fact]
    public void GateRegistry_ContainsEdPlugClearanceGate()
    {
        var ids = new System.Collections.Generic.HashSet<string>(
            System.Linq.Enumerable.Select(GateRegistry.All, g => g.Id),
            System.StringComparer.Ordinal);
        Assert.Contains("EXPANSION_DEFLECTION_PLUG_CLEARANCE", ids);
    }

    [Fact]
    public void EdPlugClearanceGate_IsAdvisory_WithManufacturabilityFloorKind()
    {
        var gate = GateRegistry.ById("EXPANSION_DEFLECTION_PLUG_CLEARANCE");
        Assert.Equal(GateSeverity.Advisory, gate.Severity);
        Assert.Equal(GateKind.ManufacturabilityFloor, gate.Kind);
    }

    [Fact]
    public void EdPlugClearanceGate_FiresOnSmall500NDesign()
    {
        // 500 N LOX/CH4 at 6.9 MPa → round-throat radius ≈ 3.9 mm → cowl radius
        // ≈ 3.9 × 1.091 ≈ 4.3 mm, well below the 12 mm advisory floor.
        // The gate must fire and include the advisory constraint.
        var (cond, design) = SmallDesign();
        var gen   = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.True(
            System.Linq.Enumerable.Any(score.FeasibilityViolations,
                v => v.ConstraintId == "EXPANSION_DEFLECTION_PLUG_CLEARANCE"),
            "Advisory gate must fire on a 500 N design whose E-D cowl radius << 12 mm advisory floor.");
    }

    [Fact]
    public void EdPlugClearanceGate_SilentForNonEdTopology()
    {
        // Gate must be a no-op for plain bell topology — check the constraint
        // does NOT appear in the violation list.
        var (cond, baseDes) = SmallDesign();
        var design = baseDes with { ChannelTopology = ChannelTopology.None };

        var gen   = RegenChamberOptimization.GenerateWith(cond, design, skipVoxelGeometry: true);
        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.DoesNotContain(score.FeasibilityViolations,
            v => v.ConstraintId == "EXPANSION_DEFLECTION_PLUG_CLEARANCE");
    }

    // ── Schema v24 → v25 identity migration ────────────────────────────

    [Fact]
    public void DesignPersistence_RoundTripsEdTopology()
    {
        var cond = new OperatingConditions
        {
            Thrust_N = 5_000, ChamberPressure_Pa = 6.9e6, MixtureRatio = 3.3,
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.ExpansionDeflection,
        };

        using var tmp = TestTempFile.WithUniqueName("ed-roundtrip", "json");
        DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.Equal(ChannelTopology.ExpansionDeflection, loaded.Design!.ChannelTopology);
    }

    [Fact]
    public void DesignPersistence_PreV25File_LoadsWithCurrentSchema()
    {
        // A v24 file must climb to v25 via the identity migration.
        const string v24Json = """
            {
              "Schema": "v24",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 5000,
                "ChamberPressure_Pa": 6900000,
                "MixtureRatio": 3.3,
                "WallMaterialIndex": 1
              },
              "Design": { "ChamberRadius_mm": 30, "ThroatRadius_mm": 15 }
            }
            """;

        using var tmp = TestTempFile.WithUniqueName("ed-pre-v25", "json");
        File.WriteAllText(tmp.Path, v24Json);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        // v24 has no ExpansionDeflection usage so topology defaults to Axial.
        Assert.Equal(ChannelTopology.Axial, loaded.Design!.ChannelTopology);
    }
}
