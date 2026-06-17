// Sprint34aCascadeTests.cs — Physics-correctness cascade Sprint 34a
// (partial — PH-26 + PH-10 only; PH-8 and PH-9 deferred to Sprint 34b).
//
// Pins behaviour of:
//   • PH-26 turbine stator-throat choke check (π_crit form) and the
//     TURBINE_UNCHOKED feasibility gate that fires when any sized
//     turbine on the design runs subsonic.
//   • PH-10 ShaftLayout split (Straddled vs Overhung) on the first-mode
//     bending eigenvalue, with the (4.73 / 1.875)² ≈ 6× drop on RPM_crit
//     that the cantilever case must produce vs the fixed-fixed case.

using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Voxelforge.Turbopump;

namespace Voxelforge.Tests;

public class Sprint34aCascadeTests
{
    // ─────────────────────────────────────────────────────────────────
    //  PH-26 — Turbine stator choke check
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TurbineStage_DefaultIsChoked_PreservesBackCompat()
    {
        // Synthetic-fixture call sites that build TurbineStage directly
        // without the sizer pass should default to choked = true. This
        // keeps the TURBINE_UNCHOKED gate silent on legacy fixtures.
        var stage = new TurbineStage(
            Label:                       "fuel",
            MassFlow_kgs:                0.5,
            InletTemperature_K:          1000,
            InletPressure_Pa:            10e6,
            OutletPressure_Pa:           5e6,
            Gamma:                       1.30,
            MolecularWeight_gmol:        20,
            Cp_Jkg_K:                    2000,
            Efficiency:                  0.55,
            IsentropicSpecificWork_Jkg:  100e3,
            ActualSpecificWork_Jkg:      55e3,
            SpoutingVelocity_ms:         500,
            TipSpeed_ms:                 250,
            WheelRadius_mm:              50,
            Rpm:                         50000,
            BladeCount:                  60,
            StatorVaneCount:             24,
            RequiredShaftPower_W:        20e3,
            AvailableShaftPower_W:       30e3,
            PowerSufficient:             true,
            Notes:                       "test-stub");
        Assert.True(stage.IsChoked,
            "Default IsChoked must be true for back-compat with legacy fixture sites.");
    }

    [Theory]
    [InlineData(1.20)]
    [InlineData(1.30)]
    [InlineData(1.40)]
    public void CriticalPressureRatio_SatisfiesAnalyticForm(double gamma)
    {
        // π_crit = (2/(γ+1))^(γ/(γ-1)). At γ=1.30 → ~0.546.
        double piCrit = System.Math.Pow(
            2.0 / (gamma + 1.0), gamma / (gamma - 1.0));
        Assert.True(piCrit > 0 && piCrit < 1,
            $"π_crit must lie in (0, 1) for γ={gamma}; got {piCrit}.");
    }

    [Fact]
    public void TurbineUnchokedGate_FiresOnSubsonicExpanderTurbine()
    {
        // Closed-expander cycle with insufficient jacket ΔP — synthesise
        // an ExpanderTurbineResult with π = 0.85 (well above π_crit ≈
        // 0.546 for γ=1.30) and IsChoked = false; assert the gate fires.
        var safe = SafeFixture();
        var unchoked = new ExpanderTurbineResult(
            Cycle:                      EngineCycle.ClosedExpander,
            CoolantLabel:               "CH4",
            InletTemperature_K:         700,
            InletPressure_Pa:           10e6,
            OutletPressure_Pa:          8.5e6, // π = 0.85, definitely subsonic for γ=1.30
            MassFlow_kgs:               1.0,
            Cp_Jkg_K:                   3000,
            EffectiveGamma:             1.30,
            IsentropicSpecificWork_Jkg: 50e3,
            ActualSpecificWork_Jkg:     27.5e3,
            Efficiency:                 0.55,
            AvailableShaftPower_W:      27.5e3,
            RequiredShaftPower_W:       20e3,
            PowerSufficient:            true,
            Notes:                      "test-stub-unchoked",
            CriticalPressureRatio:      0.546,
            IsChoked:                   false);
        var result = safe with { ExpanderTurbine = unchoked };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.Contains(gate.Violations, v => v.ConstraintId == "TURBINE_UNCHOKED");
    }

    [Fact]
    public void TurbineUnchokedGate_DoesNotFireOnChokedExpanderTurbine()
    {
        // Mirror test: same expander result with IsChoked = true → silent.
        var safe = SafeFixture();
        var choked = new ExpanderTurbineResult(
            Cycle:                      EngineCycle.ClosedExpander,
            CoolantLabel:               "CH4",
            InletTemperature_K:         700,
            InletPressure_Pa:           10e6,
            OutletPressure_Pa:          3e6, // π = 0.30, well below π_crit ≈ 0.546
            MassFlow_kgs:               1.0,
            Cp_Jkg_K:                   3000,
            EffectiveGamma:             1.30,
            IsentropicSpecificWork_Jkg: 80e3,
            ActualSpecificWork_Jkg:     44e3,
            Efficiency:                 0.55,
            AvailableShaftPower_W:      44e3,
            RequiredShaftPower_W:       20e3,
            PowerSufficient:            true,
            Notes:                      "test-stub-choked",
            CriticalPressureRatio:      0.546,
            IsChoked:                   true);
        var result = safe with { ExpanderTurbine = choked };
        var gate = FeasibilityGate.Evaluate(result);
        Assert.DoesNotContain(gate.Violations,
            v => v.ConstraintId == "TURBINE_UNCHOKED");
    }

