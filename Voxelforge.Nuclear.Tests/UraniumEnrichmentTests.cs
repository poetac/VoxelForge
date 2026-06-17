// UraniumEnrichmentTests.cs — Sprint NU.W5 unit tests for the per-tier
// enrichment registry + wiring through NTR_THERMAL_FLUX_EXCEEDED gate.

using System.Linq;
using Voxelforge.Nuclear;
using Voxelforge.Nuclear.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Nuclear.Tests;

public sealed class UraniumEnrichmentTests
{
    // ── UraniumEnrichmentTiers registry ──────────────────────────────────

    [Fact]
    public void For_None_ResolvesToHEU()
    {
        var data = UraniumEnrichmentTiers.For(UraniumEnrichment.None);
        Assert.Equal(UraniumEnrichmentTiers.HEU, data);
    }

    [Fact]
    public void For_LEU_HasExpectedLimits()
    {
        var data = UraniumEnrichmentTiers.For(UraniumEnrichment.LEU);
        Assert.Equal(50.0, data.MaxVolumetricHeatFlux_MWm3, precision: 6);
        Assert.Equal(0.0,  data.MinU235Fraction,            precision: 6);
        Assert.Equal(0.05, data.MaxU235Fraction,            precision: 6);
    }

    [Fact]
    public void For_HALEU_HasExpectedLimits()
    {
        var data = UraniumEnrichmentTiers.For(UraniumEnrichment.HALEU);
        Assert.Equal(500.0,  data.MaxVolumetricHeatFlux_MWm3, precision: 6);
        Assert.Equal(0.05,   data.MinU235Fraction,            precision: 6);
        Assert.Equal(0.1975, data.MaxU235Fraction,            precision: 6);
    }

    [Fact]
    public void For_HEU_HasExpectedLimits()
    {
        var data = UraniumEnrichmentTiers.For(UraniumEnrichment.HEU);
        Assert.Equal(4000.0, data.MaxVolumetricHeatFlux_MWm3, precision: 6);
        Assert.Equal(0.1975, data.MinU235Fraction,            precision: 6);
        Assert.Equal(1.0,    data.MaxU235Fraction,            precision: 6);
    }

    [Fact]
    public void TierBands_AreContiguous_AndCoverFullRange()
    {
        // LEU upper = HALEU lower; HALEU upper = HEU lower; HEU upper = 1.0.
        Assert.Equal(
            UraniumEnrichmentTiers.LEU.MaxU235Fraction,
            UraniumEnrichmentTiers.HALEU.MinU235Fraction,
            precision: 6);
        Assert.Equal(
            UraniumEnrichmentTiers.HALEU.MaxU235Fraction,
            UraniumEnrichmentTiers.HEU.MinU235Fraction,
            precision: 6);
        Assert.Equal(0.0, UraniumEnrichmentTiers.LEU.MinU235Fraction, precision: 6);
        Assert.Equal(1.0, UraniumEnrichmentTiers.HEU.MaxU235Fraction, precision: 6);
    }

    [Fact]
    public void TierMaxFlux_IsMonotonicallyIncreasingByTier()
    {
        // Higher enrichment → higher achievable power density.
        Assert.True(UraniumEnrichmentTiers.LEU.MaxVolumetricHeatFlux_MWm3
                  < UraniumEnrichmentTiers.HALEU.MaxVolumetricHeatFlux_MWm3);
        Assert.True(UraniumEnrichmentTiers.HALEU.MaxVolumetricHeatFlux_MWm3
                  < UraniumEnrichmentTiers.HEU.MaxVolumetricHeatFlux_MWm3);
    }

    // ── NuclearThermalDesign field defaults ──────────────────────────────

    [Fact]
    public void NuclearThermalDesign_EnrichmentTier_DefaultsToNone()
    {
        // Wave-1/W2/W3/W4 designs that pre-date NU.W5 leave the field at
        // None, which maps to HEU behaviour for backwards compat.
        var design = NominalDesign();
        Assert.Equal(UraniumEnrichment.None, design.EnrichmentTier);
    }

