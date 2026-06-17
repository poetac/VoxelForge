// LinearAerospikeTests.cs — Sprint 26 (2026-04-23):
// Coverage for the linear (extruded-rectangular) aerospike topology.
//
//   1. Contour generator produces a rectangular-slot throat area,
//      IsLinear=true, PlugWidth_mm + LinearAspectRatio set.
//   2. BuildLinearPhysicsOnly populates the same AerospikeBuildResult
//      surface the axisymmetric path uses — contour, derived
//      dimensions, volume, mass.
//   3. Aspect-ratio feasibility gate fires below floor (0.20) and
//      above ceiling (6.0), and stays silent inside the [0.30, 5.00]
//      envelope.
//   4. Schema v15 → v16 round-trip preserves the two new design
//      fields.
//   5. End-to-end dispatch: AerospikeOptimization.BuildAndEvaluate
//      routes LinearAerospike designs to the linear builder.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;

namespace Voxelforge.Tests;

public class LinearAerospikeTests
{
    // ═══════════════════════════════════════════════════════════════
    //   Contour generator
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LinearContour_Generate_ProducesSlotThroatAreaAndLinearMetadata()
    {
        var c = LinearAerospikeContourGenerator.Generate(
            throatHeight_mm: 10.0,
            plugWidth_mm:    40.0,
            expansionRatio:  15.0,
            plugLengthRatio: 0.30,
            gamma:           1.15);

        Assert.True(c.IsLinear);
        Assert.Equal(40.0, c.PlugWidth_mm, precision: 6);
        // Throat area = 2 · h · W (bilateral-symmetric slots).
        Assert.Equal(2.0 * 10.0 * 40.0, c.ThroatAnnulusArea_mm2, precision: 3);
        Assert.True(c.PlugFullLength_mm > 0);
        Assert.True(c.PlugTruncatedLength_mm > 0);
        // Aspect ratio = plug length / plug width.
        Assert.Equal(c.PlugTruncatedLength_mm / 40.0, c.LinearAspectRatio, precision: 6);
    }

    [Fact]
    public void AxisymmetricContour_Generate_LeavesLinearFieldsAtDefault()
    {
        // Regression guard: the added init-only fields on AerospikeContour
        // must default to false/0 on the existing axisymmetric generator
        // so pre-Sprint-26 call sites are bit-identical.
        var c = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 10.0,
            expansionRatio:       15.0);

