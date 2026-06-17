// AerostructuresVoxelBuilderTests.cs — Sprint A.82 (C.2) voxel-pipeline
// backfill coverage. Exercises AerostructuresVoxelBuilder against a
// Cessna-172-class wing-spar anchor + cross-section parity checks.
//
// Test sizing note: the literal Cessna-172 fixture (HalfSpan = 5.5 m =
// 5500 mm; section 250 × 80 mm) would need a ~ 5500 × 250 × 80 voxel
// grid at 1 mm voxel — manageable but slow. For voxel-roundtrip tests
// we use a 1/10-scale Cessna-archetype spar (HalfSpan = 0.55 m, section
// 25 × 8 mm, 0.6 mm wall) sized so the build completes quickly in
// xUnit. The literal Cessna fixture is exercised by the algebraic-only
// dimensional cross-check (no voxel materialisation). Same pattern as
// TankageVoxelBuilderTests' Falcon-literal vs Falcon-archetype split
// and FlywheelVoxelBuilderTests' Beacon-literal vs Beacon-archetype.
//
// Hollow-box voxel-body note: A.82 builds the HollowRectangularBox
// section as a 4-plate union (top + bottom flanges, left + right webs)
// extruded along the +X span axis. Both ±X ends are OPEN (no end caps
// in the design surface), so the cavity renders correctly — PicoGK
// 2.0.0's closed-cavity flood-fill limitation (A.70 Tankage) does NOT
// apply here. Mass-recovery tests use the closed-form shell area:
//   A_shell = b·h − (b − 2t)·(h − 2t)
//   V_shell = A_shell · L
//   mass    = ρ · V_shell
// matching the AS.W1 WingSparSolver section-area formula exactly.
//
// All voxel-roundtrip tests run in-process per PicoGK 2.0.0 + xUnit
// pitfall #8 (CLAUDE.md): `using var lib = new Library(voxel_mm);
// using var libScope = LibraryScope.Set(lib);` lets xUnit construct +
// dispose Library without the legacy subprocess shim. Same pattern as
// TankageVoxelBuilderTests / FlywheelVoxelBuilderTests.

using System;
using PicoGK;
using Voxelforge.Aerostructures;
using Voxelforge.Geometry;
using Xunit;

namespace Voxelforge.Tests.Aerostructures;

public sealed class AerostructuresVoxelBuilderTests
{
    // 1 mm voxel — coarse enough that build completes quickly on a
    // 550 mm × 25 mm × 8 mm Cessna-archetype envelope, fine enough that
    // the 0.6 mm wall resolves at ~ 1 voxel per wall (thin walls
    // quantise to wider mass-recovery bands; accepted in the assertion
    // tolerances below). Matches the voxel size used by
    // TankageVoxelBuilderTests / FlywheelVoxelBuilderTests.
    private const float VoxelSize_mm = 1.0f;

    // ── Cessna-172 archetype (for voxel roundtrip) ───────────────────
    //
    // Cessna-172-ARCHETYPE wing spar (preserves topology + material;
    // dimensions chosen so the wall resolves cleanly at 1 mm voxel):
    // HalfSpan = 0.55 m (1/10 of the literal 5.5 m so the voxel grid
    // stays in xUnit-comfortable territory), h = 0.050 m, b = 0.020 m,
    // wall = 0.003 m (3 mm — 3× the voxel size so quantisation lands
    // within ±20 % per the A.70 Tankage 1.83×-voxel-precedent), Al
    // 7075-T6, 981 N/m distributed lift at 3.8 g. Literal Cessna 172
    // (250 × 80 × 6 mm wall at 5.5 m HalfSpan) is exercised by the
    // algebraic-only dimensional check below.
    private static WingSparDesign Cessna172ArchetypeForVoxelTests() => new(
        SectionType:           SparSectionType.HollowRectangularBox,
        Material:              SparMaterial.Aluminum7075,
        HalfSpan_m:            0.55,
        OuterHeight_m:         0.050,
        OuterWidth_m:          0.020,
        WallThickness_m:       0.003,
        DistributedLift_Nm:    981.0,
        LoadFactor:            3.8);

