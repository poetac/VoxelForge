// OOB-11 (issue #340): monopropellant sizing tests.
using Voxelforge.Combustion;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class MonopropTests
{
    // ── Table anchor tests ──────────────────────────────────────────────────

    [Fact]
    public void SpecFor_H2O2_90pct_ReturnsCorrectAnchorValues()
    {
        var spec = MonopropTables.SpecFor(MonopropellantKind.H2O2_90pct);
        Assert.Equal(165.0, spec.Isp_vac_s, precision: 9);
        Assert.Equal(1174.0, spec.Tc_K, precision: 9);
        Assert.Equal(1.26, spec.Gamma, precision: 9);
        Assert.Equal(21.8, spec.MolWeight_kgmol, precision: 9);
        Assert.Equal(1400.0, spec.Density_kgm3, precision: 9);
        Assert.Equal(10.0, spec.CatalystLoadingLimit_kgm2s, precision: 9);
    }

    [Fact]
    public void SpecFor_HAN269_ReturnsCorrectAnchorValues()
    {
        var spec = MonopropTables.SpecFor(MonopropellantKind.HAN_269);
        Assert.Equal(252.0, spec.Isp_vac_s, precision: 9);
        Assert.Equal(2023.0, spec.Tc_K, precision: 9);
        Assert.Equal(1440.0, spec.Density_kgm3, precision: 9);
        Assert.Equal(8.0, spec.CatalystLoadingLimit_kgm2s, precision: 9);
    }

    [Fact]
    public void SpecFor_None_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => MonopropTables.SpecFor(MonopropellantKind.None));
    }

    // ── Isp correction direction tests ─────────────────────────────────────

    [Fact]
    public void Isp_HigherExpansionRatio_YieldsHigherIsp()
    {
        double isp40 = MonopropTables.Isp(MonopropellantKind.H2O2_90pct, 2e6, 40.0);
        double isp100 = MonopropTables.Isp(MonopropellantKind.H2O2_90pct, 2e6, 100.0);
        Assert.True(isp100 > isp40,
            $"Expected Isp(ε=100) > Isp(ε=40): {isp100:F2} vs {isp40:F2}");
    }

    [Fact]
    public void Isp_AllKinds_WithinEightPercentOfAnchor()
    {
        foreach (var kind in new[] { MonopropellantKind.H2O2_90pct, MonopropellantKind.H2O2_98pct, MonopropellantKind.HAN_269 })
        {
            var spec = MonopropTables.SpecFor(kind);
            double isp = MonopropTables.Isp(kind, 2e6, 40.0);
            Assert.InRange(isp, spec.Isp_vac_s * 0.92, spec.Isp_vac_s * 1.08);
        }
    }

    // ── Sizing mass-balance tests ───────────────────────────────────────────

    [Fact]
    public void Size_H2O2_90pct_MassBalanceHolds()
    {
        // F = mdot × g0 × Isp → mdot = F / (g0 × Isp), check ±1 %.
        var design = new MonopropDesign
        {
            Propellant = MonopropellantKind.H2O2_90pct,
            Thrust_N = 10.0,
            ChamberPressure_Pa = 2e6,
            ExpansionRatio = 50.0,
            CatalystBedDiameter_mm = 30.0,
        };
        var result = MonopropSizing.Size(design);
        double recomputed = result.MassFlow_kgs * 9.80665 * result.Isp_vac_s;
        Assert.InRange(recomputed, design.Thrust_N * 0.99, design.Thrust_N * 1.01);
    }

    [Fact]
    public void Size_AllPropellantKinds_DoNotThrow()
    {
        foreach (var kind in new[] { MonopropellantKind.H2O2_90pct, MonopropellantKind.H2O2_98pct, MonopropellantKind.HAN_269 })
        {
            var design = new MonopropDesign
            {
                Propellant = kind,
                Thrust_N = 5.0,
                ChamberPressure_Pa = 1.5e6,
                ExpansionRatio = 50.0,
                CatalystBedDiameter_mm = 25.0,
            };
            var result = MonopropSizing.Size(design);
            Assert.True(result.ThroatRadius_mm > 0);
            Assert.True(result.MassFlow_kgs > 0);
        }
    }

    // ── Gate: MONOPROP_CATALYST_OVERLOADED ─────────────────────────────────

    [Fact]
    public void Size_SmallBedDiameter_CatalystOverloadedGateFires()
    {
        // Tiny bed → very high loading → should exceed limit.
        var design = new MonopropDesign
        {
            Propellant = MonopropellantKind.H2O2_90pct,
            Thrust_N = 200.0,
            ChamberPressure_Pa = 2e6,
            ExpansionRatio = 50.0,
            CatalystBedDiameter_mm = 5.0, // tiny bed, forced overload
        };
        var result = MonopropSizing.Size(design);
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "MONOPROP_CATALYST_OVERLOADED");
        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public void Size_LargeBedDiameter_CatalystGateSilent()
    {
        // Generous bed → loading below limit → gate silent.
        var design = new MonopropDesign
        {
            Propellant = MonopropellantKind.H2O2_90pct,
            Thrust_N = 1.0,
            ChamberPressure_Pa = 1.5e6,
            ExpansionRatio = 50.0,
            CatalystBedDiameter_mm = 80.0,
        };
        var result = MonopropSizing.Size(design);
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "MONOPROP_CATALYST_OVERLOADED");
        Assert.True(result.IsAcceptable);
    }

    // ── Gate: MONOPROP_CHAMBER_TEMP_EXCEEDS_BED ────────────────────────────

    [Fact]
    public void Size_HAN269_HighTcAdvisoryGateFires()
    {
        // HAN-269 Tc = 2023 K > 1700 K limit → advisory fires.
        var design = new MonopropDesign
        {
            Propellant = MonopropellantKind.HAN_269,
            Thrust_N = 5.0,
            ChamberPressure_Pa = 2e6,
            ExpansionRatio = 50.0,
            CatalystBedDiameter_mm = 60.0,
        };
        var result = MonopropSizing.Size(design);
        Assert.Contains(result.Violations,
            v => v.ConstraintId == "MONOPROP_CHAMBER_TEMP_EXCEEDS_BED");
    }

    [Fact]
    public void Size_H2O2_90pct_HighTcGateSilent()
    {
        // H2O2 90 % Tc = 1174 K < 1700 K → advisory silent.
        var design = new MonopropDesign
        {
            Propellant = MonopropellantKind.H2O2_90pct,
            Thrust_N = 5.0,
            ChamberPressure_Pa = 2e6,
            ExpansionRatio = 50.0,
            CatalystBedDiameter_mm = 60.0,
        };
        var result = MonopropSizing.Size(design);
        Assert.DoesNotContain(result.Violations,
            v => v.ConstraintId == "MONOPROP_CHAMBER_TEMP_EXCEEDS_BED");
    }
}
