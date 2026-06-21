// Vfd016AnalyzerTests — issue #823.
//
// VFD016: Any invocation of the form `MathF.Clamp(...)` is an error.
// System.MathF has no Clamp overload; the correct alternative is
// Math.Clamp(value, min, max), which has float/double/decimal/int
// overloads since .NET Core 2.0 / .NET Standard 2.1.
//
// Unlike VFD001-015, VFD016 is a GLOBAL rule (not scoped to
// [Deterministic] / IObjective).  It is implemented as a syntax-level
// check so it fires regardless of whether the call-site compiles.
//
// CI note: the `SA_Solve_StaysWithinBudget(Maximum)` test in
// SaLatencyBudgetTests is a separate, pre-existing flaky test (CPU contention
// on the shared runner — same pattern as PRs #674 and #830). Its budget has
// been raised to 30 s; it does not affect VFD016 tests.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class Vfd016AnalyzerTests
{
    private const string MathFStubsSource = """
        namespace VoxelforgeTestStubs
        {
            public static class MathF
            {
                public static float Clamp(float v, float a, float b) => v;
                public static float Abs(float v) => v < 0f ? -v : v;
            }
            public static class Math
            {
                public static float Clamp(float v, float a, float b) => v;
            }
        }
        """;

    private static async Task RunAsync(string testSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeterministicAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.TestState.Sources.Add(("MathFStubs.cs", MathFStubsSource));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    // ── Positive — fires ─────────────────────────────────────────────

    [Fact]
    public async Task MathFClamp_InMethodBody_Fires()
    {
        var src = """
            using VoxelforgeTestStubs;

            class C
            {
                float Clamp01(float x) => MathF.Clamp(x, 0f, 1f);
            }
            """;
        await RunAsync(src, new DiagnosticResult(DeterministicAnalyzer.Vfd016)
            .WithLocation(5, 31));
    }

    [Fact]
    public async Task MathFClamp_AsStatement_Fires()
    {
        var src = """
            using VoxelforgeTestStubs;

            class C
            {
                void Foo(float x)
                {
                    _ = MathF.Clamp(x, -1f, 1f);
                }
            }
            """;
        await RunAsync(src, new DiagnosticResult(DeterministicAnalyzer.Vfd016)
            .WithLocation(7, 13));
    }

    [Fact]
    public async Task MathFClamp_InStaticMethod_Fires()
    {
        var src = """
            using VoxelforgeTestStubs;

            static class Helper
            {
                public static float Saturate(float v) => MathF.Clamp(v, 0f, 1f);
            }
            """;
        await RunAsync(src, new DiagnosticResult(DeterministicAnalyzer.Vfd016)
            .WithLocation(5, 46));
    }

    [Fact]
    public async Task QualifiedMathFClamp_Fires()
    {
        // Namespace-qualified `<ns>.MathF.Clamp(...)` — the receiver of `.Clamp`
        // is a MemberAccessExpressionSyntax whose trailing name is MathF, not a
        // bare IdentifierNameSyntax. Before the fix the analyzer required the
        // bare form and missed this, leaving only the raw CS0117. Fail-on-old /
        // pass-on-new: the pre-fix analyzer emits no VFD016 and this expectation
        // fails; the fixed analyzer surfaces the actionable message.
        var src = """
            using VoxelforgeTestStubs;

            class C
            {
                float Clamp01(float x) => VoxelforgeTestStubs.MathF.Clamp(x, 0f, 1f);
            }
            """;
        await RunAsync(src, new DiagnosticResult(DeterministicAnalyzer.Vfd016)
            .WithLocation(5, 31));
    }

    // ── Negative — does NOT fire ──────────────────────────────────────

    [Fact]
    public async Task MathClamp_DoesNotFire()
    {
        var src = """
            using VoxelforgeTestStubs;

            class C
            {
                float Saturate(float x) => Math.Clamp(x, 0f, 1f);
            }
            """;
        await RunAsync(src);
    }

    [Fact]
    public async Task MathFAbs_DoesNotFire()
    {
        var src = """
            using VoxelforgeTestStubs;

            class C
            {
                float AbsVal(float x) => MathF.Abs(x);
            }
            """;
        await RunAsync(src);
    }

    [Fact]
    public async Task OtherTypeClamp_DoesNotFire()
    {
        var src = """
            class MathUtils
            {
                public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
            }

            class C
            {
                float Foo(float x) => MathUtils.Clamp(x, 0f, 1f);
            }
            """;
        await RunAsync(src);
    }
}
