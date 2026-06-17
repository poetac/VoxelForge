// PulsejetCycleSolverTests.cs — Wave 1 PR-4 (sub-step 1a.5).
// Smoke + contract tests for PulsejetCycleSolver.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class PulsejetCycleSolverTests
{
    private static AirbreathingEngineDesign V1ReferenceDesign() =>
        new AirbreathingEngineDesign(
            Kind:                AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:  0.030,
            CombustorArea_m2:    0.075,
            CombustorLength_m:   0.80,
            NozzleThroatArea_m2: 0.025,
            NozzleExitArea_m2:   0.040,
            EquivalenceRatio:    0.95,
            CompressorPressureRatio: 1.0)
        with
        {
            PulsejetTubeLength_m    = 3.40,
            PulsejetIntakeArea_m2   = 0.030,
            PulsejetTailpipeArea_m2 = 0.040,
        };

    private static FlightConditions SeaLevelStaticJp8() =>
        new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    [Fact]
    public void Solve_V1ReferenceStatic_ProducesPositiveThrustAndIsp()
    {
        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());

        Assert.NotNull(result);
        Assert.True(result.Stations.ThrustNet_N > 0,
            $"Expected positive thrust at V-1 nominal; got {result.Stations.ThrustNet_N}");
        Assert.True(result.Stations.SpecificImpulse_s > 0,
            $"Expected positive Isp at V-1 nominal; got {result.Stations.SpecificImpulse_s}");
        // Pulsejet has no rotating turbomachinery — both diagnostics null.
        Assert.Null(result.CompressorDiagnostics);
        Assert.Null(result.TurbineDiagnostics);
    }

    [Fact]
    public void Solve_V1ReferenceStatic_AirMassFlowInRealisticRange()
    {
        // V-1 air mass flow estimates in literature span 1–5 kg/s
        // depending on the source; the η_vol = 0.14 calibration in
        // PulsejetCycleSolver targets 1.5–2.0 kg/s to match the ~3 kN
        // static thrust at the energy-balance V_9.
        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());

        var station0 = result.Stations.Station(0);
        Assert.InRange(station0.MassFlow_kg_s, 1.0, 5.0);
    }

    [Fact]
    public void Solve_KindMismatch_ThrowsArgumentException()
    {
        var solver = new PulsejetCycleSolver();
        var ramjetDesign = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Ramjet,
            0.030, 0.075, 0.80, 0.025, 0.040, 0.95);
        Assert.Throws<ArgumentException>(
            () => solver.Solve(ramjetDesign, SeaLevelStaticJp8()));
    }

    [Fact]
    public void Solve_NullDesign_ThrowsArgumentNullException()
    {
        var solver = new PulsejetCycleSolver();
        Assert.Throws<ArgumentNullException>(
            () => solver.Solve(null!, SeaLevelStaticJp8()));
    }

    [Fact]
    public void Solve_PopulatesStationDiscipline()
    {
        // Ramjet pattern: 0/1/2/4/5/8/9 populated, 3/6/7 NaN.
        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());
        var stations = result.Stations.Stations;

        Assert.False(double.IsNaN(stations[0].StagnationT_K));
        Assert.False(double.IsNaN(stations[1].StagnationT_K));
        Assert.False(double.IsNaN(stations[2].StagnationT_K));
        Assert.True(double.IsNaN(stations[3].StagnationT_K), "Station 3 (compressor) should be NaN for pulsejet");
        Assert.False(double.IsNaN(stations[4].StagnationT_K));
        Assert.False(double.IsNaN(stations[5].StagnationT_K));
        Assert.True(double.IsNaN(stations[6].StagnationT_K), "Station 6 (afterburner) should be NaN for pulsejet");
        Assert.True(double.IsNaN(stations[7].StagnationT_K), "Station 7 (afterburner) should be NaN for pulsejet");
        Assert.False(double.IsNaN(stations[8].StagnationT_K));
        Assert.False(double.IsNaN(stations[9].StagnationT_K));
    }

    [Fact]
    public void Solve_RegisteredInDispatcher()
    {
        // Wave 1 PR-4 registers PulsejetCycleSolver in the dispatch table.
        Assert.True(AirbreathingCycleSolvers.IsSupported(AirbreathingEngineKind.Pulsejet));
        var solver = AirbreathingCycleSolvers.Get(AirbreathingEngineKind.Pulsejet);
        Assert.IsType<PulsejetCycleSolver>(solver);
    }

    [Fact]
    public void Solve_Deterministic()
    {
        // Two invocations with identical inputs must produce bit-identical results.
        var solver = new PulsejetCycleSolver();
        var r1 = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());
        var r2 = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());
        Assert.Equal(r1.Stations.ThrustNet_N, r2.Stations.ThrustNet_N);
        Assert.Equal(r1.Stations.SpecificImpulse_s, r2.Stations.SpecificImpulse_s);
        Assert.Equal(r1.Stations.FuelMassFlow_kg_s, r2.Stations.FuelMassFlow_kg_s);
    }

    // ── Valveless variant tests (sub-step 1a.5 polish, issue #415) ────────────

    private static AirbreathingEngineDesign ValvelessDesign() =>
        V1ReferenceDesign() with { PulsejetVariant = PulsejetVariant.Valveless };

    [Fact]
    public void ValvelessVariant_ProducesPositiveThrustAndIsp()
    {
        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(ValvelessDesign(), SeaLevelStaticJp8());

        Assert.True(result.Stations.ThrustNet_N > 0,
            $"Valveless design should produce positive thrust; got {result.Stations.ThrustNet_N}");
        Assert.True(result.Stations.SpecificImpulse_s > 0,
            $"Valveless design should produce positive Isp; got {result.Stations.SpecificImpulse_s}");
        Assert.Null(result.CompressorDiagnostics);
        Assert.Null(result.TurbineDiagnostics);
    }

    [Fact]
    public void ValvelessVariant_ProducesLessThrust_ThanStandard()
    {
        // Valveless η_vol = 0.10 < Standard η_vol = 0.14 → lower ṁ_a →
        // lower thrust at identical geometry and flight conditions.
        var solver = new PulsejetCycleSolver();
        var standard  = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());
        var valveless = solver.Solve(ValvelessDesign(),   SeaLevelStaticJp8());

        Assert.True(valveless.Stations.ThrustNet_N < standard.Stations.ThrustNet_N,
            $"Valveless thrust ({valveless.Stations.ThrustNet_N:F1} N) should be "
          + $"less than Standard thrust ({standard.Stations.ThrustNet_N:F1} N).");
    }

    [Fact]
    public void ValvelessVariant_LowerAirMassFlow_ThanStandard()
    {
        // η_vol reduction feeds directly into ṁ_a at static conditions.
        var solver = new PulsejetCycleSolver();
        var standard  = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());
        var valveless = solver.Solve(ValvelessDesign(),   SeaLevelStaticJp8());

        double mdot_std = standard.Stations.Station(0).MassFlow_kg_s;
        double mdot_vl  = valveless.Stations.Station(0).MassFlow_kg_s;

        Assert.True(mdot_vl < mdot_std,
            $"Valveless ṁ_a ({mdot_vl:F4} kg/s) should be less than "
          + $"Standard ṁ_a ({mdot_std:F4} kg/s) at static conditions.");
    }

    [Fact]
    public void ValvelessVariant_Deterministic()
    {
        var solver = new PulsejetCycleSolver();
        var r1 = solver.Solve(ValvelessDesign(), SeaLevelStaticJp8());
        var r2 = solver.Solve(ValvelessDesign(), SeaLevelStaticJp8());
        Assert.Equal(r1.Stations.ThrustNet_N,       r2.Stations.ThrustNet_N);
        Assert.Equal(r1.Stations.SpecificImpulse_s, r2.Stations.SpecificImpulse_s);
        Assert.Equal(r1.Stations.FuelMassFlow_kg_s, r2.Stations.FuelMassFlow_kg_s);
    }

    [Fact]
    public void ValvelessVariant_StationDiscipline_Unchanged()
    {
        // Valveless uses the same station layout as Standard: 3/6/7 NaN, rest populated.
        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(ValvelessDesign(), SeaLevelStaticJp8());
        var stations = result.Stations.Stations;

        Assert.False(double.IsNaN(stations[0].StagnationT_K));
        Assert.False(double.IsNaN(stations[1].StagnationT_K));
        Assert.False(double.IsNaN(stations[2].StagnationT_K));
        Assert.True(double.IsNaN(stations[3].StagnationT_K), "Station 3 (compressor) should be NaN for valveless pulsejet");
        Assert.False(double.IsNaN(stations[4].StagnationT_K));
        Assert.False(double.IsNaN(stations[5].StagnationT_K));
        Assert.True(double.IsNaN(stations[6].StagnationT_K), "Station 6 (afterburner) should be NaN for valveless pulsejet");
        Assert.True(double.IsNaN(stations[7].StagnationT_K), "Station 7 (afterburner) should be NaN for valveless pulsejet");
        Assert.False(double.IsNaN(stations[8].StagnationT_K));
        Assert.False(double.IsNaN(stations[9].StagnationT_K));
    }

    [Fact]
    public void ValvelessVariant_WithExpressionRoundTrips()
    {
        // `design with { PulsejetVariant = Valveless }` must not lose Kind
        // and must not throw in the solver.
        var design = V1ReferenceDesign() with { PulsejetVariant = PulsejetVariant.Valveless };
        Assert.Equal(AirbreathingEngineKind.Pulsejet, design.Kind);
        Assert.Equal(PulsejetVariant.Valveless, design.PulsejetVariant);

        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(design, SeaLevelStaticJp8());
        Assert.True(result.Stations.ThrustNet_N > 0);
    }

    [Fact]
    public void EtaVolConstants_MatchCalibration()
    {
        // Guard against accidental constant drift.
        Assert.Equal(0.14, PulsejetCycleSolver.StaticVolumetricEfficiency,  precision: 10);
        Assert.Equal(0.10, PulsejetCycleSolver.ValvelessVolumetricEfficiency, precision: 10);
        // Valveless is strictly lower (physically motivated).
        Assert.True(PulsejetCycleSolver.ValvelessVolumetricEfficiency
                  < PulsejetCycleSolver.StaticVolumetricEfficiency);
    }

    // ── EstimatedBuzzFrequency_Hz (issue #451) ────────────────────────────────

    [Fact]
    public void Solve_Standard_PopulatesEstimatedBuzzFrequency_NearV1Published()
    {
        var solver = new PulsejetCycleSolver();
        var result = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());

        Assert.False(double.IsNaN(result.EstimatedBuzzFrequency_Hz),
            "EstimatedBuzzFrequency_Hz must be populated for a firing pulsejet.");
        // V-1 published 47 Hz; Foa §11.3 estimator predicts ~45 Hz at calibrated geometry.
        // Loose ±10 Hz band tolerates published-value drift; tight band lives in the V-1 fixture.
        Assert.InRange(result.EstimatedBuzzFrequency_Hz, 35.0, 55.0);
    }

    [Fact]
    public void Solve_Valveless_HigherBuzzFrequency_ThanStandard()
    {
        // Open-open mode (Valveless f = c/2L) yields exactly 2× the closed-open
        // mode (Standard f = c/4L) when the pipe-mode dominates (V-1 r ≈ 2.4 > 2.0).
        var solver = new PulsejetCycleSolver();
        var standard  = solver.Solve(V1ReferenceDesign(), SeaLevelStaticJp8());
        var valveless = solver.Solve(ValvelessDesign(),   SeaLevelStaticJp8());

        Assert.True(valveless.EstimatedBuzzFrequency_Hz > standard.EstimatedBuzzFrequency_Hz,
            $"Valveless buzz ({valveless.EstimatedBuzzFrequency_Hz:F1} Hz) should exceed "
          + $"Standard buzz ({standard.EstimatedBuzzFrequency_Hz:F1} Hz) at identical geometry.");
        // Exact 2× holds when alpha=1 (pipe-mode dominates); allow small slack for V-1's
        // r ≈ 2.4 where alpha=1.0 and the dispatch is pure pipe.
        double ratio = valveless.EstimatedBuzzFrequency_Hz / standard.EstimatedBuzzFrequency_Hz;
        Assert.InRange(ratio, 1.95, 2.05);
    }

    [Fact]
    public void GenerateWith_Standard_PropagatesBuzzFrequencyToResult()
    {
        // AirbreathingOptimization.GenerateWith must surface the field on the
        // outer AirbreathingResult, not just the inner CycleSolveResult.
        var result = AirbreathingOptimization.GenerateWith(V1ReferenceDesign(), SeaLevelStaticJp8());
        Assert.False(double.IsNaN(result.EstimatedBuzzFrequency_Hz));
        Assert.InRange(result.EstimatedBuzzFrequency_Hz, 35.0, 55.0);
    }
}
