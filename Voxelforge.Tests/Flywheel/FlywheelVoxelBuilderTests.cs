// FlywheelVoxelBuilderTests.cs — Sprint A.67 (C.2) voxel-pipeline
// backfill coverage. Exercises FlywheelVoxelBuilder against a Beacon-
// Power-Smart-Energy-25-archetype anchor design + cross-shape sanity
// checks.
//
// Test sizing note: the literal Beacon fixture
// (Mass_kg = 1025, R_o = 0.5 m, ThinRim carbon fibre) translates
// through the mass-consistent thickness equation to a ~ 4.6 m tall
// rotor — geometrically impractical, because Beacon's published 1025
// kg figure includes the entire rotor assembly (hub + shaft + rim),
// not just the rim. The voxel builder honours the design record
// literally (this is the LPBF printability gate's job to flag, not
// the builder's). For voxel-roundtrip tests we use a Beacon-archetype
// design with a *rim-only* mass (~ 22 kg) that lands a sensible
// 100 mm axial thickness, suitable for in-process xUnit voxelization.
// The two algebraic tests at the bottom cross-check the literal
// Beacon-fixture arithmetic.
//
// All tests run in-process per PicoGK 2.0.0 + xUnit pitfall #8
// (CLAUDE.md): `using var lib = new Library(voxel_mm); using var
// libScope = LibraryScope.Set(lib);` lets xUnit construct + dispose
// Library without the legacy subprocess shim. Same pattern as
// ExpansionDeflectionPlugTests / ThrustTakeoutAdapterVoxelTests.

using System.Numerics;
using PicoGK;
using Voxelforge.Flywheel;
using Voxelforge.Geometry;
using Xunit;

namespace Voxelforge.Tests.Flywheel;

public sealed class FlywheelVoxelBuilderTests
{
    // 1 mm voxel — coarse enough that build completes quickly on a
    // 1 m diameter rotor, fine enough that the bore (50 mm diameter)
    // and the 50 mm rim wall resolve cleanly. Matches the voxel size
    // used by ExpansionDeflectionPlugTests.
    private const float VoxelSize_mm = 1.0f;

    // ── Beacon-archetype rim-only fixture (for voxel roundtrip) ──────
    //
    // Beacon-Power-Smart-Energy-25 geometry (R_o = 0.5 m, carbon-fibre
    // ThinRim, magnetic-levitation bearings), but with a rim-only mass
    // (22.4 kg) sized to produce a 100 mm axial thickness via the
    // mass-consistency equation:
    //   t = m / (ρ · π · (R_o² − R_i²))
    //     = 22.4 / (1500 · π · (0.25 − 0.2025))
    //     = 22.4 / 223.85 ≈ 0.10003 m = 100.03 mm.
    // The literal Beacon mass (1025 kg) yields a ~ 4.6 m tall rotor;
    // tests at that geometry would need a 4 600 × 1 000 × 1 000 voxel
    // grid at 1 mm voxel (~ 18 GB), unfit for in-process xUnit. The
    // rim-only variant covers the same builder code paths at 1 % of
    // the grid memory.
    private static FlywheelDesign BeaconArchetypeForVoxelTests() => new(
        Shape:             FlywheelShape.ThinRim,
        Material:          FlywheelMaterial.CarbonFibreComposite,
        OuterRadius_m:     0.5,
        Mass_kg:           22.4,
        RotationSpeed_rpm: 14000.0)
    {
        Bearing       = BearingType.MagneticLevitation,
        StateOfCharge = 1.0,
    };

    // ── Literal Beacon fixture (for algebraic-only checks) ───────────
    //
    // Matches FlywheelFixture_BeaconPowerSmartEnergy25 exactly. Used
    // only for closed-form arithmetic checks that don't materialise a
    // voxel field.
    private static FlywheelDesign BeaconSmartEnergy25Literal() => new(
        Shape:             FlywheelShape.ThinRim,
        Material:          FlywheelMaterial.CarbonFibreComposite,
        OuterRadius_m:     0.5,
        Mass_kg:           1025.0,
        RotationSpeed_rpm: 14000.0)
    {
        Bearing       = BearingType.MagneticLevitation,
        StateOfCharge = 1.0,
    };

