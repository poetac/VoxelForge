// AntennaWave6Tests.cs — Sprint ANT.W6 unit + integration tests for the
// microstrip patch antenna voxel builder and printability gates:
//   PatchAntennaVoxelBuilder.Build()
//   PatchGeometryResult fields
//   SubstrateTooThin gate
//   GeometryRfMismatch gate
//   PrintMaterial self-consistency checks (combined W5 + W6)
//
// VoxelBuild-tagged tests require a PicoGK Library ambient scope.

using PicoGK;
using Voxelforge.Antenna;
using Voxelforge.Geometry;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave6Tests
{
    private const float VoxelSize_mm = 1.0f;

    // ── Shared design factory ──────────────────────────────────────────

    private static AntennaLinkDesign PatchDesign(
        PrintMaterial mat = PrintMaterial.SlaResinRogers,
        double subThick_mm = 1.6,
        double patchW_mm   = 0.0,
        double patchL_mm   = 0.0) => new(
        TransmitAntennaKind:   AntennaKind.Patch,
        ReceiveAntennaKind:    AntennaKind.Patch,
        Frequency_Hz:          2.4e9,
        TransmitPower_W:       0.1,
        LinkDistance_m:        10.0,
        PrintMaterialKind:     mat,
        SubstrateThickness_mm: subThick_mm,
        PatchWidth_mm:         patchW_mm,
        PatchLength_mm:        patchL_mm);

    // ── PatchGeometryResult basic fields ──────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_BuildProducesNonEmptyVoxels()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(PatchDesign(), VoxelSize_mm);
        r.Voxels.AsPicoGK().CalculateProperties(out float vol, out _);
        Assert.True(vol > 0.0f, $"Patch assembly must have positive volume (got {vol:F0}).");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_AutoComputedDimensions_ArePositive()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(PatchDesign(), VoxelSize_mm);
        Assert.True(r.PatchWidth_mm  > 0.0, $"PatchWidth_mm = {r.PatchWidth_mm:F2}");
        Assert.True(r.PatchLength_mm > 0.0, $"PatchLength_mm = {r.PatchLength_mm:F2}");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_ResonantFrequency_IsNearDesignFrequency_WhenAutoComputed()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(PatchDesign(), VoxelSize_mm);
        Assert.InRange(r.ResonantFrequency_Hz, 2.2e9, 2.6e9);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_Material_EchoedInResult()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(
            PatchDesign(mat: PrintMaterial.SlaResinStandard), VoxelSize_mm);
        Assert.Equal(PrintMaterial.SlaResinStandard, r.Material);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_SubstrateThickness_EchoedInResult()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(
            PatchDesign(subThick_mm: 1.6), VoxelSize_mm);
        Assert.InRange(r.SubstrateThickness_mm, 1.5, 1.7);
    }

    // ── Substrate-too-thin gate (ANT.W6) ─────────────────────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_SubstrateTooThin_FiredWhenBelowMinFeature_Lpbf()
    {
        // LPBF min feature = 0.3 mm; substrate = 0.1 mm → fires.
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(
            PatchDesign(mat: PrintMaterial.Lpbf316L, subThick_mm: 0.1), VoxelSize_mm);
        Assert.True(r.SubstrateTooThin,
            "SubstrateTooThin gate must fire when thickness < LPBF min feature.");
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_SubstrateTooThin_NotFiredWhenAboveMinFeature()
    {
        // Substrate 1.6 mm >> 0.1 mm SLA minimum → no flag.
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(
            PatchDesign(mat: PrintMaterial.SlaResinRogers, subThick_mm: 1.6), VoxelSize_mm);
        Assert.False(r.SubstrateTooThin);
    }

    // ── GeometryRfMismatch gate (ANT.W7 in patch context) ─────────────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_NoMismatchFlag_WhenAutoComputedDimensions()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(PatchDesign(), VoxelSize_mm);
        // Auto-computed dimensions → self-consistent → no RF mismatch.
        Assert.False(r.GeometryRfMismatch);
    }

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void Patch_MismatchFlag_WhenPatchLengthHalved()
    {
        // Compute correct length then halve it → resonant freq ≈ 2×.
        double W_mm = AntennaSolver.ComputePatchWidth_mm(2.4e9,
            PrintMaterialTable.RelativePermittivity(PrintMaterial.SlaResinRogers));
        double L_corr = AntennaSolver.ComputePatchLength_mm(2.4e9,
            PrintMaterialTable.RelativePermittivity(PrintMaterial.SlaResinRogers),
            W_mm, 1.6);

        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        PatchGeometryResult r = AntennaVoxelBuilder.BuildPatch(
            PatchDesign(patchW_mm: W_mm, patchL_mm: 0.5 * L_corr), VoxelSize_mm);
        Assert.True(r.GeometryRfMismatch,
            "GeometryRfMismatch should fire when patch length is halved.");
    }

    // ── BuildAny dispatch for Patch (ANT.W6 extension of W5-voxel) ────

    [Fact]
    [Trait("Category", "VoxelBuild")]
    public void BuildAny_DispatchesPatch()
    {
        using var lib = new Library(VoxelSize_mm);
        using var scope = LibraryScope.Set(lib);
        IAntennaGeometryResult result = AntennaVoxelBuilder.BuildAny(PatchDesign(), VoxelSize_mm);
        Assert.IsType<PatchGeometryResult>(result);
    }

    // ── Non-solid topologies throw NotSupportedException ─────────────

    [Fact]
    public void BuildAny_IdealIsotropic_ThrowsNotSupported()
    {
        var design = new AntennaLinkDesign(
            TransmitAntennaKind: AntennaKind.IdealIsotropic,
            ReceiveAntennaKind:  AntennaKind.IdealIsotropic,
            Frequency_Hz:        1e9,
            TransmitPower_W:     1.0,
            LinkDistance_m:      100.0);
        Assert.Throws<System.NotSupportedException>(
            () => AntennaVoxelBuilder.BuildAny(design, 1.0));
    }
}
