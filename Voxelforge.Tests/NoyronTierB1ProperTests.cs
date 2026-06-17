// NoyronTierB1ProperTests.cs — Tier B1 proper. Integration tests for
// the TPMS channel-topology wiring that rode the correlation scaffold.
// Covers:
//   • ChannelTopology enum additions (TpmsGyroid / TpmsSchwarzP / TpmsSchwarzD).
//   • RegenChamberDesign.TpmsKind projection.
//   • TpmsCorrelations.StrutThickness_mm + MinStrutThickness_mm constant.
//   • LPBF gate #15 (TPMS_CELL_FEATURE_TOO_SMALL): fires when strut < 2.0 mm,
//     passes when strut ≥ 2.0 mm, does NOT fire for non-TPMS topologies.
//   • Hand-rolled TpmsUnitCellImplicit: sign convention + threshold shift.
//   • TpmsAnnularImplicit: axial-clip, radial-clip, in-band sampling.
//   • AutoSeeder: ChannelTopologyOverride propagates topology +
//     populates TPMS cell/solid-fraction so the LPBF gate passes
//     out of the box; rationale line mentions TPMS.
//   • RegenSolverInputs TpmsKind round-trip (no thermal-march invocation
//     here — PicoGK Library would be required for a full Generate; the
//     struct-level assertions suffice for the input-plumbing path).
//
// Gate-logic tests reuse the FeasibilityGateTests "SafeResult" pattern —
// run the real solver once to get a fully-populated RegenGenerationResult
// cached in `_rawCache`, then `with`-override exactly the fields under
// test. No separate PicoGK Library lifecycle needed beyond that shared
// fixture.

