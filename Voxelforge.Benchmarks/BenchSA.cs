// `--bench-sa` — pre-Sprint-30 SA physics fingerprint capture.
//
// Runs the Simulated Annealing optimizer on a canonical preset
// design (CanonicalDesigns) in pure-physics mode (no voxel build,
// keep mfg analysis for fingerprint completeness), records per-
// iteration timing, and emits a schema-v1 JSONL record per repeat.
//
// The captured baselines under
// `Voxelforge.Benchmarks/baselines/bench-sa-<preset>-<date>.jsonl`
// are FROZEN reference values. Sprints 30-37 will shift the
// physics-fingerprint scalars by 10-30 % per design; the post-
// cascade diff against this snapshot IS the cascade's measured
// impact. See ADR-013.
//
// CLI:
//   --bench-sa --seed <int=42> --iterations <N=2000>
//              --design-preset <merlin|rl10|pressure-fed-small|aerospike|pintle>
//              --repeat <N=3>
//              [--out <jsonl>]

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Benchmarks;

internal static partial class BenchSA
{
    // Phase-1 diagnostic: when set, every infeasible candidate dumps its
    // FeasibilityViolations[].ConstraintId so we can build the per-preset
    // gate-firing histogram. Set via --dump-violations.
    private static bool s_dumpViolations = false;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> s_violationCounts = new();
    private static int s_totalCandidates = 0;

    // Sprint feasibility-audit-H3 (2026-04-27): per-candidate JSONL
    // trace for diagnosing 99 % gate-firing patterns. Set via
    // --dump-sa-trace <path>. Each line contains:
    //   { "iter": N, "feasible": bool, "score": number|null,
    //     "violations": [{"gate":..., "actual":..., "limit":...}, ...],
    //     "design": [26-element double array of SA-vector values],
    //     "scalars": { "peak_wall_t_k":..., "min_sf":...,
    //                  "blockage":..., "expander_avail_kw":...,
    //                  "expander_req_kw":..., "coolant_dp_pa":... } }
    // Designed for grep / jq filtering: e.g. `jq 'select(.violations | any(.gate == "PINTLE_BLOCKAGE_OUT_OF_BAND"))'`
    // shows every candidate that fired the pintle gate, with full
    // design context. Lets you correlate SA-dim values with which
    // gates fire, which is the missing link for diagnosing why the
    // pintle preset's gate fires 99 % when the seed passes.
    private static System.IO.StreamWriter? s_traceWriter = null;
    private static readonly object s_traceLock = new();
    private static long s_traceIter = 0;

    public const string UsageLine =
        "Usage: Voxelforge.Benchmarks --bench-sa "
      + "--design-preset <merlin|rl10|pressure-fed-small|aerospike|pintle> "
      + "[--seed <int=42>] [--iterations <N=2000>] [--repeat <N=3>] "
      + "[--multi-chain] [--chains <N=auto>] [--no-infeasible-exit] "
      + "[--out <jsonl>] [--dump-violations] [--dump-sa-trace <path>] "
      + "[--sa-animation-gif <out.gif> [--sa-animation-voxel <mm=0.5>] "
      + "[--sa-animation-material <slug=copper>] "
      + "[--sa-animation-resolution <low|high|maximum=low>] "
      + "[--sa-animation-frame-delay-ms <N=500>] "
      + "[--sa-animation-hold-last-ms <N=2500>]]";

