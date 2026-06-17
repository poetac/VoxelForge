// TankageVoxelBuilderTests.cs — Sprint A.70 (C.2) voxel-pipeline
// backfill coverage. Exercises TankageVoxelBuilder against a Falcon-9-
// class steel-monocoque anchor and a Toyota-Mirai-class composite H₂
// anchor + cross-shape parity checks.
//
// Test sizing note: the literal Falcon 9 stage-1 LOX-tank fixture
// (R = 1.83 m, L = 20 m, hemi caps) translates to a 23.66 m × 3.66 m
// envelope — geometrically a 23 660 × 3 660 × 3 660 voxel grid at
// 1 mm voxel (~ 320 GB). The TankageVoxelBuilder honours the design
// record literally; we use a scaled-down Falcon-archetype fixture
// (R = 0.183 m, L = 1.0 m, same shell + pressure) for voxel-roundtrip
// tests and the literal fixture only for algebraic-only checks at a
// coarse voxel size (no in-process voxelisation). Same pattern as
// FlywheelVoxelBuilderTests' Beacon-literal vs Beacon-archetype split.
//
// Solid-vs-shell voxel-mass note: PicoGK 2.0.0 cannot represent
// fully-enclosed cavities — its voxelizer flood-fills any region
// enclosed by a closed surface (verified during A.70 development; see
// the TankageVoxelBuilder.cs header docstring). When end caps are
// enabled, the builder produces the SOLID outer envelope. When end
// caps are disabled, the cavity has axially-open ends and the
// HOLLOW shell renders correctly. Mass-consistency tests use the
// matching closed-form reference accordingly:
//   • With caps:    ρ · V_solid_envelope = ρ · (π·R_o²·L + (4/3)·π·R_o³)
//   • Without caps: ρ · V_shell           = ρ · π·(R_o² − R_i²)·L
//                                          ≈ PressureVesselSolver
//                                              .Solve(design).ShellMass_kg
//                                              when caps disabled
//
// All voxel-roundtrip tests run in-process per PicoGK 2.0.0 + xUnit
// pitfall #8 (CLAUDE.md): `using var lib = new Library(voxel_mm);
// using var libScope = LibraryScope.Set(lib);` lets xUnit construct +
// dispose Library without the legacy subprocess shim. Same pattern as
// FlywheelVoxelBuilderTests / ExpansionDeflectionPlugTests.

using System;
using PicoGK;
using Voxelforge.Geometry;
using Voxelforge.Tankage;
using Xunit;

namespace Voxelforge.Tests.Tankage;

public sealed class TankageVoxelBuilderTests
{
    // 1 mm voxel — coarse enough that the build completes quickly on
    // a 366 mm × 1366 mm Falcon-archetype envelope, fine enough that
    // the shell wall resolves cleanly. Matches the voxel size used by
    // FlywheelVoxelBuilderTests.
    private const float VoxelSize_mm = 1.0f;

    // ── Falcon-9-class scaled archetype (for voxel roundtrip) ────────
    //
    // 1/10-scale Falcon-9 stage-1 LOX-tank geometry: R = 0.183 m,
    // L = 1.0 m, 4130 stainless, 1.83 mm wall (R/t = 100, comfortably
    // thin-wall), 3 bar MEOP. The wall is bumped up from the literal
    // 4.78 mm scaled-down (0.478 mm) so it resolves cleanly at 1 mm
    // voxel — sub-voxel walls quantise poorly. The literal Falcon
    // fixture is exercised by the algebraic-only check below.
    private static PressureVesselDesign FalconArchetypeForVoxelTests() => new(
        ShellType:                TankShellType.Steel4130,
        InternalRadius_m:         0.183,
        ShellLength_m:            1.0,
        WallThickness_m:          0.00183,     // 1.83 mm (R/t = 100)
        OperatingPressure_Pa:     3e5,         // 3 bar
        HasHemisphericalEndCaps:  true);

    // ── Literal Falcon-9 fixture (for algebraic-only checks) ─────────
    //
    // Matches the test-fixture in PressureVesselSolverTests exactly.
    // Used only for closed-form arithmetic checks that don't
    // materialise a voxel field.
    private static PressureVesselDesign Falcon9LoxTankLiteral() => new(
        ShellType:                TankShellType.Steel4130,
        InternalRadius_m:         1.83,
        ShellLength_m:            20.0,
        WallThickness_m:          0.00478,
        OperatingPressure_Pa:     3e5);