using System.Numerics;
using PicoGK;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class NoyronTierB1ProperTests
{
    // ══════════════════════ Enum + design fields ══════════════════════

    [Theory]
    [InlineData(ChannelTopology.TpmsGyroid,   TpmsKind.Gyroid)]
    [InlineData(ChannelTopology.TpmsSchwarzP, TpmsKind.SchwarzP)]
    [InlineData(ChannelTopology.TpmsSchwarzD, TpmsKind.SchwarzD)]
    public void DesignTpmsKindAccessor_ProjectsCorrectly(
        ChannelTopology topo, TpmsKind expected)
    {
        var design = new RegenChamberDesign { ChannelTopology = topo };
        Assert.Equal(expected, design.TpmsKind);
    }

    [Theory]
    [InlineData(ChannelTopology.Axial)]
    [InlineData(ChannelTopology.Helical)]
    [InlineData(ChannelTopology.None)]
    public void DesignTpmsKindAccessor_ReturnsNullForNonTpms(ChannelTopology topo)
    {
        var design = new RegenChamberDesign { ChannelTopology = topo };
        Assert.Null(design.TpmsKind);
    }

    [Fact]
    public void DesignTpmsDefaults_AreSane()
    {
        var design = new RegenChamberDesign();
        Assert.Equal(3.0,  design.TpmsCellEdge_mm, 6);
        Assert.Equal(0.50, design.TpmsSolidFraction, 6);
    }

    // ══════════════════════ MinStrutThickness + helpers ══════════════════════

    [Fact]
    public void MinStrutThicknessConstant_Is2mm()
    {
        Assert.Equal(2.0, TpmsCorrelations.MinStrutThickness_mm, 6);
    }

    [Theory]
    [InlineData(3.0,  0.50, 1.50)]
    [InlineData(4.0,  0.50, 2.00)]
    [InlineData(5.0,  0.60, 3.00)]
    [InlineData(2.0,  0.30, 0.60)]
    public void StrutThickness_LinearInCellAndSolidFraction(
        double cellEdge_mm, double solidFraction, double expected_mm)
    {
        double actual = TpmsCorrelations.StrutThickness_mm(cellEdge_mm, solidFraction);
        Assert.Equal(expected_mm, actual, 6);
    }

    [Fact]
    public void StrutThickness_ZeroCellEdge_ReturnsZero()
    {
        Assert.Equal(0, TpmsCorrelations.StrutThickness_mm(0, 0.50), 6);
    }

    [Fact]
    public void StrutThickness_OutOfRangeSolidFraction_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => TpmsCorrelations.StrutThickness_mm(3.0, 0.10));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => TpmsCorrelations.StrutThickness_mm(3.0, 0.90));
    }

    // ══════════════════════ Feasibility gate #15 ══════════════════════
    //
    // Reuse the same shared-fixture pattern FeasibilityGateTests uses —
    // one real solver run cached, then `with`-injection to isolate the
    // single field each test cares about. Re-using FeasibilityGateTests'
    // cached RawResult directly would couple the files; a local mirror
    // is clearer.

    private static RegenGenerationResult? _rawCache;
    private static readonly object _rawLock = new();

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

    private static RegenGenerationResult RawResult()
    {
        lock (_rawLock)
            return _rawCache ??= RegenChamberOptimization.GenerateWith(
                DefaultConditions(),
                new RegenChamberDesign
                {
                    IncludeManifolds      = false,
                    IncludePorts          = false,
                    IncludeInjectorFlange = false,
                    ContourStationCount   = 60,
                });
    }

    private static RegenGenerationResult SafeResult()
    {
        var r   = RawResult();
        var mat = WallMaterials.All[DefaultConditions().WallMaterialIndex];
        var ch4 = MethaneFluid.Instance;
        return r with
        {
            Thermal = r.Thermal with
            {
                PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,
                WallTempExceedsLimit = false,
                CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0,
            },
            Stress = r.Stress with { MinSafetyFactor = 2.5, YieldExceeded = false },
            Manufacturing = r.Manufacturing with
            {
                MinFeatureSize_mm = 0.55,
                FeatureSizeOK     = true,
            },
            Stability = r.Stability with
            {
                Composite       = StabilityRating.Pass,
                CompositeReason = "test-injected feasible",
            },
        };
    }

    [Fact]
    public void FeasibilityGate_TpmsStrutTooSmall_Fires()
    {
        // 3.0 mm cell × 0.50 solid → 1.5 mm strut; below 2.0 mm floor.
        var r = SafeResult() with
        {
            ChannelTopology   = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm   = 3.0,
            TpmsSolidFraction = 0.50,
        };
        var eval = FeasibilityGate.Evaluate(r);
        Assert.False(eval.IsFeasible);
        var v = Assert.Single(eval.Violations, x => x.ConstraintId == "TPMS_CELL_FEATURE_TOO_SMALL");
        Assert.InRange(v.ActualValue, 1.49, 1.51);
        Assert.Equal(2.0, v.Limit, 6);
    }

    [Fact]
    public void FeasibilityGate_TpmsStrutAtFloor_Passes()
    {
        // 4.0 mm cell × 0.50 solid → 2.0 mm strut == floor; pass edge.
        var r = SafeResult() with
        {
            ChannelTopology   = ChannelTopology.TpmsSchwarzD,
            TpmsCellEdge_mm   = 4.0,
            TpmsSolidFraction = 0.50,
        };
        var eval = FeasibilityGate.Evaluate(r);
        Assert.DoesNotContain(eval.Violations,
            v => v.ConstraintId == "TPMS_CELL_FEATURE_TOO_SMALL");
    }

    [Fact]
    public void FeasibilityGate_AxialTopology_NeverFiresTpmsGate()
    {
        // Even with a tiny (nonsense for Axial) cell edge, the gate does
        // not evaluate for a non-TPMS topology.
        var r = SafeResult() with
        {
            ChannelTopology   = ChannelTopology.Axial,
            TpmsCellEdge_mm   = 0.1,
            TpmsSolidFraction = 0.50,
        };
        var eval = FeasibilityGate.Evaluate(r);
        Assert.DoesNotContain(eval.Violations,
            v => v.ConstraintId == "TPMS_CELL_FEATURE_TOO_SMALL");
    }

    [Theory]
    [InlineData(ChannelTopology.TpmsGyroid)]
    [InlineData(ChannelTopology.TpmsSchwarzP)]
    [InlineData(ChannelTopology.TpmsSchwarzD)]
    public void FeasibilityGate_AllThreeTpmsFamilies_FireOnUndersizedStrut(ChannelTopology topo)
    {
        var r = SafeResult() with
        {
            ChannelTopology   = topo,
            TpmsCellEdge_mm   = 2.5,
            TpmsSolidFraction = 0.50,
        };
        var eval = FeasibilityGate.Evaluate(r);
        Assert.Contains(eval.Violations,
            v => v.ConstraintId == "TPMS_CELL_FEATURE_TOO_SMALL");
    }

    // ══════════════════════ Hand-rolled implicits ══════════════════════

    [Fact]
    public void TpmsUnitCellImplicit_FunctionDependsOnPosition()
    {
        // At cell edge 4 mm and solid fraction 0.50, threshold c = 0.
        // Sample at two different positions and verify the function
        // value changes.
        var unit = new TpmsUnitCellImplicit(TpmsKind.Gyroid, cellEdge_mm: 4.0f, solidFraction: 0.50f);
        float a = unit.fSignedDistance(new Vector3(0, 0, 0));
        float b = unit.fSignedDistance(new Vector3(1.0f, 1.0f, 0f));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TpmsUnitCellImplicit_ThresholdShiftsWithSolidFraction()
    {
        // Sign convention: fSignedDistance returns
        //     LipschitzScale * (threshold(ψ) - f(p))
        // At ψ = 0.65 > 0.50 the threshold rises (more solid), so at a
        // fixed p the returned value grows more positive (the point sits
        // deeper inside the solid phase). Assert the expected direction.
        var half  = new TpmsUnitCellImplicit(TpmsKind.Gyroid, cellEdge_mm: 4.0f, solidFraction: 0.50f);
        var dense = new TpmsUnitCellImplicit(TpmsKind.Gyroid, cellEdge_mm: 4.0f, solidFraction: 0.65f);
        var p = new Vector3(1.0f, 1.0f, 0f);
        float vHalf  = half .fSignedDistance(p);
        float vDense = dense.fSignedDistance(p);
        Assert.True(vDense > vHalf,
            $"Expected vDense ({vDense}) > vHalf ({vHalf}) since denser ψ shifts the threshold up.");
    }

    [Fact]
    public void TpmsUnitCellImplicit_ZeroCellEdgeThrows()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new TpmsUnitCellImplicit(TpmsKind.Gyroid, 0f, 0.50f));
    }

    [Fact]
    public void TpmsAnnularImplicit_OutsideAxialRange_ReportsPositiveDistance()
    {
        var contour = new RevolvedContourImplicit(new[] {
            (0.0, 10.0), (100.0, 10.0) });
        var annular = new TpmsAnnularImplicit(
            innerContour: contour,
            kind: TpmsKind.Gyroid, cellEdge_mm: 4.0f, solidFraction: 0.50f,
            tWall_mm: 1.0f,
            hChamber_mm: 2.0f, hThroat_mm: 2.0f, hExit_mm: 2.0f,
            xStart_mm: 10.0f, xThroat_mm: 50.0f, xEnd_mm: 90.0f);
        // Sample at x = 0 (outside [10, 90]). Should report positive
        // distance to the nearest axial cap (~10 mm).
        float d = annular.fSignedDistance(new Vector3(0f, 12f, 0f));
        Assert.True(d > 0, $"Expected positive, got {d}");
        Assert.InRange(d, 9.9f, 10.1f);
    }

    [Fact]
    public void TpmsAnnularImplicit_InnerToRadialBand_ReportsPositiveDistance()
    {
        var contour = new RevolvedContourImplicit(new[] {
            (0.0, 10.0), (100.0, 10.0) });
        var annular = new TpmsAnnularImplicit(
            innerContour: contour,
            kind: TpmsKind.Gyroid, cellEdge_mm: 4.0f, solidFraction: 0.50f,
            tWall_mm: 1.0f,
            hChamber_mm: 2.0f, hThroat_mm: 2.0f, hExit_mm: 2.0f,
            xStart_mm: 10.0f, xThroat_mm: 50.0f, xEnd_mm: 90.0f);
        // Inside axial range, well inside gas wall (r = 5 mm, inner-band
        // starts at R = 11 mm + clearance). Must be positive.
        float d = annular.fSignedDistance(new Vector3(50f, 5f, 0f));
        Assert.True(d > 0, $"Expected positive, got {d}");
    }

    // ══════════════════════ AutoSeeder override ══════════════════════

    [Theory]
    [InlineData(ChannelTopology.TpmsGyroid)]
    [InlineData(ChannelTopology.TpmsSchwarzP)]
    [InlineData(ChannelTopology.TpmsSchwarzD)]
    public void AutoSeeder_TpmsOverride_PopulatesTpmsFields(ChannelTopology topo)
    {
        var spec = new EngineSpec(
            PropellantPair:          PropellantPair.LOX_CH4,
            Thrust_N:                20000,
            ChamberPressure_Pa:      7e6,
            ExpansionRatio:          15.0,
            ChannelTopologyOverride: topo);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(topo, seed.Design.ChannelTopology);
        Assert.True(seed.Design.TpmsCellEdge_mm * seed.Design.TpmsSolidFraction
                    >= TpmsCorrelations.MinStrutThickness_mm,
            $"seed strut = {seed.Design.TpmsCellEdge_mm * seed.Design.TpmsSolidFraction}");
        Assert.Contains(seed.Rationale, r => r.Contains("TPMS"));
    }

    [Fact]
    public void AutoSeeder_NoOverride_LeavesAxialBaseline()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_CH4,
            Thrust_N:           5000,
            ChamberPressure_Pa: 5e6,
            ExpansionRatio:     10.0);
        var seed = AutoSeeder.Seed(spec);
        Assert.Equal(ChannelTopology.Axial, seed.Design.ChannelTopology);
    }

    [Fact]
    public void AutoSeeder_TpmsCellEdgeFor_ScalesWithThrust()
    {
        double small = AutoSeeder.TpmsCellEdgeFor(1_000);
        double mid   = AutoSeeder.TpmsCellEdgeFor(10_000);
        double large = AutoSeeder.TpmsCellEdgeFor(100_000);
        double mega  = AutoSeeder.TpmsCellEdgeFor(500_000);
        Assert.True(small <= mid);
        Assert.True(mid   <= large);
        Assert.True(large <= mega);
        foreach (double c in new[] { small, mid, large, mega })
            Assert.True(c * 0.50 >= TpmsCorrelations.MinStrutThickness_mm,
                $"cell edge {c} * 0.50 < {TpmsCorrelations.MinStrutThickness_mm}");
    }

    // ══════════════════════ Solver-input plumbing ══════════════════════

    [Fact]
    public void RegenSolverInputs_TpmsFields_RoundTrip()
    {
        var inputs = new RegenSolverInputs(
            Contour:                 RawResult().Contour,
            Gas:                     RawResult().Gas,
            Wall:                    WallMaterials.CuCrZr,
            Channels:                new ChannelSchedule(80, 0.8, 0.8, 2.5, 1.5, 2.0),
            CoolantMassFlow_kgs:     0.1,
            CoolantInletTemp_K:      150,
            CoolantInletPressure_Pa: 12e6,
            TpmsKind:                TpmsKind.Gyroid,
            TpmsCellEdge_m:          4e-3,
            TpmsSolidFraction:       0.55);

        Assert.Equal(TpmsKind.Gyroid, inputs.TpmsKind);
        Assert.Equal(4e-3,            inputs.TpmsCellEdge_m, 9);
        Assert.Equal(0.55,            inputs.TpmsSolidFraction, 6);
    }

    [Fact]
    public void RegenSolverInputs_NullTpmsKind_IsDefault()
    {
        var inputs = new RegenSolverInputs(
            Contour:                 RawResult().Contour,
            Gas:                     RawResult().Gas,
            Wall:                    WallMaterials.CuCrZr,
            Channels:                new ChannelSchedule(80, 0.8, 0.8, 2.5, 1.5, 2.0),
            CoolantMassFlow_kgs:     0.1,
            CoolantInletTemp_K:      150,
            CoolantInletPressure_Pa: 12e6);
        Assert.Null(inputs.TpmsKind);
    }

    // ══════════════════════ Topology echo on result ══════════════════════

    [Fact]
    public void RegenGenerationResult_AxialDesign_EchoesAxialTopology()
    {
        // Baseline (Axial) — result should stamp topology = Axial.
        var r = RawResult();
        Assert.Equal(ChannelTopology.Axial, r.ChannelTopology);
    }

    // ══════════════════════ SA variable [18]/[19] ══════════════════════

    [Fact]
    public void Bounds_Slots18And19_AreTpmsRanges()
    {
        // Bounds[18] = TpmsCellEdge_mm ∈ [2.0, 6.0], Bounds[19] = ψ ∈ [0.35, 0.65].
        // Bounds was later lengthened to 22 (added PreburnerMrRatio @20 + FlangeRadialProjection @21).
        // Lengthened to 23 (added PlugLengthRatio @22).
        // Lengthened to 24 (added AerospikeContractionRatio @23).
        // Lengthened to 26 (feasibility-audit Sprint 5: FilmFuelFraction @24 + FilmSlotHeightOverride @25).
        // OOB-6 / Sprint B-3 (2026-04-30) added dims 31-33 (acoustic dampers);
        // total = 34. TPMS slots at 18/19 unchanged.
        var b = RegenChamberOptimization.Bounds;
        Assert.Equal(34, b.Length);
        Assert.Equal((2.0, 6.0),   b[18]);
        Assert.Equal((0.35, 0.65), b[19]);
    }

    [Fact]
    public void Pack_AlwaysEmitsTpmsFields_RegardlessOfTopology()
    {
        // The pack vector is length-stable across topologies so the SA
        // scratch arrays never need to be re-sized when a user flips
        // ChannelTopology.
        var axial = new RegenChamberDesign
        {
            ChannelTopology   = ChannelTopology.Axial,
            TpmsCellEdge_mm   = 4.5,
            TpmsSolidFraction = 0.55,
        };
        var tpms = new RegenChamberDesign
        {
            ChannelTopology   = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm   = 4.5,
            TpmsSolidFraction = 0.55,
        };
        var packedAxial = RegenChamberOptimization.Pack(axial);
        var packedTpms  = RegenChamberOptimization.Pack(tpms);
        // Feasibility-audit Sprint 5 (2026-04-26): length = 26 (TPMS @ 18/19,
        // Tier-C @ 20/21, aerospike plug @ 22, aerospike contraction @ 23,
        // FilmFuelFraction @ 24, FilmSlotHeightOverride @ 25).
        Assert.Equal(34, packedAxial.Length);
        Assert.Equal(34, packedTpms.Length);
        Assert.Equal(4.5,  packedAxial[18], precision: 6);
        Assert.Equal(0.55, packedAxial[19], precision: 6);
        Assert.Equal(4.5,  packedTpms[18], precision: 6);
        Assert.Equal(0.55, packedTpms[19], precision: 6);
    }

    [Fact]
    public void Unpack_AppliesTpmsFields_OnTpmsBaseline()
    {
        var baseline = new RegenChamberDesign
        {
            ChannelTopology   = ChannelTopology.TpmsGyroid,
            TpmsCellEdge_mm   = 3.0,
            TpmsSolidFraction = 0.50,
        };
        var p = RegenChamberOptimization.Pack(baseline);
        p[18] = 5.5;
        p[19] = 0.42;
        var unpacked = RegenChamberOptimization.Unpack(p, baseline);

        Assert.Equal(5.5,  unpacked.TpmsCellEdge_mm, precision: 6);
        Assert.Equal(0.42, unpacked.TpmsSolidFraction, precision: 6);
        // Categorical preserved — SA never flips topology.
        Assert.Equal(ChannelTopology.TpmsGyroid, unpacked.ChannelTopology);
    }

    [Theory]
    [InlineData(ChannelTopology.Axial)]
    [InlineData(ChannelTopology.Helical)]
    [InlineData(ChannelTopology.None)]
    public void Unpack_LeavesTpmsFieldsAtBaseline_OnNonTpmsTopology(ChannelTopology topo)
    {
        // Same "silent revert" rule as port standards / element type /
        // mounting flange standard — non-TPMS designs must not have their
        // TPMS defaults silently mutated by an SA perturbation.
        var baseline = new RegenChamberDesign
        {
            ChannelTopology   = topo,
            TpmsCellEdge_mm   = 3.0,   // record default
            TpmsSolidFraction = 0.50,  // record default
        };
        var p = RegenChamberOptimization.Pack(baseline);
        // Drive the TPMS slots way out of the baseline value.
        p[18] = 5.9;
        p[19] = 0.63;
        var unpacked = RegenChamberOptimization.Unpack(p, baseline);

        Assert.Equal(3.0,  unpacked.TpmsCellEdge_mm, precision: 6);
        Assert.Equal(0.50, unpacked.TpmsSolidFraction, precision: 6);
        Assert.Equal(topo, unpacked.ChannelTopology);
    }

    [Fact]
    public void Unpack_ClampsTpmsFields_ToValidEnvelope()
    {
        var baseline = new RegenChamberDesign
        {
            ChannelTopology = ChannelTopology.TpmsSchwarzD,
        };
        var p = RegenChamberOptimization.Pack(baseline);
        p[18] = 50.0;    // absurd cell edge
        p[19] = 2.5;     // absurd ψ
        var over = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(over.TpmsCellEdge_mm,   1.0, 10.0);
        Assert.InRange(over.TpmsSolidFraction, 0.30, 0.70);

        p[18] = -5.0;
        p[19] = -0.1;
        var under = RegenChamberOptimization.Unpack(p, baseline);
        Assert.InRange(under.TpmsCellEdge_mm,   1.0, 10.0);
        Assert.InRange(under.TpmsSolidFraction, 0.30, 0.70);
    }

    [Fact]
    public void PackUnpack_RoundTripsAllTpmsFamilies()
    {
        foreach (var topo in new[]
        {
            ChannelTopology.TpmsGyroid,
            ChannelTopology.TpmsSchwarzP,
            ChannelTopology.TpmsSchwarzD,
        })
        {
            var baseline = new RegenChamberDesign
            {
                ChannelTopology   = topo,
                TpmsCellEdge_mm   = 4.2,
                TpmsSolidFraction = 0.48,
            };
            var p = RegenChamberOptimization.Pack(baseline);
            var copy = RegenChamberOptimization.Unpack(p, baseline);
            Assert.Equal(topo, copy.ChannelTopology);
            Assert.Equal(4.2,  copy.TpmsCellEdge_mm, precision: 6);
            Assert.Equal(0.48, copy.TpmsSolidFraction, precision: 6);
        }
    }
}
