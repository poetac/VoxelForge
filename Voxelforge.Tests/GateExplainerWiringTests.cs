// GateExplainerWiringTests.cs — OOB-13 (issue #202) wiring acceptance tests.
//
// These tests mirror the exact call chain in PopulateWarningsPanel:
//   score.FeasibilityViolations
//     → new FeasibilityGateResult(IsFeasible: false, Violations: violations)
//     → GateExplainer.BuildMarkdown(gateResult, r.DesignHash)
//     → appended to txtWarnings.Text
//
// Testing PopulateWarningsPanel directly is impractical (WinForms form
// construction). These tests verify the wiring contract instead: given the
// inputs the wiring passes to BuildMarkdown, the output is what the panel
// would display.
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class GateExplainerWiringTests
{
    [Fact]
    public void WiringPath_WithViolations_BuildMarkdownContainsFailingGatesSection()
    {
        var violations = new[]
        {
            new FeasibilityViolation(
                ConstraintId:  "WALL_TEMP",
                Description:   "Peak wall temperature 1350 K exceeds limit 1200 K",
                ActualValue:   1350.0,
                Limit:         1200.0),
        };
        var gateResult = new FeasibilityGateResult(IsFeasible: false, Violations: violations);

        var md = GateExplainer.BuildMarkdown(gateResult, designHash: "");

        Assert.Contains("## Failing gates", md);
        Assert.Contains("### WALL_TEMP", md);
        Assert.Contains("## Recommended next steps", md);
    }

    [Fact]
    public void WiringPath_NoViolations_BuildMarkdownOmitsFailingGatesSection()
    {
        var gateResult = new FeasibilityGateResult(
            IsFeasible: true,
            Violations: Array.Empty<FeasibilityViolation>());

        var md = GateExplainer.BuildMarkdown(gateResult, designHash: "");

        Assert.DoesNotContain("## Failing gates", md);
        Assert.Contains("PASS", md);
    }
}
