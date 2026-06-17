// IsentropicNozzleSolverTests.cs — Sprint E.1 acceptance tests for the
// choked-flow isentropic CD-nozzle solver.

using System;
using Voxelforge.ElectricPropulsion.Solvers;

namespace Voxelforge.ElectricPropulsion.Tests.Solvers;

public sealed class IsentropicNozzleSolverTests
{
    private static ElectricPropulsionEngineDesign Mr501bDesign() => new(
        Kind:                    ElectricPropulsionEngineKind.Resistojet,
        HeaterPower_W:           870.0,
        PropellantMassFlow_kgs:  1.2e-4,
        NozzleThroatRadius_mm:   0.20,
        NozzleAreaRatio:         100.0,
        HeaterChamberLength_mm:  25.0,
        HeaterChamberRadius_mm:  6.0);

    private static ResistojetConditions Mr501bConditions() => new(
        BusVoltage_V:        28.0,
        BusPower_W_avail:    900.0,
        AmbientPressure_Pa:  0.0,
        Propellant:          Propellant.N2H4Decomposed,
        InletTemperature_K:  900.0,
        InletComposition:    PropellantInletComposition.Hydrazine_Shell405);

    [Fact]
    public void Solve_AtMr501bChamberTemp_Converges()
    {
        var result = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        Assert.True(result.Converged, "Nozzle Newton must converge on MR-501B-class inputs");
    }

    [Fact]
    public void Solve_InVacuum_AlwaysChoked()
    {
        var result = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        Assert.True(result.ChokedFlow);
    }

    [Fact]
    public void Solve_ExitMachIsSupersonic()
    {
        var result = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        // ε=100 supersonic exit → M_e ≈ 4–5 for γ ≈ 1.3.
        Assert.True(result.ExitMachNumber > 1.0);
        Assert.InRange(result.ExitMachNumber, 3.0, 6.0);
    }

    [Fact]
    public void Solve_GivesPositiveThrust()
    {
        var result = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        Assert.True(result.Thrust_N > 0);
    }

    [Fact]
    public void Solve_OnMr501b_GivesPlausibleIsp()
    {
        // MR-501B targets ~300 s vacuum Isp; the lumped 0-D model with
        // Wave-1 fidelity should land in 200-360 s band.
        var result = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        Assert.InRange(result.IspVacuum_s, 200.0, 360.0);
    }

    [Fact]
    public void Solve_HigherChamberT_GivesHigherIsp()
    {
        var lo = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 800.0,
            propellantMassFlow_kgs: 1.2e-4);
        var hi = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1500.0,
            propellantMassFlow_kgs: 1.2e-4);
        Assert.True(hi.IspVacuum_s > lo.IspVacuum_s);
    }

    [Fact]
    public void Solve_HigherAreaRatio_GivesHigherIsp()
    {
        var lo = IsentropicNozzleSolver.Solve(
            Mr501bDesign() with { NozzleAreaRatio = 30.0 },
            Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        var hi = IsentropicNozzleSolver.Solve(
            Mr501bDesign() with { NozzleAreaRatio = 150.0 },
            Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 1.2e-4);
        Assert.True(hi.IspVacuum_s > lo.IspVacuum_s,
            $"ε=150 Isp {hi.IspVacuum_s} should exceed ε=30 Isp {lo.IspVacuum_s}");
    }

    [Fact]
    public void Solve_HigherMassFlow_GivesHigherChamberPressure()
    {
        // Choked-throat continuity: ṁ ∝ P_c at fixed (T_c, A_t, γ).
        var lo = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 0.5e-4);
        var hi = IsentropicNozzleSolver.Solve(
            Mr501bDesign(), Mr501bConditions(),
            chamberTemperature_K: 1200.0,
            propellantMassFlow_kgs: 2.0e-4);
        Assert.True(hi.ChamberPressure_Pa > lo.ChamberPressure_Pa);
        // Roughly 4× mass flow → 4× P_c (continuity is linear in P_c).
        double ratio = hi.ChamberPressure_Pa / lo.ChamberPressure_Pa;
        Assert.InRange(ratio, 3.5, 4.5);
    }

    [Fact]
    public void AreaMachResidual_AtKnownAnswer_IsZero()
    {
        // For γ = 1.4 and M = 2.0:
        //   ε = (1/2) · [(2/2.4) · (1 + 0.2·4)]^(2.4/0.8)
        //     = 0.5 · (0.8333 · 1.8)^3
        //     = 0.5 · (1.5)^3
        //     = 0.5 · 3.375
        //     = 1.6875
        double residual = IsentropicNozzleSolver.AreaMachResidual(
            epsilonTarget: 1.6875, M: 2.0, gamma: 1.4);
        Assert.InRange(residual, -0.001, 0.001);
    }

    [Fact]
    public void Solve_RejectsZeroThroatRadius()
    {
        var bad = Mr501bDesign() with { NozzleThroatRadius_mm = 0.0 };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IsentropicNozzleSolver.Solve(bad, Mr501bConditions(), 1200.0, 1.2e-4));
    }

    [Fact]
    public void Solve_RejectsAreaRatioBelowOne()
    {
        var bad = Mr501bDesign() with { NozzleAreaRatio = 0.5 };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IsentropicNozzleSolver.Solve(bad, Mr501bConditions(), 1200.0, 1.2e-4));
    }
}