        Assert.False(c.IsLinear);
        Assert.Equal(0.0, c.PlugWidth_mm, precision: 6);
        Assert.Equal(0.0, c.LinearAspectRatio, precision: 6);
    }

    // ═══════════════════════════════════════════════════════════════
    //   BuildLinearPhysicsOnly
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildLinearPhysicsOnly_PopulatesPhysicsSurface_WithLinearContour()
    {
        var spec = new AerospikeSpec(
            Thrust_N:           20_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            IsLinear:           true,
            LinearPlugWidth_mm: 60.0);

        var r = AerospikeBuilder.BuildLinearPhysicsOnly(spec);

        Assert.Null(r.Voxels);                       // rectangular plug voxelisation deferred
        Assert.NotNull(r.Contour);
        Assert.True(r.Contour.IsLinear);
        Assert.Equal(60.0, r.Contour.PlugWidth_mm, precision: 6);
        Assert.True(r.ThroatOuterRadius_mm > 0);     // plug-tip half-height
        Assert.True(r.ChamberRadius_mm > r.ThroatOuterRadius_mm,
            "circular pre-chamber must be wider than plug-tip half-height");
        Assert.True(r.PlugTruncatedLength_mm > 0);
        Assert.True(r.SolidVolume_mm3 > 0);
        Assert.True(r.EstimatedMass_g > 0);
        Assert.Contains("Linear aerospike", r.Description);
        Assert.Null(r.Thermal);   // IncludeRegenChannels default false
    }

    [Fact]
    public void BuildLinearPhysicsOnly_WithRegenChannels_PopulatesThermal()
    {
        // Sprint 15 plug-channel cooling opt-in must work end-to-end on
        // the linear topology — the thermal solver's wetted-area branch
        // on contour.IsLinear produces a non-null thermal result with
        // positive peak wall T + non-zero coolant outlet temperature rise.
        var spec = new AerospikeSpec(
            Thrust_N:               20_000.0,
            ChamberPressure_Pa:     7e6,
            ExpansionRatio:         15.0,
            PlugLengthRatio:        0.30,
            PropellantPair:         PropellantPair.LOX_CH4,
            IncludeRegenChannels:   true,
            PlugChannelCount:       24,
            PlugChannelWidth_mm:    2.5,
            PlugChannelDepth_mm:    2.0,
            IsLinear:               true,
            LinearPlugWidth_mm:     60.0);

        var r = AerospikeBuilder.BuildLinearPhysicsOnly(spec);

        Assert.NotNull(r.Thermal);
        Assert.True(r.Thermal!.PeakGasSideWallT_K > 0,
            $"expected positive peak wall T, got {r.Thermal.PeakGasSideWallT_K}");
        Assert.True(r.Thermal.CoolantOutletT_K >= spec.CoolantInletTemp_K,
            "coolant outlet must be ≥ inlet — heat is being picked up");
        Assert.True(r.Thermal.TotalHeatLoad_W > 0);
    }

    [Fact]
    public void BuildLinearPhysicsOnly_RejectsAxisymmetricSpec()
    {
        // Wrong-entry-point guard: calling BuildLinearPhysicsOnly with
        // IsLinear=false should throw rather than silently producing a
        // half-and-half axisymmetric result.
        var spec = new AerospikeSpec(
            Thrust_N:           20_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30,
            PropellantPair:     PropellantPair.LOX_CH4,
            IsLinear:           false);

        Assert.Throws<System.ArgumentException>(() =>
            AerospikeBuilder.BuildLinearPhysicsOnly(spec));
    }

    // ═══════════════════════════════════════════════════════════════
    //   LINEAR_AEROSPIKE_ASPECT_RATIO feasibility gate
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AspectRatioGate_Silent_InsideBand()
    {
        // Aspect ≈ 1.4 — well inside [0.30, 5.00]. Achieved with a full-
        // spike plug (PlugLengthRatio = 1.0) at a moderate transverse
        // width: the Angelino plug-length formula
        //   L_full = R_o · cot(arcsin(1/M_e))
        // gives L_full ≈ 3.3 × R_o at γ=1.15 / ε=15 (the design point
        // here). A 100 mm width against a 20 kN @ 7 MPa thrust lands the
        // full-spike length around the same order as the width.
        // Pre-#548-B the formula was R_o·(ε−1)/(2·tan(ν_e)) which gave
        // a 3× shorter plug — the test passed at PlugWidth=20 mm under
        // that broken formula. Width updated to 100 mm with the
        // corrected formula.
        var design = new RegenChamberDesign
        {
            ChannelTopology             = ChannelTopology.LinearAerospike,
            PlugLengthRatio             = 1.0,
            ExpansionRatio              = 15.0,
            LinearAerospikePlugWidth_mm = 100.0,
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.True(build.Contour.IsLinear);
        Assert.InRange(build.Contour.LinearAspectRatio,
            LinearAerospikeContourGenerator.MinAspectRatio,
            LinearAerospikeContourGenerator.MaxAspectRatio);
        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "LINEAR_AEROSPIKE_ASPECT_RATIO");
    }

    [Fact]
    public void AspectRatioGate_Fires_BelowFloor()
    {
        // Super-wide plug at short truncation forces aspect < 0.30
        // (side-wall recirculation regime). The gate must fire with
        // the observed aspect reported as ActualValue.
        var design = new RegenChamberDesign
        {
            ChannelTopology             = ChannelTopology.LinearAerospike,
            PlugLengthRatio             = 0.15,    // heavily truncated
            ExpansionRatio              = 8.0,
            LinearAerospikePlugWidth_mm = 200.0,   // wide
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.True(build.Contour.LinearAspectRatio < LinearAerospikeContourGenerator.MinAspectRatio,
            $"test fixture should produce aspect < floor; got {build.Contour.LinearAspectRatio}");
        var v = Assert.Single(feas.Violations,
            x => x.ConstraintId == "LINEAR_AEROSPIKE_ASPECT_RATIO");
        Assert.Equal(build.Contour.LinearAspectRatio, v.ActualValue, precision: 6);
        Assert.Equal(LinearAerospikeContourGenerator.MinAspectRatio, v.Limit, precision: 6);
    }

    [Fact]
    public void AspectRatioGate_Fires_AboveCeiling()
    {
        // Full-spike plug at narrow transverse width pushes aspect
        // well above 5.00 (long-span cantilever regime).
        // (Expansion ratio stays moderate — extreme ε at γ=1.15 pushes
        // ν_exit toward π/2 where tan(ν) explodes and the plug length
        // formula becomes degenerate; 15 keeps us in the Angelino
        // model's physically meaningful band.)
        var design = new RegenChamberDesign
        {
            ChannelTopology             = ChannelTopology.LinearAerospike,
            PlugLengthRatio             = 1.0,     // full-spike
            ExpansionRatio              = 15.0,
            LinearAerospikePlugWidth_mm = 10.0,    // very narrow
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var (build, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.True(build.Contour.LinearAspectRatio > LinearAerospikeContourGenerator.MaxAspectRatio,
            $"test fixture should produce aspect > ceiling; got {build.Contour.LinearAspectRatio}");
        var v = Assert.Single(feas.Violations,
            x => x.ConstraintId == "LINEAR_AEROSPIKE_ASPECT_RATIO");
        Assert.Equal(build.Contour.LinearAspectRatio, v.ActualValue, precision: 6);
        Assert.Equal(LinearAerospikeContourGenerator.MaxAspectRatio, v.Limit, precision: 6);
    }

    [Fact]
    public void AspectRatioGate_Silent_OnAxisymmetricTopology()
    {
        // The gate must be topology-gated — an axisymmetric aerospike
        // with absurd plug-length parameters should not trigger a
        // LINEAR_AEROSPIKE_ASPECT_RATIO violation (it's a non-linear
        // contour; the gate has no opinion).
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            PlugLengthRatio = 0.30,
            ExpansionRatio  = 15.0,
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var (_, feas) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.DoesNotContain(feas.Violations,
            v => v.ConstraintId == "LINEAR_AEROSPIKE_ASPECT_RATIO");
    }

    // ═══════════════════════════════════════════════════════════════
    //   Schema round-trip (v16 migration)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DesignPersistence_RoundTripsLinearAerospikeFields()
    {
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };
        var design = new RegenChamberDesign
        {
            ChannelTopology             = ChannelTopology.LinearAerospike,
            LinearAerospikePlugWidth_mm = 85.5,
            LinearAerospikeAspectRatio  = 2.25,
            ExpansionRatio              = 20.0,
        };

        using var tmp = TestTempFile.Create();
        DesignPersistence.Save(tmp.Path, cond, design, r: null);
        var loaded = DesignPersistence.Load(tmp.Path);

        Assert.NotNull(loaded);
        Assert.Equal(Voxelforge.IO.DesignPersistence.CurrentSchemaVersion, loaded!.Schema);
        Assert.Equal(ChannelTopology.LinearAerospike, loaded.Design!.ChannelTopology);
        Assert.Equal(85.5, loaded.Design.LinearAerospikePlugWidth_mm, precision: 6);
        Assert.Equal(2.25, loaded.Design.LinearAerospikeAspectRatio,  precision: 6);
    }

    // ═══════════════════════════════════════════════════════════════
    //   End-to-end dispatch via AerospikeOptimization
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildAndEvaluate_DispatchesLinearTopology_ToLinearBuilder()
    {
        // Catches a silent-regression failure where LinearAerospike
        // topology accidentally routes to BuildPhysicsOnly (producing
        // an axisymmetric contour with IsLinear=false). Both topologies
        // must share the AerospikeBuildResult record but the contour's
        // IsLinear flag is the signal the thermal solver / gate rely on.
        var axisymmetricDesign = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            ExpansionRatio  = 15.0,
        };
        var linearDesign = axisymmetricDesign with
        {
            ChannelTopology             = ChannelTopology.LinearAerospike,
            LinearAerospikePlugWidth_mm = 60.0,
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var axi = AerospikeOptimization.BuildAndEvaluate(cond, axisymmetricDesign, new Voxelforge.Geometry.AerospikeBuilderAdapter());
        var lin = AerospikeOptimization.BuildAndEvaluate(cond, linearDesign, new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.False(axi.Build.Contour.IsLinear);
        Assert.True(lin.Build.Contour.IsLinear);
        Assert.Contains("Linear aerospike", lin.Build.Description);
        Assert.DoesNotContain("Linear aerospike", axi.Build.Description);
    }

    // ═══════════════════════════════════════════════════════════════
    //   RectangularPlugImplicit (Sprint 26 follow-on — voxel SDF)
    //
    //   These exercise the pure-math signed-distance function WITHOUT
    //   instantiating a PicoGK Library (ADR-005-safe) — just
    //   constructing an IImplicit and calling fSignedDistance for
    //   known reference points.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RectangularPlug_SignsInside_ForInteriorPoints()
    {
        // A full-spike linear plug at W = 40 mm. Origin (0, 0, 0)
        // sits at the throat tip, which is the leading edge of the
        // plug body — interior with large cross-section.
        var contour = LinearAerospikeContourGenerator.Generate(
            throatHeight_mm: 10.0,
            plugWidth_mm:    40.0,
            expansionRatio:  15.0,
            plugLengthRatio: 1.0,       // full spike (non-truncated tail)
            gamma:           1.15);
        var plug = new RectangularPlugImplicit(contour);

        // Station 0: throat tip, half-height ≈ 10 mm, half-width 20 mm.
        // Point (0.1 mm into +X, y=0, z=0) — well inside.
        float d = plug.fSignedDistance(new System.Numerics.Vector3(0.1f, 0f, 0f));
        Assert.True(d < 0f, $"expected interior (negative) SDF, got {d}");
    }

    [Fact]
    public void RectangularPlug_SignsOutside_ForExteriorPoints()
    {
        var contour = LinearAerospikeContourGenerator.Generate(
            throatHeight_mm: 10.0,
            plugWidth_mm:    40.0,
            expansionRatio:  15.0,
            plugLengthRatio: 1.0,
            gamma:           1.15);
        var plug = new RectangularPlugImplicit(contour);

        // Point far above the plug in +Y (well above throat half-height
        // 10 mm). Must report a positive distance.
        float dAbove = plug.fSignedDistance(new System.Numerics.Vector3(0.1f, 100f, 0f));
        Assert.True(dAbove > 0f, $"expected exterior (positive) SDF, got {dAbove}");
        // Point far outside transverse plug-width (|z| > 20 mm).
        float dSide = plug.fSignedDistance(new System.Numerics.Vector3(0.1f, 0f, 100f));
        Assert.True(dSide > 0f, $"expected exterior (positive) SDF, got {dSide}");
        // Point ahead of the throat (x < 0) is outside the plug.
        float dAhead = plug.fSignedDistance(new System.Numerics.Vector3(-50f, 0f, 0f));
        Assert.True(dAhead > 0f, $"expected exterior (positive) SDF, got {dAhead}");
        // Point behind the truncation (x > xMax).
        float dBehind = plug.fSignedDistance(new System.Numerics.Vector3(
            (float)(contour.PlugTruncatedLength_mm + 50.0), 0f, 0f));
        Assert.True(dBehind > 0f, $"expected exterior (positive) SDF, got {dBehind}");
    }

    [Fact]
    public void RectangularPlug_SurfaceDistance_IsSmallNearWall()
    {
        // Points a few mm outside the plug surface should report
        // distances on the same order. Sprint 31 (PH-1): the area-Mach
        // back-solve seats the plug surface at R_i = 0.4·R_o ≈ 4 mm
        // at the throat (the OLD linear-cone formula had a station-0
        // discontinuity from R_i to R_o). Sample at y = 6 mm
        // (≈ R_i + 2) so the SDF lands around 2 mm.
        var contour = LinearAerospikeContourGenerator.Generate(
            throatHeight_mm: 10.0,
            plugWidth_mm:    40.0,
            expansionRatio:  15.0,
            plugLengthRatio: 1.0,
            gamma:           1.15);
        var plug = new RectangularPlugImplicit(contour);

        // Station ~= 0 (throat tip). Plug r ≈ 4 mm; y = 4 + 2 = 6 mm.
        float d = plug.fSignedDistance(new System.Numerics.Vector3(0.1f, 6f, 0f));
        Assert.InRange(d, 0.5f, 4.0f);
    }

    [Fact]
    public void RectangularPlug_RejectsAxisymmetricContour()
    {
        // Wrong-contour-topology guard — RectangularPlugImplicit must
        // refuse an axisymmetric AerospikeContour (IsLinear=false) so
        // a caller can't silently produce a malformed plug body.
        var contour = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 10.0,
            expansionRatio:       15.0);
        Assert.Throws<System.ArgumentException>(() =>
            new RectangularPlugImplicit(contour));
    }

    // ═══════════════════════════════════════════════════════════════
    //   P7 (2026-04-29) — cached AerospikeBuildResult short-circuit
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void P7_GenerateWith_UsesCachedAerospikeResult_WhenSupplied()
    {
        // Pre-PH7 monolithic-aerospike export ran AerospikeOptimization
        // .BuildAndEvaluate twice — once via AerospikeBuilder.Build inside
        // MonolithicEngineBuilder.BuildAerospikeCore, then again via
        // GenerateWith's `Aerospike = ...BuildAndEvaluate(...).Build` line.
        // P7 adds a `cachedAerospikeResult` parameter to GenerateWith so
        // the caller can pass the already-computed result and skip the
        // duplicate ~50-200 ms solve. This test proves the short-circuit:
        // when the cache is supplied, result.Aerospike IS that exact
        // reference.
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            PlugLengthRatio = 0.30,
            ExpansionRatio  = 15.0,
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var (build, _) = AerospikeOptimization.BuildAndEvaluate(cond, design, new Voxelforge.Geometry.AerospikeBuilderAdapter());
        var result = RegenChamberOptimization.GenerateWith(
            cond, design,
            skipVoxelGeometry: true, skipMfgAnalysis: true,
            cachedAerospikeResult: build);

        Assert.Same(build, result.Aerospike);
    }

    [Fact]
    public void P7_GenerateWith_DefaultsToFreshSolve_WhenCacheNotProvided()
    {
        // Back-compat: default null cache → GenerateWith calls
        // BuildAndEvaluate itself and produces a non-null Aerospike result.
        var design = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.Aerospike,
            PlugLengthRatio = 0.30,
            ExpansionRatio  = 15.0,
        };
        var cond = new OperatingConditions { Thrust_N = 20_000.0 };

        var result = RegenChamberOptimization.GenerateWith(
            cond, design,
            skipVoxelGeometry: true, skipMfgAnalysis: true,
            aerospikeBuilder: new Voxelforge.Geometry.AerospikeBuilderAdapter());

        Assert.NotNull(result.Aerospike);
        Assert.True(result.Aerospike!.Contour.PlugLengthRatio > 0);
    }
}
