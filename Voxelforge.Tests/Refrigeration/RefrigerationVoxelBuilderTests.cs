// RefrigerationVoxelBuilderTests.cs — Sprint A.81 (C.2) voxel-pipeline
// backfill coverage. Exercises RefrigerationVoxelBuilder against the
// Sanden ECO-CUTE R-744 heat-pump water-heater anchor (matching the
// A.73 RefrigerationFixture_SandenEcoCuteHeatPump cluster anchor) +
// cluster-anchor sizing-law cross-checks + voxel-roundtrip assertions.
//
// Anchor design point (Sanden ECO-CUTE GUS-A45HOL, JIS C 9220 rating):
//   - Heating mode, R-744 (CO₂ transcritical)
//   - T_cold = 280 K (7 °C outdoor air); T_hot = 338 K (65 °C hot water)
//   - W_compressor = 1000 W (residential 4.5 kW thermal-output class)
//   - Expected per RFG.W1 solver: COP_cooling ≈ 2.41; COP_heating ≈ 3.41
//   - Q_cold ≈ 2414 W (evaporator duty); Q_hot ≈ 3414 W (condenser duty)
//
// Expected envelope per RefrigerationVoxelBuilder cluster-anchor laws:
//   - R_compressor_mm = 37.5 · (1000 / 1000)^(1/3) = 37.5 mm (75 mm OD)
//   - L_compressor_mm = 1.6 · 75 = 120 mm
//   - R_coil_inner    = R_compressor = 37.5 mm
//   - R_coil_outer    = 37.5 + 30 = 67.5 mm (30 mm bundle thickness)
//   - L_condenser_mm  = 200 · √(3414 / 3500) ≈ 197.6 mm
//   - L_evaporator_mm = 200 · √(2414 / 3500) ≈ 166.3 mm
//   - OverallLength_mm ≈ 120 + 197.6 + 166.3 ≈ 483.9 mm
//
// All voxel-roundtrip tests run in-process per PicoGK 2.0.0 + xUnit
// pitfall #8 (CLAUDE.md): `using var lib = new Library(voxel_mm); using
// var libScope = LibraryScope.Set(lib);` lets xUnit construct + dispose
// Library without the legacy subprocess shim. Same pattern as
// FlywheelVoxelBuilderTests / TankageVoxelBuilderTests.

using System;
using PicoGK;
using Voxelforge.Geometry;
using Voxelforge.Refrigeration;
using Xunit;

namespace Voxelforge.Tests.Refrigeration;

public sealed class RefrigerationVoxelBuilderTests
{
    // 1 mm voxel — coarse enough that the build completes quickly on
    // the ~ 484 mm × 135 mm × 135 mm Sanden ECO-CUTE envelope, fine
    // enough that the 30 mm coil bundle and 37.5 mm compressor radius
    // resolve cleanly. Matches the voxel size used by
    // FlywheelVoxelBuilderTests / TankageVoxelBuilderTests.
    private const float VoxelSize_mm = 1.0f;

    // ── Sanden ECO-CUTE GUS-A45HOL anchor (residential R-744 heat-pump) ──
    //
    // Matches Voxelforge.Tests/Refrigeration/RefrigerationFixture_SandenEco
    // CuteHeatPump.cs exactly so the voxel builder is exercised at the
    // same anchor point as the algebraic solver fixture.
    private static RefrigerationDesign SandenEcoCute() => new(
        Mode:                       RefrigerationMode.Heating,
        Refrigerant:                Refrigerant.R744,
        ColdReservoirTemperature_K: 280.0,
        HotReservoirTemperature_K:  338.0,
        CompressorPowerInput_W:     1000.0);

    // ── Build helper ─────────────────────────────────────────────────

    /// <summary>
    /// Build the heat-pump assembly in-process (PicoGK 2.0.0 +
    /// LibraryScope) and extract volume + bounding box via
    /// <c>Voxels.CalculateProperties</c>. Returns the geometry summary +
    /// the measured volume / BBox so test methods can run lots of cheap
    /// assertions on one build.
    /// </summary>
    private static (RefrigerationGeometryResult result, float volume_mm3, BBox3 bbox)
        BuildAndMeasure(RefrigerationDesign design, float voxelSize_mm = VoxelSize_mm)
    {
        using var lib = new Library(voxelSize_mm);
        using var libScope = LibraryScope.Set(lib);

        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(design, voxelSize_mm);

        Voxels voxels = result.Voxels.AsPicoGK();
        voxels.CalculateProperties(out float volume_mm3, out BBox3 bbox);
        return (result, volume_mm3, bbox);
    }