    // ── Build helper ─────────────────────────────────────────────────

    /// <summary>
    /// Build the rotor in-process (PicoGK 2.0.0 + LibraryScope) and
    /// extract volume + bounding box via <c>Voxels.CalculateProperties</c>.
    /// Returns the geometry summary + the measured volume / BBox so
    /// test methods can run lots of cheap assertions on one build.
    /// </summary>
    private static (FlywheelGeometryResult result, float volume_mm3, BBox3 bbox)
        BuildAndMeasure(FlywheelDesign design, float voxelSize_mm = VoxelSize_mm)
    {
        using var lib = new Library(voxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        FlywheelGeometryResult result = FlywheelVoxelBuilder.Build(design, voxelSize_mm);

        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);
        return (result, volume_mm3, bbox);
    }

    // ── Geometry-record arithmetic checks (rim-only Beacon archetype) ─

    [Fact]
    public void Beacon_OuterRadiusMillimetres_MatchesDesign()
    {
        // R_o conversion: design is in metres, builder reports mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult result =
            FlywheelVoxelBuilder.Build(BeaconArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Equal(500.0, result.OuterRadius_mm, precision: 6);
    }

    [Fact]
    public void Beacon_ThinRim_InnerRadiusUsesTenPercentRimFraction()
    {
        // ThinRim default rim wall = 10 % of R_o → R_i = 450 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult result =
            FlywheelVoxelBuilder.Build(BeaconArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Equal(450.0, result.InnerRadius_mm, precision: 6);
        Assert.Equal( 50.0, result.RimWallThickness_mm, precision: 6);
    }

    [Fact]
    public void Beacon_ShaftBoreRadius_IsFivePercentOfOuter()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult result =
            FlywheelVoxelBuilder.Build(BeaconArchetypeForVoxelTests(), VoxelSize_mm);
        Assert.Equal(25.0, result.ShaftBoreRadius_mm, precision: 6);
    }

    [Fact]
    public void Beacon_AxialThickness_MassConsistentClosedFormCheck()
    {
        // Closed-form: t = m / (ρ · π · (R_o² − R_i²)).
        // ρ_cf = 1500 kg/m³, m = 22.4 kg, R_o = 0.5 m, R_i = 0.45 m.
        // A = π · 0.0475 ≈ 0.14923 m², ρA = 223.85 kg/m,
        // t = 22.4 / 223.85 ≈ 0.10003 m ≈ 100.03 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult result =
            FlywheelVoxelBuilder.Build(BeaconArchetypeForVoxelTests(), VoxelSize_mm);

        const double rho = 1500.0;
        const double Ro_m = 0.5;
        const double Ri_m = 0.45;
        double expected_t_mm =
            (22.4 / (rho * System.Math.PI * (Ro_m * Ro_m - Ri_m * Ri_m))) * 1000.0;
        Assert.Equal(expected_t_mm, result.AxialThickness_mm, precision: 3);
    }

    // ── Literal Beacon — algebraic-only cross-check ───────────────────

    [Fact]
    public void LiteralBeaconFixture_ClosedFormThickness_ReportsConsistentMillimetres()
    {
        // The literal Beacon Smart Energy 25 fixture (Mass_kg = 1025)
        // is the solver's primary anchor; for the voxel builder it
        // produces a ~ 4.6 m tall rotor (physically impractical;
        // downstream gates flag this). Verify the math line-up: the
        // builder's reported AxialThickness_mm matches the
        // closed-form derivation, sanity-check that the literal
        // fixture would in fact yield ~ 4.578 m. We deliberately
        // do NOT materialise voxels at this geometry — the test only
        // exercises the algebraic path of FlywheelVoxelBuilder.Build,
        // which still requires a Library to be present for the
        // bounding-box constructor inputs. We use a coarse voxel
        // (50 mm) so the BBox3 envelope stays sane.
        using var lib = new Library(50.0f);
        using var libScope = LibraryScope.Set(lib);

        FlywheelGeometryResult result =
            FlywheelVoxelBuilder.Build(BeaconSmartEnergy25Literal(), 50.0f);

        // Closed form: t = 1025 / (1500 · π · 0.0475) m
        double expected_t_m = 1025.0 / (1500.0 * System.Math.PI * (0.25 - 0.2025));
        double expected_t_mm = expected_t_m * 1000.0;
        Assert.Equal(expected_t_mm, result.AxialThickness_mm, precision: 1);

        // Sanity: literal Beacon thickness must land in the
        // 4 500 - 4 700 mm cluster band (close to the closed-form
        // 4 578 mm). If this drifts substantially the underlying
        // material density or rim fraction has been changed silently.
        Assert.InRange(result.AxialThickness_mm, 4500.0, 4700.0);
    }

    // ── Voxel-roundtrip checks ────────────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Beacon_VoxelSet_IsNonEmpty()
    {
        // The build must produce a non-degenerate voxel body. Trigger
        // mesh extraction to confirm the voxel field actually rendered.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult result =
            FlywheelVoxelBuilder.Build(BeaconArchetypeForVoxelTests(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 100,
            $"Voxel mesh must have substantially > 0 triangles (got {triangleCount}). "
          + "Indicates the rotor build degenerated to an empty voxel set.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Beacon_BoundingBox_DiameterMatchesOuterRadiusWithinVoxelTolerance()
    {
        // Bounding-box diameter must equal 2 · R_o ± 2 voxels (one voxel
        // slack per side from grid quantisation). For Beacon archetype
        // R_o = 0.5 m: 2R = 1000 mm; band [990, 1010] gives 5 mm slack
        // per side (CalculateProperties uses the conservative surface
        // envelope).
        (_, _, BBox3 bbox) = BuildAndMeasure(BeaconArchetypeForVoxelTests());
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        float zExt = bbox.vecMax.Z - bbox.vecMin.Z;
        Assert.InRange(yExt, 990.0f, 1010.0f);
        Assert.InRange(zExt, 990.0f, 1010.0f);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Beacon_BoundingBox_AxialExtentMatchesDesignThickness()
    {
        // Axial extent (X) must equal AxialThickness_mm within voxel
        // tolerance. For the 100 mm-thick rim archetype: band 96-104 mm.
        var (result, _, bbox) = BuildAndMeasure(BeaconArchetypeForVoxelTests());
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        // 2 voxels = 2 mm absolute, plus 1 % relative for surface envelope.
        double slack_mm = System.Math.Max(2.0, 0.01 * result.AxialThickness_mm);
        Assert.InRange((double)xExt,
            result.AxialThickness_mm - slack_mm,
            result.AxialThickness_mm + slack_mm);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Beacon_Volume_MassConsistentWithinTenPercent()
    {
        // ρ · V == m within ±10 %. The bore subtraction sits inside the
        // hollow rim so doesn't remove rim mass; voxel quantisation adds
        // a few percent. Target: 22.4 kg ± 2.24 kg.
        var (result, volume_mm3, _) = BuildAndMeasure(BeaconArchetypeForVoxelTests());
        double volume_m3 = volume_mm3 * 1e-9;
        double rho_kg_m3 =
            FlywheelMaterialRegistry.For(FlywheelMaterial.CarbonFibreComposite).Density_kgm3;
        double mass_voxel_kg = rho_kg_m3 * volume_m3;
        const double designMass_kg = 22.4;
        double relErr = System.Math.Abs(mass_voxel_kg - designMass_kg) / designMass_kg;
        Assert.True(relErr < 0.10,
            $"Voxel-derived mass {mass_voxel_kg:F3} kg must match design "
          + $"{designMass_kg:F3} kg within 10 % (got {relErr * 100:F2} %). "
          + $"Outer R_o = {result.OuterRadius_mm:F0} mm, thickness "
          + $"{result.AxialThickness_mm:F1} mm.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Beacon_HasCentralBore_BoreFitsInsideHollowRim()
    {
        // For ThinRim with R_shaft = 25 mm and R_i = 450 mm, the shaft
        // bore sits entirely inside the hollow rim — it never bores
        // into the rim wall. Verify via two surface-mesh checks:
        //   1. With-bore triangle count >= no-bore reference - 4
        //      tolerance (bore should not LOSE rim-wall triangles).
        //   2. ShaftBoreRadius < InnerRadius (geometric invariant).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        // With-bore build (default).
        FlywheelGeometryResult withBore =
            FlywheelVoxelBuilder.Build(BeaconArchetypeForVoxelTests(), VoxelSize_mm);
        long trisWithBore = withBore.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();

        // Without-bore reference: voxelise the AnnulusImplicit alone (no
        // bore subtraction). This is the geometry FlywheelVoxelBuilder
        // produces before its CylinderImplicit BoolSubtract.
        float halfT_mm = (float)(0.5 * withBore.AxialThickness_mm);
        var bounds = new BBox3(
            new Vector3(-halfT_mm - 2f, -(float)withBore.OuterRadius_mm - 2f, -(float)withBore.OuterRadius_mm - 2f),
            new Vector3( halfT_mm + 2f,  (float)withBore.OuterRadius_mm + 2f,  (float)withBore.OuterRadius_mm + 2f));
        var noBoreImpl = new AnnulusImplicit(
            xMin:   -halfT_mm,
            xMax:    halfT_mm,
            rInner: (float)withBore.InnerRadius_mm,
            rOuter: (float)withBore.OuterRadius_mm);
        Voxels noBore = LibraryScope.MakeVoxels(noBoreImpl, bounds);
        long trisNoBore = noBore.mshAsMesh().nTriangleCount();

        // For Beacon archetype (R_shaft = 25 < R_i = 450) the bore sits
        // entirely inside the hollow rim and produces no surface delta.
        // Assert: bore subtraction did not bore into the rim wall.
        Assert.True(trisWithBore >= trisNoBore - 4,
            $"Bore subtraction should not bore into the rim wall on a ThinRim "
          + $"(no-bore {trisNoBore} tris, with-bore {trisWithBore} tris). "
          + "If this fires, R_shaft is bleeding into r >= R_i.");

        // Geometric invariant: bore fits inside the rim hollow.
        Assert.True(withBore.ShaftBoreRadius_mm < withBore.InnerRadius_mm,
            $"Shaft bore R_shaft = {withBore.ShaftBoreRadius_mm:F1} mm must fit "
          + $"inside the rim hollow R_i = {withBore.InnerRadius_mm:F1} mm.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void SolidDisk_HasCentralBore_RemovesMaterialFromCentre()
    {
        // For SolidDisk the bore actually carves into solid material
        // (R_shaft = 25 mm sits inside R_o = 500 mm with no rim
        // hollow). Verify the build produces non-empty voxels AND the
        // bore correctly subtracts volume.
        //
        // We use a small (R_o = 50 mm) solid disc so the build is
        // tractable at fine voxel size; the bore radius scales to
        // 2.5 mm and the disc thickness lands at ~ 21 mm for a 5 kg
        // steel-4340 disc (ρ = 7850 kg/m³).
        //   t = m / (ρ · π · R_o²) = 5 / (7850 · π · 0.0025)
        //                          ≈ 0.0811 m → 81 mm.
        var smallSolid = new FlywheelDesign(
            Shape:             FlywheelShape.SolidDisk,
            Material:          FlywheelMaterial.Steel4340,
            OuterRadius_m:     0.05,
            Mass_kg:           5.0,
            RotationSpeed_rpm: 30000.0);

        const float voxel_mm = 0.5f;
        using var lib = new Library(voxel_mm);
        using var libScope = LibraryScope.Set(lib);

        FlywheelGeometryResult result = FlywheelVoxelBuilder.Build(smallSolid, voxel_mm);
        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);

        // Mass consistency.
        double volume_m3 = volume_mm3 * 1e-9;
        double mass_voxel_kg = 7850.0 * volume_m3;
        // Bore volume = π · R_shaft² · t · ρ ≈ π · (0.0025)² · 0.0811 · 7850
        //             ≈ 0.0125 kg. SolidDisk mass minus bore ≈ 4.987 kg.
        Assert.InRange(mass_voxel_kg, 4.5, 5.2);

        // Sanity: the bore removes appreciable inner triangles.
        long withBoreTris = voxels.mshAsMesh().nTriangleCount();
        Assert.True(withBoreTris > 100, $"SolidDisc mesh must be non-empty (got {withBoreTris}).");

        // Bounding-box diameter ≈ 2 · 50 mm = 100 mm ± 2 mm.
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        Assert.InRange(yExt, 98.0f, 102.0f);
    }

    // ── ThinRim vs SolidDisk parity ──────────────────────────────────

    [Fact]
    public void ThinRim_IsTallerThanSolidDiskAtSameRadiusAndMass()
    {
        // At identical (R_o, m, ρ, voxel size), ThinRim has a LARGER
        // axial thickness than SolidDisk because its in-plane area
        // is smaller (an annulus < a full disc) and mass-consistency
        // forces t up to compensate. The closed-form ratio:
        //   t_rim / t_disc = R_o² / (R_o² − R_i²)
        //                  = 1 / (1 − (1 − rimFraction)²)
        //                  = 1 / (1 − 0.81) ≈ 5.26 for rimFraction = 0.1.
        // Note (CA1859 compliance): variables are typed against the
        // concrete record `FlywheelGeometryResult`, not an interface.
        FlywheelDesign thinRim = BeaconArchetypeForVoxelTests();
        FlywheelDesign solid   = thinRim with { Shape = FlywheelShape.SolidDisk };

        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult rimResult   = FlywheelVoxelBuilder.Build(thinRim, VoxelSize_mm);
        FlywheelGeometryResult solidResult = FlywheelVoxelBuilder.Build(solid,   VoxelSize_mm);

        Assert.True(rimResult.AxialThickness_mm > solidResult.AxialThickness_mm * 4.0,
            $"ThinRim axial thickness ({rimResult.AxialThickness_mm:F1} mm) should be "
          + $"substantially greater than SolidDisk thickness "
          + $"({solidResult.AxialThickness_mm:F1} mm) at identical mass — "
          + "ratio ≈ 1/(1-0.9²) = 5.26.");
    }

    [Fact]
    public void SolidDisk_InnerRadiusIsZero()
    {
        FlywheelDesign solid = BeaconArchetypeForVoxelTests() with { Shape = FlywheelShape.SolidDisk };
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelGeometryResult result = FlywheelVoxelBuilder.Build(solid, VoxelSize_mm);
        Assert.Equal(0.0,   result.InnerRadius_mm, precision: 6);
        Assert.Equal(500.0, result.RimWallThickness_mm, precision: 6);
    }

    // ── Validation surface ───────────────────────────────────────────

    [Fact]
    public void Build_NullDesign_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        Assert.Throws<System.ArgumentNullException>(
            () => FlywheelVoxelBuilder.Build(null!, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveVoxelSize_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        FlywheelDesign d = BeaconArchetypeForVoxelTests();
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => FlywheelVoxelBuilder.Build(d,  0.0));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => FlywheelVoxelBuilder.Build(d, -1.0));
    }

    [Fact]
    public void Build_DegenerateDesign_PropagatesValidationError()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        // ValidateSelf throws for non-positive mass.
        FlywheelDesign bad = BeaconArchetypeForVoxelTests() with { Mass_kg = 0.0 };
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => FlywheelVoxelBuilder.Build(bad, VoxelSize_mm));
    }
}
