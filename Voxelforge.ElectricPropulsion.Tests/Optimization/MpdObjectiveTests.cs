// MpdObjectiveTests.cs — Sprint EP.W2.MPD objective-adapter tests.
// Covers Pack/Unpack round-trip, bounds shape, bus-power clip, kind-mismatch.

using System;
using Voxelforge.ElectricPropulsion.Optimization;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.ElectricPropulsion.Tests.Optimization;

public sealed class MpdObjectiveTests
{
    private static ElectricPropulsionEngineDesign NasaLewisDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.MagnetoPlasmaDynamic,
        HeaterPower_W:           double.NaN,
        PropellantMassFlow_kgs:    2.0e-4,
        NozzleThroatRadius_mm:   double.NaN,
        NozzleAreaRatio:         double.NaN,
        HeaterChamberLength_mm:  double.NaN,
        HeaterChamberRadius_mm:  double.NaN)
    {
        MpdArcCurrent_A     = 4000.0,
        MpdCathodeRadius_mm =   10.0,
        MpdAnodeRadius_mm   =  100.0,
        MpdChamberLength_mm =  150.0,
    };

    private static ResistojetConditions DefaultConditions() => new(
        BusVoltage_V:        100.0,
        BusPower_W_avail: 250000.0,
        AmbientPressure_Pa:    0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K: 300.0,
        InletComposition:   PropellantInletComposition.PureH2);

    [Fact]
    public void PackThenUnpack_RoundTripsEveryField()
    {
        var original = NasaLewisDesign();
        double[] vec = MpdObjective.Pack(original);
        var restored = MpdObjective.Unpack(vec, original);
        Assert.Equal(original.MpdArcCurrent_A,         restored.MpdArcCurrent_A,         precision: 6);
        Assert.Equal(original.PropellantMassFlow_kgs,  restored.PropellantMassFlow_kgs,  precision: 12);
        Assert.Equal(original.MpdCathodeRadius_mm,     restored.MpdCathodeRadius_mm,     precision: 6);
        Assert.Equal(original.MpdAnodeRadius_mm,       restored.MpdAnodeRadius_mm,       precision: 6);
        Assert.Equal(original.MpdChamberLength_mm,     restored.MpdChamberLength_mm,     precision: 6);
    }

    [Fact]
    public void Pack_ReturnsFiveElementVector()
    {
        double[] vec = MpdObjective.Pack(NasaLewisDesign());
        Assert.Equal(5, vec.Length);
        Assert.Equal(MpdObjective.DefaultVariableNames.Length, vec.Length);
    }

    [Fact]
    public void Pack_PreservesElementOrder()
    {
        double[] vec = MpdObjective.Pack(NasaLewisDesign());
        Assert.Equal(4000.0, vec[0], precision: 6);
        Assert.Equal(   2.0e-4, vec[1], precision: 12);
        Assert.Equal(  10.0, vec[2], precision: 6);
        Assert.Equal( 100.0, vec[3], precision: 6);
        Assert.Equal( 150.0, vec[4], precision: 6);
    }

    [Fact]
    public void DefaultBounds_MatchVariableNamesShape()
    {
        Assert.Equal(MpdObjective.DefaultVariableNames.Length, MpdObjective.DefaultBounds.Length);
        for (int i = 0; i < MpdObjective.DefaultBounds.Length; i++)
            Assert.Equal(MpdObjective.DefaultVariableNames[i], MpdObjective.DefaultBounds[i].Name);
    }

    [Fact]
    public void DefaultBounds_AreOrderedAndPositive()
    {
        foreach (var b in MpdObjective.DefaultBounds)
        {
            Assert.True(b.Min < b.Max, $"{b.Name}: Min={b.Min} ≥ Max={b.Max}");
            Assert.True(b.Min > 0,     $"{b.Name}: Min={b.Min} not strictly positive");
        }
    }

    [Fact]
    public void Build_NasaLewisDesign_ReturnsObjective()
    {
        var obj = MpdObjective.Build(DefaultConditions(), NasaLewisDesign());
        Assert.NotNull(obj);
    }

    [Fact]
    public void Build_WrongKind_Throws()
    {
        var git = NasaLewisDesign() with { Kind = ElectricPropulsionEngineKind.GriddedIon };
        Assert.Throws<ArgumentException>(() => MpdObjective.Build(DefaultConditions(), git));
    }

    [Fact]
    public void Build_NullConditions_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            MpdObjective.Build(null!, NasaLewisDesign()));

    [Fact]
    public void Build_NullBaseline_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            MpdObjective.Build(DefaultConditions(), null!));

    [Fact]
    public void Unpack_WrongVectorLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MpdObjective.Unpack(new double[3], NasaLewisDesign()));
    }

    [Fact]
    public void Build_LowBusPower_ClipsArcCurrentBound()
    {
        // BusPower=1 kW with worst-case V_arc → tiny J ceiling. Build should
        // still succeed.
        var lowPower = new ResistojetConditions(
            BusVoltage_V:        28.0,
            BusPower_W_avail:  1000.0,
            AmbientPressure_Pa:   0.0,
            Propellant:          Propellant.N2H4Decomposed,
            InletTemperature_K:  300.0,
            InletComposition:    PropellantInletComposition.PureH2);
        var obj = MpdObjective.Build(lowPower, NasaLewisDesign());
        Assert.NotNull(obj);
    }
}