    // ── Toyota-Mirai-class CF composite H₂ tank (for voxel roundtrip) ──
    //
    // 0.30 m OD × 0.85 m L Type-IV CF composite, 700 bar nominal MEOP.
    // For the thin-wall validity envelope (R/t ≥ 10) we use:
    //   R_internal = 0.140 m, wall = 0.014 m → R/t = 10 (at the boundary).
    // This is geometrically representative of the cluster (Mirai
    // typical wall thickness ~ 18-25 mm including liner; this 14 mm
    // anchor models the load-bearing CF overwrap).
    private static PressureVesselDesign MiraiArchetypeForVoxelTests() => new(
        ShellType:                TankShellType.CarbonFibreComposite,
        InternalRadius_m:         0.140,
        ShellLength_m:            0.85,
        WallThickness_m:          0.014,       // R/t = 10
        OperatingPressure_Pa:     70e6,        // 700 bar
        HasHemisphericalEndCaps:  true);

    // ── Build helper ─────────────────────────────────────────────────

    /// <summary>
    /// Build the pressure vessel in-process (PicoGK 2.0.0 + LibraryScope)
    /// and extract volume + bounding box via
    /// <c>Voxels.CalculateProperties</c>. Returns the geometry summary +
    /// the measured volume / BBox so test methods can run lots of cheap
    /// assertions on one build.
    /// </summary>
    private static (TankageGeometryResult result, float volume_mm3, BBox3 bbox)
        BuildAndMeasure(PressureVesselDesign design, float voxelSize_mm = VoxelSize_mm)
    {
        using var lib = new Library(voxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        TankageGeometryResult result = TankageVoxelBuilder.Build(design, voxelSize_mm);

        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);
        return (result, volume_mm3, bbox);
    }

    // ── Geometry-record arithmetic checks (Falcon archetype) ─────────

    [Fact]
    public void FalconArchetype_OuterRadiusMillimetres_EqualsInternalPlusWall()
    {
        // R_outer = R_internal + wall = 183 mm + 1.83 mm = 184.83 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        TankageGeometryResult result =
            TankageVoxelBuilder.Build(FalconArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Equal(183.0,    result.InternalRadius_mm, precision: 6);
        Assert.Equal(1.83,     result.WallThickness_mm,  precision: 6);
        Assert.Equal(184.83,   result.OuterRadius_mm,    precision: 6);
    }

    [Fact]
    public void FalconArchetype_ShellLengthMillimetres_MatchesDesign()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        TankageGeometryResult result =
            TankageVoxelBuilder.Build(FalconArchetypeForVoxelTests(), VoxelSize_mm);
        // ShellLength_mm = design.ShellLength_m × 1000 = 1000 mm.
        Assert.Equal(1000.0, result.ShellLength_mm, precision: 6);
    }

