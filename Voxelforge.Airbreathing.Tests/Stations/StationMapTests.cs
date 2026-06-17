// StationMapTests.cs — coverage for the SAE AS755 station-numbering
// data carrier. Audit 05-test-gaps.md Section 2 High.
//
// StationMap is the station-by-station thermodynamic state every
// air-breathing cycle solver produces. Existing tests consume it
// implicitly via cycle-solver round-trips; this file targets the type
// directly.

using System;
using System.Collections.Generic;
using Voxelforge.Airbreathing.Stations;

namespace Voxelforge.Airbreathing.Tests.Stations;

public sealed class StationMapTests
{
    private static StationState Some(double t = 300.0, double p = 100_000.0) =>
        new(StagnationT_K: t, StagnationP_Pa: p, MassFlow_kg_s: 1.0, MachNumber: 0.0);

    private static List<StationState> TenStations()
    {
        var list = new List<StationState>(10);
        for (int i = 0; i < 10; i++) list.Add(Some(300.0 + i * 100.0));
        return list;
    }

    [Fact]
    public void Ctor_StoresAllFourFields()
    {
        var stations = TenStations();
        var map = new StationMap(
            Stations:           stations,
            ThrustNet_N:        12_500.0,
            SpecificImpulse_s:  340.0,
            FuelMassFlow_kg_s:  0.05);

        Assert.Same(stations, map.Stations);
        Assert.Equal(12_500.0, map.ThrustNet_N,        precision: 6);
        Assert.Equal(340.0,    map.SpecificImpulse_s,  precision: 6);
        Assert.Equal(0.05,     map.FuelMassFlow_kg_s,  precision: 6);
    }

    [Fact]
    public void Station_ReturnsStationStateByIndex()
    {
        var stations = TenStations();
        var map = new StationMap(stations, 0.0, 0.0, 0.0);
        Assert.Equal(stations[0], map.Station(0));
        Assert.Equal(stations[9], map.Station(9));
    }

    [Fact]
    public void Station_NegativeIndex_ThrowsArgumentOutOfRange()
    {
        var map = new StationMap(TenStations(), 0.0, 0.0, 0.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Station(-1));
    }

    [Fact]
    public void Station_OutOfRangeIndex_ThrowsArgumentOutOfRange()
    {
        var map = new StationMap(TenStations(), 0.0, 0.0, 0.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Station(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Station(100));
    }

    [Fact]
    public void Station_NullArray_ThrowsInvalidOperation()
    {
        var map = new StationMap(null!, 0.0, 0.0, 0.0);
        Assert.Throws<InvalidOperationException>(() => map.Station(0));
    }

    [Fact]
    public void Station_SupportsTurbofanExtendedLength17()
    {
        // Sprint A8: turbofan extends to 17 entries (indices 0-16) so
        // stations 13 + 16 (fan exit + bypass duct exit) become valid.
        var list = new List<StationState>(17);
        for (int i = 0; i < 17; i++) list.Add(Some());
        var map = new StationMap(list, 0.0, 0.0, 0.0);
        Assert.Equal(list[13], map.Station(13));
        Assert.Equal(list[16], map.Station(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Station(17));
    }

    [Fact]
    public void StationListExtensions_Station_MirrorsMapBehaviour()
    {
        var stations = TenStations();
        Assert.Equal(stations[5], ((IReadOnlyList<StationState>)stations).Station(5));
    }

    [Fact]
    public void StationListExtensions_Station_NullList_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IReadOnlyList<StationState>)null!).Station(0));
    }

    [Fact]
    public void StationListExtensions_Station_OutOfRange_ThrowsArgumentOutOfRange()
    {
        var stations = TenStations();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((IReadOnlyList<StationState>)stations).Station(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((IReadOnlyList<StationState>)stations).Station(10));
    }

    [Fact]
    public void StationState_NaNTemperature_PropagatesToEnthalpy()
    {
        // Per documented behaviour: NaN T_K yields NaN enthalpy.
        var s = new StationState(double.NaN, 100_000.0, 1.0, 0.0);
        Voxelforge.Engines.IThermodynamicState contract = s;
        Assert.True(double.IsNaN(contract.Enthalpy_Jkg));
    }

    [Fact]
    public void StationState_NaNTemperature_DegeneratesDensityToZero()
    {
        var s = new StationState(double.NaN, 100_000.0, 1.0, 0.0);
        Voxelforge.Engines.IThermodynamicState contract = s;
        Assert.Equal(0.0, contract.Density_kgm3, precision: 9);
    }

    [Fact]
    public void StationState_ZeroOrNegativeTemperature_DegeneratesDensityToZero()
    {
        var s = new StationState(0.0, 100_000.0, 1.0, 0.0);
        Voxelforge.Engines.IThermodynamicState contract = s;
        Assert.Equal(0.0, contract.Density_kgm3, precision: 9);
    }

    [Fact]
    public void StationState_NominalState_HasFiniteEnthalpyAndDensity()
    {
        var s = new StationState(StagnationT_K: 300.0, StagnationP_Pa: 100_000.0,
                                 MassFlow_kg_s: 1.0,  MachNumber: 0.0);
        Voxelforge.Engines.IThermodynamicState contract = s;
        Assert.True(double.IsFinite(contract.Enthalpy_Jkg));
        Assert.True(contract.Density_kgm3 > 0.0);
        // ρ = P / (R · T) with R = 287.05.
        Assert.InRange(contract.Density_kgm3, 1.10, 1.25);
    }
}
