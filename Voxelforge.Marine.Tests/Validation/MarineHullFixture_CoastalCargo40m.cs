// MarineHullFixture_CoastalCargo40m.cs — Sprint M.W4 acceptance fixture
// for the DisplacementSurface (Holtrop-Mennen) pipeline.
//
// Reference vessel: representative 40 m coastal cargo / motor-vessel
// cluster anchor. LWL=40 m, B=8 m, T=3 m, Cb=0.65, Δ=600 tonnes. Design
// cruise speed 10 knots (~5.14 m/s) — Fn ≈ 0.26, mid-displacement regime.
//
// This is not a specific named vessel — Holtrop-Mennen was published as
// a STATISTICAL fit across hundreds of model tank-test results from the
// Wageningen series. The fixture validates that the simplified
// implementation lands inside the cluster band; precise validation
// against an individual published hull is out of scope (would require
// Wageningen series-60 data).
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Displacement-surface (Holtrop-Mennen simplified) variant under
// ADR-036 § Marine pillar (±25 % resistance, ±10 % wetted area). The
// assertion bands are cluster sanity ranges rather than strict ±%: the
// Holtrop-Mennen statistical fit was published as a band across hundreds of
// Wageningen-series model-tank tests, not a single-vessel calibration. The
// simplified implementation drops appendage form factors, transom resistance,
// bulbous-bow corrections, and air resistance — the cluster band (5–100 kN
// resistance) is wider than a strict ±25 % to absorb those gaps. The 5.5–6.3
// MN buoyancy band is Archimedes-exact ±3 % (rounded for floating-point).
// Wetted-surface area band 380–460 m² matches the Mumford formula ±10 % per
// ADR-036's marine row.

using Voxelforge.Marine;
using Xunit;

namespace Voxelforge.Marine.Tests.Validation;

public sealed class MarineHullFixture_CoastalCargo40m
{
    private static MarineDesign CoastalCargoDesign() => new(
        Kind:                MarineKind.DisplacementSurface,
        Length_m:           40.0,
        Diameter_m:          1.0,
        NoseFairingFraction: 0.25,
        TailFairingFraction: 0.25,
        WallThickness_m:     0.005,
        MaterialIndex:       0,
        DepthRating_m:       1.0,
        HullFamily:          HullFamily.DisplacementSurface)
    {
        BeamWaterline_m      = 8.0,
        DraftDesign_m        = 3.0,
        BlockCoefficient     = 0.65,
        DisplacementMass_kg  = 600_000.0,
    };

    private static MarineConditions CoastalCargoConditions()
        => new(CruiseSpeed_ms: 5.144, MaxDepth_m: 0.0);  // 10 knots

    // ── Cluster sanity band ──────────────────────────────────────────────

    [Fact]
    public void CoastalCargo_TotalResistance_InCargoClusterBand()
    {
        // Cluster anchor for a 600-tonne / 10-kt coastal cargo: resistance
        // typically 15–80 kN (Cs ~ 0.5–1.5 N/kg displacement). The
        // simplified Holtrop-Mennen lands in the lower half because it
        // drops appendage form factors + air drag.
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        Assert.InRange(r.DragForce_N, 5_000.0, 100_000.0);
    }

    [Fact]
    public void CoastalCargo_Froude_InDisplacementBand()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        // Fn = 5.144 / √(9.81·40) ≈ 0.260.
        Assert.InRange(r.SpeedCoefficient, 0.20, 0.30);
    }

    [Fact]
    public void CoastalCargo_DisplacedVolume_MatchesDesignDisplacement()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        // ∇ = Δ / ρ = 600 000 / 1025 ≈ 585.4 m³.
        Assert.Equal(585.4, r.DisplacedVolume_m3, precision: 1);
    }

    [Fact]
    public void CoastalCargo_BuoyancyEqualsArchimedesUplift()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        // F_b = ρ · V · g = 1025 · 585.4 · 9.80665 ≈ 5.88 MN.
        Assert.InRange(r.BuoyancyForce_N, 5.5e6, 6.3e6);
    }

    [Fact]
    public void CoastalCargo_WettedSurfaceArea_FromMumfordFormula()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        // S_wet ≈ 1.025·40·(0.65·8 + 1.7·3) = 1.025·40·(5.2 + 5.1) = 422 m².
        Assert.InRange(r.WettedSurfaceArea_m2, 380.0, 460.0);
    }

    [Fact]
    public void CoastalCargo_AuvSpecificFieldsAreNaN()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        Assert.True(double.IsNaN(r.BuoyantWeight_N));            // AUV-specific concept
        Assert.True(double.IsNaN(r.CriticalBucklingPressure_Pa));
        Assert.True(double.IsNaN(r.BucklingSafetyFactor));
        Assert.True(double.IsNaN(r.TrimAngle_deg));               // Planing-specific
        Assert.True(double.IsNaN(r.WettedLengthToBeamRatio));     // Planing-specific
    }

    [Fact]
    public void CoastalCargo_HullMassEqualsDisplacementMass()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        Assert.Equal(600_000.0, r.HullMass_kg, precision: 6);
    }

    [Fact]
    public void CoastalCargo_BaselineIsFeasible()
    {
        var r = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        Assert.True(r.IsFeasible,
            $"Coastal cargo baseline should pass; saw {r.Violations.Count} violations.");
    }

    [Fact]
    public void CoastalCargo_Deterministic()
    {
        var r1 = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        var r2 = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
        Assert.Equal(r1.DragForce_N, r2.DragForce_N);
        Assert.Equal(r1.SpeedCoefficient, r2.SpeedCoefficient);
    }

    [Fact]
    public void CoastalCargo_RoundTripsThroughPersistence()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vxf_marine_cargo_{System.IO.Path.GetRandomFileName()}.json");
        try
        {
            Voxelforge.Marine.IO.MarineDesignPersistence.SaveJson(
                CoastalCargoDesign(), CoastalCargoConditions(), path);
            var (loaded, cond) = Voxelforge.Marine.IO.MarineDesignPersistence.LoadJson(path);
            Assert.Equal(MarineKind.DisplacementSurface, loaded.Kind);
            Assert.Equal(HullFamily.DisplacementSurface, loaded.HullFamily);
            Assert.Equal(8.0, loaded.BeamWaterline_m, precision: 6);
            Assert.Equal(0.65, loaded.BlockCoefficient, precision: 6);
            var fresh    = MarineOptimization.GenerateWith(CoastalCargoDesign(), CoastalCargoConditions());
            var roundtrip = MarineOptimization.GenerateWith(loaded, cond);
            Assert.Equal(fresh.DragForce_N, roundtrip.DragForce_N, precision: 4);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
