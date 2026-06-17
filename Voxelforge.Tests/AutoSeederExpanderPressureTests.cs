// AutoSeederExpanderPressureTests — discipline tests pinning the
// cycle-aware coolant-inlet (= jacket-inlet, = fuel-pump-discharge)
// pressure default for expander-family cycles.
//
// Why this exists: Sprint feasibility-audit-F (2026-04-26 night). The
// pre-fix AutoSeeder default for `OperatingConditions.CoolantInletPressure_Pa`
// was `max(Pc × 1.6, 8 MPa)` regardless of engine cycle. That's
// correct for cycles where the jacket-outlet feeds directly into the
// injector (PressureFed, GasGenerator, ORSC, Tap-off, Closed/Staged
// combustion) but WRONG for expander cycles where the jacket-outlet
// FIRST crosses a turbine that must extract enough enthalpy to drive
// the pump shaft. The closed-expander turbine back-pressure is
// `Pc × 1.10` (chamber injection); the jacket outlet must exceed
// that for forward expansion through the turbine.
//
// Pre-fix RL10 numbers (Pc = 4 MPa, ClosedExpander):
//   • Jacket inlet (default):       8 MPa
//   • Jacket ΔP (Sprint-PR-#86 fix): 4.2 MPa
//   • Jacket outlet:                3.8 MPa
//   • Closed-expander back-pressure: 4 × 1.10 = 4.4 MPa
//   • Jacket outlet (3.8) < back-pressure (4.4) → no forward
//     expansion → AvailableShaftPower = 0 → EXPANDER_TURBINE_-
//     ENTHALPY_DEFICIT firing at 100 % of SA candidates.
//
// Post-fix:
//   • Jacket inlet for ClosedExpander: max(Pc × 4.0, 14 MPa)
//   • Jacket inlet for OpenExpander:   max(Pc × 3.0, 12 MPa)
//   • Non-expander cycles unchanged at max(Pc × 1.6, 8 MPa)
//   • PumpDischargePressure_Pa is also routed to match for expander
//     cycles (otherwise TurbopumpSizing sizes the pump for the
//     ResolvePumpDischarge fallback Pc × 1.5, which is energetically
//     inconsistent with a 14-16 MPa jacket-inlet boundary condition).
//
// Real-engine references that motivated the multipliers:
//   • RL10 (Pc 3.4 MPa, ClosedExpander): fuel-pump discharge ≈ 14 MPa
//     = 4.1× Pc
//   • J-2 (Pc 5.4 MPa, OpenExpander, partial): fuel-pump discharge
//     ≈ 16 MPa = 3× Pc
//
// References:
//   - Sutton & Biblarz, "Rocket Propulsion Elements" 9e §10.4 (expander
//     cycle topology + turbine PR), §10.5 (turbomachinery sizing).
//   - Pratt & Whitney "RL10 Fact Sheet" (turbomachinery numbers).
//   - NASA SP-4404 "The Saturn V S-IVB" (J-2 turbomachinery).

using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Tests;

public class AutoSeederExpanderPressureTests
{
    [Fact]
    public void ClosedExpander_JacketInletExceedsTurbineBackPressure()
    {
        // RL10-class: Pc 4 MPa ClosedExpander. Forward expansion
        // through the turbine requires jacket-outlet > Pc × 1.10
        // = 4.4 MPa. With typical jacket ΔP ≈ 4-5 MPa, that means
        // jacket-inlet must exceed ≈ 9 MPa. The pre-fix 8 MPa default
        // failed this test; the post-fix 16 MPa default has plenty of
        // margin.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 4e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var seed = AutoSeeder.Seed(spec);

        double turbineBackPressure = spec.ChamberPressure_Pa
                                   * CycleSolvers.ChamberInjectionBackPressureRatio;
        // Allow up to 6 MPa jacket ΔP (pre-fix RL10 measured 4.2 MPa;
        // post-fix at 16 MPa inlet measured 11.5 MPa due to denser LH2).
        double minJacketInletForForwardExpansion = turbineBackPressure + 6e6;

        Assert.True(
            seed.Conditions.CoolantInletPressure_Pa > minJacketInletForForwardExpansion,
            $"ClosedExpander jacket inlet {seed.Conditions.CoolantInletPressure_Pa / 1e6:F1} MPa "
          + $"must exceed turbine back-pressure ({turbineBackPressure / 1e6:F1} MPa) + jacket ΔP margin "
          + $"({minJacketInletForForwardExpansion / 1e6:F1} MPa) for AvailableShaftPower > 0.");
    }

