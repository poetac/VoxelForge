// AirbreathingSchemaVersionTests.cs — coverage for the internal schema-
// version constants. Audit 05-test-gaps.md Section 2 Low.
//
// AirbreathingSchemaVersion is internal — the test project consumes it
// via the existing InternalsVisibleTo grant.

using Voxelforge.Airbreathing.IO;

namespace Voxelforge.Airbreathing.Tests.IO;

public sealed class AirbreathingSchemaVersionTests
{
    [Fact]
    public void Current_IsV12()
    {
        // Sprint A.W4 lifted Current to v12 (RDE numeric fields). The
        // explicit string-pin here makes a forward migration impossible
        // to ship silently without updating the test.
        Assert.Equal("v12", AirbreathingSchemaVersion.Current);
    }

    [Fact]
    public void Known_IsMonotoneVersionList()
    {
        // The Known list MUST contain Current.
        Assert.Contains(AirbreathingSchemaVersion.Current, AirbreathingSchemaVersion.Known);
    }

    [Fact]
    public void Known_HasNoDuplicates()
    {
        var set = new System.Collections.Generic.HashSet<string>(AirbreathingSchemaVersion.Known);
        Assert.Equal(AirbreathingSchemaVersion.Known.Length, set.Count);
    }

    [Fact]
    public void IsSupported_ReturnsTrueForCurrent()
    {
        Assert.True(AirbreathingSchemaVersion.IsSupported(AirbreathingSchemaVersion.Current));
    }

    [Fact]
    public void IsSupported_ReturnsTrueForAllKnown()
    {
        foreach (var v in AirbreathingSchemaVersion.Known)
            Assert.True(AirbreathingSchemaVersion.IsSupported(v), $"Version {v} should be supported.");
    }

    [Fact]
    public void IsSupported_ReturnsFalseForUnknown()
    {
        Assert.False(AirbreathingSchemaVersion.IsSupported("v999"));
        Assert.False(AirbreathingSchemaVersion.IsSupported("nonsense"));
        Assert.False(AirbreathingSchemaVersion.IsSupported(""));
    }
}
