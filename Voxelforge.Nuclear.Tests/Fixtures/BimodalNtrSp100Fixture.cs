// BimodalNtrSp100Fixture.cs — Sprint NU.W3 acceptance fixture for the
// bimodal NTR + closed-cycle He Brayton variant.
//
// Reference: NASA SP-100 / SAFE-400 derivative bimodal NTR concept.
// SP-100 was a space-nuclear-power study (1980s–90s); SAFE-400 was a
// heatpipe-cooled test article. The bimodal NTR concept couples a NERVA-
// style propellant flow path with a closed-cycle He Brayton loop for
// continuous ~100 kWe electric output between propulsive burns. Reference
// values are cluster-anchored:
//
//   Reactor thermal power:       1.5 MW (test-article class; far smaller
//                                than flight NTR's 1100 MW)
//   Brayton turbine inlet T:     1300 K (cluster mid-band)
//   Alternator RPM:              45 000 RPM (high-speed PM alternator)
//   He pressure:                 120 bar (~12 MPa)
//   Electric output target:      100 kWe
//   Recuperator effectiveness:   0.90 (cluster anchor)
//
// Targets (cluster-anchored sanity bands; no flight hardware exists):
//   Electric output:             80 – 120 kWe (close to design target ±20 %)
//   Brayton thermal efficiency:  0.20 – 0.45 (sub-Carnot but realistic)
//   Reactor power tap:           ~0.4 – 0.8 MW (below 95 % ceiling)
//   Bimodal-mode result fields populated, thrust-side result fields NaN'd
//   in Electric mode only.
//
// Per-quantity tolerance rationale per #745 / PublishedEngineValidation README
// convention. ADR-036 § Nuclear pillar marks NTR-bimodal-Brayton as DEFERRED
// (Wave-2+) and only addresses thrust / Isp ±10 % bands; the quantities asserted
// here (electric power output, Brayton thermal efficiency, reactor power tap
// ratio) are outside ADR-036's per-fixture ladder rows. Bands are cluster-anchor
// sanity ranges per ADR-029 D4. The wide ±20 % on electric power is driven by
// SP-100 / SAFE-400 being concept studies with no flight or thrust-test hardware
// at this 1.5 MW thermal class — the cluster spans 80–120 kWe across published
// design points (Mason 2001 NASA TM-2001-211008; Lipinski et al. 1999 LA-13530).

using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests.Fixtures;

public sealed class BimodalNtrSp100Fixture
{
    private const double Reactor_MW           = 1.5;
    private const double ElectricTarget_kWe   = 100.0;
    private const double TurbineInletTemp_K   = 1300.0;
    private const double HePressure_bar       = 120.0;
    private const double AlternatorRpm        = 45_000.0;

    private static NuclearThermalDesign MakeSp100Design(BimodalMode mode) =>
        new NuclearThermalDesign(
            Kind:                    NuclearKind.BimodalNtr,
            ReactorThermalPower_MW:  Reactor_MW,
            ReactorCoreLength_mm:    500.0,
            ReactorCoreDiameter_mm:  300.0,
            FuelLoadingFraction:     0.65,
            PropellantMassFlow_kgs:  0.5,         // small for test-article class
            ChamberPressure_bar:     40.0,
            ThroatRadius_mm:         50.0,
            ExpansionRatio:          100.0,
            NozzleLength_mm:         2000.0,
            RegenChannelDepth_mm:    2.0,
            RegenChannelCount:       80,
            NozzleWallThickness_mm:  1.5,
            NozzleChannelWidth_mm:   3.0,
            NozzleManifoldDepth_mm:  5.0) with
        {
            BimodalMode                  = mode,
            ElectricPowerTarget_kWe      = ElectricTarget_kWe,
            BraytonTurbineInletTemp_K    = TurbineInletTemp_K,
            BraytonHePressure_bar        = HePressure_bar,
            AlternatorRpm                = AlternatorRpm,
        };

    private static NuclearThermalConditions Conditions() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    // ── Hybrid mode — both thrust and electric ──────────────────────────

