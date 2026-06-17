; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
VFD001  | Voxelforge.Determinism | Error | Use of wall-clock DateTime.Now / UtcNow / Today inside a [Deterministic] scope
VFD002  | Voxelforge.Determinism | Error | Use of unseeded `new Random()` inside a [Deterministic] scope
VFD003  | Voxelforge.Determinism | Error | Use of `Guid.NewGuid()` inside a [Deterministic] scope
VFD004  | Voxelforge.Determinism | Error | Use of `Environment.TickCount` inside a [Deterministic] scope
VFD005  | Voxelforge.Determinism | Error | foreach over Dictionary/HashSet enumeration inside a [Deterministic] scope
VFD006  | Voxelforge.Determinism | Error | Use of environment-dependent Path or Environment methods inside a [Deterministic] scope
VFD007  | Voxelforge.Determinism | Error | Use of `Thread.Sleep` or `Task.Delay` (wall-clock delays) inside a [Deterministic] scope
VFD008  | Voxelforge.Determinism | Error | Use of `Process.Start`, `File.{Read,Write}*`, or `Directory.*` (filesystem / subprocess side effects) inside a [Deterministic] scope
VFD009  | Voxelforge.Determinism | Error | Use of `string.GetHashCode()` (parameterless overload) inside a [Deterministic] scope — non-deterministic across .NET process restarts due to hash randomization
VFD011  | Voxelforge.Determinism | Error | Use of `Console.{Write, WriteLine, Read, ReadLine, ReadKey}` or instance methods on `Console.{Out, Error, In}` inside a [Deterministic] scope — output-stream side effect
VFD012  | Voxelforge.Determinism | Error | Wall-clock read (DateTime.{Now,UtcNow,Today}, DateTimeOffset.{Now,UtcNow}, Environment.TickCount{,64}, Stopwatch.{GetTimestamp,StartNew,GetElapsedTime}) inside an IObjective implementation — structural determinism contract on the optimizer evaluation surface
VFD013  | Voxelforge.Determinism | Error | Read of a static mutable field (neither `static readonly` nor `const`, no `[Pure]` / `[ThreadSafe]` allow-list attribute) inside a [Deterministic] / IObjective scope — see ADR-042, audit C1+C2 / issue #551
VFD014  | Voxelforge.Determinism | Error | `for (double t = ...; t < ...; t += ...)` FP-accumulated time loop inside a [Deterministic] / IObjective scope — refactor to integer-tick form per #553 / #547
VFD015  | Voxelforge.Determinism | Error | `Array.Sort` / `List<T>.Sort` / `Enumerable.OrderBy` with a single-`CompareTo` comparer (no tie-break) inside a [Deterministic] / IObjective scope — add a position-anchor fallback per #552
VFA001  | Voxelforge.Pillars     | Error | Cross-family `using` directive in a family-specific assembly (e.g. Voxelforge.Airbreathing.* importing Voxelforge.Marine.*)
VFA002  | Voxelforge.Pillars     | Error | Type in a family-specific assembly declared outside the matching `Voxelforge.{Family}` namespace
VFA004  | Voxelforge.Tests       | Warning | Test method (in a `*Tests.cs` file with `[Fact]`/`[Theory]`) whose name does not contain at least one underscore — voxelforge's ambient `Method_Behaviour_Expected` convention
VFA005  | Voxelforge.Determinism | Error | Ambient `#pragma warning {disable,restore} VFD012` — use `[SuppressMessage("Voxelforge.Determinism", "VFD012", Justification = "…")]` on the symbol instead so the suppression surfaces in IDE hover + review
VFD016  | Voxelforge.Determinism | Error | `MathF.Clamp` call — `System.MathF` has no `Clamp` method; use `Math.Clamp(float, float, float)` instead (global rule, not scoped to `[Deterministic]`)
