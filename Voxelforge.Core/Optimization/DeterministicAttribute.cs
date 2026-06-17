// DeterministicAttribute.cs — ADR-020 / issue #209.
//
// Marker attribute consumed by the Voxelforge.Analyzers analyzer to
// enforce determinism rules (VFD001-VFD004) at compile time. Methods
// or classes annotated here, plus everything they transitively call
// inside the same compilation, must produce bit-identical output for
// identical input regardless of wall clock, OS scheduler, or process.

using System;

namespace Voxelforge.Optimization;

/// <summary>
/// Marks a method or class as required to produce bit-identical output
/// for identical input across runs and platforms. Enforced at compile
/// time by Voxelforge.Analyzers (rules VFD001-VFD004). Class-level
/// application marks every method on the class.
/// </summary>
/// <remarks>
/// Determinism is load-bearing for the multi-chain SA strict-determinism
/// contract (ADR-017), bench-regression CI, audit trail, and reproducibility
/// across compute environments. Enforcement is opt-in: a method must be
/// annotated, or be transitively called from an annotated method, for the
/// analyzer to fire on its body. Out of scope for v1: virtual / interface
/// dispatch (declared method only), reflection, dynamic, property/field
/// initializers, Dictionary/HashSet enumeration order. See ADR-020 for
/// the full rationale and limitations.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class DeterministicAttribute : Attribute
{
}
