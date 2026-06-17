// IThermodynamicStateContractTests.cs — Sprint A8 contract tests for
// IThermodynamicState across all three concrete implementations:
//   • StationState      (Voxelforge.Airbreathing.Stations)
//   • CoolantState      (Voxelforge.Coolant)
//   • CombustionProductState (Voxelforge.Combustion)
//
// 5 tests per struct = 15 tests total.
// Tests verify: instantiation, Temperature_K, Pressure_Pa,
// Density_kgm3 via the interface, and that the interface reference works.

using Voxelforge.Airbreathing.Stations;
using Voxelforge.Coolant;
using Voxelforge.Combustion;
using Voxelforge.Engines;

namespace Voxelforge.Airbreathing.Tests;

// ── StationState ──────────────────────────────────────────────────────────

public sealed class StationState_IThermodynamicStateTests
{
    private static StationState Make()
        => new(StagnationT_K: 500.0, StagnationP_Pa: 200_000.0, MassFlow_kg_s: 10.0, MachNumber: 0.3);

    [Fact]
    public void StationState_Instantiates()
    {
        var s = Make();
        Assert.Equal(500.0, s.StagnationT_K);
    }

    [Fact]
    public void StationState_Interface_Temperature_K()
    {
        IThermodynamicState s = Make();
        Assert.Equal(500.0, s.Temperature_K);
    }

    [Fact]
    public void StationState_Interface_Pressure_Pa()
    {
        IThermodynamicState s = Make();
        Assert.Equal(200_000.0, s.Pressure_Pa);
    }

    [Fact]
    public void StationState_Interface_Density_kgm3_PositiveForValidState()
    {
        IThermodynamicState s = Make();
        // ρ = P / (R_air · T) = 200000 / (287.05 · 500) ≈ 1.394 kg/m³
        Assert.True(s.Density_kgm3 > 0, $"Density should be positive; got {s.Density_kgm3}");
    }

    [Fact]
    public void StationState_Interface_Reference_Works()
    {
        StationState concrete = Make();
        IThermodynamicState iface = concrete;
        Assert.Equal(concrete.StagnationT_K, iface.Temperature_K);
        Assert.Equal(concrete.StagnationP_Pa, iface.Pressure_Pa);
    }
}

// ── CoolantState ─────────────────────────────────────────────────────────

public sealed class CoolantState_IThermodynamicStateTests
{
    private static CoolantState Make()
        => new(
            T_K:               300.0,
            P_Pa:              10_000_000.0,
            Density_kgm3:      450.0,
            Cp_Jkg:            2500.0,
            Viscosity_PaS:     0.0001,
            Conductivity_WmK:  0.18,
            Prandtl:           1.4,
            Enthalpy_Jkg:      600_000.0);

    [Fact]
    public void CoolantState_Instantiates()
    {
        var s = Make();
        Assert.Equal(300.0, s.T_K);
    }

    [Fact]
    public void CoolantState_Interface_Temperature_K()
    {
        IThermodynamicState s = Make();
        Assert.Equal(300.0, s.Temperature_K);
    }

    [Fact]
    public void CoolantState_Interface_Pressure_Pa()
    {
        IThermodynamicState s = Make();
        Assert.Equal(10_000_000.0, s.Pressure_Pa);
    }

    [Fact]
    public void CoolantState_Interface_Density_kgm3()
    {
        IThermodynamicState s = Make();
        Assert.Equal(450.0, s.Density_kgm3);
    }

    [Fact]
    public void CoolantState_Interface_Reference_Works()
    {
        CoolantState concrete = Make();
        IThermodynamicState iface = concrete;
        Assert.Equal(concrete.T_K,            iface.Temperature_K);
        Assert.Equal(concrete.P_Pa,           iface.Pressure_Pa);
        Assert.Equal(concrete.Enthalpy_Jkg,   iface.Enthalpy_Jkg);
        Assert.Equal(concrete.Density_kgm3,   iface.Density_kgm3);
    }
}

// ── CombustionProductState ────────────────────────────────────────────────

public sealed class CombustionProductState_IThermodynamicStateTests
{
    private static CombustionProductState Make()
        => new(
            Temperature_K:    3200.0,
            Pressure_Pa:      6_000_000.0,
            Enthalpy_Jkg:     4_000_000.0,
            Density_kgm3:     8.5,
            MolarMass_kgkmol: 22.3,
            GammaEffective:   1.21);

    [Fact]
    public void CombustionProductState_Instantiates()
    {
        var s = Make();
        Assert.Equal(3200.0, s.Temperature_K);
    }

    [Fact]
    public void CombustionProductState_Interface_Temperature_K()
    {
        IThermodynamicState s = Make();
        Assert.Equal(3200.0, s.Temperature_K);
    }

    [Fact]
    public void CombustionProductState_Interface_Pressure_Pa()
    {
        IThermodynamicState s = Make();
        Assert.Equal(6_000_000.0, s.Pressure_Pa);
    }

    [Fact]
    public void CombustionProductState_Interface_Density_kgm3()
    {
        IThermodynamicState s = Make();
        Assert.Equal(8.5, s.Density_kgm3);
    }

    [Fact]
    public void CombustionProductState_Interface_Reference_Works()
    {
        CombustionProductState concrete = Make();
        IThermodynamicState iface = concrete;
        Assert.Equal(concrete.Temperature_K,  iface.Temperature_K);
        Assert.Equal(concrete.Pressure_Pa,    iface.Pressure_Pa);
        Assert.Equal(concrete.Enthalpy_Jkg,   iface.Enthalpy_Jkg);
        Assert.Equal(concrete.Density_kgm3,   iface.Density_kgm3);
        Assert.Equal(0.0,                     iface.Entropy_JkgK);
    }
}
