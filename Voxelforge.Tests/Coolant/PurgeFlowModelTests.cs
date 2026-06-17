// PurgeFlowModelTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.5: PurgeFlowModel sizes GN₂/GHe/GOX purge
// flow against a sharp-edge orifice and feeds the PURGE_FLOW_INSUFFICIENT
// gate. Currently referenced once via Phase1CompletionTests but has no
// dedicated unit-test surface.

using Voxelforge.Coolant;

namespace Voxelforge.Tests;

public class PurgeFlowModelTests
{
    private static PurgePort GN2Port(
        double inletPressure_Pa = 5e6,
        double boreDiameter_mm  = 1.5,
        double requestedFlow_kgs = 0.01) => new(
            Location:         PurgeLocation.ChamberPrePurge,
            Fluid:            PurgeFluid.GN2,
            MassFlow_kgs:     requestedFlow_kgs,
            InletPressure_Pa: inletPressure_Pa,
            BoreDiameter_mm:  boreDiameter_mm);

    [Fact]
    public void PurgePort_Ctor_StoresFieldsVerbatim()
    {
        var p = GN2Port();
        Assert.Equal(PurgeLocation.ChamberPrePurge, p.Location);
        Assert.Equal(PurgeFluid.GN2, p.Fluid);
        Assert.Equal(5e6, p.InletPressure_Pa, precision: 0);
        Assert.Equal(1.5, p.BoreDiameter_mm, precision: 6);
        Assert.Equal(0.01, p.MassFlow_kgs, precision: 6);
    }

    [Fact]
    public void Evaluate_NoneFluid_ReturnsZeroFlowResult()
    {
        var port = GN2Port() with { Fluid = PurgeFluid.None, MassFlow_kgs = 0.0 };
        var r = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 1e6);

        Assert.Equal(0.0, r.ActualMassFlow_kgs, precision: 6);
        Assert.Equal(0.0, r.JetVelocity_ms, precision: 6);
        Assert.True(r.MeetsRequestedFlow,
            "MassFlow=0 + Fluid=None should satisfy requested-flow check trivially.");
    }

    [Fact]
    public void Evaluate_InletAtOrBelowChamberPressure_StallsAndFlagsFailure()
    {
        // Backflow gradient — no flow possible. Returns Notes string
        // explaining the gradient is wrong and the requested flow > 0
        // case fails the MeetsRequestedFlow check.
        var port = GN2Port(inletPressure_Pa: 1e6);   // == chamberPressure
        var r = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 1e6);

        Assert.Equal(0.0, r.ActualMassFlow_kgs, precision: 6);
        Assert.False(r.MeetsRequestedFlow);
        Assert.Contains("gradient", r.Notes);
    }

    [Fact]
    public void Evaluate_GN2_HighDeltaP_ProducesPositiveFlowAndVelocity()
    {
        var r = PurgeFlowModel.Evaluate(GN2Port(), chamberPressure_Pa: 1e6);

        Assert.True(r.ActualMassFlow_kgs > 0,
            $"ActualMassFlow_kgs={r.ActualMassFlow_kgs} must be positive for ΔP=4 MPa.");
        Assert.True(r.JetVelocity_ms > 0);
        Assert.Equal(4e6, r.DeltaP_Pa, precision: 0);
    }

    [Fact]
    public void Evaluate_HeliumLighterThanGN2_ProducesHigherVelocityAtSameDeltaP()
    {
        // v = √(2·ΔP/ρ); helium is ~7× less dense than GN₂ at the same
        // pressure, so jet velocity must be √7 ≈ 2.65× higher.
        var port = GN2Port();
        var rGN2 = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 1e6);
        var rHe  = PurgeFlowModel.Evaluate(port with { Fluid = PurgeFluid.Helium },
                                           chamberPressure_Pa: 1e6);

        Assert.True(rHe.JetVelocity_ms > rGN2.JetVelocity_ms,
            $"Helium velocity ({rHe.JetVelocity_ms:F1}) should exceed " +
            $"GN₂ velocity ({rGN2.JetVelocity_ms:F1}) at same ΔP.");
    }

    [Fact]
    public void Evaluate_LargerBore_ProducesProportionallyMoreFlow()
    {
        // ṁ = Cd · A · √(2·ρ·ΔP); A scales as d². Doubling d should
        // 4× the mass flow (everything else constant).
        var small = PurgeFlowModel.Evaluate(GN2Port(boreDiameter_mm: 1.0),
                                            chamberPressure_Pa: 1e6);
        var big   = PurgeFlowModel.Evaluate(GN2Port(boreDiameter_mm: 2.0),
                                            chamberPressure_Pa: 1e6);

        Assert.Equal(4.0, big.ActualMassFlow_kgs / small.ActualMassFlow_kgs,
                     precision: 4);
    }

    [Fact]
    public void Evaluate_MeetsRequestedFlow_WhenActualWithin5Percent()
    {
        // Default GN2Port with 5 MPa, 1.5 mm bore delivers a known flow.
        // Set the requested flow to just below the actual; the port
        // should satisfy the check.
        var dryRun = PurgeFlowModel.Evaluate(GN2Port(), chamberPressure_Pa: 1e6);
        double actualFlow = dryRun.ActualMassFlow_kgs;

        var requested = GN2Port(requestedFlow_kgs: actualFlow * 0.90);
        var r = PurgeFlowModel.Evaluate(requested, chamberPressure_Pa: 1e6);
        Assert.True(r.MeetsRequestedFlow);
    }

    [Fact]
    public void Evaluate_FailsRequestedFlow_WhenActualBelow95PercentOfTarget()
    {
        // Set requested flow well above what 1.5 mm at 5 MPa can deliver.
        var port = GN2Port(requestedFlow_kgs: 1.0);   // 1 kg/s — wildly over-spec
        var r = PurgeFlowModel.Evaluate(port, chamberPressure_Pa: 1e6);
        Assert.False(r.MeetsRequestedFlow);
    }

    [Fact]
    public void EvaluateAll_NullList_ReturnsEmptyArray()
    {
        var r = PurgeFlowModel.EvaluateAll(null, chamberPressure_Pa: 1e6);
        Assert.Empty(r);
    }

    [Fact]
    public void EvaluateAll_EmptyList_ReturnsEmptyArray()
    {
        var r = PurgeFlowModel.EvaluateAll(System.Array.Empty<PurgePort>(),
                                           chamberPressure_Pa: 1e6);
        Assert.Empty(r);
    }

    [Fact]
    public void EvaluateAll_PreservesInputOrderAndCount()
    {
        var ports = new[]
        {
            GN2Port(),
            GN2Port() with { Fluid = PurgeFluid.Helium },
            GN2Port() with { Fluid = PurgeFluid.GOX },
        };

        var results = PurgeFlowModel.EvaluateAll(ports, chamberPressure_Pa: 1e6);
        Assert.Equal(3, results.Length);
        Assert.Equal(PurgeFluid.GN2,    results[0].Port.Fluid);
        Assert.Equal(PurgeFluid.Helium, results[1].Port.Fluid);
        Assert.Equal(PurgeFluid.GOX,    results[2].Port.Fluid);
    }
}
