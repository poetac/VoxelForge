// BB-5c (2026-05-05): airbreathing cycle-solver physics microbenches.
// Measures the wall-clock cost of one Solve() call per solver family at a
// representative feasible design point so cross-sprint regressions in the
// airbreathing physics pipeline are visible alongside the rocket baselines.
//
// Design points are lifted verbatim from the corresponding *CycleSolverTests
// fixtures — each produces a feasible result (positive thrust / Isp / shaft
// power) at the given flight conditions. Scramjet requires Mach ≥ 4; all
// other solvers use Mach ≈ 0 sea-level static or Mach 2 ramjet conditions.
//
// Fields are stored as readonly instance members (BDN instantiates the class
// once per benchmark run, so field initialisation cost is amortised and not
// counted in the timed region).

using BenchmarkDotNet.Attributes;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;

namespace Voxelforge.MicroBenchmarks;

[MemoryDiagnoser]
public class AirbreathingCyclesBench
{
    // ── Ramjet (Mach 2, 12 km altitude, H2) ─────────────────────────────────

    private readonly RamjetCycleSolver _ramjet = new();
    private readonly AirbreathingEngineDesign _ramjetDesign = new(
        Kind:               AirbreathingEngineKind.Ramjet,
        InletThroatArea_m2: 0.10,
        CombustorArea_m2:   0.30,
        CombustorLength_m:  0.50,
        NozzleThroatArea_m2: 0.0848,
        NozzleExitArea_m2:  0.20,
        EquivalenceRatio:   0.40);
    private readonly FlightConditions _ramjetCond = new(
        Altitude_m:  12_000.0,
        MachNumber:  2.0,
        Fuel:        AirbreathingFuel.H2);

    [Benchmark]
    public CycleSolveResult Ramjet_Solve() => _ramjet.Solve(_ramjetDesign, _ramjetCond);

    // ── Turbojet (J85-class, sea-level static, Jp8) ──────────────────────────

    private readonly TurbojetCycleSolver _turbojet = new();
    private readonly AirbreathingEngineDesign _turbojetDesign = new(
        Kind:                    AirbreathingEngineKind.Turbojet,
        InletThroatArea_m2:      0.115,
        CombustorArea_m2:        0.10,
        CombustorLength_m:       0.30,
        NozzleThroatArea_m2:     0.060,
        NozzleExitArea_m2:       0.078,
        EquivalenceRatio:        0.22,
        CompressorPressureRatio: 8.0);
    private readonly FlightConditions _turbojetCond = new(
        Altitude_m: 0.0,
        MachNumber: 0.001,
        Fuel:       AirbreathingFuel.Jp8);

    [Benchmark]
    public CycleSolveResult Turbojet_Solve() => _turbojet.Solve(_turbojetDesign, _turbojetCond);

    // ── Turbofan (F404-class, sea-level static, Jp8) ─────────────────────────

    private readonly TurbofanCycleSolver _turbofan = new();
    private readonly AirbreathingEngineDesign _turbofanDesign = new(
        Kind:                    AirbreathingEngineKind.Turbofan,
        InletThroatArea_m2:      0.37,
        CombustorArea_m2:        0.15,
        CombustorLength_m:       0.40,
        NozzleThroatArea_m2:     0.12,
        NozzleExitArea_m2:       0.18,
        EquivalenceRatio:        0.30,
        CompressorPressureRatio: 25.0,
        BypassRatio:             0.34);
    private readonly FlightConditions _turbofanCond = new(
        Altitude_m: 0.0,
        MachNumber: 0.001,
        Fuel:       AirbreathingFuel.Jp8);

    [Benchmark]
    public CycleSolveResult Turbofan_Solve() => _turbofan.Solve(_turbofanDesign, _turbofanCond);

    // ── Scramjet (Mattingly reference, Mach 8, 25 km altitude, H2) ──────────

    private readonly ScramjetCycleSolver _scramjet = new();
    private readonly AirbreathingEngineDesign _scramjetDesign = new(
        Kind:               AirbreathingEngineKind.Scramjet,
        InletThroatArea_m2: 0.20,
        CombustorArea_m2:   0.30,
        CombustorLength_m:  1.50,
        NozzleThroatArea_m2: 0.25,
        NozzleExitArea_m2:  1.00,
        EquivalenceRatio:   0.60,
        IsolatorLength_m:   0.80);
    private readonly FlightConditions _scramjetCond = new(
        Altitude_m: 25_000.0,
        MachNumber: 8.0,
        Fuel:       AirbreathingFuel.H2);

    [Benchmark]
    public CycleSolveResult Scramjet_Solve() => _scramjet.Solve(_scramjetDesign, _scramjetCond);

    // ── RBCC — DuctedRocket mode (Mach 0.5 sea-level, H2) ───────────────────

    private readonly RbccCycleSolver _rbcc = new();
    private readonly AirbreathingEngineDesign _rbccDesign = new(
        Kind:                    AirbreathingEngineKind.Rbcc,
        InletThroatArea_m2:      0.10,
        CombustorArea_m2:        0.30,
        CombustorLength_m:       0.50,
        NozzleThroatArea_m2:     0.085,
        NozzleExitArea_m2:       0.20,
        EquivalenceRatio:        0.55,
        IsolatorLength_m:        0.50,
        RbccMode:                RbccOperatingMode.DuctedRocket,
        EjectorEntrainmentRatio: 1.5);
    private readonly FlightConditions _rbccCond = new(
        Altitude_m: 0.0,
        MachNumber: 0.5,
        Fuel:       AirbreathingFuel.H2);

    [Benchmark]
    public CycleSolveResult Rbcc_Solve() => _rbcc.Solve(_rbccDesign, _rbccCond);

    // ── Gas turbine (GE LM2500-class, sea-level static, Jp8) ────────────────

    private readonly GasTurbineCycleSolver _gasTurbine = new();
    private readonly AirbreathingEngineDesign _gasTurbineDesign = new(
        Kind:                    AirbreathingEngineKind.GasTurbine,
        InletThroatArea_m2:      0.38,
        CombustorArea_m2:        0.20,
        CombustorLength_m:       0.60,
        NozzleThroatArea_m2:     0.05,
        NozzleExitArea_m2:       0.10,
        EquivalenceRatio:        0.32,
        CompressorPressureRatio: 18.0)
    {
        RecuperatorEffectiveness = 0.0,
    };
    private readonly FlightConditions _gasTurbineCond = new(
        Altitude_m: 0.0,
        MachNumber: 0.001,
        Fuel:       AirbreathingFuel.Jp8);

    [Benchmark]
    public CycleSolveResult GasTurbine_Solve() => _gasTurbine.Solve(_gasTurbineDesign, _gasTurbineCond);
}