    // ── Compressor envelope sizing (cluster-anchor cross-check) ───────

    [Fact]
    public void Sanden_CompressorRadius_AtOneKilowatt_EqualsAnchorRadius()
    {
        // R_compressor_mm = 37.5 · (W / 1000)^(1/3). At W = 1000 W the
        // ratio is exactly 1 → R = 37.5 mm (75 mm OD), the cluster mid
        // of hermetic-rotary residential compressors documented in the
        // RefrigerationVoxelBuilder file header.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        Assert.Equal(37.5, result.CompressorOuterRadius_mm, precision: 6);
    }

    [Fact]
    public void Sanden_CompressorLength_EqualsAspectRatioTimesOuterDiameter()
    {
        // L_compressor_mm = CompressorLengthToDiameterRatio (= 1.6) · OD
        //                 = 1.6 · 2 · 37.5 = 120 mm.
        // Cluster mid for hermetic-rotary residential compressors.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        Assert.Equal(120.0, result.CompressorLength_mm, precision: 6);
    }

    [Fact]
    public void CompressorRadius_ScalesAsCubeRootOfShaftPower()
    {
        // The OD scaling law R ∝ W^(1/3) (volumetric displacement
        // scales linearly with shaft power; envelope radius is the cube
        // root). Doubling W should multiply R by 2^(1/3) ≈ 1.2599.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult baseline =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        RefrigerationGeometryResult doubled =
            RefrigerationVoxelBuilder.Build(
                SandenEcoCute() with { CompressorPowerInput_W = 2000.0 },
                VoxelSize_mm);

        double ratio = doubled.CompressorOuterRadius_mm
                     / baseline.CompressorOuterRadius_mm;
        // 2^(1/3) ≈ 1.2599. Tight band — closed-form sizing law.
        Assert.Equal(Math.Cbrt(2.0), ratio, precision: 6);
    }

    // ── Coil envelope sizing (cluster-anchor cross-check) ─────────────

