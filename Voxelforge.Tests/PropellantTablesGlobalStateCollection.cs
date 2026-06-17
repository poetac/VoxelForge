// Issue #311 — xUnit collection definition that serialises every test
// class which reads or mutates the static
// `Voxelforge.Combustion.PropellantTables.UseEquilibrium` /
// `EquilibriumCorrectionProvider` flags or assumes a particular default
// state.
//
// xUnit runs different test classes in parallel by default. The
// PropellantTables.Lookup cache key includes UseEquilibrium, so a
// mutation by one test class can flip the cached state under another
// test's feet mid-execution and break tests like
// `Phase4PerfTests.PropellantLookup_RepeatedCalls_AreBitIdentical`
// which assume the flag is stable for the duration of a single
// `[Fact]`.
//
// Tests participating in this collection run sequentially (xUnit
// guarantee). Tests OUTSIDE the collection still run in parallel with
// each other, so the global serialisation cost is bounded to the
// handful of classes that actually touch shared state.
//
// Defense-in-depth: every class in this collection that mutates
// UseEquilibrium / EquilibriumCorrectionProvider must also wrap the
// mutation in try/finally and restore the prior value on exit. Even
// with sequential execution, a test that throws mid-way must not leak
// modified global state into the next class.

using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Voxelforge.Tests;

// xUnit convention: a class annotated with [CollectionDefinition]
// declares the collection. The class itself is purely a marker — its
// type identity is unused beyond joining tests to the named
// collection. The "Collection" suffix is idiomatic for this role; CA1711
// (rename-to-not-end-in-Collection) is suppressed inline.
[CollectionDefinition(PropellantTablesGlobalStateCollection.Name, DisableParallelization = true)]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit collection-definition classes idiomatically end in 'Collection' so the [Collection(name)] attribute and the class name share a semantic anchor.")]
public sealed class PropellantTablesGlobalStateCollection
{
    public const string Name = "PropellantTablesGlobalState";
}
