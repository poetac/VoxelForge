// AcousticDamperVolumeGateTests.cs — Issue #82: ACOUSTIC_DAMPER_OVERSIZED
// used to reconstruct a throwaway zero-geometry AcousticDamperConfig
// (NeckArea_mm2/NeckLength_mm/CavityVolume_mm3 all hardcoded to 0) because
// AcousticDamperResult never carried the aggregate cavity volume through
// from the live config -- the reconstruction was provably dead code
// (`_ = config;`), so only the resonator-count-alone proxy could ever fire.
// AcousticDamperResult now carries TotalVolume_mm3, stamped by
// AcousticDamper.Evaluate itself before the config goes out of scope, so
// the gate can check true cavity displacement against chamber volume.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class AcousticDamperVolumeGateTests
{
    // --- Unit level: AcousticDamper.Evaluate stamps TotalVolume_mm3 -------

    [Fact]
    public void Evaluate_HelmholtzConfig_StampsTotalVolumeFromConfig()
    {
        // neckLength_mm = 0 zeroes the neck-volume term (NeckArea_mm2 still
        // must be > 0 for IsActive), isolating TotalVolume_mm3 to
        // Count * CavityVolume_mm3 = 8 * 1000 = 8000 for an easy assertion.
        var config = AcousticDamperConfig.Helmholtz(
            count: 8, neckArea_mm2: 10.0, neckLength_mm: 0.0, cavityVolume_mm3: 1000.0);
        var screech = new ScreechModeResult(
            SoundSpeed_ms: 1800.0, L1_Hz: 5000.0, T1_Hz: 9000.0, T2_Hz: 15000.0);

        var result = AcousticDamper.Evaluate(config, screech);

        Assert.NotNull(result);
        Assert.Equal(8000.0, result!.TotalVolume_mm3, precision: 6);
        Assert.Equal(config.TotalVolume_mm3, result.TotalVolume_mm3, precision: 6);
    }

    // --- Gate level: ACOUSTIC_DAMPER_OVERSIZED on true cavity volume ------

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

    private static AcousticDamperResult MakeDamper(int count, double totalVolume_mm3) => new(
        Type:                  AcousticDamperType.Helmholtz,
        Count:                 count,
        ResonanceFrequency_Hz: 9000.0,
        DampingRatio_L1:       0.02,
        DampingRatio_T1:       0.03,
        DampingRatio_T2:       0.01,
        IsTunedToAnyMode:      true,
        Notes:                 "test fixture")
    {
        TotalVolume_mm3 = totalVolume_mm3,
    };

    private static RegenGenerationResult WithDamper(RegenGenerationResult baseline, AcousticDamperResult damper) =>
        baseline with { Stability = baseline.Stability with { AcousticDamper = damper } };

    [Fact]
    public void Evaluate_DamperVolumeOver5PercentOfChamber_FiresOversized()
    {
        var baseline = BaselineResult();
        double chamberVolume_mm3 = baseline.Contour.ChamberVolume_mm3;
        Assert.True(chamberVolume_mm3 > 0.0);   // sanity: fixture actually has a real chamber volume

        // Count = 4 stays under the count-fallback threshold (16), isolating
        // this to the volume check -- impossible pre-fix, since the old gate
        // body never read any volume field at all.
        var gen = WithDamper(baseline, MakeDamper(count: 4, totalVolume_mm3: chamberVolume_mm3 * 0.10));

        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.Contains(score.FeasibilityViolations, v => v.ConstraintId == "ACOUSTIC_DAMPER_OVERSIZED");
    }

    [Fact]
    public void Evaluate_DamperVolumeUnder5PercentOfChamber_DoesNotFireOversized()
    {
        var baseline = BaselineResult();
        double chamberVolume_mm3 = baseline.Contour.ChamberVolume_mm3;

        var gen = WithDamper(baseline, MakeDamper(count: 4, totalVolume_mm3: chamberVolume_mm3 * 0.01));

        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.DoesNotContain(score.FeasibilityViolations, v => v.ConstraintId == "ACOUSTIC_DAMPER_OVERSIZED");
    }

    [Fact]
    public void Evaluate_ZeroVolumeLegacyResultWithHighCount_FallsBackToCountCheck()
    {
        // TotalVolume_mm3 = 0.0 (legacy/hand-built result) with Count > 16
        // must still fire via the pre-existing count-alone fallback --
        // the new volume check must not silently swallow this case.
        var baseline = BaselineResult();
        var gen = WithDamper(baseline, MakeDamper(count: 20, totalVolume_mm3: 0.0));

        var score = RegenChamberOptimization.Evaluate(gen, RegenChamberOptimization.Profiles[0]);

        Assert.Contains(score.FeasibilityViolations, v => v.ConstraintId == "ACOUSTIC_DAMPER_OVERSIZED");
    }

    [Fact]
    public void Evaluate_NoDamperBaseline_UnaffectedByOversizedGate()
    {
        var baseline = BaselineResult();
        Assert.Null(baseline.Stability.AcousticDamper);

        var score = RegenChamberOptimization.Evaluate(baseline, RegenChamberOptimization.Profiles[0]);

        Assert.DoesNotContain(score.FeasibilityViolations, v => v.ConstraintId == "ACOUSTIC_DAMPER_OVERSIZED");
    }
}
