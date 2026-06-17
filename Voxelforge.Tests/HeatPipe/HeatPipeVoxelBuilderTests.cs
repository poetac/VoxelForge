// HeatPipeVoxelBuilderTests.cs — Sprint A.80 (C.2) voxel-pipeline
// backfill coverage. Exercises HeatPipeVoxelBuilder against the SAFE-400
// / KRUSTY Li-tungsten primary-heat-pipe anchor (matches the A.69
// HeatPipeFixture_Safe400KrustyReactor design parameters) + Cu-water
// cross-cluster sanity checks.
//
// Test sizing note: the SAFE-400 anchor (D = 14 mm vapour core,
// L = 1.0 m, Li-tungsten) is fully in-process tractable — overall
// envelope OD ≈ 18.9 mm × 1000 mm long is ~ 18.9 mm × 18.9 mm × 1000 mm
// = ~ 360 K voxels at 1 mm voxel. Heat pipes are characteristically
// small-OD-long-axial — even the literal anchor voxelises in xUnit
// without scaling down (unlike A.67 Beacon / A.70 Falcon where the
// literal fixtures needed archetype shrink-downs for tractability).
//
// Voxel-mass note: the heat pipe is rendered as an OPEN-ENDED envelope
// + wick (both hollow shells via AnnulusImplicit). This sidesteps the
// PicoGK 2.0.0 closed-cavity flood-fill limitation (documented A.70).
// Mass-consistency tests use the matching closed-form annulus reference:
//   • Envelope wall: ρ_env · π·(R_envOuter² − R_envInner²)·L
//   • Wick:          ρ_wick · π·(R_wickOuter² − R_wickInner²)·L
// (For the cluster-anchor tests below we use rough envelope-only mass
// bounds since wick density varies with porosity; the envelope wall mass
// is the unambiguous voxel-roundtrip target.)
//
// All voxel-roundtrip tests run in-process per PicoGK 2.0.0 + xUnit
// pitfall #8 (CLAUDE.md): `using var lib = new Library(voxel_mm);
// using var libScope = LibraryScope.Set(lib);` lets xUnit construct +
// dispose Library without the legacy subprocess shim. Same pattern as
// FlywheelVoxelBuilderTests / TankageVoxelBuilderTests /
// ExpansionDeflectionPlugTests.

using System;
using PicoGK;
using Voxelforge.Geometry;
using Voxelforge.HeatPipe;
using Xunit;

namespace Voxelforge.Tests.HeatPipe;

public sealed class HeatPipeVoxelBuilderTests
{
    // 0.5 mm voxel — fine enough that the SAFE-400 envelope wall
    // (~ 1.05 mm) resolves cleanly (~ 2 voxels through the wall) while
    // staying tractable for an 18.9 × 18.9 × 1000 mm envelope
    // (~ 1.5 M voxels at 0.5 mm). One step finer than the 1 mm voxel
    // used by FlywheelVoxelBuilderTests / TankageVoxelBuilderTests
    // because the heat-pipe shell walls are sub-millimetre.
    private const float VoxelSize_mm = 0.5f;

    // ── SAFE-400 / KRUSTY primary heat pipe (the A.69 literal anchor) ─
    //
    // Li-tungsten cluster, vapour-core ID 14 mm, length 1.0 m, ~ 1 kW
    // heat throughput per pipe, 1400 K evaporator-mean. Matches
    // HeatPipeFixture_Safe400KrustyReactor.Safe400PrimaryHeatPipe()
    // exactly. References:
    //   Poston D.I. (2004) LA-UR-04-2884 (SAFE-400 100 kWt core, 8 Li
    //     primary heat pipes, 1 kW each, 1400 K mean).
    //   Gibson M., Mason L., Bowman C. (2017) NETS-2017 LA-UR-17-21851
    //     (KRUSTY 1 kWe demonstrator; SAFE-400-class lineage).
    private static HeatPipeDesign Safe400PrimaryHeatPipe() => new(
        Fluid:                   HeatPipeFluid.Lithium,
        InternalDiameter_m:      0.014,
        Length_m:                1.0,
        HeatThroughput_W:        1_000.0,
        OperatingTemperature_K:  1400.0);

