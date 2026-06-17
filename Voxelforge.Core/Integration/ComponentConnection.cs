// ComponentConnection.cs — Sprint SI.W1 connection record. Binds a
// source component's output port to a destination component's input
// port within a ComponentNetwork.

namespace Voxelforge.Integration;

/// <summary>
/// A directed connection wiring an output port of one component to an
/// input port of another (Sprint SI.W1).
/// </summary>
/// <param name="FromComponent">Source component name.</param>
/// <param name="FromPort">Name of the source component's output port.</param>
/// <param name="ToComponent">Destination component name.</param>
/// <param name="ToPort">Name of the destination component's input port.</param>
internal sealed record ComponentConnection(
    string FromComponent,
    string FromPort,
    string ToComponent,
    string ToPort);
