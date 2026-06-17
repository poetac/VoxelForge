// AntennaWave7Tests.cs — Sprint ANT.W7 unit tests for the geometry → RF
// coupling advisory checks in AntennaSolver:
//   CheckHelicalGeometryRfMismatch
//   CheckYagiElementSpacingValidity
//   ComputePatchResonantFrequency_Hz
//   CheckPatchGeometryRfMismatch
//
// No PicoGK dependency — pure physics/algebra tests.

using System;
using Voxelforge.Antenna;
using Xunit;

namespace Voxelforge.Tests.Antenna;

public sealed class AntennaWave7Tests
{
    // ── Shared test design ─────────────────────────────────────────────

    private static AntennaLinkDesign BaseHelical(
        double coilDia_mm = 0.0,
        double cRel = 1.0) => new(
        TransmitAntennaKind:    AntennaKind.Helical,
        ReceiveAntennaKind:     AntennaKind.Helical,
        Frequency_Hz:           2.4e9,
        TransmitPower_W:        1.0,
        LinkDistance_m:         1_000.0,
        HelicalTurns:           10,
        HelicalCircumference_rel: cRel,
        HelicalCoilDiameter_mm: coilDia_mm);

    private static AntennaLinkDesign BaseYagi(double spacing_mm = 0.0) => new(
        TransmitAntennaKind:    AntennaKind.YagiUda,
        ReceiveAntennaKind:     AntennaKind.YagiUda,
        Frequency_Hz:           2.4e9,
        TransmitPower_W:        1.0,
        LinkDistance_m:         100.0,
        YagiElementSpacing_mm:  spacing_mm);

    // ── Helical geometry → RF mismatch ────────────────────────────────

    [Fact]
    public void Helical_ZeroCoilDiameter_NoMismatchFlag()
    {
        // HelicalCoilDiameter_mm = 0 → coupling check disabled.
        Assert.False(AntennaSolver.CheckHelicalGeometryRfMismatch(BaseHelical()));
    }

    [Fact]
    public void Helical_MatchingPhysicalCrel_NoMismatchFlag()
    {
        // Physical C/λ = 1.0; design C/λ = 1.0 → no mismatch.
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 2.4e9 * 1000.0;
        double dia_mm = lambda_mm / Math.PI;   // C = π·D = λ → C/λ = 1.0
        Assert.False(AntennaSolver.CheckHelicalGeometryRfMismatch(
            BaseHelical(coilDia_mm: dia_mm, cRel: 1.0)));
    }

    [Fact]
    public void Helical_10pctMismatch_FiresMismatchFlag()
    {
        // Physical C/λ ≈ 1.1 (10 % more than design C/λ = 1.0) → fires.
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 2.4e9 * 1000.0;
        double dia_mm = 1.1 * lambda_mm / Math.PI;  // C = 1.1λ
        Assert.True(AntennaSolver.CheckHelicalGeometryRfMismatch(
            BaseHelical(coilDia_mm: dia_mm, cRel: 1.0)));
    }

    [Fact]
    public void Helical_3pctMismatch_DoesNotFireFlag()
    {
        // 3 % relative difference → below 5 % threshold → no flag.
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 2.4e9 * 1000.0;
        double dia_mm = 1.03 * lambda_mm / Math.PI;
        Assert.False(AntennaSolver.CheckHelicalGeometryRfMismatch(
            BaseHelical(coilDia_mm: dia_mm, cRel: 1.0)));
    }

    // ── Yagi element-spacing validity ─────────────────────────────────

    [Fact]
    public void Yagi_ZeroSpacing_NoCouplingCheck()
    {
        Assert.False(AntennaSolver.CheckYagiElementSpacingValidity(BaseYagi(0.0)));
    }

    [Fact]
    public void Yagi_OptimalSpacing0p3Lambda_NoFlag()
    {
        // 0.3λ spacing → within [0.1, 0.5] Yagi optimal range.
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 2.4e9 * 1000.0;
        Assert.False(AntennaSolver.CheckYagiElementSpacingValidity(
            BaseYagi(spacing_mm: 0.3 * lambda_mm)));
    }

    [Fact]
    public void Yagi_TooSmallSpacing_FiresFlag()
    {
        // 0.05λ < 0.1λ minimum → fires.
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 2.4e9 * 1000.0;
        Assert.True(AntennaSolver.CheckYagiElementSpacingValidity(
            BaseYagi(spacing_mm: 0.05 * lambda_mm)));
    }

