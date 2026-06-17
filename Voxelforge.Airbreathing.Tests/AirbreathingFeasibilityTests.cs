// AirbreathingFeasibilityTests.cs — Sprint A5 acceptance for the
// ramjet feasibility gates.

using Voxelforge.Airbreathing;

namespace Voxelforge.Airbreathing.Tests;

public sealed class AirbreathingFeasibilityTests
{
    private static AirbreathingEngineDesign Design(double phi = 0.40)
        => new(
            Kind:                 AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:   0.10,
            CombustorArea_m2:     0.30,
            CombustorLength_m:    0.50,
            NozzleThroatArea_m2:  0.0848,
            NozzleExitArea_m2:    0.20,
            EquivalenceRatio:     phi);

    private static FlightConditions Cond(double mach = 2.0, double alt = 12_000.0)
        => new(alt, mach, AirbreathingFuel.H2);

    [Fact]
    public void NominalDesign_PassesAllGates()
    {
        var result = AirbreathingOptimization.GenerateWith(Design(), Cond());
        Assert.True(result.IsFeasible, $"Expected feasibility; got violations: {string.Join(", ", System.Linq.Enumerable.Select(result.Violations, v => v.ConstraintId))}");
    }

    [Fact]
    public void BelowLeanBlowout_FiresLeanBlowoutGate()
    {
        var design = Design(phi: 0.10);   // below LeanBlowoutPhi = 0.20
        var result = AirbreathingOptimization.GenerateWith(design, Cond());
        Assert.False(result.IsFeasible);
        Assert.Contains(result.Violations, v => v.ConstraintId == "COMBUSTOR_BLOWOUT_LEAN");
    }

    [Fact]
    public void AboveRichBlowout_FiresRichBlowoutGate()
    {
        var design = Design(phi: 2.0);    // above RichBlowoutPhi = 1.5
        var result = AirbreathingOptimization.GenerateWith(design, Cond());
        Assert.False(result.IsFeasible);
        Assert.Contains(result.Violations, v => v.ConstraintId == "COMBUSTOR_BLOWOUT_RICH");
    }

    [Fact]
    public void TT4AboveUncooledLimit_FiresTLimitGate()
    {
        // φ ≈ 0.85 with H2 at M=2 / 12 km drives T_t4 well above 2200 K.
        // Approximate: T_t4 ≈ T_t2 + φ·f_st·η_b·LHV/cp ≈ 390 + 0.85·0.0291·0.99·119.4e3/1004.7
        // ≈ 390 + 2913 ≈ 3303 K (rough). Well above the 2200 K ceiling.
        var design = Design(phi: 0.85);
        var result = AirbreathingOptimization.GenerateWith(design, Cond());
        Assert.Contains(result.Violations, v => v.ConstraintId == "T_T4_EXCEEDS_LIMIT");
    }

    [Fact]
    public void HighMach_DropsInletRecoveryButStaysAboveFloor()
    {
        // At M=4 the MIL-STD curve gives π_d ≈ 0.95 × (1 − 0.075 × 3^1.35)
        // = 0.95 × (1 − 0.337) ≈ 0.629 — above the 0.50 INLET_UNSTART
        // floor, so this gate should NOT fire.
        var result = AirbreathingOptimization.GenerateWith(Design(), Cond(mach: 4.0));
        Assert.DoesNotContain(result.Violations, v => v.ConstraintId == "INLET_UNSTART");
    }

    [Fact]
    public void VeryHighMach_FiresInletUnstart()
    {
        // At M=5 (curve domain edge): piDMax = 1 − 0.075 × 4^1.35 ≈ 1 − 0.480 = 0.52.
        // After ×0.95 mech: 0.494, below 0.50 floor.
        // Note: the cycle solver is NOT meaningful at M=5 (scramjet
        // sub-step territory) — but the gate machinery still
        // evaluates because we want INLET_UNSTART to fire BEFORE the
        // user notices the cycle output is junk.
        var result = AirbreathingOptimization.GenerateWith(Design(), Cond(mach: 5.0));
        Assert.Contains(result.Violations, v => v.ConstraintId == "INLET_UNSTART");
    }

    [Fact]
    public void Violations_CarryActualAndLimitNumericValues()
    {
        // Diagnostic data plumbing — UI / report layer reads
        // ActualValue + Limit to render the violation.
        var result = AirbreathingOptimization.GenerateWith(Design(phi: 0.10), Cond());
        var leanGate = System.Linq.Enumerable.Single(result.Violations, v => v.ConstraintId == "COMBUSTOR_BLOWOUT_LEAN");
        Assert.Equal(0.10, leanGate.ActualValue, 3);
        Assert.Equal(0.20, leanGate.Limit, 3);
    }
}
