// Vfd005Vfd006AnalyzerTests — ADR-020 VFD005 + VFD006 rules.
//
// VFD005: foreach over Dictionary<,>, HashSet<>, ConcurrentDictionary<,>, ConcurrentBag<>
//         fires inside a [Deterministic] scope (enumeration order is non-deterministic).
// VFD006: Path.GetTempPath/GetRandomFileName/GetTempFileName and
//         Environment.GetEnvironmentVariable are machine-/session-specific.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class Vfd005Vfd006AnalyzerTests
{
    // Stub attribute — mirrors DeterministicAnalyzerTests.cs.
    private const string AttributeSource = """
        namespace Voxelforge.Optimization
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
            public sealed class DeterministicAttribute : System.Attribute { }
        }
        """;

    private static async Task RunAsync(string testSource, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeterministicAnalyzer, DefaultVerifier>
        {
            TestCode = testSource,
        };
        test.TestState.Sources.Add(("DeterministicAttribute.cs", AttributeSource));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Vfd005(int line, int column, string typeName) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd005)
            .WithLocation(line, column)
            .WithArguments(typeName);

    private static DiagnosticResult Vfd006(int line, int column, string member) =>
        new DiagnosticResult(DeterministicAnalyzer.Vfd006)
            .WithLocation(line, column)
            .WithArguments(member);

    // ── VFD005 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForeachOnDictionary_InDeterministicMethod_Fires()
    {
        var src = """
            using System.Collections.Generic;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int>();
                    foreach (var kvp in d)
                    {
                    }
                }
            }
            """;
        // Line 10: "        foreach (var kvp in d)" — 'f' at column 9.
        await RunAsync(src, Vfd005(line: 10, column: 9, typeName: "Dictionary"));
    }

    [Fact]
    public async Task ForeachOnHashSet_InDeterministicMethod_Fires()
    {
        var src = """
            using System.Collections.Generic;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var s = new HashSet<int>();
                    foreach (var x in s)
                    {
                    }
                }
            }
            """;
        // Line 10: "        foreach (var x in s)" — 'f' at column 9.
        await RunAsync(src, Vfd005(line: 10, column: 9, typeName: "HashSet"));
    }

    [Fact]
    public async Task ForeachOnList_InDeterministicMethod_DoesNotFire()
    {
        var src = """
            using System.Collections.Generic;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var lst = new List<int>();
                    foreach (var x in lst)
                    {
                    }
                }
            }
            """;
        await RunAsync(src); // no diagnostics — List<> iteration is deterministic
    }

    [Fact]
    public async Task ForeachOnDictionary_NotInDeterministicMethod_DoesNotFire()
    {
        var src = """
            using System.Collections.Generic;

            public class C
            {
                public void M()
                {
                    var d = new Dictionary<string, int>();
                    foreach (var kvp in d)
                    {
                    }
                }
            }
            """;
        await RunAsync(src); // no [Deterministic] annotation — tainted set is empty
    }

    [Fact]
    public async Task ForeachOnConcurrentDictionary_InDeterministicMethod_Fires()
    {
        var src = """
            using System.Collections.Concurrent;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var d = new ConcurrentDictionary<string, int>();
                    foreach (var kvp in d)
                    {
                    }
                }
            }
            """;
        // Line 10: "        foreach (var kvp in d)" — 'f' at column 9.
        await RunAsync(src, Vfd005(line: 10, column: 9, typeName: "ConcurrentDictionary"));
    }

    // ── VFD006 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PathGetTempPath_InDeterministicMethod_Fires()
    {
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    Path.GetTempPath();
                }
            }
            """;
        // Line 9: "        Path.GetTempPath();" — 'P' at column 9.
        await RunAsync(src, Vfd006(line: 9, column: 9, member: "Path.GetTempPath"));
    }

    [Fact]
    public async Task PathGetRandomFileName_InDeterministicMethod_Fires()
    {
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    Path.GetRandomFileName();
                }
            }
            """;
        // Line 9: "        Path.GetRandomFileName();" — 'P' at column 9.
        await RunAsync(src, Vfd006(line: 9, column: 9, member: "Path.GetRandomFileName"));
    }

    [Fact]
    public async Task PathGetTempFileName_InDeterministicMethod_Fires()
    {
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    Path.GetTempFileName();
                }
            }
            """;
        // Line 9: "        Path.GetTempFileName();" — 'P' at column 9.
        await RunAsync(src, Vfd006(line: 9, column: 9, member: "Path.GetTempFileName"));
    }

    [Fact]
    public async Task PathCombine_InDeterministicMethod_DoesNotFire()
    {
        var src = """
            using System.IO;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    var p = Path.Combine("a", "b");
                }
            }
            """;
        await RunAsync(src); // Path.Combine is deterministic — no diagnostic
    }

    [Fact]
    public async Task EnvironmentGetEnvironmentVariable_InDeterministicMethod_Fires()
    {
        var src = """
            using System;
            using Voxelforge.Optimization;

            [Deterministic]
            public class C
            {
                public void M()
                {
                    Environment.GetEnvironmentVariable("X");
                }
            }
            """;
        // Line 9: "        Environment.GetEnvironmentVariable(...);" — 'E' at column 9.
        await RunAsync(src,
            Vfd006(line: 9, column: 9, member: "Environment.GetEnvironmentVariable"));
    }
}
