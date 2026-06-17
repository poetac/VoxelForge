// Sprint render (2026-04-25) — RenderArgs CLI parsing tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Voxelforge.Renderer;
using Xunit;

namespace Voxelforge.Tests;

public class RenderArgsTests
{
    [Fact]
    public void Parse_RequiresIn()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--out", "x.png" }));
        Assert.Contains("--in", ex.Message);
    }

    [Fact]
    public void Parse_RequiresOut()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x.stl" }));
        Assert.Contains("--out", ex.Message);
    }

    [Fact]
    public void Parse_HappyPath_AllDefaults()
    {
        var a = RenderArgs.Parse(new[] { "--in", "in.stl", "--out", "out.png" });
        Assert.Equal("in.stl", a.InputStl);
        Assert.Equal("out.png", a.OutputPath);
        Assert.Equal("Still", a.Mode.ToString());
        Assert.Equal("copper", a.Material);
        Assert.Equal("High", a.Resolution.ToString());
        Assert.Equal(RenderArgs.DefaultFrames, a.Frames);
        Assert.Null(a.BlenderPathOverride);
    }

    [Fact]
    public void Parse_AllOptionsExplicit()
    {
        var a = RenderArgs.Parse(new[]
        {
            "--in", "in.stl", "--out", "out.png",
            "--mode", "turntable",
            "--material", "inconel",
            "--resolution", "maximum",
            "--frames", "32",
            "--blender-path", "C:/blender.exe",
        });
        Assert.Equal("Turntable", a.Mode.ToString());
        Assert.Equal("inconel", a.Material);
        Assert.Equal("Maximum", a.Resolution.ToString());
        Assert.Equal(32, a.Frames);
        Assert.Equal("C:/blender.exe", a.BlenderPathOverride);
    }

    [Theory]
    [InlineData("low", "Low")]
    [InlineData("LOW", "Low")]
    [InlineData("high", "High")]
    [InlineData("maximum", "Maximum")]
    [InlineData("max", "Maximum")]
    public void Parse_ResolutionAliases(string s, string expectedName)
    {
        var a = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", s });
        Assert.Equal(expectedName, a.Resolution.ToString());
    }

    [Theory]
    [InlineData("still", "Still")]
    [InlineData("STILL", "Still")]
    [InlineData("turntable", "Turntable")]
    public void Parse_ModeAliases(string s, string expectedName)
    {
        var a = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--mode", s });
        Assert.Equal(expectedName, a.Mode.ToString());
    }

    [Fact]
    public void Parse_RejectsUnknownArg()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--bogus" }));
        Assert.Contains("unknown argument", ex.Message);
    }

    [Fact]
    public void Parse_RejectsBadResolution()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "ultra" }));
        Assert.Contains("--resolution", ex.Message);
    }

    [Fact]
    public void Parse_RejectsBadMode()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--mode", "panorama" }));
    }

    [Fact]
    public void Parse_RejectsNonPositiveFrames()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--frames", "0" }));
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--frames", "-3" }));
    }

    [Fact]
    public void Parse_RequiresValueAfterFlag()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out" }));   // --out missing value
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--material" }));
    }

    [Fact]
    public void Parse_HelpThrowsWithUsage()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--help" }));
        Assert.Contains("Usage:", ex.Message);
    }

    [Fact]
    public void ResolutionPresets_LowUsesEevee_HighAndMaxUseCycles()
    {
        var low  = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "low"     }).Resolution;
        var high = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "high"    }).Resolution;
        var max  = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "maximum" }).Resolution;
        Assert.Equal("BLENDER_EEVEE_NEXT", ResolutionPresets.Spec(low).Engine);
        Assert.Equal("CYCLES",             ResolutionPresets.Spec(high).Engine);
        Assert.Equal("CYCLES",             ResolutionPresets.Spec(max).Engine);
    }

    [Fact]
    public void ResolutionPresets_SamplesScaleUpWithQuality()
    {
        var lowRes  = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "low"     }).Resolution;
        var highRes = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "high"    }).Resolution;
        var maxRes  = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--resolution", "maximum" }).Resolution;
        var low  = ResolutionPresets.Spec(lowRes).Samples;
        var high = ResolutionPresets.Spec(highRes).Samples;
        var max  = ResolutionPresets.Spec(maxRes).Samples;
        Assert.True(low < high);
        Assert.True(high < max);
    }

    // ── Material JSON schema validation ────────────────────────────────────

    private static string MaterialsDir =>
        Path.Combine(AppContext.BaseDirectory, "materials");

    [Fact]
    public void MaterialSpec_AllRequiredPbrFieldsPresent()
    {
        var dir = MaterialsDir;
        if (!Directory.Exists(dir)) return; // materials not copied yet — build Renderer first

        foreach (var path in Directory.GetFiles(dir, "*.json"))
        {
            string slug = Path.GetFileNameWithoutExtension(path);
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("name",       out _), $"{slug}.json missing 'name'");
            Assert.True(root.TryGetProperty("base_color", out var bc), $"{slug}.json missing 'base_color'");
            Assert.True(root.TryGetProperty("metallic",   out _), $"{slug}.json missing 'metallic'");
            Assert.True(root.TryGetProperty("roughness",  out _), $"{slug}.json missing 'roughness'");

            Assert.Equal(JsonValueKind.Array, bc.ValueKind);
            Assert.Equal(4, bc.GetArrayLength());
        }
    }

    [Fact]
    public void MaterialSpec_AllFourCanonicalMaterialsPresent()
    {
        var dir = MaterialsDir;
        if (!Directory.Exists(dir)) return; // materials not copied yet — build Renderer first

        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(dir, "*.json"))
            slugs.Add(Path.GetFileNameWithoutExtension(f));

        foreach (var expected in new[] { "copper", "inconel", "titanium", "stainless" })
            Assert.Contains(expected, slugs);
    }

    // ── --material slug validation (audit 01-security.md M1) ───────────────
    //
    // The renderer concatenates --material into a file path without
    // Path.Combine normalising '..' segments, so an unvalidated value
    // could read arbitrary .json files on disk. Validate at the parse
    // site so all downstream code can assume the slug is safe.

    [Theory]
    [InlineData("copper")]
    [InlineData("lava")]
    [InlineData("my-material")]
    [InlineData("matte_metal")]
    [InlineData("Material1")]
    [InlineData("INCONEL")]
    public void Parse_MaterialSlug_AcceptsAlphanumericDashUnderscore(string slug)
    {
        var a = RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--material", slug });
        Assert.Equal(slug, a.Material);
    }

    [Theory]
    [InlineData("../etc/passwd")]   // path-traversal
    [InlineData("/absolute/path")]  // POSIX absolute
    [InlineData("C:/abs/path")]     // Windows absolute + drive-letter colon
    [InlineData(@"..\..\secret")]   // Windows-style traversal
    [InlineData("a/b")]             // any forward slash
    [InlineData(@"a\b")]            // any backslash
    [InlineData("..")]              // parent-dir token alone
    [InlineData("a.b")]             // dot rejected (would allow `..` adjacency tricks)
    [InlineData("a b")]             // whitespace
    [InlineData("")]                // empty
    public void Parse_MaterialSlug_RejectsPathTraversalAndDisallowedChars(string slug)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--material", slug }));
        Assert.Equal("material", ex.ParamName);
    }

    [Fact]
    public void Parse_MaterialSlug_RejectsTabAndNewline()
    {
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--material", "a\tb" }));
        Assert.Throws<ArgumentException>(() =>
            RenderArgs.Parse(new[] { "--in", "x", "--out", "y", "--material", "a\nb" }));
    }
}
