// LpbfRoutingGraph.cs — Sprint 27 (2026-04-23): plumbing-routing graph
// abstraction for the drain-path analysis.
//
// Design rationale. The drain-path check asks: "every tube in the part
// has a path that lets powder leave it under gravity + standard
// post-processing orientations." It's a graph problem on the part's
// internal plumbing topology — feed manifolds, coolant channels, purge
// lines, dome cavities — not a voxel problem. Each fluid junction +
// external port is a node; each tube segment / manifold / dome is an
// edge. A node with degree 1 and no external-port flag is a dead end.
//
// The graph is built from the design by the production caller
// (RegenChamberOptimization.Evaluate). Tests build synthetic graphs
// directly to exercise the analysis logic.

namespace Voxelforge.Geometry.LpbfAnalysis;

/// <summary>
/// One node in the plumbing-routing graph. <see cref="IsExternalPort"/>
/// marks a node as having a path to the outside world — feed inlet,
/// purge outlet, injector face bore. Dead-end detection wants nodes that
/// are <c>false</c> here AND have only one edge attached.
/// </summary>
public sealed record LpbfRoutingNode(
    string Id,
    string Label,
    bool   IsExternalPort);

/// <summary>One tube / manifold / passage between two nodes.</summary>
public sealed record LpbfRoutingEdge(
    string FromId,
    string ToId,
    string Label);

/// <summary>Immutable plumbing-routing graph passed to <see cref="DrainPathAnalysis"/>.</summary>
public sealed record LpbfRoutingGraph(
    System.Collections.Generic.IReadOnlyList<LpbfRoutingNode> Nodes,
    System.Collections.Generic.IReadOnlyList<LpbfRoutingEdge> Edges)
{
    public static readonly LpbfRoutingGraph Empty = new(
        System.Array.Empty<LpbfRoutingNode>(),
        System.Array.Empty<LpbfRoutingEdge>());
}