    [Fact]
    public void FalconArchetype_WithEndCaps_OverallLengthEqualsLPlusTwoOuterRadius()
    {
        // Overall length = L + 2·R_outer = 1000 + 2·184.83 = 1369.66 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        TankageGeometryResult result =
            TankageVoxelBuilder.Build(FalconArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.True(result.HasEndCaps);
        Assert.Equal(1000.0 + 2.0 * 184.83, result.OverallLength_mm, precision: 3);
    }

    [Fact]
    public void FalconArchetype_WithoutEndCaps_OverallLengthEqualsShellLength()
    {
        // Without end caps overall = L. R_outer doesn't extend the axial envelope.
        var cylinderOnly = FalconArchetypeForVoxelTests()
            with { HasHemisphericalEndCaps = false };
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        TankageGeometryResult result = TankageVoxelBuilder.Build(cylinderOnly, VoxelSize_mm);
        Assert.False(result.HasEndCaps);
        Assert.Equal(1000.0, result.OverallLength_mm, precision: 6);
    }

    // ── Literal Falcon-9 fixture — algebraic-only cross-check ─────────

    [Fact]
    public void LiteralFalcon9Fixture_DimensionalFieldsMatchClosedForm()
    {
        // Literal Falcon 9 stage-1 LOX-tank: R = 1.83 m, L = 20 m,
        // wall = 4.78 mm, hemi caps. Verify the builder reports the
        // expected mm-scale dimensions. We use a coarse 50 mm voxel
        // so the BBox3 envelope stays sane on the 23.66 m geometry
        // (deliberately don't materialise voxels at this scale; the
        // build still requires a Library for the constructor inputs).
        using var lib = new Library(50.0f);
        using var libScope = LibraryScope.Set(lib);

        TankageGeometryResult result =
            TankageVoxelBuilder.Build(Falcon9LoxTankLiteral(), 50.0);

        Assert.Equal(1830.0,        result.InternalRadius_mm, precision: 6);
        Assert.Equal(4.78,          result.WallThickness_mm,  precision: 6);
        Assert.Equal(1834.78,       result.OuterRadius_mm,    precision: 6);
        Assert.Equal(20000.0,       result.ShellLength_mm,    precision: 6);
        // Overall = 20 000 + 2·1834.78 = 23 669.56 mm.
        Assert.Equal(23669.56,      result.OverallLength_mm,  precision: 2);
        Assert.True(result.HasEndCaps);
    }

    // ── Voxel-roundtrip checks (Falcon archetype, hemi caps) ─────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void FalconArchetype_VoxelSet_IsNonEmpty()
    {
        // The build must produce a non-degenerate voxel body. Trigger
        // mesh extraction to confirm the voxel field actually rendered.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        TankageGeometryResult result =
            TankageVoxelBuilder.Build(FalconArchetypeForVoxelTests(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 1000,
            $"Voxel mesh must have substantially > 0 triangles (got {triangleCount}). "
          + "Indicates the pressure-vessel build degenerated to an empty voxel set.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void FalconArchetype_BoundingBox_DiameterMatchesTwoOuterRadius()
    {
        // Bounding-box diameter (Y, Z) must equal 2 · R_outer ± 4 voxels.
        // R_outer = 184.83 mm → 2R = 369.66 mm; with smoothing the
        // surface envelope can move ± 2 voxels per side, giving 4 mm slack.
        (TankageGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(FalconArchetypeForVoxelTests());
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        float zExt = bbox.vecMax.Z - bbox.vecMin.Z;
        double expected = 2.0 * result.OuterRadius_mm;
        Assert.InRange((double)yExt, expected - 4.0, expected + 4.0);
        Assert.InRange((double)zExt, expected - 4.0, expected + 4.0);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void FalconArchetype_BoundingBox_AxialExtentEqualsOverallLength()
    {
        // Axial extent (X) must equal OverallLength_mm within voxel
        // tolerance. For Falcon archetype with caps: ~ 1370 mm ± 4 mm.
        (TankageGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(FalconArchetypeForVoxelTests());
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        double slack_mm = Math.Max(4.0, 0.01 * result.OverallLength_mm);
        Assert.InRange((double)xExt,
            result.OverallLength_mm - slack_mm,
            result.OverallLength_mm + slack_mm);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void FalconArchetype_WithCaps_VoxelMassMatchesSolidEnvelope()
    {
        // PicoGK 2.0.0 cannot represent fully-enclosed cavities (see
        // TankageVoxelBuilder.cs header): with hemi caps the voxel body
        // is the SOLID outer envelope. Closed-form solid capsule volume:
        //   V_solid = π · R_outer² · L + (4/3) · π · R_outer³
        // Mass = ρ · V_solid. ±10 % band for voxel quantisation.
        var design = FalconArchetypeForVoxelTests();
        var (result, volume_mm3, _) = BuildAndMeasure(design);

        double Router_m = result.OuterRadius_mm * 1e-3;
        double L_m      = result.ShellLength_mm * 1e-3;
        double V_solid_m3 = Math.PI * Router_m * Router_m * L_m
                          + (4.0 / 3.0) * Math.PI * Math.Pow(Router_m, 3);
        double rho_kg_m3 = TankShellRegistry.Steel4130.Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_solid_m3;

        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;
        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.10,
            $"Voxel-derived mass {voxelMass_kg:F3} kg must match solid-envelope "
          + $"mass {expectedMass_kg:F3} kg within 10 % (got {relErr * 100:F2} %). "
          + "With hemi caps, voxel body is SOLID per PicoGK 2.0.0 cavity limitation.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void FalconArchetype_WithoutEndCaps_BoundingBoxAxialEqualsShellLength()
    {
        // Without end caps the bounding box must shrink to L (no R_outer
        // extension on either side). Falcon archetype L = 1000 mm.
        var cylinderOnly = FalconArchetypeForVoxelTests()
            with { HasHemisphericalEndCaps = false };
        var (result, _, bbox) = BuildAndMeasure(cylinderOnly);
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        Assert.InRange((double)xExt,
            result.ShellLength_mm - 4.0,
            result.ShellLength_mm + 4.0);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void FalconArchetype_WithoutEndCaps_VoxelMassMatchesShellMass()
    {
        // Cylinder-only voxel body IS hollow (axially-open ends sidestep
        // the PicoGK closed-cavity limitation). Mass matches the
        // closed-form shell mass.
        //   V_shell_cyl = π·(R_o² − R_i²)·L
        // = π·(184.83² − 183²)·1000 ≈ π·672.5·1000 ≈ 2.113 M mm³
        // = 0.002113 m³ → mass = 7850 · 0.002113 ≈ 16.59 kg.
        var cylinderOnly = FalconArchetypeForVoxelTests()
            with { HasHemisphericalEndCaps = false };
        var (result, volume_mm3, _) = BuildAndMeasure(cylinderOnly);

        double Router_m = result.OuterRadius_mm * 1e-3;
        double Rinner_m = result.InternalRadius_mm * 1e-3;
        double L_m      = result.ShellLength_mm * 1e-3;
        double V_shell_m3 = Math.PI * (Router_m * Router_m - Rinner_m * Rinner_m) * L_m;
        double rho_kg_m3 = TankShellRegistry.Steel4130.Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_shell_m3;

        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;
        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.20,
            $"Voxel-derived shell mass {voxelMass_kg:F3} kg must match closed-form "
          + $"shell mass {expectedMass_kg:F3} kg within 20 % (got {relErr * 100:F2} %). "
          + $"Wall = {result.WallThickness_mm:F2} mm, voxel = {VoxelSize_mm:F2} mm "
          + "(thin walls quantise to wider bands).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void EndCaps_AddLengthAndVolumeVsCylinderOnly()
    {
        // At identical (R, L, t, material), enabling hemi caps must
        // INCREASE both the bounding-box axial length AND the voxel
        // volume vs the cylinder-only variant.
        var withCaps = FalconArchetypeForVoxelTests();
        var noCaps   = withCaps with { HasHemisphericalEndCaps = false };

        var (capRes, capVol, capBbox)  = BuildAndMeasure(withCaps);
        var (_     , cylVol, cylBbox)  = BuildAndMeasure(noCaps);

        // Axial-length increment must be at least 90 % of the analytic
        // 2·R_outer extension (voxel quantisation can trim a few %).
        double dx_cap  = capBbox.vecMax.X - capBbox.vecMin.X;
        double dx_cyl  = cylBbox.vecMax.X - cylBbox.vecMin.X;
        double dx_analytic = 2.0 * capRes.OuterRadius_mm;
        Assert.True(dx_cap - dx_cyl > 0.9 * dx_analytic,
            $"End caps must extend axial extent by ~ 2·R_outer = {dx_analytic:F1} mm "
          + $"(got Δ = {dx_cap - dx_cyl:F1} mm).");

        // Voxel-volume increment must be strictly positive.
        Assert.True(capVol > cylVol,
            $"End caps must ADD voxel volume (got cap = {capVol:F0} mm³, "
          + $"cyl = {cylVol:F0} mm³). The hemi-cap envelope contributes "
          + "real material — should never be a degenerate no-op.");
    }

    // ── Mirai (CF composite) anchor ──────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void MiraiArchetype_DimensionalFieldsMatchDesign()
    {
        // CF composite H₂ tank: R = 140 mm, wall = 14 mm, L = 850 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        TankageGeometryResult result =
            TankageVoxelBuilder.Build(MiraiArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Equal(140.0, result.InternalRadius_mm, precision: 6);
        Assert.Equal( 14.0, result.WallThickness_mm,  precision: 6);
        Assert.Equal(154.0, result.OuterRadius_mm,    precision: 6);
        Assert.Equal(850.0, result.ShellLength_mm,    precision: 6);
        // Overall = 850 + 2·154 = 1158 mm.
        Assert.Equal(1158.0, result.OverallLength_mm, precision: 6);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void MiraiArchetype_WithCaps_VoxelMassMatchesSolidEnvelope()
    {
        // With caps the voxel body is the solid outer envelope. CF
        // composite Mirai: V_solid = π·154²·850 + (4/3)·π·154³ ≈ 78.6 M mm³.
        // Mass = 1500 kg/m³ · 78.6e-3 m³ ≈ 117.9 kg.
        var design = MiraiArchetypeForVoxelTests();
        var (result, volume_mm3, _) = BuildAndMeasure(design);

        double Router_m = result.OuterRadius_mm * 1e-3;
        double L_m      = result.ShellLength_mm * 1e-3;
        double V_solid_m3 = Math.PI * Router_m * Router_m * L_m
                          + (4.0 / 3.0) * Math.PI * Math.Pow(Router_m, 3);
        double rho_kg_m3 = TankShellRegistry.CarbonFibreComposite.Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_solid_m3;
        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;

        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.10,
            $"Mirai voxel mass {voxelMass_kg:F3} kg vs solid-envelope mass "
          + $"{expectedMass_kg:F3} kg: relErr {relErr * 100:F2} %.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void MiraiArchetype_WithoutEndCaps_VoxelMassMatchesShellMass()
    {
        // Cylinder-only Mirai: hollow shell. V_shell = π·(154² − 140²)·850
        // ≈ 11.0 M mm³ → 16.5 kg.
        var cylinderOnly = MiraiArchetypeForVoxelTests()
            with { HasHemisphericalEndCaps = false };
        var (result, volume_mm3, _) = BuildAndMeasure(cylinderOnly);

        double Router_m = result.OuterRadius_mm * 1e-3;
        double Rinner_m = result.InternalRadius_mm * 1e-3;
        double L_m      = result.ShellLength_mm * 1e-3;
        double V_shell_m3 = Math.PI * (Router_m * Router_m - Rinner_m * Rinner_m) * L_m;
        double rho_kg_m3 = TankShellRegistry.CarbonFibreComposite.Density_kgm3;
        double expectedMass_kg = rho_kg_m3 * V_shell_m3;
        double voxelMass_kg = rho_kg_m3 * volume_mm3 * 1e-9;

        double relErr = Math.Abs(voxelMass_kg - expectedMass_kg) / expectedMass_kg;
        Assert.True(relErr < 0.15,
            $"Cylinder-only Mirai voxel mass {voxelMass_kg:F3} kg vs closed-form "
          + $"shell mass {expectedMass_kg:F3} kg: relErr {relErr * 100:F2} %.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void MiraiArchetype_BoundingBox_DiameterAndLengthMatchDesign()
    {
        var (result, _, bbox) = BuildAndMeasure(MiraiArchetypeForVoxelTests());
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        double expectedDiameter = 2.0 * result.OuterRadius_mm;
        Assert.InRange((double)yExt, expectedDiameter - 4.0, expectedDiameter + 4.0);
        Assert.InRange((double)xExt, result.OverallLength_mm - 4.0,
                                     result.OverallLength_mm + 4.0);
    }

    // ── Validation surface ───────────────────────────────────────────

    [Fact]
    public void Build_NullDesign_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        Assert.Throws<ArgumentNullException>(
            () => TankageVoxelBuilder.Build(null!, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveVoxelSize_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        PressureVesselDesign d = FalconArchetypeForVoxelTests();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TankageVoxelBuilder.Build(d,  0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TankageVoxelBuilder.Build(d, -1.0));
    }

    [Fact]
    public void Build_ThickWallDesign_PropagatesValidationError()
    {
        // R/t < 10 is outside the thin-wall envelope; design.ValidateSelf()
        // rejects it. Voxel builder must propagate the same error so the
        // user can't sneak a thick-wall geometry through the voxel
        // pipeline (thick-wall Lamé physics is deferred to TANK.W2).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        PressureVesselDesign thickWall = FalconArchetypeForVoxelTests()
            with { WallThickness_m = 0.05, InternalRadius_m = 0.10 };  // R/t = 2
        Assert.Throws<ArgumentException>(
            () => TankageVoxelBuilder.Build(thickWall, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonePropagatesValidationError()
    {
        // TankShellType.None sentinel must be rejected — ValidateSelf
        // throws and the voxel builder propagates.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        PressureVesselDesign bad = FalconArchetypeForVoxelTests()
            with { ShellType = TankShellType.None };
        Assert.Throws<ArgumentException>(
            () => TankageVoxelBuilder.Build(bad, VoxelSize_mm));
    }
}