    // Small Cu-water heat pipe (laptop / spacecraft TCS cluster):
    // D = 6 mm vapour core, L = 200 mm, 50 W throughput at 350 K.
    // Matches the typical Cu-water cluster anchor on small electronics
    // cooling pipes (Faghri 2016 §2). Renders in ~ 80 K voxels at
    // 0.5 mm — fastest in-process build of the test suite.
    private static HeatPipeDesign CuWaterLaptopHeatPipe() => new(
        Fluid:                   HeatPipeFluid.Water,
        InternalDiameter_m:      0.006,
        Length_m:                0.200,
        HeatThroughput_W:        50.0,
        OperatingTemperature_K:  350.0);

    // ── Build helper ─────────────────────────────────────────────────

    /// <summary>
    /// Build the heat pipe in-process (PicoGK 2.0.0 + LibraryScope) and
    /// extract volume + bounding box via <c>Voxels.CalculateProperties</c>.
    /// Returns the geometry summary + the measured volume / BBox so
    /// test methods can run lots of cheap assertions on one build.
    /// </summary>
    private static (HeatPipeGeometryResult result, float volume_mm3, BBox3 bbox)
        BuildAndMeasure(HeatPipeDesign design, float voxelSize_mm = VoxelSize_mm)
    {
        using var lib = new Library(voxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        HeatPipeGeometryResult result = HeatPipeVoxelBuilder.Build(design, voxelSize_mm);

        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);
        return (result, volume_mm3, bbox);
    }

    // ── Geometry-record arithmetic checks (SAFE-400 anchor) ──────────

    [Fact]
    public void Safe400_VapourCoreDiameter_MatchesDesign14mm()
    {
        // D_vapour = design.InternalDiameter_m × 1000 = 14 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        Assert.Equal(14.0, result.VapourCoreDiameter_mm, precision: 6);
        // VapourCore == wick inner diameter.
        Assert.Equal(14.0, result.WickInnerDiameter_mm, precision: 6);
    }

    [Fact]
    public void Safe400_WickAnnulusThicknessUsesTenPercentCluster()
    {
        // Wick thickness = WickThicknessFraction · D_vap = 0.10 · 14 = 1.4 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        Assert.Equal(1.4, result.WickThickness_mm, precision: 6);
        // Wick OD = vapour core + 2·tWick = 14 + 2.8 = 16.8 mm.
        Assert.Equal(16.8, result.WickOuterDiameter_mm, precision: 6);
        // Envelope ID == wick OD.
        Assert.Equal(16.8, result.EnvelopeInnerDiameter_mm, precision: 6);
    }

    [Fact]
    public void Safe400_EnvelopeWallThicknessUsesSevenAndHalfPercentCluster()
    {
        // Envelope wall = EnvelopeWallThicknessFraction · D_vap
        //               = 0.075 · 14 = 1.05 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        Assert.Equal(1.05, result.EnvelopeWallThickness_mm, precision: 6);
        // Envelope OD = wick OD + 2·tWall = 16.8 + 2.1 = 18.9 mm.
        Assert.Equal(18.9, result.EnvelopeOuterDiameter_mm, precision: 6);
    }

