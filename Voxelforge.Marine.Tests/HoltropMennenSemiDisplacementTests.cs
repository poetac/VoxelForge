// HoltropMennenSemiDisplacementTests.cs — Sprint M.W5 unit tests for the
// semi-displacement Froude-band correction extending the Sprint M.W4
// Holtrop-Mennen resistance model.

using System;
using System.Linq;
using Voxelforge.Marine;
using Voxelforge.Marine.Hydrodynamics;
using Voxelforge.Marine.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Marine.Tests;

public sealed class HoltropMennenSemiDisplacementTests
{
    private const double Rho_seawater = 1025.0;
    private const double Nu_seawater  = 1.35e-6;

    // ── ComputeSemiDisplacementReductionFactor — pure unit-tests ─────────

    [Fact]
    public void ReductionFactor_Disabled_AlwaysReturnsUnity()
    {
        // SD correction disabled → factor=1.0 across the entire Fn range.
        foreach (double fn in new[] { 0.05, 0.20, 0.30, 0.45, 0.55, 0.70 })
        {
            double r = HoltropMennenResistanceModel
                .ComputeSemiDisplacementReductionFactor(fn, enableSemiDisplacementCorrection: false);
            Assert.Equal(1.0, r, precision: 9);
        }
    }

    [Fact]
    public void ReductionFactor_BelowOnset_ReturnsUnity()
    {
        // SD enabled but Fn ≤ 0.30 → no reduction (bit-identical to M.W4).
        foreach (double fn in new[] { 0.05, 0.10, 0.20, 0.30 })
        {
            double r = HoltropMennenResistanceModel
                .ComputeSemiDisplacementReductionFactor(fn, enableSemiDisplacementCorrection: true);
            Assert.Equal(1.0, r, precision: 9);
        }
    }

    [Fact]
    public void ReductionFactor_AtOnset_ReturnsUnity()
    {
        // Continuity at Fn = 0.30: factor = 1.0.
        double r = HoltropMennenResistanceModel.ComputeSemiDisplacementReductionFactor(0.30, true);
        Assert.Equal(1.0, r, precision: 9);
    }

    [Fact]
    public void ReductionFactor_AtCeiling_ReturnsFloor()
    {
        // At Fn = 0.55: factor = 1.0 − 0.40 = 0.60.
        double r = HoltropMennenResistanceModel.ComputeSemiDisplacementReductionFactor(0.55, true);
        Assert.Equal(0.60, r, precision: 9);
    }

    [Fact]
    public void ReductionFactor_AtMidband_ReturnsQuadraticBlend()
    {
        // Quadratic blend in t = (Fn−0.30)/0.25. At Fn = 0.425 → t = 0.5
        // → factor = 1 − 0.40·0.25 = 0.90.
        double r = HoltropMennenResistanceModel.ComputeSemiDisplacementReductionFactor(0.425, true);
        Assert.Equal(0.90, r, precision: 9);
    }

    [Fact]
    public void ReductionFactor_AboveCeiling_ClampsAtFloor()
    {
        // Above Fn = 0.55 the formula clamps at 0.60 — the gate enforces
        // the upper-Fn cutoff so the model stays at the floor regardless.
        double r = HoltropMennenResistanceModel.ComputeSemiDisplacementReductionFactor(0.70, true);
        Assert.Equal(0.60, r, precision: 9);
    }

    [Fact]
    public void ReductionFactor_Monotonic_DecreasingInFn_WhenEnabled()
    {
        double[] fns = { 0.30, 0.35, 0.40, 0.45, 0.50, 0.55 };
        double prev = HoltropMennenResistanceModel
            .ComputeSemiDisplacementReductionFactor(fns[0], true);
        for (int i = 1; i < fns.Length; i++)
        {
            double cur = HoltropMennenResistanceModel
                .ComputeSemiDisplacementReductionFactor(fns[i], true);
            Assert.True(cur < prev,
                $"Expected factor monotonically decreasing in Fn: prev={prev:F4} at Fn={fns[i - 1]:F3}; "
              + $"cur={cur:F4} at Fn={fns[i]:F3}.");
            prev = cur;
        }
    }