    // ── Literal Cessna-172 fixture (for algebraic-only checks) ───────
    //
    // Matches the test-fixture in AerostructuresFixture_Cessna172WingSpar
    // exactly. Used only for closed-form arithmetic checks that don't
    // materialise a voxel field at full scale.
    private static WingSparDesign Cessna172Literal() => new(
        SectionType:           SparSectionType.HollowRectangularBox,
        Material:              SparMaterial.Aluminum7075,
        HalfSpan_m:            5.5,
        OuterHeight_m:         0.25,
        OuterWidth_m:          0.08,
        WallThickness_m:       0.006,
        DistributedLift_Nm:    981.0,
        LoadFactor:            3.8);

    // ── Solid-section archetypes (for cross-section parity) ──────────

    // Solid rectangular: same outer envelope as the hollow archetype.
    // Used to verify that flipping the section type changes the voxel
    // body to a solid cuboid (and increases voxel volume).
    private static WingSparDesign SolidRectangularArchetype() =>
        Cessna172ArchetypeForVoxelTests() with
        {
            SectionType = SparSectionType.SolidRectangular,
        };

    // Solid circular: h reinterpreted as 2·R = 25 mm diameter, span
    // 550 mm. Helicopter-blade-spar / UAV-boom topology.
    private static WingSparDesign SolidCircularArchetype() =>
        Cessna172ArchetypeForVoxelTests() with
        {
            SectionType = SparSectionType.SolidCircular,
        };

    // ── Build helper ─────────────────────────────────────────────────

    /// <summary>
    /// Build the wing spar in-process (PicoGK 2.0.0 + LibraryScope) and
    /// extract volume + bounding box via <c>Voxels.CalculateProperties</c>.
    /// Returns the geometry summary + the measured volume / BBox so test
    /// methods can run lots of cheap assertions on one build.
    /// </summary>
    private static (AerostructuresGeometryResult result, float volume_mm3, BBox3 bbox)
        BuildAndMeasure(WingSparDesign design, float voxelSize_mm = VoxelSize_mm)
    {
        using var lib = new Library(voxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(design, voxelSize_mm);

        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);
        return (result, volume_mm3, bbox);
    }

    // ── Geometry-record arithmetic checks (Cessna archetype) ─────────

    [Fact]
    public void CessnaArchetype_DimensionalFieldsMatchDesign()
    {
        // Archetype: HalfSpan = 550 mm, h = 50 mm, b = 20 mm, t = 3 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(Cessna172ArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Equal(550.0,   result.HalfSpan_mm,      precision: 6);
        Assert.Equal( 50.0,   result.OuterHeight_mm,   precision: 6);
        Assert.Equal( 20.0,   result.OuterWidth_mm,    precision: 6);
        Assert.Equal(  3.0,   result.WallThickness_mm, precision: 6);
        Assert.Equal(SparSectionType.HollowRectangularBox, result.SectionType);
    }

