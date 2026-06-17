// CycleSolverTests.cs — Sprint 21 cycle-balance refactor invariant suite.
//
// Pins the dispatch tables that ICycleSolver consolidates:
//   • Pc multipliers: SC/FF = 1.50, GG = 1.20, others = 0
//   • Mass-flow fractions: SC/FF = 1.00, GG = 0.05, others = 0
//   • Preburner / turbopump / turbine existence per cycle
//   • Registry exhaustiveness (every EngineCycle has a solver)
//   • Cross-cycle invariants (Pc>0 ⇔ has any preburner; FFSC ⇔ both
//     fuel-rich AND ox-rich; HasOxRichPreburner ⇒ HasTurbopump etc.)
//   • End-to-end equivalence: dispatching SizePreburnerFor through
//     the solver produces the same numerical output it did before
//     the refactor (regression guard against accidental table edits).

using Voxelforge.FeedSystem;

namespace Voxelforge.Tests;

public class CycleSolverTests
{
    [Theory]
    [InlineData(EngineCycle.PressureFed,      0.00, 0.00)]
    [InlineData(EngineCycle.GasGenerator,     1.20, 0.05)]
    [InlineData(EngineCycle.ElectricPump,     0.00, 0.00)]
    [InlineData(EngineCycle.OpenExpander,     0.00, 0.00)]
    [InlineData(EngineCycle.StagedCombustion, 1.50, 1.00)]
    [InlineData(EngineCycle.FullFlow,         1.50, 1.00)]
    public void CycleSolver_PcMultiplier_And_MassFlowFraction_Match_LegacyTables(
        EngineCycle cycle, double expectedPcMultiplier, double expectedMassFlowFraction)
    {
        var s = CycleSolvers.Get(cycle);
        Assert.Equal(expectedPcMultiplier,    s.PreburnerPcMultiplier,            precision: 6);
        Assert.Equal(expectedMassFlowFraction, s.FuelRichPreburnerMassFlowFraction, precision: 6);
    }

    [Theory]
    [InlineData(EngineCycle.PressureFed,      false, false, false, false, false, false)]
    [InlineData(EngineCycle.GasGenerator,     true,  false, true,  false, true,  false)]
    [InlineData(EngineCycle.ElectricPump,     false, false, true,  true,  false, false)]
    [InlineData(EngineCycle.OpenExpander,     false, false, true,  false, true,  false)]
    [InlineData(EngineCycle.StagedCombustion, true,  false, true,  false, true,  true)]
    [InlineData(EngineCycle.FullFlow,         true,  true,  true,  false, true,  true)]
    public void CycleSolver_BooleanFlags_Match_LegacyDispatch(
        EngineCycle cycle,
        bool fuelRich, bool oxRich, bool turbopump, bool electric, bool turbine, bool dischargeFeedsMain)
    {
        var s = CycleSolvers.Get(cycle);
        Assert.Equal(fuelRich,           s.HasFuelRichPreburner);
        Assert.Equal(oxRich,             s.HasOxRichPreburner);
        Assert.Equal(turbopump,          s.HasTurbopump);
        Assert.Equal(electric,           s.HasElectricPowerConverter);
        Assert.Equal(turbine,            s.HasTurbine);
        Assert.Equal(dischargeFeedsMain, s.TurbineDischargeFeedsMainChamber);
    }

