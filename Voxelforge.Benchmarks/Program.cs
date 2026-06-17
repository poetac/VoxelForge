// Voxelforge.Benchmarks — standalone console harness for capturing
// Phase-0 voxel-build + STL-export baselines at a given voxel size. The
// in-process xUnit path (Phase4VoxelBenchmarks) is constrained to the
// 0.4 mm session voxel because PicoGK's Library singleton locks the voxel
// size for the life of the process; this exe is the only clean way to
// capture numbers at 0.20 / 0.10 mm without recycling the main app.
//
// Usage:
//   dotnet run --project Voxelforge.Benchmarks -- \
//     --voxel 0.1 [--repeat 3] [--out baseline.jsonl] [--no-export] [--out-stl chamber.stl]
//
// On stdout: one BENCH block per run (parse with the same Program.ParseBench
// helpers the main app uses), plus a final summary row. On the optional
// JSONL sink: one JSON record per run so cross-session history accumulates.
//
// Writes all intermediates to %TEMP%/regen-benchmarks/ so the working tree
// doesn't get churned by .stl / .obj intermediates during a sweep.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using PicoGK;
using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Geometry;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.IO;
using Voxelforge.Manufacturing;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

public static partial class Program
{
    // SSOT for the subcommand list. `--list-benches` iterates this;
    // each runtime dispatch arm in Main matches a registered flag.
    private static readonly (string Flag, string Description)[] BenchRegistry =
    {
        ("--voxel <mm>",       "Default voxel + STL benchmark (per-record JSONL via --out)."),
        ("--autonomous",       "Spec → engine autonomous CLI (auto-seed + generate + STL)."),
        ("--mega-sweep",       "Meganewton-class envelope sweep (multi-thrust, multi-voxel)."),
        ("--probe-envelope",   "Print MegaScaleEnvelope recommendation for each thrust preset (no build)."),
        ("--aerospike",        "Aerospike / plug-nozzle standalone STL pipeline."),
        ("--turbopump",        "Turbopump geometry generator (single-stage centrifugal)."),
        ("--monolithic",       "Monolithic engine: chamber + turbopump + preburner + manifold."),
        ("--bench-pareto",      "Weekly Pareto frontier characterization (#655): NSGA-II on Isp/mass and cost/mass objective pairs. --design-preset <name> [--seed N] [--population N] [--generations N] [--out path.jsonl]."),
        ("--bench-stl-validation", "Nightly STL topology validation (#657): export + manifold/watertight check for all canonical presets. [--voxel <mm=0.5>] [--out path.jsonl] [--stl-dir dir]."),
        ("--bench-sa",         "BB-2 pre-Sprint-30 SA physics fingerprint capture (CanonicalDesigns). Add --sa-animation-gif <path.gif> for an OA-1 trade-show GIF of best-improvement frames."),
        ("--bench-sa-airbreathing <preset>",
                               "Air-breathing SA physics fingerprint. --preset <mattingly-ramjet|j85-turbojet>"),
        ("--bench-cfd-export",          "BB-3 CFD VTI export bench: --iterations N --grid-nx M [--out path.jsonl]."),
        ("--bench-dual-bell",           "BB-5b dual-bell contour timing: --iterations N --station-count M [--out path.jsonl]."),
        ("--bench-linear-aerospike",    "BB-5b linear-aerospike physics-only timing: --iterations N --thrust N [--out path.jsonl]."),
        ("--bench-diff",                "BB-6 regression check: diff a current bench-sa JSONL against a frozen baseline. Use --pillar <rocket|airbreathing|all> for cross-pillar mode."),
        ("--bench-runtime-audit",       "BB Wave-1 runtime drift audit: scan all dated baselines in a pillar dir, report p50 drift per preset. Use --pillar <rocket|airbreathing> [--baselines-dir <path>] [--out <report.md>]."),
        ("--sobol",                     "OOB-5 Sobol sensitivity indices on the SA design vector."),
        ("--render-preset <name>",      "Render a PNG of a canonical design preset seed (site asset generation). Use --out <path.png> [--voxel <mm>] [--material <name>] [--resolution <low|high|maximum>]."),
        ("--calibrate <csv>",           "OOB-1 MAP calibration: back-solve {CStarEff, CfEff, BartzSF} from a hot-fire CSV. Use --preset <name> [--out <json>] [--write-back <design>] [--verbose]."),
        ("--design-doe",               "OOB-10 DOE sweep: Sobol-sampled feasible designs with predicted observables → CSV. Use [--preset <name>] [--n <count>] [--out <path.csv>]."),
        ("--post-test <csv>",          "OOB-10 post-test: calibrate knobs from measured hot-fire CSV, emit Markdown comparison report. Use [--preset <name>] [--out <report.md>]."),
        ("--sweep",                      "Ad-hoc 1D parameter sweep over any SA design variable or condition (p_c, thrust). Emits CSV + PNG artifact. Use --preset, --variable, --range lo,hi, [--samples N=20], [--objective score|isp|…], [--out path.csv]."),
        ("--list-benches",              "Print this list and exit."),
    };

