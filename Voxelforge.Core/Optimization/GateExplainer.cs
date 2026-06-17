// GateExplainer.cs — OOB-13 first slice (issue #202) + Phase 1-3 of part 2 (issue #347).
//
// Library foundation for the "why did this gate fail?" causal-explainer
// track. Produces a structured GateExplanation per FeasibilityViolation
// (offending value, limit, hand-authored levers, registered AdrRef,
// SA-tunable coupled variables) plus an aggregated Markdown report
// mirroring the SafetyReport / BuildSheet banner template.
//
// Coverage: 15 most-frequently-fired ConstraintIds. Uncovered gates fall
// through to a generic fallback that points the reader at the gate's
// AdrRef from GateRegistry.
//
// Issue #347 Phase 1 — gate→variable coupling map:
// each hint entry now carries a CoupledVariables list naming the
// [SaDesignVariable]-tagged properties on RegenChamberDesign /
// InjectorPattern that physically influence the gate. Validated at
// static-init against DesignVariableRegistry so a future property rename
// can't silently break the map.
//
// Issue #347 Phase 3 — Sobol-ranked lever wiring:
// BuildMarkdown(gateResult, rankerFactory, ...) internal overload accepts
// a Func<string, RankedLever[]> delegate. For each of the top
// maxRankedGates failing gates the delegate is called and the resulting
// Sobol-ordered variable table replaces the hand-authored bullet list.
// Core stays physics-free — the caller supplies the ranking closure.
// Falls back silently to hand-authored hints when the delegate is null,
// returns an empty array, or throws.

using System.Globalization;
using System.Text;
using Voxelforge.Injector;

namespace Voxelforge.Optimization;

/// <summary>
/// Structured explanation of a single failing feasibility gate. Suitable
/// for consumption by a UI panel that wants to render offending value /
/// limit / levers separately, or for direct Markdown rendering via
/// <see cref="GateExplainer.BuildMarkdown"/>.
/// </summary>
/// <param name="ConstraintId">Stable machine-readable gate ID
/// (round-tripped from <see cref="FeasibilityViolation.ConstraintId"/>).</param>
/// <param name="ShortDescription">One-line summary of what failed.</param>
/// <param name="OffendingValue">Pre-formatted offender (e.g., <c>"1350 K"</c>);
/// rendered as em-dash (<c>—</c>) when the underlying ActualValue is NaN
/// (categorical gates).</param>
/// <param name="Limit">Pre-formatted threshold (e.g., <c>"1200 K"</c>);
/// also em-dash when the underlying Limit is NaN.</param>
/// <param name="Levers">2–3 short imperative bullet strings naming
/// <see cref="RegenChamberDesign"/> properties the user can adjust.</param>
/// <param name="ReferenceDoc">The owning gate's <c>AdrRef</c> from
/// <see cref="GateRegistry"/>, or <c>"(unregistered)"</c> if the gate is
/// not in the registry.</param>
/// <param name="CoupledVariables">Names of the
/// <see cref="SaDesignVariableAttribute"/>-tagged properties on
/// <see cref="RegenChamberDesign"/> / <see cref="InjectorPattern"/>
/// that physically influence this gate. Empty when no SA-tunable lever
/// is known (e.g., NPSH gates whose levers are all
/// <see cref="OperatingConditions"/>-side or pump-preset-side and not
/// SA-tagged today). The data layer Phase 2 of issue #347 (Sobol
/// sensitivity ranking) consumes.</param>
public sealed record GateExplanation(
    string ConstraintId,
    string ShortDescription,
    string OffendingValue,
    string Limit,
    IReadOnlyList<string> Levers,
    string ReferenceDoc,
    IReadOnlyList<string> CoupledVariables);

/// <summary>
/// Causal explainer for failing feasibility gates. First slice of OOB-13
/// (issue #202): hand-authored levers for the 15 most-frequently-fired
/// ConstraintIds, with a registry-AdrRef fallback for the rest.
/// </summary>
public static class GateExplainer
{
    public const string ReportSchemaVersion = "v1.2 (2026-05-04)";