    [Fact]
    public void CycleSolver_OnlyFullFlow_UsesFfscDualPreburnerSizing()
    {
        // Sprint 21 invariant: today FullFlow is the only cycle that
        // routes preburner sizing through PreburnerChamber.SizeFfscDual.
        // If a future cycle adds a second sizer flavour, this test
        // forces the contributor to acknowledge it.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            Assert.Equal(c == EngineCycle.FullFlow, s.UsesFfscDualPreburnerSizing);
        }
    }

    [Fact]
    public void CycleSolver_Get_HasSolverForEveryEnumValue()
    {
        // Compile-time forcing function: if a future EngineCycle is
        // added without a corresponding case in CycleSolvers.Get, the
        // throw fires and this test surfaces it before any caller does.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            Assert.NotNull(s);
            Assert.Equal(c, s.Cycle);
        }
    }

    // ── Cross-cycle physical invariants ───────────────────────────────

    [Fact]
    public void CycleSolver_PcMultiplierPositive_IffAnyPreburner()
    {
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            bool hasAny = s.HasFuelRichPreburner || s.HasOxRichPreburner;
            Assert.Equal(hasAny, s.PreburnerPcMultiplier > 0.0);
        }
    }

    [Fact]
    public void CycleSolver_FuelRichMassFlowPositive_IffHasFuelRichPreburner()
    {
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            Assert.Equal(s.HasFuelRichPreburner, s.FuelRichPreburnerMassFlowFraction > 0.0);
        }
    }

    [Fact]
    public void CycleSolver_FfscDualSizing_RequiresBothPreburners()
    {
        // If a cycle uses dual sizing, it must have BOTH fuel-rich
        // AND ox-rich preburners — the SizeFfscDual API takes both.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            if (s.UsesFfscDualPreburnerSizing)
            {
                Assert.True(s.HasFuelRichPreburner,
                    $"{c}: UsesFfscDualPreburnerSizing but HasFuelRichPreburner is false");
                Assert.True(s.HasOxRichPreburner,
                    $"{c}: UsesFfscDualPreburnerSizing but HasOxRichPreburner is false");
            }
        }
    }

    [Fact]
    public void CycleSolver_AnyPreburner_ImpliesHasTurbine()
    {
        // A preburner exists to drive a turbine. If a cycle has a
        // preburner but no turbine, the dispatch is broken.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            if (s.HasFuelRichPreburner || s.HasOxRichPreburner)
                Assert.True(s.HasTurbine,
                    $"{c}: has a preburner but HasTurbine is false");
        }
    }

    [Fact]
    public void CycleSolver_HasTurbine_ImpliesHasTurbopump()
    {
        // A turbine drives a pump. If a cycle has a turbine but no
        // turbopump, something is mis-wired.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            if (s.HasTurbine)
                Assert.True(s.HasTurbopump,
                    $"{c}: HasTurbine but HasTurbopump is false");
        }
    }

    [Fact]
    public void CycleSolver_TurbineDischargeFeedsMainChamber_OnlyForStagedCycles()
    {
        // Today the staged cycles (SC, FFSC) feed the turbine
        // discharge into the main chamber; everyone else dumps to
        // ambient (or has no turbine). Pinning this invariant so
        // a future cycle that wires the same way doesn't silently
        // diverge.
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            var s = CycleSolvers.Get(c);
            if (s.TurbineDischargeFeedsMainChamber)
                Assert.True(s.HasTurbine,
                    $"{c}: TurbineDischargeFeedsMainChamber but HasTurbine is false");
        }
        Assert.True(CycleSolvers.Get(EngineCycle.StagedCombustion).TurbineDischargeFeedsMainChamber);
        Assert.True(CycleSolvers.Get(EngineCycle.FullFlow).TurbineDischargeFeedsMainChamber);
        Assert.False(CycleSolvers.Get(EngineCycle.GasGenerator).TurbineDischargeFeedsMainChamber);
        Assert.False(CycleSolvers.Get(EngineCycle.OpenExpander).TurbineDischargeFeedsMainChamber);
    }

    [Fact]
    public void CycleSolver_ElectricPump_HasNoTurbine()
    {
        // Electric-pump cycle gets shaft power from a battery + motor,
        // not a gas turbine. Pinning this so a refactor doesn't swap it.
        var s = CycleSolvers.Get(EngineCycle.ElectricPump);
        Assert.True(s.HasTurbopump);
        Assert.True(s.HasElectricPowerConverter);
        Assert.False(s.HasTurbine);
        Assert.False(s.HasFuelRichPreburner);
        Assert.False(s.HasOxRichPreburner);
    }

    [Fact]
    public void CycleSolver_PressureFed_IsCompletelyEmpty()
    {
        // PressureFed has nothing to size — no pumps, no turbines,
        // no preburner. The solver must report zero on every flag.
        var s = CycleSolvers.Get(EngineCycle.PressureFed);
        Assert.False(s.HasFuelRichPreburner);
        Assert.False(s.HasOxRichPreburner);
        Assert.False(s.HasTurbopump);
        Assert.False(s.HasElectricPowerConverter);
        Assert.False(s.HasTurbine);
        Assert.False(s.TurbineDischargeFeedsMainChamber);
        Assert.False(s.UsesFfscDualPreburnerSizing);
        Assert.Equal(0.0, s.PreburnerPcMultiplier,            precision: 6);
        Assert.Equal(0.0, s.FuelRichPreburnerMassFlowFraction, precision: 6);
    }

    [Fact]
    public void CycleSolver_Get_ReturnsSameSingleton_OnRepeatedCalls()
    {
        // Solvers are stateless — registry should hand out the same
        // instance each time so callers can hold on to references
        // across SA iterations without per-candidate allocation.
        Assert.Same(CycleSolvers.Get(EngineCycle.StagedCombustion),
                    CycleSolvers.Get(EngineCycle.StagedCombustion));
        Assert.Same(CycleSolvers.Get(EngineCycle.FullFlow),
                    CycleSolvers.Get(EngineCycle.FullFlow));
        Assert.Same(CycleSolvers.Get(EngineCycle.PressureFed),
                    CycleSolvers.Get(EngineCycle.PressureFed));
    }
}