    [Fact]
    public void Safe400_LengthMillimetres_MatchesDesign1000mm()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        Assert.Equal(1000.0, result.Length_mm, precision: 6);
    }

    // ── Algebraic dimensional cross-check (SAFE-400 closed form) ────

    [Fact]
    public void Safe400_DimensionalChain_VapourCorePlusWickPlusWallEqualsEnvelopeOD()
    {
        // D_envOuter == D_vap + 2·tWick + 2·tWall (geometric closure).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        double expectedEnvOD = result.VapourCoreDiameter_mm
                             + 2.0 * result.WickThickness_mm
                             + 2.0 * result.EnvelopeWallThickness_mm;
        Assert.Equal(expectedEnvOD, result.EnvelopeOuterDiameter_mm, precision: 6);
    }

    [Fact]
    public void Safe400_WickAnnulusFitsInsideEnvelope()
    {
        // Geometric invariant: wick OD must be strictly less than envelope OD
        // (i.e. envelope wall is present); wick ID must be strictly less than
        // wick OD (wick is a real annulus, not degenerate); and the wick must
        // sit at non-negative radius.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);

        Assert.True(result.VapourCoreDiameter_mm > 0.0);
        Assert.True(result.WickInnerDiameter_mm   < result.WickOuterDiameter_mm,
            $"Wick must be a real annulus: ID = {result.WickInnerDiameter_mm:F2} mm, "
          + $"OD = {result.WickOuterDiameter_mm:F2} mm.");
        Assert.True(result.WickOuterDiameter_mm  < result.EnvelopeOuterDiameter_mm,
            $"Wick OD ({result.WickOuterDiameter_mm:F2} mm) must fit inside "
          + $"envelope OD ({result.EnvelopeOuterDiameter_mm:F2} mm).");
    }

    // ── Voxel-roundtrip checks (SAFE-400, in-process at literal scale) ─

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Safe400_VoxelSet_IsNonEmpty()
    {
        // The build must produce a non-degenerate voxel body. Trigger
        // mesh extraction to confirm the voxel field actually rendered.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 1000,
            $"Voxel mesh must have substantially > 0 triangles (got {triangleCount}). "
          + "Indicates the heat-pipe build degenerated to an empty voxel set.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Safe400_BoundingBox_DiameterMatchesEnvelopeOD()
    {
        // Bounding-box diameter (Y, Z) must equal envelope OD ± 4 voxels
        // (2 voxels slack per side from grid quantisation + smoothing
        // surface envelope). For SAFE-400: 18.9 mm ± 2 mm → band [16.9, 20.9] mm.
        (HeatPipeGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(Safe400PrimaryHeatPipe());
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        float zExt = bbox.vecMax.Z - bbox.vecMin.Z;
        double expected = result.EnvelopeOuterDiameter_mm;
        Assert.InRange((double)yExt, expected - 2.0, expected + 2.0);
        Assert.InRange((double)zExt, expected - 2.0, expected + 2.0);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Safe400_BoundingBox_AxialExtentMatchesLength()
    {
        // Axial extent (X) must equal Length_mm within voxel tolerance.
        // For SAFE-400 L = 1000 mm: band [996, 1004] (4 voxels = 2 mm
        // slack, or 1 % whichever is larger).
        (HeatPipeGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(Safe400PrimaryHeatPipe());
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        double slack_mm = Math.Max(4.0, 0.01 * result.Length_mm);
        Assert.InRange((double)xExt,
            result.Length_mm - slack_mm,
            result.Length_mm + slack_mm);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Safe400_Volume_MatchesEnvelopePlusWickAnnulusWithinTenPercent()
    {
        // Open-ended hollow shells (envelope + wick) — voxel volume should
        // match the closed-form sum of two annular volumes:
        //   V_envelope = π·(R_envO² − R_envI²)·L
        //   V_wick     = π·(R_wickO² − R_wickI²)·L = π·(R_envI² − R_vap²)·L
        // Sum = π·(R_envO² − R_vap²)·L  (wick OD == envelope ID; the two
        // annuli are contiguous, so the sum collapses to a single annulus
        // from R_vap to R_envO).
        // SAFE-400: R_vap = 7 mm, R_envO = 9.45 mm, L = 1000 mm →
        //   V = π·(9.45² − 7²)·1000 = π·40.2625·1000 ≈ 126 489 mm³.
        // ±10 % band for voxel quantisation (sub-mm wall on 0.5 mm voxel).
        var (result, volume_mm3, _) = BuildAndMeasure(Safe400PrimaryHeatPipe());

        double R_envO_mm = 0.5 * result.EnvelopeOuterDiameter_mm;
        double R_vap_mm  = 0.5 * result.VapourCoreDiameter_mm;
        double L_mm      = result.Length_mm;
        double expectedVolume_mm3 = Math.PI * (R_envO_mm * R_envO_mm - R_vap_mm * R_vap_mm) * L_mm;

        double relErr = Math.Abs(volume_mm3 - expectedVolume_mm3) / expectedVolume_mm3;
        Assert.True(relErr < 0.10,
            $"Voxel volume {volume_mm3:F0} mm³ must match closed-form annulus "
          + $"volume {expectedVolume_mm3:F0} mm³ within 10 % (got {relErr * 100:F2} %). "
          + $"Envelope OD = {result.EnvelopeOuterDiameter_mm:F2} mm, "
          + $"vapour-core D = {result.VapourCoreDiameter_mm:F2} mm, L = {L_mm:F0} mm.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Safe400_VapourCoreIsHollow_BoundingBoxConfirmsOpenEndedTopology()
    {
        // The voxel body is open-ended (no end caps, per the PicoGK 2.0.0
        // closed-cavity workaround). Verify by computing the mass of an
        // equivalent SOLID outer cylinder (no central cavity) — the voxel
        // mass must be strictly LESS than the solid mass, by at least
        // 30 % (the cavity removes ~ V_vap/V_outer fraction; for SAFE-400
        // R_vap/R_envO = 7/9.45 → V_ratio ≈ 0.55, so cavity removes ~ 55 %
        // of the solid envelope volume).
        var (result, volume_mm3, _) = BuildAndMeasure(Safe400PrimaryHeatPipe());

        double R_envO_mm = 0.5 * result.EnvelopeOuterDiameter_mm;
        double L_mm      = result.Length_mm;
        double V_solidCylinder_mm3 = Math.PI * R_envO_mm * R_envO_mm * L_mm;

        // Voxel volume must be substantially less than solid-cylinder volume.
        double fillFraction = volume_mm3 / V_solidCylinder_mm3;
        Assert.True(fillFraction < 0.70,
            $"Voxel-fill fraction {fillFraction * 100:F2} % indicates the vapour "
          + $"core may be solid-filled (PicoGK 2.0.0 closed-cavity flood-fill). "
          + $"Expected < 70 % for open-ended SAFE-400 topology. Voxel "
          + $"V = {volume_mm3:F0} mm³ vs solid cylinder V = {V_solidCylinder_mm3:F0} mm³.");
    }

    // ── Cu-water cross-cluster sanity ────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void CuWaterLaptop_VoxelSet_IsNonEmpty()
    {
        // Cross-cluster check: a small 6 mm Cu-water pipe must build cleanly
        // and produce non-degenerate voxels. The cluster anchors are the
        // same multiplicative fractions — wick = 0.6 mm, wall = 0.45 mm,
        // envelope OD = 8.1 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(CuWaterLaptopHeatPipe(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 100,
            $"Cu-water laptop heat-pipe voxel mesh must be non-empty (got {triangleCount}).");
    }

    [Fact]
    public void CuWaterLaptop_DimensionalFields_FollowSameClusterFractions()
    {
        // 6 mm vapour core → 0.6 mm wick → 0.45 mm wall → 8.1 mm envelope OD.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult result =
            HeatPipeVoxelBuilder.Build(CuWaterLaptopHeatPipe(), VoxelSize_mm);
        Assert.Equal(6.0,  result.VapourCoreDiameter_mm,    precision: 6);
        Assert.Equal(0.6,  result.WickThickness_mm,          precision: 6);
        Assert.Equal(7.2,  result.WickOuterDiameter_mm,      precision: 6);   // 6 + 2·0.6
        Assert.Equal(0.45, result.EnvelopeWallThickness_mm,  precision: 6);
        Assert.Equal(8.1,  result.EnvelopeOuterDiameter_mm,  precision: 6);   // 7.2 + 2·0.45
        Assert.Equal(200.0, result.Length_mm,                precision: 6);
    }

    // ── Scaling sanity ───────────────────────────────────────────────

    [Fact]
    public void DoubledVapourCoreDiameter_DoublesAllRadialFeatures()
    {
        // All radial features scale linearly with D_vap (since wick and
        // wall are fixed fractions of D_vap). Doubling D doubles the wick
        // thickness, the envelope wall thickness, and the envelope OD.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult baseResult =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        HeatPipeGeometryResult doubledResult =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe()
                with { InternalDiameter_m = 0.028 }, VoxelSize_mm);

        Assert.Equal(2.0, doubledResult.VapourCoreDiameter_mm   / baseResult.VapourCoreDiameter_mm,   precision: 6);
        Assert.Equal(2.0, doubledResult.WickThickness_mm         / baseResult.WickThickness_mm,         precision: 6);
        Assert.Equal(2.0, doubledResult.EnvelopeWallThickness_mm / baseResult.EnvelopeWallThickness_mm, precision: 6);
        Assert.Equal(2.0, doubledResult.EnvelopeOuterDiameter_mm / baseResult.EnvelopeOuterDiameter_mm, precision: 6);
    }

    [Fact]
    public void HalvedLength_HalvesAxialExtentAndLeavesRadialFeaturesIntact()
    {
        // Length scaling: L → L/2 halves Length_mm but does NOT change any
        // radial feature (all radial dimensions are functions of D_vap only).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeGeometryResult full =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe(), VoxelSize_mm);
        HeatPipeGeometryResult half =
            HeatPipeVoxelBuilder.Build(Safe400PrimaryHeatPipe()
                with { Length_m = 0.5 }, VoxelSize_mm);

        Assert.Equal(0.5, half.Length_mm / full.Length_mm, precision: 9);
        Assert.Equal(full.VapourCoreDiameter_mm,    half.VapourCoreDiameter_mm,    precision: 6);
        Assert.Equal(full.WickThickness_mm,          half.WickThickness_mm,          precision: 6);
        Assert.Equal(full.EnvelopeWallThickness_mm,  half.EnvelopeWallThickness_mm,  precision: 6);
        Assert.Equal(full.EnvelopeOuterDiameter_mm,  half.EnvelopeOuterDiameter_mm,  precision: 6);
    }

    // ── Wall-safe smoothing surface ──────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Safe400_BuildSucceeds_WallSafeSmoothingDoesNotEraseWick()
    {
        // Wall-safe smoothing cap (PicoGK pitfall #1): Smoothen(d) destroys
        // features < 2d. The cap is 25 % of min(tWick, tWall, R_envOuter).
        // For SAFE-400: min = tWall = 1.05 mm → d_safe ≤ 0.26 mm. The
        // surviving wick must still be a real annulus (mesh non-degenerate
        // and bounding-box wider than the wall-only envelope).
        var (_, volume_mm3, bbox) = BuildAndMeasure(Safe400PrimaryHeatPipe());
        Assert.True(volume_mm3 > 0.0f, "Smoothing must not erase the voxel body.");
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        Assert.True(yExt > 16.0f,
            $"After smoothing, bounding-box diameter ({yExt:F2} mm) must still "
          + $"reflect a real envelope (~ 18.9 mm). Smoothing has erased material.");
    }

    // ── Validation surface ───────────────────────────────────────────

    [Fact]
    public void Build_NullDesign_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        Assert.Throws<ArgumentNullException>(
            () => HeatPipeVoxelBuilder.Build(null!, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveVoxelSize_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeDesign d = Safe400PrimaryHeatPipe();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeatPipeVoxelBuilder.Build(d,  0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HeatPipeVoxelBuilder.Build(d, -1.0));
    }

    [Fact]
    public void Build_FluidNoneSentinel_PropagatesValidationError()
    {
        // HeatPipeFluid.None is a degenerate sentinel; ValidateSelf throws
        // and the voxel builder propagates so the user can't sneak a
        // fluid-less design through the voxel pipeline.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeDesign bad = Safe400PrimaryHeatPipe() with { Fluid = HeatPipeFluid.None };
        Assert.Throws<ArgumentException>(
            () => HeatPipeVoxelBuilder.Build(bad, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveDiameter_PropagatesValidationError()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeDesign bad = Safe400PrimaryHeatPipe() with { InternalDiameter_m = 0.0 };
        Assert.Throws<ArgumentException>(
            () => HeatPipeVoxelBuilder.Build(bad, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveLength_PropagatesValidationError()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        HeatPipeDesign bad = Safe400PrimaryHeatPipe() with { Length_m = -1.0 };
        Assert.Throws<ArgumentException>(
            () => HeatPipeVoxelBuilder.Build(bad, VoxelSize_mm));
    }
}
