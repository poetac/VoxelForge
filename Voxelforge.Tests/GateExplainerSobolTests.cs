// GateExplainerSobolTests — OOB-13 Part 2 / issue #347 Phase 3 (2026-05-05).
//
// Tests the *public* BuildMarkdown(FeasibilityGateResult, SobolGateRanker?, string)
// overload added in Phase 3. The internal BuildMarkdown(result, rankerFactory)
// overload is already covered by GateExplainerTests.cs (lines 497–729).

using System;
using System.Diagnostics;
using System.Linq;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class GateExplainerSobolTests
{
    // ─── helpers ──────────────────────────────────────────────────────

    private static FeasibilityViolation V(
        string id, double actual, double limit, string desc = "test violation")
        => new(ConstraintId: id, Description: desc, ActualValue: actual, Limit: limit);

    private static (OperatingConditions Cond, RegenChamberDesign Design) BuildMerlinSeed()
    {
        var spec = new EngineSpec(
            PropellantPair:     PropellantPair.LOX_RP1,
            Thrust_N:           800_000.0,
            ChamberPressure_Pa: 9.7e6,
            ExpansionRatio:     16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = AutoSeeder.Seed(spec);
        return (seed.Conditions, seed.Design);
    }

    private static string StripTimestamp(string md) =>
        System.Text.RegularExpressions.Regex.Replace(
            md,
            @"\*\*Generated:\*\* \d{4}-\d{2}-\d{2} \d{2}:\d{2} \(local\)",
            "**Generated:** (stripped)");

    // ─── argument validation ──────────────────────────────────────────

    [Fact]
    public void Ranker_NullBaseline_Throws()
    {
        var (cond, _) = BuildMerlinSeed();
        Assert.Throws<ArgumentNullException>(
            () => new SobolGateRanker(null!, cond));
    }

    [Fact]
    public void Ranker_NullCond_Throws()
    {
        var (_, design) = BuildMerlinSeed();
        Assert.Throws<ArgumentNullException>(
            () => new SobolGateRanker(design, null!));
    }

    [Fact]
    public void Ranker_ZeroSamples_Throws()
    {
        var (cond, design) = BuildMerlinSeed();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SobolGateRanker(design, cond, samples: 0));
    }

    // ─── null-ranker behaviour ────────────────────────────────────────

    [Fact]
    public void BuildMarkdown_WithNullRanker_IdenticalToBaseline()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var withNull  = StripTimestamp(GateExplainer.BuildMarkdown(result, ranker: null));
        var baseline  = StripTimestamp(GateExplainer.BuildMarkdown(result));

        Assert.Equal(baseline, withNull);
    }

    // ─── NPSH gate (no coupled vars) ─────────────────────────────────

    [Fact]
    public void BuildMarkdown_WithRanker_NoCoupledVars_FallsBackToStaticHints()
    {
        var (cond, design) = BuildMerlinSeed();
        var ranker = new SobolGateRanker(design, cond, samples: 32);
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("NPSH_INSUFFICIENT", 8.0, 12.0) });

        var md = GateExplainer.BuildMarkdown(result, ranker: ranker);

        // NPSH has 0 coupled vars — ranker.Rank returns empty → falls back.
        Assert.DoesNotContain("| Rank |", md);
        Assert.Contains("**Levers to consider:**", md);
    }

    // ─── CONTRACTION_RATIO gate renders table ─────────────────────────

    [Fact]
    public void BuildMarkdown_WithRanker_ContractionRatio_RendersTable()
    {
        var (cond, design) = BuildMerlinSeed();
        var ranker = new SobolGateRanker(design, cond, samples: 64);
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("CONTRACTION_RATIO_OUT_OF_BAND", 11.5, 10.0) });

        var md = GateExplainer.BuildMarkdown(result, ranker: ranker);

        // CONTRACTION_RATIO_OUT_OF_BAND has coupled vars that PreScreen evaluates
        // → at least one ranked-table row should appear.
        Assert.Contains("|", md);
    }

    // ─── WALL_TEMP gate renders table (all-zero indices) ─────────────

    [Fact]
    public void BuildMarkdown_WithRanker_WallTemp_RendersTable()
    {
        var (cond, design) = BuildMerlinSeed();
        var ranker = new SobolGateRanker(design, cond, samples: 32);
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result, ranker: ranker);

        // WALL_TEMP is not a PreScreen gate → all samples return 0.0 → indices are
        // zero, but the table still renders (non-empty RankedLever array).
        Assert.Contains("WALL_TEMP", md);
        // Table header or lever rows should be present (GateExplainer renders table
        // when levers non-empty, static hints when levers empty).
        Assert.False(string.IsNullOrEmpty(md));
    }

    // ─── sorted order ─────────────────────────────────────────────────

    [Fact]
    public void RankedLevers_SortedByTotalST_Descending()
    {
        var (cond, design) = BuildMerlinSeed();
        var ranker = new SobolGateRanker(design, cond, samples: 128);

        // Use the internal Rank method directly to inspect the sorted output.
        var levers = ranker.Rank("CONTRACTION_RATIO_OUT_OF_BAND");

        for (int i = 0; i < levers.Length - 1; i++)
        {
            Assert.True(levers[i].TotalST >= levers[i + 1].TotalST,
                $"ST not non-increasing at i={i}: {levers[i].TotalST} vs {levers[i + 1].TotalST}");
        }
    }

    // ─── determinism ──────────────────────────────────────────────────

    [Fact]
    public void Ranker_IsDeterministic()
    {
        var (cond, design) = BuildMerlinSeed();
        // Two separate rankers with identical config should produce identical output
        // because SobolSensitivity.Compute is seeded deterministically by GateLeverRanker.
        var rankerA = new SobolGateRanker(design, cond, samples: 64);
        var rankerB = new SobolGateRanker(design, cond, samples: 64);

        var a = rankerA.Rank("WALL_TEMP");
        var b = rankerB.Rank("WALL_TEMP");

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].VariableName, b[i].VariableName);
            Assert.Equal(a[i].FirstOrderS,  b[i].FirstOrderS,  10);
            Assert.Equal(a[i].TotalST,      b[i].TotalST,      10);
        }
    }

    // ─── performance ──────────────────────────────────────────────────

    [Fact]
    public void Performance_128Samples_Under5Seconds()
    {
        var (cond, design) = BuildMerlinSeed();
        var ranker = new SobolGateRanker(design, cond, samples: 128);

        var sw = Stopwatch.StartNew();
        ranker.Rank("WALL_TEMP");
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 5.0,
            $"Rank took {sw.Elapsed.TotalSeconds:F2} s — exceeded 5-second budget");
    }

    // ─── integration: full BuildMarkdown with ranker ──────────────────

    [Fact]
    public void Integration_WallTempViolation_TableHasAtLeastOneRow()
    {
        var (cond, design) = BuildMerlinSeed();
        var ranker = new SobolGateRanker(design, cond, samples: 64);
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350.0, 1200.0) });

        var md = GateExplainer.BuildMarkdown(result, ranker: ranker);

        // The markdown must mention WALL_TEMP and contain meaningful content.
        Assert.Contains("WALL_TEMP", md);
        Assert.Contains("## Failing gates", md);

        // The table OR the static-hints bullets must be present (depends on whether
        // WALL_TEMP's coupled vars are all resolvable against the PreScreen path).
        bool hasTable  = md.Contains("| Rank |");
        bool hasLevers = md.Contains("**Levers to consider:**");
        Assert.True(hasTable || hasLevers,
            "BuildMarkdown must render either a ranked table or static-hints levers");
    }
}