    /// <summary>
    /// Build a <see cref="GateExplanation"/> for a single
    /// <see cref="FeasibilityViolation"/>. Pulls structured offending /
    /// limit / lever fields out of an internal hand-authored lookup; falls
    /// back to a generic registry-AdrRef pointer when the ConstraintId is
    /// not in the lookup.
    /// </summary>
    public static GateExplanation Explain(FeasibilityViolation violation)
    {
        var ci = CultureInfo.InvariantCulture;
        if (HintsLookup.TryGetValue(violation.ConstraintId, out var h))
        {
            return new GateExplanation(
                ConstraintId:      violation.ConstraintId,
                ShortDescription:  h.ShortDescription,
                OffendingValue:    FormatValue(violation.ActualValue, h.UnitSuffix, ci),
                Limit:             FormatValue(violation.Limit,       h.UnitSuffix, ci),
                Levers:            h.Levers,
                ReferenceDoc:      LookupReferenceDoc(violation.ConstraintId),
                CoupledVariables:  h.CoupledVariables);
        }

        string adrRef = LookupReferenceDoc(violation.ConstraintId);
        return new GateExplanation(
            ConstraintId:      violation.ConstraintId,
            ShortDescription:  "Feasibility violation (no detailed explainer entry yet for this gate).",
            OffendingValue:    FormatValue(violation.ActualValue, "", ci),
            Limit:             FormatValue(violation.Limit,       "", ci),
            Levers:            new[]
            {
                $"No detailed lever guidance yet for this gate; see {adrRef} for the gate's physics.",
            },
            ReferenceDoc:      adrRef,
            CoupledVariables:  Array.Empty<string>());
    }

    /// <summary>
    /// Look up the SA-tunable design variables coupled to a feasibility
    /// gate by ConstraintId. Returns the same list embedded in
    /// <see cref="GateExplanation.CoupledVariables"/> for a violation of
    /// the same gate; offered as a standalone accessor so a future
    /// Sobol-sensitivity ranker (#347 Phase 2) can ask
    /// "which SA dims should I sweep for gate X?" without needing a
    /// concrete <see cref="FeasibilityViolation"/>.
    /// </summary>
    /// <returns>Empty list when the gate is uncovered by the explainer
    /// or has no SA-tunable levers (e.g., feed-system gates whose
    /// physical levers are all <see cref="OperatingConditions"/>-side).</returns>
    public static IReadOnlyList<string> GetCoupledVariables(string constraintId)
        => HintsLookup.TryGetValue(constraintId, out var h)
            ? h.CoupledVariables
            : Array.Empty<string>();

    /// <summary>
    /// Aggregate a <see cref="FeasibilityGateResult"/> into an operator-
    /// facing Markdown report. Mirrors the
    /// <c>SafetyReport.BuildMarkdown</c> banner template (status / generated
    /// timestamp / schema / optional design hash / disclaimer blockquote).
    /// </summary>
    /// <remarks>
    /// When <see cref="FeasibilityGateResult.IsFeasible"/> is true the
    /// report renders a short-form banner only — no <c>## Failing gates</c>
    /// or <c>## Recommended next steps</c> sections.
    /// </remarks>
    private static string BuildMarkdownNoRanker(
        FeasibilityGateResult gateResult,
        string designHash)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int n = gateResult.Violations.Length;
        bool isPass = gateResult.IsFeasible;
        string status = isPass
            ? "PASS"
            : $"FAIL — {n.ToString(ci)} gate(s) failing";

        sb.AppendLine("# Feasibility Gates — Causal Explainer");
        sb.AppendLine();
        sb.AppendLine($"**Status:** {status}  ");
        sb.AppendLine($"**Generated:** {DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)} (local)  ");
        sb.AppendLine($"**Schema:** {ReportSchemaVersion}");
        if (!string.IsNullOrEmpty(designHash))
            sb.AppendLine($"  \n**Design hash:** `{designHash}`");
        sb.AppendLine();
        sb.AppendLine("> Operator-facing summary of why feasibility gates rejected this design. " +
                      "Each violation lists the offending value, the limit, the SA-tunable " +
                      "design variables physically coupled to the gate, and 2–3 levers (design " +
                      "knobs to turn). Lever rankings are hand-authored in this slice; the " +
                      "coupled-variable list is the data layer Phase 2 of #347 consumes for " +
                      "Sobol-sensitivity ordering.");
        sb.AppendLine();