    [Fact]
    public void Yagi_TooLargeSpacing_FiresFlag()
    {
        // 0.6λ > 0.5λ maximum → fires.
        double lambda_mm = AntennaSolver.SpeedOfLight_ms / 2.4e9 * 1000.0;
        Assert.True(AntennaSolver.CheckYagiElementSpacingValidity(
            BaseYagi(spacing_mm: 0.6 * lambda_mm)));
    }

    // ── Patch resonant frequency ──────────────────────────────────────

    [Fact]
    public void PatchResonantFrequency_AutoComputed_EqualsDesignFrequency()
    {
        // When PatchWidth and PatchLength are both 0 (auto-compute), the
        // Bahl-Trivedi self-consistent formulas return f_r = f_design.
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:  AntennaKind.Patch,
            ReceiveAntennaKind:   AntennaKind.Patch,
            Frequency_Hz:         2.4e9,
            TransmitPower_W:      0.1,
            LinkDistance_m:       1.0,
            PrintMaterialKind:    PrintMaterial.SlaResinRogers,
            SubstrateThickness_mm: 1.6,
            PatchWidth_mm:        0.0,
            PatchLength_mm:       0.0);
        double f_r = AntennaSolver.ComputePatchResonantFrequency_Hz(design);
        // Self-consistent → f_r ≈ f_design to machine precision.
        Assert.InRange(f_r, 2.3e9, 2.5e9);
    }

    [Fact]
    public void PatchResonantFrequency_SlaRogers_IsConsistentWithEpsilon355()
    {
        // SLA Rogers: ε_r = 3.55. Auto-computed patch at 10 GHz on 1 mm substrate.
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:  AntennaKind.Patch,
            ReceiveAntennaKind:   AntennaKind.Patch,
            Frequency_Hz:         10e9,
            TransmitPower_W:      0.1,
            LinkDistance_m:       1.0,
            PrintMaterialKind:    PrintMaterial.SlaResinRogers,
            SubstrateThickness_mm: 1.0);
        double f_r = AntennaSolver.ComputePatchResonantFrequency_Hz(design);
        // Auto-compute → self-consistent; result should be near design frequency.
        Assert.InRange(f_r, 9.5e9, 10.5e9);
    }

    [Fact]
    public void PatchMismatch_ShortPatch_FiresFlag()
    {
        // Patch length half of what it should be → f_r ≈ 2× design freq → > 5 % off.
        double W_mm = AntennaSolver.ComputePatchWidth_mm(2.4e9,
            PrintMaterialTable.RelativePermittivity(PrintMaterial.SlaResinRogers));
        double correctL  = AntennaSolver.ComputePatchLength_mm(
            2.4e9,
            PrintMaterialTable.RelativePermittivity(PrintMaterial.SlaResinRogers),
            W_mm, 1.6);
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:   AntennaKind.Patch,
            ReceiveAntennaKind:    AntennaKind.Patch,
            Frequency_Hz:          2.4e9,
            TransmitPower_W:       0.1,
            LinkDistance_m:        1.0,
            PrintMaterialKind:     PrintMaterial.SlaResinRogers,
            SubstrateThickness_mm: 1.6,
            PatchWidth_mm:         W_mm,
            PatchLength_mm:        0.5 * correctL);  // half-length → 2× frequency
        Assert.True(AntennaSolver.CheckPatchGeometryRfMismatch(design));
    }

    [Fact]
    public void PatchMismatch_ZeroUserDimensions_NoFlag()
    {
        // Both dimensions 0 (auto-compute) → never triggers advisory.
        var design = new AntennaLinkDesign(
            TransmitAntennaKind:  AntennaKind.Patch,
            ReceiveAntennaKind:   AntennaKind.Patch,
            Frequency_Hz:         2.4e9,
            TransmitPower_W:      0.1,
            LinkDistance_m:       1.0,
            PrintMaterialKind:    PrintMaterial.SlaResinRogers,
            SubstrateThickness_mm: 1.6);
        Assert.False(AntennaSolver.CheckPatchGeometryRfMismatch(design));
    }

    [Fact]
    public void ConstraintIdStrings_HaveExpectedValues()
    {
        Assert.Equal("ANTENNA_WIRE_TOO_THIN",
            AntennaConstraintIds.WireTooThin);
        Assert.Equal("ANTENNA_ELEMENT_OVERHANG_UNSUPPORTED",
            AntennaConstraintIds.ElementOverhangUnsupported);
        Assert.Equal("ANTENNA_SUBSTRATE_TOO_THIN",
            AntennaConstraintIds.SubstrateTooThin);
        Assert.Equal("ANTENNA_GEOMETRY_RF_MISMATCH",
            AntennaConstraintIds.GeometryRfMismatch);
    }
}
