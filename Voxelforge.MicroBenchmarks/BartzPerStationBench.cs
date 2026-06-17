// BB-4 (2026-04-29): Bartz per-station microbenches. Pre-PH-5 reference
// — when the audit's per-station r_curv (currently a single throat
// radius) lands, this baseline tells us how much the new shape changes
// the hot-path cost.

using BenchmarkDotNet.Attributes;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class BartzPerStationBench
{
    private PropellantState _gas;
    private const double ThroatDiameter_m = 0.040;     // 40 mm throat — nominal small chamber
    private const double ThroatCurvature_m = 0.030;    // 1.5× throat radius — typical
    private const double AreaRatio = 0.85;             // mid-chamber station
    private const double LocalMach = 0.18;             // chamber subsonic
    private const double WallTempGas_K = 950.0;        // hot wall
    private double _h_g_cached;

    [GlobalSetup]
    public void Setup()
    {
        _gas = PropellantTables.Lookup(PropellantPair.LOX_CH4, mixtureRatio: 3.3, chamberPressure_Pa: 6.9e6);
        _h_g_cached = BartzHeatFlux.HeatTransferCoefficient(
            in _gas, ThroatDiameter_m, ThroatCurvature_m, AreaRatio,
            LocalMach, WallTempGas_K);
    }

    [Benchmark]
    public double HeatTransferCoefficient_ChamberStation()
        => BartzHeatFlux.HeatTransferCoefficient(
            in _gas, ThroatDiameter_m, ThroatCurvature_m, AreaRatio,
            LocalMach, WallTempGas_K);

    [Benchmark]
    public double HeatTransferCoefficient_ThroatStation()
        => BartzHeatFlux.HeatTransferCoefficient(
            in _gas, ThroatDiameter_m, ThroatCurvature_m,
            areaRatioToThroat: 1.0, localMach: 1.0, wallTempGas_K: WallTempGas_K);

    [Benchmark]
    public double AccelerationParameter()
        => BartzHeatFlux.AccelerationParameter(
            in _gas, LocalMach, T_static_K: 2800.0, velocityGradient_1ps: 1500.0);

    [Benchmark]
    public double HeatFlux()
        => BartzHeatFlux.HeatFlux(_h_g_cached, T_aw_K: 3100.0, T_wg_K: WallTempGas_K);
}
