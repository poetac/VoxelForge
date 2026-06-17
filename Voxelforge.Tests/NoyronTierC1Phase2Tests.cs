// NoyronTierC1Phase2Tests.cs — Tier C1 Phase 2 forcing-function suite
// for the aerospike plug-regen-cooling pipeline.
//
// Coverage
// ────────
//   • AerospikeSpec Phase 2 defaults (IncludeRegenChannels = false, etc.)
//   • AerospikePlugChannelImplicit axial clip + radial clip + in-band sign
//   • AerospikePlugChannelArray returns min over channels
//   • AerospikeContour.SegmentLengthApprox_mm round-trip
//   • AerospikeThermalResult population when spec opts in (solver
//     runs without PicoGK — pure-math)
//   • AerospikePlugCooling.Solve returns populated per-station arrays
//     + peak wall T > inlet coolant T
//   • AerospikeFeasibility.Evaluate: null thermal → feasible; hot
//     thermal → AEROSPIKE_PLUG_WALL_TEMP fires; cold thermal → passes
//
// All tests pure-math — no PicoGK Library initialisation required.

using System.Numerics;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class NoyronTierC1Phase2Tests
{
    // ══════════════════════ Spec defaults ══════════════════════

    [Fact]
    public void Spec_Phase1Default_LeavesRegenChannelsOff()
    {
        var spec = new AerospikeSpec(20000, 7e6, 15.0, 0.30);
        Assert.False(spec.IncludeRegenChannels);
        Assert.Equal(24,  spec.PlugChannelCount);
        Assert.Equal(2.5, spec.PlugChannelWidth_mm, 6);
        Assert.Equal(2.0, spec.PlugChannelDepth_mm, 6);
        Assert.Equal(0.8, spec.PlugWallThickness_mm, 6);
        Assert.Equal(1,   spec.WallMaterialIndex);   // CuCrZr default
    }

    [Fact]
    public void Spec_WithRegenChannels_RoundTrips()
    {
        var spec = new AerospikeSpec(
            Thrust_N:             20000,
            ChamberPressure_Pa:   7e6,
            ExpansionRatio:       15.0,
            PlugLengthRatio:      0.30,
            IncludeRegenChannels: true,
            PlugChannelCount:     32,
            PlugChannelWidth_mm:  3.0,
            PlugChannelDepth_mm:  2.5);
        Assert.True(spec.IncludeRegenChannels);
        Assert.Equal(32,  spec.PlugChannelCount);
        Assert.Equal(3.0, spec.PlugChannelWidth_mm, 6);
    }

    // ══════════════════════ Plug-channel implicit ══════════════════════

    [Fact]
    public void PlugChannel_OutsideAxialRange_ReturnsPositive()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var ch = new AerospikePlugChannelImplicit(
            contour, tWall_mm: 0.8f, depth_mm: 2.0f, width_mm: 2.5f,
            thetaCenterRad: 0f);
        // Sample well before the contour's axial extent (contour starts at x=0).
        var p = new Vector3(-50f, (float)contour.Stations[0].R_inner_mm, 0);
        Assert.True(ch.fSignedDistance(p) > 0);
    }

    [Fact]
    public void PlugChannel_OffAxisFromCenterline_ReturnsPositive()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var ch = new AerospikePlugChannelImplicit(
            contour, tWall_mm: 0.8f, depth_mm: 2.0f, width_mm: 2.5f,
            thetaCenterRad: 0f);
        // Channel at θ=0 (along +Y). Sample at θ=π (opposite side).
        var mid = contour.Stations[contour.Stations.Length / 2];
        var p = new Vector3((float)mid.X_mm, -(float)mid.R_inner_mm, 0);
        Assert.True(ch.fSignedDistance(p) > 0);
    }

    [Fact]
    public void PlugChannelArray_AtEveryThetaKChannels_HasNegativeSDFInChannelBand()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        int count = 16;
        var array = new AerospikePlugChannelArray(
            contour, count, tWall_mm: 0.8f, depth_mm: 2.0f, width_mm: 2.5f);
        // Sample along the channel centerline of channel 0 (θ = 0) at
        // the midpoint axial station. Should be inside a channel
        // (negative).
        var mid = contour.Stations[contour.Stations.Length / 2];
        float rSurface = (float)mid.R_inner_mm;
        float tWall = 0.8f;
        float depth = 2.0f;
        float rCenter = rSurface - tWall - 0.5f * depth;
        var p = new Vector3((float)mid.X_mm, rCenter, 0);
        Assert.True(array.fSignedDistance(p) < 0);
    }

    [Fact]
    public void PlugChannelArray_NonPositiveCount_Throws()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            new AerospikePlugChannelArray(
                contour, count: 0, tWall_mm: 0.8f, depth_mm: 2.0f, width_mm: 2.5f));
    }

    // ══════════════════════ Contour helpers ══════════════════════

    [Fact]
    public void SegmentLengthApprox_ReturnsPositive()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        for (int i = 0; i < contour.Stations.Length; i++)
        {
            double ds = contour.SegmentLengthApprox_mm(i);
            Assert.True(ds > 0, $"station {i} ds={ds}");
        }
    }

    [Fact]
    public void SegmentLengthApprox_SumApproximatesTotalPlugLength()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        double sum = 0;
        for (int i = 0; i < contour.Stations.Length; i++)
            sum += contour.SegmentLengthApprox_mm(i);
        // Trapezoidal sum of station lengths ≈ total plug length
        // (first + last stations contribute half-segments).
        Assert.InRange(sum,
            contour.PlugTruncatedLength_mm * 0.95,
            contour.PlugTruncatedLength_mm * 1.05);
    }

    // ══════════════════════ Thermal solver ══════════════════════

    private static AerospikePlugCoolingInputs MakeCoolingInputs(bool hotGas = false)
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var gas = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 7e6);
        // Optionally crank up chamber temp to force a hot-wall failure.
        if (hotGas) gas = gas with { ChamberTemp_K = 6000 };
        var fluid = CoolantRegistry.Get(PropellantPairs.GetMeta(PropellantPair.LOX_CH4).CoolantFluidKey);
        return new AerospikePlugCoolingInputs(
            Contour:                contour,
            Gas:                    gas,
            Wall:                   WallMaterials.CuCrZr,
            ChannelCount:           24,
            ChannelWidth_mm:        2.5,
            ChannelDepth_mm:        2.0,
            PlugWallThickness_mm:   0.8,
            CoolantMassFlow_kgs:    0.5,
            CoolantInletTemp_K:     120.0,
            CoolantInletPressure_Pa: 12e6,
            CoolantFluid:           fluid);
    }

    [Fact]
    public void PlugCooling_Solve_ReturnsArraysAtStationResolution()
    {
        var inp = MakeCoolingInputs();
        var result = AerospikePlugCooling.Solve(inp);
        Assert.Equal(inp.Contour.Stations.Length, result.GasSideWallT_K.Length);
        Assert.Equal(inp.Contour.Stations.Length, result.CoolantBulkT_K.Length);
        Assert.Equal(inp.Contour.Stations.Length, result.HeatFlux_Wm2.Length);
    }

    [Fact]
    public void PlugCooling_PeakWallT_Positive()
    {
        var inp = MakeCoolingInputs();
        var result = AerospikePlugCooling.Solve(inp);
        Assert.True(result.PeakGasSideWallT_K > inp.CoolantInletTemp_K,
            $"peak wall T {result.PeakGasSideWallT_K} should exceed coolant inlet {inp.CoolantInletTemp_K}");
    }

    [Fact]
    public void PlugCooling_CoolantOutletT_GreaterThanInlet()
    {
        var inp = MakeCoolingInputs();
        var result = AerospikePlugCooling.Solve(inp);
        // Coolant bulk has been heated by the gas-side integral.
        Assert.True(result.CoolantOutletT_K > inp.CoolantInletTemp_K,
            $"outlet T {result.CoolantOutletT_K} not above inlet {inp.CoolantInletTemp_K}");
    }

    [Fact]
    public void PlugCooling_CoolantPressureDrop_NonNegative()
    {
        var inp = MakeCoolingInputs();
        var result = AerospikePlugCooling.Solve(inp);
        Assert.True(result.CoolantPressureDrop_Pa >= 0);
    }

    // ══════════════════════ PH-41 + PH-43 (2026-04-29) ══════════════════════
    //
    // Two coupled fixes to AerospikePlugCooling.Solve:
    //   • PH-41: rCurv set to D_ref (was 0.5·D_ref), collapsing the
    //     (D_t/r_c)^0.1 Bartz curvature term to 1.0. The plug nozzle
    //     has no longitudinal throat curvature in the bell-nozzle sense
    //     — the prior 7 % enhancement was a fictitious placeholder.
    //   • PH-43: areaRatio = A_t/A_local from the isentropic compressible-
    //     flow area-Mach relation. Pre-PH-43 the (R_o/r_surf)² formula
    //     was clamped to 1 inside BartzHeatFlux, neutralising the term5
    //     area-ratio enhancement at every plug station. The new form
    //     varies smoothly with M_local and contributes < 1.

    [Fact]
    public void PH41_PH43_PlugCooling_PeakWallT_BelowChamberT()
    {
        // Sanity pin — peak T_wg must lie strictly between the coolant
        // inlet and the chamber stagnation T post-rescale. Pre-PH-41/PH-43
        // the (D/r_c)^0.1 curvature term gave a 7 % fictitious enhancement
        // and the area-ratio term was silently neutralised by an upward
        // clamp. The new formulation reduces h_g past the throat in the
        // expected direction; a well-formed solver still keeps peak wall T
        // < chamber T regardless of the cooling envelope. The canonical
        // LOX/CH4 fixture is intentionally hot — this test asserts only
        // physical plausibility, not gate-passing.
        var inp = MakeCoolingInputs();
        var result = AerospikePlugCooling.Solve(inp);
        Assert.True(result.PeakGasSideWallT_K > inp.CoolantInletTemp_K,
            $"peak wall T {result.PeakGasSideWallT_K} should exceed coolant inlet {inp.CoolantInletTemp_K}");
        Assert.True(result.PeakGasSideWallT_K < inp.Gas.ChamberTemp_K,
            $"peak wall T {result.PeakGasSideWallT_K} should be below chamber T {inp.Gas.ChamberTemp_K}");
    }

    [Fact]
    public void PH43_AreaRatio_DecreasesPastThroat()
    {
        // The post-PH-43 area-Mach relation gives areaRatio = A_t/A_local
        // ≤ 1 that decreases with axial distance (M_local rises along the
        // plug). The peak heat flux should occur at or near the first
        // station (highest h_g, hottest gas), not deep into the expansion.
        var inp = MakeCoolingInputs();
        var result = AerospikePlugCooling.Solve(inp);
        // Peak station should be in the upstream half of the plug, where
        // gas density is highest. Past-throat expansion drops both density
        // and h_g monotonically post-PH-43.
        Assert.True(result.PeakStation_X_mm < inp.Contour.PlugTruncatedLength_mm * 0.6,
            $"Peak T_wg station {result.PeakStation_X_mm} mm should occur "
          + $"in the upstream half of the {inp.Contour.PlugTruncatedLength_mm:F1} mm plug.");
    }

    // ══════════════════════ Feasibility gate #16 ══════════════════════

    private static AerospikeBuildResult MakeBuildResultWithThermal(AerospikeThermalResult thermal)
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        return new AerospikeBuildResult(
            Voxels: null,
            Contour: contour,
            ThroatOuterRadius_mm: 30.0,
            ThroatInnerRadius_mm: 12.0,
            PlugTruncatedLength_mm: contour.PlugTruncatedLength_mm,
            ChamberRadius_mm: 75.0,
            ChamberLength_mm: 90.0,
            TotalLength_mm: 90.0 + contour.PlugTruncatedLength_mm,
            TotalDiameter_mm: 150.0,
            SolidVolume_mm3: 100_000,
            EstimatedMass_g: 800,
            Description: "test fixture",
            Thermal: thermal);
    }

    [Fact]
    public void Feasibility_NullThermal_ReturnsFeasible()
    {
        var build = MakeBuildResultWithThermal(null!);
        // null override path via `with`-style construction:
        var phase1 = build with { Thermal = null };
        var r = AerospikeFeasibility.Evaluate(phase1, wallMaterialIndex: 1);
        Assert.True(r.IsFeasible);
        Assert.Empty(r.Violations);
    }

    [Fact]
    public void Feasibility_CoolWallT_Passes()
    {
        var thermal = new AerospikeThermalResult(
            GasSideWallT_K:  new[] { 500.0, 550.0, 600.0 },
            CoolantBulkT_K:  new[] { 120.0, 160.0, 200.0 },
            HeatFlux_Wm2:    new[] { 1e6, 1.5e6, 2e6 },
            PeakGasSideWallT_K:    600.0,   // CuCrZr limit 800 K → pass
            PeakStation_X_mm:      20.0,
            CoolantOutletT_K:      200.0,
            CoolantPressureDrop_Pa: 1e5,
            TotalHeatLoad_W:       5000,
            Warnings:              System.Array.Empty<string>());
        var build = MakeBuildResultWithThermal(thermal);
        var r = AerospikeFeasibility.Evaluate(build, wallMaterialIndex: 1);
        Assert.True(r.IsFeasible);
    }

    [Fact]
    public void Feasibility_HotWallT_FiresPlugWallTempGate()
    {
        var thermal = new AerospikeThermalResult(
            GasSideWallT_K:  new[] { 500.0, 1200.0, 1300.0 },
            CoolantBulkT_K:  new[] { 120.0, 160.0, 200.0 },
            HeatFlux_Wm2:    new[] { 1e6, 5e6, 6e6 },
            PeakGasSideWallT_K:    1300.0,  // CuCrZr limit 800 K → fail
            PeakStation_X_mm:      22.0,
            CoolantOutletT_K:      210.0,
            CoolantPressureDrop_Pa: 1e5,
            TotalHeatLoad_W:       10000,
            Warnings:              System.Array.Empty<string>());
        var build = MakeBuildResultWithThermal(thermal);
        var r = AerospikeFeasibility.Evaluate(build, wallMaterialIndex: 1);
        Assert.False(r.IsFeasible);
        var v = Assert.Single(r.Violations, x => x.ConstraintId == "AEROSPIKE_PLUG_WALL_TEMP");
        Assert.Equal(1300.0, v.ActualValue, 1);
    }
}