    // ── Solve() — flag effect on R_W ─────────────────────────────────────

    [Fact]
    public void Solve_DisabledFlag_ReducesNothing_AtAllFroudes()
    {
        // V = 5 m/s, L = 40 m → Fn ≈ 0.25 — below the 0.30 onset anyway.
        // The flag should be irrelevant.
        var rOff = HoltropMennenResistanceModel.Solve(
            5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater,
            enableSemiDisplacementCorrection: false);
        var rOn  = HoltropMennenResistanceModel.Solve(
            5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater,
            enableSemiDisplacementCorrection: true);
        Assert.Equal(rOff.WaveMakingResistance_N, rOn.WaveMakingResistance_N, precision: 6);
        Assert.Equal(1.0, rOn.SemiDisplacementReductionFactor, precision: 9);
    }

    [Fact]
    public void Solve_SemiDisplacementBand_ReducesWaveMakingWhenEnabled()
    {
        // Drive Fn into the SD transition band. V = 8 m/s, L = 40 m
        // → Fn = 8/√(9.81·40) = 0.404 — solidly in the SD band [0.30, 0.55].
        var rOff = HoltropMennenResistanceModel.Solve(
            8.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater,
            enableSemiDisplacementCorrection: false);
        var rOn  = HoltropMennenResistanceModel.Solve(
            8.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater,
            enableSemiDisplacementCorrection: true);
        // R_W must drop when SD is enabled.
        Assert.True(rOn.WaveMakingResistance_N < rOff.WaveMakingResistance_N,
            $"SD-enabled R_W = {rOn.WaveMakingResistance_N:F1} N; expected < SD-disabled "
          + $"R_W = {rOff.WaveMakingResistance_N:F1} N at Fn = {rOn.FroudeNumber:F3}.");
        // Factor must be < 1.0.
        Assert.True(rOn.SemiDisplacementReductionFactor < 1.0
                  && rOn.SemiDisplacementReductionFactor >= 0.60);
        // Off-state factor must be exactly 1.0 (no SD).
        Assert.Equal(1.0, rOff.SemiDisplacementReductionFactor, precision: 9);
    }

    [Fact]
    public void Solve_DisplacementOnly_PreservesSprintMW4BehaviorBitIdentically()
    {
        // The default-constructed Solve (without the flag) MUST produce
        // bit-identical R_T to the call with explicit flag=false. This is
        // the bit-identical-Wave-M.W4 invariant.
        var rDefault = HoltropMennenResistanceModel.Solve(
            5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater);
        var rExplicit = HoltropMennenResistanceModel.Solve(
            5.0, 40.0, 8.0, 3.0, 0.65, 600_000, Rho_seawater, Nu_seawater,
            enableSemiDisplacementCorrection: false);
        Assert.Equal(rDefault.TotalResistance_N, rExplicit.TotalResistance_N, precision: 9);
        Assert.Equal(rDefault.WaveMakingResistance_N, rExplicit.WaveMakingResistance_N, precision: 9);
    }

    // ── End-to-end pipeline / gate behaviour ─────────────────────────────

    [Fact]
    public void Design_EnableSemiDisplacement_DefaultsToFalse()
    {
        var d = MakeDisplacementDesign();
        Assert.False(d.EnableSemiDisplacementCorrection);
    }

    [Fact]
    public void GateRelaxedCeiling_PureDisplacement_RejectsFnAbove040()
    {
        // SD flag off, Fn ≈ 0.45 (V=9, L=40) → outside the 0.40 hard ceiling.
        var design = MakeDisplacementDesign() with { EnableSemiDisplacementCorrection = false };
        var cond   = MakeCond(speed_ms: 9.0);
        var r = MarineOptimization.GenerateWith(design, cond);
        Assert.Contains(r.Violations,
            v => v.ConstraintId == MarineConstraintIds.HoltropFroudeOutOfBand);
    }

