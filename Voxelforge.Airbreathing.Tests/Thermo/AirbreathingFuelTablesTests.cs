// AirbreathingFuelTablesTests.cs — Sprint A3 acceptance for fuel tables.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Thermo;

namespace Voxelforge.Airbreathing.Tests.Thermo;

public sealed class AirbreathingFuelTablesTests
{
    [Fact]
    public void Hydrogen_Lookup_ReturnsCanonicalProperties()
    {
        var h2 = AirbreathingFuelTables.Lookup(AirbreathingFuel.H2);
        Assert.Equal(119_960_000.0, h2.LowerHeatingValue_J_kg, 0);
        Assert.InRange(h2.StoichiometricFuelAirRatio, 0.028, 0.030);
        Assert.InRange(h2.FormulaWeight_kg_kmol, 2.0, 2.05);
    }

    [Fact]
    public void JetA_Lookup_ReturnsKeroseneProperties()
    {
        var jetA = AirbreathingFuelTables.Lookup(AirbreathingFuel.JetA);
        Assert.InRange(jetA.LowerHeatingValue_J_kg, 42e6, 44e6);
        Assert.InRange(jetA.StoichiometricFuelAirRatio, 0.066, 0.070);
    }

    [Fact]
    public void Jp8_Lookup_ReturnsKeroseneProperties()
    {
        var jp8 = AirbreathingFuelTables.Lookup(AirbreathingFuel.Jp8);
        Assert.InRange(jp8.LowerHeatingValue_J_kg, 42e6, 44e6);
        Assert.InRange(jp8.StoichiometricFuelAirRatio, 0.066, 0.070);
    }
}
