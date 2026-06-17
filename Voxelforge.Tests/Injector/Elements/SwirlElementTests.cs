// SwirlElementTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.5: SwirlElement was covered transitively
// by the [Theory] in InjectorElementTests.cs (IsImplemented + positive
// areas). The swirl-specific physics — Bazarov K-fit for μ, Abramovich
// cone half-angle — had no direct assertion. This test bundle pins
// those closed forms.

using Voxelforge.Injector;
using Voxelforge.Injector.Elements;

namespace Voxelforge.Tests;

public class SwirlElementTests
{
    private static SizingInputs MakeInputs(
        double oxFlow_kgs   = 0.01,
        double fuelFlow_kgs = 0.01,
        double dP_Pa        = 0.20 * 6.9e6,
        double oxRho        = OrificeModel.ReferenceDensity_kgm3.LOX,
        double fuelRho      = OrificeModel.ReferenceDensity_kgm3.LCH4) =>
        new(DeltaPInj_Pa:           dP_Pa,
            OxDensity_kgm3:         oxRho,
            FuelDensity_kgm3:       fuelRho,
            OxFlowPerElement_kgs:   oxFlow_kgs,
            FuelFlowPerElement_kgs: fuelFlow_kgs,
            CdOx:                   OrificeModel.DefaultCd,
            CdFuel:                 OrificeModel.DefaultCd);

    [Fact]
    public void Metadata_ReportsSwirlAndImplemented()
    {
        var elem = new SwirlElement();
        Assert.Equal("Swirl", elem.ElementType);
        Assert.True(elem.IsImplemented);
        // Default swirl K is 2.0 per Bazarov, gives ~60° cone half-angle.
        Assert.Equal(2.0, elem.SwirlParameter, precision: 6);
    }

    [Fact]
    public void Size_ProducesPositiveAreasAtBaselineK()
    {
        var elem = new SwirlElement();
        var r = elem.Size(MakeInputs());

        Assert.True(r.OxOrificeArea_mm2 > 0);
        Assert.True(r.FuelOrificeArea_mm2 > 0);
        Assert.True(r.OxVelocity_ms > 0);
        Assert.True(r.FuelVelocity_ms > 0);
    }

    [Fact]
    public void Size_NoteIncludesSwirlParameterAndConeAngle()
    {
        var r = new SwirlElement { SwirlParameter = 2.0 }.Size(MakeInputs());
        Assert.Contains("Swirl K = 2.00", r.Notes[0]);
        Assert.Contains("cone half-angle", r.Notes[0]);
    }

    [Fact]
    public void Size_LowK_RaisesEffectiveCd_ShrinksOxOrifice()
    {
        // μ = 0.35/√K — small K (lower swirl) gives higher μ, so the
        // ox orifice is smaller for the same ṁ. K=1 → μ=0.35;
        // K=4 → μ=0.175. Halving μ doubles the area at fixed flow.
        var rLowK  = new SwirlElement { SwirlParameter = 1.0 }.Size(MakeInputs());
        var rHighK = new SwirlElement { SwirlParameter = 4.0 }.Size(MakeInputs());

        Assert.True(rLowK.OxOrificeArea_mm2 < rHighK.OxOrificeArea_mm2,
            $"K=1 area {rLowK.OxOrificeArea_mm2:F3} should be smaller than " +
            $"K=4 area {rHighK.OxOrificeArea_mm2:F3} mm².");
    }

    [Fact]
    public void Size_HigherK_WidersConeHalfAngle()
    {
        // μ decreases with K → sin(α) = 2√(1-μ²)/(1+√(1-μ²)) increases.
        var rLowK  = new SwirlElement { SwirlParameter = 1.5 }.Size(MakeInputs());
        var rHighK = new SwirlElement { SwirlParameter = 4.0 }.Size(MakeInputs());

        // Extract numeric cone angle from the note string.
        double angleLow  = ParseAngleDeg(rLowK.Notes[0]);
        double angleHigh = ParseAngleDeg(rHighK.Notes[0]);
        Assert.True(angleHigh > angleLow,
            $"K=4 cone half-angle ({angleHigh:F1}°) should exceed " +
            $"K=1.5 ({angleLow:F1}°).");
    }

    [Fact]
    public void Size_FuelSide_UsesPlainOrificeArea()
    {
        // Fuel side bypasses the swirl path and uses OrificeModel directly.
        var inp = MakeInputs();
        var r = new SwirlElement { SwirlParameter = 2.0 }.Size(inp);

        double expectedFuel_mm2 = OrificeModel.OrificeArea_mm2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa,
            inp.FuelDensity_kgm3, inp.CdFuel);

        Assert.Equal(expectedFuel_mm2, r.FuelOrificeArea_mm2, precision: 4);
    }

    [Fact]
    public void Size_NonPintleElement_LeavesBlockageFractionAtZero()
    {
        var r = new SwirlElement().Size(MakeInputs());
        Assert.Equal(0.0, r.PintleBlockageFraction, precision: 6);
    }

    [Fact]
    public void Size_ExtremelyHighK_NoteWarnsOnWideConeAngle()
    {
        // K=10 drives μ low enough that the cone half-angle approaches
        // the wide-cone (>75°) warning band.
        var r = new SwirlElement { SwirlParameter = 10.0 }.Size(MakeInputs());
        bool gotWarning = false;
        foreach (var n in r.Notes)
            if (n.Contains("over-wet") || n.Contains("> 75")) gotWarning = true;
        Assert.True(gotWarning,
            "High K should add a wide-cone over-wetting warning to Notes.");
    }

    [Fact]
    public void Size_VerySmallK_FloorsAtPoint2()
    {
        // SwirlParameter clamped to 0.2 minimum inside Size(). Below
        // that the formula would over-credit μ and become unphysical.
        var rUnderfloor = new SwirlElement { SwirlParameter = 0.05 }.Size(MakeInputs());
        var rAtFloor    = new SwirlElement { SwirlParameter = 0.20 }.Size(MakeInputs());

        // Same effective K → identical ox area.
        Assert.Equal(rAtFloor.OxOrificeArea_mm2, rUnderfloor.OxOrificeArea_mm2,
                     precision: 4);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static double ParseAngleDeg(string note)
    {
        // Note format: "Swirl K = X.XX, μ_ox = X.XX, cone half-angle = XX°."
        int idx = note.IndexOf("cone half-angle =", System.StringComparison.Ordinal);
        if (idx < 0) return double.NaN;
        int start = idx + "cone half-angle =".Length;
        int end = note.IndexOf('°', start);
        if (end < 0) return double.NaN;
        var slice = note.Substring(start, end - start).Trim();
        return double.Parse(slice, System.Globalization.CultureInfo.InvariantCulture);
    }
}
