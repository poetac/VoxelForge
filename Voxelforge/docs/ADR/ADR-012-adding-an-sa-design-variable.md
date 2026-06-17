# ADR-012 — How to add an SA design variable

**Status:** Accepted
**Date:** 2026-04-22 (Sprint 8 Track C — codifies the Sprint 6 + 7 workflow)

## Context

The simulated-annealing optimiser (ADR-003) walks a packed `double[]`
vector where each dimension is one tunable design variable. Before
Sprint 6 Track A, adding a new variable meant coordinating edits
across four files (`RegenChamberOptimization.Bounds` literal,
`Pack`, `Unpack`, and at least one test) — the original bounds-debt
sprint history is preserved in git log.

Sprint 6 Track A migrated the bounds to a reflection-based registry
(`DesignVariableRegistry`). Sprint 7 Track C migrated Pack / Unpack
to the same pattern (`DesignVariableBinder`). The net effect is that
a new SA variable is now a **one-line attribute annotation** on the
target record property — everything else is derived at startup.

This ADR documents the post-Sprint-7 workflow so future contributors
have a checklist rather than having to reverse-engineer the pattern
from git history.

## Decision

**Every SA variable is declared via
`[SaDesignVariable(index, min, max, gate?)]` on a `public init-only`
property of a participating record (today: `RegenChamberDesign` or
`InjectorPattern`). The registry + binder handle everything else.**

## Step-by-step workflow for adding a new dim

**Assumption:** you want to promote an existing record property to SA
tuning, OR you want to add a new property and tune it.

### 1. Pick an index

Look at the current last-used index:

```csharp
var last = DesignVariableRegistry.DescriptorsForMany(
    typeof(RegenChamberDesign),
    typeof(Injector.InjectorPattern))[^1];
// last.Index is the highest in use.
// As of 2026-05-01: last index = 33 (34 dims total, indices 0–33).
```

New dims go at `last.Index + 1`. No reshuffling allowed: the SA vector
length is observable (tests pin `packed.Length` for drift
detection), and a mid-vector insert would renumber every dim above
it.

### 2. Pick bounds

The `(min, max)` you pass to the attribute is the SA sampling range
AND the Unpack clamp. Pre-Sprint-7 the clamp was sometimes wider
than the bounds as a legacy safety valve; the Sprint 7 Track C binder
tightens both to the attribute values. Set the range to the
physically-sensible envelope; SA will explore it and Unpack will
clamp any out-of-range values to it.

**Common traps:**

- **Min = 0 is usually wrong for a flow / pressure / count.** SA will
  perturb around 0 and most samples land at the clamp boundary. Pick
  a realistic lower bound.
- **Range > 10 × nominal is often wrong.** SA cooling schedules
  assume the range is not pathologically wide.
- **Int-typed properties** (today only `ChannelCount`) get
  `Math.Round` + cast to int during Unpack. The bounds still use
  `double` at the attribute layer.

### 3. Pick a gate

`SaGate` is a discriminator enum that decides whether Unpack writes
the sampled value back into the baseline:

| Gate value | When Unpack applies this dim |
| --- | --- |
| `None` (default) | Always. Use for chamber + channel geometry that every design has. |
| `InjectorPatternPresent` | Only when `baseline.InjectorElementPattern != null`. Use for fields on `InjectorPattern`. |
| `TpmsTopology` | Only when `baseline.ChannelTopology` is TpmsGyroid / TpmsSchwarzP / TpmsSchwarzD. Use for fields that are meaningless outside a TPMS design. |
| `AerospikeTopology` | Only when `baseline.ChannelTopology == Aerospike`. Use for fields specific to the aerospike plug. |

**The value is always packed** regardless of the gate — the gate
controls Unpack, not Pack. This keeps the vector length stable across
categorical-topology flips.

Need a new gate? Extend `SaGate` + add a match in
`DesignVariableBinder.GateAllowsApplication`. Don't bolt ad-hoc gate
logic into Unpack.

### 4. Annotate the property

```csharp
[SaDesignVariable(index: 23, min: 0.5, max: 5.0, gate: SaGate.None)]
public double MyNewKnob_mm { get; init; } = 1.5;
```

That's it for the core variable plumbing. Build + run tests.

### 5. Update the tests that pin vector length

Several tests hard-code the current vector length (today: **34**) for
drift detection. Don't rely on a stable list of test names — those are
renamed on every dim bump (e.g. `Bounds_Length_Is23AfterAerospikeSprint1`
became `Bounds_Length_Is24AfterSprint9TrackC`). Use a grep instead:

