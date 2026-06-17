// TestNamingAnalyzerTests — VFA004 (issue #625).
//
// Each test feeds a hand-rolled user-source file into the analyzer
// harness and asserts VFA004 fires when the test method name lacks
// an underscore. Tests cover:
//   • [Fact] method without underscore → fires
//   • [Theory] method without underscore → fires
//   • [Fact] method with underscore → does not fire
//   • Plain helper method (no [Fact]) → does not fire
//   • [Fact] method in a non-Tests.cs file → does not fire (scope filter)
//   • Fully-qualified [Xunit.Fact] attribute → fires (attribute-name matcher)

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Voxelforge.Analyzers;

namespace Voxelforge.Tests.Analyzers;

public sealed class TestNamingAnalyzerTests
{
    private const string XunitStub = """
        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public class TheoryAttribute : System.Attribute { }
        }
        """;

    private static async Task RunAsync(
        string testSource,
        string sourceFileName = "FooTests.cs",
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TestNamingAnalyzer, DefaultVerifier>
        {
            TestState =
            {
                Sources = { (sourceFileName, testSource) },
            },
        };
        test.TestState.Sources.Add(("XunitStub.cs", XunitStub));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static DiagnosticResult Vfa004InFile(string fileName, int line, int column, string methodName, int endColumn) =>
        new DiagnosticResult(TestNamingAnalyzer.Vfa004)
            .WithSpan(fileName, line, column, line, endColumn)
            .WithArguments(methodName);

    [Fact]
    public async Task FactMethod_NoUnderscore_Fires()
    {
        var src = """
            using Xunit;
            public class FooTests
            {
                [Fact]
                public void RecordEqualityIsValueBased() { }
            }
            """;
        await RunAsync(src, sourceFileName: "FooTests.cs",
            Vfa004InFile("FooTests.cs", line: 5, column: 17, methodName: "RecordEqualityIsValueBased", endColumn: 43));
    }

    [Fact]
    public async Task TheoryMethod_NoUnderscore_Fires()
    {
        var src = """
            using Xunit;
            public class BarTests
            {
                [Theory]
                public void DefaultsAreSensible() { }
            }
            """;
        await RunAsync(src, sourceFileName: "BarTests.cs",
            Vfa004InFile("BarTests.cs", line: 5, column: 17, methodName: "DefaultsAreSensible", endColumn: 36));
    }

    [Fact]
    public async Task FactMethod_WithUnderscore_DoesNotFire()
    {
        var src = """
            using Xunit;
            public class FooTests
            {
                [Fact]
                public void Record_Equality_IsValueBased() { }
            }
            """;
        await RunAsync(src, sourceFileName: "FooTests.cs");
    }

    [Fact]
    public async Task NonTestMethod_InTestsFile_DoesNotFire()
    {
        // Plain helper method without [Fact] / [Theory] — out of scope.
        var src = """
            using Xunit;
            public class FooTests
            {
                private static int HelperMethodNoUnderscore() => 42;

                [Fact]
                public void Real_Test_Method() { }
            }
            """;
        await RunAsync(src, sourceFileName: "FooTests.cs");
    }

    [Fact]
    public async Task FactMethod_NonTestsFile_DoesNotFire()
    {
        // Scope filter: rule only fires on files ending with `Tests.cs`.
        // A `[Fact]` method in production source is exotic but not in scope
        // (the rule is about voxelforge's test-naming convention, not
        // about banning [Fact] outside test projects).
        var src = """
            using Xunit;
            public class Foo
            {
                [Fact]
                public void NoUnderscoreHere() { }
            }
            """;
        await RunAsync(src, sourceFileName: "Foo.cs");
    }

    [Fact]
    public async Task FullyQualifiedFactAttribute_Fires()
    {
        // [Xunit.Fact] (fully-qualified) should match same as [Fact].
        var src = """
            public class FooTests
            {
                [Xunit.Fact]
                public void NoUnderscoreFqAttr() { }
            }
            """;
        await RunAsync(src, sourceFileName: "FooTests.cs",
            Vfa004InFile("FooTests.cs", line: 4, column: 17, methodName: "NoUnderscoreFqAttr", endColumn: 35));
    }
}
