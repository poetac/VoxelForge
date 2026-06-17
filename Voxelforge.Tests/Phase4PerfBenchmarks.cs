// Phase4PerfBenchmarks.cs — Wall-clock regression guards for the
// perf-optimisation work.
//
// These act as benchmark-style tests that assert the thermal solve
// completes in under a generous ceiling. Each ceiling is set to
// roughly 2× the typical runtime on a modern dev machine so they
// catch egregious regressions (e.g. a quadratic-cost rewrite, a
// bypassed cache, an accidentally-disabled parallel path) without
// being flaky on slow CI hardware.
//
// Each test prints its measured wall clock in the assertion message,
// so a developer running `dotnet test --logger "console;verbosity=detailed"`
// can see the current numbers and compare to historical baselines.
//
// Sized for the full suite to add < 5 s to the existing 26 s budget.
//
// To capture fresh baseline numbers: bump verbosity, run the test
// class, and read the printed measurements off the assertion text.

using System.Diagnostics;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Xunit.Abstractions;

namespace Voxelforge.Tests;

public class Phase4PerfBenchmarks
{
    private readonly ITestOutputHelper _out;
    public Phase4PerfBenchmarks(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Soft ceiling on a single cold-cache `RegenCoolingSolver.Solve`
    /// for the default 500 N LOX/CH4 design at 80 stations. Typical
    /// runtime on a modern dev machine: 30-80 ms. Ceiling
    /// at 500 ms catches a 6× regression. Bump if a future change
    /// genuinely raises the floor (e.g. higher RadialWallNodes default).
    /// </summary>
    private const long ThermalSolveCeiling_ms = 500;

    /// <summary>
    /// Soft ceiling on a 100-sample `ToleranceAnalysis.Run`
    /// parallelisation. Typical runtime on an 8-core machine: 200-600 ms.
    /// Ceiling at 4 s catches a 6-7× regression (≈ accidentally falling
    /// back to sequential).
    /// </summary>
    private const long ToleranceSweep_100Samples_Ceiling_ms = 4_000;

    /// <summary>
    /// Soft ceiling on 8 sequential `GenerateWith(skipVoxelGeometry: true)`
    /// physics-only candidate evaluations — proxies an SA batch of 8
    /// without invoking the real Parallel.For (which xUnit can't easily
    /// stop-watch in a portable way without inflating the budget for
    /// thread-pool warmup). Typical runtime: 250-800 ms.
    /// </summary>
    private const long EightSACandidates_Ceiling_ms = 4_000;

    [Fact]
    public void Bench_ThermalSolve_CompletesUnderSoftCeiling()
    {
        var (cond, design, contour, solverInputs) = MakeSolverInputs(stationCount: 80);

        // Warm JIT + table caches with a throwaway run.
        _ = RegenCoolingSolver.Solve(solverInputs);

        var sw = Stopwatch.StartNew();
        var result = RegenCoolingSolver.Solve(solverInputs);
        sw.Stop();

        _out.WriteLine($"BENCH thermal_solve_80stations_ms = {sw.ElapsedMilliseconds}");
        Assert.True(sw.ElapsedMilliseconds < ThermalSolveCeiling_ms,
            $"Thermal solve took {sw.ElapsedMilliseconds} ms (ceiling {ThermalSolveCeiling_ms} ms). "
            + $"Inspect the coolant cache and buffer reuse paths.");
        Assert.True(result.PeakGasSideWallT_K > 0, "Solve produced no wall-T result — invalid baseline.");
    }

    [Fact]
    public void Bench_ToleranceSweep_100Samples_CompletesUnderSoftCeiling()
    {
        var (cond, design, contour, _) = MakeSolverInputs(stationCount: 60);
        var inp = new ToleranceInputs(SampleCount: 100, RandomSeed: 1);

        // Warm JIT + threadpool — first Parallel.For pays a one-time
        // worker-thread spinup that can dominate small workloads.
        _ = ToleranceAnalysis.Run(contour, cond, design, inp);

        var sw = Stopwatch.StartNew();
        var r = ToleranceAnalysis.Run(contour, cond, design, inp);
        sw.Stop();

        _out.WriteLine($"BENCH tolerance_sweep_100samples_ms = {sw.ElapsedMilliseconds}  "
                     + $"(per-sample mean {r.MeanComputeTime_ms:F2} ms; total / wall ratio "
                     + $"{r.MeanComputeTime_ms * 100.0 / System.Math.Max(sw.ElapsedMilliseconds, 1):F1} "
                     + $"≈ effective core count saturation)");
        Assert.True(sw.ElapsedMilliseconds < ToleranceSweep_100Samples_Ceiling_ms,
            $"Tolerance sweep (N=100) took {sw.ElapsedMilliseconds} ms "
            + $"(ceiling {ToleranceSweep_100Samples_Ceiling_ms} ms). "
            + $"Inspect the Parallel.For + per-iter RNG path.");
        Assert.True(double.IsFinite(r.PeakWallT_K.P50), "Sweep produced non-finite quantile — invalid run.");
    }

    [Fact]
    public void Bench_EightPhysicsOnlyCandidates_CompleteUnderSoftCeiling()
    {
        // Proxies one 8-batch step of the parallel SA path. Run them
        // sequentially here (xUnit-friendly, deterministic) — the real
        // SA path uses Parallel.For which would just be faster.
        var (cond, design, _, _) = MakeSolverInputs(stationCount: 80);

        // Warm JIT + caches.
        _ = RegenChamberOptimization.GenerateWith(cond, design,
            skipVoxelGeometry: true, skipMfgAnalysis: true);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 8; i++)
        {
            // Tiny perturbation per iteration to defeat any spurious
            // result-equality short-circuit and to mimic the SA candidate
            // search more faithfully.
            var d = design with { ChannelCount = design.ChannelCount + i };
            var g = RegenChamberOptimization.GenerateWith(cond, d,
                skipVoxelGeometry: true, skipMfgAnalysis: true);
            // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
            var s = RegenChamberOptimization.Evaluate(g, RegenChamberOptimization.Profiles[0]);
            Assert.True(double.IsFinite(s.TotalScore) || double.IsPositiveInfinity(s.TotalScore));
        }
        sw.Stop();

        _out.WriteLine($"BENCH eight_sa_candidates_skipMfg_ms = {sw.ElapsedMilliseconds}  "
                     + $"(per-candidate {sw.ElapsedMilliseconds / 8.0:F1} ms)");
        Assert.True(sw.ElapsedMilliseconds < EightSACandidates_Ceiling_ms,
            $"8 physics-only SA candidates took {sw.ElapsedMilliseconds} ms "
            + $"(ceiling {EightSACandidates_Ceiling_ms} ms). "
            + $"Inspect cache + skip wiring on the SA hot path.");
    }

