// NuclearSchemaTests.cs — schema v1 save/load round-trip and completeness guard.

using System.IO;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.IO;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearSchemaTests
{
    private static NuclearThermalDesign MakeDesign() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions MakeCond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    [Fact]
    public void SaveLoad_RoundTrip_PreservesAllScalarFields()
    {
        var design = MakeDesign();
        var cond   = MakeCond();
        var tmp    = Path.GetTempFileName();
        try
        {
            NuclearDesignPersistence.SaveJson(design, cond, tmp);
            var (loaded, loadedCond) = NuclearDesignPersistence.LoadJson(tmp);

            Assert.Equal(design.Kind,                    loaded.Kind);
            Assert.Equal(design.ReactorThermalPower_MW,  loaded.ReactorThermalPower_MW);
            Assert.Equal(design.ReactorCoreLength_mm,    loaded.ReactorCoreLength_mm);
            Assert.Equal(design.ReactorCoreDiameter_mm,  loaded.ReactorCoreDiameter_mm);
            Assert.Equal(design.FuelLoadingFraction,     loaded.FuelLoadingFraction);
            Assert.Equal(design.PropellantMassFlow_kgs,  loaded.PropellantMassFlow_kgs);
            Assert.Equal(design.ChamberPressure_bar,     loaded.ChamberPressure_bar);
            Assert.Equal(design.ThroatRadius_mm,         loaded.ThroatRadius_mm);
            Assert.Equal(design.ExpansionRatio,          loaded.ExpansionRatio);
            Assert.Equal(design.NozzleLength_mm,         loaded.NozzleLength_mm);
            Assert.Equal(design.RegenChannelDepth_mm,    loaded.RegenChannelDepth_mm);
            Assert.Equal(design.RegenChannelCount,       loaded.RegenChannelCount);
            Assert.Equal(design.NozzleWallThickness_mm,  loaded.NozzleWallThickness_mm);
            Assert.Equal(design.NozzleChannelWidth_mm,   loaded.NozzleChannelWidth_mm);
            Assert.Equal(design.NozzleManifoldDepth_mm,  loaded.NozzleManifoldDepth_mm);

            Assert.Equal(cond.PropellantInletTemp_K, loadedCond.PropellantInletTemp_K);
            Assert.Equal(cond.TargetDeltaV_ms,       loadedCond.TargetDeltaV_ms);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void SaveLoad_ProducesCurrentSchemaTag()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            NuclearDesignPersistence.SaveJson(MakeDesign(), MakeCond(), tmp);
            var json = File.ReadAllText(tmp);
            // Bumped to v3 after Sprint NU.W3 (Bimodal NTR + Brayton).
            // Test asserts the current schema tag is present rather than a
            // hard-coded version so future bumps don't re-stale the test.
            Assert.Contains($"\"{NuclearDesignPersistence.CurrentSchemaVersion}\"", json);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void LoadJson_UnsupportedSchema_Throws()
    {
        const string futureJson = """
            {
              "Schema": "v99",
              "Version": "1.0",
              "AppName": "Voxelforge.Nuclear",
              "Conditions": { "PropellantInletTemp_K": 80.0, "TargetDeltaV_ms": 3000.0 },
              "Design": {}
            }
            """;
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, futureJson);
            Assert.Throws<UnsupportedNuclearSchemaException>(
                () => NuclearDesignPersistence.LoadJson(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CurrentSchemaVersion_IsV5()
    {
        // v1 — Wave-1 NERVA scaffold.
        // v2 — Sprint NU.W2 fuel-pin heat-conduction model (identity migration).
        // v3 — Sprint NU.W3 bimodal NTR + Brayton (identity migration).
        // v4 — Sprint NU.W4 fuel material variants (identity migration; None
        //      maps to UO₂-cermet anchors for Wave-1/W2/W3 backwards compat).
        // v5 — Sprint NU.W5 uranium enrichment tiers (identity migration; None
        //      maps to HEU for Wave-1/W2/W3/W4 backwards compat).
        Assert.Equal("v5", NuclearDesignPersistence.CurrentSchemaVersion);
    }
}
