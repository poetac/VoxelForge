// DrainPathAnalysis.cs — Sprint 27 (2026-04-23): graph-walk the part's
// plumbing topology to surface dead-end tubes where powder can't evacuate.
//
// The algorithm
// ─────────────
// The design-level plumbing (feed manifold, coolant channels, purge lines,
// dome cavities, igniter bore) is modelled as a graph: tube junctions +
// external ports are nodes, tubes/passages are edges. A dead-end is any
// node with degree 1 that isn't flagged as an external port. Optionally
// surface isolated components (subgraphs disconnected from any external
// port) as a separate failure mode — classic "someone forgot to wire up
// the manifold" bug.
//
// Why a graph instead of a voxel check: the trapped-powder analysis is
// the voxel-side check. This module catches a different class of bug —
// user adds a purge tap into a manifold branch, forgets to add the
// external inlet, and now the tap is a dead-end even though the rest of
// the manifold drains fine. The voxel flood-fill sees the tap as
// reachable (it's continuous with the manifold); only a graph check sees
// that the manifold itself can't drain.

using System.Collections.Generic;
using System.Linq;

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>One drain-path violation — a dead-end node or an isolated subgraph.</summary>
public readonly record struct DrainPathViolation(
    string NodeId,
    string Label,
    string Reason);      // "dead-end" | "isolated-component"

/// <summary>Output of <see cref="DrainPathAnalysis.Analyze"/>.</summary>
public sealed record DrainPathReport(
    int                                                          ViolationCount,
    System.Collections.Generic.IReadOnlyList<DrainPathViolation> Violations)
{
    public bool IsPrintable => ViolationCount == 0;
}

/// <summary>
/// Sprint 27 (2026-04-23): graph-walk the plumbing topology for dead-ends +
/// isolated components. Graph-free API so tests can build synthetic inputs.
/// </summary>
public static class DrainPathAnalysis
{
    public static DrainPathReport Analyze(LpbfRoutingGraph graph)
    {
        if (graph is null) throw new System.ArgumentNullException(nameof(graph));

        var violations = new List<DrainPathViolation>();

        // Build adjacency list (bidirectional) + degree count.
        var adj = new Dictionary<string, List<string>>();
        foreach (var n in graph.Nodes) adj[n.Id] = new List<string>();
        foreach (var e in graph.Edges)
        {
            if (adj.TryGetValue(e.FromId, out var fromAdj)) fromAdj.Add(e.ToId);
            if (adj.TryGetValue(e.ToId,   out var toAdj))   toAdj.Add(e.FromId);
        }

        // ── Pass 1: degree-1 nodes that aren't external ports
        foreach (var n in graph.Nodes)
        {
            int deg = adj.TryGetValue(n.Id, out var nbrs) ? nbrs.Count : 0;
            if (deg == 1 && !n.IsExternalPort)
            {
                violations.Add(new DrainPathViolation(
                    NodeId: n.Id,
                    Label:  n.Label,
                    Reason: "dead-end"));
            }
        }

        // ── Pass 2: connected components that contain no external port
        var visited = new HashSet<string>();
        foreach (var seed in graph.Nodes)
        {
            if (visited.Contains(seed.Id)) continue;
            var component = new List<LpbfRoutingNode>();
            var stack = new Stack<string>();
            stack.Push(seed.Id);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                if (!visited.Add(cur)) continue;
                var node = graph.Nodes.FirstOrDefault(n => n.Id == cur);
                if (node is null) continue;
                component.Add(node);
                if (adj.TryGetValue(cur, out var nbrs))
                    foreach (var nb in nbrs)
                        if (!visited.Contains(nb))
                            stack.Push(nb);
            }
            bool hasExternal = component.Exists(n => n.IsExternalPort);
            if (!hasExternal && component.Count > 0)
            {
                // Flag the first node of the component so the caller has a
                // stable handle; de-dup via a set keyed on component seed.
                var rep = component[0];
                violations.Add(new DrainPathViolation(
                    NodeId: rep.Id,
                    Label:  rep.Label,
                    Reason: "isolated-component"));
            }
        }

        return new DrainPathReport(
            ViolationCount: violations.Count,
            Violations:     violations);
    }
}