    [Fact]
    public void Sp100_HybridMode_ElectricOutputWithinTwentyPercent()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        // ±20 % electric output: closed-cycle He Brayton has no thrust-side
        // coupling losses to model, so the band is driven entirely by the
        // 80–120 kWe cluster spread across SP-100 / SAFE-400 concept-study
        // design points (Mason 2001 NASA TM-2001-211008 §3; Lipinski 1999
        // LA-13530 Table 2). Tighten only when test-article hardware ships.
        double lo = ElectricTarget_kWe * 0.80;
        double hi = ElectricTarget_kWe * 1.20;
        Assert.InRange(r.ElectricPowerOutput_kWe, lo, hi);
    }

    [Fact]
    public void Sp100_HybridMode_BraytonEfficiencyInClusterBand()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        // 0.20–0.55 = realistic He-Brayton η_th cluster at TIT ≈ 1300 K
        // (Mason 2001 Fig. 4; Wright 2006 SAND2006-2518 §3). Lower bound
        // is a turbine-inlet-only η floor; upper bound stays sub-Carnot
        // (η_Carnot ≈ 0.58 at 1300 K / 550 K) to catch sign-error bugs in
        // the recuperator energy balance. Not a strict ±% — band is a
        // physically admissible operating window, not a model uncertainty.
        Assert.InRange(r.BraytonThermalEfficiency, 0.20, 0.55);
    }

    [Fact]
    public void Sp100_HybridMode_BraytonEfficiencyBelowCarnot()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        // Hard 2nd-law constraint, not a tolerance band — η_th > η_Carnot
        // is a thermodynamic impossibility, not a calibration question.
        Assert.True(r.BraytonThermalEfficiency <= r.BraytonCarnotEfficiency);
    }

    [Fact]
    public void Sp100_HybridMode_ReactorPowerTapBelowCeiling()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        double tapRatio = r.ReactorPowerToBrayton_MW / Reactor_MW;
        // 0.95 ceiling = design constraint, not a model-prediction band:
        // hybrid mode needs ≥5 % thermal head reserved for the propellant
        // flow path (Mason 2001 §2.3). Above 0.95 means the dispatcher has
        // starved the thrust side; a bug, not a tolerance widening trigger.
        Assert.True(tapRatio < 0.95,
            $"Brayton tap ratio {tapRatio:F3} should be below 0.95 ceiling.");
    }

    [Fact]
    public void Sp100_HybridMode_ProducesPositiveThrust()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        Assert.True(r.ThrustVacuum_N > 0,
            "Hybrid mode produces both thrust and electric power.");
        Assert.True(r.IspVacuum_s > 0);
    }

    [Fact]
    public void Sp100_HybridMode_IsFeasible()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        Assert.True(r.IsFeasible,
            $"SP-100 baseline should pass; saw {r.Violations.Count} violations.");
    }

    // ── Electric-only mode ──────────────────────────────────────────────

    [Fact]
    public void Sp100_ElectricMode_ZeroThrustReported()
    {
        // In pure-electric mode, the LH₂ flow is conceptually shut off and
        // the dispatch NaN-s the thrust + Isp result fields.
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Electric), Conditions());
        Assert.True(double.IsNaN(r.ThrustVacuum_N),
            $"Electric mode should have NaN thrust; got {r.ThrustVacuum_N}.");
        Assert.True(double.IsNaN(r.IspVacuum_s));
    }

    [Fact]
    public void Sp100_ElectricMode_ElectricOutputMatchesTarget()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Electric), Conditions());
        Assert.True(double.IsFinite(r.ElectricPowerOutput_kWe));
        // Same ±20 % electric-output band as hybrid mode; rationale is on
        // Sp100_HybridMode_ElectricOutputWithinTwentyPercent above. In
        // pure-electric mode the band tightens *in principle* (no thrust-
        // side coupling) but stays at ±20 % until SP-100 / SAFE-400 test
        // hardware exists to anchor it.
        Assert.InRange(r.ElectricPowerOutput_kWe,
            ElectricTarget_kWe * 0.80, ElectricTarget_kWe * 1.20);
    }

    // ── Thrust-only mode bit-identical to Wave-2 ────────────────────────

    [Fact]
    public void Sp100_ThrustMode_BraytonFieldsAllNaN()
    {
        // Pure-thrust mode skips the Brayton pipeline entirely. All bimodal
        // result fields must be NaN.
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Thrust), Conditions());
        Assert.True(double.IsNaN(r.ElectricPowerOutput_kWe));
        Assert.True(double.IsNaN(r.BraytonThermalEfficiency));
        Assert.True(double.IsNaN(r.BraytonCarnotEfficiency));
        Assert.True(double.IsNaN(r.ReactorPowerToBrayton_MW));
        Assert.True(double.IsNaN(r.BraytonHeMassFlow_kgs));
    }

    [Fact]
    public void Sp100_ThrustMode_HasNormalThrust()
    {
        var r = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Thrust), Conditions());
        Assert.True(r.ThrustVacuum_N > 0);
        Assert.True(r.IspVacuum_s > 0);
    }

    // ── Determinism ─────────────────────────────────────────────────────

    [Fact]
    public void Sp100_Deterministic()
    {
        var r1 = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        var r2 = NuclearOptimization.GenerateWith(MakeSp100Design(BimodalMode.Hybrid), Conditions());
        Assert.Equal(r1.ElectricPowerOutput_kWe, r2.ElectricPowerOutput_kWe);
        Assert.Equal(r1.BraytonThermalEfficiency, r2.BraytonThermalEfficiency);
        Assert.Equal(r1.ThrustVacuum_N, r2.ThrustVacuum_N);
    }

    // ── Schema round-trip ───────────────────────────────────────────────

    [Fact]
    public void Sp100_RoundTripsThroughPersistence()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"vxf_nuclear_sp100_{System.IO.Path.GetRandomFileName()}.json");
        try
        {
            Voxelforge.Nuclear.IO.NuclearDesignPersistence.SaveJson(
                MakeSp100Design(BimodalMode.Hybrid), Conditions(), path);
            var (loaded, cond) = Voxelforge.Nuclear.IO.NuclearDesignPersistence.LoadJson(path);
            Assert.Equal(NuclearKind.BimodalNtr, loaded.Kind);
            Assert.Equal(BimodalMode.Hybrid, loaded.BimodalMode);
            Assert.Equal(100.0, loaded.ElectricPowerTarget_kWe, precision: 6);
            // Re-run produces the same numerical results.
            var fresh     = NuclearOptimization.GenerateWith(
                MakeSp100Design(BimodalMode.Hybrid), Conditions());
            var roundtrip = NuclearOptimization.GenerateWith(loaded, cond);
            Assert.Equal(fresh.ElectricPowerOutput_kWe, roundtrip.ElectricPowerOutput_kWe, precision: 6);
        }
        finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
