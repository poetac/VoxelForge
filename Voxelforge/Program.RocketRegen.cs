// Program.RocketRegen.cs — rocket-pillar manual-mode regen handler.
//
// Extracted from Program.cs (Sprint 0 / Wave 1, 2026-05-05) as a
// partial-class slice. Behavior is unchanged. Sibling to
// Program.Airbreathing.cs (RegenerateAirbreathingForManualMode) — both
// methods are dispatched from the task-thread Run() loop based on
// the active mode. Centralising rocket regen here makes the
// dispatcher (Program.cs) easier to audit.

using System;
using PicoGK;
using Voxelforge.Optimization;

namespace Voxelforge;

public static partial class Program
{
    // ═════════════════════════════════════════════════════════════════════
    //   Request handlers (task thread)
    // ═════════════════════════════════════════════════════════════════════

    private static void RegenerateForManualMode(OperatingConditions cond, RegenChamberDesign design)
    {
        try
        {
            // The form has no first-class UI for the injector element pattern.
            // Without a pattern, ChamberVoxelBuilder silently skips ALL
            // injector-element voxelization (line 558-561 there) — the user
            // sees a flange ring with nothing inside it. Fill in a default
            // pattern from AutoSeeder when none was carried through from a
            // loaded JSON. AutoSeeder is pure math (microseconds) and
            // deterministic, so the cache hash below stays consistent across
            // repeated previews. SA / batch / optimizer paths are unaffected
            // — AutoSeeder.Seed already runs there to produce the seeded
            // design, so they always have a non-null pattern.
            if (design.InjectorElementPattern is null)
            {
                try
                {
                    var spec = new Voxelforge.Optimization.EngineSpec(
                        cond.PropellantPair,
                        cond.Thrust_N,
                        cond.ChamberPressure_Pa,
                        design.ExpansionRatio);
                    var seeded = Voxelforge.Optimization.AutoSeeder.Seed(spec);
                    design = design with
                    {
                        InjectorElementPattern = seeded.Design.InjectorElementPattern
                    };
                }
                catch (Exception seedEx)
                {
                    // AutoSeeder rejected the spec (out-of-envelope thrust /
                    // Pc / ε, or unsupported propellant pair). Proceed with
                    // the user's null pattern; the preview falls back to the
                    // pre-fix behaviour of skipping injector elements.
                    Library.Log($"AutoSeeder fallback skipped: {seedEx.Message}");
                }
            }

            // Reuse the cached voxel build when the user's design
            // hasn't actually changed since the last successful
            // Generate. DesignProvenance.Compute(cond, design)
            // produces the design hash stamped on every
            // RegenGenerationResult — we just compare. Hash-match →
            // reuse _lastResult, skip Build().
            string newHash = Voxelforge.Optimization.DesignProvenance
                .Compute(cond, design);
            if (_lastResult is not null
                && !string.IsNullOrEmpty(_lastResult.DesignHash)
                && _lastResult.DesignHash == newHash)
            {
                SetFormStatus("No design change — reusing cached build.");
                UpdateViewer(_lastResult);
                UpdateFormResults(_lastResult, _lastScore);
                return;
            }

            // "Fast preview" mode clones the design with
            // ChannelTopology.None so this Generate skips the full channel
            // voxelise pass (~84 % of build time at 0.4 mm per the legacy
            // baseline). The user's ACTUAL design is preserved — we only
            // swap the topology for this one render. SA / FinalizeOpt /
            // batch paths ignore FastPreviewMode so they always see the
            // user's real topology.
            //
            // Only cloak CHANNEL-STYLE topologies (Axial / Helical / TPMS).
            // Aerospike + LinearAerospike are full-geometry replacements
            // dispatched to AerospikeBuilder.Build; they have no channel-
            // voxelize phase to skip, so cloaking them to None silently
            // produces a bell-chamber shell instead of the requested
            // aerospike. Bug surfaced 2026-04-25.
            var effectiveDesign = design;
            if (Voxelforge.UI.ResourceBudget.FastPreviewMode
                && design.ChannelTopology.IsChannelStyle())
            {
                effectiveDesign = design with
                {
                    ChannelTopology = Voxelforge.Optimization.ChannelTopology.None
                };
                SetFormStatus("Fast preview: rendering channels-skipped shell (~10× faster)…");
            }
            else
            {
                SetFormStatus("Regenerating…");
            }

            // Tiled dispatch: when the user opted in to
            // TileLargeBuilds, Generate runs physics via the analytical
            // path (skipVoxelGeometry: true — cheap, no voxels) and the
            // voxel/mesh/STL pipeline via ChamberAxialTileBuilder.BuildTiled
            // (peak memory ≈ 1/N of monolithic). The resulting STL is
            // written to %TEMP%/regen-tiled/latest.stl and the user is
            // pointed at the file via the status bar. Viewer is NOT
            // updated (no monolithic Voxels grid exists in this mode); the
            // user inspects the STL in their preferred CAD/slicer. SA /
            // FinalizeOpt paths ignore this flag so committed designs
            // always produce monolithic output with full viewer render.
            if (Voxelforge.UI.ResourceBudget.TileLargeBuilds)
            {
                SetFormStatus("Tiled build: computing physics + per-tile voxels…");
                var physicsGen = RegenChamberOptimization.GenerateWith(
                    cond, effectiveDesign,
                    voxelSize_mm:      0.0,
                    skipVoxelGeometry: true,
                    skipMfgAnalysis:   false);

                var tileOpts = RegenChamberOptimization.ComposeChamberBuildOptions(cond, effectiveDesign);
                int tiles = Voxelforge.UI.ResourceBudget.TileCount;
                var plan = Voxelforge.Geometry.ChamberAxialTileBuilder.PlanTiles(
                    tileOpts.Contour,
                    targetTileCount:            tiles,
                    injectorFlangeThickness_mm: tileOpts.IncludeInjectorFlange ? tileOpts.InjectorFlangeThickness_mm : 0.0,
                    mountFlangeThickness_mm:    tileOpts.IncludeMountingFlange  ? tileOpts.MountingFlangeThickness_mm : 0.0,
                    gimbalAftExtension_mm:      0.0);
                string outDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "regen-tiled");
                System.IO.Directory.CreateDirectory(outDir);
                string outPath = System.IO.Path.Combine(outDir, "latest.stl");

                var summary = Voxelforge.Geometry.ChamberAxialTileBuilder.BuildTiled(
                    tileOpts, plan, outPath);

                _lastResult = physicsGen;
                _lastDesign = design;
                _lastScore  = null;
                _lastResultBestSoFarIter = 0;
                _lastParetoFrontSnapshot = null;
                UpdateFormResults(physicsGen, null);
                // Viewer stays on the previous monolithic result (if any);
                // nothing to swap in because tiled mode produces STL, not
                // a PicoGK Voxels object that ViewerInterface can render.

                SetFormStatus(
                    $"Ready. Tiled: {summary.Plan.Count} tiles × ~{summary.PerTileBuild_ms / summary.Plan.Count:F0} ms each, "
                  + $"{summary.WeldResult.OutputTriangleCount:N0} tris → {outPath} "
                  + $"({summary.WeldResult.OutputBytes / 1_048_576:N1} MB). {physicsGen.Geometry.Description}");
                return;
            }

            // When the user opted in to AutoCoarsenVoxelToFitBudget,
            // catch the memory gate's exception and retry at the coarser
            // voxel size the gate suggested, up to 3 retries. Transparent
            // to the rest of the pipeline — the returned result is a
            // normal RegenGenerationResult with the actually-used voxel
            // stamped on Geometry.Profile.
            var voxelGen = new Voxelforge.Geometry.ChamberVoxelBuilderAdapter();
            var gen = Voxelforge.UI.ResourceBudget.AutoCoarsenVoxelToFitBudget
                ? RegenChamberOptimization.GenerateWithAutoCoarsen(cond, effectiveDesign, voxelSize_mm: VoxelSizeMM,
                    onVoxelSubstituted: (prev, now, _) =>
                        SetFormStatus($"Voxel auto-coarsened {prev:F2} → {now:F2} mm to fit memory budget; regenerating…"),
                    voxelGenerator: voxelGen)
                : RegenChamberOptimization.GenerateWith(cond, effectiveDesign, voxelSize_mm: VoxelSizeMM,
                    voxelGenerator: voxelGen);

            // Aerospike preview swap. RegenChamberOptimization.GenerateWith
            // always builds bell-chamber voxels via ChamberVoxelBuilder.Build (its
            // physics sidecar at line 760 only populates `gen.Aerospike` for
            // analytical/scoring use; voxel rendering for the UI is the caller's
            // job per the comment at GenerateWith line 753). Without this swap,
            // selecting the Aerospike topology shows a bell shell in the viewer
            // because `gen.Geometry.Voxels` remains the bell chamber.
            //
            // Only voxelise the axisymmetric `Aerospike`; `LinearAerospike`'s
            // voxel pipeline is a future-sprint follow-on
            // (AerospikeBuilder.BuildLinearPhysicsOnly today).
            if (Voxelforge.Optimization.ChannelTopologyDispatcher.IsAerospikeAxisymmetric(effectiveDesign.ChannelTopology))
            {
                try
                {
                    var aeroSpec = Voxelforge.Optimization.AerospikeOptimization.ToSpec(cond, effectiveDesign);
                    var aeroResult = Voxelforge.Geometry.AerospikeBuilder.Build(aeroSpec, voxelSize_mm: VoxelSizeMM);
                    if (aeroResult.Voxels is not null)
                    {
                        gen = gen with { Geometry = gen.Geometry with { Voxels = aeroResult.Voxels } };
                    }
                }
                catch (Exception aerospikeEx)
                {
                    Library.Log("Aerospike voxelisation failed for preview: " + aerospikeEx.Message);
                    // Fall through with bell-chamber voxels rather than blanking the
                    // viewer — at least the user sees SOMETHING and the status bar
                    // tells them physics succeeded.
                }
            }
            else if (effectiveDesign.ChannelTopology == Voxelforge.Optimization.ChannelTopology.LinearAerospike)
            {
                // LinearAerospike has no voxel
                // builder yet — only AerospikeBuilder.BuildLinearPhysicsOnly.
                // Without an explicit branch here, the preview silently
                // shows the bell-chamber voxels GenerateWith returned
                // (same surprise as the original Aerospike bug). Blank
                // the voxels so UpdateViewer skips the render entirely
                // (Voxels == null short-circuits at line 1675), then the
                // status-bar message below tells the user physics ran but
                // geometry isn't visualisable yet.
                gen = gen with { Geometry = gen.Geometry with { Voxels = null! } };
            }

            _lastResult = gen;
            _lastDesign = design;            // keep the user's REAL design for proof/tolerance/save callbacks
            _lastScore = null;
            _lastResultBestSoFarIter = 0;   // manual regen — result is final, not a best-so-far
            _lastParetoFrontSnapshot = null; // PHASE 6: drop stale front from a prior SA run
            UpdateViewer(gen);
            UpdateFormResults(gen, null);
            string previewTag = Voxelforge.UI.ResourceBudget.FastPreviewMode
                                && design.ChannelTopology != Voxelforge.Optimization.ChannelTopology.None
                              ? "  [fast preview — channels skipped; uncheck Fast preview for full build]"
                              : "";
            string topologyTag = design.ChannelTopology == Voxelforge.Optimization.ChannelTopology.LinearAerospike
                              ? "  [linear-aerospike voxel preview not yet implemented — physics ran, no 3D preview]"
                              : "";
            SetFormStatus("Ready. " + gen.Geometry.Description + previewTag + topologyTag);
        }
        catch (Voxelforge.Analysis.MemoryBudgetExceededException mem)
        {
            // The main voxel build would have
            // exceeded the configured memory budget. Log + surface the
            // actionable suggested voxel size; do NOT allocate. This is
            // the primary guard against the 5-hour pagefile-thrash crash
            // on over-sized (e.g. 50 kN) designs.
            Library.Log($"Regen memory gate: {mem.Message}");
            SetFormStatus(
                $"Generate blocked — projected {mem.ProjectedBytes / 1_048_576:N0} MB exceeds "
              + $"{mem.BudgetBytes / 1_048_576:N0} MB budget. "
              + $"Coarsen voxel to ≥ {mem.SuggestedVoxel_mm:F2} mm "
              + $"or raise the Resource Budget cap.");
        }
        catch (Exception ex)
        {
            Library.Log($"Regen error: {ex}");
            SetFormStatus("Error: " + ex.Message);
        }
    }
}