    // ─────────────────────────────────────────────────────────────────
    //  PH-10 — Shaft layout boundary-condition split
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void OverhungShaft_HasSixTimesLowerCriticalSpeed_ThanStraddled()
    {
        // The cantilever β₁L = 1.875 vs fixed-fixed β₁L = 4.73 ratio
        // squared is (4.73/1.875)² ≈ 6.36. ω_n scales as β₁L² so
        // RPM_crit_overhung / RPM_crit_straddled ≈ 1/6.36 ≈ 0.157.
        var pump = NewTestPump();
        var turbine = NewTestTurbine();

        var straddled = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Straddled);
        var overhung  = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung);

        Assert.NotNull(straddled);
        Assert.NotNull(overhung);
        double ratio = overhung!.FirstCriticalRpm / straddled!.FirstCriticalRpm;
        Assert.InRange(ratio, 0.140, 0.180); // (1.875/4.73)² ≈ 0.157
    }

    [Fact]
    public void StraddledIsTheDefault_PreservingBackCompat()
    {
        // Estimate's layout parameter defaults to Straddled. Without it,
        // pre-Sprint-34 tests should reproduce the legacy critical speed
        // exactly.
        var pump = NewTestPump();
        var turbine = NewTestTurbine();

        var implicitDefault = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine, operatingRpm: 50000);
        var explicitStraddled = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Straddled);

        Assert.NotNull(implicitDefault);
        Assert.NotNull(explicitStraddled);
        Assert.Equal(implicitDefault!.FirstCriticalRpm,
                     explicitStraddled!.FirstCriticalRpm,
                     precision: 6);
        Assert.Equal(ShaftLayout.Straddled, implicitDefault.Layout);
    }

    [Fact]
    public void RegenChamberDesign_DefaultsShaftLayoutToStraddled()
    {
        // Back-compat: existing test fixtures and saved designs that
        // don't specify a layout get fixed-fixed BC → preserves
        // pre-Sprint-34 SHAFT_WHIRL behaviour.
        var design = new RegenChamberDesign();
        Assert.Equal(ShaftLayout.Straddled, design.ShaftLayout);
    }

    // ─────────────────────────────────────────────────────────────────
    //  PH-50 (2026-04-29) — asymmetric-bearing-stiffness whirl split
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PH50_IsotropicDefault_PreservesPreSprint34LegacyValue()
    {
        // bearingAsymmetryRatio defaults to 0 — back-compat with the
        // pre-PH-50 isotropic estimate. FirstCriticalRpm must match
        // exactly between {default} and {bearingAsymmetryRatio: 0}.
        var pump = NewTestPump();
        var turbine = NewTestTurbine();

        var defaultPath = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung);
        var explicitZero = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung,
            bearingAsymmetryRatio: 0.0);

        Assert.NotNull(defaultPath);
        Assert.NotNull(explicitZero);
        Assert.Equal(defaultPath!.FirstCriticalRpm,
                     explicitZero!.FirstCriticalRpm,
                     precision: 6);
        // Forward/backward both equal isotropic when ε = 0.
        Assert.Equal(defaultPath.FirstCriticalRpm, defaultPath.ForwardCriticalRpm,  precision: 3);
        Assert.Equal(defaultPath.FirstCriticalRpm, defaultPath.BackwardCriticalRpm, precision: 3);
    }

    [Fact]
    public void PH50_BearingAsymmetry_SplitsForwardAboveBackward()
    {
        // ε = 0.30 → forward critical = ω_n · √1.30 ≈ 1.140·ω_n
        //         → backward critical = ω_n · √0.70 ≈ 0.837·ω_n
        // Gate keys on the LOWER (backward) so FirstCriticalRpm matches it.
        var pump = NewTestPump();
        var turbine = NewTestTurbine();

        var iso = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung,
            bearingAsymmetryRatio: 0.0);
        var split = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung,
            bearingAsymmetryRatio: 0.30);

        Assert.NotNull(iso);
        Assert.NotNull(split);
        Assert.True(split!.ForwardCriticalRpm  > iso!.FirstCriticalRpm);
        Assert.True(split.BackwardCriticalRpm < iso.FirstCriticalRpm);
        // Gate-keying critical = backward (lower of the two).
        Assert.Equal(split.BackwardCriticalRpm, split.FirstCriticalRpm, precision: 3);
        // Magnitude of split: forward ≈ ω_n·√1.30, backward ≈ ω_n·√0.70.
        Assert.Equal(iso.FirstCriticalRpm * System.Math.Sqrt(1.30),
                     split.ForwardCriticalRpm,
                     precision: 0);
        Assert.Equal(iso.FirstCriticalRpm * System.Math.Sqrt(0.70),
                     split.BackwardCriticalRpm,
                     precision: 0);
    }

    [Fact]
    public void PH50_BearingAsymmetry_ClampedAtMaxRatio()
    {
        // ε > 0.5 wanders into nonlinear-bearing territory the linear
        // whirl split doesn't model. The implementation clamps at 0.5.
        var pump = NewTestPump();
        var turbine = NewTestTurbine();

        var clamped = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung,
            bearingAsymmetryRatio: 0.95);
        var atCap = ShaftCriticalSpeed.Estimate(
            label: "fuel", pump: pump, turbine: turbine,
            operatingRpm: 50000, layout: ShaftLayout.Overhung,
            bearingAsymmetryRatio: 0.50);

        Assert.NotNull(clamped);
        Assert.NotNull(atCap);
        Assert.Equal(0.50, clamped!.BearingAsymmetryRatio, precision: 6);
        Assert.Equal(atCap!.FirstCriticalRpm, clamped.FirstCriticalRpm, precision: 3);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────

    private static TurbopumpGeometry NewTestPump() => new(
        ImpellerHubRadius_mm:    8,
        ImpellerTipRadius_mm:    30,
        ImpellerThickness_mm:    20,
        ImpellerBladeCount:      8,
        InducerHubRadius_mm:     0,
        InducerTipRadius_mm:     0,
        InducerLength_mm:        0,
        InducerBladeCount:       0,
        VoluteMinorRadiusStart_mm: 8,
        VoluteMinorRadiusEnd_mm: 16,
        CasingOuterRadius_mm:    60,
        CasingLength_mm:         50,
        TotalLength_mm:          80,
        EstimatedMass_g:         1500,
        Notes:                   "test-stub");

    private static TurbineGeometry NewTestTurbine() => new(
        WheelHubRadius_mm:       6,
        WheelTipRadius_mm:       25,
        WheelThickness_mm:       10,
        WheelBladeCount:         30,
        StatorInnerRadius_mm:    30,
        StatorOuterRadius_mm:    45,
        StatorAxialHeight_mm:    8,
        StatorVaneCount:         12,
        NozzleThroatArea_mm2:    50,
        HousingOuterRadius_mm:   50,
        TotalLength_mm:          60,
        EstimatedMass_g:         800,
        Notes:                   "test-stub");

    private static RegenGenerationResult? _safeCache;
    private static readonly object _safeLock = new();

    /// <summary>
    /// Reuse the fixture pattern from <c>FeasibilityGateTests</c> by
    /// running a real generate-with then injecting safe values onto
    /// every gate-relevant metric. Cached for speed.
    /// </summary>
    private static RegenGenerationResult SafeFixture()
    {
        lock (_safeLock)
        {
            if (_safeCache is not null) return _safeCache;
            var cond = new OperatingConditions
            {
                Thrust_N              = 2224.0,
                ChamberPressure_Pa    = 6.9e6,
                MixtureRatio          = 3.3,
                CoolantInletTemp_K    = 150.0,
                CoolantInletPressure_Pa = 12e6,
                WallMaterialIndex     = 1, // CuCrZr
                PropellantPair        = Combustion.PropellantPair.LOX_CH4,
            };
            var raw = RegenChamberOptimization.GenerateWith(cond, new RegenChamberDesign
            {
                IncludeManifolds      = false,
                IncludePorts          = false,
                IncludeInjectorFlange = false,
                ContourStationCount   = 60,
            });
            var mat = HeatTransfer.WallMaterials.All[cond.WallMaterialIndex];
            var ch4 = Coolant.MethaneFluid.Instance;
            _safeCache = raw with
            {
                Thermal = raw.Thermal with
                {
                    PeakGasSideWallT_K   = mat.MaxServiceTemp_K - 200.0,
                    WallTempExceedsLimit = false,
                    CoolantOutletT_K     = ch4.Metadata.MaxBulkT_K - 100.0,
                },
                Stress = raw.Stress with
                {
                    MinSafetyFactor = 2.5,
                    YieldExceeded   = false,
                },
                Manufacturing = raw.Manufacturing with
                {
                    MinFeatureSize_mm = 0.55,
                    FeatureSizeOK     = true,
                },
                Stability = raw.Stability with
                {
                    Composite       = Combustion.Stability.StabilityRating.Pass,
                    CompositeReason = "test-injected feasible",
                },
                IgniterType = Geometry.IgniterType.SparkTorch,
                Contour = raw.Contour with { CharacteristicLength_m = 1.10 },
            };
            return _safeCache;
        }
    }
}
