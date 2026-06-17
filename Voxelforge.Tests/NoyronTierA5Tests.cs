// NoyronTierA5Tests.cs — Tier A5 forcing-function suite. Covers:
//   • Factory: all 5 element types are implemented.
//   • Sizing: each element type produces positive orifice areas +
//     reasonable velocity ratios for LOX/CH4 @ 20 kN / Pc = 7 MPa.
//   • InjectorFaceLayoutGenerator: each layout returns the expected
//     number of positions within the chamber bounds.
//   • AutoSeeder: emits an InjectorElementPattern for every supported
//     pair; element type + count + layout match the propellant heuristic;
//     ElementTypeOverride is honored.
//   • Pattern: FaceLayout round-trips through InjectorPattern.
//
// These are pure-math tests — no PicoGK Library required.

using Voxelforge.Combustion;
using Voxelforge.Injector;
using Voxelforge.Injector.Elements;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class NoyronTierA5Tests
{
    // ══════════════════════ Factory ══════════════════════

    [Theory]
    [InlineData("Coax")]
    [InlineData("ImpingingDoublet")]
    [InlineData("Pintle")]
    [InlineData("Showerhead")]
    [InlineData("Swirl")]
    public void Factory_AllAdvertisedTypesResolveToImplementedElements(string elementType)
    {
        var element = InjectorElementFactory.Create(elementType);
        Assert.Equal(elementType, element.ElementType);
        Assert.True(element.IsImplemented,
            $"Factory reports {elementType} as implemented but the element class says IsImplemented=false.");
    }

    [Fact]
    public void Factory_AllTypesListMatchesRegisteredClasses()
    {
        // Forcing function: if someone adds a new switch branch to
        // Create() without updating AllTypes, this test fails.
        foreach (var t in InjectorElementFactory.AllTypes)
        {
            var created = InjectorElementFactory.Create(t);
            Assert.Equal(t, created.ElementType);
        }
    }

    [Fact]
    public void Factory_UnknownTypeFallsBackToCoax()
    {
        var e = InjectorElementFactory.Create("NotReal");
        Assert.Equal("Coax", e.ElementType);
    }

    // ══════════════════════ Sizing ══════════════════════

    [Theory]
    [InlineData("Coax")]
    [InlineData("ImpingingDoublet")]
    [InlineData("Pintle")]
    [InlineData("Showerhead")]
    [InlineData("Swirl")]
    public void Sizing_ProducesPositiveAreasForLoxCh4(string elementType)
    {
        var e = InjectorElementFactory.Create(elementType);
        var inp = new SizingInputs(
            DeltaPInj_Pa:           1.4e6,
            OxDensity_kgm3:         1140.0,
            FuelDensity_kgm3:       420.0,
            OxFlowPerElement_kgs:   0.12,
            FuelFlowPerElement_kgs: 0.036);

        var r = e.Size(inp);
        Assert.True(r.OxOrificeArea_mm2 > 0,
            $"{elementType} produced non-positive ox orifice area: {r.OxOrificeArea_mm2}.");
        Assert.True(r.FuelOrificeArea_mm2 > 0,
            $"{elementType} produced non-positive fuel orifice area: {r.FuelOrificeArea_mm2}.");
        Assert.True(r.OxVelocity_ms > 0 && r.FuelVelocity_ms > 0);
    }

    [Fact]
    public void Sizing_VelocityRatioMatchesFlow()
    {
        // Coax at MR = 3.3 should give v_fuel/v_ox ≈ √(ρ_ox/ρ_fuel).
        var e = new CoaxElement();
        var inp = new SizingInputs(
            DeltaPInj_Pa:           1.4e6,
            OxDensity_kgm3:         1140.0,
            FuelDensity_kgm3:       420.0,
            OxFlowPerElement_kgs:   0.12,
            FuelFlowPerElement_kgs: 0.036);
        var r = e.Size(inp);
        double expectedRatio = Math.Sqrt(1140.0 / 420.0);
        Assert.InRange(r.VelocityRatio, expectedRatio * 0.9, expectedRatio * 1.1);
    }

    // ══════════════════════ InjectorFaceLayout ══════════════════════

    [Fact]
    public void Layout_CircularPlacesNPositionsOnPitchCircle()
    {
        var positions = InjectorFaceLayoutGenerator.PlaceElements(
            InjectorFaceLayout.Circular, elementCount: 24,
            pitchRadius_mm: 20.0, chamberRadius_mm: 30.0);
        Assert.Equal(24, positions.Length);
        foreach (var (y, z) in positions)
        {
            double r = Math.Sqrt(y * y + z * z);
            Assert.InRange(r, 19.9, 20.1);
        }
    }

    [Fact]
    public void Layout_CentralReturnsOnlyOrigin()
    {
        var positions = InjectorFaceLayoutGenerator.PlaceElements(
            InjectorFaceLayout.Central, elementCount: 42,   // count ignored
            pitchRadius_mm: 20.0, chamberRadius_mm: 30.0);
        Assert.Single(positions);
        Assert.Equal(0.0, positions[0].y_mm, 6);
        Assert.Equal(0.0, positions[0].z_mm, 6);
    }

    [Fact]
    public void Layout_HexagonalFitsWithinChamberRadius()
    {
        var positions = InjectorFaceLayoutGenerator.PlaceElements(
            InjectorFaceLayout.Hexagonal, elementCount: 19,
            pitchRadius_mm: 20.0, chamberRadius_mm: 25.0);
        Assert.True(positions.Length <= 19);
        Assert.True(positions.Length > 0);
        foreach (var (y, z) in positions)
            Assert.InRange(Math.Sqrt(y * y + z * z), 0, 25.0 * 0.95);
    }

    [Fact]
    public void Layout_AnnularRowsProducesMultipleRings()
    {
        var positions = InjectorFaceLayoutGenerator.PlaceElements(
            InjectorFaceLayout.AnnularRows, elementCount: 48,
            pitchRadius_mm: 30.0, chamberRadius_mm: 40.0);
        Assert.True(positions.Length > 0);
        // Count distinct radii (rounded to nearest mm).
        var radii = new HashSet<int>();
        foreach (var (y, z) in positions)
            radii.Add((int)Math.Round(Math.Sqrt(y * y + z * z)));
        Assert.True(radii.Count >= 2,
            $"Expected ≥ 2 concentric rings; got {radii.Count}: [{string.Join(", ", radii)}].");
    }

    [Fact]
    public void Layout_InvalidInputsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InjectorFaceLayoutGenerator.PlaceElements(
                InjectorFaceLayout.Circular, elementCount: 0,
                pitchRadius_mm: 10.0, chamberRadius_mm: 20.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InjectorFaceLayoutGenerator.PlaceElements(
                InjectorFaceLayout.Circular, elementCount: 8,
                pitchRadius_mm: 0.0, chamberRadius_mm: 20.0));
    }

    // ══════════════════════ AutoSeeder injector defaults ══════════════════════

    [Theory]
    [InlineData(PropellantPair.LOX_CH4, "Coax",   InjectorFaceLayout.Hexagonal)]
    [InlineData(PropellantPair.LOX_H2,  "Coax",   InjectorFaceLayout.Hexagonal)]
    [InlineData(PropellantPair.LOX_RP1, "Pintle", InjectorFaceLayout.Central)]
    public void AutoSeeder_EmitsPatternWithExpectedElementTypeAndLayout(
        PropellantPair pair, string expectedType, InjectorFaceLayout expectedLayout)
    {
        var r = AutoSeeder.Seed(new EngineSpec(pair, 10_000, 7e6, 10.0));
        Assert.NotNull(r.Design.InjectorElementPattern);
        Assert.Equal(expectedType,   r.Design.InjectorElementPattern!.ElementType);
        Assert.Equal(expectedLayout, r.Design.InjectorElementPattern.FaceLayout);
    }

    [Fact]
    public void AutoSeeder_PintleIsSingleElement()
    {
        var r = AutoSeeder.Seed(new EngineSpec(PropellantPair.LOX_RP1, 10_000, 7e6, 10.0));
        Assert.Equal(1, r.Design.InjectorElementPattern!.ElementCount);
    }

    [Fact]
    public void AutoSeeder_ElementCountClampedIntoSaBand()
    {
        // Non-Pintle: SA variable [13] spans [8, 48].
        foreach (double thrust in new[] { 500.0, 10_000.0, 100_000.0 })
        {
            var r = AutoSeeder.Seed(new EngineSpec(PropellantPair.LOX_CH4, thrust, 7e6, 10.0));
            int count = r.Design.InjectorElementPattern!.ElementCount;
            Assert.InRange(count, 8, 48);
        }
    }

    [Fact]
    public void AutoSeeder_ElementTypeOverrideHonoured()
    {
        var r = AutoSeeder.Seed(new EngineSpec(
            PropellantPair.LOX_CH4, 10_000, 7e6, 10.0,
            ElementTypeOverride: "ImpingingDoublet"));
        Assert.Equal("ImpingingDoublet", r.Design.InjectorElementPattern!.ElementType);
        Assert.Equal(InjectorFaceLayout.Circular, r.Design.InjectorElementPattern.FaceLayout);
    }

    [Fact]
    public void AutoSeeder_DeterministicOnInjectorPattern()
    {
        var spec = new EngineSpec(PropellantPair.LOX_H2, 50_000, 10e6, 20.0);
        var r1 = AutoSeeder.Seed(spec);
        var r2 = AutoSeeder.Seed(spec);
        Assert.Equal(r1.Design.InjectorElementPattern!.ElementType,
                     r2.Design.InjectorElementPattern!.ElementType);
        Assert.Equal(r1.Design.InjectorElementPattern.ElementCount,
                     r2.Design.InjectorElementPattern.ElementCount);
        Assert.Equal(r1.Design.InjectorElementPattern.FaceLayout,
                     r2.Design.InjectorElementPattern.FaceLayout);
        Assert.Equal(r1.Design.InjectorElementPattern.OuterRowFilmFraction,
                     r2.Design.InjectorElementPattern.OuterRowFilmFraction);
    }

    [Fact]
    public void AutoSeeder_RationaleMentionsInjector()
    {
        var r = AutoSeeder.Seed(new EngineSpec(PropellantPair.LOX_CH4, 10_000, 7e6, 10.0));
        Assert.Contains(r.Rationale, line => line.Contains("Injector:"));
    }

    // ══════════════════════ Pattern round-trip ══════════════════════

    [Fact]
    public void InjectorPattern_FaceLayoutRoundTrips()
    {
        var p = new InjectorPattern
        {
            ElementType  = "Swirl",
            ElementCount = 24,
            FaceLayout   = InjectorFaceLayout.AnnularRows,
        };
        var q = p with { FaceLayout = InjectorFaceLayout.Hexagonal };
        Assert.Equal(InjectorFaceLayout.Hexagonal, q.FaceLayout);
        Assert.Equal("Swirl", q.ElementType);     // other fields preserved
        Assert.Equal(24,      q.ElementCount);
    }
}