    // ── NTR_THERMAL_FLUX_EXCEEDED — per-tier gate behaviour ──────────────
    //
    // NominalDesign: P = 1100 MW, V = π·(0.7)²·1.4 = 2.155 m³ →
    // Q_vol ≈ 510 MW/m³. This falls:
    //   LEU   (50 MW/m³)   — fires.
    //   HALEU (500 MW/m³)  — fires (~510 > 500).
    //   HEU   (4000 MW/m³) — passes.
    //   None  → HEU        — passes.

    [Fact]
    public void ThermalFlux_None_BehavesAsHEU_DoesNotFire()
    {
        var design = NominalDesign();   // EnrichmentTier defaults to None.
        var r = NuclearOptimization.GenerateWith(design, Cond());
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
    }

    [Fact]
    public void ThermalFlux_HEU_DoesNotFire_AtNominalDesignFlux()
    {
        var design = NominalDesign() with { EnrichmentTier = UraniumEnrichment.HEU };
        var r = NuclearOptimization.GenerateWith(design, Cond());
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
    }

    [Fact]
    public void ThermalFlux_LEU_Fires_AtNominalDesignFlux()
    {
        // Nominal Q_vol ≈ 510 MW/m³, far above LEU's 50 MW/m³ ceiling.
        var design = NominalDesign() with { EnrichmentTier = UraniumEnrichment.LEU };
        var r = NuclearOptimization.GenerateWith(design, Cond());
        var violation = r.Violations.FirstOrDefault(
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
        Assert.NotNull(violation);
        Assert.Equal(50.0, violation!.Limit, precision: 6);
        Assert.Contains("LEU", violation.Description);
    }

    [Fact]
    public void ThermalFlux_HALEU_Fires_AtNominalDesignFlux()
    {
        // Nominal Q_vol ≈ 510 MW/m³, just above HALEU's 500 MW/m³ ceiling.
        var design = NominalDesign() with { EnrichmentTier = UraniumEnrichment.HALEU };
        var r = NuclearOptimization.GenerateWith(design, Cond());
        var violation = r.Violations.FirstOrDefault(
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
        Assert.NotNull(violation);
        Assert.Equal(500.0, violation!.Limit, precision: 6);
        Assert.Contains("HALEU", violation.Description);
    }

    [Fact]
    public void ThermalFlux_HALEU_Passes_AtLowFlux()
    {
        // Slightly larger core (L=1.6 m) drives Q_vol ≈ 446 MW/m³ < 500.
        // Verifies HALEU passes when the design is below its 500 MW/m³
        // ceiling — proves the ceiling is being applied as a ceiling,
        // not as a baseline-firing constant.
        var design = NominalDesign() with
        {
            EnrichmentTier      = UraniumEnrichment.HALEU,
            ReactorCoreLength_mm = 1600.0,
        };
        var r = NuclearOptimization.GenerateWith(design, Cond());
        Assert.DoesNotContain(r.Violations,
            v => v.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
    }

    [Fact]
    public void ThermalFlux_LEU_DescriptionReferencesActualLEULabel_NotBackwardsCompat()
    {
        // When the user explicitly selects LEU, the description must
        // identify the tier as "LEU", NOT the "HEU (backwards-compat
        // default)" label reserved for the None sentinel.
        var design = NominalDesign() with { EnrichmentTier = UraniumEnrichment.LEU };
        var r = NuclearOptimization.GenerateWith(design, Cond());
        var v = r.Violations.First(x => x.ConstraintId == NuclearConstraintIds.ThermalFluxExceeded);
        Assert.DoesNotContain("backwards-compat", v.Description);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static NuclearThermalDesign NominalDesign() => new(
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
        NozzleManifoldDepth_mm:  5.0);

    private static NuclearThermalConditions Cond() =>
        new(PropellantInletTemp_K: 80.0);
}