    [Fact]
    public void GateRelaxedCeiling_SemiDisplacement_AcceptsFnAbove040()
    {
        // SD flag on, Fn ≈ 0.45 (V=9, L=40) → within the relaxed 0.55 hard
        // ceiling. HOLTROP_FROUDE_OUT_OF_BAND must NOT fire.
        var design = MakeDisplacementDesign() with { EnableSemiDisplacementCorrection = true };
        var cond   = MakeCond(speed_ms: 9.0);
        var r = MarineOptimization.GenerateWith(design, cond);
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == MarineConstraintIds.HoltropFroudeOutOfBand);
    }

    [Fact]
    public void SemiDisplacementAdvisory_FiresWhenInBand_AndEnabled()
    {
        // V=8 m/s, L=40 → Fn ≈ 0.404. Flag on → advisory fires.
        var design = MakeDisplacementDesign() with { EnableSemiDisplacementCorrection = true };
        var cond   = MakeCond(speed_ms: 8.0);
        var r = MarineOptimization.GenerateWith(design, cond);
        Assert.Contains(r.Advisories,
            a => a.ConstraintId == MarineConstraintIds.HoltropSemiDisplacementRegime);
    }

    [Fact]
    public void SemiDisplacementAdvisory_SilentWhenDisabled_EvenIfFnInBand()
    {
        // Without the flag, Fn > 0.30 trips the hard ceiling instead — no
        // SD-regime advisory should fire.
        var design = MakeDisplacementDesign() with { EnableSemiDisplacementCorrection = false };
        var cond   = MakeCond(speed_ms: 8.0);
        var r = MarineOptimization.GenerateWith(design, cond);
        Assert.DoesNotContain(r.Advisories,
            a => a.ConstraintId == MarineConstraintIds.HoltropSemiDisplacementRegime);
    }

    [Fact]
    public void SemiDisplacementAdvisory_SilentBelowOnset_EvenWhenEnabled()
    {
        // Flag on but Fn ≤ 0.30 → no advisory; the design is still in
        // the pure-displacement regime.
        var design = MakeDisplacementDesign() with { EnableSemiDisplacementCorrection = true };
        var cond   = MakeCond(speed_ms: 5.0);   // Fn ≈ 0.25
        var r = MarineOptimization.GenerateWith(design, cond);
        Assert.DoesNotContain(r.Advisories,
            a => a.ConstraintId == MarineConstraintIds.HoltropSemiDisplacementRegime);
    }

    [Fact]
    public void SemiDisplacementCeiling_StillRejectsFnAbove055()
    {
        // Even with SD enabled, Fn = 0.60 (V=11.9, L=40) is above the
        // semi-displacement 0.55 ceiling → hard gate must still fire.
        var design = MakeDisplacementDesign() with { EnableSemiDisplacementCorrection = true };
        var cond   = MakeCond(speed_ms: 11.9);
        var r = MarineOptimization.GenerateWith(design, cond);
        Assert.Contains(r.Violations,
            v => v.ConstraintId == MarineConstraintIds.HoltropFroudeOutOfBand);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static MarineDesign MakeDisplacementDesign() => new MarineDesign(
        Kind:                MarineKind.DisplacementSurface,
        Length_m:            40.0,
        Diameter_m:           8.0,            // ignored for DisplacementSurface
        NoseFairingFraction:  0.25,           // ignored
        TailFairingFraction:  0.25,           // ignored
        WallThickness_m:      0.010,          // ignored
        MaterialIndex:        2,
        DepthRating_m:        1.0,            // ignored (surface vehicle)
        HullFamily:           HullFamily.DisplacementSurface) with
    {
        BeamWaterline_m     = 8.0,
        DraftDesign_m       = 3.0,
        BlockCoefficient    = 0.65,
        DisplacementMass_kg = 600_000.0,
    };

    private static MarineConditions MakeCond(double speed_ms) =>
        new(CruiseSpeed_ms: speed_ms, MaxDepth_m: 0.0);
}