    [Fact]
    public void CessnaArchetype_HollowBoxFlagsHollowVoxelBody()
    {
        // HollowRectangularBox renders as a hollow shell (the spanwise
        // ends are open in the design surface). IsHollowVoxelBody must
        // be true so downstream callers know mass = ρ · V_voxel maps
        // to the shell mass (not the solid envelope).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(Cessna172ArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.True(result.IsHollowVoxelBody,
            "HollowRectangularBox spar must render as a hollow voxel body "
          + "(open spanwise ends sidestep the PicoGK 2.0.0 closed-cavity limitation).");
    }

    [Fact]
    public void CessnaArchetype_SectionDescriptionDescribesGeometry()
    {
        // Description carries h × b × t for hollow boxes — useful for
        // logging / STL filename stamping. Doesn't pin the exact format
        // (allow future tweaks); just asserts the key dimensional
        // tokens appear.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(Cessna172ArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Contains("HollowRectangularBox", result.SectionDescription);
        Assert.Contains("50",  result.SectionDescription);  // h_mm
        Assert.Contains("20",  result.SectionDescription);  // b_mm
    }

    // ── Literal Cessna fixture — algebraic-only cross-check ──────────

    [Fact]
    public void LiteralCessnaFixture_DimensionalFieldsMatchDesign()
    {
        // Literal Cessna 172: HalfSpan = 5500 mm, h = 250 mm, b = 80 mm,
        // wall = 6 mm. Build at a coarse 5 mm voxel — the build still
        // creates a non-trivial voxel field at this scale (~ 1100 × 50 ×
        // 16 voxels = 880 k cells), small enough to fit in xUnit
        // comfortably. Verifies the closed-form arithmetic survives the
        // voxel-builder path at literal Cessna scale.
        using var lib = new Library(5.0f);
        using var libScope = LibraryScope.Set(lib);

        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(Cessna172Literal(), 5.0);

        Assert.Equal(5500.0, result.HalfSpan_mm,      precision: 6);
        Assert.Equal( 250.0, result.OuterHeight_mm,   precision: 6);
        Assert.Equal(  80.0, result.OuterWidth_mm,    precision: 6);
        Assert.Equal(   6.0, result.WallThickness_mm, precision: 6);
        Assert.True(result.IsHollowVoxelBody);
    }

    // ── Voxel-roundtrip checks (Cessna archetype, hollow box) ────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void CessnaArchetype_VoxelSet_IsNonEmpty()
    {
        // The build must produce a non-degenerate voxel body. Trigger
        // mesh extraction to confirm the voxel field actually rendered.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(Cessna172ArchetypeForVoxelTests(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 1000,
            $"Voxel mesh must have substantially > 0 triangles (got {triangleCount}). "
          + "Indicates the wing-spar build degenerated to an empty voxel set.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void CessnaArchetype_BoundingBox_AxialExtentMatchesHalfSpan()
    {
        // Axial extent (X) must equal HalfSpan_mm ± a few voxels of
        // smoothing slack. The spar is centred about x = 0 and runs
        // from -L/2 to +L/2, total extent = L.
        (AerostructuresGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(Cessna172ArchetypeForVoxelTests());
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        double slack_mm = Math.Max(4.0, 0.01 * result.HalfSpan_mm);
        Assert.InRange((double)xExt,
            result.HalfSpan_mm - slack_mm,
            result.HalfSpan_mm + slack_mm);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void CessnaArchetype_BoundingBox_CrossSectionMatchesDesign()
    {
        // Y extent must equal OuterWidth_mm (= b), Z extent must equal
        // OuterHeight_mm (= h), each within a few voxels of smoothing
        // slack. Confirms the chord-direction / chord-normal axis
        // convention is correct.
        (AerostructuresGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(Cessna172ArchetypeForVoxelTests());
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        float zExt = bbox.vecMax.Z - bbox.vecMin.Z;
        Assert.InRange((double)yExt,
            result.OuterWidth_mm - 4.0,
            result.OuterWidth_mm + 4.0);
        Assert.InRange((double)zExt,
            result.OuterHeight_mm - 4.0,
            result.OuterHeight_mm + 4.0);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void CessnaArchetype_HollowBox_VoxelMassMatchesShellMass()
    {
        // HollowRectangularBox voxel body is the HOLLOW shell (open
        // spanwise ends sidestep the closed-cavity limitation). Mass-
        // recovery matches the closed-form shell mass:
        //   A_shell = b·h − (b − 2t)·(h − 2t)
        //   V_shell = A_shell · L
        //   mass    = ρ · V_shell
        // ±20 % band: 3 mm wall at 1 mm voxel resolves at 3 voxels per
        // side (matches the A.70 Tankage 1.83×-voxel-precedent at
        // ±20 %). The test ALSO asserts the voxel mass is strictly
        // less than the solid-envelope mass — guards against
        // accidentally building a solid when the design asks for
        // hollow.
        var design = Cessna172ArchetypeForVoxelTests();
        var (result, volume_mm3, _) = BuildAndMeasure(design);

        double h_m = result.OuterHeight_mm   * 1e-3;
        double b_m = result.OuterWidth_mm    * 1e-3;
        double t_m = result.WallThickness_mm * 1e-3;
        double L_m = result.HalfSpan_mm      * 1e-3;
        double A_shell_m2 = b_m * h_m - (b_m - 2.0 * t_m) * (h_m - 2.0 * t_m);
        double V_shell_m3 = A_shell_m2 * L_m;
        double rho_kg_m3 = SparMaterialRegistry.For(design.Material).Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_shell_m3;

        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;

        // Hollow-body check: voxel volume must be LESS than the solid
        // envelope (b · h · L). If the body accidentally rendered as
        // solid, the mass would be ~ 5× the shell value.
        double V_solid_m3 = b_m * h_m * L_m;
        double solidMass_kg = rho_kg_m3 * V_solid_m3;
        Assert.True(voxelMass_kg < solidMass_kg,
            $"HollowRectangularBox voxel mass {voxelMass_kg:F3} kg must be < solid "
          + $"envelope mass {solidMass_kg:F3} kg — body must render hollow.");

        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.20,
            $"Voxel-derived shell mass {voxelMass_kg:F3} kg must match closed-form "
          + $"shell mass {expectedMass_kg:F3} kg within 20 % (got {relErr * 100:F2} %). "
          + $"Wall = {result.WallThickness_mm:F2} mm, voxel = {VoxelSize_mm:F2} mm.");
    }

    // ── Solid-rectangular cross-section parity ───────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidRectangular_FlagsSolidVoxelBody()
    {
        // SolidRectangular renders as a solid cuboid — IsHollowVoxelBody
        // must be false and WallThickness_mm must be 0 in the result
        // record (the WingSparDesign WallThickness is ignored for solid
        // sections).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(SolidRectangularArchetype(), VoxelSize_mm);
        Assert.False(result.IsHollowVoxelBody,
            "SolidRectangular spar must render as a solid (not hollow) voxel body.");
        Assert.Equal(0.0, result.WallThickness_mm, precision: 6);
        Assert.Equal(SparSectionType.SolidRectangular, result.SectionType);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidRectangular_VoxelMassMatchesSolidEnvelope()
    {
        // Solid cuboid mass = ρ · b · h · L. ±5 % band: solid sections
        // quantise cleanly at the bounding-box surface (no thin-wall
        // sub-voxel features). Same envelope as Cessna archetype but
        // filled in.
        var design = SolidRectangularArchetype();
        var (result, volume_mm3, _) = BuildAndMeasure(design);

        double h_m = result.OuterHeight_mm * 1e-3;
        double b_m = result.OuterWidth_mm  * 1e-3;
        double L_m = result.HalfSpan_mm    * 1e-3;
        double V_solid_m3 = b_m * h_m * L_m;
        double rho_kg_m3 = SparMaterialRegistry.For(design.Material).Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_solid_m3;
        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;

        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.05,
            $"SolidRectangular voxel mass {voxelMass_kg:F3} kg vs solid-envelope mass "
          + $"{expectedMass_kg:F3} kg: relErr {relErr * 100:F2} %.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidRectangular_HasMoreVolumeThanHollowBox_AtIdenticalEnvelope()
    {
        // At identical (b, h, L) and material, flipping HollowRectangularBox
        // → SolidRectangular must INCREASE voxel volume (the cavity gets
        // filled). Topology fingerprint — guards against accidentally
        // building a solid when the design asks for hollow.
        var hollow = Cessna172ArchetypeForVoxelTests();
        var solid  = hollow with { SectionType = SparSectionType.SolidRectangular };

        var (_, hollowVol, _) = BuildAndMeasure(hollow);
        var (_, solidVol,  _) = BuildAndMeasure(solid);

        Assert.True(solidVol > hollowVol,
            $"Solid voxel volume ({solidVol:F0} mm³) must exceed hollow-box volume "
          + $"({hollowVol:F0} mm³) at identical envelope — the cavity must subtract "
          + "real material.");
    }

    // ── Solid-circular cross-section parity ──────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidCircular_FlagsSolidVoxelBody()
    {
        // SolidCircular renders as a solid cylinder. IsHollowVoxelBody
        // false, WallThickness_mm zero, OuterWidth_mm equals
        // OuterHeight_mm (the design ignores b for circular sections;
        // the result record reports them equal so downstream consumers
        // see a square bounding box matching the circumscribed
        // diameter).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        AerostructuresGeometryResult result =
            AerostructuresVoxelBuilder.Build(SolidCircularArchetype(), VoxelSize_mm);
        Assert.False(result.IsHollowVoxelBody);
        Assert.Equal(0.0, result.WallThickness_mm, precision: 6);
        Assert.Equal(result.OuterHeight_mm, result.OuterWidth_mm, precision: 6);
        Assert.Equal(SparSectionType.SolidCircular, result.SectionType);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidCircular_VoxelMassMatchesClosedFormCylinder()
    {
        // Solid cylinder mass = ρ · π · R² · L with R = h/2. ±5 % band
        // — same as SolidRectangular (solid sections quantise cleanly).
        var design = SolidCircularArchetype();
        var (result, volume_mm3, _) = BuildAndMeasure(design);

        double R_m = (result.OuterHeight_mm * 0.5) * 1e-3;
        double L_m = result.HalfSpan_mm * 1e-3;
        double V_cyl_m3 = Math.PI * R_m * R_m * L_m;
        double rho_kg_m3 = SparMaterialRegistry.For(design.Material).Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_cyl_m3;
        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;

        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.05,
            $"SolidCircular voxel mass {voxelMass_kg:F3} kg vs cylinder mass "
          + $"{expectedMass_kg:F3} kg: relErr {relErr * 100:F2} %.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidCircular_HasLessVolumeThanSolidRectangular_AtIdenticalH()
    {
        // At identical h (square cross-section side vs cylinder
        // diameter), the cylinder is INSCRIBED in the square — its
        // cross-section area (π·R² = π·h²/4 ≈ 0.785·h²) is smaller
        // than the square (h²). Voxel volume must reflect this.
        // Skip if b ≠ h (only meaningful at b = h); use a square
        // archetype for the comparison.
        var squareArchetype = Cessna172ArchetypeForVoxelTests() with
        {
            SectionType = SparSectionType.SolidRectangular,
            OuterWidth_m  = 0.025,   // b = h = 25 mm → square section
            OuterHeight_m = 0.025,
        };
        var circular = squareArchetype with { SectionType = SparSectionType.SolidCircular };

        var (_, squareVol,   _) = BuildAndMeasure(squareArchetype);
        var (_, circularVol, _) = BuildAndMeasure(circular);

        // π/4 ≈ 0.785 → expect circular ≈ 0.79 · square.
        Assert.True(circularVol < squareVol,
            $"Inscribed cylinder voxel volume ({circularVol:F0} mm³) must be < "
          + $"circumscribed square-section volume ({squareVol:F0} mm³) at identical h.");
        double ratio = circularVol / (double)squareVol;
        Assert.InRange(ratio, 0.70, 0.85);  // π/4 ≈ 0.785 ± quantisation
    }

    // ── Validation surface ───────────────────────────────────────────

    [Fact]
    public void Build_NullDesign_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        Assert.Throws<ArgumentNullException>(
            () => AerostructuresVoxelBuilder.Build(null!, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveVoxelSize_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        WingSparDesign d = Cessna172ArchetypeForVoxelTests();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AerostructuresVoxelBuilder.Build(d,  0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AerostructuresVoxelBuilder.Build(d, -1.0));
    }

    [Fact]
    public void Build_NoneSectionType_PropagatesValidationError()
    {
        // SparSectionType.None sentinel must be rejected — ValidateSelf
        // throws and the voxel builder propagates.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        WingSparDesign bad = Cessna172ArchetypeForVoxelTests()
            with { SectionType = SparSectionType.None };
        Assert.Throws<ArgumentException>(
            () => AerostructuresVoxelBuilder.Build(bad, VoxelSize_mm));
    }

    [Fact]
    public void Build_NoneMaterial_PropagatesValidationError()
    {
        // SparMaterial.None sentinel must be rejected. Belt-and-braces
        // with the SectionType.None check above — both sentinels are
        // structurally invalid.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        WingSparDesign bad = Cessna172ArchetypeForVoxelTests()
            with { Material = SparMaterial.None };
        Assert.Throws<ArgumentException>(
            () => AerostructuresVoxelBuilder.Build(bad, VoxelSize_mm));
    }

    [Fact]
    public void Build_OversizeWall_PropagatesValidationError()
    {
        // WallThickness ≥ half of the smaller outer dimension is
        // structurally degenerate (the cavity would close). ValidateSelf
        // rejects this; the voxel builder must propagate.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        WingSparDesign bad = Cessna172ArchetypeForVoxelTests()
            with { WallThickness_m = 0.011 };  // wall = 11 mm ≥ b/2 = 10 mm
        Assert.Throws<ArgumentException>(
            () => AerostructuresVoxelBuilder.Build(bad, VoxelSize_mm));
    }
}