    public static int Main(string[] args)
    {
        // Subcommand registry — single source of truth. Both the
        // dispatch loop below and the `--list-benches` arm iterate this.
        // Adding a subcommand goes here AND nowhere else (per the
        // BenchRegistry SSOT discipline introduced by Sprint BB
        // pre-cascade — see CLAUDE.md track 7).
        if (args.Length > 0 && args[0] == "--list-benches")
        {
            Console.WriteLine("Registered subcommands:");
            foreach (var (flag, desc) in BenchRegistry)
                Console.WriteLine($"  {flag,-20} {desc}");
            return 0;
        }

        // Ad-hoc 1D parameter sweep (#830). Evaluates N evenly-spaced sample
        // points across a design variable or operating condition in pure-
        // physics mode and emits a CSV + PNG artifact.
        if (args.Length > 0 && args[0] == "--sweep")
            return BenchSweep.Run(args.AsSpan(1).ToArray());

        // Weekly Pareto frontier characterization (#655). Runs NSGA-II on a
        // CanonicalDesigns preset with two objective pairs (Isp/mass and
        // cost/mass) and emits schema-v1 JSONL with Pareto-front points.
        if (args.Length > 0 && args[0] == "--bench-pareto")
            return BenchPareto.Run(args.AsSpan(1).ToArray());

        // Nightly STL topology validation (#657). Exports each canonical preset
        // at 0.5 mm voxel and validates manifold/watertight topology with a
        // pure-managed binary-STL reader. No native dependencies (ADR-024).
        if (args.Length > 0 && args[0] == "--bench-stl-validation")
            return BenchStlValidation.Run(args.AsSpan(1).ToArray());

        // BB-2 (2026-04-24): pre-Sprint-30 SA physics fingerprint capture.
        // Routes to BenchSA which runs SA on a CanonicalDesigns preset
        // and emits schema-v1 JSONL with timing + fingerprint scalars.
        if (args.Length > 0 && args[0] == "--bench-sa")
            return BenchSA.Run(args.AsSpan(1).ToArray());

        // Air-breathing SA bench (Step 1b, 2026-05-03).
        // Runs MultiChainSA on RamjetObjective / TurbojetObjective and emits
        // schema-v1 JSONL under baselines/bench-sa-airbreathing-<preset>-<date>.jsonl.
        if (args.Length > 0 && args[0] == "--bench-sa-airbreathing")
            return BenchSaAirbreathing.Run(args.AsSpan(1).ToArray());

        // BB-3 (2026-04-29): restored CFD VTI export bench. Iterates
        // CfdFieldExport.Write against a canonical bell chamber + 80-
        // station thermal solve; emits schema-v1 JSONL with iteration
        // count, grid Nx, file_bytes, and timing percentiles. The
        // legacy phantom baselines/bench-cfd-export.jsonl is regenerable
        // from this CLI on the same machine within ±5 % noise.
        if (args.Length > 0 && args[0] == "--bench-cfd-export")
            return BenchCfdExport.Run(args.AsSpan(1).ToArray());

        // BB-5b (2026-04-29): dual-bell contour generation timing.
        // Times ChamberContourGenerator.Generate with dualBell: true vs
        // the classic single-bell path on a canonical LOX/CH4 10 kN
        // chamber; emits schema-v1 JSONL. No voxels — PicoGK-free.
        if (args.Length > 0 && args[0] == "--bench-dual-bell")
            return BenchDualBell.Run(args.AsSpan(1).ToArray());

        // BB-5b (2026-04-29): linear-aerospike physics-only timing.
        // Times AerospikeBuilder.BuildLinearPhysicsOnly on an X-33 /
        // XRS-2200-class LOX/CH4 20 kN spec (IsLinear=true); emits
        // schema-v1 JSONL. No voxels — PicoGK-free.
        if (args.Length > 0 && args[0] == "--bench-linear-aerospike")
            return BenchLinearAerospike.Run(args.AsSpan(1).ToArray());

        // BB-6 (2026-04-25): regression check. Compares a current
        // bench-sa JSONL against a frozen baseline at a configurable
        // threshold; the bench-regression.yml workflow consumes the
        // exit code to gate PRs that drift beyond tolerance.
        // Sprint B.2: added --pillar cross-pillar mode.
        if (args.Length > 0 && args[0] == "--bench-diff")
            return BenchDiff.Run(args.AsSpan(1).ToArray());

        // Sprint B.3 (BB Wave 1): runtime drift audit. Reads all dated
        // JSONL baselines in a pillar dir, groups by preset, and reports
        // per_iter_p50_us drift from oldest to latest baseline.
        if (args.Length > 0 && args[0] == "--bench-runtime-audit")
            return BenchRuntimeAudit.Run(args.AsSpan(1).ToArray());

        // OOB-5 (2026-04-25): Sobol sensitivity indices on the SA
        // design vector. Identifies which of the 24 SA dimensions
        // actually move scoring, enabling SA-band tightening or
        // dimension freezing in a future sprint.
        if (args.Length > 0 && args[0] == "--sobol")
            return SobolSensitivityCli.Run(args.AsSpan(1).ToArray());

        // Site asset render helper (2026-05-01): renders a single PNG of a
        // canonical design preset seed without running SA. Reuses
        // SubprocessFrameRenderer so the same STL-export + voxelforge-render
        // pipeline exercised by the SA animation GIF is used end-to-end.
        if (args.Length > 0 && args[0] == "--render-preset")
            return BenchRenderPreset.Run(args.AsSpan(1).ToArray());

        // OOB-1 Sprint 2 (2026-05-01): hot-fire CSV → MAP calibration.
        // Reads a MeasuredDataOverlay CSV, builds a headless physics runner
        // from a CanonicalDesigns preset, and runs CalibrationPosterior.Calibrate
        // to back-solve {CStarEfficiency, NozzleCfEfficiency, BartzScalingFactor}.
        if (args.Length > 0 && args[0] == "--calibrate")
            return BenchCalibrate.Run(args.AsSpan(1).ToArray());

        // OOB-10: Sobol-sampled DOE plan → CSV.
        if (args.Length > 0 && args[0] == "--design-doe")
            return BenchDesignDoe.Run(args.AsSpan(1).ToArray());

        // OOB-10: post-test measured CSV → calibrate + Markdown report.
        if (args.Length > 0 && args[0] == "--post-test")
            return BenchPostTest.Run(args.AsSpan(1).ToArray());

        // "Spec → engine" autonomous CLI.
        // `--autonomous` dispatches before the benchmark arg parser so
        // users can run the full auto-seed → generate → STL path without
        // juggling --voxel etc. Benchmark mode stays the legacy default.
        if (args.Length > 0 && args[0] == "--autonomous")
            return RunAutonomous(args.AsSpan(1).ToArray());

        // Meganewton-class baseline sweep.
        // Runs a curated set of (thrust, voxel, tiles) configurations
        // and writes a JSONL baseline file for cross-session history.
        // Pre-flight uses MegaScaleEnvelope.Recommend against the
        // user's budget so sweep points are feasible on the machine.
        if (args.Length > 0 && args[0] == "--mega-sweep")
            return RunMegaSweep(args.AsSpan(1).ToArray());

        // Pure envelope probe — no voxel build.
        // Prints the MegaScaleEnvelope recommendation for each thrust
        // preset at the specified budget. Use to plan hardware before
        // committing to a multi-hour sweep.
        if (args.Length > 0 && args[0] == "--probe-envelope")
            return RunEnvelopeProbe(args.AsSpan(1).ToArray());

        // Aerospike / plug-nozzle geometry pipeline. Bypasses the
        // regen chamber/nozzle stack and dispatches to
        // AerospikeBuilder for a standalone STL. Regen integration
        // (thermal solver, cooling channels on the plug, feasibility
        // gate adaptations) was delivered in later follow-ups.
        if (args.Length > 0 && args[0] == "--aerospike")
            return RunAerospike(args.AsSpan(1).ToArray());

        // Turbopump geometry generator.
        // Takes a sized `PumpSizing` + produces a single-stage
        // centrifugal turbopump STL (inducer + impeller + volute +
        // casing). Exists as a standalone CLI so a pump can be
        // previewed / printed independently of the chamber; the
        // GenerateWith path attaches `TurbopumpGeometry` to
        // `result.Turbopump` when `cond.IncludeTurbopumpGeometry` is
        // true.
        if (args.Length > 0 && args[0] == "--turbopump")
            return RunTurbopump(args.AsSpan(1).ToArray());

        // **Monolithic engine capstone.**
        // Composes chamber + turbopump + preburner + feed manifold
        // into one voxel body + one STL. Delivers the
        // "functionally-integrated part that doesn't require
        // assembly" headline. Takes the same 4-input spec as
        // --autonomous plus a cycle override.
        if (args.Length > 0 && args[0] == "--monolithic")
            return RunMonolithic(args.AsSpan(1).ToArray());

        BenchArgs cli;
        try { cli = BenchArgs.Parse(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(BenchArgs.UsageLine);
            return 3;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "regen-benchmarks");
        Directory.CreateDirectory(tempRoot);
        string stlOutPath = cli.OutStlPath ?? Path.Combine(
            tempRoot, $"chamber_{cli.VoxelSizeMM:F3}mm.stl");

        Console.WriteLine($"# Voxelforge.Benchmarks — voxel={cli.VoxelSizeMM:F3} mm, "
                        + $"repeat={cli.Repeat}, tiles={cli.Tiles}, export={(cli.NoExport ? "off" : "on")}");
        Console.WriteLine($"# Temp workspace: {tempRoot}");
        if (cli.JsonlOutPath != null)
            Console.WriteLine($"# JSONL history: {cli.JsonlOutPath}");

        var cond   = new OperatingConditions();       // defaults: 2224 N LOX/CH4 at 6.9 MPa
        var design = new RegenChamberDesign();        // defaults: manifolds + ports + flanges

        // One PicoGK Library per process — locked at cli.VoxelSizeMM.
        try
        {
            using var lib = new Library(cli.VoxelSizeMM);

            // Warm the JIT + any lookup caches. One throwaway run before we
            // stopwatch anything so the first timed run isn't inflated by
            // first-call compilation and propellant-table init.
            Console.WriteLine("# Warm-up run (not recorded)…");
            _ = RegenChamberOptimization.GenerateWith(cond, design, voxelSize_mm: cli.VoxelSizeMM);

            // When --tiles ≥ 2, dispatch the
            // axial-tiled path. Peak memory ≈ 1/N of monolithic.
            if (cli.Tiles >= 2)
            {
                return RunTiled(cond, design, cli, stlOutPath);
            }

            var records = new List<RunRecord>();
            for (int iter = 0; iter < cli.Repeat; iter++)
            {
                Console.WriteLine($"# ── Run {iter + 1} / {cli.Repeat} ────────────────────");
                var rec = RunOne(cond, design, cli.VoxelSizeMM, cli.NoExport ? null : stlOutPath);
                records.Add(rec);
                rec.EmitBench(Console.Out);
                if (cli.JsonlOutPath != null) rec.AppendJsonl(cli.JsonlOutPath);
            }

            Console.WriteLine("# ── Summary (median of runs) ───────────────────");
            EmitMedianSummary(records, Console.Out);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Benchmark failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
