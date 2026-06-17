// GasTurbineCycleSolverTests.cs — Sprint A8 unit tests for the open
// Brayton gas-turbine cycle solver.
//
// Covers: Kind property, simple-cycle power output, recuperated cycle
// efficiency uplift, feasibility gates, LM2500 fixture validation,
// non-regression for propulsive engines, π_c sweep monotonicity,
// recuperated-vs-simple shaft-power parity, and determinism.

using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Tests.Validation;

namespace Voxelforge.Airbreathing.Tests.Cycles;

public sealed class GasTurbineCycleSolverTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static AirbreathingEngineDesign SimpleDesign(
        double phi        = 0.32,
        double piC        = 18.0,
        double aInlet     = 0.38,
        double recuperator = 0.0)
        => new(
            Kind:                    AirbreathingEngineKind.GasTurbine,
            InletThroatArea_m2:      aInlet,
            CombustorArea_m2:        0.20,
            CombustorLength_m:       0.60,
            NozzleThroatArea_m2:     0.05,
            NozzleExitArea_m2:       0.10,
            EquivalenceRatio:        phi,
            CompressorPressureRatio: piC)
        {
            RecuperatorEffectiveness = recuperator,
        };

    private static FlightConditions SeaLevel()
        => new(Altitude_m: 0.0, MachNumber: 0.001, Fuel: AirbreathingFuel.Jp8);

    private static void AssertWithinFraction(
        string propertyName, double expected, double actual, double fraction)
    {
        if (double.IsNaN(expected)) return;
        var allowed = System.Math.Abs(expected) * fraction;
        var error   = System.Math.Abs(actual - expected);
        Assert.True(
            error <= allowed,
            $"{propertyName}: expected {expected:G6} ± {fraction:P0}, got {actual:G6} (error {error:G6})");
    }

    // ── 1. Kind property ─────────────────────────────────────────────────

    [Fact]
    public void GasTurbineKind_IsGasTurbine()
    {
        var solver = new GasTurbineCycleSolver();
        Assert.Equal(AirbreathingEngineKind.GasTurbine, solver.Kind);
    }

    // ── 2. Simple-cycle net work is positive ──────────────────────────────

    [Fact]
    public void SimpleCycle_NetWork_IsPositive()
    {
        var solver = new GasTurbineCycleSolver();
        var result = solver.Solve(SimpleDesign(), SeaLevel());
        Assert.True(
            result.ShaftPower_W > 0,
            $"Expected positive net shaft work; got {result.ShaftPower_W:G4} W");
    }

    // ── 3. Recuperated efficiency higher than simple-cycle ─────────────────
    // Use a lower π_c (≈ 8) where the recuperator gain is large enough to
    // measure cleanly with both constant-efficiency map stand-ins.

    [Fact]
    public void Recuperated_ThermalEfficiency_HigherThan_SimpleCycle()
    {
        var solver  = new GasTurbineCycleSolver();
        var simple  = solver.Solve(SimpleDesign(piC: 8.0, phi: 0.40, recuperator: 0.0), SeaLevel());
        var recup   = solver.Solve(SimpleDesign(piC: 8.0, phi: 0.40, recuperator: 0.80), SeaLevel());

        Assert.True(
            recup.ThermalEfficiency > simple.ThermalEfficiency,
            $"Recuperated η_th {recup.ThermalEfficiency:P2} should exceed simple-cycle "
          + $"{simple.ThermalEfficiency:P2}");
    }

    // ── 4. GAS_TURBINE_NET_WORK_NEGATIVE gate fires at low π_c ────────────

    [Fact]
    public void NegativeWork_Gate_FiresWhenPiC_TooLow()
    {
        // π_c = 1.5 → T_t2 barely above T_t0 → compressor almost free,
        // but turbine expansion is tiny → W_net ≤ 0.
        var design = SimpleDesign(piC: 1.5, phi: 0.05);
        var result = AirbreathingOptimization.GenerateWith(design, SeaLevel());
        Assert.Contains(
            result.Violations,
            v => v.ConstraintId == "GAS_TURBINE_NET_WORK_NEGATIVE");
    }

    // ── 5. Recuperator overtemp gate fires when T_t4 < T_t2 ───────────────
    // High π_c (→ hot T_t2) + low φ (→ modest TIT / low T_t4) + ε > 0.

    [Fact]
    public void RecuperatorOvertemp_Gate_FiresWhen_T_t4_BelowCompressorExit()
    {
        // π_c = 15, φ = 0.10: T_t2 ≈ 685 K, T_t4 ≈ 517 K → T_t4 < T_t2.
        var design = SimpleDesign(piC: 15.0, phi: 0.10, recuperator: 0.60);
        var result = AirbreathingOptimization.GenerateWith(design, SeaLevel());
        Assert.Contains(
            result.Advisories,
            v => v.ConstraintId == "GAS_TURBINE_RECUPERATOR_OVERTEMPERATURE");
    }

    // ── 6 & 7. LM2500 fixture validation ──────────────────────────────────

    [Fact]
    public void LM2500_SimpleCycle_ShaftPower_WithinTolerance()
    {
        var f      = AirbreathingFixtures.GE_LM2500_SimpleCycle;
        var result = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);
        AssertWithinFraction(
            "ShaftPower_W",
            f.Expected.ShaftPower_W,
            result.ShaftPower_W,
            f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void LM2500_SimpleCycle_ThermalEfficiency_WithinTolerance()
    {
        var f      = AirbreathingFixtures.GE_LM2500_SimpleCycle;
        var result = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);
        AssertWithinFraction(
            "ThermalEfficiency",
            f.Expected.ThermalEfficiency,
            result.ThermalEfficiency,
            f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void LM2500_Recuperated_ShaftPower_WithinTolerance()
    {
        var f      = AirbreathingFixtures.GE_LM2500_WithRecuperator;
        var result = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);
        AssertWithinFraction(
            "ShaftPower_W (recuperated)",
            f.Expected.ShaftPower_W,
            result.ShaftPower_W,
            f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void LM2500_Recuperated_ThermalEfficiency_WithinTolerance()
    {
        var f      = AirbreathingFixtures.GE_LM2500_WithRecuperator;
        var result = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);
        AssertWithinFraction(
            "ThermalEfficiency (recuperated)",
            f.Expected.ThermalEfficiency,
            result.ThermalEfficiency,
            f.Tolerance.PerformanceFraction);
    }

    // ── 8. ShaftPower_W = 0 for propulsive engines (non-regression) ────────

    [Fact]
    public void ShaftPower_IsZero_ForTurbojet()
    {
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Turbojet,
            InletThroatArea_m2:      0.115,
            CombustorArea_m2:        0.10,
            CombustorLength_m:       0.30,
            NozzleThroatArea_m2:     0.060,
            NozzleExitArea_m2:       0.078,
            EquivalenceRatio:        0.22,
            CompressorPressureRatio: 8.0);
        var result = AirbreathingOptimization.GenerateWith(design, SeaLevel());
        Assert.Equal(0.0, result.ShaftPower_W);
    }

    [Fact]
    public void ShaftPower_IsZero_ForRamjet()
    {
        var design = new AirbreathingEngineDesign(
            Kind:               AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2: 0.10,
            CombustorArea_m2:   0.30,
            CombustorLength_m:  0.50,
            NozzleThroatArea_m2: 0.0848,
            NozzleExitArea_m2:  0.20,
            EquivalenceRatio:   0.40);
        var ramjetCond = new FlightConditions(12_000.0, 2.0, AirbreathingFuel.H2);
        var result = AirbreathingOptimization.GenerateWith(design, ramjetCond);
        Assert.Equal(0.0, result.ShaftPower_W);
    }

    // ── 9. η_th monotonically increases with π_c (sub-optimal regime) ──────
    // Below the TIT limit, higher pressure ratio → higher cycle efficiency.

    [Fact]
    public void ThermalEfficiency_MonotonicallyIncreases_WithPiC_BelowTitLimit()
    {
        var solver = new GasTurbineCycleSolver();
        // φ = 0.50 to keep TIT well below the 1700 K hard gate.
        var r10 = solver.Solve(SimpleDesign(phi: 0.50, piC: 10.0), SeaLevel());
        var r20 = solver.Solve(SimpleDesign(phi: 0.50, piC: 20.0), SeaLevel());
        var r30 = solver.Solve(SimpleDesign(phi: 0.50, piC: 30.0), SeaLevel());

        Assert.True(r20.ThermalEfficiency > r10.ThermalEfficiency,
            $"η_th(π_c=20) {r20.ThermalEfficiency:P3} should exceed η_th(π_c=10) {r10.ThermalEfficiency:P3}");
        Assert.True(r30.ThermalEfficiency > r20.ThermalEfficiency,
            $"η_th(π_c=30) {r30.ThermalEfficiency:P3} should exceed η_th(π_c=20) {r20.ThermalEfficiency:P3}");
    }

    // ── 10. Recuperated vs simple: at constant φ, recuperated produces more shaft work ──────
    // At constant equivalence ratio the recuperator raises T_comb_in → raises TIT → increases
    // W_turb more than W_comp, so W_net_recuperated > W_net_simple.
    // (The "same W_net" claim only holds at constant TIT / variable φ.)

    [Fact]
    public void OperatingPointSweep_Recuperated_vs_Simple_RecuperatedProducesMoreWork()
    {
        var solver = new GasTurbineCycleSolver();
        var simple = solver.Solve(SimpleDesign(piC: 10.0, phi: 0.45, recuperator: 0.0), SeaLevel());
        var recup  = solver.Solve(SimpleDesign(piC: 10.0, phi: 0.45, recuperator: 0.80), SeaLevel());

        Assert.True(
            recup.ShaftPower_W > simple.ShaftPower_W,
            $"At constant φ, recuperated W_net ({recup.ShaftPower_W / 1e6:F3} MW) should exceed "
          + $"simple-cycle W_net ({simple.ShaftPower_W / 1e6:F3} MW).");
    }

    // ── 11. Thrust is zero (stationary unit) ─────────────────────────────

    [Fact]
    public void GasTurbine_ThrustNet_IsZero()
    {
        var solver = new GasTurbineCycleSolver();
        var result = solver.Solve(SimpleDesign(), SeaLevel());
        Assert.Equal(0.0, result.Stations.ThrustNet_N);
        Assert.Equal(0.0, result.Stations.SpecificImpulse_s);
    }

    // ── 12. Deterministic: two calls → identical results ─────────────────

    [Fact]
    public void GasTurbine_Deterministic()
    {
        var solver  = new GasTurbineCycleSolver();
        var design  = SimpleDesign(recuperator: 0.75);
        var cond    = SeaLevel();
        var result1 = solver.Solve(design, cond);
        var result2 = solver.Solve(design, cond);

        Assert.Equal(result1.ShaftPower_W,      result2.ShaftPower_W);
        Assert.Equal(result1.ThermalEfficiency, result2.ThermalEfficiency);
        Assert.Equal(result1.SpecificWork_Jkg,  result2.SpecificWork_Jkg);

        var s3a = result1.Stations.Station(3);
        var s3b = result2.Stations.Station(3);
        Assert.Equal(s3a.StagnationT_K, s3b.StagnationT_K);
        Assert.Equal(s3a.StagnationP_Pa, s3b.StagnationP_Pa);
    }
}
