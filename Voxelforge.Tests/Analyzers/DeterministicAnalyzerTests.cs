// DeterministicAnalyzerTests — ADR-020 / issue #209.
//
// Pure analyzer tests via Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit.
// Each test feeds a hand-rolled user-source file (with a stub
// Voxelforge.Optimization.DeterministicAttribute as a *separate* source
// file in the same compilation) into the analyzer harness and asserts
// which diagnostics fire (or don't).
//
// No PicoGK, no voxel construction — sidesteps the xUnit + PicoGK
// pitfall that lives in CLAUDE.md.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class DeterministicAnalyzerTests
{
    // Stub of the [Deterministic] marker attribute. Lives in a separate
    // source file added to the test compilation so the user-code's
    // `using` directives can come first (a single file with a leading
    // namespace block + later usings is a CS1529 error).
    private const string AttributeSource = """
        namespace Voxelforge.Optimization
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
            public sealed class DeterministicAttribute : System.Attribute { }
        }
        """;

    // Stub of the IObjective interface (minimal shape — VFD012 only needs
    // the type symbol, not the member surface). Mirrors the real interface
    // in Voxelforge.Core/Optimization/IObjective.cs.
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

    // Diagnostic builders. Line / column refer to lines in the user source
    // (the primary `TestCode`), not the merged compilation — the harness
    // tracks per-file locations by file path.
    private static DiagnosticResult Vfd001(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd001).WithLocation(line, column).WithArguments(member);

    private static DiagnosticResult Vfd002(int line, int column) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd002).WithLocation(line, column);

    private static DiagnosticResult Vfd003(int line, int column) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd003).WithLocation(line, column);

    private static DiagnosticResult Vfd004(int line, int column) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd004).WithLocation(line, column);

    // ── Direct hits ──────────────────────────────────────────────────────

    [Fact]
    public async Task Vfd001_FiresOnDateTimeNow_InsideMarkedClass()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var x = DateTime.Now;
                }
            }
            """;
        // Line 9, column 17: DateTime.Now begins.
        await RunAsync(src, Vfd001(line: 9, column: 17, member: "DateTime.Now"));
    }

    [Fact]
    public async Task Vfd001_FiresOnDateTimeUtcNow_AndDateTimeOffsetUtcNow()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var a = DateTime.UtcNow;
                    var b = DateTimeOffset.UtcNow;
                }
            }
            """;
        await RunAsync(src,
            Vfd001(line: 9, column: 17, member: "DateTime.UtcNow"),
            Vfd001(line: 10, column: 17, member: "DateTimeOffset.UtcNow"));
    }

    [Fact]
    public async Task Vfd002_FiresOnUnseededRandom()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var r = new Random();
                }
            }
            """;
        await RunAsync(src, Vfd002(line: 9, column: 17));
    }

    [Fact]
    public async Task Vfd003_FiresOnGuidNewGuid()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var g = Guid.NewGuid();
                }
            }
            """;
        await RunAsync(src, Vfd003(line: 9, column: 17));
    }

    [Fact]
    public async Task Vfd004_FiresOnEnvironmentTickCount()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var t = Environment.TickCount;
                }
            }
            """;
        await RunAsync(src, Vfd004(line: 9, column: 17));
    }

    // ── Negative cases ───────────────────────────────────────────────────

    [Fact]
    public async Task NoMarking_DoesNotFireOnDateTimeNow()
    {
        var src = """
            using System;

            public class C
            {
                public void M()
                {
                    var x = DateTime.Now;
                }
            }
            """;
        await RunAsync(src);
    }

    [Fact]
    public async Task SeededRandom_DoesNotFireVfd002()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var r = new Random(0);
                }
            }
            """;
        await RunAsync(src);
    }

    [Fact]
    public async Task StopwatchGetTimestamp_DoesNotFireVfd004()
    {
        // ADR-020 narrowing: VFD004 covers Environment.TickCount only.
        // Stopwatch.GetTimestamp() is permitted for elapsed-time
        // instrumentation in optimizer Run methods.
        var src = """
            using System;
            using System.Diagnostics;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var t = Stopwatch.GetTimestamp();
                }
            }
            """;
        await RunAsync(src);
    }

    // ── Method-level marking only ────────────────────────────────────────

    [Fact]
    public async Task MethodLevelMarking_OnlyMarkedMethodFires()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            public class C
            {
                [Deterministic]
                public void M()
                {
                    var x = DateTime.Now;
                }

                public void N()
                {
                    var y = DateTime.Now;
                }
            }
            """;
        await RunAsync(src, Vfd001(line: 9, column: 17, member: "DateTime.Now"));
    }

    // ── Call-graph closure ───────────────────────────────────────────────

    [Fact]
    public async Task CallGraph_TransitiveCallFromMarkedMethod_FiresOnHelper()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            public class C
            {
                [Deterministic]
                public void Run()
                {
                    Helper();
                }

                public void Helper()
                {
                    var x = DateTime.Now;
                }
            }
            """;
        // Diagnostic emitted at Helper's DateTime.Now, since Helper is
        // reachable from the marked Run via the call graph.
        await RunAsync(src, Vfd001(line: 14, column: 17, member: "DateTime.Now"));
    }

    [Fact]
    public async Task CallGraph_LambdaInsideMarkedMethod_Fires()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            public class C
            {
                [Deterministic]
                public void M()
                {
                    System.Action a = () => { var x = DateTime.Now; };
                    a();
                }
            }
            """;
        await RunAsync(src, Vfd001(line: 9, column: 43, member: "DateTime.Now"));
    }

    // ── End-to-end: simulate the real shape with MultiChainOptimizer ─────

    [Fact]
    public async Task EndToEnd_SimulatedMultiChainOptimizer_FiresOnTransitiveDateTimeNow()
    {
        // Mirrors MultiChainOptimizer.Run shape: a [Deterministic] method
        // calling into a helper that reads DateTime.UtcNow. The analyzer
        // must reach the helper through the call graph and emit at the
        // helper's read site.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class MultiChainOptimizer
            {
                public void Run()
                {
                    Initialize();
                }

                private static long Initialize()
                {
                    return DateTime.UtcNow.Ticks;
                }
            }
            """;
        await RunAsync(src, Vfd001(line: 14, column: 16, member: "DateTime.UtcNow"));
    }

    // ── VFD007 — Thread.Sleep / Task.Delay (Sprint H) ──────────────────

    private static DiagnosticResult Vfd007(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd007).WithLocation(line, column).WithArguments(member);

    [Fact]
    public async Task Vfd007_FiresOnThreadSleep_InsideMarkedClass()
    {
        var src = """
            using System.Threading;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class RetryLoop
            {
                public void Run()
                {
                    Thread.Sleep(100);
                }
            }
            """;
        await RunAsync(src, Vfd007(line: 9, column: 9, member: "Thread.Sleep"));
    }

    [Fact]
    public async Task Vfd007_FiresOnTaskDelay_InsideMarkedClass()
    {
        var src = """
            using System.Threading.Tasks;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class AsyncRetryLoop
            {
                public async Task RunAsync()
                {
                    await Task.Delay(100);
                }
            }
            """;
        await RunAsync(src, Vfd007(line: 9, column: 15, member: "Task.Delay"));
    }

    [Fact]
    public async Task Vfd007_SilentOutsideDeterministicScope()
    {
        // Thread.Sleep outside any [Deterministic] surface is legitimate
        // (RetryingObjective.Evaluate uses it on the retry-backoff path).
        // The analyzer should NOT fire on this code.
        var src = """
            using System.Threading;

            public sealed class NormalRetryLoop
            {
                public void Run()
                {
                    Thread.Sleep(100);  // no [Deterministic], no diagnostic
                }
            }
            """;
        await RunAsync(src);  // empty diagnostics array
    }

    [Fact]
    public async Task Vfd007_FiresTransitivelyThroughCallGraph()
    {
        // Mirrors VFD001's transitive-call test pattern: a [Deterministic]
        // method calling into a private helper that uses Thread.Sleep
        // should still fire VFD007 at the helper's call site.
        var src = """
            using System.Threading;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class OuterDeterministic
            {
                public void Run()
                {
                    InnerHelper();
                }

                private static void InnerHelper()
                {
                    Thread.Sleep(50);
                }
            }
            """;
        await RunAsync(src, Vfd007(line: 14, column: 9, member: "Thread.Sleep"));
    }

    // ── VFD008 — Process.Start / File / Directory side effects ────────

    private static DiagnosticResult Vfd008(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd008).WithLocation(line, column).WithArguments(member);

    [Fact]
    public async Task Vfd008_FiresOnProcessStart_InsideMarkedClass()
    {
        var src = """
            using System.Diagnostics;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class SubprocessCaller
            {
                public void Run()
                {
                    Process.Start("voxelforge-eval");
                }
            }
            """;
        await RunAsync(src, Vfd008(line: 9, column: 9, member: "Process.Start"));
    }

    [Fact]
    public async Task Vfd008_FiresOnFileReadAllText()
    {
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class FileReader
            {
                public string Run()
                {
                    return File.ReadAllText("input.json");
                }
            }
            """;
        await RunAsync(src, Vfd008(line: 9, column: 16, member: "File.ReadAllText"));
    }

    [Fact]
    public async Task Vfd008_FiresOnFileAppendAllText()
    {
        // Companion to WriteAllText. A user "fixing" a VFD008 WriteAllText
        // trip by switching to AppendAllText would have silently introduced
        // the same non-determinism shape before this rule landed.
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class FileAppender
            {
                public void Run()
                {
                    File.AppendAllText("trace.log", "tick\n");
                }
            }
            """;
        await RunAsync(src, Vfd008(line: 9, column: 9, member: "File.AppendAllText"));
    }

    [Fact]
    public async Task Vfd008_FiresOnFileWriteAllTextAsync()
    {
        // The Async variant matters because hot-path code on the optimizer
        // side may legitimately want to fire-and-forget a trace write.
        var src = """
            using System.IO;
            using System.Threading.Tasks;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class AsyncWriter
            {
                public Task Run() => File.WriteAllTextAsync("out.json", "{}");
            }
            """;
        await RunAsync(src, Vfd008(line: 8, column: 26, member: "File.WriteAllTextAsync"));
    }

    [Fact]
    public async Task Vfd008_FiresOnDirectoryGetFiles()
    {
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class DirWalker
            {
                public string[] Run()
                {
                    return Directory.GetFiles(".");
                }
            }
            """;
        await RunAsync(src, Vfd008(line: 9, column: 16, member: "Directory.GetFiles"));
    }

    [Fact]
    public async Task Vfd008_SilentOutsideDeterministicScope()
    {
        // The voxelforge-eval subprocess oracle is EXPLICITLY non-deterministic
        // (it shells out to a subprocess). It must NOT be marked [Deterministic].
        // Outside the marker, Process.Start is legitimate and analyzer is silent.
        var src = """
            using System.Diagnostics;

            public sealed class EvalSubprocessOracle
            {
                public void Run()
                {
                    Process.Start("voxelforge-eval");  // legitimate, no [Deterministic]
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    // ── VFD009 — string.GetHashCode() (hash randomization) ────────────

    private static DiagnosticResult Vfd009(int line, int column) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd009).WithLocation(line, column);

    [Fact]
    public async Task Vfd009_FiresOnParameterlessGetHashCode()
    {
        var src = """
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class StringHasher
            {
                public int Hash(string s)
                {
                    return s.GetHashCode();
                }
            }
            """;
        await RunAsync(src, Vfd009(line: 8, column: 16));
    }

    [Fact]
    public async Task Vfd009_SilentOnExplicitStringComparisonOverload()
    {
        // GetHashCode(StringComparison) is explicitly process-stable.
        // Analyzer must NOT fire on this overload — the whole reason
        // the .NET team added it was to give callers a deterministic
        // alternative.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class StableHasher
            {
                public int Hash(string s)
                {
                    return s.GetHashCode(StringComparison.Ordinal);
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    // ── VFD011 — Console.{Write, WriteLine, Read*} ─────────────────────

    private static DiagnosticResult Vfd011(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd011).WithLocation(line, column).WithArguments(member);

    [Fact]
    public async Task Vfd011_FiresOnConsoleWriteLine()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class NoisyOptimizer
            {
                public void Run()
                {
                    Console.WriteLine("starting");
                }
            }
            """;
        await RunAsync(src, Vfd011(line: 9, column: 9, member: "Console.WriteLine"));
    }

    [Fact]
    public async Task Vfd011_FiresOnConsoleReadLine()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class InteractiveOptimizer
            {
                public string Run()
                {
                    return Console.ReadLine() ?? string.Empty;
                }
            }
            """;
        await RunAsync(src, Vfd011(line: 9, column: 16, member: "Console.ReadLine"));
    }

    [Fact]
    public async Task Vfd011_SilentOutsideDeterministicScope()
    {
        // The CLI entry point + bench-diff tool both use Console.WriteLine.
        // Outside the [Deterministic] marker, this is legitimate.
        var src = """
            using System;

            public sealed class Program
            {
                public static void Main()
                {
                    Console.WriteLine("voxelforge");
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    [Fact]
    public async Task Vfd011_FiresOnConsoleErrorWriteLine()
    {
        // The shape that owner-type string-match missed pre-B.7. The
        // TargetMethod here is System.IO.TextWriter.WriteLine — its
        // ContainingType is TextWriter, not Console. Receiver-walk
        // detects Console.Error as the property reference.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class NoisyOptimizer
            {
                public void Run()
                {
                    Console.Error.WriteLine("stderr");
                }
            }
            """;
        // Note: Roslyn 4.12 reports IInvocationOperation.Syntax for chained
        // member-access invocations starting at the outermost receiver
        // (`Console`, col 9) rather than the inner member access
        // (`Error.WriteLine(...)`, col 13). The pre-4.12 location was col 13.
        await RunAsync(src, Vfd011(line: 9, column: 9, member: "Console.Error.WriteLine"));
    }

    [Fact]
    public async Task Vfd011_FiresOnConsoleOutWriteLine()
    {
        // Symmetric coverage: Console.Out.WriteLine routes through the
        // same TextWriter shape as Console.Error.WriteLine.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class NoisyOptimizer
            {
                public void Run()
                {
                    Console.Out.WriteLine("stdout");
                }
            }
            """;
        // Roslyn 4.12 location convention — see Vfd011_FiresOnConsoleErrorWriteLine.
        await RunAsync(src, Vfd011(line: 9, column: 9, member: "Console.Out.WriteLine"));
    }

    [Fact]
    public async Task Vfd011_FiresOnConsoleInReadLine()
    {
        // Console.In returns a TextReader. The instance method ReadLine()
        // routes through TextReader, not Console, so the owner-type
        // string-match would miss it. Receiver-walk catches it.
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class InteractiveOptimizer
            {
                public string Run()
                {
                    return Console.In.ReadLine() ?? string.Empty;
                }
            }
            """;
        // Roslyn 4.12 location convention — see Vfd011_FiresOnConsoleErrorWriteLine.
        // The invocation here starts at `Console` after `return ` (col 16).
        await RunAsync(src, Vfd011(line: 9, column: 16, member: "Console.In.ReadLine"));
    }

    [Fact]
    public async Task Vfd011_FiresOnConsoleErrorWriteLineAsync()
    {
        // Async overloads on TextWriter (WriteLineAsync) are equally
        // captured: the wall-clock-dependent ordering of the continuation
        // is a determinism hazard regardless of which overload is called.
        var src = """
            using System;
            using System.Threading.Tasks;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class NoisyOptimizer
            {
                public async Task Run()
                {
                    await Console.Error.WriteLineAsync("stderr");
                }
            }
            """;
        // Roslyn 4.12 location convention — see Vfd011_FiresOnConsoleErrorWriteLine.
        // The invocation here starts at `Console` after `await ` (col 15).
        await RunAsync(src, Vfd011(line: 10, column: 15, member: "Console.Error.WriteLineAsync"));
    }

    [Fact]
    public async Task Vfd011_SilentOnNonConsoleTextWriter()
    {
        // The fix must not over-match. A TextWriter that isn't Console.Out /
        // Console.Error / Console.In (e.g. an in-memory StringWriter used
        // for legitimate buffered accumulation) MUST NOT fire VFD011.
        var src = """
            using System;
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public sealed class BufferedOptimizer
            {
                public string Run()
                {
                    var buffer = new StringWriter();
                    buffer.WriteLine("captured");
                    return buffer.ToString();
                }
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    // ── VFD012 — Wall-clock reads inside IObjective implementations ──────

    private static DiagnosticResult Vfd012(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd012).WithLocation(line, column).WithArguments(member);

    [Fact]
    public async Task Vfd012_FiresOnDateTimeUtcNow_InsideIObjective()
    {
        // The exact bug shape from issue #510. TeeObjective-style wrapper
        // reads DateTime.UtcNow inside a method on a class implementing
        // IObjective. Nothing on the call chain is [Deterministic] — VFD001
        // alone wouldn't fire, but VFD012 does because the IObjective scope
        // is a structural determinism contract.
        var src = """
            using System;
            using Voxelforge.Optimization;

            public sealed class TeeStyleWrapper : IObjective
            {
                public DateTime Stamp() => DateTime.UtcNow;
            }
            """;
        await RunAsync(src, Vfd012(line: 6, column: 32, member: "DateTime.UtcNow"));
    }

    [Fact]
    public async Task Vfd012_FiresOnEnvironmentTickCount64_InsideIObjective()
    {
        // Coverage extension over VFD004 (which only catches TickCount).
        // TickCount64 has identical determinism shape and was added in .NET 5.
        var src = """
            using System;
            using Voxelforge.Optimization;

            public sealed class TickCountWrapper : IObjective
            {
                public long Stamp() => Environment.TickCount64;
            }
            """;
        await RunAsync(src, Vfd012(line: 6, column: 28, member: "Environment.TickCount64"));
    }

    [Fact]
    public async Task Vfd012_FiresOnStopwatchGetTimestamp_InsideIObjective()
    {
        // ADR-020 exempts Stopwatch.GetTimestamp() at the project level
        // (general perf use is fine in [Deterministic] scope). IObjective
        // scope is stricter: ANY wall-clock read inside Score is a
        // determinism hazard, including Stopwatch.
        var src = """
            using System.Diagnostics;
            using Voxelforge.Optimization;

            public sealed class StopwatchWrapper : IObjective
            {
                public long Stamp() => Stopwatch.GetTimestamp();
            }
            """;
        await RunAsync(src, Vfd012(line: 6, column: 28, member: "Stopwatch.GetTimestamp"));
    }

    [Fact]
    public async Task Vfd012_SilentOutsideIObjective()
    {
        // The same wall-clock reads in a plain class (not an IObjective)
        // must NOT fire VFD012. Legitimate non-objective code reads
        // DateTime.UtcNow constantly (logging, telemetry, scheduled tasks).
        var src = """
            using System;

            public sealed class Telemetry
            {
                public DateTime Stamp() => DateTime.UtcNow;
            }
            """;
        await RunAsync(src);   // empty diagnostics
    }

    [Fact]
    public async Task Vfd012_SuppressedViaSuppressMessage_InsideIObjective()
    {
        // TeeObjective's escape hatch: a wall-clock read inside an
        // IObjective implementation is allowed when explicitly suppressed
        // with [SuppressMessage("Voxelforge.Determinism", "VFD012")] (the
        // category string must match the descriptor's Category constant
        // exactly for Roslyn's suppression matcher to fire). The standard
        // pattern records intentional opt-out at the call site.
        var src = """
            using System;
            using System.Diagnostics.CodeAnalysis;
            using Voxelforge.Optimization;

            public sealed class TeeObjectiveStub : IObjective
            {
                [SuppressMessage("Voxelforge.Determinism", "VFD012",
                    Justification = "Trace records capture wall-clock by contract.")]
                public DateTime Stamp() => DateTime.UtcNow;
            }
            """;
        await RunAsync(src);   // empty diagnostics — suppression honored
    }
}
