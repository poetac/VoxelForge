// NtrGenerationResultTests.cs — direct ctor + property tests for the
// NtrGenerationResult record. Per audit 05-test-gaps.md §5 the type was
// previously exercised only transitively through NuclearOptimization.

using System.Collections.Generic;
using Voxelforge.Nuclear;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NtrGenerationResultTests
{
    private static NuclearThermalDesign MakeNrxA6() => new(
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
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions MakeCond()
        => new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    private static NtrGenerationResult MakeMinimalFeasible() => new(
        Design:                            MakeNrxA6(),
        Conditions:                        MakeCond(),
        CoreExitTemp_K:                    2350.0,
        GammaEff:                          1.40,
        CStar_ms:                          2400.0,
        IspVacuum_s:                       825.0,
        ThrustVacuum_N:                    267_000.0,
        VolumetricHeatFlux_MWm3:           510.0,
        KEff:                              1.02,
        RegenNozzleWallTempExceedsLimit:   false,
        Violations:                        new List<FeasibilityViolation>(),
        Advisories:                        new List<FeasibilityViolation>(),
        IsFeasible:                        true);

    // ── Ctor / positional fields ─────────────────────────────────────────

    [Fact]
    public void Ctor_StoresAllPositionalFields()
    {
        var r = MakeMinimalFeasible();
        Assert.Equal(NuclearKind.NervaSolidCore, r.Design.Kind);
        Assert.Equal(80.0,         r.Conditions.PropellantInletTemp_K);
        Assert.Equal(2350.0,       r.CoreExitTemp_K);
        Assert.Equal(1.40,         r.GammaEff);
        Assert.Equal(2400.0,       r.CStar_ms);
        Assert.Equal(825.0,        r.IspVacuum_s);
        Assert.Equal(267_000.0,    r.ThrustVacuum_N);
        Assert.Equal(510.0,        r.VolumetricHeatFlux_MWm3);
        Assert.Equal(1.02,         r.KEff);
        Assert.False(r.RegenNozzleWallTempExceedsLimit);
        Assert.Empty(r.Violations);
        Assert.Empty(r.Advisories);
        Assert.True(r.IsFeasible);
    }

    // ── Init-only Wave-2 fuel-pin fields ────────────────────────────────

    [Fact]
    public void InitOnly_FuelPinFields_DefaultToNaN()
    {
        // NU.W2 per-pin fields are NaN-by-default; lumped-only Wave-1
        // results leave them at NaN.
        var r = MakeMinimalFeasible();
        Assert.True(double.IsNaN(r.PeakFuelCenterlineTemp_K));
        Assert.True(double.IsNaN(r.PinSurfaceTemp_K));
        Assert.True(double.IsNaN(r.FuelPinHotChannelFactor));
        Assert.True(double.IsNaN(r.FuelPinCoolantExitTemp_K));
    }

    [Fact]
    public void WithExpression_OverridesFuelPinFields()
    {
        var r = MakeMinimalFeasible() with
        {
            PeakFuelCenterlineTemp_K = 2750.0,
            PinSurfaceTemp_K         = 2400.0,
            FuelPinHotChannelFactor  = 1.40,
            FuelPinCoolantExitTemp_K = 2200.0,
        };
        Assert.Equal(2750.0, r.PeakFuelCenterlineTemp_K);
        Assert.Equal(2400.0, r.PinSurfaceTemp_K);
        Assert.Equal(1.40,   r.FuelPinHotChannelFactor);
        Assert.Equal(2200.0, r.FuelPinCoolantExitTemp_K);
    }

    // ── Init-only Wave-3 bimodal fields ─────────────────────────────────

    [Fact]
    public void InitOnly_BimodalFields_DefaultToNaN()
    {
        // NU.W3 bimodal fields are NaN-by-default; NervaSolidCore /
        // BimodalMode.Thrust results leave them at NaN.
        var r = MakeMinimalFeasible();
        Assert.True(double.IsNaN(r.ElectricPowerOutput_kWe));
        Assert.True(double.IsNaN(r.BraytonThermalEfficiency));
        Assert.True(double.IsNaN(r.BraytonCarnotEfficiency));
        Assert.True(double.IsNaN(r.ReactorPowerToBrayton_MW));
        Assert.True(double.IsNaN(r.BraytonHeMassFlow_kgs));
    }

    [Fact]
    public void WithExpression_OverridesBimodalFields()
    {
        var r = MakeMinimalFeasible() with
        {
            ElectricPowerOutput_kWe   = 100.0,
            BraytonThermalEfficiency  = 0.28,
            BraytonCarnotEfficiency   = 0.55,
            ReactorPowerToBrayton_MW  = 0.5,
            BraytonHeMassFlow_kgs     = 1.2,
        };
        Assert.Equal(100.0, r.ElectricPowerOutput_kWe);
        Assert.Equal(0.28,  r.BraytonThermalEfficiency);
        Assert.Equal(0.55,  r.BraytonCarnotEfficiency);
        Assert.Equal(0.5,   r.ReactorPowerToBrayton_MW);
        Assert.Equal(1.2,   r.BraytonHeMassFlow_kgs);
    }

    // ── IsFeasible echo-back ─────────────────────────────────────────────

    [Fact]
    public void IsFeasible_CarriesThroughCtor_False_WithViolations()
    {
        var violations = new List<FeasibilityViolation>
        {
            new(ConstraintId: "NTR_THERMAL_FLUX_EXCEEDED",
                Description:  "Flux 5000 MW/m³ above HEU limit 4000",
                ActualValue:  5000.0,
                Limit:        4000.0),
        };
        var r = new NtrGenerationResult(
            MakeNrxA6(), MakeCond(),
            CoreExitTemp_K: 2500, GammaEff: 1.4, CStar_ms: 2400,
            IspVacuum_s: 825, ThrustVacuum_N: 267_000,
            VolumetricHeatFlux_MWm3: 5000, KEff: 1.0,
            RegenNozzleWallTempExceedsLimit: false,
            Violations: violations,
            Advisories: new List<FeasibilityViolation>(),
            IsFeasible: false);
        Assert.False(r.IsFeasible);
        Assert.Single(r.Violations);
        Assert.Equal("NTR_THERMAL_FLUX_EXCEEDED", r.Violations[0].ConstraintId);
    }

    // ── IEngineResult marker ─────────────────────────────────────────────

    [Fact]
    public void NtrGenerationResult_ImplementsIEngineResult()
    {
        var r = MakeMinimalFeasible();
        Assert.IsAssignableFrom<Voxelforge.Engines.IEngineResult>(r);
    }

    // ── End-to-end finite-field smoke ────────────────────────────────────

    [Fact]
    public void EndToEnd_FromOptimization_PopulatesScalarFields()
    {
        // Sanity: a real NRX-A6 evaluation must populate every primary
        // scalar with a finite value. Catches regressions where a solver
        // leaves a field at NaN.
        var r = NuclearOptimization.GenerateWith(MakeNrxA6(), MakeCond());
        Assert.False(double.IsNaN(r.CoreExitTemp_K));
        Assert.False(double.IsNaN(r.GammaEff));
        Assert.False(double.IsNaN(r.CStar_ms));
        Assert.False(double.IsNaN(r.IspVacuum_s));
        Assert.False(double.IsNaN(r.ThrustVacuum_N));
        Assert.False(double.IsNaN(r.VolumetricHeatFlux_MWm3));
        Assert.False(double.IsNaN(r.KEff));
    }
}
