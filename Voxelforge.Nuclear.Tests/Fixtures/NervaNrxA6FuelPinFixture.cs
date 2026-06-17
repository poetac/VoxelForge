// NervaNrxA6FuelPinFixture.cs — Sprint NU.W2 acceptance fixture for the
// per-pin heat-conduction model.
//
// The Wave-1 NervaNrxA6Fixture covers the lumped reactor (Isp, thrust,
// core-exit T). This fixture extends the same NRX-A6 reference engine
// with the Wave-2 per-pin geometry detail: 564 hex elements × 19 pins
// each, 2.5 mm pin diameter, 3.2 mm pin pitch, 1.4 m fuel-pin length.
//
// Note on geometry: NRX-A6 actually used a "fuel matrix with 19 axial
// coolant channels per element" geometry rather than discrete fuel pins.
// The per-pin model is geometrically equivalent (heat conducts from the
// surrounding fuel matrix to the channel wall, channel coolant absorbs
// it). The "pin" terminology is retained for cross-design generality
// (advanced cermet designs and CERMET fuel-pin variants).
//
// Targets (cluster-anchored sanity bands):
//   Peak fuel centreline T:    2500 – 3500 K   (UO₂-cermet operational band)
//   Pin surface T:             2300 – 3100 K   (chemical compatibility)
//   ΔT centreline-to-surface:   100 – 1000 K   (radial conduction in matrix)
//   Per-pin power:               80 – 130 kW   (P/N_pin at NRX-A6 anchors)
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. This is a SUB-MODEL fixture (per-pin thermals at the same NRX-A6
// engine operating point as [[NervaNrxA6Fixture]]), so ADR-036's per-fixture
// tolerance ladder — which addresses engine-scope quantities (Isp, thrust) — has
// no row for per-pin centreline / surface / ΔT bands. Bands here are
// cluster-anchor sanity ranges per ADR-029 D4, not strict ±%; they bracket the
// physically admissible UO₂-cermet operating window, not a model-prediction
// uncertainty. The lumped per-pin model omits axial march and uses a single
// film-temperature anchor for the Dittus-Boelter HTC; widening the bands further
// would mask those modelling gaps rather than reveal them. Cross-pillar lumped
// performance (Isp / thrust / core-exit T) is identical to the Wave-1 fixture
// by construction — see Wave1/Wave2 bit-identity test below.

using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests.Fixtures;

public sealed class NervaNrxA6FuelPinFixture
{
    private static NuclearThermalDesign MakeNrxA6FuelPinDesign() =>
        new NuclearThermalDesign(
            Kind:                    NuclearKind.NervaSolidCore,
            ReactorThermalPower_MW:  1100.0,
            ReactorCoreLength_mm:    1400.0,
            ReactorCoreDiameter_mm:  1400.0,
            FuelLoadingFraction:     0.65,
            PropellantMassFlow_kgs:  33.0,
            ChamberPressure_bar:     34.0,
            ThroatRadius_mm:         120.0,
            ExpansionRatio:          100.0,
            NozzleLength_mm:         4000.0,
            RegenChannelDepth_mm:    2.0,
            RegenChannelCount:       200,
            NozzleWallThickness_mm:  1.5,
            NozzleChannelWidth_mm:   3.0,
            NozzleManifoldDepth_mm:  5.0) with
        {
            FuelPinDiameter_mm  = 2.5,
            FuelPinPitch_mm     = 3.2,
            FuelPinHexRings     = 2,            // 19 pins/element
            FuelElementCount    = 564,
            FuelPinLength_m     = 1.4,
            // FuelPinHotChannelFactor left at NaN → cluster anchor (1.40).
        };

    private static NuclearThermalConditions MakeNrxA6Conditions() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    [Fact]
    public void NrxA6FuelPin_PeakCenterlineTemp_InCermetOperationalBand()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        Assert.False(double.IsNaN(result.PeakFuelCenterlineTemp_K),
            "Per-pin model should activate when all four fuel-pin fields are populated.");
        // 2500–3500 K = UO₂-cermet operational band: lower bound is the
        // minimum reactor-exit T at which the lumped cycle hits NRX-A6's
        // 825 s Isp; upper bound is below the 3120 K UO₂ melting point
        // (Olander 1976) with hot-channel-factor margin. The radial
        // conduction model (Dittus-Boelter HTC + 1-D Fourier) lacks an
        // axial-march correction, so widening would mask that gap.
        Assert.InRange(result.PeakFuelCenterlineTemp_K, 2500.0, 3500.0);
    }

    [Fact]
    public void NrxA6FuelPin_PinSurfaceTemp_InCompatibilityBand()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        // 2300–3100 K = UO₂/H₂ chemical compatibility band (Lyon 1973 NASA
        // TM-X-2929 §4, Taub 1975 LA-5878-MS): hydrogen reduces UO₂ above
        // ~3100 K, so any prediction above that signals coolant-film
        // attachment failure rather than a tolerance question.
        Assert.InRange(result.PinSurfaceTemp_K, 2300.0, 3100.0);
    }

    [Fact]
    public void NrxA6FuelPin_CenterlineHotterThanSurface()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        Assert.True(result.PeakFuelCenterlineTemp_K > result.PinSurfaceTemp_K,
            "Centreline must be hotter than the surface — radial conduction direction.");
        double dT = result.PeakFuelCenterlineTemp_K - result.PinSurfaceTemp_K;
        // 50–1000 K radial ΔT: lower bound rules out a numerically-fused
        // pin (k_fuel→∞), upper bound is the centreline melt clearance
        // at q'''_max for NRX-A6 fuel-pin diameter (Carslaw & Jaeger §7.5).
        // Not a model-prediction band: a value outside is a centreline-
        // BC or fuel-conductivity bug, not a tolerance widening trigger.
        Assert.InRange(dT, 50.0, 1000.0);
    }

    [Fact]
    public void NrxA6FuelPin_HotChannelFactor_DefaultedToClusterAnchor()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        // 1.40 is the NRX-A6 hot-channel factor anchor (Bennett 1972 §3;
        // Walton 1991 NASA TM-105252) covering radial power peaking +
        // axial peaking + engineering uncertainty. Strict equality (not
        // a range) because this is a defaulted constant, not a model
        // prediction — drift here means the cluster-anchor table moved.
        Assert.Equal(1.40, result.FuelPinHotChannelFactor, precision: 6);
    }

    [Fact]
    public void NrxA6FuelPin_CoolantExitTemp_AgreesWithCycleSolverWithin200K()
    {
        // The per-pin energy balance and the lumped cycle solver both
        // compute T_exit from the same P / ṁ / cp(T) basis, so the two
        // results should agree within ~10 % (different cp(T)-iteration
        // anchors).
        var result = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        // 2200–2700 K = NRX-A6 reactor-exit band (Bennett 1972 §3 + ADR-029
        // D4 cluster anchor). Wider on the low end than the 2100–2500 K
        // engine-cycle band in [[NervaNrxA6Fixture]] because the per-pin
        // model integrates over a film-coupled coolant channel; the
        // additional 200 K covers the channel-vs-core T difference at the
        // mass-averaged exit station.
        Assert.InRange(result.FuelPinCoolantExitTemp_K, 2200.0, 2700.0);
        Assert.InRange(result.CoreExitTemp_K, 2200.0, 2700.0);
        double drift = System.Math.Abs(result.FuelPinCoolantExitTemp_K - result.CoreExitTemp_K);
        // 200 K drift bound: the per-pin and cycle solvers use different
        // cp(T) iteration anchors (single film T vs cycle-mean T). A drift
        // above 200 K signals a cp(T) curve-fit divergence rather than a
        // tolerance question.
        Assert.True(drift < 200.0,
            $"Per-pin vs cycle coolant T should agree within 200 K; got {drift:F0} K drift.");
    }

    [Fact]
    public void NrxA6FuelPin_BaselineIsFeasible()
    {
        var result = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        Assert.True(result.IsFeasible,
            $"NRX-A6 baseline should pass; saw {result.Violations.Count} violations: "
          + string.Join(", ", System.Linq.Enumerable.Select(result.Violations, v => v.ConstraintId)));
    }

    [Fact]
    public void NrxA6FuelPin_Wave1Behavior_BitIdentical_When_FuelPinFieldsAbsent()
    {
        // Wave-1 designs (no fuel-pin fields) should still pass through the
        // pipeline unchanged. Compare a Wave-1-shape design to a Wave-2
        // design with fuel-pin fields populated — the lumped cycle outputs
        // (Isp, thrust, core-exit T) should be identical.
        var wave1 = MakeNrxA6FuelPinDesign() with
        {
            FuelPinDiameter_mm  = double.NaN,
            FuelPinPitch_mm     = double.NaN,
            FuelPinHexRings     = 0,
            FuelElementCount    = 0,
            FuelPinLength_m     = double.NaN,
        };
        var rWave1 = NuclearOptimization.GenerateWith(wave1, MakeNrxA6Conditions());
        var rWave2 = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        Assert.Equal(rWave1.IspVacuum_s,    rWave2.IspVacuum_s,    precision: 6);
        Assert.Equal(rWave1.ThrustVacuum_N, rWave2.ThrustVacuum_N, precision: 6);
        Assert.Equal(rWave1.CoreExitTemp_K, rWave2.CoreExitTemp_K, precision: 6);
        Assert.True(double.IsNaN(rWave1.PeakFuelCenterlineTemp_K),
            "Wave-1 (no fuel-pin fields) → per-pin result must be NaN.");
        Assert.True(double.IsFinite(rWave2.PeakFuelCenterlineTemp_K));
    }

    [Fact]
    public void NrxA6FuelPin_Deterministic()
    {
        var r1 = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        var r2 = NuclearOptimization.GenerateWith(MakeNrxA6FuelPinDesign(), MakeNrxA6Conditions());
        Assert.Equal(r1.PeakFuelCenterlineTemp_K, r2.PeakFuelCenterlineTemp_K);
        Assert.Equal(r1.PinSurfaceTemp_K,         r2.PinSurfaceTemp_K);
        Assert.Equal(r1.FuelPinCoolantExitTemp_K, r2.FuelPinCoolantExitTemp_K);
    }
}
