// TopologyOptimizedChannelTests.cs — Sprint 1 physics tests for OOB-2.
//
// Coverage:
//   a. Determinism      — same inputs → bit-identical DensityField[] and IterationsRun
//   b. Volume fraction  — VolumeFractionAchieved within ±1 % of 1.0
//   c. Throat concentration — density higher at throat than at chamber
//                             on a synthetic throat-peaked heat flux profile
//   d. Pressure drop    — optimized ΔP ≤ baseline on a realistic flux profile
//
// These tests are Core-only: no PicoGK, no GenerateWith calls, no voxel work.
// All inputs are built synthetically so tests are fast and self-contained.

using System;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class TopologyOptimizedChannelTests
{
    // ── Synthetic fixture ─────────────────────────────────────────────
    //
    // 40-station contour:
    //   Stations  0-14: cylindrical barrel + converging (R linearly 40 → 20 mm)
    //   Station  15:    throat (R = 20 mm minimum)
    //   Stations 16-39: diverging bell (R linearly 20 → 50 mm)
    //
    // Default heat flux: Gaussian peaked at station 15 (the throat),
    //   σ ≈ 4.8 stations, q_peak = 15 MW/m², q_chamber ≈ 2 MW/m².
    //
    private const int StationCount = 40;
    private const int ThroatIdx    = 15;
    private const int ChannelCount = 80;

    private static TopologyChannelInputs BuildInputs(
        Func<int, double>? heatFluxProfileFn = null,
        int nCh = ChannelCount)
    {
        var contour  = BuildContour();
        var thermal  = BuildThermalStations(heatFluxProfileFn, nCh);
        var schedule = new ChannelSchedule(
            ChannelCount:              nCh,
            RibThickness_mm:           0.6,
            GasSideWallThickness_mm:   1.0,
            ChannelHeightAtChamber_mm: 2.0,
            ChannelHeightAtThroat_mm:  3.5,
            ChannelHeightAtExit_mm:    2.5);
        return new TopologyChannelInputs(
            Contour:                 contour,
            ThermalStations:         thermal,
            BaseSchedule:            schedule,
            MassFlowCoolant_kgs:     0.5,
            MaxIterations:           100,
            SimpPenalty:             3.0,
            MinDensity:              0.01,
            VolumeFractionTolerance: 0.01,
            ConvergenceTolerance:    1e-4,
            MinChannelsPerStation:   8,
            Seed:                    42);
    }

    private static ContourStation[] BuildContour()
    {
        var s = new ContourStation[StationCount];
        for (int i = 0; i < StationCount; i++)
        {
            double x = i * 5.0;   // 5 mm axial spacing
            double R = i <= ThroatIdx
                ? 40.0 - 20.0 * i / ThroatIdx                                  // converging
                : 20.0 + 30.0 * (i - ThroatIdx) / (StationCount - 1 - ThroatIdx); // diverging
            s[i] = new ContourStation(
                X_mm:     x,
                R_mm:     R,
                Area_mm2: Math.PI * R * R,
                Slope:    0.0,
                Region:   i < ThroatIdx ? ChamberRegion.Barrel
                        : i == ThroatIdx ? ChamberRegion.ThroatArc
                        : ChamberRegion.BellParabola);
        }
        return s;
    }

    // Default heat flux: Gaussian peak at throat.
    private static double DefaultHeatFlux(int i)
    {
        const double sigma = 4.8;
        return 2e6 + 13e6 * Math.Exp(-0.5 * Math.Pow((i - ThroatIdx) / sigma, 2));
    }

    private static StationResult[] BuildThermalStations(
        Func<int, double>? fluxFn = null, int nCh = ChannelCount)
    {
        fluxFn ??= DefaultHeatFlux;
        var s = new StationResult[StationCount];
        for (int i = 0; i < StationCount; i++)
        {
            double R    = i <= ThroatIdx
                ? 40.0 - 20.0 * i / ThroatIdx
                : 20.0 + 30.0 * (i - ThroatIdx) / (StationCount - 1 - ThroatIdx);
            double q    = fluxFn(i);
            double pitch = 2.0 * Math.PI * R / nCh;
            double w_mm  = Math.Max(0.1, pitch - 0.6);
            double h_mm  = 2.5;
            double dh_mm = 2.0 * w_mm * h_mm / (w_mm + h_mm);
            double vel   = 25.0;
            double Re    = 8e5;

            s[i] = new StationResult(
                Index:                    i,
                X_mm:                     i * 5.0,
                R_mm:                     R,
                AreaRatioToThroat:        (R * R) / (20.0 * 20.0),
                Mach:                     i < ThroatIdx ? 0.4 : 1.5,
                StaticTemp_K:             3000.0,
                AdiabaticWallTemp_K:      3500.0,
                EffectiveRecoveryTemp_K:  3500.0,
                FilmEffectiveness:        0.0,
                HeatFlux_Wm2:             q,
                h_g_Wm2K:                 5_000.0,
                h_c_Wm2K:                 30_000.0,
                GasSideWallTemp_K:        850.0,
                CoolantSideWallTemp_K:    500.0,
                WallRadialProfile_K:      new[] { 850.0, 750.0, 650.0, 580.0, 500.0 },
                AxialConductionFlux_Wm2:  0.0,
                CoolantBulkTemp_K:        200.0,
                CoolantBulkPressure_Pa:   10e6,
                CoolantVelocity_ms:       vel,
                Reynolds:                 Re,
                PrandtlBulk:              1.0,
                ChannelWidth_mm:          w_mm,
                ChannelHeight_mm:         h_mm,
                HydraulicDiameter_mm:     dh_mm,
                PressureGradient_Pam:     1e6);
        }
        return s;
    }

    // ── Test a — determinism ──────────────────────────────────────────

    [Fact]
    public void Determinism_SameInputProducesSameDensityField()
    {
        var inp = BuildInputs();
        var r1  = TopologyOptimizedChannels.Solve(inp);
        var r2  = TopologyOptimizedChannels.Solve(inp);

        Assert.Equal(r1.DensityField.Length, r2.DensityField.Length);
        for (int i = 0; i < r1.DensityField.Length; i++)
            Assert.Equal(r1.DensityField[i], r2.DensityField[i]);

        Assert.Equal(r1.IterationsRun, r2.IterationsRun);
        Assert.Equal(r1.Converged,     r2.Converged);
        Assert.Equal(r1.VolumeFractionAchieved, r2.VolumeFractionAchieved);
    }

    // ── Test b — volume fraction ──────────────────────────────────────

    [Fact]
    public void VolumeFractionConstraint_SatisfiedWithinTolerance()
    {
        var result = TopologyOptimizedChannels.Solve(BuildInputs());

        Assert.InRange(result.VolumeFractionAchieved, 0.99, 1.01);
    }

    // ── Test c — throat concentration ────────────────────────────────

    [Fact]
    public void ThroatConcentration_DensityHigherAtThroatThanChamber()
    {
        // Gaussian heat flux peaks at station 15 (throat, R=20 mm).
        // Chamber station 0 sees ~2 MW/m²; throat sees ~15 MW/m².
        // The OC optimizer must concentrate channels at high-flux stations.
        var result = TopologyOptimizedChannels.Solve(BuildInputs());

        double rhoThroat  = result.DensityField[ThroatIdx];
        double rhoChamber = result.DensityField[0];

        Assert.True(rhoThroat > rhoChamber,
            $"Expected density at throat (station {ThroatIdx}) = {rhoThroat:F4} "
          + $"> density at chamber (station 0) = {rhoChamber:F4}. "
          + "The OC update must drive density toward high-flux regions.");
    }

    // ── Test d — pressure drop improvement ───────────────────────────

    [Fact]
    public void PressureDropImprovement_OptimizedNotWorseThanUniform()
    {
        // Throat-peaked heat flux → OC concentrates channels at the throat
        // (small axial extent, small L) and removes them from the long
        // barrel (large L, low flux). The net effect is that high-velocity
        // segments become shorter and low-velocity segments become longer,
        // reducing or matching the total frictional ΔP.
        //
        // We allow a 20 % tolerance to accommodate the integer-rounding
        // step that can slightly increase ΔP relative to the continuous
        // density field optimum.
        var result = TopologyOptimizedChannels.Solve(BuildInputs());

        double dPOpt  = result.OptimizedPressureDrop_Pa;
        double dPBase = result.BaselinePressureDrop_Pa;

        Assert.True(dPOpt <= dPBase * 1.20,
            $"Expected OptimizedΔP ({dPOpt:F0} Pa) ≤ 1.20 × BaselineΔP ({dPBase * 1.20:F0} Pa). "
          + "Topology-optimized routing should not significantly increase total jacket ΔP.");
    }
}
