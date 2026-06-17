// BB-4 (2026-04-29): CoolantCorrelations microbenches. Each public
// method gets its own [Benchmark] so future PRs touching the
// correlation registry (PH-6 Dean-number, PH-7 Haaland, PH-39 Pizzarelli
// auto-select) can compare against a frozen pre-cascade reference.
//
// Inputs are constructed once in [GlobalSetup] from the canonical
// LOX/CH4 + RP-1-coolant pairing (matches the default OperatingConditions).
// Per-bench mutation is unnecessary: the correlation methods are pure
// functions of their arguments, so JIT cannot fold them away across
// invocations.

using BenchmarkDotNet.Attributes;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.HeatTransfer;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class CoolantCorrelationsBench
{
    private CoolantState _bulk;
    private CoolantState _wall;
    private CoolantNusseltFactors _factors;
    private const double Velocity_ms = 25.0;
    private const double HydraulicDiameter_m = 0.0025;
    private const double CurvatureRadius_m = 0.020;
    private const double LpbfRoughness = 0.02;
    private double _Re;
    private ICoolantFluid _fluid = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fluid = CoolantRegistry.Get(PropellantPairs.GetMeta(PropellantPair.LOX_CH4).CoolantFluidKey);
        _bulk = _fluid.GetState(T_K: 350.0, P_Pa: 12e6);
        _wall = _fluid.GetState(T_K: 600.0, P_Pa: 12e6);
        _factors = CoolantCorrelations.ComputeNusseltFactors(
            in _bulk, Velocity_ms, HydraulicDiameter_m);
        _Re = CoolantCorrelations.ReynoldsNumber(in _bulk, Velocity_ms, HydraulicDiameter_m);
    }

    [Benchmark]
    public double HeatTransferCoefficient_Legacy_SiederTate()
        => CoolantCorrelations.HeatTransferCoefficient(
            in _bulk, in _wall, Velocity_ms, HydraulicDiameter_m,
            CoolantCorrelationKind.SiederTate);

    [Benchmark]
    public double HeatTransferCoefficient_Hoisted_SiederTate()
        => CoolantCorrelations.HeatTransferCoefficient(
            in _factors, in _bulk, in _wall, CoolantCorrelationKind.SiederTate);

    [Benchmark]
    public double HeatTransferCoefficient_Hoisted_DittusBoelter()
        => CoolantCorrelations.HeatTransferCoefficient(
            in _factors, in _bulk, in _wall, CoolantCorrelationKind.DittusBoelter);

    [Benchmark]
    public double HeatTransferCoefficient_Hoisted_Pizzarelli()
        => CoolantCorrelations.HeatTransferCoefficient(
            in _factors, in _bulk, in _wall, CoolantCorrelationKind.SupercriticalPizzarelli);

    [Benchmark]
    public CoolantNusseltFactors ComputeNusseltFactors_Bulk()
        => CoolantCorrelations.ComputeNusseltFactors(in _bulk, Velocity_ms, HydraulicDiameter_m);

    [Benchmark]
    public CoolantCorrelationKind AutoSelectKind_LoxCh4_NominalBulk()
        => CoolantCorrelations.AutoSelectKind(in _bulk, _fluid, CoolantCorrelationKind.SiederTate);

    [Benchmark]
    public double FrictionFactor_Smooth()
        => CoolantCorrelations.FrictionFactor(_Re);

    [Benchmark]
    public double FrictionFactor_Haaland_LpbfRoughness()
        => CoolantCorrelations.FrictionFactor(_Re, LpbfRoughness);

    [Benchmark]
    public double PressureGradient_Smooth()
        => CoolantCorrelations.PressureGradient(in _bulk, Velocity_ms, HydraulicDiameter_m);

    [Benchmark]
    public double PressureGradient_LpbfRoughness()
        => CoolantCorrelations.PressureGradient(in _bulk, Velocity_ms, HydraulicDiameter_m, LpbfRoughness);

    [Benchmark]
    public double DeanNumberNuMultiplier_HelicalChannel()
        => CoolantCorrelations.DeanNumberNuMultiplier(HydraulicDiameter_m, CurvatureRadius_m);

    [Benchmark]
    public double ReynoldsNumber()
        => CoolantCorrelations.ReynoldsNumber(in _bulk, Velocity_ms, HydraulicDiameter_m);
}
