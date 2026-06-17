// CycleNotConvergedExceptionTests.cs — coverage for the typed exception
// the turbofan shaft-balance Newton loop throws on iteration-cap.
// Audit 05-test-gaps.md Section 2 Low.

using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class CycleNotConvergedExceptionTests
{
    [Fact]
    public void Ctor_StoresIterationsAndFinalResidual()
    {
        var ex = new CycleNotConvergedException(iterations: 25, finalResidual: 1.5e-3);
        Assert.Equal(25, ex.Iterations);
        Assert.Equal(1.5e-3, ex.FinalResidual, precision: 9);
    }

    [Fact]
    public void Ctor_EmbedsIterationsInMessage()
    {
        var ex = new CycleNotConvergedException(iterations: 42, finalResidual: 1.2345e-2);
        Assert.Contains("42", ex.Message);
    }

    [Fact]
    public void Ctor_EmbedsResidualInMessage()
    {
        // G4 formatting on 0.5 produces "0.5" verbatim. This verifies the
        // residual appears in the message rather than getting dropped /
        // truncated to integer / e-notation.
        var ex = new CycleNotConvergedException(iterations: 1, finalResidual: 0.5);
        Assert.Contains("0.5", ex.Message);
    }

    [Fact]
    public void Ctor_MessageMentionsConvergeAndResidual()
    {
        var ex = new CycleNotConvergedException(iterations: 50, finalResidual: 1e-2);
        Assert.Contains("converge",   ex.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("residual",   ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Is_SystemException()
    {
        var ex = new CycleNotConvergedException(1, 0.0);
        Assert.IsAssignableFrom<System.Exception>(ex);
    }
}
