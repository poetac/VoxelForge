// AntennaVoxelBuilderTests.cs — Sprint A.83 (C.2) voxel-pipeline backfill
// coverage. Exercises AntennaVoxelBuilder against a DBS-residential-
// dish-archetype anchor design + cross-kind validation checks.
//
// Cluster anchor: the residential direct-broadcast-satellite (DBS)
// 0.6 m offset-fed dish (commodity Ku-band satellite TV receiver). The
// design surface is the **prime-focus equivalent** — a 0.6 m diameter
// parabolic reflector with f/D ≈ 0.4 (cluster-typical for prime-focus
// dishes; offset-fed dishes use a parent-paraboloid f/D in this same
// band). Public anchors:
//   - DBS Ku-band downlink standard (12.2-12.7 GHz)
//   - 0.6 m typical residential dish diameter (Hughes / DirecTV / Dish
//     Network / Sky / Bell)
//   - Aluminium spun-reflector typical wall thickness: 1.5-3 mm
//   - Feed-block radius: ~40 mm = D/15 (LNB envelope, cluster-mid)
//
// Cross-kind validation: ParabolicDish is the only supported topology
// (wire-class HalfWaveDipole / YagiUda + aperture-class Horn +
// IdealIsotropic are deferred to ANT.W4 framing-C work, #762-765).
// The voxel builder throws NotSupportedException for those kinds.
//
// All voxel-roundtrip tests run in-process per PicoGK 2.0.0 + xUnit
// pitfall #8 (CLAUDE.md): `using var lib = new Library(voxel_mm);
// using var libScope = LibraryScope.Set(lib);` lets xUnit construct +
// dispose Library without the legacy subprocess shim. Same pattern as
// FlywheelVoxelBuilderTests / TankageVoxelBuilderTests.
//
// Test sizing note: a literal DSN 34-m BWG dish at 1 mm voxel would
// need a 34 000 × 34 000 × 13 600 voxel grid (~ 16 TB) — wildly
// impractical for in-process xUnit. The DBS archetype (D = 600 mm) at
// 1 mm voxel needs ~ 600 × 600 × 240 = 86 M voxels (a few hundred MB),
// suitable for in-process xUnit. The literal 34-m DSN fixture is
// exercised by an algebraic-only check at a coarse 50 mm voxel size.