    [Fact]
    public void Bench_PropellantTablesLookup_CacheHitsAreEffectivelyFree()
    {
        // Cache sanity guard: a million Lookup hits with the same
        // (pair, MR, Pc) should be effectively free thanks to the
        // ConcurrentDictionary memoizer (single GetOrAdd entry, all
        // subsequent calls hit). Pre-cache-miss this would have been
        // ~10s of table interpolation work; any ceiling well under
        // that detects a cache bypass.
        //
        // Ceiling history:
        //   Original ceiling: 200 ms (dev-box headroom).
        //   Raised to 500 ms once the suite grew past 800 tests
        //   running in parallel collections. Measured dev-box still
        //   ~80 ms / 1e6 cached hits when run solo; the ceiling is a
        //   cache-bypass detector, not an SLO.
        // Warm:
        _ = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1_000_000; i++)
        {
            var s = PropellantTables.Lookup(PropellantPair.LOX_CH4, 3.3, 6.9e6);
            // Touch a field so the JIT can't fold the call away.
            Assert.True(s.ChamberTemp_K > 0);
        }
        sw.Stop();

        _out.WriteLine($"BENCH lookup_cache_1M_hits_ms = {sw.ElapsedMilliseconds}  "
                     + $"({sw.ElapsedMilliseconds * 1000.0 / 1_000_000.0:F2} ns / hit)");
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"1e6 cached Lookup hits took {sw.ElapsedMilliseconds} ms "
            + $"(ceiling 500 ms). Cache may be bypassed.");
    }

    // ═════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════

    private static (OperatingConditions cond, RegenChamberDesign design,
                    ChamberContour contour, RegenSolverInputs inputs)
        MakeSolverInputs(int stationCount)
    {
        var cond = new OperatingConditions
        {
            PropellantPair = PropellantPair.LOX_CH4,
        };
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false, IncludePorts = false,
            IncludeInjectorFlange = false, ContourStationCount = stationCount,
        };
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm:        derived.ThroatRadius_mm,
            contractionRatio:       design.ContractionRatio,
            expansionRatio:         design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            stationCount:           stationCount);
        var material = WallMaterials.All[
            System.Math.Clamp(cond.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        var pairMeta = PropellantPairs.GetMeta(cond.PropellantPair);
        var fluid = CoolantRegistry.Get(pairMeta.CoolantFluidKey);
        var channels = new ChannelSchedule(
            ChannelCount: design.ChannelCount,
            RibThickness_mm: design.RibThickness_mm,
            GasSideWallThickness_mm: design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm: design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm: design.ChannelHeightExit_mm);
        var inputs = new RegenSolverInputs(
            Contour: contour, Gas: gas, Wall: material, Channels: channels,
            CoolantMassFlow_kgs: derived.FuelMassFlow_kgs,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            CoolantFluid: fluid);
        return (cond, design, contour, inputs);
    }
}