    [Fact]
    public void ClosedExpander_PumpDischargeMatchesJacketInlet()
    {
        // For an expander cycle the fuel-pump discharge IS the jacket-
        // inlet boundary condition. If TurbopumpSizing sizes the pump
        // for a lower discharge (the Pc × 1.5 fallback in
        // ResolvePumpDischarge), the cycle is energetically inconsistent
        // — the pump can't deliver the pressure the regen solver
        // assumes. Pin the AutoSeeder routing.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 4e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(
            seed.Conditions.CoolantInletPressure_Pa,
            seed.Conditions.PumpDischargePressure_Pa);
    }

    [Fact]
    public void OpenExpander_JacketInletAt4xPc()
    {
        // J-2-class: Pc 5.4 MPa OpenExpander. Open-expander turbine
        // exhausts to ambient (101 kPa) so the back-pressure constraint
        // is much weaker than closed; what we need is a meaningful
        // pressure RATIO across the turbine. Sprint F1 (2026-04-27)
        // raised the multiplier 3× → 4× per PREFLIGHT_EXPANDER data
        // showing the lower multiplier produced inadequate turbine PR
        // when jacket ΔP exceeds the original 4-MPa estimate.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 1_000_000.0,
            ChamberPressure_Pa: 5.4e6,
            ExpansionRatio: 27.5,
            EngineCycleOverride: EngineCycle.OpenExpander);
        var seed = AutoSeeder.Seed(spec);

        double expectedFloor = System.Math.Max(spec.ChamberPressure_Pa * 4.0, 16e6);
        Assert.Equal(expectedFloor, seed.Conditions.CoolantInletPressure_Pa);
    }

    [Fact]
    public void NonExpanderCycles_PreserveLegacyDefault()
    {
        // Regression guard: the cycle-aware fix must NOT shift the
        // coolant-inlet default for non-expander cycles. Pre-fix
        // default `max(Pc × 1.6, 8 MPa)` is preserved bit-identically.
        var nonExpanderCycles = new[]
        {
            EngineCycle.PressureFed,
            EngineCycle.GasGenerator,
            EngineCycle.ElectricPump,
            EngineCycle.ORSC,
            EngineCycle.StagedCombustion,
            EngineCycle.FullFlow,
            EngineCycle.TapOff,
        };

        foreach (var cycle in nonExpanderCycles)
        {
            var spec = new EngineSpec(
                PropellantPair: PropellantPair.LOX_CH4,
                Thrust_N: 100_000.0,
                ChamberPressure_Pa: 7e6,
                ExpansionRatio: 16.0,
                EngineCycleOverride: cycle);
            var seed = AutoSeeder.Seed(spec);

            double expected = System.Math.Max(spec.ChamberPressure_Pa * 1.6, 8e6);
            Assert.Equal(expected, seed.Conditions.CoolantInletPressure_Pa);
        }
    }

    [Fact]
    public void NonExpanderCycles_PumpDischargeRemainsZero()
    {
        // Regression guard: non-expander cycles preserve the pre-fix
        // PumpDischargePressure_Pa = 0 default (which routes through
        // ResolvePumpDischarge → Pc × 1.5 fallback). Only expander
        // cycles set PumpDischargePressure_Pa explicitly.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_CH4,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 7e6,
            ExpansionRatio: 16.0,
            EngineCycleOverride: EngineCycle.GasGenerator);
        var seed = AutoSeeder.Seed(spec);

        Assert.Equal(0.0, seed.Conditions.PumpDischargePressure_Pa);
    }

    [Fact]
    public void ClosedExpanderMultiplier_MatchesRl10Reference()
    {
        // RL10 fuel-pump discharge ≈ 14-15 MPa for Pc 3.4 MPa = 4-5× Pc.
        // Sprint F1 (2026-04-27) bumped multiplier 4.0× → 5.0× / floor
        // 14 → 18 MPa per PREFLIGHT_EXPANDER instrumentation showing
        // 4× was inadequate (turbine PR = 0.944, very small specific
        // work). 5× provides healthy margin.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 73_000.0,
            ChamberPressure_Pa: 3.4e6,
            ExpansionRatio: 84.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        double computed = AutoSeeder.CoolantInletPressureFor(spec);

        // 5.0× Pc = 17.0 MPa, but the 18 MPa floor takes over.
        Assert.Equal(18e6, computed);
    }

    [Fact]
    public void ClosedExpander_HighPc_UsesMultiplierNotFloor()
    {
        // Floor only applies at low Pc. At Pc = 6 MPa the 5.0×
        // multiplier (30 MPa) dominates the 18 MPa floor — pin both
        // branches of the Math.Max.
        var spec = new EngineSpec(
            PropellantPair: PropellantPair.LOX_H2,
            Thrust_N: 100_000.0,
            ChamberPressure_Pa: 6e6,
            ExpansionRatio: 50.0,
            EngineCycleOverride: EngineCycle.ClosedExpander);
        double computed = AutoSeeder.CoolantInletPressureFor(spec);

        Assert.Equal(30e6, computed);
    }
}
