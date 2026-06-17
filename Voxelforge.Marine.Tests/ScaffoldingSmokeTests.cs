// ScaffoldingSmokeTests.cs — Sprint M.0 acceptance criteria.
//
// Verifies:
//   (a) Voxelforge.Marine.Core builds (implicit — these tests run).
//   (b) EngineFamilies.Marine == "marine".
//   (c) MarineEngine.Instance.Family == "marine".
//   (d) No cross-pillar import leaks (verified by VFA001 Roslyn rule
//       in the CI build; tested here structurally by checking Family
//       string rather than by importing forbidden namespaces).
//   (e) MarineDesign.Family propagates through the engine.
//   (f) MarineConditions.Family == "marine".

using System;
using Voxelforge.Engines;
using Voxelforge.Marine;
using Voxelforge.Marine.Engines;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class ScaffoldingSmokeTests
{
    [Fact]
    public void EngineFamilies_Marine_IsLiteralStringMarine()
    {
        Assert.Equal("marine", EngineFamilies.Marine);
    }

    [Fact]
    public void MarineEngine_Instance_IsNotNull()
    {
        Assert.NotNull(MarineEngine.Instance);
    }

    [Fact]
    public void MarineEngine_Family_IsMarine()
    {
        Assert.Equal(EngineFamilies.Marine, MarineEngine.Instance.Family);
    }

    [Fact]
    public void MarineDesign_Family_IsMarine()
    {
        var design = MakeRemus100Design();
        Assert.Equal(EngineFamilies.Marine, design.Family);
    }

    [Fact]
    public void MarineConditions_Family_IsMarine()
    {
        var cond = MakeRemus100Conditions();
        Assert.Equal(EngineFamilies.Marine, cond.Family);
    }

    [Fact]
    public void MarineDesign_ComputedProperties_AreConsistent()
    {
        var design = MakeRemus100Design();
        Assert.Equal(design.Length_m * design.NoseFairingFraction, design.NoseLength_m, precision: 10);
        Assert.Equal(design.Length_m * design.TailFairingFraction, design.TailLength_m, precision: 10);
        Assert.Equal(
            design.Length_m - design.NoseLength_m - design.TailLength_m,
            design.MidBodyLength_m,
            precision: 10);
        Assert.Equal(design.Length_m / design.Diameter_m, design.FinenessRatio, precision: 10);
    }

    [Fact]
    public void MarineConditions_WaterDensity_IsInReasonableRange()
    {
        var cond = MakeRemus100Conditions();
        // Ocean seawater at 4°C, 35 ppt salinity ≈ 1027 kg/m³
        Assert.InRange(cond.WaterDensity_kgm3, 1020, 1040);
    }

    [Fact]
    public void MarineConditions_HydrostaticPressure_MatchesDepth()
    {
        var cond = MakeRemus100Conditions();
        // P ≈ ρ × g × h = 1027 × 9.80665 × 100 ≈ 1.007 MPa
        double expectedMin = 1000 * 9.80665 * 100;
        double expectedMax = 1040 * 9.80665 * 100;
        Assert.InRange(cond.HydrostaticPressure_Pa, expectedMin, expectedMax);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static MarineDesign MakeRemus100Design() => new(
        Kind:                MarineKind.AuvMidBody,
        Length_m:            1.595,
        Diameter_m:          0.190,
        NoseFairingFraction: 0.18,
        TailFairingFraction: 0.22,
        WallThickness_m:     0.005,   // 5 mm — SF ≈ 2.8 at 100 m (4 mm gives SF=1.40 < 1.5 hard gate)
        MaterialIndex:       1,        // Al-6061
        DepthRating_m:       100.0);

    internal static MarineConditions MakeRemus100Conditions() => new(
        CruiseSpeed_ms: 1.5,
        MaxDepth_m:     100.0);
}