        if (isPass)
        {
            sb.AppendLine("> No failing gates — design passes feasibility screening.");
            return sb.ToString();
        }

        var explanations = new GateExplanation[n];
        for (int i = 0; i < n; i++)
            explanations[i] = Explain(gateResult.Violations[i]);

        sb.AppendLine("## Failing gates");
        sb.AppendLine();
        for (int i = 0; i < n; i++)
        {
            var ex = explanations[i];
            var v  = gateResult.Violations[i];
            sb.AppendLine($"### {ex.ConstraintId}");
            sb.AppendLine();
            sb.AppendLine($"- **What failed:** {ex.ShortDescription}");
            sb.AppendLine($"- **Detail:** {v.Description}");
            sb.AppendLine($"- **Offending value:** {ex.OffendingValue}");
            sb.AppendLine($"- **Limit:** {ex.Limit}");
            sb.AppendLine($"- **Reference:** {ex.ReferenceDoc}");
            if (ex.CoupledVariables.Count > 0)
                sb.AppendLine($"- **Coupled SA variables:** {string.Join(", ", ex.CoupledVariables)}");
            sb.AppendLine();
            sb.AppendLine("**Levers to consider:**");
            sb.AppendLine();
            for (int k = 0; k < ex.Levers.Count; k++)
                sb.AppendLine($"{(k + 1).ToString(ci)}. {ex.Levers[k]}");
            sb.AppendLine();
        }

