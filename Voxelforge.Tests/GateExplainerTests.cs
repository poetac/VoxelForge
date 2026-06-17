using Voxelforge.Injector;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class GateExplainerTests
{
    private static FeasibilityViolation V(
        string id, double actual, double limit, string desc = "test description")
        => new(ConstraintId: id, Description: desc, ActualValue: actual, Limit: limit);

    // ─────────────────────────────────────────────────────────────────
    //  Per-gate Explain tests (15 covered ConstraintIds)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Explain_WallTemp_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("WALL_TEMP", 1350.0, 1200.0));
        Assert.Equal("WALL_TEMP", ex.ConstraintId);
        Assert.Contains("wall T", ex.ShortDescription);
        Assert.Equal("1350 K", ex.OffendingValue);
        Assert.Equal("1200 K", ex.Limit);
        Assert.True(ex.Levers.Count >= 2, "WALL_TEMP should have at least 2 levers");
        Assert.Contains(ex.Levers, l => l.Contains("ChannelHeight"));
        Assert.Contains(ex.Levers, l => l.Contains("WallMaterialIndex"));
        Assert.Equal("ADR-009 / Sprint 1", ex.ReferenceDoc);
    }

    [Fact]
    public void Explain_YieldExceeded_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("YIELD_EXCEEDED", 0.85, 1.0));
        Assert.Equal("YIELD_EXCEEDED", ex.ConstraintId);
        Assert.Contains("0.85 (safety factor)", ex.OffendingValue);
        Assert.Contains("1 (safety factor)", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("GasSideWallThickness_mm"));
        Assert.Contains(ex.Levers, l => l.Contains("RibThickness_mm"));
    }

    [Fact]
    public void Explain_FeatureTooSmall_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("FEATURE_TOO_SMALL", 0.18, 0.30));
        Assert.Equal("FEATURE_TOO_SMALL", ex.ConstraintId);
        Assert.Contains("0.18 mm", ex.OffendingValue);
        Assert.Contains(ex.Levers, l => l.Contains("RibThickness_mm") || l.Contains("ChannelCount"));
    }

    [Fact]
    public void Explain_CoolantTExceeded_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("COOLANT_T_EXCEEDED", 580.0, 500.0));
        Assert.Contains("580 K", ex.OffendingValue);
        Assert.Contains("500 K", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("ChannelHeight") || l.Contains("ChannelCount"));
    }

    [Fact]
    public void Explain_InjectorFaceTExceeded_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("INJECTOR_FACE_T_EXCEEDED", 1300.0, 1200.0));
        Assert.Contains("burnout", ex.ShortDescription);
        Assert.Contains("1300 K", ex.OffendingValue);
        Assert.Contains(ex.Levers, l => l.Contains("ElementCount") || l.Contains("FilmFuelFraction"));
    }

    [Fact]
    public void Explain_ElementDensityTooHigh_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("ELEMENT_DENSITY_TOO_HIGH", 0.95, 0.7));
        Assert.Contains("0.95 / cm²", ex.OffendingValue);
        Assert.Contains("0.7 / cm²", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("ElementCount"));
    }

    [Fact]
    public void Explain_PintleBlockage_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("PINTLE_BLOCKAGE_OUT_OF_BAND", 0.95, 0.90));
        Assert.Contains("(blockage)", ex.OffendingValue);
        Assert.Contains(ex.Levers, l => l.Contains("PintleDiameter_mm"));
        Assert.Contains(ex.Levers, l => l.Contains("PintleSleeveHoleCount"));
    }

    [Fact]
    public void Explain_BurstMargin_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("BURST_MARGIN_INSUFFICIENT", 2.0, 2.5));
        Assert.Contains("2× MEOP", ex.OffendingValue);
        Assert.Contains("2.5× MEOP", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("GasSideWallThickness_mm") || l.Contains("OuterJacketThickness_mm"));
        Assert.Contains(ex.Levers, l => l.Contains("WallMaterialIndex"));
    }

    [Fact]
    public void Explain_LcfLifeInsufficient_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("LCF_LIFE_INSUFFICIENT", 80.0, 400.0));
        Assert.Contains("Coffin-Manson", ex.ShortDescription);
        Assert.Contains("80 cycles", ex.OffendingValue);
        Assert.Contains("400 cycles", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("ΔT") || l.Contains("MissionCycles"));
    }

    [Fact]
    public void Explain_NpshInsufficient_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("NPSH_INSUFFICIENT", 8.0, 12.0));
        Assert.Contains("8 m (NPSHA)", ex.OffendingValue);
        Assert.Contains("12 m (NPSHA)", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("ullage") || l.Contains("inducer"));
    }

    [Fact]
    public void Explain_FeedPressureInsufficient_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("FEED_PRESSURE_INSUFFICIENT", 8.5e6, 10e6));
        Assert.Contains("Pa", ex.OffendingValue);
        Assert.Contains(ex.Levers, l => l.Contains("ullage"));
    }

    [Fact]
    public void Explain_TpmsCellTooSmall_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("TPMS_CELL_FEATURE_TOO_SMALL", 0.45, 0.6));
        Assert.Contains("0.45 mm", ex.OffendingValue);
        Assert.Contains(ex.Levers, l => l.Contains("TpmsCellEdge_mm"));
        Assert.Contains(ex.Levers, l => l.Contains("TpmsSolidFraction"));
    }

    [Fact]
    public void Explain_OverhangAngleExceeded_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("OVERHANG_ANGLE_EXCEEDED", 30.0, 45.0));
        Assert.Contains("30°", ex.OffendingValue);
        Assert.Contains("45°", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("orient") || l.Contains("supports") || l.Contains("BellExitAngle_deg"));
    }

    [Fact]
    public void Explain_ContractionRatioOutOfBand_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("CONTRACTION_RATIO_OUT_OF_BAND", 12.0, 10.0));
        Assert.Contains("12 (ratio)", ex.OffendingValue);
        Assert.Contains("10 (ratio)", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("ContractionRatio"));
        Assert.Contains(ex.Levers, l => l.Contains("Mach"));
    }

    [Fact]
    public void Explain_LStarBelowPropellantMin_ReturnsCuratedHints()
    {
        var ex = GateExplainer.Explain(V("L_STAR_BELOW_PROPELLANT_MIN", 0.65, 0.83));
        Assert.Contains("0.65 m", ex.OffendingValue);
        Assert.Contains("0.83 m", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("CharacteristicLength_m"));
    }

    // ─────────────────────────────────────────────────────────────────
    //  CONTRACTION_RATIO regression test (issue #202 acceptance criterion)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Explain_ContractionRatio_AboveBand_AcceptanceCriterion()
    {
        var ex = GateExplainer.Explain(V("CONTRACTION_RATIO_OUT_OF_BAND", 12.0, 10.0,
            desc: "Contraction ratio ε_c = 12.00 above practical band [2.5, 10.0]"));

        Assert.Equal("CONTRACTION_RATIO_OUT_OF_BAND", ex.ConstraintId);
        Assert.Equal("12 (ratio)", ex.OffendingValue);
        Assert.Equal("10 (ratio)", ex.Limit);
        Assert.Contains(ex.Levers, l => l.Contains("decrease ContractionRatio"));
        Assert.Contains("Sutton", ex.ShortDescription);
        Assert.Equal("ADR-009 / PH-17 Sprint 36", ex.ReferenceDoc);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Fallback tests
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Explain_UncoveredButRegisteredGate_FallsBackToAdrRef()
    {
        // TURBINE_UNCHOKED is registered in GateRegistry but not in the
        // 15-gate first slice.
        var ex = GateExplainer.Explain(V("TURBINE_UNCHOKED", 0.95, 0.55));

        Assert.Equal("TURBINE_UNCHOKED", ex.ConstraintId);
        Assert.Contains("No detailed lever guidance", ex.Levers[0]);
        Assert.NotEqual("(unregistered)", ex.ReferenceDoc);
        Assert.Contains(ex.Levers[0], ex.Levers[0]); // sanity
        Assert.Contains(ex.ReferenceDoc, ex.Levers[0]); // fallback names the AdrRef
    }

    [Fact]
    public void Explain_UnregisteredGate_FallsBackToUnregisteredMarker()
    {
        var ex = GateExplainer.Explain(V("MADE_UP_GATE_DOES_NOT_EXIST", 1.0, 2.0));
        Assert.Equal("(unregistered)", ex.ReferenceDoc);
        Assert.Contains("No detailed lever guidance", ex.Levers[0]);
    }

    [Fact]
    public void Explain_NaNValues_RenderEmDash()
    {
        // Categorical gate (e.g., STABILITY_FAIL) carries NaN ActualValue +
        // Limit. Fallback to em-dash so renderers don't show "NaN".
        var ex = GateExplainer.Explain(V("STABILITY_FAIL", double.NaN, double.NaN));
        Assert.Equal("—", ex.OffendingValue);
        Assert.Equal("—", ex.Limit);
    }

    // ─────────────────────────────────────────────────────────────────
    //  BuildMarkdown end-to-end + structural tests
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildMarkdown_MultipleViolations_RendersAllSections()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[]
            {
                V("WALL_TEMP", 1350, 1200),
                V("NPSH_INSUFFICIENT", 8.0, 12.0),
                V("CONTRACTION_RATIO_OUT_OF_BAND", 12.0, 10.0),
            });

        var md = GateExplainer.BuildMarkdown(result);

        Assert.Contains("# Feasibility Gates — Causal Explainer", md);
        Assert.Contains("**Status:** FAIL — 3 gate(s) failing", md);
        Assert.Contains($"**Schema:** {GateExplainer.ReportSchemaVersion}", md);
        Assert.Contains("## Failing gates", md);
        Assert.Contains("### WALL_TEMP", md);
        Assert.Contains("### NPSH_INSUFFICIENT", md);
        Assert.Contains("### CONTRACTION_RATIO_OUT_OF_BAND", md);
        Assert.Contains("## Recommended next steps", md);
        Assert.Contains("- Address **WALL_TEMP**:", md);
        Assert.Contains("- Address **NPSH_INSUFFICIENT**:", md);
        Assert.Contains("- Address **CONTRACTION_RATIO_OUT_OF_BAND**:", md);
    }

    [Fact]
    public void BuildMarkdown_DesignHash_AppearsInHeader()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result, designHash: "sha-abc123");
        Assert.Contains("**Design hash:** `sha-abc123`", md);
    }

    [Fact]
    public void BuildMarkdown_EmptyDesignHash_OmitsHashLine()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result);
        Assert.DoesNotContain("**Design hash:**", md);
    }

    [Fact]
    public void BuildMarkdown_PassingResult_RendersShortBannerOnly()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: true,
            Violations: System.Array.Empty<FeasibilityViolation>());

        var md = GateExplainer.BuildMarkdown(result);

        Assert.Contains("**Status:** PASS", md);
        Assert.Contains("No failing gates", md);
        Assert.DoesNotContain("## Failing gates", md);
        Assert.DoesNotContain("## Recommended next steps", md);
    }

    [Fact]
    public void BuildMarkdown_IsDeterministic_ApartFromTimestamp()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[]
            {
                V("WALL_TEMP", 1350, 1200),
                V("YIELD_EXCEEDED", 0.85, 1.0),
            });

        string a = StripTimestamp(GateExplainer.BuildMarkdown(result, designHash: "x"));
        string b = StripTimestamp(GateExplainer.BuildMarkdown(result, designHash: "x"));

        Assert.Equal(a, b);

        static string StripTimestamp(string md)
        {
            var lines = md.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith("**Generated:**", System.StringComparison.Ordinal))
                    lines[i] = "**Generated:** <stripped>";
            return string.Join('\n', lines);
        }
    }

    [Fact]
    public void BuildMarkdown_IncludesViolationDescription()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[]
            {
                V("WALL_TEMP", 1350, 1200, desc: "Peak gas-side wall T 1350 K > CuCrZr service limit 1200 K."),
            });

        var md = GateExplainer.BuildMarkdown(result);
        Assert.Contains("Peak gas-side wall T 1350 K", md);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Registry-invariant test: every covered gate exists in GateRegistry
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AllCoveredGates_ExistInGateRegistry()
    {
        var coveredIds = new[]
        {
            "WALL_TEMP", "YIELD_EXCEEDED", "FEATURE_TOO_SMALL", "COOLANT_T_EXCEEDED",
            "INJECTOR_FACE_T_EXCEEDED", "ELEMENT_DENSITY_TOO_HIGH", "PINTLE_BLOCKAGE_OUT_OF_BAND",
            "BURST_MARGIN_INSUFFICIENT", "LCF_LIFE_INSUFFICIENT",
            "NPSH_INSUFFICIENT", "FEED_PRESSURE_INSUFFICIENT",
            "TPMS_CELL_FEATURE_TOO_SMALL", "OVERHANG_ANGLE_EXCEEDED",
            "CONTRACTION_RATIO_OUT_OF_BAND", "L_STAR_BELOW_PROPELLANT_MIN",
        };
        foreach (var id in coveredIds)
        {
            Assert.True(GateRegistry.TryGetById(id, out _),
                $"GateExplainer covers '{id}' but GateRegistry no longer registers it.");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 1 of issue #347: gate→variable coupling map tests
    // ─────────────────────────────────────────────────────────────────

    private static readonly string[] CoveredHintGateIds = new[]
    {
        "WALL_TEMP", "YIELD_EXCEEDED", "FEATURE_TOO_SMALL", "COOLANT_T_EXCEEDED",
        "INJECTOR_FACE_T_EXCEEDED", "ELEMENT_DENSITY_TOO_HIGH", "PINTLE_BLOCKAGE_OUT_OF_BAND",
        "BURST_MARGIN_INSUFFICIENT", "LCF_LIFE_INSUFFICIENT",
        "NPSH_INSUFFICIENT", "FEED_PRESSURE_INSUFFICIENT",
        "TPMS_CELL_FEATURE_TOO_SMALL", "OVERHANG_ANGLE_EXCEEDED",
        "CONTRACTION_RATIO_OUT_OF_BAND", "L_STAR_BELOW_PROPELLANT_MIN",
    };

    [Fact]
    public void Explain_PopulatesCoupledVariablesForCoveredGates()
    {
        // The two gates whose levers are entirely OperatingConditions / pump-
        // preset-side (NPSH ullage, suction-line, inducer; feed-pressure
        // ullage, FeedStackup ΔP allocations) carry empty / minimal
        // coupled-variable lists by design.
        var emptyish = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "NPSH_INSUFFICIENT",
            "FEED_PRESSURE_INSUFFICIENT",
        };

        foreach (var id in CoveredHintGateIds)
        {
            var ex = GateExplainer.Explain(V(id, 1.0, 1.0));
            if (emptyish.Contains(id))
            {
                Assert.True(ex.CoupledVariables.Count <= 1,
                    $"{id} expected ≤1 SA-coupled variables (mostly OperatingConditions levers); "
                  + $"got {ex.CoupledVariables.Count}.");
            }
            else
            {
                Assert.True(ex.CoupledVariables.Count >= 1,
                    $"{id} should have at least one SA-coupled variable (Phase 2 Sobol input).");
            }
        }
    }

    [Fact]
    public void CoupledVariables_AllResolveThroughDesignVariableRegistry()
    {
        // Static-init validation runs the same check, but exposing it as a
        // unit test gives a friendlier failure message and exercises the
        // public contract directly.
        var saVarNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var v in DesignVariableRegistry.For(typeof(RegenChamberDesign)))
            saVarNames.Add(v.MemberName);
        foreach (var v in DesignVariableRegistry.For(typeof(InjectorPattern)))
            saVarNames.Add(v.MemberName);

        foreach (var id in CoveredHintGateIds)
        {
            var coupled = GateExplainer.GetCoupledVariables(id);
            foreach (var name in coupled)
                Assert.Contains(name, saVarNames);
        }
    }

    [Fact]
    public void GetCoupledVariables_AgreesWithExplain()
    {
        foreach (var id in CoveredHintGateIds)
        {
            var fromExplain = GateExplainer.Explain(V(id, 1.0, 1.0)).CoupledVariables;
            var direct      = GateExplainer.GetCoupledVariables(id);
            Assert.Equal(fromExplain, direct);
        }
    }

    [Fact]
    public void GetCoupledVariables_UncoveredGate_ReturnsEmpty()
    {
        // Uncovered-but-registered gate: TURBINE_UNCHOKED is in GateRegistry
        // but not in the explainer hint table.
        Assert.Empty(GateExplainer.GetCoupledVariables("TURBINE_UNCHOKED"));
        // Wholly unregistered gate.
        Assert.Empty(GateExplainer.GetCoupledVariables("MADE_UP_DOES_NOT_EXIST"));
    }

    [Fact]
    public void Explain_UncoveredGate_ReturnsEmptyCoupledVariables()
    {
        var ex = GateExplainer.Explain(V("TURBINE_UNCHOKED", 0.95, 0.55));
        Assert.Empty(ex.CoupledVariables);
    }

    [Fact]
    public void Explain_WallTemp_CouplesAllChannelHeights()
    {
        var coupled = GateExplainer.GetCoupledVariables("WALL_TEMP");
        Assert.Contains(nameof(RegenChamberDesign.ChannelHeightChamber_mm), coupled);
        Assert.Contains(nameof(RegenChamberDesign.ChannelHeightThroat_mm),  coupled);
        Assert.Contains(nameof(RegenChamberDesign.ChannelHeightExit_mm),    coupled);
        Assert.Contains(nameof(RegenChamberDesign.GasSideWallThickness_mm), coupled);
    }

    [Fact]
    public void Explain_InjectorFace_CouplesElementCountAndContractionRatio()
    {
        var coupled = GateExplainer.GetCoupledVariables("INJECTOR_FACE_T_EXCEEDED");
        Assert.Contains(nameof(InjectorPattern.ElementCount),        coupled);
        Assert.Contains(nameof(RegenChamberDesign.ContractionRatio), coupled);
        Assert.Contains(nameof(RegenChamberDesign.FilmFuelFraction), coupled);
    }

    [Fact]
    public void Explain_TpmsFeatureTooSmall_CouplesEdgeAndSolidFraction()
    {
        var coupled = GateExplainer.GetCoupledVariables("TPMS_CELL_FEATURE_TOO_SMALL");
        Assert.Contains(nameof(RegenChamberDesign.TpmsCellEdge_mm),     coupled);
        Assert.Contains(nameof(RegenChamberDesign.TpmsSolidFraction),   coupled);
    }

    [Fact]
    public void BuildMarkdown_RendersCoupledVariablesLine()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result);

        Assert.Contains("**Coupled SA variables:**", md);
        Assert.Contains(nameof(RegenChamberDesign.ChannelHeightThroat_mm), md);
        Assert.Contains(nameof(RegenChamberDesign.GasSideWallThickness_mm), md);
    }

    [Fact]
    public void BuildMarkdown_OmitsCoupledLineWhenEmpty()
    {
        // NPSH_INSUFFICIENT carries an empty CoupledVariables list (all
        // physical levers live on OperatingConditions). The render should
        // skip the line entirely rather than show "**Coupled SA variables:** ".
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("NPSH_INSUFFICIENT", 8.0, 12.0) });

        var md = GateExplainer.BuildMarkdown(result);

        // Other sections still render
        Assert.Contains("### NPSH_INSUFFICIENT", md);
        Assert.Contains("**Levers to consider:**", md);
        // But NOT a Coupled SA variables line for this gate
        Assert.DoesNotContain("**Coupled SA variables:**", md);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Phase 3: ranked BuildMarkdown (issue #347 final wiring)
    // ─────────────────────────────────────────────────────────────────

    // Build a rankerFactory that uses a decaying-linear Sobol proxy so
    // dim 0 of each gate's coupled-variable list dominates the ST ranking.
    private static Func<string, RankedLever[]> MakeLinearRankerFactory(int N = 64)
        => id =>
        {
            var coupled = GateExplainer.GetCoupledVariables(id);
            if (coupled.Count == 0) return Array.Empty<RankedLever>();
            var coeffs = Enumerable.Range(0, coupled.Count)
                                   .Select(i => (double)(coupled.Count - i))
                                   .ToArray();
            return GateLeverRanker.Rank(id, pt =>
            {
                double sum = 0;
                for (int j = 0; j < pt.Length; j++) sum += coeffs[j] * pt[j];
                return sum;
            }, N: N, seed: 42);
        };

    private static string StripTimestamp(string md) =>
        System.Text.RegularExpressions.Regex.Replace(
            md,
            @"\*\*Generated:\*\* \d{4}-\d{2}-\d{2} \d{2}:\d{2} \(local\)",
            "**Generated:** (stripped)");

    [Fact]
    public void BuildMarkdown_Ranked_NullFactory_FallsBackToStaticHints()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result, rankerFactory: null);

        Assert.DoesNotContain("| Rank |", md);
        Assert.Contains("**Levers to consider:**", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_EmptyRankerReturn_FallsBackToStaticHints()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result, rankerFactory: _ => Array.Empty<RankedLever>());

        Assert.DoesNotContain("| Rank |", md);
        Assert.Contains("**Levers to consider:**", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_NoCoupledVars_SkipsRanker()
    {
        // NPSH_INSUFFICIENT has 0 coupled vars → rankerFactory must NOT be called.
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("NPSH_INSUFFICIENT", 8.0, 12.0) });

        int callCount = 0;
        var md = GateExplainer.BuildMarkdown(
            result,
            rankerFactory: id => { callCount++; return Array.Empty<RankedLever>(); });

        Assert.Equal(0, callCount);
        Assert.Contains("### NPSH_INSUFFICIENT", md);
        Assert.DoesNotContain("| Rank |", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_SingleGate_RendersTable()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result, MakeLinearRankerFactory());

        Assert.Contains("| Rank |", md);
        Assert.Contains("S_i", md);
        Assert.Contains("ST_i", md);
        Assert.DoesNotContain("**Levers to consider:**", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_TableRowCount_MatchesCoupledVarCount()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        var md = GateExplainer.BuildMarkdown(result, MakeLinearRankerFactory());

        int expectedRows = GateExplainer.GetCoupledVariables("WALL_TEMP").Count;
        // Data rows have the pattern "| {rank number} | ..." — the rank cell starts with spaces then digits.
        int dataRows = System.Text.RegularExpressions.Regex.Matches(
            md, @"^\|\s+\d+\s+\|", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        Assert.Equal(expectedRows, dataRows);
    }

    [Fact]
    public void BuildMarkdown_Ranked_IsDeterministic()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });
        var factory = MakeLinearRankerFactory();

        var md1 = GateExplainer.BuildMarkdown(result, factory);
        var md2 = GateExplainer.BuildMarkdown(result, factory);

        Assert.Equal(StripTimestamp(md1), StripTimestamp(md2));
    }

    [Fact]
    public void BuildMarkdown_Ranked_MaxRankedGatesShortCircuit()
    {
        // 7 violations (all WALL_TEMP, which has coupled vars).
        // maxRankedGates=5 → rankerFactory called for exactly the first 5.
        var violations = Enumerable.Range(0, 7)
            .Select(_ => V("WALL_TEMP", 1350, 1200))
            .ToArray();
        var result = new FeasibilityGateResult(IsFeasible: false, Violations: violations);

        int callCount = 0;
        var baseFactory = MakeLinearRankerFactory();
        Func<string, RankedLever[]> trackingFactory = id =>
        {
            callCount++;
            return baseFactory(id);
        };

        var md = GateExplainer.BuildMarkdown(result, trackingFactory, maxRankedGates: 5);

        Assert.Equal(5, callCount);
        // Violations 5 + 6 must fall back to static hints.
        Assert.Contains("**Levers to consider:**", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_RecommendedNextSteps_UsesTopRankedVar()
    {
        // FEATURE_TOO_SMALL coupled vars: [RibThickness_mm, GasSideWallThickness_mm, ChannelCount].
        // Decaying coefficients put dim 0 (RibThickness_mm) as the dominant variable.
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("FEATURE_TOO_SMALL", 0.18, 0.30) });

        var md = GateExplainer.BuildMarkdown(result, MakeLinearRankerFactory());

        string topVar = GateExplainer.GetCoupledVariables("FEATURE_TOO_SMALL")[0];
        string nextSteps = md.Split("## Recommended next steps")[1];
        Assert.Contains(topVar, nextSteps);
        Assert.Contains("Sobol sensitivity", nextSteps);
    }

    [Fact]
    public void BuildMarkdown_Ranked_PassingResult_NoTableRendered()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: true,
            Violations: Array.Empty<FeasibilityViolation>());

        var md = GateExplainer.BuildMarkdown(result, MakeLinearRankerFactory());

        Assert.Contains("PASS", md);
        Assert.DoesNotContain("| Rank |", md);
        Assert.DoesNotContain("## Failing gates", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_EvalFactoryThrows_FallsBackGracefully()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: false,
            Violations: new[] { V("WALL_TEMP", 1350, 1200) });

        // Factory throws — no exception should propagate; static hints render.
        var md = GateExplainer.BuildMarkdown(
            result,
            rankerFactory: _ => throw new InvalidOperationException("oracle unavailable"));

        Assert.Contains("**Levers to consider:**", md);
        Assert.DoesNotContain("| Rank |", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_DisclaimerDiffersFromExistingOverload()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: true,
            Violations: Array.Empty<FeasibilityViolation>());

        var mdPublic   = GateExplainer.BuildMarkdown(result);
        var mdInternal = GateExplainer.BuildMarkdown(result, rankerFactory: null);

        // New overload says "ST_i"; existing overload does not.
        Assert.Contains("ST_i", mdInternal);
        Assert.DoesNotContain("ST_i", mdPublic);
    }

    [Fact]
    public void BuildMarkdown_Ranked_SchemaVersionUpdated()
    {
        var result = new FeasibilityGateResult(
            IsFeasible: true,
            Violations: Array.Empty<FeasibilityViolation>());

        var md = GateExplainer.BuildMarkdown(result, rankerFactory: null);

        Assert.Contains("v1.2", md);
        Assert.DoesNotContain("v1.1", md);
    }

    [Fact]
    public void BuildMarkdown_Ranked_MultipleGates_MixedRankedAndFallback()
    {
        // 6 violations all WALL_TEMP. Default maxRankedGates=5 → indices 0–4 get
        // the Sobol table; index 5 falls back to static hints.
        var violations = Enumerable.Range(0, 6)
            .Select(_ => V("WALL_TEMP", 1350, 1200))
            .ToArray();
        var result = new FeasibilityGateResult(IsFeasible: false, Violations: violations);

        var md = GateExplainer.BuildMarkdown(result, MakeLinearRankerFactory());

        Assert.Contains("| Rank |", md);           // at least one ranked table
        Assert.Contains("**Levers to consider:**", md); // at least one fallback
    }
}