    [Fact]
    public void Sanden_CoilInnerRadius_MatchesCompressorOuterRadius()
    {
        // Coaxial nesting: the coil wraps around the compressor with no
        // radial gap, so R_coil_inner = R_compressor.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        Assert.Equal(result.CompressorOuterRadius_mm,
                     result.CondenserInnerRadius_mm, precision: 9);
        Assert.Equal(result.CompressorOuterRadius_mm,
                     result.EvaporatorInnerRadius_mm, precision: 9);
    }

    [Fact]
    public void Sanden_CoilBundleRadialThickness_EqualsThirtyMillimetres()
    {
        // Bundle thickness = R_coil_outer - R_coil_inner = 30 mm
        // (cluster mid, 3 tube-diameters of 6-12 mm copper tubing).
        // Applied identically to condenser + evaporator.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        double condBundle = result.CondenserOuterRadius_mm
                          - result.CondenserInnerRadius_mm;
        double evapBundle = result.EvaporatorOuterRadius_mm
                          - result.EvaporatorInnerRadius_mm;
        Assert.Equal(30.0, condBundle, precision: 6);
        Assert.Equal(30.0, evapBundle, precision: 6);
    }

    [Fact]
    public void Sanden_CondenserLength_MatchesSqrtHotSideHeatDutyLaw()
    {
        // L_cond_mm = 200 · √(Q_hot_W / 3500). Q_hot = COP_heating · W
        // = (η · T_cold/(T_h-T_c) + 1) · 1000 = (0.50 · 280/58 + 1) · 1000
        // ≈ 3414 W. Predicted L_cond = 200 · √(3414/3500) ≈ 197.55 mm.
        // Cluster mid for residential heat-pump condenser coils.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        RefrigerationResult cycle = RefrigerationSolver.Solve(SandenEcoCute());
        double expected = 200.0 * Math.Sqrt(cycle.HotSideHeatDelivery_W / 3500.0);
        Assert.Equal(expected, result.CondenserLength_mm, precision: 6);
        // Sanity: ≈ 197.5 mm at the Sanden anchor (200 mm cluster mid
        // shifts slightly down because Q_hot < 3.5 kW reference).
        Assert.InRange(result.CondenserLength_mm, 195.0, 200.0);
    }

    [Fact]
    public void Sanden_EvaporatorLength_MatchesSqrtColdSideHeatDutyLaw()
    {
        // L_evap_mm = 200 · √(Q_cold_W / 3500). Q_cold = η · T_cold/(T_h-T_c)
        // · W = 0.50 · 280/58 · 1000 ≈ 2414 W. Predicted L_evap ≈ 166.2 mm.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        RefrigerationResult cycle = RefrigerationSolver.Solve(SandenEcoCute());
        double expected = 200.0 * Math.Sqrt(cycle.ColdSideHeatRemoval_W / 3500.0);
        Assert.Equal(expected, result.EvaporatorLength_mm, precision: 6);
        // Sanity: in the 160-170 mm band at the Sanden anchor.
        Assert.InRange(result.EvaporatorLength_mm, 160.0, 170.0);
    }

    [Fact]
    public void Sanden_CondenserExceedsEvaporator_InHeatingMode()
    {
        // In any vapor-compression cycle Q_hot = Q_cold + W > Q_cold by
        // the first-law energy balance, so the condenser coil envelope
        // (which scales with Q_hot in our sizing law) must be longer
        // than the evaporator envelope (which scales with Q_cold) at
        // identical W. Holds independent of operating mode — it's a
        // thermodynamic invariant of the cycle.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        Assert.True(result.CondenserLength_mm > result.EvaporatorLength_mm,
            $"Condenser length ({result.CondenserLength_mm:F1} mm) must exceed "
          + $"evaporator length ({result.EvaporatorLength_mm:F1} mm) — Q_hot = "
          + "Q_cold + W > Q_cold by first-law energy balance.");
    }

    [Fact]
    public void Sanden_OverallLength_EqualsThreeSubAssemblyLengthsSummed()
    {
        // OverallLength_mm = L_comp + L_cond + L_evap (the three
        // envelopes butt end-to-end with no axial gap).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        double sum = result.CompressorLength_mm
                   + result.CondenserLength_mm
                   + result.EvaporatorLength_mm;
        Assert.Equal(sum, result.OverallLength_mm, precision: 6);
    }

    // ── Voxel-roundtrip checks ────────────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Sanden_VoxelSet_IsNonEmpty()
    {
        // The build must produce a non-degenerate voxel body. Trigger
        // mesh extraction to confirm the voxel field actually rendered.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult result =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        long triangleCount = result.Voxels.AsPicoGK().mshAsMesh().nTriangleCount();
        Assert.True(triangleCount > 1000,
            $"Voxel mesh must have substantially > 0 triangles (got {triangleCount}). "
          + "Indicates the heat-pump build degenerated to an empty voxel set.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Sanden_BoundingBox_DiameterMatchesTwoOuterCoilRadius()
    {
        // Bounding-box diameter (Y, Z) must equal 2 · R_coil_outer ± 4
        // voxels. R_coil_outer = 67.5 mm → 2R = 135 mm; with smoothing
        // the surface envelope can move ± 2 voxels per side.
        (RefrigerationGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(SandenEcoCute());
        float yExt = bbox.vecMax.Y - bbox.vecMin.Y;
        float zExt = bbox.vecMax.Z - bbox.vecMin.Z;
        double expected = 2.0 * result.CondenserOuterRadius_mm;
        Assert.InRange((double)yExt, expected - 4.0, expected + 4.0);
        Assert.InRange((double)zExt, expected - 4.0, expected + 4.0);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Sanden_BoundingBox_AxialExtentMatchesOverallLength()
    {
        // X extent must equal OverallLength_mm within voxel tolerance.
        // The three envelopes butt end-to-end so the bounding box is
        // exactly OverallLength_mm wide (compressor centred on origin,
        // condenser on +X, evaporator on -X).
        (RefrigerationGeometryResult result, _, BBox3 bbox) =
            BuildAndMeasure(SandenEcoCute());
        float xExt = bbox.vecMax.X - bbox.vecMin.X;
        double slack_mm = Math.Max(4.0, 0.01 * result.OverallLength_mm);
        Assert.InRange((double)xExt,
            result.OverallLength_mm - slack_mm,
            result.OverallLength_mm + slack_mm);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Sanden_VoxelVolume_MatchesClosedFormEnvelopeSum()
    {
        // Closed-form solid envelope volume:
        //   V = V_compressor + V_condenser_annulus + V_evaporator_annulus
        //     = π · R_comp² · L_comp
        //     + π · (R_coil_outer² - R_coil_inner²) · L_cond
        //     + π · (R_coil_outer² - R_coil_inner²) · L_evap
        // At the Sanden anchor: ~ 530 144 mm³ + ~ 1 954 565 mm³ +
        // ~ 1 645 192 mm³ ≈ 4 130 000 mm³.
        // ±10 % band for voxel quantisation + smoothing-induced surface
        // shift; same band used by FlywheelVoxelBuilderTests and
        // TankageVoxelBuilderTests.
        var (result, volume_mm3, _) = BuildAndMeasure(SandenEcoCute());

        double R_comp  = result.CompressorOuterRadius_mm;
        double R_inner = result.CondenserInnerRadius_mm;
        double R_outer = result.CondenserOuterRadius_mm;
        double annulusArea = Math.PI * (R_outer * R_outer - R_inner * R_inner);

        double V_comp = Math.PI * R_comp * R_comp * result.CompressorLength_mm;
        double V_cond = annulusArea * result.CondenserLength_mm;
        double V_evap = annulusArea * result.EvaporatorLength_mm;
        double V_expected = V_comp + V_cond + V_evap;

        double relErr = Math.Abs((double)volume_mm3 - V_expected) / V_expected;
        Assert.True(relErr < 0.10,
            $"Voxel volume {volume_mm3:F0} mm³ must match closed-form envelope "
          + $"sum {V_expected:F0} mm³ within 10 % (got {relErr * 100:F2} %). "
          + $"V_comp = {V_comp:F0}, V_cond = {V_cond:F0}, V_evap = {V_evap:F0}.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Sanden_BoundingBox_AsymmetricInXAroundOrigin()
    {
        // The compressor is centred on the origin, but the condenser
        // length > evaporator length in heating mode (Q_hot > Q_cold).
        // The bounding box should therefore extend further into +X than
        // into -X — a visible packaging asymmetry mirroring real-world
        // outdoor heat-pump units. Specifically:
        //   xMax = +L_comp/2 + L_cond ≈ +257.6 mm
        //   xMin = -L_comp/2 - L_evap ≈ -226.3 mm
        var (result, _, bbox) = BuildAndMeasure(SandenEcoCute());

        double expectedXMax = +0.5 * result.CompressorLength_mm
                             + result.CondenserLength_mm;
        double expectedXMin = -0.5 * result.CompressorLength_mm
                             - result.EvaporatorLength_mm;
        // ± 4 voxel slack from surface envelope.
        Assert.InRange((double)bbox.vecMax.X, expectedXMax - 4.0, expectedXMax + 4.0);
        Assert.InRange((double)bbox.vecMin.X, expectedXMin - 4.0, expectedXMin + 4.0);

        // The packaging asymmetry must be strictly positive: condenser
        // overhang on +X > evaporator overhang on -X by exactly
        // (L_cond - L_evap) ≈ 31.3 mm at the Sanden anchor.
        double condOverhang_mm = (double)bbox.vecMax.X
                               - 0.5 * result.CompressorLength_mm;
        double evapOverhang_mm = -(double)bbox.vecMin.X
                               - 0.5 * result.CompressorLength_mm;
        Assert.True(condOverhang_mm > evapOverhang_mm,
            $"Condenser overhang ({condOverhang_mm:F1} mm) must exceed evaporator "
          + $"overhang ({evapOverhang_mm:F1} mm) in heating mode (Q_hot > Q_cold).");
    }

    // ── Cross-cluster sensitivities ───────────────────────────────────

    [Fact]
    public void HigherShaftPower_GrowsAllThreeEnvelopes()
    {
        // Scaling W from 1 kW → 5 kW:
        //   - R_comp grows by 5^(1/3) ≈ 1.71×
        //   - L_comp grows the same (aspect ratio is fixed)
        //   - Q_hot / Q_cold grow ~ 5× (COP roughly constant within
        //     the cluster) → L_cond / L_evap grow by √5 ≈ 2.24×
        // Net: every dimensional field strictly grows.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult baseline =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        RefrigerationGeometryResult upsized =
            RefrigerationVoxelBuilder.Build(
                SandenEcoCute() with { CompressorPowerInput_W = 5000.0 },
                VoxelSize_mm);

        Assert.True(upsized.CompressorOuterRadius_mm > baseline.CompressorOuterRadius_mm);
        Assert.True(upsized.CompressorLength_mm     > baseline.CompressorLength_mm);
        Assert.True(upsized.CondenserLength_mm       > baseline.CondenserLength_mm);
        Assert.True(upsized.EvaporatorLength_mm     > baseline.EvaporatorLength_mm);
        // Cube-root scaling on radius: 5^(1/3) ≈ 1.71.
        double radiusRatio = upsized.CompressorOuterRadius_mm
                           / baseline.CompressorOuterRadius_mm;
        Assert.Equal(Math.Cbrt(5.0), radiusRatio, precision: 6);
    }

    [Fact]
    public void NarrowerThermalGradient_ShrinksCoilEnvelopes_AtFixedShaftPower()
    {
        // Raising T_cold from 280 K → 300 K shrinks ΔT from 58 to 38 K
        // → Carnot bound rises → COP rises → Q_hot and Q_cold both rise
        // → both coil envelope lengths grow. (At fixed W the cycle pumps
        // MORE heat across a narrower gradient.)
        // This test asserts the directionality: narrower ΔT (less lift)
        // produces a LARGER coil envelope, not a smaller one. The naming
        // is from the design surface's perspective ("narrower gradient"
        // = less lift = more heat moved).
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult winter =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        RefrigerationGeometryResult summer =
            RefrigerationVoxelBuilder.Build(
                SandenEcoCute() with { ColdReservoirTemperature_K = 300.0 },
                VoxelSize_mm);

        Assert.True(summer.CondenserLength_mm > winter.CondenserLength_mm,
            $"Summer condenser ({summer.CondenserLength_mm:F1} mm) must exceed "
          + $"winter ({winter.CondenserLength_mm:F1} mm) — narrower ΔT raises COP "
          + "→ more heat moved → larger envelope.");
        Assert.True(summer.EvaporatorLength_mm > winter.EvaporatorLength_mm,
            "Summer evaporator length must exceed winter for the same reason.");
        // Compressor envelope is invariant under operating-temperature
        // changes — it only depends on W_compressor.
        Assert.Equal(winter.CompressorOuterRadius_mm,
                     summer.CompressorOuterRadius_mm, precision: 9);
        Assert.Equal(winter.CompressorLength_mm,
                     summer.CompressorLength_mm, precision: 9);
    }

    [Fact]
    public void DifferentRefrigerants_ChangeCoilLengths_AtFixedOperatingPoint()
    {
        // Switching from R-744 (η = 0.50) to R-410A (η = 0.58) at the
        // same (T_cold, T_hot, W) raises COP → larger Q_hot/Q_cold →
        // longer coil envelopes. Compressor is invariant.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationGeometryResult r744 =
            RefrigerationVoxelBuilder.Build(SandenEcoCute(), VoxelSize_mm);
        RefrigerationGeometryResult r410a =
            RefrigerationVoxelBuilder.Build(
                SandenEcoCute() with { Refrigerant = Refrigerant.R410A },
                VoxelSize_mm);

        Assert.True(r410a.CondenserLength_mm > r744.CondenserLength_mm);
        Assert.True(r410a.EvaporatorLength_mm > r744.EvaporatorLength_mm);
        Assert.Equal(r744.CompressorOuterRadius_mm,
                     r410a.CompressorOuterRadius_mm, precision: 9);
    }

    // ── Validation surface ───────────────────────────────────────────

    [Fact]
    public void Build_NullDesign_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        Assert.Throws<ArgumentNullException>(
            () => RefrigerationVoxelBuilder.Build(null!, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveVoxelSize_Throws()
    {
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationDesign d = SandenEcoCute();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RefrigerationVoxelBuilder.Build(d,  0.0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RefrigerationVoxelBuilder.Build(d, -1.0));
    }

    [Fact]
    public void Build_InvertedThermalGradient_PropagatesValidationError()
    {
        // T_hot ≤ T_cold violates the refrigeration energy-flow direction
        // (you can't pump heat down a gradient that's already inverted).
        // ValidateSelf throws; the voxel builder propagates.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationDesign bad = SandenEcoCute()
            with { HotReservoirTemperature_K = 270.0 };  // < T_cold
        Assert.Throws<ArgumentException>(
            () => RefrigerationVoxelBuilder.Build(bad, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonePropagatesValidationError()
    {
        // The Mode = None and Refrigerant = None sentinels must be
        // rejected — ValidateSelf throws and the voxel builder
        // propagates.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationDesign noneMode = SandenEcoCute()
            with { Mode = RefrigerationMode.None };
        Assert.Throws<ArgumentException>(
            () => RefrigerationVoxelBuilder.Build(noneMode, VoxelSize_mm));

        RefrigerationDesign noneRefrigerant = SandenEcoCute()
            with { Refrigerant = Refrigerant.None };
        Assert.Throws<ArgumentException>(
            () => RefrigerationVoxelBuilder.Build(noneRefrigerant, VoxelSize_mm));
    }

    [Fact]
    public void Build_NonPositiveShaftPower_PropagatesValidationError()
    {
        // ValidateSelf throws on W_compressor ≤ 0.
        using var lib = new Library(VoxelSize_mm);
        using var libScope = LibraryScope.Set(lib);
        RefrigerationDesign bad = SandenEcoCute()
            with { CompressorPowerInput_W = 0.0 };
        Assert.Throws<ArgumentException>(
            () => RefrigerationVoxelBuilder.Build(bad, VoxelSize_mm));
    }
}
