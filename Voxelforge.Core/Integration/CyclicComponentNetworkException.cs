// CyclicComponentNetworkException.cs — Sprint B.8a / issue #490.
//
// Typed exception emitted when ComponentNetwork.TopologicalSort detects
// a cycle in the component connection graph. Inherits InvalidOperationException
// so existing catch-blocks that the SI.W1..W18 surface area depend on
// (Assert.Throws<InvalidOperationException>(...) in test suites; the
// MultiChainOptimizer's fault-handling path) continue to work without
// modification.
//
// Pre-B.8a the cycle path threw a plain InvalidOperationException with a
// magic string in the message; NetworkValidator.Validate caught it via
// `when (ex.Message.Contains("cycle"))`. That string match is fragile —
// any future tweak to the exception message silently breaks the
// validator's ContainsCycle finding. The typed exception removes the
// dependency on message content.

using System;

namespace Voxelforge.Integration;

/// <summary>
/// Thrown by <see cref="ComponentNetwork.GetTopologicalOrder"/> and the
/// dependent <see cref="ComponentNetwork.Solve"/> path when the connection
/// graph contains a cycle. Inherits <see cref="InvalidOperationException"/>
/// so existing callers that catch the base type continue to work.
/// </summary>
/// <remarks>
/// Cycle-iterative solving is supported via
/// <see cref="ComponentNetwork.SolveIterative"/> (Sprint SI.W3+).
/// </remarks>
internal sealed class CyclicComponentNetworkException : InvalidOperationException
{
    /// <summary>
    /// Construct with the default cycle-detection message.
    /// </summary>
    public CyclicComponentNetworkException()
        : base(DefaultMessage) { }

    /// <summary>
    /// Construct with a caller-supplied message (used by
    /// <see cref="ComponentNetwork"/> to include the cycle-detection
    /// site and the SolveIterative pointer).
    /// </summary>
    public CyclicComponentNetworkException(string message)
        : base(message) { }

    /// <summary>
    /// Construct with a caller-supplied message and inner exception.
    /// </summary>
    public CyclicComponentNetworkException(string message, Exception innerException)
        : base(message, innerException) { }

    private const string DefaultMessage =
        "Component connection graph contains a cycle. Use "
      + "ComponentNetwork.SolveIterative for Gauss-Seidel cycle iteration.";
}