    public static int Run(string[] args)
    {
        // Sprint feasibility-audit-4 (2026-04-26): --synthetic <obj> bypasses
        // the canonical-preset path and runs SA on a textbook test problem
        // with a known-feasible minimum. Lets us validate optimizer-quality
        // experiments (multi-chain, CMA-ES, perturbation tuning) without
        // depending on the canonical presets — which all return 0 feasible
        // candidates per ADR-018.
        for (int j = 0; j < args.Length; j++)
        {
            if (args[j] == "--synthetic")
                return BenchSyntheticObjective.Run(args);
        }

        int seed = 42;
        int iterations = 2000;
        int repeat = 3;
        string? presetName = null;
        string? outPath = null;
        bool useMultiChain = false;
        int chainCount = 0;   // 0 = auto
        bool disableInfeasibleExit = false;
        // OA-1 (#287): SA-animation GIF capture. Off unless --sa-animation-gif
        // is provided; defaults below match the kiosk-friendly preset (low-
        // resolution Eevee Next render at 0.5 mm voxel for cheap frames).
        string? saAnimGifPath        = null;
        double  saAnimVoxel_mm       = 0.5;
        string  saAnimMaterial       = "copper";
        string  saAnimResolution     = "low";
        int     saAnimFrameDelayMs   = 500;
        int     saAnimHoldLastMs     = 2500;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--seed missing value"); return 3; }
                    if (!int.TryParse(args[++i], out seed))
                    { Console.Error.WriteLine($"--seed must be int, got '{args[i]}'"); return 3; }
                    break;
                case "--iterations":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--iterations missing value"); return 3; }
                    if (!int.TryParse(args[++i], out iterations) || iterations < 1 || iterations > 100_000)
                    { Console.Error.WriteLine($"--iterations must be 1..100000, got '{args[i]}'"); return 3; }
                    break;
                case "--repeat":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--repeat missing value"); return 3; }
                    if (!int.TryParse(args[++i], out repeat) || repeat < 1 || repeat > 100)
                    { Console.Error.WriteLine($"--repeat must be 1..100, got '{args[i]}'"); return 3; }
                    break;
                case "--design-preset":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--design-preset missing value"); return 3; }
                    presetName = args[++i];
                    break;
                case "--out":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--out missing value"); return 3; }
                    outPath = args[++i];
                    break;
                case "--multi-chain":
                    useMultiChain = true;
                    break;
                case "--chains":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--chains missing value"); return 3; }
                    if (!int.TryParse(args[++i], out chainCount) || chainCount < 0)
                    { Console.Error.WriteLine($"--chains must be ≥ 0, got '{args[i]}'"); return 3; }
                    useMultiChain = true;   // implicit
                    break;
                case "--no-infeasible-exit":
                    disableInfeasibleExit = true;
                    break;
                case "--dump-violations":
                    s_dumpViolations = true;
                    break;
                case "--dump-sa-trace":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--dump-sa-trace missing path"); return 3; }
                    {
                        string traceP = args[++i];
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(traceP)) ?? ".");
                        s_traceWriter = new StreamWriter(traceP, append: false);
                    }
                    break;
                case "--sa-animation-gif":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--sa-animation-gif missing path"); return 3; }
                    saAnimGifPath = args[++i];
                    break;
                case "--sa-animation-voxel":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--sa-animation-voxel missing value"); return 3; }
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out saAnimVoxel_mm)
                        || saAnimVoxel_mm < 0.05 || saAnimVoxel_mm > 2.0)
                    { Console.Error.WriteLine($"--sa-animation-voxel must be 0.05–2.0 mm, got '{args[i]}'"); return 3; }
                    break;
                case "--sa-animation-material":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--sa-animation-material missing value"); return 3; }
                    saAnimMaterial = args[++i];
                    break;
                case "--sa-animation-resolution":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--sa-animation-resolution missing value"); return 3; }
                    saAnimResolution = args[++i];
                    break;
                case "--sa-animation-frame-delay-ms":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--sa-animation-frame-delay-ms missing value"); return 3; }
                    if (!int.TryParse(args[++i], out saAnimFrameDelayMs) || saAnimFrameDelayMs < 20 || saAnimFrameDelayMs > 10_000)
                    { Console.Error.WriteLine($"--sa-animation-frame-delay-ms must be 20..10000, got '{args[i]}'"); return 3; }
                    break;
                case "--sa-animation-hold-last-ms":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("--sa-animation-hold-last-ms missing value"); return 3; }
                    if (!int.TryParse(args[++i], out saAnimHoldLastMs) || saAnimHoldLastMs < 0 || saAnimHoldLastMs > 60_000)
                    { Console.Error.WriteLine($"--sa-animation-hold-last-ms must be 0..60000, got '{args[i]}'"); return 3; }
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(UsageLine);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown arg '{args[i]}'");
                    Console.Error.WriteLine(UsageLine);
                    return 3;
            }
        }

        if (presetName == null)
        {
            Console.Error.WriteLine("Missing required --design-preset.");
            Console.Error.WriteLine(UsageLine);
            return 3;
        }

        CanonicalDesigns.Preset preset;
        try { preset = CanonicalDesigns.Get(presetName); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 3; }

        // Phase-1 diagnostic: when --dump-violations is set we ALSO emit a
        // detailed magnitude breakdown for the seed (preflight) candidate
        // before SA runs, so we can see HOW FAR each gate is from feasible
        // (not just which ones fire). Helps discriminate "real physics
        // overload" from "gate threshold off by a few %".
        if (s_dumpViolations)
        {
            Console.WriteLine("# === seed-preflight violation magnitudes ===");
            PropellantTables.UseEquilibrium = preset.Seed.UseEquilibriumRecommended;
            var sg = RegenChamberOptimization.GenerateWith(
                preset.Seed.Conditions, preset.Seed.Design,
                skipVoxelGeometry: true, skipMfgAnalysis: true);
            // #551: Evaluate now takes explicit profile; default Profiles[0] preserves prior static-state behavior.
            var ss = RegenChamberOptimization.Evaluate(sg, RegenChamberOptimization.Profiles[0]);
            foreach (var v in ss.FeasibilityViolations)
            {
                double over = v.Limit > 0 ? 100.0 * (v.ActualValue - v.Limit) / v.Limit : 0;
                Console.WriteLine($"PREFLIGHT  preset={preset.Name}  gate={v.ConstraintId}  actual={v.ActualValue:F2}  limit={v.Limit:F2}  pct_over={over:F1}");
                Console.WriteLine($"PREFLIGHT_DESC  {v.Description}");
            }
            // Stability diagnostic: always emit the composite reason + mode frequencies
            if (sg.Stability is { } stab)
            {
                Console.WriteLine($"PREFLIGHT_STABILITY  chug_ratio={stab.Chug.DeltaPRatio:P1}  chug_rating={stab.Chug.Rating}");
                Console.WriteLine($"PREFLIGHT_STABILITY  L1_hz={stab.Screech.L1_Hz:F0}  T1_hz={stab.Screech.T1_Hz:F0}  T2_hz={stab.Screech.T2_Hz:F0}");
                Console.WriteLine($"PREFLIGHT_STABILITY  composite={stab.Composite}  reason={stab.CompositeReason}");
            }
            Console.WriteLine($"# === seed scalars ===");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  peak_wall_t_k={ss.PeakWallT_K:F1}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  coolant_t_out_k={ss.CoolantTOut_K:F1}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  coolant_dp_pa={ss.CoolantDP_Pa:F0}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  mass_g={ss.Mass_g:F1}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  min_safety_factor={ss.MinSafetyFactor:F3}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  fuel_mdot_kgs={sg.Derived.FuelMassFlow_kgs:F3}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  ox_mdot_kgs={sg.Derived.OxidizerMassFlow_kgs:F3}");
            if (sg.InjectorSizing is { } sz)
            {
                double mDotTotal = sg.Derived.OxidizerMassFlow_kgs + sg.Derived.FuelMassFlow_kgs;
                double area_m2 = (sz.TotalOxArea_mm2 + sz.TotalFuelArea_mm2) * 1e-6;
                double Ginj = area_m2 > 0 ? mDotTotal / area_m2 : 0;
                Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  G_inj_kgPm2s={Ginj:F0}");
                Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  inj_total_area_mm2={(sz.TotalOxArea_mm2 + sz.TotalFuelArea_mm2):F2}");
                Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  inj_element_count={sz.ElementCount}");
            }
            if (sg.InjectorFace is { } face)
                Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  injector_face_t_k={face.TFace_K:F1}");
            Console.WriteLine($"PREFLIGHT_SCALAR  preset={preset.Name}  film_enabled={preset.Seed.Design.FilmCooling.Enabled}  film_frac={preset.Seed.Design.FilmCooling.FuelFractionAsFilm:F3}");

            // Per-station thermal dump for the WALL_TEMP investigation. Shows
            // where in the chamber peak heat flux + peak T_wg occur, so we can
            // discriminate "Bartz over-predicts heat flux" from "coolant under-
            // predicts heat removal".
            if (sg.Thermal.Stations.Length > 0)
            {
                var tStations = sg.Thermal.Stations;
                int peakIdx = 0;
                for (int i = 1; i < tStations.Length; i++)
                    if (tStations[i].GasSideWallTemp_K > tStations[peakIdx].GasSideWallTemp_K)
                        peakIdx = i;
                var ps = tStations[peakIdx];
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_station_idx={peakIdx}  x_mm={ps.X_mm:F1}  R_mm={ps.R_mm:F2}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_q_radial_MWm2={ps.HeatFlux_Wm2 / 1e6:F2}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_h_g_kWm2K={ps.h_g_Wm2K / 1000:F2}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_h_c_kWm2K={ps.h_c_Wm2K / 1000:F2}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_T_aw_eff_K={ps.EffectiveRecoveryTemp_K:F0}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_T_wg_K={ps.GasSideWallTemp_K:F0}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_T_wc_K={ps.CoolantSideWallTemp_K:F0}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_T_bulk_K={ps.CoolantBulkTemp_K:F0}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_v_coolant_ms={ps.CoolantVelocity_ms:F2}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_Re_coolant={ps.Reynolds:F0}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_ch_h_mm={ps.ChannelHeight_mm:F2}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_ch_w_mm={ps.ChannelWidth_mm:F2}");
                // Compute thermal resistance breakdown at peak station.
                // q = (T_aw - T_bulk) / (1/h_g + t_wall/k + 1/h_c)
                // Each term contributes to driving wall T up.
                double r_g = 1.0 / Math.Max(ps.h_g_Wm2K, 1);
                double r_c = 1.0 / Math.Max(ps.h_c_Wm2K, 1);
                double rt  = r_g + r_c;
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  R_gas_pct={100 * r_g / rt:F1}  R_coolant_pct={100 * r_c / rt:F1}");
                Console.WriteLine($"PREFLIGHT_THERMAL  preset={preset.Name}  peak_film_eta={ps.FilmEffectiveness:F3}  peak_T_aw_K={ps.AdiabaticWallTemp_K:F0}");
            }

            // Sprint feasibility-audit-H3 (2026-04-27): structural breakdown
            // at the peak-VM-stress station. Splits the YIELD_EXCEEDED gate
            // into hoop / thermal / combined components so a reviewer can
            // see whether the bottleneck is the wall ΔT (thermal) or the
            // coolant-vs-chamber pressure differential (hoop). Writes
            // nothing when Structural is null (e.g., SA candidate that
            // threw an exception during evaluation).
            if (sg.Stress is { } structSeed && structSeed.Stations.Length > 0)
            {
                int psIdx = structSeed.PeakStationIndex;
                var sst = structSeed.Stations[Math.Clamp(psIdx, 0, structSeed.Stations.Length - 1)];
                Console.WriteLine($"PREFLIGHT_STRUCT  preset={preset.Name}  peak_station_idx={psIdx}  x_mm={sst.X_mm:F1}");
                Console.WriteLine($"PREFLIGHT_STRUCT  preset={preset.Name}  peak_hoop_MPa={structSeed.PeakHoop_MPa:F1}  peak_thermal_MPa={structSeed.PeakThermal_MPa:F1}");
                Console.WriteLine($"PREFLIGHT_STRUCT  preset={preset.Name}  peak_combined_VM_MPa={structSeed.PeakCombined_MPa:F1}  yield_at_T_MPa={sst.YieldAtTemp_MPa:F1}");
                Console.WriteLine($"PREFLIGHT_STRUCT  preset={preset.Name}  min_safety_factor={structSeed.MinSafetyFactor:F3}  yield_exceeded={structSeed.YieldExceeded}");
            }

            // Sprint feasibility-audit-H3 (2026-04-27): expander-cycle
            // energy balance at the seed. Surfaces the AvailableShaftPower
            // / RequiredShaftPower split + turbine pressure ratio so a
            // reviewer can see at a glance whether an EXPANDER_TURBINE_-
            // ENTHALPY_DEFICIT firing is "no forward expansion" vs
            // "forward expansion but small ratio" vs "ratio fine but
            // pump too thirsty". Skipped on non-expander cycles
            // (ExpanderTurbine is null).
            if (sg.ExpanderTurbine is { } exp)
            {
                Console.WriteLine($"PREFLIGHT_EXPANDER  preset={preset.Name}  cycle={exp.Cycle}  inlet_P_MPa={exp.InletPressure_Pa / 1e6:F2}  outlet_P_MPa={exp.OutletPressure_Pa / 1e6:F2}");
                double pr = exp.InletPressure_Pa > 0 ? exp.OutletPressure_Pa / exp.InletPressure_Pa : 0;
                Console.WriteLine($"PREFLIGHT_EXPANDER  preset={preset.Name}  pressure_ratio={pr:F3}  is_choked={exp.IsChoked}  pi_crit={exp.CriticalPressureRatio:F3}");
                Console.WriteLine($"PREFLIGHT_EXPANDER  preset={preset.Name}  inlet_T_K={exp.InletTemperature_K:F0}  cp_Jkg={exp.Cp_Jkg_K:F0}  gamma={exp.EffectiveGamma:F3}");
                Console.WriteLine($"PREFLIGHT_EXPANDER  preset={preset.Name}  specific_work_kJkg={exp.ActualSpecificWork_Jkg / 1e3:F1}  efficiency={exp.Efficiency:F2}");
                Console.WriteLine($"PREFLIGHT_EXPANDER  preset={preset.Name}  available_kW={exp.AvailableShaftPower_W / 1e3:F1}  required_kW={exp.RequiredShaftPower_W / 1e3:F1}  margin={exp.MassFlowMargin:F3}");
                Console.WriteLine($"PREFLIGHT_EXPANDER  preset={preset.Name}  power_sufficient={exp.PowerSufficient}");
            }
            Console.WriteLine();
        }

        int effectiveChains = useMultiChain
            ? (chainCount > 0 ? chainCount : MultiChainOptimizer.DefaultChainCount())
            : 1;
        string mode = useMultiChain ? $"multi-chain (×{effectiveChains})" : "single-chain";
        Console.WriteLine($"# bench-sa preset={preset.Name} seed={seed} iters={iterations} repeat={repeat} mode={mode}");
        Console.WriteLine($"# preset: {preset.Description}");
        var machine = MachineInfo.Capture();
        Console.WriteLine(machine.ToHeaderLine());

        // Default output path mirrors the BB-0 baseline naming convention.
        // Sprint B.1: rocket baselines live in baselines/rocket/.
        outPath ??= Path.Combine(AppContext.BaseDirectory, "baselines", "rocket",
            $"bench-sa-{preset.Name}-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        Console.WriteLine($"# JSONL: {outPath}");

        // Pre-flight: one Generate+Evaluate on the seeded baseline.
        // The preflight Score IS the pre-cascade fingerprint — even if
        // TotalScore is +∞ (infeasible), the underlying physics scalars
        // (PeakWallT_K, CoolantTOut_K, Mass_g, etc.) are real values
        // captured at the AutoSeeder defaults. The cascade will lift
        // these into feasibility; the diff against this snapshot is
        // what quantifies the cascade's impact.
        PropellantTables.UseEquilibrium = preset.Seed.UseEquilibriumRecommended;
        var preflight = RegenChamberOptimization.GenerateWith(
            preset.Seed.Conditions, preset.Seed.Design,
            skipVoxelGeometry: true, skipMfgAnalysis: true);
        var preflightScore = RegenChamberOptimization.Evaluate(preflight, RegenChamberOptimization.Profiles[0]);
        bool preflightFeasible = !double.IsPositiveInfinity(preflightScore.TotalScore);
        Console.WriteLine($"# preflight: total_score={preflightScore.TotalScore:F2} feasible={preflightFeasible}");
        if (!preflightFeasible)
        {
            Console.WriteLine($"#       Violations: {string.Join(", ", preflightScore.FeasibilityViolations.Select(v => v.ConstraintId).Take(8))}{(preflightScore.FeasibilityViolations.Length > 8 ? ", …" : "")}");
            Console.WriteLine($"#       NOTE: SA may improve, may not. Fingerprint scalars below ARE captured from the seed even when infeasible — that's the pre-cascade reference value.");
        }

        // Pack the seeded baseline for warm-start.
        double[] baselineParams = RegenChamberOptimization.Pack(preset.Seed.Design);

        // OA-1 (#287): construct the GIF capture orchestrator if --sa-animation-gif
        // was passed. Frames are stashed during SA (cheap — just clones the
        // SA candidate's design + conditions) and rendered after the loop
        // completes via Voxelforge.StlExporter + voxelforge-render. Multiple
        // repeats append into a single GIF — the user is expected to pass
        // --repeat 1 for a clean per-run animation.
        SaAnimationCapture? saAnim = null;
        if (saAnimGifPath is not null)
        {
            var renderer = SubprocessFrameRenderer.AutoDiscover(
                voxelSize_mm: saAnimVoxel_mm,
                material:     saAnimMaterial,
                resolution:   saAnimResolution);
            if (renderer is null)
            {
                Console.Error.WriteLine(
                    "# --sa-animation-gif requested but renderer/StlExporter not located. SA continues without animation.");
            }
            else
            {
                saAnim = new SaAnimationCapture(renderer, saAnimGifPath);
                Console.WriteLine(
                    $"# sa-animation-gif: capturing best-improvement frames → {saAnimGifPath} "
                  + $"(voxel={saAnimVoxel_mm:F2} mm, material={saAnimMaterial}, resolution={saAnimResolution})");
            }
        }

        for (int rep = 0; rep < repeat; rep++)
        {
            Console.WriteLine();
            Console.WriteLine($"# === repeat {rep + 1}/{repeat} (seed={seed + rep}) ===");
            int repSeed = seed + rep;
            if (useMultiChain)
                RunOneMultiChain(preset, preflight, preflightScore, baselineParams, repSeed, iterations, effectiveChains, disableInfeasibleExit, outPath, saAnim);
            else
                RunOne(preset, preflight, preflightScore, baselineParams, repSeed, iterations, disableInfeasibleExit, outPath, saAnim);
        }

        Console.WriteLine();
        Console.WriteLine("BENCH_MEDIAN  bench=bench-sa  preset=" + preset.Name + "  records_appended=" + repeat);

        // OA-1 (#287): compose the GIF after all repeats finish. Capture
        // is best-effort: a frame whose StlExporter or Blender call fails
        // is logged + skipped, and the GIF still emits with whatever
        // succeeded. SaAnimationResult reports captured-vs-rendered for
        // post-run inspection.
        if (saAnim is not null)
        {
            try
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"# sa-animation-gif: rendering {saAnim.CapturedIterations.Count} captured frames "
                  + $"(this may take ~30 s/frame depending on resolution)…");
                var result = saAnim.Compose(saAnimFrameDelayMs, saAnimHoldLastMs);
                Console.WriteLine(
                    $"# sa-animation-gif: done — captured={result.FramesCaptured} "
                  + $"rendered={result.FramesRendered} bytes={result.GifBytes} "
                  + $"elapsed_ms={result.ElapsedMilliseconds}");
                Console.WriteLine($"# sa-animation-gif: output → {result.GifPath}");
                Console.WriteLine(
                    $"BENCH_ANIMATION  preset={preset.Name}  captured={result.FramesCaptured}  "
                  + $"rendered={result.FramesRendered}  bytes={result.GifBytes}  "
                  + $"elapsed_ms={result.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"# sa-animation-gif: compose failed — {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                saAnim.Dispose();
            }
        }

        if (s_dumpViolations)
        {
            Console.WriteLine();
            Console.WriteLine($"# === violation histogram (preset={preset.Name}, total candidates={s_totalCandidates}) ===");
            foreach (var kv in s_violationCounts.OrderByDescending(p => p.Value))
            {
                double pct = 100.0 * kv.Value / Math.Max(1, s_totalCandidates);
                Console.WriteLine($"VIOLATION  preset={preset.Name}  gate={kv.Key}  count={kv.Value}  pct={pct:F1}");
            }
            // Reset for any subsequent preset run in the same process.
            s_violationCounts.Clear();
            s_totalCandidates = 0;
        }

        // Sprint H3 (2026-04-27): close + reset the trace writer (if any)
        // so a subsequent --bench-sa invocation in the same process starts
        // clean. lock-protected against last-iteration writes from the
        // multi-chain path.
        if (s_traceWriter is not null)
        {
            lock (s_traceLock)
            {
                s_traceWriter.Flush();
                s_traceWriter.Dispose();
                s_traceWriter = null;
            }
            s_traceIter = 0;
        }
        return 0;
    }

    private static void RecordViolations(RegenScoreResult s)
    {
        if (!s_dumpViolations) return;
        System.Threading.Interlocked.Increment(ref s_totalCandidates);
        foreach (var v in s.FeasibilityViolations)
        {
            s_violationCounts.AddOrUpdate(v.ConstraintId, 1, (_, c) => c + 1);
        }
    }

    /// <summary>
    /// Sprint feasibility-audit-H3 (2026-04-27): per-candidate JSONL
    /// trace. Emit one self-contained JSON line capturing the SA design
    /// vector + score + violations + a few diagnostic scalars (peak
    /// wall T, safety factor, pintle blockage, expander balance). This
    /// lets a downstream <c>jq</c> filter answer questions like "which
    /// SA dim values correlate with the PINTLE_BLOCKAGE_OUT_OF_BAND
    /// gate firing" without re-running the optimizer.
    ///
    /// Thread-safe via <see cref="s_traceLock"/>; writer is closed by
    /// the caller (in <see cref="Run"/>) at the end of the benchmark.
    /// </summary>
    private static void DumpTrace(double[] cand, RegenScoreResult s, RegenGenerationResult? gen)
    {
        if (s_traceWriter is null) return;
        long iter = System.Threading.Interlocked.Increment(ref s_traceIter);
        var sb = new StringBuilder(512);
        var c = CultureInfo.InvariantCulture;
        sb.Append("{\"iter\":").Append(iter);
        bool feasible = !double.IsPositiveInfinity(s.TotalScore);
        sb.Append(",\"feasible\":").Append(feasible ? "true" : "false");
        sb.Append(",\"score\":");
        if (feasible) sb.Append(s.TotalScore.ToString("R", c));
        else sb.Append("null");
        sb.Append(",\"violations\":[");
        for (int i = 0; i < s.FeasibilityViolations.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var v = s.FeasibilityViolations[i];
            sb.Append("{\"gate\":\"").Append(v.ConstraintId).Append('"');
            sb.Append(",\"actual\":").Append(JsonNumber(v.ActualValue));
            sb.Append(",\"limit\":").Append(JsonNumber(v.Limit));
            sb.Append('}');
        }
        sb.Append("],\"design\":[");
        for (int i = 0; i < cand.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonNumber(cand[i]));
        }
        sb.Append("],\"scalars\":{");
        sb.Append("\"peak_wall_t_k\":").Append(JsonNumber(s.PeakWallT_K));
        sb.Append(",\"min_sf\":").Append(JsonNumber(s.MinSafetyFactor));
        sb.Append(",\"coolant_dp_pa\":").Append(JsonNumber(s.CoolantDP_Pa));
        sb.Append(",\"coolant_t_out_k\":").Append(JsonNumber(s.CoolantTOut_K));
        if (gen?.InjectorSizing?.PerElementResult is { } per)
        {
            sb.Append(",\"blockage\":").Append(JsonNumber(per.PintleBlockageFraction));
            sb.Append(",\"momentum_ratio\":").Append(JsonNumber(per.MomentumRatio));
        }
        if (gen?.ExpanderTurbine is { } exp)
        {
            sb.Append(",\"expander_avail_kw\":").Append(JsonNumber(exp.AvailableShaftPower_W / 1e3));
            sb.Append(",\"expander_req_kw\":").Append(JsonNumber(exp.RequiredShaftPower_W / 1e3));
            sb.Append(",\"expander_pr\":").Append(
                JsonNumber(exp.InletPressure_Pa > 0
                    ? exp.OutletPressure_Pa / exp.InletPressure_Pa
                    : 0.0));
        }
        sb.Append("}}");
        lock (s_traceLock)
        {
            s_traceWriter.WriteLine(sb.ToString());
        }
    }

    /// <summary>
    /// JSON-safe number serialization. NaN/Infinity → null (JSON has no
    /// literal for them). Round-trip "R" format preserves precision.
    /// </summary>
    private static string JsonNumber(double x)
        => double.IsFinite(x)
            ? x.ToString("R", CultureInfo.InvariantCulture)
            : "null";

    private static RegenScoreResult MakeInfeasibleScore() => new(
        TotalScore: double.PositiveInfinity,
        PeakWallT_K: -1, WallTMargin_K: -1,
        CoolantDP_Pa: -1, CoolantDP_Fraction: -1,
        CoolantTOut_K: -1, TotalHeatLoad_W: -1,
        ThroatHeatFlux_Wm2: -1, Mass_g: -1, Cost_USD: -1,
        MinFeatureSize_mm: -1, MinSafetyFactor: -1,
        WallTExceeded: false, YieldExceeded: false, InfeasibleFeature: true,
        Warnings: Array.Empty<string>(),
        FeasibilityViolations: Array.Empty<FeasibilityViolation>());

    private static double StdDev(IReadOnlyList<double> xs, double mean)
    {
        if (xs.Count < 2) return 0;
        double sum2 = 0;
        for (int i = 0; i < xs.Count; i++)
        {
            double d = xs[i] - mean;
            sum2 += d * d;
        }
        return Math.Sqrt(sum2 / (xs.Count - 1));
    }

    private static string Fmt(double v) =>
        double.IsFinite(v)
            ? v.ToString("F4", CultureInfo.InvariantCulture)
            : "null";
}
