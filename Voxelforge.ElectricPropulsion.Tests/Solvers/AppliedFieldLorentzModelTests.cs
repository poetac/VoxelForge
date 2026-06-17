// AppliedFieldLorentzModelTests.cs — Sprint EP.W3.AF unit tests for the
// applied-field extension to SelfFieldLorentzModel.
//
// Pins:
//   • B = 0 produces bit-identical Wave-2 self-field output.
//   • B > floor activates the additive Sankaran-2004 fit.
//   • k_af override threads through correctly.
//   • Negative B / non-positive k_af raise ArgumentOutOfRangeException.
//   • Linear scaling of T_af in J, B, r_a.

using Voxelforge.ElectricPropulsion.Solvers;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class AppliedFieldLorentzModelTests
{
    private const double J = 1500.0;     // arc current [A]
    private const double mDot = 4.0e-5;  // 40 mg/s
    private const double rC = 6.0;       // cathode radius [mm]
    private const double rA = 50.0;      // anode radius [mm]
    private const double L = 100.0;      // chamber length [mm]

    [Fact]
    public void ZeroBApplied_ProducesBitIdenticalWave2Output()
    {
        var wave2 = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L);
        var wave3 = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.0);

        Assert.Equal(wave2.Thrust_N,                 wave3.Thrust_N);
        Assert.Equal(wave2.IspVacuum_s,              wave3.IspVacuum_s);
        Assert.Equal(wave2.ThrustEfficiency_Maecker, wave3.ThrustEfficiency_Maecker);
        Assert.Equal(0.0, wave3.AppliedFieldThrust_N);
        Assert.Equal(0.0, wave3.AppliedFieldStrength_T);
    }

    [Fact]
    public void NumericallyTinyBApplied_TreatedAsZero()
    {
        // Below AppliedFieldNumericFloor_T = 1e-6 the augmentation is
        // suppressed to avoid round-trip noise.
        var result = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: 1e-9);
        Assert.Equal(0.0, result.AppliedFieldThrust_N);
        Assert.Equal(0.0, result.AppliedFieldStrength_T);
    }

    [Fact]
    public void AppliedField_AddsSankaranFitToThrust()
    {
        // T_af = k_af · J · B · r_a (with default k_af = 0.20).
        double B = 0.15;
        var result = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: B);

        double expectedTaf = SelfFieldLorentzModel.DefaultAppliedFieldCoupling
                           * J * B * (rA * 1e-3);
        Assert.Equal(expectedTaf, result.AppliedFieldThrust_N, precision: 6);
        Assert.Equal(result.SelfFieldThrust_N + result.AppliedFieldThrust_N,
                     result.Thrust_N, precision: 9);
    }

    [Fact]
    public void AppliedFieldCouplingOverride_ThreadsThrough()
    {
        double B = 0.15;
        double k_af = 0.10;  // Polk-LiLFA calibration
        var result = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: B,
            appliedFieldCoupling:   k_af);
        double expectedTaf = k_af * J * B * (rA * 1e-3);
        Assert.Equal(expectedTaf, result.AppliedFieldThrust_N, precision: 6);
    }

    [Fact]
    public void AppliedFieldThrust_LinearInArcCurrent()
    {
        var r1 = SelfFieldLorentzModel.Solve(1000, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.10);
        var r2 = SelfFieldLorentzModel.Solve(2000, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.10);
        // T_af ∝ J: 2× current → 2× T_af.
        Assert.Equal(2.0 * r1.AppliedFieldThrust_N, r2.AppliedFieldThrust_N, precision: 6);
    }

    [Fact]
    public void AppliedFieldThrust_LinearInBField()
    {
        var r1 = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.10);
        var r2 = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.20);
        Assert.Equal(2.0 * r1.AppliedFieldThrust_N, r2.AppliedFieldThrust_N, precision: 6);
    }

    [Fact]
    public void AppliedFieldThrust_LinearInAnodeRadius()
    {
        var r1 = SelfFieldLorentzModel.Solve(J, mDot, rC,  50, L,
            appliedFieldStrength_T: 0.10);
        var r2 = SelfFieldLorentzModel.Solve(J, mDot, rC, 100, L,
            appliedFieldStrength_T: 0.10);
        // T_af ∝ r_a: 2× r_a → 2× T_af (also 2× ln-coefficient on T_self
        // but here we isolate T_af).
        Assert.Equal(2.0 * r1.AppliedFieldThrust_N, r2.AppliedFieldThrust_N, precision: 6);
    }

    [Fact]
    public void NegativeBApplied_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
                appliedFieldStrength_T: -0.10));
    }

    [Fact]
    public void NonPositiveCouplingOverride_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
                appliedFieldStrength_T: 0.10,
                appliedFieldCoupling:   0.0));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
                appliedFieldStrength_T: 0.10,
                appliedFieldCoupling:   -0.30));
    }

    [Fact]
    public void NaNCouplingOverride_UsesDefault()
    {
        // NaN-as-sentinel must be honoured — the design-record default for
        // MpdAppliedFieldCouplingOverride is NaN and the solver should
        // fall back to DefaultAppliedFieldCoupling.
        double B = 0.15;
        var rNaN = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: B,
            appliedFieldCoupling:   double.NaN);
        var rExplicit = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: B,
            appliedFieldCoupling:   SelfFieldLorentzModel.DefaultAppliedFieldCoupling);
        Assert.Equal(rExplicit.AppliedFieldThrust_N, rNaN.AppliedFieldThrust_N, precision: 9);
    }

    [Fact]
    public void TotalThrust_ExceedsSelfFieldComponent_WhenAppliedFieldOn()
    {
        var result = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.15);
        Assert.True(result.Thrust_N > result.SelfFieldThrust_N,
            "Total thrust must exceed self-field component when B > 0.");
        Assert.True(result.AppliedFieldThrust_N > 0,
            "T_af must be positive when B > 0.");
    }

    [Fact]
    public void IspScales_WithTotalThrust_NotJustSelfField()
    {
        var selfOnly = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L);
        var augmented = SelfFieldLorentzModel.Solve(J, mDot, rC, rA, L,
            appliedFieldStrength_T: 0.15);
        Assert.True(augmented.IspVacuum_s > selfOnly.IspVacuum_s,
            "Applied-field augmentation must lift Isp above the self-field-only baseline.");
        // Exact ratio: Isp_aug / Isp_self = T_total / T_self.
        double expectedRatio = augmented.Thrust_N / selfOnly.Thrust_N;
        double actualRatio   = augmented.IspVacuum_s / selfOnly.IspVacuum_s;
        Assert.Equal(expectedRatio, actualRatio, precision: 6);
    }
}
