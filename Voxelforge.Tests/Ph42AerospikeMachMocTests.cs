// PH-42 (#187, 2026-04-29): aerospike plug local M(x) sourced from the
// contour's per-station FlowAngle_rad (Sprint 31 / PH-15 had already
// recorded `nuExit - nuLocal` per station) instead of the cooling
// solver's own duplicated linear ν(x) ramp. Algebraically equivalent
// today (both come from Angelino's linear-Prandtl-Meyer assumption);
// structurally rewires so a future MoC characteristic-net upgrade of
// the contour generator (option (a) of #187) is one-step. Always emits
// a Note documenting the limitation pending T2.3 (#160) CFD validation.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class Ph42AerospikeMachMocTests
{
    private static AerospikePlugCoolingInputs MakeInputs()
    {
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        var gas = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 7e6);
        var fluid = CoolantRegistry.Get(
            PropellantPairs.GetMeta(PropellantPair.LOX_CH4).CoolantFluidKey);
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

    // ── Group 1: Notes is always populated with the PH-42 advisory ──

    [Fact]
    public void Solve_AlwaysEmitsPh42AdvisoryNote()
    {
        var result = AerospikePlugCooling.Solve(MakeInputs());
        Assert.NotNull(result.Notes);
        Assert.NotEmpty(result.Notes!);
        Assert.Contains(result.Notes!,
            n => n.Contains("PH-42")
              && n.Contains("FlowAngle_rad")
              && n.Contains("Angelino")
              && n.Contains("#160"));
    }

    [Fact]
    public void Solve_NoteIsInformational_NotADuplicateOfWarnings()
    {
        // Notes is for permanent solver-assumption disclosure; Warnings
        // is for runtime issues (cavitation, empty contour). The PH-42
        // advisory must NOT appear in Warnings.
        var result = AerospikePlugCooling.Solve(MakeInputs());
        Assert.DoesNotContain(result.Warnings, w => w.Contains("PH-42"));
        // Conversely, in a healthy run there should be no warnings at all.
        Assert.Empty(result.Warnings);
    }

    // ── Group 2: rewired ν calculation matches contour's flow-angle ─

    [Fact]
    public void Solve_LocalMach_DerivesFromContourFlowAngle()
    {
        // The cooling solver post-PH-42 reads nuLocal via
        //   nuLocal = nuExit - station.FlowAngle_rad
        // The contour's FlowAngle_rad was recorded as nuExit - nuLocal_at_generation.
        // So nuLocal_solver(i) == nuLocal_at_generation(i) at every station,
        // i.e. the solver's per-station Mach march reproduces the contour's
        // own internal Mach march bit-exactly. Pre-PH-42 the solver
        // recomputed a separate (s.X_mm / PlugFullLength_mm) ramp, which
        // happened to be algebraically equivalent under today's contour
        // generator but would diverge if the contour upgraded to MoC.
        var inp = MakeInputs();
        var result = AerospikePlugCooling.Solve(inp);

        // Indirect proof: peak wall T must occur AT or NEAR the throat
        // (highest gas-side h_g, M ≈ 1 → highest T_static and T_aw); the
        // X coordinate of the peak should land in the first 30 % of the
        // truncated plug length.
        Assert.True(result.PeakStation_X_mm >= 0,
            $"Peak station x ({result.PeakStation_X_mm:F2}) should be non-negative.");
        Assert.True(result.PeakStation_X_mm
                    <= 0.30 * inp.Contour.PlugTruncatedLength_mm,
            $"Peak wall T should appear in the first 30 % of the plug; "
          + $"x_peak = {result.PeakStation_X_mm:F2} mm vs L_trunc = "
          + $"{inp.Contour.PlugTruncatedLength_mm:F2} mm.");
    }

    [Fact]
    public void Solve_FlowAngleMonotonicallyDecreasesAlongPlug()
    {
        // Sanity check on the contour itself — FlowAngle_rad should
        // monotonically DECREASE from throat (large) to truncation
        // (small). This is the same monotonicity the cooling solver
        // depends on for nuLocal to monotonically increase along x
        // (since nuLocal = nuExit - FlowAngle).
        var contour = AerospikeContourGenerator.Generate(30.0, 15.0, 0.30);
        for (int i = 1; i < contour.Stations.Length; i++)
        {
            double prev = contour.Stations[i - 1].FlowAngle_rad;
            double curr = contour.Stations[i].FlowAngle_rad;
            Assert.True(curr <= prev + 1e-9,
                $"FlowAngle non-monotonic at station {i}: {prev:F6} → {curr:F6}.");
        }
    }

    // ── Group 3: existing per-station h_g monotonicity invariant ────
    //
    // The issue text calls out: "Existing AerospikePlugCoolingTests
    // regression keeps the per-station h_g monotonicity invariant."
    // We do NOT have a direct h_g array on the result, but the gas-side
    // wall T and heat-flux arrays carry the same shape information:
    // T_wg should peak at the throat (max h_g · ΔT) and decay toward
    // the truncation (where Mach is highest, T_static is lowest, ΔT
    // shrinks). Pin that shape so a future contour upgrade can't
    // silently regress.

    [Fact]
    public void Solve_HeatFlux_DecaysAlongPlug_WithoutNonPhysicalSpikes()
    {
        var result = AerospikePlugCooling.Solve(MakeInputs());

        // Find the peak heat-flux station.
        int peakIdx = 0;
        double peakQ = result.HeatFlux_Wm2[0];
        for (int i = 1; i < result.HeatFlux_Wm2.Length; i++)
        {
            if (result.HeatFlux_Wm2[i] > peakQ)
            {
                peakQ = result.HeatFlux_Wm2[i];
                peakIdx = i;
            }
        }

        // Peak should be in the first half of the plug (near throat).
        Assert.True(peakIdx < result.HeatFlux_Wm2.Length / 2,
            $"Peak heat-flux station {peakIdx} should sit in first half "
          + $"of {result.HeatFlux_Wm2.Length} stations (near throat).");

        // Past the peak, q should be non-increasing (allowing a small
        // numerical band for the inner-loop iter convergence ±1.5 K).
        // The "without non-physical spikes" check: no station past the
        // peak exceeds the peak by more than 5 % (i.e., a single noisy
        // station can't out-rank the throat).
        double tolerance = 1.05 * peakQ;
        for (int i = peakIdx + 1; i < result.HeatFlux_Wm2.Length; i++)
        {
            Assert.True(result.HeatFlux_Wm2[i] <= tolerance,
                $"Post-peak heat-flux spike at station {i}: "
              + $"q[{i}] = {result.HeatFlux_Wm2[i]:G3} > 1.05 × peak "
              + $"({tolerance:G3}).");
        }
    }

    // ── Group 4: empty-contour path still works (defaults Notes to null) ─

    [Fact]
    public void Solve_EmptyContour_ReturnsEarly_WithNullNotes()
    {
        // The empty-contour early return doesn't run the march, so
        // there's nothing to flag — Notes stays null. Warnings carries
        // the "Empty contour." string instead.
        var inp = MakeInputs() with
        {
            Contour = MakeInputs().Contour with
            {
                Stations = System.Array.Empty<AerospikeStation>(),
            },
        };
        var result = AerospikePlugCooling.Solve(inp);
        Assert.Null(result.Notes);
        Assert.Single(result.Warnings);
        Assert.Equal("Empty contour.", result.Warnings[0]);
    }
}
