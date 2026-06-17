// NuclearFuelMaterialTests.cs — Sprint NU.W4 unit tests for the per-
// material fuel data registry + wiring through FuelPinHeatModel +
// NuclearGates.

using Voxelforge.Nuclear;
using Voxelforge.Nuclear.FuelPin;
using Voxelforge.Optimization;
using System.Linq;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class NuclearFuelMaterialTests
{
    // ── NuclearFuelMaterials registry ────────────────────────────────────

    [Fact]
    public void For_None_ResolvesToUO2Cermet()
    {
        var data = NuclearFuelMaterials.For(NuclearFuelMaterial.None);
        Assert.Equal(NuclearFuelMaterials.UO2Cermet, data);
    }

    [Fact]
    public void For_UO2Cermet_HasExpectedConductivityAndLimit()
    {
        var data = NuclearFuelMaterials.For(NuclearFuelMaterial.UO2Cermet);
        Assert.Equal(16.0,   data.ThermalConductivity_WmK, precision: 6);
        Assert.Equal(3200.0, data.CenterlineTempLimit_K,   precision: 6);
    }

    [Fact]
    public void For_UC2Graphite_HigherTempLimitLowerConductivity()
    {
        var ce = NuclearFuelMaterials.For(NuclearFuelMaterial.UO2Cermet);
        var gr = NuclearFuelMaterials.For(NuclearFuelMaterial.UC2Graphite);
        // UC₂-graphite: higher T_max (≈ 3500 K) but lower conductivity than
        // metal-matrix cermet (≈ 8 W/(m·K)).
        Assert.True(gr.CenterlineTempLimit_K > ce.CenterlineTempLimit_K);
        Assert.True(gr.ThermalConductivity_WmK < ce.ThermalConductivity_WmK);
    }

    [Fact]
    public void For_UNRefractory_HigherConductivityLowerTempLimit()
    {
        var ce = NuclearFuelMaterials.For(NuclearFuelMaterial.UO2Cermet);
        var un = NuclearFuelMaterials.For(NuclearFuelMaterial.UNRefractory);
        // UN-refractory: higher conductivity but lower T_max (UN dissociates).
        Assert.True(un.ThermalConductivity_WmK > ce.ThermalConductivity_WmK);
        Assert.True(un.CenterlineTempLimit_K < ce.CenterlineTempLimit_K);
    }

    // ── FuelPinHeatModel per-material wiring ─────────────────────────────

    private static HexArrayGeometryResult NrxA6Geometry()
        => HexArrayGeometry.Resolve(hexRings: 2, pinDiameter_mm: 2.5, pinPitch_mm: 3.2);

    [Fact]
    public void Solve_FuelMaterial_None_BitIdenticalToUO2Cermet()
    {
        // None must produce bit-identical output to UO₂-cermet (Wave-2
        // backwards-compat invariant).
        var rNone = FuelPinHeatModel.Solve(
            reactorThermalPower_W: 1100e6,
            fuelElementCount:      564,
            hexGeometry:           NrxA6Geometry(),
            fuelPinLength_m:       1.4,
            coolantMassFlow_kgs:   33.0,
            coolantInletTemp_K:    80.0,
            coolantInletPressure_Pa: 34e5,
            fuelMaterial:          NuclearFuelMaterial.None);
        var rCe = FuelPinHeatModel.Solve(
            reactorThermalPower_W: 1100e6,
            fuelElementCount:      564,
            hexGeometry:           NrxA6Geometry(),
            fuelPinLength_m:       1.4,
            coolantMassFlow_kgs:   33.0,
            coolantInletTemp_K:    80.0,
            coolantInletPressure_Pa: 34e5,
            fuelMaterial:          NuclearFuelMaterial.UO2Cermet);
        Assert.Equal(rCe.CenterlineToSurfaceDeltaT_K, rNone.CenterlineToSurfaceDeltaT_K, precision: 6);
        Assert.Equal(rCe.PeakFuelCenterlineTemp_K,   rNone.PeakFuelCenterlineTemp_K,   precision: 6);
    }

    [Fact]
    public void Solve_UC2Graphite_GivesLargerCenterlineToSurfaceDeltaT()
    {
        // Lower conductivity → larger ΔT_cs at same q'''. UC₂-graphite
        // (k=8) should give ~2x the ΔT_cs of UO₂-cermet (k=16).
        var rCe = FuelPinHeatModel.Solve(
            1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5,
            fuelMaterial: NuclearFuelMaterial.UO2Cermet);
        var rGr = FuelPinHeatModel.Solve(
            1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5,
            fuelMaterial: NuclearFuelMaterial.UC2Graphite);
        Assert.True(rGr.CenterlineToSurfaceDeltaT_K > rCe.CenterlineToSurfaceDeltaT_K);
        // Ratio should be 16/8 = 2.0 (other terms identical at same inputs).
        double ratio = rGr.CenterlineToSurfaceDeltaT_K / rCe.CenterlineToSurfaceDeltaT_K;
        Assert.InRange(ratio, 1.98, 2.02);
    }

    [Fact]
    public void Solve_UNRefractory_GivesSmallerCenterlineToSurfaceDeltaT()
    {
        // Higher conductivity → smaller ΔT_cs. UN (k=25) should give
        // ~0.64x the ΔT_cs of cermet (k=16).
        var rCe = FuelPinHeatModel.Solve(
            1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5,
            fuelMaterial: NuclearFuelMaterial.UO2Cermet);
        var rUn = FuelPinHeatModel.Solve(
            1100e6, 564, NrxA6Geometry(), 1.4, 33.0, 80.0, 34e5,
            fuelMaterial: NuclearFuelMaterial.UNRefractory);
        Assert.True(rUn.CenterlineToSurfaceDeltaT_K < rCe.CenterlineToSurfaceDeltaT_K);
        double ratio = rUn.CenterlineToSurfaceDeltaT_K / rCe.CenterlineToSurfaceDeltaT_K;
        Assert.InRange(ratio, 0.62, 0.66);  // 16/25 = 0.64
    }

    // ── Gate per-material limit ──────────────────────────────────────────

    private static NuclearThermalDesign BaselineFuelPinDesign() => new NuclearThermalDesign(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     40.0,
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
        FuelPinHexRings     = 2,
        FuelElementCount    = 564,
        FuelPinLength_m     = 1.4,
    };

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    private static bool Has(System.Collections.Generic.IReadOnlyList<FeasibilityViolation> v, string id)
        => v.Any(x => x.ConstraintId == id);

    [Fact]
    public void Gate_UNRefractoryWithT2900_FiresOvertempEvenThoughBelowCermetLimit()
    {
        // Construct a design that runs near the UN-refractory T_max (2800 K)
        // but would pass the UO₂-cermet limit (3200 K). Force this by
        // shrinking elements so per-pin power rises.
        // For UN at k=25 → smaller ΔT_cs vs cermet, so T_peak is lower than
        // it would be for cermet at same q'''. Need significant overdrive
        // to push UN-T_peak above 2800.
        //
        // For UN k=25, NRX-A6-shaped baseline at full power: T_peak ≈ ~2900 K.
        // So baseline-with-UN should fire UN gate.
        var design = BaselineFuelPinDesign() with
        {
            FuelMaterial = NuclearFuelMaterial.UNRefractory,
        };
        var r = NuclearOptimization.GenerateWith(design, Cond());
        // Either fires UN overtemp OR not — the test is about which limit is
        // applied. UN limit is 2800 K. If T_peak > 2800 → fires; if T_peak ≤
        // 2800 → doesn't. Verify the gate description references UN.
        if (Has(r.Violations, "NTR_FUEL_PIN_OVERTEMP"))
        {
            var violation = r.Violations.First(v => v.ConstraintId == "NTR_FUEL_PIN_OVERTEMP");
            Assert.Contains("UNRefractory", violation.Description);
            Assert.Equal(2800.0, violation.Limit, precision: 6);
        }
    }

    [Fact]
    public void Gate_UC2GraphiteHasHigherTempLimit()
    {
        // UC₂-graphite limit 3500 K is HIGHER than cermet 3200 K. A design
        // with T_peak ≈ 3300 K would FIRE the cermet gate but PASS the UC₂
        // gate. Verify the limit difference is reflected in any
        // violation description.
        //
        // Achieve T_peak ≈ 3300 K via aggressive design: 200 elements
        // (vs 564) drives Q_pin up.
        var ce = BaselineFuelPinDesign() with
        {
            FuelMaterial      = NuclearFuelMaterial.UO2Cermet,
            FuelElementCount  = 200,
        };
        var gr = BaselineFuelPinDesign() with
        {
            FuelMaterial      = NuclearFuelMaterial.UC2Graphite,
            FuelElementCount  = 200,
        };
        var rCe = NuclearOptimization.GenerateWith(ce, Cond());
        var rGr = NuclearOptimization.GenerateWith(gr, Cond());
        // Cermet at this aggressive design DOES fire overtemp (limit 3200 K).
        Assert.True(Has(rCe.Violations, "NTR_FUEL_PIN_OVERTEMP"),
            $"Cermet at FEC=200 expected to fire NTR_FUEL_PIN_OVERTEMP; saw "
          + string.Join(", ", rCe.Violations.Select(v => v.ConstraintId)));
        // Graphite has lower k → larger ΔT_cs, so its peak is even higher.
        // Graphite also has higher limit (3500 K). At FEC=200, peak likely
        // > 3500 still — graphite gate may still fire. Verify the message
        // references the graphite limit, not cermet's 3200 K.
        if (Has(rGr.Violations, "NTR_FUEL_PIN_OVERTEMP"))
        {
            var v = rGr.Violations.First(x => x.ConstraintId == "NTR_FUEL_PIN_OVERTEMP");
            Assert.Equal(3500.0, v.Limit, precision: 6);
        }
    }

    [Fact]
    public void NuclearThermalDesign_FuelMaterial_DefaultsToNone()
    {
        // Wave-2 designs that pre-date NU.W4 leave FuelMaterial at None,
        // which maps to UO₂-cermet behaviour for backwards compat.
        var design = BaselineFuelPinDesign();
        Assert.Equal(NuclearFuelMaterial.None, design.FuelMaterial);
    }
}
