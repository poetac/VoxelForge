// Sprint0Pr2OnDiskFormatStringTests.cs — risk insurance for the namespace
// rename in Sprint 0 PR-2.
//
// PR-2 bulk-renamed `RegenChamberDesigner.*` → `Voxelforge.*` namespaces
// across ~360 .cs files. The plan deliberately preserves 5 magic strings
// baked into on-disk artifacts (DesignPersistence.AppName, 3MF
// Application metadata, PrinterParameterPreset schema tag, STL header
// defaults) so existing JSON saves / 3MF / STL files continue to
// round-trip without a schema bump (v21 → v22).
//
// These tests pin the exact literal "RegenChamberDesigner..." values
// that survived the rename. If a future cleanup pass accidentally
// touches a magic string, the corresponding test fails immediately.
// Together with existing coverage in `NoyronTierA34Tests`
// (PrinterParameterPreset schema) + the round-trip suite, this gives
// full pre/post-rename validation.

using System.IO;
using Voxelforge.Geometry;
using Voxelforge.IO;

namespace Voxelforge.Tests;

public class Sprint0Pr2OnDiskFormatStringTests
{
    [Fact]
    public void DesignPersistence_AppName_DefaultIsRegenChamberDesignerLiteral()
    {
        // SavedDesign.AppName field default kept as the literal
        // "RegenChamberDesigner" through the namespace rename so existing
        // on-disk JSON saves round-trip cleanly without a schema migration.
        var saved = new SavedDesign();
        Assert.Equal("RegenChamberDesigner", saved.AppName);
    }

    [Fact]
    public void ThreeMfExport_ApplicationMetadata_IsRegenChamberDesignerLiteral()
    {
        // The 3MF Application tag is set inside ThreeMFExport.Save's
        // Meta() call. Existing 3MF round-trip tests in PR #255 (OOB-15)
        // assert the metadata block is intact; this test pins the literal
        // string explicitly so a future namespace cleanup can't silently
        // drop the "RegenChamberDesigner" identifier.
        string sourcePath = TestPaths.RepoRelative(
            "Voxelforge.Core/IO/ThreeMFExport.cs");
        Assert.True(File.Exists(sourcePath),
            $"ThreeMFExport.cs not found at {sourcePath}");
        string source = File.ReadAllText(sourcePath);
        Assert.Contains(
            "Meta(\"Application\",  \"RegenChamberDesigner (Leap71 PicoGK)\")",
            source);
    }

    [Fact]
    public void AnalyticalPreviewMesh_DefaultStlHeaderTag_IsRegenChamberDesignerLiteral()
    {
        // BuildAndWrite defaults the STL header tag to
        // "RegenChamberDesigner analytical preview" when no headerTag is
        // supplied. STL viewers that key off this string would silently
        // break if a future cleanup drops the literal. Pin via source
        // search (the public API uses a default-string parameter so
        // reflection can't trivially read it).
        string sourcePath = TestPaths.RepoRelative(
            "Voxelforge.Core/Geometry/AnalyticalPreviewMesh.cs");
        Assert.True(File.Exists(sourcePath),
            $"AnalyticalPreviewMesh.cs not found at {sourcePath}");
        string source = File.ReadAllText(sourcePath);
        Assert.Contains(
            "\"RegenChamberDesigner analytical preview\"",
            source);
    }

    [Fact]
    public void ChamberAxialTileBuilder_StlHeaderTag_IsRegenChamberDesignerLiteral()
    {
        // ChamberAxialTileBuilder emits a similar STL header tag for
        // tiled-STL outputs. Same preservation rationale as
        // AnalyticalPreviewMesh — keep the literal so external tooling
        // that parses STL header strings keeps working.
        string sourcePath = TestPaths.RepoRelative(
            "Voxelforge.Voxels/Geometry/ChamberAxialTileBuilder.cs");
        Assert.True(File.Exists(sourcePath),
            $"ChamberAxialTileBuilder.cs not found at {sourcePath}");
        string source = File.ReadAllText(sourcePath);
        Assert.Contains(
            "\"RegenChamberDesigner tiled STL",
            source);
    }

    /// <summary>
    /// Test helper — resolve a repo-relative path from the test bin dir.
    /// The test runner's cwd is typically `.../bin/Debug/net9.0-windows/`,
    /// so we walk up until we hit `voxelforge.sln`. Robust to running
    /// from `dotnet test` or from an IDE.
    /// </summary>
    private static class TestPaths
    {
        public static string RepoRelative(string relPath)
        {
            string? dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "voxelforge.sln")))
                    return Path.Combine(dir, relPath);
                dir = Directory.GetParent(dir)?.FullName;
            }
            return relPath;
        }
    }
}
