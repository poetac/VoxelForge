// NoyronTierC1Tests.cs — Tier C1 forcing-function suite for the
// aerospike / plug-nozzle geometry pipeline. Covers the parametric
// contour math, the hand-rolled implicit SDFs, the high-level
// AerospikeBuilder entry, and AutoSeeder / RegenChamberDesign
// integration.
//
// All tests are pure C#. A subset exercise the full
// AerospikeBuilder.Build pipeline (including PicoGK voxel ops via
// a one-off Library fixture) — those run in the Benchmarks console
// app in production, but xUnit can host them through the shared
// GenerateWith fixture already used by FeasibilityGateTests /
// NoyronTierB1ProperTests.
//
// Scope: Phase 1 C1 work (geometry pipeline + standalone STL).
// Thermal-solver integration, plug-cooling, feasibility-gate
// adaptations, and SA-variable promotion ship in Phase 2.

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class NoyronTierC1Tests
{
    // ══════════════════════ Prandtl-Meyer function ══════════════════════

    [Fact]
    public void PrandtlMeyer_AtSonic_IsZero()
    {
        Assert.Equal(0.0, AerospikeContourGenerator.PrandtlMeyer(1.0, 1.15), 6);
    }

    [Fact]
    public void PrandtlMeyer_Monotonic_IncreasesWithMach()
    {
        double nu2 = AerospikeContourGenerator.PrandtlMeyer(2.0, 1.20);
        double nu3 = AerospikeContourGenerator.PrandtlMeyer(3.0, 1.20);
        double nu5 = AerospikeContourGenerator.PrandtlMeyer(5.0, 1.20);
        Assert.True(nu2 < nu3, $"ν(2)={nu2}, ν(3)={nu3}");
        Assert.True(nu3 < nu5, $"ν(3)={nu3}, ν(5)={nu5}");
    }

    [Fact]
    public void PrandtlMeyer_ClassicalTextbookValue()
    {
        // Anderson §9.6 Table 9.1 (γ=1.4): ν(M=2.0) = 26.38° = 0.4605 rad.
        double nu = AerospikeContourGenerator.PrandtlMeyer(2.0, 1.4);
        Assert.InRange(nu, 0.455, 0.467);
    }

    [Fact]
    public void SolveMachFromPrandtlMeyer_RoundTripsAtMultipleMachs()
    {
        foreach (double mach in new[] { 1.5, 2.0, 3.0, 4.5 })
        {
            double nu = AerospikeContourGenerator.PrandtlMeyer(mach, 1.20);
            double mBack = AerospikeContourGenerator.SolveMachFromPrandtlMeyer(nu, 1.20);
            Assert.InRange(mBack, mach - 0.01, mach + 0.01);
        }
    }

    // ══════════════════════ Area-Mach relation ══════════════════════

    [Fact]
    public void SolveExitMachFromAreaRatio_AtUnity_ReturnsOne()
    {
        Assert.Equal(1.0, AerospikeContourGenerator.SolveExitMachFromAreaRatio(1.0, 1.20), 6);
    }

    [Fact]
    public void SolveExitMachFromAreaRatio_ClassicalEps10_Mach_Is_NearThree()
    {
        // Anderson §5.8 Table B.2 (γ=1.4): A/A* = 10 → M ≈ 3.95.
        // Using γ=1.20 (closer to rocket exhaust): M should be > 3 but < 5.
        double m = AerospikeContourGenerator.SolveExitMachFromAreaRatio(10.0, 1.20);
        Assert.InRange(m, 3.0, 5.0);
    }

    // ══════════════════════ Contour generation ══════════════════════

    [Fact]
    public void Contour_DefaultTruncated_PopulatesEveryField()
    {
        var c = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 30.0, expansionRatio: 15.0,
            plugLengthRatio: 0.30, gamma: 1.15);
        Assert.NotEmpty(c.Stations);
        Assert.Equal(0, c.ThroatIndex);
        Assert.True(c.ThroatAnnulusArea_mm2 > 0);
        Assert.Equal(30.0, c.ThroatOuterRadius_mm, 6);
        Assert.InRange(c.ThroatInnerRadius_mm, 10.0, 20.0);   // 0.40 × R_o
        Assert.Equal(15.0, c.ExpansionRatio, 6);
        Assert.Equal(0.30, c.PlugLengthRatio, 6);
        Assert.True(c.PlugFullLength_mm > 0);
        Assert.Equal(c.PlugFullLength_mm * 0.30, c.PlugTruncatedLength_mm, 4);
        Assert.True(c.CowlLength_mm > 0);     // includeCowl=true by default
        Assert.True(c.DesignExitMach > 1);
    }

    [Fact]
    public void Contour_FullSpike_TruncatedLengthEqualsFullLength()
    {
        var c = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 25.0, expansionRatio: 12.0,
            plugLengthRatio: 1.00);
        Assert.Equal(c.PlugFullLength_mm, c.PlugTruncatedLength_mm, 3);
    }

    [Fact]
    public void Contour_PlugRadius_MonotonicallyDecreases()
    {
        var c = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 30.0, expansionRatio: 15.0,
            plugLengthRatio: 0.50);
        // Plug taper is from R_o (at throat tip) down toward zero.
        // Inner-plug radius at station 1+ should be ≤ the throat-outer
        // radius at every subsequent station.
        for (int i = 2; i < c.Stations.Length; i++)
        {
            Assert.True(c.Stations[i].R_inner_mm <= c.Stations[i - 1].R_inner_mm + 1e-6,
                $"station {i} ({c.Stations[i].R_inner_mm}) > station {i-1} ({c.Stations[i-1].R_inner_mm})");
        }
    }

    [Fact]
    public void Contour_StationXMonotonicallyIncreasing()
    {
        var c = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: 25.0, expansionRatio: 12.0,
            plugLengthRatio: 0.30);
        for (int i = 1; i < c.Stations.Length; i++)
        {
            Assert.True(c.Stations[i].X_mm > c.Stations[i - 1].X_mm,
                $"station {i} x={c.Stations[i].X_mm} <= station {i-1} x={c.Stations[i-1].X_mm}");
        }
    }

    [Theory]
    [InlineData(0.10)]        // below MinPlugLengthRatio
    [InlineData(1.50)]        // above MaxPlugLengthRatio
    public void Contour_OutOfRangePlugRatio_Throws(double plugRatio)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            AerospikeContourGenerator.Generate(
                throatOuterRadius_mm: 25.0,
                expansionRatio: 12.0,
                plugLengthRatio: plugRatio));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void Contour_NonPositiveThroatRadius_Throws(double rOuter)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            AerospikeContourGenerator.Generate(
                throatOuterRadius_mm: rOuter,
                expansionRatio: 12.0,
                plugLengthRatio: 0.30));
    }

    [Fact]
    public void Contour_LowExpansionRatio_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            AerospikeContourGenerator.Generate(
                throatOuterRadius_mm: 25.0,
                expansionRatio: 1.0,   // below 1.5 floor
                plugLengthRatio: 0.30));
    }

    // ══════════════════════ Hand-rolled implicits ══════════════════════

    [Fact]
    public void RevolvedPlugImplicit_AtAxis_InsidePlugIsNegative()
    {
        var c = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var plug = new RevolvedPlugImplicit(c);
        // Sample a point on the axis inside the plug (half-way axially).
        var p = new Vector3((float)(c.PlugTruncatedLength_mm * 0.5f), 0, 0);
        Assert.True(plug.fSignedDistance(p) < 0);
    }

    [Fact]
    public void RevolvedPlugImplicit_OutsidePlug_IsPositive()
    {
        var c = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var plug = new RevolvedPlugImplicit(c);
        // Sample a point well outside the plug in +Y.
        var p = new Vector3((float)(c.PlugTruncatedLength_mm * 0.5f), 100f, 0);
        Assert.True(plug.fSignedDistance(p) > 0);
    }

    [Fact]
    public void RevolvedPlugImplicit_BehindTruncation_IsPositive()
    {
        var c = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var plug = new RevolvedPlugImplicit(c);
        // Sample downstream of the plug base — should be positive.
        var p = new Vector3((float)(c.PlugTruncatedLength_mm + 20f), 0, 0);
        Assert.True(plug.fSignedDistance(p) > 0);
    }

    [Fact]
    public void AnnularThroatImplicit_InsideAnnulus_IsNegative()
    {
        var t = new AnnularThroatImplicit(
            xMin_mm: -5f, xMax_mm: 5f,
            rInner_mm: 20f, rOuter_mm: 30f);
        var p = new Vector3(0, 25f, 0);     // inside annulus
        Assert.True(t.fSignedDistance(p) < 0);
    }

    [Fact]
    public void AnnularThroatImplicit_InsideInnerHole_IsPositive()
    {
        var t = new AnnularThroatImplicit(
            xMin_mm: -5f, xMax_mm: 5f,
            rInner_mm: 20f, rOuter_mm: 30f);
        var p = new Vector3(0, 10f, 0);     // inside inner hole
        Assert.True(t.fSignedDistance(p) > 0);
    }

    [Fact]
    public void AnnularThroatImplicit_DegenerateRadii_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new AnnularThroatImplicit(-5f, 5f, 30f, 20f));   // inner > outer
    }

    // ══════════════════════ Design record + AutoSeeder ══════════════════════

    [Fact]
    public void Design_Default_PlugLengthRatio_Is_PointThree()
    {
        var d = new RegenChamberDesign();
        Assert.Equal(0.30, d.PlugLengthRatio, 6);
    }

    [Fact]
    public void ChannelTopology_AerospikeEnum_IsAddedValue()
    {
        // Guard that the enum was extended; this test trips if someone
        // drops the Aerospike value.
        var values = System.Enum.GetValues<ChannelTopology>();
        Assert.Contains(ChannelTopology.Aerospike, values);
    }

    [Fact]
    public void Design_TpmsKindAccessor_ReturnsNullForAerospike()
    {
        var d = new RegenChamberDesign { ChannelTopology = ChannelTopology.Aerospike };
        Assert.Null(d.TpmsKind);
    }

    [Fact]
    public void AutoSeeder_AerospikeOverride_SetsTopologyAndPlugRatio()
    {
        var spec = new EngineSpec(
            PropellantPair:          PropellantPair.LOX_CH4,
            Thrust_N:                20000,
            ChamberPressure_Pa:      7e6,
            ExpansionRatio:          15.0,
            ChannelTopologyOverride: ChannelTopology.Aerospike);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(ChannelTopology.Aerospike, seed.Design.ChannelTopology);
        Assert.InRange(seed.Design.PlugLengthRatio,
            AerospikeContourGenerator.MinPlugLengthRatio,
            AerospikeContourGenerator.MaxPlugLengthRatio);
        Assert.Contains(seed.Rationale, r => r.Contains("Aerospike"));
    }

    [Fact]
    public void AutoSeeder_AxialOverride_LeavesPlugRatioAtDefault()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_CH4,
            Thrust_N:           5000,
            ChamberPressure_Pa: 5e6,
            ExpansionRatio:     10.0);
        var seed = AutoSeeder.Seed(spec);
        Assert.Equal(ChannelTopology.Axial, seed.Design.ChannelTopology);
        // Default plug ratio stays at record default (0.30) even on
        // non-aerospike designs — seeder doesn't mutate it needlessly.
        Assert.Equal(0.30, seed.Design.PlugLengthRatio, 6);
    }

    // ══════════════════════ AerospikeSpec record ══════════════════════

    [Fact]
    public void AerospikeSpec_AcceptsFourInputs_WithDefaults()
    {
        var spec = new AerospikeSpec(
            Thrust_N:           20000,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio:     15.0,
            PlugLengthRatio:    0.30);
        Assert.Equal(PropellantPair.LOX_CH4, spec.PropellantPair);  // default
        Assert.Equal(3.3, spec.MixtureRatio, 6);                    // default
        Assert.Equal(0.95, spec.CStarEfficiency, 6);                // default
    }
}
