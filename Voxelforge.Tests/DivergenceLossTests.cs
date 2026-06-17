// PH-19 (#176, 2026-04-29): divergence loss decomposed from lumped
// NozzleCfEfficiency. C_F = C_F_ideal · λ_div(ε, L%) · η_BL+2Φ.
//
// Tests in three groups:
//   1. RaoBellTable.DivergenceLossFactor returns (1+cos θ_e)/2 to
//      machine precision at every (ε, L%) anchor.
//   2. ComputeDerived applies the factor on bell topologies and
//      bypasses it on aerospike topologies.
//   3. Bell-length sensitivity: long shallow bells (high L%) score
//      higher C_F than short steep bells (low L%) at the same ε.
//      This is the SA "trade bell length vs Isp" hook the issue #176
//      explicitly enables.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class DivergenceLossTests
{
    // ── Group 1: pure-math factor at the (ε, L%) anchors ─────────────

    [Theory]
    [InlineData(4.0,   0.80, 14.0)]   // RaoBellTable anchor: ε=4,    L%=0.80 → θ_e=14°
    [InlineData(10.0,  0.80, 10.0)]   // RaoBellTable anchor: ε=10,   L%=0.80 → θ_e=10°
    [InlineData(25.0,  0.80,  8.0)]   // RaoBellTable anchor: ε=25,   L%=0.80 → θ_e=8°
    [InlineData(50.0,  0.80,  7.0)]   // RaoBellTable anchor: ε=50,   L%=0.80 → θ_e=7°
    [InlineData(100.0, 0.80,  6.0)]   // RaoBellTable anchor: ε=100,  L%=0.80 → θ_e=6°
    [InlineData(4.0,   0.60, 16.0)]   // L%-shift corner: ε=4,        L%=0.60 → θ_e=16°
    [InlineData(100.0, 1.00,  4.0)]   // L%-shift corner: ε=100,      L%=1.00 → θ_e=4°
    public void DivergenceLossFactor_MatchesClosedForm(
        double epsilon, double lengthFraction, double thetaE_deg)
    {
        double thetaE_rad = thetaE_deg * Math.PI / 180.0;
        double expected = 0.5 * (1.0 + Math.Cos(thetaE_rad));
        double actual = RaoBellTable.DivergenceLossFactor(epsilon, lengthFraction);
        Assert.Equal(expected, actual, precision: 9);
    }

    [Fact]
    public void DivergenceLossFactor_DecreasesWithSteeperExitAngle()
    {
        // ε=4 / L%=0.60 (θ_e=16°) is the steepest exit in the table → smallest λ_div.
        // ε=100 / L%=1.00 (θ_e=4°) is the shallowest → largest λ_div.
        double steep   = RaoBellTable.DivergenceLossFactor(4.0,   0.60);
        double shallow = RaoBellTable.DivergenceLossFactor(100.0, 1.00);
        Assert.True(steep < shallow,
            $"Steep θ_e should have smaller λ_div; steep = {steep:F6}, shallow = {shallow:F6}.");
        // Magnitudes (sanity, not ratchets).
        Assert.InRange(steep,   0.96, 0.99);
        Assert.InRange(shallow, 0.99, 1.00);
    }

    [Fact]
    public void DivergenceLossFactor_IsContinuousAcrossInterpolation()
    {
        // No NaN / step at intermediate (ε, L%) values — the underlying
        // Lookup is a bilinear interpolation, so DivergenceLossFactor must
        // be continuous everywhere inside the table.
        double[] epsilons = { 5.0, 12.5, 22.0, 37.5, 75.0 };
        double[] lengths  = { 0.65, 0.75, 0.85, 0.95 };
        foreach (var eps in epsilons)
        foreach (var lf  in lengths)
        {
            double f = RaoBellTable.DivergenceLossFactor(eps, lf);
            Assert.True(double.IsFinite(f), $"NaN/Inf at (ε={eps}, L%={lf}).");
            Assert.InRange(f, 0.95, 1.0);
        }
    }

    // ── Group 2: ComputeDerived dispatch (bell vs aerospike) ─────────

    private static OperatingConditions BaseCond() => new()
    {
        Thrust_N                = 5_000,
        ChamberPressure_Pa      = 7e6,
        MixtureRatio            = 3.4,
        CoolantInletTemp_K      = 150,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex       = 1,
        PropellantPair          = PropellantPair.LOX_CH4,
        AmbientPressure_Pa      = 0,        // vacuum to isolate divergence from base-drag (PH-18)
    };

    [Fact]
    public void ComputeDerived_BellTopology_AppliesDivergenceLossFromTable()
    {
        var cond = BaseCond();
        var gas  = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                           cond.ChamberPressure_Pa);
        var bell = new RegenChamberDesign
        {
            ChannelTopology     = ChannelTopology.Axial,
            ExpansionRatio      = 25.0,
            BellLengthFraction  = 0.80,
        };
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, bell);
        // ε=25, L%=0.80 → θ_e=8° → λ_div = (1+cos 8°)/2 ≈ 0.99513.
        double expected = RaoBellTable.DivergenceLossFactor(25.0, 0.80);
        Assert.Equal(expected, derived.DivergenceLoss, precision: 9);
        Assert.InRange(derived.DivergenceLoss, 0.99, 1.00);
    }

    [Theory]
    [InlineData(ChannelTopology.Aerospike)]
    [InlineData(ChannelTopology.LinearAerospike)]
    public void ComputeDerived_AerospikeTopology_BypassesDivergenceLoss(
        ChannelTopology topology)
    {
        var cond = BaseCond();
        var gas  = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                           cond.ChamberPressure_Pa);
        var design = new RegenChamberDesign
        {
            ChannelTopology     = topology,
            PlugLengthRatio     = 1.0,           // full plug → no PH-18 base drag either
            ExpansionRatio      = 25.0,
            BellLengthFraction  = 0.80,
        };
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);
        Assert.Equal(1.0, derived.DivergenceLoss);
    }

    // ── Group 3: SA hook — trade bell length vs Isp ──────────────────

    [Fact]
    public void ComputeDerived_LongShallowBell_BeatsShortSteepBell_OnCf_AndOnSeaLevelIsp()
    {
        // Same ε, different L%. Long shallow (L%=1.00) → small θ_e → high
        // λ_div → high C_F. Short steep (L%=0.60) → large θ_e → low λ_div
        // → low C_F. This is the SA "trade bell length vs Isp" mechanism
        // that PH-19 unlocks (per issue #176). Pre-PH-19 these two designs
        // scored identical C_F because λ_div was lumped into NozzleCfEfficiency.
        //
        // Isp_vac in ComputeDerived is sourced directly from the propellant
        // table (gas.IspVacuum_s), not derived from C_F, so the vacuum-Isp
        // numbers don't move when C_F moves. Isp_sl, on the other hand,
        // applies the (C_F / (C_F + p_amb·ε/p_c)) factor and DOES move with
        // C_F at non-zero ambient — that's the channel through which SA
        // sees the trade-off. We assert both: ThrustCoefficient (direct
        // physics) and Isp_sl at non-zero ambient (the SA scoring channel).
        var cond = BaseCond() with { AmbientPressure_Pa = 101_325.0 };
        var gas  = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                           cond.ChamberPressure_Pa);
        var shortSteep = new RegenChamberDesign
        {
            ChannelTopology     = ChannelTopology.Axial,
            ExpansionRatio      = 25.0,
            BellLengthFraction  = 0.60,
        };
        var longShallow = shortSteep with { BellLengthFraction = 1.00 };
        var dShort = RegenChamberOptimization.ComputeDerived(cond, gas, shortSteep);
        var dLong  = RegenChamberOptimization.ComputeDerived(cond, gas, longShallow);
        Assert.True(dLong.DivergenceLoss > dShort.DivergenceLoss,
            $"Shallow bell should have larger λ_div; long = {dLong.DivergenceLoss:F6}, "
          + $"short = {dShort.DivergenceLoss:F6}.");
        Assert.True(dLong.ThrustCoefficient > dShort.ThrustCoefficient,
            $"Shallow bell should have higher C_F; long = {dLong.ThrustCoefficient:F6}, "
          + $"short = {dShort.ThrustCoefficient:F6}.");
        Assert.True(dLong.IdealIspSeaLevel_s > dShort.IdealIspSeaLevel_s,
            $"Shallow bell should beat short steep on Isp_sl; "
          + $"long = {dLong.IdealIspSeaLevel_s:F2}, short = {dShort.IdealIspSeaLevel_s:F2}.");
    }

    [Fact]
    public void ComputeDerived_DivergenceLoss_FlowsIntoMassFlow()
    {
        // Lower λ_div → lower C_F → larger throat (for fixed thrust) → higher
        // mDot. This is the second-order coupling that lets SA see a real
        // mass+performance trade-off, not just an Isp number.
        var cond = BaseCond() with { Thrust_N = 50_000 };   // bigger thrust amplifies the gap
        var gas  = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio,
                                           cond.ChamberPressure_Pa);
        var shortSteep = new RegenChamberDesign
        {
            ChannelTopology     = ChannelTopology.Axial,
            ExpansionRatio      = 25.0,
            BellLengthFraction  = 0.60,
        };
        var longShallow = shortSteep with { BellLengthFraction = 1.00 };
        var dShort = RegenChamberOptimization.ComputeDerived(cond, gas, shortSteep);
        var dLong  = RegenChamberOptimization.ComputeDerived(cond, gas, longShallow);
        Assert.True(dShort.TotalMassFlow_kgs > dLong.TotalMassFlow_kgs,
            $"Lower λ_div → bigger throat → higher mDot; "
          + $"short = {dShort.TotalMassFlow_kgs:F4}, long = {dLong.TotalMassFlow_kgs:F4}.");
        Assert.True(dShort.ThroatRadius_mm > dLong.ThroatRadius_mm);
    }
}