        sb.AppendLine("## Recommended next steps");
        sb.AppendLine();
        for (int i = 0; i < n; i++)
        {
            var ex = explanations[i];
            string firstLever = ex.Levers.Count > 0
                ? ex.Levers[0]
                : "(no lever guidance available for this gate)";
            sb.AppendLine($"- Address **{ex.ConstraintId}**: {firstLever}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Canonical public entry point. When <paramref name="ranker"/> is
    /// <c>null</c> (the default) the report renders the hand-authored
    /// lever bullets; when supplied, Phase 3 of issue #347 fires and
    /// the failing-gate sections embed Sobol-ranked lever tables.
    /// </summary>
    /// <param name="gateResult">Gate evaluation result.</param>
    /// <param name="ranker">Optional Sobol ranker; <c>null</c> keeps
    /// the hand-authored lever path.</param>
    /// <param name="designHash">Optional design hash for the report header.</param>
    /// <remarks>
    /// Collapsed from two public overloads into one (issue #559 M1):
    /// the two-overload + default-parameter shape tripped RS0026
    /// (PublicApiAnalyzers) because future callers adding <c>null</c>
    /// as a positional second arg would re-pick which overload binds.
    /// The single-overload shape also lets us drop the global RS0026
    /// suppression from <c>Directory.Build.props</c>.
    /// </remarks>
    public static string BuildMarkdown(
        FeasibilityGateResult gateResult,
        SobolGateRanker? ranker = null,
        string designHash = "")
    {
        if (ranker is null)
            return BuildMarkdownNoRanker(gateResult, designHash);
        return BuildMarkdown(gateResult, ranker.Rank, designHash: designHash);
    }

    /// <summary>
    /// Phase 3 of issue #347: ranked-lever overload. For the first
    /// <paramref name="maxRankedGates"/> failing gates, calls
    /// <paramref name="rankerFactory"/> and emits a Sobol-ranked lever
    /// table instead of the hand-authored bullet list. Falls back to the
    /// hand-authored list when the factory is null, returns an empty array,
    /// or throws (degrade-silently strategy).
    /// </summary>
    /// <param name="gateResult">Gate evaluation result.</param>
    /// <param name="rankerFactory">Optional: given a <c>constraintId</c>,
    /// returns <see cref="RankedLever"/>[] sorted by Sobol ST_i descending.
    /// Called at most <paramref name="maxRankedGates"/> times (top-N short-
    /// circuit). Null → all gates use static hints.</param>
    /// <param name="designHash">Optional design hash for the report header.</param>
    /// <param name="maxRankedGates">Cap on how many gates receive the Sobol
    /// table (wall-clock guard). Default 5.</param>
    internal static string BuildMarkdown(
        FeasibilityGateResult gateResult,
        Func<string, RankedLever[]>? rankerFactory,
        string designHash = "",
        int maxRankedGates = 5)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        int n = gateResult.Violations.Length;
        bool isPass = gateResult.IsFeasible;
        string status = isPass
            ? "PASS"
            : $"FAIL — {n.ToString(ci)} gate(s) failing";

        sb.AppendLine("# Feasibility Gates — Causal Explainer");
        sb.AppendLine();
        sb.AppendLine($"**Status:** {status}  ");
        sb.AppendLine($"**Generated:** {DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)} (local)  ");
        sb.AppendLine($"**Schema:** {ReportSchemaVersion}");
        if (!string.IsNullOrEmpty(designHash))
            sb.AppendLine($"  \n**Design hash:** `{designHash}`");
        sb.AppendLine();
        sb.AppendLine("> Operator-facing summary. Lever rankings for the top " +
                      $"{maxRankedGates.ToString(ci)} gates are ordered by Sobol total-sensitivity " +
                      "index (ST_i) computed over the gate's SA-tunable coupled variables. " +
                      "Remaining gates fall back to hand-authored hints.");
        sb.AppendLine();

        if (isPass)
        {
            sb.AppendLine("> No failing gates — design passes feasibility screening.");
            return sb.ToString();
        }

        var explanations = new GateExplanation[n];
        for (int i = 0; i < n; i++)
            explanations[i] = Explain(gateResult.Violations[i]);

        // Pre-compute ranked levers for top maxRankedGates gates with coupled vars.
        var rankedLevers = new RankedLever[]?[n];
        if (rankerFactory != null)
        {
            for (int i = 0; i < Math.Min(n, maxRankedGates); i++)
            {
                if (explanations[i].CoupledVariables.Count > 0)
                    rankedLevers[i] = TryRank(gateResult.Violations[i].ConstraintId, rankerFactory);
            }
        }

        sb.AppendLine("## Failing gates");
        sb.AppendLine();
        for (int i = 0; i < n; i++)
        {
            var ex = explanations[i];
            var v  = gateResult.Violations[i];
            sb.AppendLine($"### {ex.ConstraintId}");
            sb.AppendLine();
            sb.AppendLine($"- **What failed:** {ex.ShortDescription}");
            sb.AppendLine($"- **Detail:** {v.Description}");
            sb.AppendLine($"- **Offending value:** {ex.OffendingValue}");
            sb.AppendLine($"- **Limit:** {ex.Limit}");
            sb.AppendLine($"- **Reference:** {ex.ReferenceDoc}");
            if (ex.CoupledVariables.Count > 0)
                sb.AppendLine($"- **Coupled SA variables:** {string.Join(", ", ex.CoupledVariables)}");
            sb.AppendLine();

            var levers = rankedLevers[i];
            if (levers != null)
            {
                AppendRankedTable(sb, levers, ci);
            }
            else
            {
                sb.AppendLine("**Levers to consider:**");
                sb.AppendLine();
                for (int k = 0; k < ex.Levers.Count; k++)
                    sb.AppendLine($"{(k + 1).ToString(ci)}. {ex.Levers[k]}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Recommended next steps");
        sb.AppendLine();
        for (int i = 0; i < n; i++)
        {
            var ex = explanations[i];
            var levers = rankedLevers[i];
            string step;
            if (levers is { Length: > 0 })
                step = $"{levers[0].VariableName} has highest Sobol sensitivity " +
                       $"(ST_i = {levers[0].TotalST.ToString("F4", ci)}).";
            else
                step = ex.Levers.Count > 0
                    ? ex.Levers[0]
                    : "(no lever guidance available for this gate)";
            sb.AppendLine($"- Address **{ex.ConstraintId}**: {step}");
        }
        return sb.ToString();
    }

    private static void AppendRankedTable(StringBuilder sb, RankedLever[] levers, CultureInfo ci)
    {
        sb.AppendLine("**Sobol-ranked levers** (seed 42, N = 64):");
        sb.AppendLine();
        sb.AppendLine("| Rank | Variable                            |      S_i |     ST_i |");
        sb.AppendLine("|-----:|:------------------------------------|--------:|--------:|");
        for (int rank = 0; rank < levers.Length; rank++)
        {
            var l  = levers[rank];
            double si  = double.IsNaN(l.FirstOrderS) ? 0.0 : l.FirstOrderS;
            double sti = double.IsNaN(l.TotalST)     ? 0.0 : l.TotalST;
            sb.AppendLine(string.Format(ci,
                "| {0,4} | {1,-35} | {2,8:F4} | {3,8:F4} |",
                rank + 1, l.VariableName, si, sti));
        }
    }

    // Calls the rankerFactory and returns non-empty results, or null on
    // empty/exception (degrade-silently — the caller falls back to static hints).
    private static RankedLever[]? TryRank(string constraintId, Func<string, RankedLever[]> factory)
    {
        try
        {
            var r = factory(constraintId);
            return r.Length > 0 ? r : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatValue(double value, string unitSuffix, CultureInfo ci)
        => double.IsNaN(value) ? "—" : value.ToString("G4", ci) + unitSuffix;

    private static string LookupReferenceDoc(string constraintId)
        => GateRegistry.TryGetById(constraintId, out var d) && d is not null
            ? d.AdrRef
            : "(unregistered)";

    private sealed record GateLeverHints(
        string ShortDescription,
        string UnitSuffix,
        IReadOnlyList<string> Levers,
        IReadOnlyList<string> CoupledVariables);

    private static readonly Lazy<IReadOnlyDictionary<string, GateLeverHints>> _hints =
        new(BuildAndValidateHints, LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyDictionary<string, GateLeverHints> HintsLookup => _hints.Value;

    private static Dictionary<string, GateLeverHints> BuildAndValidateHints()
    {
        var d = new Dictionary<string, GateLeverHints>(StringComparer.Ordinal)
        {
            ["WALL_TEMP"] = new GateLeverHints(
                ShortDescription: "Peak gas-side wall T exceeds material service limit.",
                UnitSuffix:       " K",
                Levers: new[]
                {
                    "Increase coolant capacity: raise ChannelHeightThroat_mm / ChannelHeightChamber_mm or ChannelCount to drop the heat-transfer ΔT through the wall.",
                    "Thin the gas-side liner: lower GasSideWallThickness_mm so the temperature drop across the liner shrinks (within burst-margin headroom).",
                    "Pick a higher-temperature wall via WallMaterialIndex (e.g., GRCop-42 over CuCrZr) or lower the target chamber pressure.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.ChannelHeightThroat_mm),
                    nameof(RegenChamberDesign.ChannelHeightChamber_mm),
                    nameof(RegenChamberDesign.ChannelHeightExit_mm),
                    nameof(RegenChamberDesign.ChannelCount),
                    nameof(RegenChamberDesign.GasSideWallThickness_mm),
                    nameof(RegenChamberDesign.RibThickness_mm),
                    nameof(RegenChamberDesign.ChamberWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ThroatWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ExitWallThicknessOverride_mm),
                }),

            ["YIELD_EXCEEDED"] = new GateLeverHints(
                ShortDescription: "Min wall safety factor < 1.0 — yield stress exceeded.",
                UnitSuffix:       " (safety factor)",
                Levers: new[]
                {
                    "Increase GasSideWallThickness_mm and/or OuterJacketThickness_mm to reduce hoop stress.",
                    "Raise RibThickness_mm — rib bending controls the inter-channel stress concentration.",
                    "Pick a higher-yield WallMaterialIndex, or lower target chamber pressure.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.GasSideWallThickness_mm),
                    nameof(RegenChamberDesign.OuterJacketThickness_mm),
                    nameof(RegenChamberDesign.RibThickness_mm),
                    nameof(RegenChamberDesign.ChamberWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ThroatWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ExitWallThicknessOverride_mm),
                }),

            ["FEATURE_TOO_SMALL"] = new GateLeverHints(
                ShortDescription: "Minimum feature below 0.30 mm LPBF printability floor.",
                UnitSuffix:       " mm",
                Levers: new[]
                {
                    "Raise RibThickness_mm or GasSideWallThickness_mm — these set the smallest features in axial / helical channels.",
                    "Reduce ChannelCount: fewer, fatter channels widen each rib slot.",
                    "Switch to a coarser ChannelTopology if running TPMS (TPMS strut floor is governed by TPMS_CELL_FEATURE_TOO_SMALL instead).",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.RibThickness_mm),
                    nameof(RegenChamberDesign.GasSideWallThickness_mm),
                    nameof(RegenChamberDesign.ChannelCount),
                }),

            ["COOLANT_T_EXCEEDED"] = new GateLeverHints(
                ShortDescription: "Coolant outlet T above fluid service / pseudocritical limit.",
                UnitSuffix:       " K",
                Levers: new[]
                {
                    "Raise coolant mass-flow capacity: increase ChannelHeight* (chamber / throat / exit) or ChannelCount to reduce per-channel ΔT.",
                    "Reduce wall heat absorbed: thicken the jacket or pick a higher-conductivity liner so less heat reaches the bulk coolant.",
                    "Bleed off some heat ahead of the channels: raise FilmFuelFraction so less enthalpy reaches the regen jacket.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.ChannelHeightChamber_mm),
                    nameof(RegenChamberDesign.ChannelHeightThroat_mm),
                    nameof(RegenChamberDesign.ChannelHeightExit_mm),
                    nameof(RegenChamberDesign.ChannelCount),
                    nameof(RegenChamberDesign.GasSideWallThickness_mm),
                    nameof(RegenChamberDesign.OuterJacketThickness_mm),
                    nameof(RegenChamberDesign.FilmFuelFraction),
                }),

            ["INJECTOR_FACE_T_EXCEEDED"] = new GateLeverHints(
                ShortDescription: "Predicted injector-face T above face-alloy service limit — burnout risk.",
                UnitSuffix:       " K",
                Levers: new[]
                {
                    "Reduce injector element density: lower ElementCount or grow the chamber radius via ContractionRatio.",
                    "Add face-film cooling: raise FilmFuelFraction or set FilmSlotHeightOverride_mm.",
                    "Pick a higher-temperature face alloy (face-material selection lives in MonolithicEngineBuilder; not currently SA-tunable).",
                },
                CoupledVariables: new[]
                {
                    nameof(InjectorPattern.ElementCount),
                    nameof(RegenChamberDesign.ContractionRatio),
                    nameof(RegenChamberDesign.FilmFuelFraction),
                    nameof(RegenChamberDesign.FilmSlotHeightOverride_mm),
                    nameof(InjectorPattern.OuterRowFilmFraction),
                }),

            ["ELEMENT_DENSITY_TOO_HIGH"] = new GateLeverHints(
                ShortDescription: "Injector element density > 0.7 / cm² — face-plate burnout risk.",
                UnitSuffix:       " / cm²",
                Levers: new[]
                {
                    "Reduce ElementCount on the injector pattern.",
                    "Increase chamber radius via ContractionRatio so the face-area denominator grows.",
                    "Switch to a fewer-larger-element pattern (e.g., Pintle) by changing ElementType.",
                },
                CoupledVariables: new[]
                {
                    nameof(InjectorPattern.ElementCount),
                    nameof(RegenChamberDesign.ContractionRatio),
                }),

            ["PINTLE_BLOCKAGE_OUT_OF_BAND"] = new GateLeverHints(
                ShortDescription: "Pintle blockage factor outside Dressler stable-combustion band.",
                UnitSuffix:       " (blockage)",
                Levers: new[]
                {
                    "If below band: reduce PintleDiameter_mm or increase PintleSleeveHoleCount — sheet breakup is starved.",
                    "If above band: increase PintleDiameter_mm or reduce PintleSleeveHoleCount — jets collide too aggressively.",
                    "Tune PintleSleeveHoleDiameter_mm to retune blockage without disturbing TMR.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.PintleDiameterOverride_mm),
                    nameof(RegenChamberDesign.PintleSleeveHoleCountOverride),
                }),

            ["BURST_MARGIN_INSUFFICIENT"] = new GateLeverHints(
                ShortDescription: "Elastic burst margin < 2.5× MEOP (ASME BPVC §VIII Div 1 ground-test floor).",
                UnitSuffix:       "× MEOP",
                Levers: new[]
                {
                    "Increase GasSideWallThickness_mm and/or OuterJacketThickness_mm so elastic-burst pressure rises.",
                    "Pick a higher-yield WallMaterialIndex (e.g., IN718 jacket over SS316L).",
                    "Lower target chamber pressure — burst margin scales as σ_y / Pc.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.GasSideWallThickness_mm),
                    nameof(RegenChamberDesign.OuterJacketThickness_mm),
                    nameof(RegenChamberDesign.ChamberWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ThroatWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ExitWallThicknessOverride_mm),
                }),

            ["LCF_LIFE_INSUFFICIENT"] = new GateLeverHints(
                ShortDescription: "Predicted LCF life below 4× mission-cycle target (Coffin-Manson, NASA PURS data).",
                UnitSuffix:       " cycles",
                Levers: new[]
                {
                    "Reduce ΔT through the wall: raise coolant flow (ChannelHeight* / ChannelCount) or thin the GasSideWallThickness_mm liner.",
                    "Pick a higher-σ_f WallMaterialIndex — Coffin-Manson is dominated by fatigue strength.",
                    "Lower the cycle target (MissionCycles) if the duty profile permits.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.ChannelHeightThroat_mm),
                    nameof(RegenChamberDesign.ChannelHeightChamber_mm),
                    nameof(RegenChamberDesign.ChannelCount),
                    nameof(RegenChamberDesign.GasSideWallThickness_mm),
                    nameof(RegenChamberDesign.ChamberWallThicknessOverride_mm),
                    nameof(RegenChamberDesign.ThroatWallThicknessOverride_mm),
                }),

            ["NPSH_INSUFFICIENT"] = new GateLeverHints(
                ShortDescription: "Pump cavitation risk — NPSHA below NPSHR on at least one pump.",
                UnitSuffix:       " m (NPSHA)",
                Levers: new[]
                {
                    "Raise tank ullage / pressurant pressure to grow NPSHA.",
                    "Reduce suction-line velocity by widening the suction-line cross-section or lowering pump RPM (PumpRpm_rpm).",
                    "Add an inducer ahead of the impeller (lowers NPSHR; selected via pump preset, not currently SA-tunable).",
                },
                // No SA-tagged levers: ullage / RPM / inducer-presence all live on
                // OperatingConditions or pump presets, not on the SA vector. Phase 2
                // Sobol sweep skips this gate and the lever table degrades to the
                // hand-authored copy.
                CoupledVariables: Array.Empty<string>()),

            ["FEED_PRESSURE_INSUFFICIENT"] = new GateLeverHints(
                ShortDescription: "Feed-system stackup yields chamber pressure below target.",
                UnitSuffix:       " Pa",
                Levers: new[]
                {
                    "Raise tank ullage pressure — most direct lever for pressure-fed feed.",
                    "Cut intermediate ΔP: review line / valve / filter / umbilical / injector ΔP fractions in the FeedStackup.",
                    "Lower target chamber pressure if the duty cycle permits.",
                },
                // Ullage + non-injector ΔP allocations + Pc are
                // OperatingConditions / FeedStackup fields and not SA-tagged;
                // injector ΔP fraction is the only SA-tunable lever that
                // raises the available chamber pressure for a fixed feed.
                CoupledVariables: new[]
                {
                    nameof(InjectorPattern.DeltaPInjFraction),
                }),

            ["TPMS_CELL_FEATURE_TOO_SMALL"] = new GateLeverHints(
                ShortDescription: "TPMS strut thickness below LPBF floor for the chosen lattice.",
                UnitSuffix:       " mm",
                Levers: new[]
                {
                    "Raise TpmsCellEdge_mm — strut thickness scales linearly with cell edge.",
                    "Raise TpmsSolidFraction (toward the 0.65 ceiling) — strut thickness scales linearly with the fraction.",
                    "Switch ChannelTopology to a non-TPMS option (Axial / Helical) where the LPBF floor governs rib thickness instead.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.TpmsCellEdge_mm),
                    nameof(RegenChamberDesign.TpmsSolidFraction),
                }),

            ["OVERHANG_ANGLE_EXCEEDED"] = new GateLeverHints(
                ShortDescription: "Surface patches overhang below the LPBF unsupported-angle floor.",
                UnitSuffix:       "°",
                Levers: new[]
                {
                    "Re-orient the build per the printability orientation advisor — the steepest face often shifts vertical with a different build axis.",
                    "Soften the steepest slopes: increase BellExitAngle_deg or smooth the contour to keep all surfaces above the supported-angle floor.",
                    "Add sacrificial supports along the offending patches at slicing time (post-process, not SA-tunable).",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.BellExitAngle_deg),
                    nameof(RegenChamberDesign.BellEntranceAngle_deg),
                    nameof(RegenChamberDesign.BellLengthFraction),
                }),

            ["CONTRACTION_RATIO_OUT_OF_BAND"] = new GateLeverHints(
                ShortDescription: "Contraction ratio outside the practical [2.5, 10.0] band (Sutton §8.2 / Huzel & Huang §4.1).",
                UnitSuffix:       " (ratio)",
                Levers: new[]
                {
                    "If below band: increase ContractionRatio — low ε_c risks combustion instability via chamber Mach > 0.2.",
                    "If above band: decrease ContractionRatio — high ε_c bloats wall area and cooling surface.",
                    "Re-evaluate CharacteristicLength_m simultaneously — chamber volume scales with both knobs.",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.ContractionRatio),
                    nameof(RegenChamberDesign.CharacteristicLength_m),
                }),

            ["L_STAR_BELOW_PROPELLANT_MIN"] = new GateLeverHints(
                ShortDescription: "Characteristic length below the propellant-specific floor — C* loss risk.",
                UnitSuffix:       " m",
                Levers: new[]
                {
                    "Raise CharacteristicLength_m — direct lever; the floor is propellant-pair-specific.",
                    "Accept a larger chamber-volume target — short L* trades C* efficiency for mass.",
                    "Re-evaluate the propellant pair: each pair has a different L* nominal (LOX/H2 < LOX/RP-1 < LOX/CH4).",
                },
                CoupledVariables: new[]
                {
                    nameof(RegenChamberDesign.CharacteristicLength_m),
                    nameof(RegenChamberDesign.ContractionRatio),
                }),
        };

        // Validate that every covered ConstraintId is still registered, and
        // that every coupled variable name resolves through the SA design-
        // variable registry. A future property rename or gate retirement
        // can't silently break the explainer / Phase 2 Sobol consumer.
        var saVarNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in DesignVariableRegistry.For(typeof(RegenChamberDesign)))
            saVarNames.Add(v.MemberName);
        foreach (var v in DesignVariableRegistry.For(typeof(InjectorPattern)))
            saVarNames.Add(v.MemberName);

        foreach (var (key, hints) in d)
        {
            if (!GateRegistry.TryGetById(key, out _))
                throw new InvalidOperationException(
                    $"GateExplainer hints map ConstraintId '{key}' that no longer exists in "
                  + "GateRegistry.All. Update the hints table or restore the gate.");

            foreach (var name in hints.CoupledVariables)
            {
                if (!saVarNames.Contains(name))
                    throw new InvalidOperationException(
                        $"GateExplainer gate '{key}' references coupled variable '{name}' "
                      + "that is not [SaDesignVariable]-tagged on RegenChamberDesign or "
                      + "InjectorPattern. Rename was missed, or the variable retired.");
            }
        }
        return d;
    }
}