```bash
# Replace N with the current length (find it with:
#   grep -c SaDesignVariable Voxelforge/**/*.cs
# or inspect the registry-driven Bounds array).
rg 'Assert\.Equal\(N,\s*(b|packed|p|positions|vec|bounds)\.Length' Voxelforge.Tests
```

Bump every hit to the new length. At the time of writing the hits are in
`SprintUpgradesTests.cs`, `NoyronTierA5Tests.cs`, and
`NoyronTierB1ProperTests.cs`; also rename the one-off
`Bounds_Length_IsNAfterSprintX` guard test to reflect the new N / sprint.
Incrementing the count is trivial but mechanical — do it in the same
commit as the attribute addition so CI doesn't go red between edits.

### 6. Add a round-trip test for the new dim

Follow the pattern in `SprintUpgradesTests.PackUnpack_RoundTrips*`:

```csharp
[Fact]
public void PackUnpack_RoundTripsMyNewKnob()
{
    var baseline = new RegenChamberDesign { MyNewKnob_mm = 2.7 };
    double[] p = RegenChamberOptimization.Pack(baseline);
    Assert.Equal(2.7, p[N], precision: 6);   // N = your new dim index

    var copy = RegenChamberOptimization.Unpack(p, baseline);
    Assert.Equal(2.7, copy.MyNewKnob_mm, precision: 6);
}
```

If your dim has a gate, add a negative test that the dim is NOT
applied when the baseline's gate state doesn't match. Look at
`Unpack_LeavesPlugLengthRatioAtDefault_WhenBaselineIsNotAerospike` for
an example.

### 7. (Optional) Update AutoSeeder for the new dim

If the new dim benefits from a principled initial value based on
propellant / thrust / cycle, add it to
`Optimization/AutoSeeder.cs`. If the record-default is already a
sensible starting point, do nothing — the default flows through
`new RegenChamberDesign()`.

### 8. (Optional) Surface in the UI

If this is a user-tunable knob (vs. purely optimiser-internal), add
a `NumericUpDown` + label + `Row` invocation in `RegenChamberForm.cs`
and wire it through `ReadDesign` / `ApplyDesign`
(`RegenChamberForm.ParameterIO.cs` since Sprint 6 Track B). Keep the
SA variable and the UI field bounds identical to avoid confusion.

## What you do NOT need to do

- Edit `RegenChamberOptimization.Bounds` — derived from the registry.
- Edit `RegenChamberOptimization.Pack` — delegates to the binder.
- Edit `RegenChamberOptimization.Unpack` — delegates to the binder.
- Add a gating block for a new conditional application — the `SaGate`
  enum covers every case we've seen; if a new gate type is genuinely
  needed, extend the enum + binder match once, then tag freely.

## Consequences

Positive:
- Adding a new SA variable is a ~10-minute edit (attribute + tests)
  rather than the former ~30-minute 4-file coordination.
- Attribute bounds are the single source of truth. The binder's
  drift-guard tests fail CI if anything regresses.
- `SaGate` enum is enumerable — new contributors can see at a glance
  which categorical branches the optimiser knows about.

Negative:
- Reflection cost was eliminated in T1.4 (PR #172): the Roslyn source
  generator in `Voxelforge.Generators/` emits static `GeneratedAccessors`
  at compile time; `DesignVariableBinder` consults them first. The
  `PropertyInfo` fallback path still exists for dynamically typed test
  fixtures only.
- Debugger stepping: Pack / Unpack go through `DesignVariableBinder`
  rather than inlined typed field access. Step-in / step-out is
  slightly deeper for casual debugging.

## Related ADRs

- ADR-003 (simulated-annealing optimizer) — the consumer of the
  packed vector.

## References in code

- `Voxelforge.Core/Optimization/SaDesignVariableAttribute.cs` —
  attribute + `SaGate` enum.
- `Voxelforge.Core/Optimization/DesignVariableRegistry.cs` —
  reflection-based discovery; `BoundsForMany` + `DescriptorsForMany`.
- `Voxelforge.Core/Optimization/DesignVariableBinder.cs` —
  registry-driven Pack / Unpack; uses T1.4 source-generated static
  accessors (PR #172) first, falls back to reflection on miss.
- `Voxelforge.Core/Optimization/RegenChamberOptimization.cs`
  `Bounds`, `Pack`, `Unpack` — 4-line total delegations.
