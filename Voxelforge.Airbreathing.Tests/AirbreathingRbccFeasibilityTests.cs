// AirbreathingRbccFeasibilityTests.cs — Sprint A11 feasibility gate tests
// for RBCC operating-mode envelope checks.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingRbccFeasibilityTests
{
    // ── Helpers ────────────────────────────────────────────────────────────

    private static AirbreathingEngineDesign RbccDesign(
        RbccOperatingMode mode,
        double phi = 0.50)
        => new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Rbcc,
            InletThroatArea_m2:      0.10,
            CombustorArea_m2:        0.30,
            CombustorLength_m:       0.50,
            NozzleThroatArea_m2:     0.085,
            NozzleExitArea_m2:       0.20,
            EquivalenceRatio:        phi,
            IsolatorLength_m:        0.80,
            RbccMode:                mode,
            EjectorEntrainmentRatio: 1.5);

    // ── In-envelope gates pass ────────────────────────────────────────────

    [Fact]
    public void RamjetMode_NominalDesign_PassesAllGates()
    {
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.Ramjet),
            new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2));

        Assert.True(result.IsFeasible,
            $"Expected feasible; violations: {string.Join(", ", System.Linq.Enumerable.Select(result.Violations, v => v.ConstraintId))}");
    }

    [Fact]
    public void ScramjetMode_NominalDesign_PassesAllGates()
    {
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.Scramjet, phi: 0.40),
            new FlightConditions(25_000.0, 7.0, AirbreathingFuel.H2));

        // Scramjet mode at M=7 is inside the envelope; only scramjet-specific
        // physics gates (ISOLATOR_UNSTART etc.) could fire here.
        // The RBCC_MODE_OUT_OF_ENVELOPE gate should NOT fire.
        bool rbccGateFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "RBCC_MODE_OUT_OF_ENVELOPE");
        Assert.False(rbccGateFired, "RBCC_MODE_OUT_OF_ENVELOPE must not fire at M=7 scramjet mode.");
    }

    [Fact]
    public void DuctedRocket_AtMach0p5_ModeGatePasses()
    {
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.DuctedRocket),
            new FlightConditions(0.0, 0.5, AirbreathingFuel.H2));

        bool gateFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "RBCC_MODE_OUT_OF_ENVELOPE");
        Assert.False(gateFired,
            "RBCC_MODE_OUT_OF_ENVELOPE must not fire at M=0.5 ducted-rocket mode (within ceiling).");
    }

    [Fact]
    public void DuctedRocket_AtMach2p5_ModeGatePasses_InclusiveBound()
    {
        // M = 2.5 is exactly the ceiling; gate should NOT fire (strict >).
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.DuctedRocket),
            new FlightConditions(12_000.0, 2.5, AirbreathingFuel.H2));

        bool gateFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "RBCC_MODE_OUT_OF_ENVELOPE");
        Assert.False(gateFired,
            "RBCC_MODE_OUT_OF_ENVELOPE must not fire exactly at M=2.5 ceiling (bound is exclusive).");
    }

    [Fact]
    public void Scramjet_AtMach4_ModeGatePasses_InclusiveBound()
    {
        // M = 4.0 is exactly the scramjet floor; gate should NOT fire (strict <).
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.Scramjet, phi: 0.35),
            new FlightConditions(20_000.0, 4.0, AirbreathingFuel.H2));

        bool gateFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "RBCC_MODE_OUT_OF_ENVELOPE");
        Assert.False(gateFired,
            "RBCC_MODE_OUT_OF_ENVELOPE must not fire exactly at M=4.0 floor (bound is exclusive).");
    }

    // ── Out-of-envelope gates fire ────────────────────────────────────────

    [Fact]
    public void DuctedRocket_AboveMach2p5_FiresModeOutOfEnvelope()
    {
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.DuctedRocket),
            new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2));

        bool gateFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "RBCC_MODE_OUT_OF_ENVELOPE");
        Assert.True(gateFired,
            "RBCC_MODE_OUT_OF_ENVELOPE must fire when DuctedRocket mode is used above M=2.5.");
        Assert.False(result.IsFeasible, "Design must be infeasible when mode gate fires.");
    }

    [Fact]
    public void Scramjet_BelowMach4_FiresModeOutOfEnvelope()
    {
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.Scramjet, phi: 0.40),
            new FlightConditions(12_000.0, 2.0, AirbreathingFuel.H2));

        bool gateFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "RBCC_MODE_OUT_OF_ENVELOPE");
        Assert.True(gateFired,
            "RBCC_MODE_OUT_OF_ENVELOPE must fire when Scramjet mode is used below M=4.0.");
        Assert.False(result.IsFeasible, "Design must be infeasible when mode gate fires.");
    }

    // ── Inherited common gates still fire ─────────────────────────────────

    [Fact]
    public void RamjetMode_LeanEquivalenceRatio_FiresBlowoutGate()
    {
        var result = AirbreathingOptimization.GenerateWith(
            RbccDesign(RbccOperatingMode.Ramjet, phi: 0.05),   // way below lean-blowout floor
            new FlightConditions(15_000.0, 3.5, AirbreathingFuel.H2));

        bool blowoutFired = System.Linq.Enumerable.Any(
            result.Violations, v => v.ConstraintId == "COMBUSTOR_BLOWOUT_LEAN");
        Assert.True(blowoutFired,
            "COMBUSTOR_BLOWOUT_LEAN must fire for φ = 0.05 (below lean-blowout floor).");
    }
}
