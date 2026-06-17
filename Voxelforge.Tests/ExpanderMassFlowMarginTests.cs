// Sprint 34d (2026-04-25) / PH-9 — ExpanderTurbineResult.MassFlowMargin tests.
//
// PH-9 (audit 2026-04-23) flagged the expander cycle's energy balance as
// one-shot, recommending Picard iteration on coolant mass flow. Sprint 34d
// analysis showed the audit's recipe applies to expander models that treat
// ṁ_c as a free variable (jacket-bypass throttling). The current voxelforge
// model pins ṁ_c by mass balance (RegenChamberOptimization.cs:303-305 →
// `coolantMassFlow = FuelMassFlow × (1 - FilmCoolingFraction)`), so the
// regen solver is a forward march and the expander balance is genuinely
// one-shot. PH-9 ships the MassFlowMargin diagnostic field that exposes
// (P_avail / P_required - 1); these tests pin its sign convention and
// behaviour relative to the existing PowerSufficient gate.

using Voxelforge.Coolant;
using Voxelforge.FeedSystem;
using Xunit;

namespace Voxelforge.Tests;

public class ExpanderMassFlowMarginTests
{
    private static ExpanderTurbineResult? SizeRl10Class(double requiredPower_W = 1.5e6)
    {
        // RL-10-class closed-expander: H₂ at 200 K / 12 MPa jacket outlet,
        // 100 K inlet (chilldown), 4.0 kg/s fuel flow, Pc = 3.3 MPa.
        return ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.ClosedExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         200.0,
            coolantOutletP_Pa:        12.0e6,
            coolantInletT_K:          100.0,
            coolantMassFlow_kgs:      4.0,
            mainChamberPressure_Pa:   3.3e6,
            requiredPumpShaftPower_W: requiredPower_W);
    }

    [Fact]
    public void MassFlowMargin_PowerSurplus_IsPositive()
    {
        // Tiny pump load → expander has surplus → margin > 0.
        var r = SizeRl10Class(requiredPower_W: 1.0e5);
        Assert.NotNull(r);
        Assert.True(r!.PowerSufficient);
        Assert.True(r.MassFlowMargin > 0,
            $"expected MassFlowMargin > 0 for power surplus, got {r.MassFlowMargin:F4}");
    }

    [Fact]
    public void MassFlowMargin_PowerDeficit_IsNegative_AndMatchesGateFiring()
    {
        // Huge pump load → expander insufficient → margin < 0 AND
        // PowerSufficient = false (the gate condition).
        var r = SizeRl10Class(requiredPower_W: 5.0e7);
        Assert.NotNull(r);
        Assert.False(r!.PowerSufficient);
        Assert.True(r.MassFlowMargin < 0,
            $"expected MassFlowMargin < 0 for power deficit, got {r.MassFlowMargin:F4}");
        // Sign correlation: margin sign and !PowerSufficient must agree.
        Assert.Equal(r.MassFlowMargin >= 0, r.PowerSufficient);
    }

    [Fact]
    public void MassFlowMargin_RatioFormula_MatchesPAvailOverPRequired()
    {
        var r = SizeRl10Class(requiredPower_W: 1.5e6);
        Assert.NotNull(r);
        double expected = (r!.AvailableShaftPower_W / r.RequiredShaftPower_W) - 1.0;
        Assert.Equal(expected, r.MassFlowMargin, precision: 9);
    }

    [Fact]
    public void MassFlowMargin_OpenExpander_AlsoComputed()
    {
        // Both cycles should report MassFlowMargin (not just closed).
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         200.0,
            coolantOutletP_Pa:        12.0e6,
            coolantInletT_K:          100.0,
            coolantMassFlow_kgs:      4.0,
            mainChamberPressure_Pa:   3.3e6,
            requiredPumpShaftPower_W: 1.5e6);
        Assert.NotNull(r);
        // OpenExpander vents to ~ambient → much higher pressure ratio →
        // larger w → higher margin than closed at the same conditions.
        var rClosed = SizeRl10Class(requiredPower_W: 1.5e6);
        Assert.True(r!.MassFlowMargin > rClosed!.MassFlowMargin,
            $"expected open margin ({r.MassFlowMargin:F3}) > closed margin ({rClosed.MassFlowMargin:F3})");
    }

    [Fact]
    public void MassFlowMargin_ZeroRequiredPower_ReportsZero()
    {
        // Degenerate guard: when no pump power required, margin defined as 0.
        var r = SizeRl10Class(requiredPower_W: 0.0);
        Assert.NotNull(r);
        Assert.Equal(0.0, r!.MassFlowMargin);
    }

    [Fact]
    public void MassFlowMargin_ForwardExpansionFails_DefaultsToZero()
    {
        // When jacket-outlet P drops below back-pressure (no forward
        // expansion), Size returns the early-return result with all zero
        // power values. MassFlowMargin defaults to 0 there.
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.ClosedExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         200.0,
            coolantOutletP_Pa:        2.0e6,    // below Pc × 1.30 = 4.29 MPa
            coolantInletT_K:          100.0,
            coolantMassFlow_kgs:      4.0,
            mainChamberPressure_Pa:   3.3e6,
            requiredPumpShaftPower_W: 1.5e6);
        Assert.NotNull(r);
        Assert.False(r!.PowerSufficient);
        Assert.Equal(0.0, r.MassFlowMargin);
    }

    // ═════════════════════════════════════════════════════════════════
    //   Model-assumption pin — documents WHY no Picard iteration here
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public void OneShotExpanderBalance_IsCorrect_UnderPinnedMassFlowModel()
    {
        // Under voxelforge's current model `coolantMassFlow = FuelMassFlow ×
        // (1 - FilmCoolingFraction)` — see RegenChamberOptimization.cs:303-305.
        // ṁ_c is pinned by chamber-side mass balance, not by power balance.
        //
        // Consequence: the audit's PH-9 recipe ("Picard iterate ṁ_c until
        // |Δṁ_c|/ṁ_c < 0.01") cannot apply because there's no degree of
        // freedom to shift ṁ_c. The "converged" state is whatever the mass
        // balance dictates, and MassFlowMargin reports the resulting power
        // balance. This test pins that semantics: calling Size twice with
        // the SAME inputs must produce a bit-identical result — there's no
        // hidden iteration state that could drift.
        var r1 = SizeRl10Class(requiredPower_W: 1.5e6);
        var r2 = SizeRl10Class(requiredPower_W: 1.5e6);
        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1!.AvailableShaftPower_W, r2!.AvailableShaftPower_W);
        Assert.Equal(r1.MassFlowMargin, r2.MassFlowMargin);
        Assert.Equal(r1.PowerSufficient, r2.PowerSufficient);
    }
}
