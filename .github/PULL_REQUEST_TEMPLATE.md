## Summary

<!-- 1-3 bullets: what changed and why. Link the sprint/ADR if applicable. -->

## Merge mechanics

- Repo setting is **squash-merge only** (merge-commit + rebase-merge disabled, 2026-05-17). Your individual commits are flattened into a single commit on `main` whose subject defaults to the single-commit subject (or the PR title when the branch carries multiple commits) — so write commit messages that are usable as the final history entry.
- **Draft this PR** (`gh pr ready --undo` or the GitHub "Convert to draft" menu) if the change is incomplete, seeking early feedback, or touches a hotspot file listed below. Mark "Ready for review" only when CI is green and you'd be happy for someone to merge it.

## Test plan

<!-- How did you verify this? Tick what applies. -->
- [ ] `dotnet build voxelforge.sln` passes
- [ ] `dotnet test Voxelforge.Tests/Voxelforge.Tests.csproj` passes locally and matches the baseline
- [ ] Ran the app (`dotnet run --project Voxelforge/Voxelforge.csproj`) and exercised affected UI paths
- [ ] Ran the relevant CLI flags (e.g. `--regen`, `--aerospike`, `--monolithic`, `--autonomous`)
- [ ] New tests added for new behavior (or explain why not)
- [ ] Baseline regressions in `Voxelforge.Benchmarks/baselines/` reviewed if physics scoring changed

## Hotspot file coordination

<!-- Delete this section if you didn't touch any of the files below. -->
<!-- Note: PR-2 (Sprint 0 namespace rename, 2026-04-30) + the 2026-05-04
     follow-on renamed namespaces and folders to `Voxelforge.*`. The 5
     documented magic strings (DesignPersistence AppName, ThreeMFExport
     meta, AnalyticalPreviewMesh + tiled STL header, PrinterParameterPresets
     schema) stay literal for JSON/3MF/STL round-trip. -->
If this PR edits any of the files below, open as **Draft** first so a
hotspot reviewer can coordinate before the diff hardens. Canonical
hotspot list lives in [CODEOWNERS](../CODEOWNERS) + the
"Hotspot files" section of [CONTRIBUTING.md](../CONTRIBUTING.md).
- [ ] `Voxelforge/UI/RegenChamberForm.cs`
- [ ] `Voxelforge/Program.cs`
- [ ] `Voxelforge.Core/Optimization/RegenChamberOptimization.cs`
- [ ] `Voxelforge.Voxels/Geometry/ChamberVoxelBuilder.cs`

## Feasibility gates / ADRs

<!-- If this PR adds or modifies a feasibility gate or design variable, link/note the ADR. -->
- New feasibility gates: <!-- none | name them -->
- New design variables (Pack/Unpack/Bounds updated): <!-- none | name them -->
- ADRs added/updated: <!-- none | list -->
