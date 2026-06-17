// AntennaWave5VoxelTests.cs — Sprint ANT.W5-voxel PicoGK geometry tests
// for the three new antenna topology builders (Helical, Horn, YagiUda)
// and the BuildAny dispatch method. Uses the xUnit + PicoGK Library
// scope pattern established in AntennaVoxelBuilderTests.cs.
//
// Tests tagged [Trait("Category", "VoxelBuild")] require a PicoGK
// Library ambient scope; they run in the standard Voxelforge.Tests
// xUnit host (NOT in a subprocess). Pattern: new Library(voxel_mm)
// + LibraryScope.Set(lib) per test, as per xUnit + PicoGK pitfall #8.

using PicoGK;
using Voxelforge.Antenna;
using Voxelforge.Geometry;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave5VoxelTests
{
    private const float VoxelSize_mm = 2.0f;  // coarser voxel for speed

    // ── Shared design factories ────────────────────────────────────────

    // 1 GHz designs: λ = 300 mm → coarse features, fast voxelisation.
    private static AntennaLinkDesign Helical1GHz() => new(
        TransmitAntennaKind:     AntennaKind.Helical,
        ReceiveAntennaKind:      AntennaKind.Helical,
        Frequency_Hz:            1e9,
        TransmitPower_W:         1.0,
        LinkDistance_m:          1_000.0,
        HelicalTurns:            4,
        HelicalCircumference_rel: 1.0,
        HelicalTurnSpacing_rel:   0.25);

    // 10 GHz: λ = 30 mm → R_aperture = 150 mm, L_horn ≈ 595 mm (manageable).
    // 1 GHz would give λ = 300 mm → L_horn ≈ 5946 mm → ~10 B voxels → crash.
    private static AntennaLinkDesign Horn10GHz() => new(
        TransmitAntennaKind: AntennaKind.Horn,
        ReceiveAntennaKind:  AntennaKind.Horn,
        Frequency_Hz:        10e9,
        TransmitPower_W:     1.0,
        LinkDistance_m:      1_000.0);

    private static AntennaLinkDesign Yagi1GHz() => new(
        TransmitAntennaKind: AntennaKind.YagiUda,
        ReceiveAntennaKind:  AntennaKind.YagiUda,
        Frequency_Hz:        1e9,
        TransmitPower_W:     1.0,
        LinkDistance_m:      100.0);

    // ── Helical builder ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Helical_BuildProducesNonEmptyVoxels()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HelicalGeometryResult r = AntennaVoxelBuilder.BuildHelical(Helical1GHz(), VoxelSize_mm);
        r.Voxels.AsPicoGK().CalculateProperties(out float vol, out _);
        Assert.True(vol > 0.0f,
            $"Helical voxel body must have positive volume (got {vol:F0} mm³).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Helical_GeometryResultHasCorrectHelixRadius()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HelicalGeometryResult r = AntennaVoxelBuilder.BuildHelical(Helical1GHz(), VoxelSize_mm);
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 1e9 * 1000.0;
        double expectedR = lambda_mm / (2.0 * System.Math.PI);  // C/λ=1.0
        Assert.InRange(r.HelixRadius_mm, expectedR * 0.99, expectedR * 1.01);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Helical_TotalAxialLength_IsNTurnsTimesPitch()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HelicalGeometryResult r = AntennaVoxelBuilder.BuildHelical(Helical1GHz(), VoxelSize_mm);
        double lambda_mm     = AntennaSolver.SpeedOfLight_ms / 1e9 * 1000.0;
        double expectedLen   = 4 * 0.25 * lambda_mm;  // N=4, S/λ=0.25
        Assert.InRange(r.TotalAxialLength_mm, expectedLen * 0.99, expectedLen * 1.01);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Helical_WireTooThinFlag_FalseForLpbf316L_At1GHz()
    {
        // At 1 GHz, wire = λ/50 = 6 mm >> 0.3 mm LPBF minimum → no flag.
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HelicalGeometryResult r = AntennaVoxelBuilder.BuildHelical(Helical1GHz(), VoxelSize_mm);
        Assert.False(r.WireTooThinForMaterial);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Helical_WireTooThinFlag_TrueAtHighFrequencyWithFdm()
    {
        // At 100 GHz, λ = 3 mm, wire = 3/50 = 0.06 mm < 0.4 mm FDM min → fires.
        var design = Helical1GHz() with
        {
            Frequency_Hz      = 100e9,
            PrintMaterialKind = PrintMaterial.ConductiveFdmPla
        };
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HelicalGeometryResult r = AntennaVoxelBuilder.BuildHelical(design, VoxelSize_mm);
        Assert.True(r.WireTooThinForMaterial);
    }

    // ── Horn builder ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Horn_BuildProducesNonEmptyVoxels()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HornGeometryResult r = AntennaVoxelBuilder.BuildHorn(Horn10GHz(), VoxelSize_mm);
        r.Voxels.AsPicoGK().CalculateProperties(out float vol, out _);
        Assert.True(vol > 0.0f, $"Horn voxel body must have positive volume (got {vol:F0}).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Horn_ApertureDiameterLargerThanThroatDiameter()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HornGeometryResult r = AntennaVoxelBuilder.BuildHorn(Horn10GHz(), VoxelSize_mm);
        Assert.True(r.ApertureDiameter_mm > r.ThroatDiameter_mm,
            $"Aperture {r.ApertureDiameter_mm:F1} mm must exceed throat {r.ThroatDiameter_mm:F1} mm.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Horn_FlareAngle_MatchesBuilderConstant()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HornGeometryResult r = AntennaVoxelBuilder.BuildHorn(Horn10GHz(), VoxelSize_mm);
        Assert.Equal(HornAntennaVoxelBuilder.FlareAngle_deg,
            r.FlareAngle_deg, precision: 10);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Horn_WallThickness_IsAtLeastFourTimesVoxelSize()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        HornGeometryResult r = AntennaVoxelBuilder.BuildHorn(Horn10GHz(), VoxelSize_mm);
        Assert.True(r.WallThickness_mm >= 4.0 * VoxelSize_mm,
            $"Wall {r.WallThickness_mm:F2} mm must be ≥ 4× voxel = {4*VoxelSize_mm:F2} mm.");
    }

    // ── Yagi-Uda builder ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void YagiUda_BuildProducesNonEmptyVoxels()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        YagiUdaGeometryResult r = AntennaVoxelBuilder.BuildYagiUda(Yagi1GHz(), VoxelSize_mm);
        r.Voxels.AsPicoGK().CalculateProperties(out float vol, out _);
        Assert.True(vol > 0.0f, $"Yagi voxel body must have positive volume (got {vol:F0}).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void YagiUda_ReflectorLongerThanDirector()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        YagiUdaGeometryResult r = AntennaVoxelBuilder.BuildYagiUda(Yagi1GHz(), VoxelSize_mm);
        Assert.True(r.ReflectorLength_mm > r.DirectorLength_mm,
            "Reflector must be longer than director (classic Yagi-Uda design).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void YagiUda_DirectorCount_MatchesDefault()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        YagiUdaGeometryResult r = AntennaVoxelBuilder.BuildYagiUda(Yagi1GHz(), VoxelSize_mm);
        Assert.Equal(YagiUdaAntennaVoxelBuilder.DefaultDirectorCount, r.DirectorCount);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void YagiUda_OverhangViolated_ForLpbf316L()
    {
        // Elements are at 90° overhang; LPBF max is 45° → flag fires.
        var design = Yagi1GHz() with { PrintMaterialKind = PrintMaterial.Lpbf316L };
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        YagiUdaGeometryResult r = AntennaVoxelBuilder.BuildYagiUda(design, VoxelSize_mm);
        Assert.True(r.ElementOverhangViolated);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void YagiUda_OverhangNotViolated_ForSlaResin()
    {
        // SLA: max overhang = 90° (liquid support) → no violation.
        var design = Yagi1GHz() with { PrintMaterialKind = PrintMaterial.SlaResinStandard };
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        YagiUdaGeometryResult r = AntennaVoxelBuilder.BuildYagiUda(design, VoxelSize_mm);
        Assert.False(r.ElementOverhangViolated);
    }

    // ── BuildAny dispatch ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void BuildAny_DispatchesParabolicDish()
    {
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:    AntennaKind.ParabolicDish,
            ReceiveAntennaKind:     AntennaKind.ParabolicDish,
            Frequency_Hz:           1e9,
            TransmitPower_W:        1.0,
            LinkDistance_m:         1_000.0,
            TransmitDishDiameter_m: 1.0,
            ReceiveDishDiameter_m:  1.0);
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        IAntennaGeometryResult result = AntennaVoxelBuilder.BuildAny(design, VoxelSize_mm);
        Assert.IsType<AntennaGeometryResult>(result);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void BuildAny_DispatchesHelical()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        IAntennaGeometryResult result = AntennaVoxelBuilder.BuildAny(Helical1GHz(), VoxelSize_mm);
        Assert.IsType<HelicalGeometryResult>(result);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void BuildAny_DispatchesHorn()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        IAntennaGeometryResult result = AntennaVoxelBuilder.BuildAny(Horn10GHz(), VoxelSize_mm);
        Assert.IsType<HornGeometryResult>(result);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void BuildAny_DispatchesYagiUda()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        IAntennaGeometryResult result = AntennaVoxelBuilder.BuildAny(Yagi1GHz(), VoxelSize_mm);
        Assert.IsType<YagiUdaGeometryResult>(result);
    }
}
