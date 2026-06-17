// NuclearEngineTests.cs — direct tests for NuclearEngine, the IEngine
// wrapper around NuclearOptimization.GenerateWith. Per audit
// 05-test-gaps.md §5 the engine class was referenced by NtrObjective but
// had no direct tests of its own.

using System;
using Voxelforge.Engines;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.Engines;
using Xunit;

namespace Voxelforge.Nuclear.Tests.Engines;

public sealed class NuclearEngineTests
{
    private static NuclearThermalDesign MakeNrxA6() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions MakeCond()
        => new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    // ── Singleton + identity ─────────────────────────────────────────────

    [Fact]
    public void Instance_IsNotNull()
    {
        Assert.NotNull(NuclearEngine.Instance);
    }

    [Fact]
    public void Instance_IsStable_AcrossAccesses()
    {
        // Static singleton — repeated reads return the same reference.
        Assert.Same(NuclearEngine.Instance, NuclearEngine.Instance);
    }

    [Fact]
    public void Family_IsNuclear()
    {
        Assert.Equal(EngineFamilies.Nuclear, NuclearEngine.Instance.Family);
    }

    [Fact]
    public void NuclearEngine_ImplementsIEngine_GenericContract()
    {
        Assert.IsAssignableFrom<
            IEngine<NuclearThermalDesign, NuclearThermalConditions, NtrGenerationResult>>(
            NuclearEngine.Instance);
    }

    // ── Evaluate happy path ──────────────────────────────────────────────

    [Fact]
    public void Evaluate_NrxA6_ReturnsFinitePerformanceResult()
    {
        var result = NuclearEngine.Instance.Evaluate(MakeNrxA6(), MakeCond());
        Assert.NotNull(result);
        Assert.True(result.IspVacuum_s > 0.0,
            $"Isp_vac {result.IspVacuum_s:F1} must be > 0 for NRX-A6 baseline.");
        Assert.True(result.ThrustVacuum_N > 0.0,
            $"Thrust_vac {result.ThrustVacuum_N:F1} must be > 0.");
    }

    [Fact]
    public void Evaluate_MatchesNuclearOptimization_GenerateWith()
    {
        // The engine wrapper must produce the same result as the
        // underlying static orchestrator — it adds null/family guards
        // only, not transformations.
        var direct  = NuclearOptimization.GenerateWith(MakeNrxA6(), MakeCond());
        var wrapped = NuclearEngine.Instance.Evaluate(MakeNrxA6(), MakeCond());
        Assert.Equal(direct.IspVacuum_s,    wrapped.IspVacuum_s,    precision: 9);
        Assert.Equal(direct.ThrustVacuum_N, wrapped.ThrustVacuum_N, precision: 6);
        Assert.Equal(direct.CoreExitTemp_K, wrapped.CoreExitTemp_K, precision: 6);
    }

    // ── Typed exceptions ─────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NullDesign_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NuclearEngine.Instance.Evaluate(null!, MakeCond()));
    }

    [Fact]
    public void Evaluate_NullConditions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NuclearEngine.Instance.Evaluate(MakeNrxA6(), null!));
    }
}
