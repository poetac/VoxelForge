// Vfd015AnalyzerTests — ADR-020 / issue #565.
//
// VFD015: Array.Sort / List<T>.Sort / Enumerable.OrderBy called inside
// [Deterministic] (or IObjective) scope with a comparer / keySelector
// lambda that has exactly one CompareTo invocation and no fallthrough
// (no conditional / ternary / if-statement). The pattern is the one
// PR 2 (#552) eradicated from CmaEs + NsgaII: a primary-key compare
// with no tie-break, fed to an unstable introsort, produces a
// non-deterministic permutation among tied elements.
//
// The heuristic is deliberately permissive — false positives can be
// suppressed with [SuppressMessage("Voxelforge.Determinism", "VFD015")]
// plus an inline comment explaining why ties are impossible.
//
// Mirrors DeterministicAnalyzerTests.cs / Vfd013AnalyzerTests.cs
// conventions: stub attribute + IObjective interface in side sources,
// hand-rolled user source as TestCode, line/column anchored to user
// source.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class Vfd015AnalyzerTests
{
    // Stub of the [Deterministic] marker attribute — mirrors the other
    // analyzer test files (DeterministicAnalyzerTests, etc.). Separate
    // source file lets user code lead with `using` directives.
    private const string AttributeSource = """
        namespace Voxelforge.Optimization
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
            public sealed class DeterministicAttribute : System.Attribute { }
        }
        """;

    // Stub of the IObjective interface (minimal shape — only the type
    // symbol is needed to wire IObjective-scope detection).
    private const string IObjectiveSource = """
        namespace Voxelforge.Optimization
        {
            public interface IObjective { }
        }
        """;

    private static async Task RunAsync(string testSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeterministicAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.TestState.Sources.Add(("DeterministicAttribute.cs", AttributeSource));
        test.TestState.Sources.Add(("IObjective.cs", IObjectiveSource));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Vfd015(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd015)
            .WithLocation(line, column)
            .WithArguments(member);

    // ── Positive — single CompareTo, no tie-break, inside [Deterministic] ─

    [Fact]
    public async Task Vfd015_FiresOnSingleCompareTo_InDeterministicScope()
    {
        // The exact shape PR 2 fixed in CmaEs / NsgaII: a primary-key
        // CompareTo with no fallback. Unstable introsort + ties → run-to-
        // run non-determinism in the post-sort element order.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void Sort()
                {
                    int[] x = {3, 1, 2};
                    Array.Sort(x, (a, b) => a.CompareTo(b));
                }
            }
            """;
        // Line 10: "        Array.Sort(x, (a, b) => a.CompareTo(b));"
        // — `A` of `Array.Sort` is at column 9.
        await RunAsync(src, Vfd015(line: 10, column: 9, member: "System.Array.Sort"));
    }

    // ── Negative — comparer includes a tie-break (canonical fix) ─────────

    [Fact]
    public async Task Vfd015_DoesNotFire_WithTieBreak()
    {
        // The canonical fix from PR 2: compute the primary CompareTo into
        // a local, then return a ternary that falls back to a deterministic
        // position-anchor CompareTo on ties. The ternary registers as an
        // IConditionalOperation in the lambda body, suppressing the rule.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void Sort()
                {
                    int[] x = {3, 1, 2};
                    Array.Sort(x, (a, b) => { int c = a.CompareTo(b); return c != 0 ? c : a.CompareTo(b); });
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics — tie-break present
    }

    // ── Negative — no comparer lambda (default IComparable sort) ─────────

    [Fact]
    public async Task Vfd015_DoesNotFire_OnDefaultSort()
    {
        // Array.Sort with no comparer falls back to the element type's
        // default IComparable<T>. Whether *that* is deterministic is
        // out-of-scope for VFD015 — the rule targets explicit comparer
        // lambdas that omit a tie-break, not parameterless overloads.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void Sort()
                {
                    int[] x = {3, 1, 2};
                    Array.Sort(x);
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics — no comparer lambda
    }

    // ── Negative — outside [Deterministic] / IObjective scope ────────────

    [Fact]
    public async Task Vfd015_DoesNotFire_OutsideDeterministicScope()
    {
        // The exact same unsafe lambda shape in a plain class (no
        // [Deterministic], no IObjective) is legitimate. VFD015 must
        // only fire when the enclosing method is in the [Deterministic]
        // taint closure OR implements IObjective.
        var src = """
            using System;

            public class C
            {
                public void Sort()
                {
                    int[] x = {3, 1, 2};
                    Array.Sort(x, (a, b) => a.CompareTo(b));
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics — not tainted
    }
}
