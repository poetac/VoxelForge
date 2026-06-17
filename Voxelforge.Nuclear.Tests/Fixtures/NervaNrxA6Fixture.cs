// NervaNrxA6Fixture.cs — NERVA NRX-A6 published-engine validation fixture.
//
// Ground-truth data:
//   Reactor thermal power:  1100 MW
//   LH2 mass flow:          33.0 kg/s
//   Chamber pressure:       34.0 bar
//   Expansion ratio:        100.0
//   Expected Isp (vacuum):  825 s ± 5 %   (pass band: 784 – 866 s)
//   Expected thrust (vac):  267 kN ± 5 %  (pass band: 254 – 280 kN)
//   Expected core-exit T:   2100 – 2500 K  (sanity band)
//
// References:
//   Borowski, S. K. et al. (2012). "Nuclear Thermal Propulsion (NTP)." AIAA-2012-3889.
//   Bennett, R. G. (1972). "NERVA Program Summary." AIAA-72-1161.
//   NERVA NRX-A6 ground test, Jackass Flats NV, 1969.
//   Illes & Ohler (1998). Frozen-flow efficiency for H2 at ε~100.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. Calibrated NTR variant under ADR-036 § Nuclear pillar (±5 % Isp,
// ±5 % thrust). The lumped 0-D solver is calibrated to ±3 % vs the published
// NRX-A6 ground-test data (Bennett 1972); the ±5 % band provides a comfortable
// working margin while still being the tightest in the cross-pillar portfolio,
// consistent with ADR-036's "single-fixture, closed-form analytical, calibrated
// to one anchor" justification.

using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests.Fixtures;

public sealed class NervaNrxA6Fixture
{
    // NRX-A6 published performance — Bennett (1972) AIAA-72-1161 Table 2.
    private const double ExpectedIsp_s    = 825.0;
    private const double ExpectedThrust_N = 267_000.0;  // 267 kN
    // ±5 % covers both Isp and thrust. Closed-form hot-H₂ impulse (Isp = √(2·ΔH))
    // is exact to within the cp(T) curve fit (Illes & Ohler 1998 frozen-flow η at
    // ε≈100); thrust = ṁ·g₀·Isp inherits that band. Tighter than the rocket
    // pillar's gas-generator ±15 % because there is no cycle-power balance to
    // model — heat flux into the propellant is the reactor-side input.
    private const double Tolerance        = 0.05;        // ±5 %

    // Core-exit temperature sanity band — UO₂-cermet fuel operates 2100–2500 K
    // (Bennett 1972 §3; ADR-029 D4 cluster anchor). Not a strict ±%: the lumped
    // 0-D solver chooses T_exit from cycle energy balance, so any value outside
    // this band signals a cp(T) curve-fit drift, not a tolerance question.
    private const double CoreExitTempMin_K = 2100.0;
    private const double CoreExitTempMax_K = 2500.0;

    private static NuclearThermalDesign MakeNrxA6Design() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,      // NERVA NRX-A6 core ≈ 1.4 m
        ReactorCoreDiameter_mm:  1400.0,      // NERVA NRX-A6 core ≈ 1.4 m diameter
        FuelLoadingFraction:     0.65,         // UO2-cermet volume fraction
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,        // ~A* ≈ 0.0452 m² for NRX-A6 at 33 kg/s
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,       // estimated for ε=100, R_t=120 mm, 15° half-angle
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions MakeNrxA6Conditions() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    [Fact]
    public void NrxA6_IspVacuum_WithinFivePercent()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6Design(), MakeNrxA6Conditions());
        Assert.InRange(result.IspVacuum_s,
            ExpectedIsp_s * (1.0 - Tolerance),
            ExpectedIsp_s * (1.0 + Tolerance));
    }

    [Fact]
    public void NrxA6_ThrustVacuum_WithinFivePercent()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6Design(), MakeNrxA6Conditions());
        Assert.InRange(result.ThrustVacuum_N,
            ExpectedThrust_N * (1.0 - Tolerance),
            ExpectedThrust_N * (1.0 + Tolerance));
    }

    [Fact]
    public void NrxA6_CoreExitTemp_InReasonableBand()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6Design(), MakeNrxA6Conditions());
        Assert.InRange(result.CoreExitTemp_K, CoreExitTempMin_K, CoreExitTempMax_K);
    }

    [Fact]
    public void NrxA6_IsFeasible()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6Design(), MakeNrxA6Conditions());
        Assert.True(result.IsFeasible,
            $"Expected feasible. Violations: {string.Join(", ", result.Violations)}");
    }

    [Fact]
    public void NrxA6_IsDeterministic()
    {
        var design = MakeNrxA6Design();
        var cond   = MakeNrxA6Conditions();
        var r1     = NuclearOptimization.GenerateWith(design, cond);
        var r2     = NuclearOptimization.GenerateWith(design, cond);
        Assert.Equal(r1.IspVacuum_s,    r2.IspVacuum_s);
        Assert.Equal(r1.ThrustVacuum_N, r2.ThrustVacuum_N);
        Assert.Equal(r1.CoreExitTemp_K, r2.CoreExitTemp_K);
    }
}
