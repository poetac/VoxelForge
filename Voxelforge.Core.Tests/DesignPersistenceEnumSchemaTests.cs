// DesignPersistenceEnumSchemaTests.cs — Issue #81: DesignPersistence stored
// enums as raw numeric ordinals, not names, contradicting the v24->v25
// migration comment's claim that string serialisation already existed
// (it didn't -- no JsonStringEnumConverter was ever registered). Schema
// bumped to v32, which registers the converter on both Save and Load.
//
// JsonStringEnumConverter's default allowIntegerValues=true means this is
// purely additive for reads: v31-and-earlier files (numeric-encoded enums)
// still deserialize correctly, while every v32+ save is immune to a future
// enum-member insertion/reorder silently remapping an old numeric value to
// a different member. These tests pin both directions directly.

using System;
using System.IO;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class DesignPersistenceEnumSchemaTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 1,
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    private static RegenChamberDesign HelicalDesign() => new()
    {
        IncludeManifolds      = false,
        IncludePorts          = false,
        IncludeInjectorFlange = false,
        ContourStationCount   = 60,
        ChannelTopology       = ChannelTopology.Helical,
    };

    [Fact]
    public void CurrentSchemaVersion_IsV32()
    {
        Assert.Equal("v32", DesignPersistence.CurrentSchemaVersion);
        Assert.Contains("v32", DesignPersistence.KnownSchemas);
    }

    [Fact]
    public void Save_WritesEnumFieldsAsNames_NotNumericOrdinals()
    {
        string path = Path.Combine(Path.GetTempPath(), $"designpersist-enumstr-{Guid.NewGuid():N}.json");
        try
        {
            DesignPersistence.Save(path, DefaultConditions(), HelicalDesign(), r: null);
            string json = File.ReadAllText(path);

            Assert.Contains("\"Helical\"", json);
            Assert.Contains("\"LOX_CH4\"", json);
            // The old bug wrote the enum's raw ordinal instead -- ChannelTopology.Helical = 1,
            // so a standalone "1" value for that field would appear pre-fix. Post-fix the
            // field's value is always the quoted name, never a bare ordinal.
            Assert.DoesNotContain("\"ChannelTopology\": 1", json);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveLoadRoundTrip_PreservesEnumValue()
    {
        string path = Path.Combine(Path.GetTempPath(), $"designpersist-roundtrip-{Guid.NewGuid():N}.json");
        try
        {
            DesignPersistence.Save(path, DefaultConditions(), HelicalDesign(), r: null);
            var loaded = DesignPersistence.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal("v32", loaded!.Schema);
            Assert.Equal(ChannelTopology.Helical, loaded.Design!.ChannelTopology);
            Assert.Equal(PropellantPair.LOX_CH4, loaded.Conditions!.PropellantPair);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_V31NumericEncodedEnum_StillDeserializesCorrectly()
    {
        // Hand-built v31 file with ChannelTopology stored as its raw ordinal
        // (1 = Helical) -- exactly what pre-#81 Save() would have written.
        // JsonStringEnumConverter's allowIntegerValues=true fallback must
        // still parse this correctly under v32.
        const string v31Json = """
            {
              "Schema": "v31",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 2224.0,
                "ChamberPressure_Pa": 6900000.0,
                "MixtureRatio": 3.3,
                "CoolantInletTemp_K": 150.0,
                "CoolantInletPressure_Pa": 12000000.0,
                "WallMaterialIndex": 1,
                "PropellantPair": 0
              },
              "Design": {
                "ChannelTopology": 1,
                "IncludeManifolds": false,
                "IncludePorts": false,
                "IncludeInjectorFlange": false,
                "ContourStationCount": 60
              }
            }
            """;
        string path = Path.Combine(Path.GetTempPath(), $"designpersist-v31numeric-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, v31Json);
            var loaded = DesignPersistence.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal("v32", loaded!.Schema);      // migrated forward
            Assert.Equal(ChannelTopology.Helical, loaded.Design!.ChannelTopology);
            Assert.Equal(PropellantPair.LOX_CH4, loaded.Conditions!.PropellantPair);
        }
        finally { File.Delete(path); }
    }
}
