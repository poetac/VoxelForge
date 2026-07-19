// RdeAnnulusFillGateTests.cs — Issue #75: RDE_ANNULUS_FILL_STARVED's
// inter-wave-period threshold used to be reconstructed by inverting
// DetonationWaveCount's own formula (C_nominal = N x v_frac x L_cj), which
// made both N and v_frac cancel out of the final period and collapsed the
// threshold to the same constant (~8.33 us) regardless of the design's
// actual annulus geometry. RegenGenerationResult now carries the true
// annulus circumference (RdeAnnulusCircumference_m) so the gate can compute
// a genuine per-design threshold. These tests inject two different
// circumference values via `with` (mirroring AerospikeGateWiringTests'
// injection pattern) and assert they produce different fire/no-fire
// outcomes at the SAME fill time and wave count — impossible pre-fix, since
// the old formula never read circumference at all.

using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class RdeAnnulusFillGateTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N                = 2224.0,
        ChamberPressure_Pa      = 6.9e6,
        MixtureRatio            = 3.3,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 1,
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    private static RegenGenerationResult BaselineResult() =>
        RegenChamberOptimization.GenerateWith(
            DefaultConditions(),
            new RegenChamberDesign
            {
                IncludeManifolds      = false,
                IncludePorts          = false,
                IncludeInjectorFlange = false,
                ContourStationCount   = 60,
            });

    // waveFreq_hz = 2400.0 * 0.90 / C = 2160.0 / C
    // interWavePeriod_us = 1e6 / (N * waveFreq_hz) = 1e6 * C / (N * 2160.0)
    //
    // N = 2, C = 0.02 m -> period ~= 4.63 us; fill time 10.0 us > period -> FIRES.
    // N = 2, C = 0.10 m -> period ~= 23.15 us; fill time 10.0 us <= period -> silent.
    // Pre-fix, both cases collapsed to the same constant (~8.33 us) regardless
    // of C, so both would have fired identically -- this split is impossible
    // pre-fix.

    [Fact]
    public void Evaluate_SmallCircumferenceFastWaves_FiresAnnulusFillStarved()
    {
        var gen = BaselineResult() with
        {
            RdeTopology               = RdeTopology.Annular,
            RdeWaveCount              = 2,
            RdeAnnulusFillTime_us     = 10.0,
            RdeAnnulusCircumference_m = 0.02,
        };

        var result = FeasibilityGate.Evaluate(gen);

        Assert.Contains(result.Violations, v => v.ConstraintId == "RDE_ANNULUS_FILL_STARVED");
    }

    [Fact]
    public void Evaluate_LargeCircumferenceSlowWaves_DoesNotFireAnnulusFillStarved()
    {
        var gen = BaselineResult() with
        {
            RdeTopology               = RdeTopology.Annular,
            RdeWaveCount              = 2,
            RdeAnnulusFillTime_us     = 10.0,
            RdeAnnulusCircumference_m = 0.10,
        };

        var result = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "RDE_ANNULUS_FILL_STARVED");
    }

    [Fact]
    public void Evaluate_NonRdeBaseline_UnaffectedByAnnulusFillGate()
    {
        var baseline = BaselineResult();
        Assert.Equal(RdeTopology.None, baseline.RdeTopology);

        var result = FeasibilityGate.Evaluate(baseline);

        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "RDE_ANNULUS_FILL_STARVED");
    }

    [Fact]
    public void Evaluate_ZeroCircumferenceLegacyResult_GateStaysSilent()
    {
        // Legacy/hand-built RegenGenerationResult values (default
        // RdeAnnulusCircumference_m = 0.0) must not throw or false-fire --
        // the gate treats this as "not enough information", same as it
        // already does for RdeWaveCount <= 0 / RdeAnnulusFillTime_us <= 0.
        var gen = BaselineResult() with
        {
            RdeTopology           = RdeTopology.Annular,
            RdeWaveCount          = 2,
            RdeAnnulusFillTime_us = 10.0,
            // RdeAnnulusCircumference_m left at its 0.0 default.
        };

        var result = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "RDE_ANNULUS_FILL_STARVED");
    }
}
