// NetworkValidator.cs — Sprint SI.W18 static-analysis validator for
// ComponentNetwork topologies.
//
// Walks a network's components, connections, and external inputs to
// surface common modelling mistakes BEFORE Solve() is called:
//
//   • Unconnected output ports          (Info — signal going nowhere)
//   • Inputs with no feed at all        (Error — Solve will throw)
//   • Inputs with BOTH ext + connection (Warning — external wins,
//                                        but flag for awareness)
//   • Multiple connections feeding the  (Error — ambiguous; the last
//     same input port                    wired wins, which is fragile)
//   • Cycles requiring iterative solve  (Warning — Solve() will throw,
//                                        SolveIterative() is needed)
//
// Use as a CI smoke-check or a pre-flight inside an IDE form.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Integration;

/// <summary>Severity of a static-analysis finding.</summary>
internal enum ValidationSeverity
{
    /// <summary>Diagnostic — no action required.</summary>
    Info = 0,
    /// <summary>Suspicious — likely a mistake but doesn't break solve.</summary>
    Warning = 1,
    /// <summary>Solve will fail with this topology.</summary>
    Error = 2,
}

/// <summary>One finding from a NetworkValidator.Validate() pass.</summary>
internal sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Category,
    string Component,
    string Message);

/// <summary>
/// Aggregate report from a NetworkValidator.Validate() pass.
/// </summary>
internal sealed record ValidationReport(
    IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>Count of <see cref="ValidationSeverity.Error"/> findings.</summary>
    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);
    /// <summary>Count of <see cref="ValidationSeverity.Warning"/> findings.</summary>
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    /// <summary>Count of <see cref="ValidationSeverity.Info"/> findings.</summary>
    public int InfoCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);
    /// <summary>True when there are no Error-severity findings.</summary>
    public bool IsValid => ErrorCount == 0;
}

/// <summary>
/// Static-analysis topology validator for
/// <see cref="ComponentNetwork"/> (Sprint SI.W18).
/// </summary>
internal static class NetworkValidator
{
    /// <summary>Run all static checks on the given network.</summary>
    public static ValidationReport Validate(ComponentNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var issues = new List<ValidationIssue>();
        var components  = network.ComponentNames;
        var connections = network.Connections;
        var externals   = new HashSet<(string, string)>(network.ExternalInputBindings);

        // ── A. Outgoing wire analysis ─────────────────────────────────────
        var sourceUsage = new HashSet<(string, string)>(
            connections.Select(c => (c.FromComponent, c.FromPort)));
        foreach (var name in components)
            foreach (var portName in network.OutputPortsOf(name))
                if (!sourceUsage.Contains((name, portName)))
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Info,
                        Category:  "UnconnectedOutput",
                        Component: name,
                        Message:   $"Output port '{portName}' has no downstream consumer."));

        // ── B. Inbound wire analysis ──────────────────────────────────────
        var inboundCount = new Dictionary<(string, string), int>();
        foreach (var c in connections)
        {
            var key = (c.ToComponent, c.ToPort);
            inboundCount[key] = inboundCount.TryGetValue(key, out var v) ? v + 1 : 1;
        }

        foreach (var name in components)
        {
            foreach (var portName in network.InputPortsOf(name))
            {
                bool hasExt = externals.Contains((name, portName));
                int  inbound = inboundCount.TryGetValue((name, portName), out var n)
                    ? n : 0;

                if (!hasExt && inbound == 0)
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        Category:  "UnfedInput",
                        Component: name,
                        Message:   $"Input port '{portName}' has no external feed "
                                 + "and no internal connection. Solve() will throw."));
                else if (hasExt && inbound > 0)
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        Category:  "OverDeterminedInput",
                        Component: name,
                        Message:   $"Input port '{portName}' has BOTH an external "
                                 + "feed and an internal connection. External feed wins; "
                                 + "remove one to disambiguate."));
                else if (inbound > 1)
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        Category:  "MultipleSourcesForInput",
                        Component: name,
                        Message:   $"Input port '{portName}' has {inbound} internal "
                                 + "connections feeding it. The last-wired wins, which "
                                 + "is fragile — remove all but one."));
            }
        }

        // ── C. Cycle detection ────────────────────────────────────────────
        try
        {
            _ = network.GetTopologicalOrder();
        }
        catch (CyclicComponentNetworkException)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                Category:  "ContainsCycle",
                Component: "(network)",
                Message:   "Connection graph contains at least one cycle. "
                         + "Solve() will throw — use SolveIterative() for "
                         + "Gauss-Seidel cycle iteration."));
        }

        // ── D. Sprint SI.W24 — Unit-suffix consistency on connections ─────
        //     Convention: port names carry a unit suffix after the last `_`
        //     (e.g. PackElectricalPower_W, ShaftTorque_Nm, FlowRate_kgs).
        //     When both endpoints of a connection carry RECOGNIZED unit
        //     suffixes that disagree (e.g. `_W` → `_A`), surface a Warning.
        //     Ports without a recognized suffix are exempt (legitimately
        //     dimensionless or domain-specific).
        foreach (var c in connections)
        {
            var fromSuffix = ExtractRecognizedUnitSuffix(c.FromPort);
            var toSuffix   = ExtractRecognizedUnitSuffix(c.ToPort);
            if (fromSuffix is null || toSuffix is null) continue;
            if (fromSuffix == toSuffix) continue;

            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                Category:  "UnitMismatch",
                Component: c.FromComponent,
                Message:   $"Connection '{c.FromComponent}.{c.FromPort}' → "
                         + $"'{c.ToComponent}.{c.ToPort}' wires {fromSuffix} "
                         + $"to {toSuffix}. Unit suffixes disagree; verify "
                         + "the connection is intentional (e.g. a converter "
                         + "with an implicit transform) or rename one port."));
        }

        return new ValidationReport(issues);
    }

    /// <summary>
    /// Known SI-flavoured unit suffixes used across the
    /// <see cref="ComponentNetwork"/> port catalogue. Sprint SI.W24.
    /// Suffixes outside this set (e.g. <c>_total</c>, <c>_avg</c>,
    /// <c>_frac</c>) are considered descriptors, not units; connections
    /// with descriptor-suffix ports skip the unit-mismatch check.
    /// </summary>
    private static readonly HashSet<string> RecognizedUnitSuffixes = new()
    {
        // Power / energy
        "W", "kW", "kWh", "J", "Nm",
        // Voltage / current
        "V", "A",
        // Mass / flow
        "kg", "kgs", "g", "gs",
        // Pressure / volume / flow rate
        "Pa", "bar", "m3", "m3s", "L", "Ls",
        // Temperature
        "K", "C",
        // Length / area
        "m", "mm", "m2", "mm2",
        // Angular
        "rad", "rads", "rpm",
        // Time
        "s", "ms", "hr",
        // Force
        "N",
        // Dimensionless ratios that often-but-not-always need unit consistency
        // (left out of the recognized set on purpose — bare names act as
        // descriptors and skip the check).
    };

    private static string? ExtractRecognizedUnitSuffix(string portName)
    {
        int lastUnderscore = portName.LastIndexOf('_');
        if (lastUnderscore < 0 || lastUnderscore == portName.Length - 1) return null;
        string suffix = portName.Substring(lastUnderscore + 1);
        return RecognizedUnitSuffixes.Contains(suffix) ? suffix : null;
    }
}
