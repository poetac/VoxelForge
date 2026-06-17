// OOB-9 (issue #344): finite-rate chemistry Isp correction tests.
using System.IO;
using Voxelforge.Combustion;
using Voxelforge.IO;
using Voxelforge.Optimization;
using Voxelforge.Tests.Helpers;
using Xunit;

namespace Voxelforge.Tests;

public class FiniteRateChemistryTests
{
    // ── Correction factor direction tests ───────────────────────────────────

    [Fact]
    public void CorrectionFactor_DecreasesWithLowerPc_LOX_CH4()
    {
        double lowPc  = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_CH4, 3e6,  3.5);
        double highPc = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_CH4, 20e6, 3.5);
        Assert.True(lowPc < highPc,
            $"Lower Pc should yield larger penalty: f(3 MPa)={lowPc:F4}, f(20 MPa)={highPc:F4}");
    }

    [Fact]
    public void CorrectionFactor_LOX_H2_LargerPenaltyThanLOX_RP1_AtSamePc()
    {
        double h2  = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_H2,  3e6, 6.0);
        double rp1 = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_RP1, 3e6, 2.8);
        Assert.True(h2 < rp1,
            $"LOX/H2 should have larger dissociation penalty than LOX/RP1: {h2:F4} vs {rp1:F4}");
    }

    [Fact]
    public void CorrectionFactor_AllPairs_ReturnsBetween096And100()
    {
        foreach (var pair in new[] { PropellantPair.LOX_CH4, PropellantPair.LOX_H2, PropellantPair.LOX_RP1 })
        {
            foreach (double pc in new[] { 3e6, 8e6, 20e6 })
            {
                double f = FiniteRateCorrection.DissociationCorrectionFactor(pair, pc, 3.5);
                Assert.InRange(f, 0.96, 1.00);
            }
        }
    }

    [Fact]
    public void CorrectionFactor_InterpolatesMonotonically_LOX_CH4()
    {
        double f3  = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_CH4, 3e6,  3.5);
        double f10 = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_CH4, 10e6, 3.5);
        double f20 = FiniteRateCorrection.DissociationCorrectionFactor(PropellantPair.LOX_CH4, 20e6, 3.5);
        Assert.True(f3 < f10 && f10 < f20,
            $"Factor must increase monotonically with Pc: {f3:F4} < {f10:F4} < {f20:F4}");
    }

    // ── GenerateWith integration tests ─────────────────────────────────────

    private static OperatingConditions BaseConditions() => new()
    {
        Thrust_N                = 5000.0,
        ChamberPressure_Pa      = 3e6,
        MixtureRatio            = 3.5,
        CoolantInletTemp_K      = 150.0,
        CoolantInletPressure_Pa = 8e6,
        WallMaterialIndex       = 1,
        PropellantPair          = PropellantPair.LOX_CH4,
    };

    [Fact]
    public void GenerateWith_FiniteRateDisabled_BitIdenticalToLegacy()
    {
        var cond = BaseConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts     = false,
        };
        var r1 = RegenChamberOptimization.GenerateWith(cond, design);
        var r2 = RegenChamberOptimization.GenerateWith(
            cond with { UseFiniteRateCorrection = false }, design);
        Assert.Equal(r1.Derived.IdealIspVacuum_s, r2.Derived.IdealIspVacuum_s, precision: 9);
        Assert.Equal(1.0, r1.FiniteRateCorrectionFactor, precision: 9);
    }

    [Fact]
    public void GenerateWith_FiniteRateEnabled_ReducesIsp()
    {
        var cond = BaseConditions();
        var design = new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts     = false,
        };
        var withoutFr = RegenChamberOptimization.GenerateWith(cond, design);
        var withFr    = RegenChamberOptimization.GenerateWith(
            cond with { UseFiniteRateCorrection = true }, design);
        Assert.True(withFr.Derived.IdealIspVacuum_s < withoutFr.Derived.IdealIspVacuum_s,
            $"FR correction must reduce Isp: {withFr.Derived.IdealIspVacuum_s:F3} < {withoutFr.Derived.IdealIspVacuum_s:F3}");
        Assert.InRange(withFr.FiniteRateCorrectionFactor, 0.96, 0.9999);
    }

    // ── Gate tests ─────────────────────────────────────────────────────────

    [Fact]
    public void Gate_FINITE_RATE_ISP_PENALTY_LARGE_FiresWhenFactorBelow0985()
    {
        // LOX/H2 at 3 MPa → factor ≈ 0.973, well below 0.985 threshold.
        var cond = BaseConditions() with
        {
            PropellantPair          = PropellantPair.LOX_H2,
            MixtureRatio            = 6.0,
            ChamberPressure_Pa      = 3e6,
            UseFiniteRateCorrection = true,
        };
        var result = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts     = false,
        });
        var gate = FeasibilityGate.Evaluate(result);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "FINITE_RATE_ISP_PENALTY_LARGE");
    }

    [Fact]
    public void Gate_FINITE_RATE_ISP_PENALTY_LARGE_SilentWhenDisabled()
    {
        // Same low-Pc LOX/H2 scenario but UseFiniteRateCorrection = false.
        var cond = BaseConditions() with
        {
            PropellantPair          = PropellantPair.LOX_H2,
            MixtureRatio            = 6.0,
            ChamberPressure_Pa      = 3e6,
            UseFiniteRateCorrection = false,
        };
        var result = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts     = false,
        });
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations, v => v.ConstraintId == "FINITE_RATE_ISP_PENALTY_LARGE");
    }

    [Fact]
    public void Gate_FINITE_RATE_ISP_PENALTY_LARGE_SilentAtHighPc_LOX_CH4()
    {
        // LOX/CH4 at 20 MPa → factor ≈ 0.995, above 0.985 threshold.
        var cond = BaseConditions() with
        {
            PropellantPair          = PropellantPair.LOX_CH4,
            ChamberPressure_Pa      = 20e6,
            UseFiniteRateCorrection = true,
        };
        var result = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign
        {
            IncludeManifolds = false,
            IncludePorts     = false,
        });
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations, v => v.ConstraintId == "FINITE_RATE_ISP_PENALTY_LARGE");
    }

    // ── Schema tests ────────────────────────────────────────────────────────

    [Fact]
    public void CurrentSchemaVersion_IsV30()
    {
        Assert.Equal("v31", DesignPersistence.CurrentSchemaVersion);
    }

    [Fact]
    public void Schema_V29Design_LoadsWithUseFiniteRateCorrectionFalse()
    {
        const string v29Json = """
            {
              "Schema": "v29",
              "Version": "1.0",
              "Conditions": {
                "Thrust_N": 5000,
                "ChamberPressure_Pa": 3000000,
                "MixtureRatio": 3.5,
                "CoolantInletTemp_K": 150,
                "CoolantInletPressure_Pa": 8000000,
                "WallMaterialIndex": 1
              },
              "Design": {}
            }
            """;
        using var tmp = TestTempFile.WithUniqueName("fr-migration-v29", "json");
        File.WriteAllText(tmp.Path, v29Json);
        var loaded = DesignPersistence.Load(tmp.Path);
        Assert.NotNull(loaded);
        Assert.Equal("v31", loaded!.Schema);
        Assert.False(loaded.Conditions!.UseFiniteRateCorrection);
    }
}