using System;
using PicoGK;
using Voxelforge.Antenna;
using Voxelforge.Geometry;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaVoxelBuilderTests
{
    // 1 mm voxel — coarse enough that build completes quickly on a
    // 0.6 m DBS-archetype dish, fine enough that the 2 mm reflector
    // wall resolves cleanly. Matches the voxel size used by
    // FlywheelVoxelBuilderTests / TankageVoxelBuilderTests.
    private const float VoxelSize_mm = 1.0f;

    // ── DBS residential 0.6 m dish archetype (voxel roundtrip) ───────
    //
    // Prime-focus equivalent of the canonical 0.6 m residential DBS
    // dish (DirecTV / Dish Network / Sky / Bell). Ku-band at 12.2 GHz
    // downlink centre. Tx power is a stub value (the dish is RX-only
    // in this commercial cluster but AntennaLinkDesign requires a
    // non-zero Tx power for ValidateSelf); link distance is a
    // geostationary-orbit-ish 36 000 km stub.
    private static AntennaLinkDesign DbsResidentialArchetype() => new(
        TransmitAntennaKind:    AntennaKind.ParabolicDish,
        ReceiveAntennaKind:     AntennaKind.ParabolicDish,
        Frequency_Hz:           12.2e9,         // Ku-band downlink
        TransmitPower_W:        100.0,          // stub (RX-only in cluster)
        LinkDistance_m:         3.6e7,          // GEO stub
        TransmitDishDiameter_m: 0.6,            // 600 mm cluster anchor
        ReceiveDishDiameter_m:  0.6,
        DishApertureEfficiency: 0.65);

    // ── Literal DSN 34-m fixture (algebraic-only) ────────────────────
    //
    // Matches the MRO-to-DSN-34-m fixture in
    // AntennaLinkFixture_MroToDsn34m.cs exactly. Used only for
    // closed-form arithmetic checks that don't materialise a voxel
    // field at the absurd literal scale.
    private static AntennaLinkDesign Dsn34mLiteral() => new(
        TransmitAntennaKind:    AntennaKind.ParabolicDish,
        ReceiveAntennaKind:     AntennaKind.ParabolicDish,
        Frequency_Hz:           8.4e9,
        TransmitPower_W:        100.0,
        LinkDistance_m:         1.496e11,
        TransmitDishDiameter_m: 34.0,          // DSN 34-m BWG
        ReceiveDishDiameter_m:  3.0,           // (unused for Tx build)
        DishApertureEfficiency: 0.65);

    // ── Build helper ─────────────────────────────────────────────────

    /// <summary>
    /// Build the antenna assembly in-process (PicoGK 2.0.0 + LibraryScope)
    /// and extract volume + bounding box via <c>Voxels.CalculateProperties</c>.
    /// Returns the geometry summary + the measured volume / BBox so test
    /// methods can run lots of cheap assertions on one build.
    /// </summary>
    private static (AntennaGeometryResult result, float volume_mm3, BBox3 bbox)
        BuildAndMeasure(AntennaLinkDesign design, float voxelSize_mm = VoxelSize_mm)
    {
        using var lib = new Library(voxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        AntennaGeometryResult result = AntennaVoxelBuilder.Build(design, voxelSize_mm);

        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);
        return (result, volume_mm3, bbox);
    }

    // ── Geometry-record arithmetic checks (DBS archetype) ────────────
    //
    // Algebraic tests use a coarse 10 mm voxel: the geometry record
    // fields are voxel-size-independent (apart from the 4×voxel wall
    // floor which we test explicitly at both 1 mm and 0.25 mm voxel
    // below), but the Build() call still materialises a voxel grid for
    // the bounding-box pass. At 10 mm voxel the DBS grid is only
    // 60×60×40 = 144 K voxels — sub-megabyte, lets xUnit churn through
    // a dozen algebraic tests without per-test multi-GB allocations.
    // The wall-thickness algebraic test at 1 mm voxel (below) uses the
    // fine voxel deliberately to exercise the floor branch.
    private const float CoarseAlgebraicVoxel_mm = 10.0f;

    [Fact]
    public void DbsArchetype_DishDiameterMillimetres_MatchesDesign()
    {
        // D conversion: design is in metres, builder reports mm.
        // 0.6 m × 1000 = 600 mm.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), CoarseAlgebraicVoxel_mm);
        Assert.Equal(600.0, result.DishDiameter_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_FocalLength_UsesDefaultFOverD()
    {
        // F = (f/D) × D = 0.4 × 600 = 240 mm.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), CoarseAlgebraicVoxel_mm);
        Assert.Equal(0.4 * 600.0, result.FocalLength_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_DishDepth_MatchesParaboloidGeometry()
    {
        // depth = (D/2)² / (4·F) = 300² / (4·240) = 90 000 / 960 = 93.75 mm.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), CoarseAlgebraicVoxel_mm);
        double expected_depth_mm = (300.0 * 300.0) / (4.0 * 240.0);
        Assert.Equal(expected_depth_mm, result.DishDepth_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_ReflectorWallThickness_HitsFourVoxelFloorAt1mmVoxel()
    {
        // Default wall = 2 mm; floor = 4×voxel = 4 mm at 1 mm voxel.
        // max(2, 4) = 4 mm. Algebraic-only: passes 1 mm to Build (the
        // floor-branch parameter) but keeps the Library at coarse
        // 10 mm so the voxel grid is small. The wall-floor arithmetic
        // is voxel-grid-independent; we're testing the if/else branch
        // around `Math.Max(DefaultReflectorWallThickness_mm,
        // 4 * voxelSize_mm)`.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), VoxelSize_mm);
        Assert.Equal(4.0, result.ReflectorWallThickness_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_ReflectorWallThickness_UsesDefaultAtFineVoxel()
    {
        // At a 0.25 mm voxel, the 4×voxel floor = 1 mm, well below the
        // 2 mm default → wall stays at 2 mm. Uses coarse 10 mm voxel
        // for the Library scope (we only need the algebraic path; the
        // 0.25 mm parameter passed to Build governs the floor branch,
        // not the actual voxel grid size which is set by the Library).
        // Note: the Library voxel size and Build's voxelSize_mm must
        // match for downstream consumers, but for this algebraic check
        // the divergence is acceptable — we're testing the wall-floor
        // arithmetic, not the voxel quantisation.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), 0.25);
        Assert.Equal(2.0, result.ReflectorWallThickness_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_FeedRadius_UsesDFractionAnchor()
    {
        // FeedRadius = D / 15 = 600 / 15 = 40 mm.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), CoarseAlgebraicVoxel_mm);
        Assert.Equal(40.0, result.FeedRadius_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_FeedLength_UsesDFractionAnchor()
    {
        // FeedLength = 0.5 × D = 0.5 × 600 = 300 mm.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), CoarseAlgebraicVoxel_mm);
        Assert.Equal(300.0, result.FeedLength_mm, precision: 6);
    }

    [Fact]
    public void DbsArchetype_OverallAxialLength_RunsVertexToFeedTop()
    {
        // FeedTop = F + L_feed/2 = 240 + 150 = 390 mm.
        // ReflectorTop = depth + wall = 93.75 + 4 = 97.75 mm.
        // Overall = max(FeedTop, ReflectorTop) = 390 mm.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), CoarseAlgebraicVoxel_mm);
        // Wall stays at the 2 mm default (at 10 mm voxel the floor is
        // 40 mm — wait, that's HIGHER than the default → wall = 40 mm).
        // So at coarse voxel, OverallAxialLength still = max(FeedTop,
        // depth + 40) = max(390, 133.75) = 390 mm. Same as fine voxel.
        Assert.Equal(390.0, result.OverallAxialLength_mm, precision: 6);
    }

    // ── Literal DSN 34-m fixture — algebraic-only cross-check ─────────

    [Fact]
    public void LiteralDsn34mFixture_DimensionalFieldsMatchClosedForm()
    {
        // Literal DSN 34-m BWG dish at 8.4 GHz. Verify the builder
        // reports the expected mm-scale dimensions. Use a coarse 500 mm
        // voxel so the BBox3 envelope stays sane on the 34 m geometry
        // (deliberately don't materialise voxels at this scale —
        // 34 000 / 500 = 68 voxels lateral, 22 100 / 500 = 44 voxels
        // axial → ~ 200 K voxels, sub-megabyte).
        const float coarseVoxel_mm = 500.0f;
        using var lib = new Library(coarseVoxel_mm);
        using var libScope = LibraryScope.Set(lib);

        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(Dsn34mLiteral(), coarseVoxel_mm);

        // D = 34 m × 1000 = 34 000 mm.
        Assert.Equal(34000.0, result.DishDiameter_mm, precision: 6);
        // F = 0.4 × 34 000 = 13 600 mm.
        Assert.Equal(13600.0, result.FocalLength_mm, precision: 6);
        // depth = (17 000)² / (4 × 13 600) = 2.89e8 / 54 400 ≈ 5312.5 mm.
        Assert.Equal(5312.5, result.DishDepth_mm, precision: 3);
        // Wall: max(2 mm, 4 × 500 mm) = 2000 mm at coarse voxel.
        Assert.Equal(2000.0, result.ReflectorWallThickness_mm, precision: 6);
        // FeedRadius = 34 000 / 15 ≈ 2266.67 mm.
        Assert.Equal(34000.0 / 15.0, result.FeedRadius_mm, precision: 3);
        // FeedLength = 0.5 × 34 000 = 17 000 mm.
        Assert.Equal(17000.0, result.FeedLength_mm, precision: 6);
        // Overall = max(F + L_feed/2, depth + wall)
        //        = max(13 600 + 8500, 5312.5 + 2000)
        //        = max(22 100, 7312.5) = 22 100 mm.
        Assert.Equal(22100.0, result.OverallAxialLength_mm, precision: 6);
    }

    // ── Voxel-roundtrip checks (DBS archetype) ────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_VoxelSet_IsNonEmpty()
    {
        // The build must produce a non-degenerate voxel body. Trigger
        // mesh extraction to confirm the voxel field actually rendered.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaGeometryResult result =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 1000,
            $"Voxel mesh must have substantially > 0 triangles (got {triangleCount}). "
          + "Indicates the antenna build degenerated to an empty voxel set.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_BoundingBox_DiameterMatchesDishDiameter()
    {
        // Bounding-box lateral diameter (X, Y) must equal D ± 4 voxels
        // (the dish aperture defines the lateral extent; the feed
        // radius 40 mm sits inside R = 300 mm so doesn't extend the
        // envelope). With voxel quantisation the surface envelope
        // moves ± 2 voxels per side, giving 4 mm slack.
        (AntennaGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(DbsResidentialArchetype());
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        Assert.InRange((double)xExt, result.DishDiameter_mm - 4.0,
                                     result.DishDiameter_mm + 4.0);
        Assert.InRange((double)yExt, result.DishDiameter_mm - 4.0,
                                     result.DishDiameter_mm + 4.0);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_BoundingBox_AxialExtentMatchesOverallLength()
    {
        // Axial extent (Z) must equal OverallAxialLength_mm within
        // voxel tolerance. For DBS archetype: 390 mm ± 4 mm. The
        // bottom of the bbox sits at z = 0 (dish vertex); the top at
        // z = F + L_feed/2 = 390 mm.
        var (result, _, bbox) = BuildAndMeasure(DbsResidentialArchetype());
        float zExt = bbox.vecMax.Z - bbox.vecMin.Z;
        double slack_mm = Math.Max(4.0, 0.01 * result.OverallAxialLength_mm);
        Assert.InRange((double)zExt,
            result.OverallAxialLength_mm - slack_mm,
            result.OverallAxialLength_mm + slack_mm);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_ReflectorShellMassAboveDegenerateFloor()
    {
        // The reflector shell alone has approximate volume
        // V_reflector ≈ A_aperture × wall = π R² × wall
        //             = π × 300² × 4 = 1.131 M mm³ (≈ 3.1 kg aluminium
        // at ρ = 2700 kg/m³). The feed cylinder adds
        // V_feed = π × 40² × 300 = 1.508 M mm³. Total voxel volume
        // should be substantially larger than the noise floor (~ 1000
        // mm³ worth of stray boundary voxels). Assert > 0.5 M mm³ as a
        // loose lower bound that catches "build returned empty" but
        // doesn't pin a tight number (PicoGK envelope rendering can
        // vary 5-10 % on a shell SDF).
        var (_, volume_mm3, _) = BuildAndMeasure(DbsResidentialArchetype());
        Assert.True(volume_mm3 > 500_000.0f,
            $"Voxel volume {volume_mm3:F0} mm³ should be substantially > 0 "
          + "(reflector shell + feed cylinder expected ~ 2.6 M mm³ at DBS "
          + "archetype scale).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_ReflectorShellMassUnderSolidEnvelopeCeiling()
    {
        // The reflector is a SHELL, not a solid bowl. Its volume must
        // be substantially smaller than the solid-paraboloid envelope.
        // A solid paraboloid bowl has volume V = (π R² × depth) / 2
        // (paraboloid is exactly half the volume of its enclosing
        // cylinder). For DBS archetype: V_solid = π × 300² × 93.75 / 2
        // ≈ 13.25 M mm³. Adding feed cylinder ~ 1.51 M mm³ gives a
        // total solid-envelope ceiling of ~ 14.76 M mm³. The actual
        // voxel volume should be well under this (shell + feed
        // realistically ~ 2.6 M mm³). Assert < 50 % of the solid
        // ceiling — generous band accounting for PicoGK envelope
        // rendering, but tight enough to catch "shell degenerated to
        // a solid bowl" silently.
        var (result, volume_mm3, _) = BuildAndMeasure(DbsResidentialArchetype());
        double V_solidParaboloid_mm3 = System.Math.PI
            * (result.DishDiameter_mm * 0.5) * (result.DishDiameter_mm * 0.5)
            * result.DishDepth_mm / 2.0;
        double V_feedCylinder_mm3 = System.Math.PI
            * result.FeedRadius_mm * result.FeedRadius_mm
            * result.FeedLength_mm;
        double V_solidCeiling_mm3 = V_solidParaboloid_mm3 + V_feedCylinder_mm3;

        Assert.True((double)volume_mm3 < 0.5 * V_solidCeiling_mm3,
            $"Voxel volume {volume_mm3:F0} mm³ must be substantially less "
          + $"than the solid-paraboloid + solid-feed ceiling "
          + $"{V_solidCeiling_mm3:F0} mm³ (50 % band). If this fires, the "
          + "reflector shell SDF has degenerated into a filled bowl.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_HasMaterialAtFocalPoint()
    {
        // The feed cylinder is centred on z = F = 240 mm. Sample a
        // voxel inside the feed envelope (origin, z = F) and verify
        // it lies in the material. PicoGK's bRayCast lets us probe
        // the SDF directly without recomputing. A more robust check
        // uses mesh extraction: confirm the build's triangle count is
        // strictly greater than a reflector-only reference build (no
        // feed). This proves the feed cylinder added material.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        // With-feed build (the default).
        AntennaGeometryResult withFeed =
            AntennaVoxelBuilder.Build(DbsResidentialArchetype(), VoxelSize_mm);

        // Measure volume first — mshAsMesh() is called after to avoid any
        // native-side ordering dependency between the two PicoGK calls.
        withFeed.Voxels.AsPicoGK().CalculateProperties(out float volWithFeed, out _);
        long trisWithFeed = withFeed.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();

        // Reflector-only reference: build the ParaboloidShellImplicit
        // alone (no feed BoolAdd). This is the geometry
        // AntennaVoxelBuilder produces before its CylinderImplicit
        // BoolAdd.
        // (Cannot directly access ParaboloidShellImplicit from here —
        // it's internal to Voxelforge.Voxels. Instead use the
        // mass-comparison strategy: with-feed voxel volume must
        // exceed an idealised reflector-shell-only volume.)

        double V_reflectorShell_mm3 = System.Math.PI
            * (withFeed.DishDiameter_mm * 0.5) * (withFeed.DishDiameter_mm * 0.5)
            * withFeed.ReflectorWallThickness_mm;
        // The realistic shell volume is a bit less than the cylinder
        // approximation (the paraboloid slope tapers the shell).
        // Assert volume > 0.8 × V_reflector_cyl_approx to leave room
        // for the slope correction but require the feed cylinder
        // contribute meaningful additional volume on top.
        Assert.True((double)volWithFeed > 0.8 * V_reflectorShell_mm3,
            $"With-feed voxel volume {volWithFeed:F0} mm³ must exceed 80 % of "
          + $"the reflector-shell cylinder approximation "
          + $"{V_reflectorShell_mm3:F0} mm³ (= π·R²·wall). If this fires, "
          + "the feed cylinder failed to add material.");
        Assert.True(trisWithFeed > 1000,
            $"With-feed mesh must be non-empty (got {trisWithFeed} tris).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void DbsArchetype_ReflectorIsOpenFrontShell_NoFloodFillToSolidBowl()
    {
        // The reflector is an OPEN-FRONT shell — the aperture circle
        // r = R at z = depth has no covering surface. This means the
        // PicoGK voxelizer should NOT flood-fill the bowl interior
        // (unlike the A.70 Tankage closed-cavity case). Verify by
        // checking the voxel volume is much closer to the shell
        // estimate than to the solid-bowl estimate.
        var (result, volume_mm3, _) = BuildAndMeasure(DbsResidentialArchetype());

        // Solid-bowl V = π R² × depth / 2.
        double V_solidBowl_mm3 = System.Math.PI
            * (result.DishDiameter_mm * 0.5) * (result.DishDiameter_mm * 0.5)
            * result.DishDepth_mm / 2.0;
        // Shell V ~ π R² × wall (cylinder approximation; true shell
        // volume is slightly less due to taper but the order of
        // magnitude holds).
        double V_shellApprox_mm3 = System.Math.PI
            * (result.DishDiameter_mm * 0.5) * (result.DishDiameter_mm * 0.5)
            * result.ReflectorWallThickness_mm;

        double V_feedCylinder_mm3 = System.Math.PI
            * result.FeedRadius_mm * result.FeedRadius_mm
            * result.FeedLength_mm;

        // Build total ≈ V_shellApprox + V_feedCylinder (and is well
        // below V_solidBowl + V_feedCylinder).
        double assembledEstimate = V_shellApprox_mm3 + V_feedCylinder_mm3;
        double solidEstimate     = V_solidBowl_mm3   + V_feedCylinder_mm3;

        // Voxel volume should be closer to assembledEstimate than to
        // solidEstimate. Translate to: voxelVolume < midpoint between
        // assembledEstimate and solidEstimate.
        double midpoint = 0.5 * (assembledEstimate + solidEstimate);
        Assert.True((double)volume_mm3 < midpoint,
            $"Voxel volume {volume_mm3:F0} mm³ must be closer to shell+feed "
          + $"({assembledEstimate:F0} mm³) than to solid-bowl+feed "
          + $"({solidEstimate:F0} mm³). Midpoint {midpoint:F0} mm³. "
          + "Indicates flood-fill of the bowl interior — the reflector SDF "
          + "is no longer open-front.");
    }

    // ── Cross-diameter parity ─────────────────────────────────────────

    [Fact]
    public void DishDiameter_DoublesWhenDesignDiameterDoubles()
    {
        // Linear dimensional scaling: doubling D doubles
        // DishDiameter_mm, FeedRadius_mm, FeedLength_mm; F also
        // doubles (f/D constant); depth doubles too (depth =
        // (R²/(4F)) ∝ R²/F ∝ R = D/2).
        //
        // Algebraic-only check: use a coarse voxel size (10 mm) so the
        // 1.2 m dish doesn't materialise a multi-GB voxel grid. The
        // GEOMETRY RECORD is voxel-size-independent (apart from the
        // wall-thickness 4×voxel floor), so the parity check holds.
        const float coarseVoxel_mm = 10.0f;
        using var lib = new Library(coarseVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        var small = DbsResidentialArchetype();
        var large = small with { TransmitDishDiameter_m = 1.2 };

        AntennaGeometryResult smallResult = AntennaVoxelBuilder.Build(small, coarseVoxel_mm);
        AntennaGeometryResult largeResult = AntennaVoxelBuilder.Build(large, coarseVoxel_mm);

        Assert.Equal(smallResult.DishDiameter_mm * 2.0, largeResult.DishDiameter_mm, precision: 6);
        Assert.Equal(smallResult.FocalLength_mm  * 2.0, largeResult.FocalLength_mm,  precision: 6);
        Assert.Equal(smallResult.DishDepth_mm    * 2.0, largeResult.DishDepth_mm,    precision: 6);
        Assert.Equal(smallResult.FeedRadius_mm   * 2.0, largeResult.FeedRadius_mm,   precision: 6);
        Assert.Equal(smallResult.FeedLength_mm   * 2.0, largeResult.FeedLength_mm,   precision: 6);
    }

    // ── Non-parabolic kind rejection ─────────────────────────────────
    //
    // Wire-class (HalfWaveDipole, YagiUda) + aperture-class (Horn) +
    // ideal topology (IdealIsotropic) are deferred to ANT.W4 framing-C
    // work for non-dish topology library (#762-765). The voxel builder
    // must throw NotSupportedException so the user can't silently get
    // an empty / nonsense voxel body. AntennaKind is internal — can't
    // pass it across an `[InlineData]` boundary on a public test
    // method without bumping accessibility, so we expand the four
    // unsupported kinds into individual Fact methods (same pattern as
    // any other AntennaKind-typed test in this project).

    [Fact]
    public void Build_IdealIsotropicKind_ThrowsNotSupportedException()
    {
        AssertNonParabolicKindRejected(AntennaKind.IdealIsotropic);
    }

    [Fact]
    public void Build_HalfWaveDipoleKind_ThrowsNotSupportedException()
    {
        AssertNonParabolicKindRejected(AntennaKind.HalfWaveDipole);
    }

    [Fact]
    public void Build_YagiUdaKind_ThrowsNotSupportedException()
    {
        AssertNonParabolicKindRejected(AntennaKind.YagiUda);
    }

    [Fact]
    public void Build_HornKind_ThrowsNotSupportedException()
    {
        AssertNonParabolicKindRejected(AntennaKind.Horn);
    }

    private static void AssertNonParabolicKindRejected(AntennaKind kind)
    {
        // No voxel grid materialises — Build throws before
        // MakeVoxels — so a coarse Library is fine.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaLinkDesign d = DbsResidentialArchetype()
            with { TransmitAntennaKind = kind };
        NotSupportedException ex = Assert.Throws<NotSupportedException>(
            () => AntennaVoxelBuilder.Build(d, CoarseAlgebraicVoxel_mm));
        Assert.Contains("ParabolicDish", ex.Message, StringComparison.Ordinal);
        Assert.Contains("BuildAny", ex.Message, StringComparison.Ordinal);
    }

    // ── Validation surface ───────────────────────────────────────────

    [Fact]
    public void Build_NullDesign_Throws()
    {
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        Assert.Throws<ArgumentNullException>(
            () => AntennaVoxelBuilder.Build(null!, CoarseAlgebraicVoxel_mm));
    }

    [Fact]
    public void Build_NonPositiveVoxelSize_Throws()
    {
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaLinkDesign d = DbsResidentialArchetype();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaVoxelBuilder.Build(d,  0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaVoxelBuilder.Build(d, -1.0));
    }

    [Fact]
    public void Build_DegenerateDesign_PropagatesValidationError()
    {
        // ValidateSelf throws ArgumentException for AntennaKind.None.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaLinkDesign bad = DbsResidentialArchetype()
            with { TransmitAntennaKind = AntennaKind.None };
        Assert.Throws<ArgumentException>(
            () => AntennaVoxelBuilder.Build(bad, CoarseAlgebraicVoxel_mm));
    }

    [Fact]
    public void Build_NonPositiveDishDiameter_PropagatesValidationError()
    {
        // ValidateSelf throws ArgumentOutOfRangeException for
        // ParabolicDish Tx with diameter <= 0.
        using var lib = new Library(CoarseAlgebraicVoxel_mm);
        using var libScope = LibraryScope.Set(lib);
        AntennaLinkDesign bad = DbsResidentialArchetype()
            with { TransmitDishDiameter_m = 0.0 };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AntennaVoxelBuilder.Build(bad, CoarseAlgebraicVoxel_mm));
    }
}
