// ShowerheadElementTests.cs — Issue #556 PR-1 (2026-05-16).
//
// Per audit 05-test-gaps.md § 1.5: ShowerheadElement was tested only
// transitively via the [Theory] in InjectorElementTests.cs that confirms
// IsImplemented + positive areas. The orifice-sizing physics + warning
// thresholds had no direct coverage. This test bundle pins the closed-form
// orifice equation (Q = Cd·A·√(2·ρ·ΔP)) and the LPBF-floor warning trip.

using Voxelforge.Combustion;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;

namespace Voxelforge.Tests;

public class ShowerheadElementTests
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
    public void Metadata_ReportsShowerheadAndImplemented()
    {
        var elem = new ShowerheadElement();
        Assert.Equal("Showerhead", elem.ElementType);
        Assert.True(elem.IsImplemented);
    }

    [Fact]
    public void Size_ProducesPositiveAreasForBaselineInputs()
    {
        var elem = new ShowerheadElement();
        var r = elem.Size(MakeInputs());

        Assert.True(r.OxOrificeArea_mm2 > 0);
        Assert.True(r.FuelOrificeArea_mm2 > 0);
        Assert.True(r.OxVelocity_ms > 0);
        Assert.True(r.FuelVelocity_ms > 0);
    }

    [Fact]
    public void Size_MatchesClosedFormOrificeEquation()
    {
        // Showerhead is a straight orifice on each side — area must
        // equal the canonical OrificeModel.OrificeArea_mm2 result.
        var elem = new ShowerheadElement();
        var inp = MakeInputs();
        var r = elem.Size(inp);

        double expectedOx = OrificeModel.OrificeArea_mm2(
            inp.OxFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.OxDensity_kgm3, inp.CdOx);
        double expectedFuel = OrificeModel.OrificeArea_mm2(
            inp.FuelFlowPerElement_kgs, inp.DeltaPInj_Pa, inp.FuelDensity_kgm3, inp.CdFuel);

        Assert.Equal(expectedOx, r.OxOrificeArea_mm2, precision: 4);
        Assert.Equal(expectedFuel, r.FuelOrificeArea_mm2, precision: 4);
    }

    [Fact]
    public void Size_LowDensityFuel_NeedsLargerOrificeForSameFlow()
    {
        // A ∝ 1/√ρ at equal flow. LCH4 (430) is ~2.65× less dense than
        // LOX (1140), so the fuel orifice must be √2.65 ≈ 1.63× the ox area.
        var r = new ShowerheadElement().Size(MakeInputs(
            oxFlow_kgs:   0.01, fuelFlow_kgs: 0.01,
            oxRho:        OrificeModel.ReferenceDensity_kgm3.LOX,
            fuelRho:      OrificeModel.ReferenceDensity_kgm3.LCH4));

        Assert.True(r.FuelOrificeArea_mm2 > r.OxOrificeArea_mm2);
    }

    [Fact]
    public void Size_SymmetricInputs_VelocityRatioEqualsOne()
    {
        // V = Cd·√(2·ΔP/ρ); same Cd, ρ, ΔP gives identical velocities.
        var r = new ShowerheadElement().Size(MakeInputs(
            oxRho:   1000.0,
            fuelRho: 1000.0));

        Assert.Equal(1.0, r.VelocityRatio, precision: 4);
    }

    [Fact]
    public void Size_NotesDescribeOxAndFuelBoreDiameters()
    {
        var r = new ShowerheadElement().Size(MakeInputs());
        // First note is the diameter-summary line.
        Assert.Contains("Ox hole",   r.Notes[0]);
        Assert.Contains("Fuel hole", r.Notes[0]);
        // Showerhead-specific note advertising shear-driven atomisation.
        Assert.Contains("Non-impinging", r.Notes[1]);
    }

    [Fact]
    public void Size_TinyOrifice_TripsLpbfFloorWarning()
    {
        // Pick a flow / ΔP combination that drives area below the
        // 0.30 mm² LPBF threshold. Very small per-element flow + very
        // high ΔP collapses A toward zero.
        var inp = MakeInputs(oxFlow_kgs: 1e-5, fuelFlow_kgs: 1e-5,
                             dP_Pa: 1e7);
        var r = new ShowerheadElement().Size(inp);

        Assert.True(r.OxOrificeArea_mm2 < 0.3,
            $"Expected ox area below LPBF floor; got {r.OxOrificeArea_mm2:F4} mm².");
        Assert.Contains(r.Notes, n => n.Contains("WARNING"));
        Assert.Contains(r.Notes, n => n.Contains("LPBF"));
    }

    [Fact]
    public void Size_NonPintleElement_LeavesBlockageFractionAtZero()
    {
        // ShowerheadElement is not a pintle; the OrificeResult.PintleBlockageFraction
        // field is short-circuited at zero so the PINTLE_BLOCKAGE_OUT_OF_BAND gate
        // ignores non-pintle elements.
        var r = new ShowerheadElement().Size(MakeInputs());
        Assert.Equal(0.0, r.PintleBlockageFraction, precision: 6);
    }

    [Fact]
    public void Size_AreaScalesAsSqrtRecipDensity()
    {
        // For fixed ṁ, ΔP, Cd: A ∝ 1/√ρ. Double the density,
        // area should drop by factor √2.
        var elem = new ShowerheadElement();
        var rA = elem.Size(MakeInputs(oxRho: 500.0, fuelRho: 500.0));
        var rB = elem.Size(MakeInputs(oxRho: 1000.0, fuelRho: 1000.0));

        double ratio = rA.OxOrificeArea_mm2 / rB.OxOrificeArea_mm2;
        Assert.Equal(System.Math.Sqrt(2.0), ratio, precision: 4);
    }

    [Fact]
    public void Size_VelocityScalesAsSqrtRecipDensity()
    {
        var elem = new ShowerheadElement();
        var rA = elem.Size(MakeInputs(oxRho: 500.0, fuelRho: 500.0));
        var rB = elem.Size(MakeInputs(oxRho: 1000.0, fuelRho: 1000.0));

        Assert.True(rA.OxVelocity_ms > rB.OxVelocity_ms);
        double ratio = rA.OxVelocity_ms / rB.OxVelocity_ms;
        Assert.Equal(System.Math.Sqrt(2.0), ratio, precision: 4);
    }
}
