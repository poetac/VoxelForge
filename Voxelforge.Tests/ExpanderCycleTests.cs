// ExpanderCycleTests.cs — Sprint 23 regression + invariant suite for
// the coolant-driven turbine energy balance.
//
// Coverage:
//   • ExpanderCycleSizing.Size returns null on non-expander cycles
//   • Open + closed expander both produce non-null results with
//     sensible back-pressures (ambient vs chamber × 1.30)
//   • Closed-expander specific work < open-expander specific work at
//     the same inlet state (higher back-pressure → smaller pressure
//     ratio → less isentropic work)
//   • Efficient power balance: feasible when available ≥ required
//   • Power-balance gate: PowerSufficient=false ⇒ the
//     EXPANDER_TURBINE_ENTHALPY_DEFICIT gate fires with the right
//     ConstraintId + ActualValue + Limit
//   • CycleSolver: ClosedExpander solver reports correct flags
//     (no preburner, has turbine, HasTurbopump, discharge feeds
//     main chamber)
//   • Degenerate back-pressure case: when jacket outlet P ≤ turbine
//     back-pressure, the module returns a PowerSufficient=false
//     result instead of producing negative specific work

using Voxelforge.Coolant;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.Tests;

public class ExpanderCycleTests
{
    // Representative hydrogen expander baseline — RL10-class numbers.
    private const double InletT_K            = 250.0;         // jacket outlet T
    private const double InletP_Pa           = 12e6;          // jacket outlet P (12 MPa)
    private const double CoolantInletT_K     = 25.0;          // cryogenic H2 inlet
    private const double CoolantMassFlow_kgs = 0.8;           // H2 mass flow
    private const double MainChamberP_Pa     = 4e6;           // main chamber Pc
    private const double PumpPower_Low_W     = 50_000.0;      // 50 kW (easy)
    // H2 + 12 MPa → 0.1 MPa expansion at 0.8 kg/s yields ~1.1 MW; set
    // the infeasibility threshold well clear of that.
    private const double PumpPower_High_W    = 3_000_000.0;   // 3 MW (infeasible)

    [Fact]
    public void ExpanderSizing_ReturnsNull_OnNonExpanderCycles()
    {
        foreach (EngineCycle c in System.Enum.GetValues(typeof(EngineCycle)))
        {
            if (c == EngineCycle.OpenExpander || c == EngineCycle.ClosedExpander) continue;

            var result = ExpanderCycleSizing.Size(
                cycle:                    c,
                coolant:                  HydrogenFluid.Instance,
                coolantOutletT_K:         InletT_K,
                coolantOutletP_Pa:        InletP_Pa,
                coolantInletT_K:          CoolantInletT_K,
                coolantMassFlow_kgs:      CoolantMassFlow_kgs,
                mainChamberPressure_Pa:   MainChamberP_Pa,
                requiredPumpShaftPower_W: PumpPower_Low_W);

            Assert.Null(result);
        }
    }

    [Fact]
    public void ExpanderSizing_ReturnsNull_WhenJacketDidNoWork()
    {
        // Jacket outlet T ≤ inlet T ⇒ no enthalpy to extract.
        var result = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         CoolantInletT_K,       // same as inlet
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);

        Assert.Null(result);
    }

    [Fact]
    public void OpenExpander_DischargesToAmbient()
    {
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);

        Assert.NotNull(r);
        Assert.Equal(ExpanderCycleSizing.AmbientBackPressure_Pa, r!.OutletPressure_Pa, precision: 3);
        Assert.Contains("Open-expander", r.Notes);
    }

    [Fact]
    public void ClosedExpander_DischargesAtChamberInjectionRatio()
    {
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.ClosedExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);

        Assert.NotNull(r);
        double expectedPOut = MainChamberP_Pa * ExpanderCycleSizing.ChamberInjectionBackPressureRatio;
        Assert.Equal(expectedPOut, r!.OutletPressure_Pa, precision: 3);
        Assert.Contains("Closed-expander", r.Notes);
    }

    [Fact]
    public void OpenExpander_ProducesMoreSpecificWorkThanClosed_AtSameInletState()
    {
        var open = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);
        var closed = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.ClosedExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);

        Assert.NotNull(open);
        Assert.NotNull(closed);
        Assert.True(open!.ActualSpecificWork_Jkg > closed!.ActualSpecificWork_Jkg,
            $"Open-expander must extract more work than closed at same inlet: "
          + $"open {open.ActualSpecificWork_Jkg / 1e3:F1} kJ/kg vs "
          + $"closed {closed.ActualSpecificWork_Jkg / 1e3:F1} kJ/kg");
    }

    [Fact]
    public void OpenExpander_FeasibleAtLowPumpPower()
    {
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);

        Assert.NotNull(r);
        Assert.True(r!.PowerSufficient,
            $"Expected feasible at {PumpPower_Low_W / 1e3:F0} kW pump, got "
          + $"available {r.AvailableShaftPower_W / 1e3:F1} kW");
    }

    [Fact]
    public void OpenExpander_InfeasibleAtHighPumpPower()
    {
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        InletP_Pa,
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_High_W);

        Assert.NotNull(r);
        Assert.False(r!.PowerSufficient,
            $"Expected infeasible at {PumpPower_High_W / 1e3:F0} kW pump, got "
          + $"available {r.AvailableShaftPower_W / 1e3:F1} kW (sufficient)");
    }

    [Fact]
    public void ExpanderSizing_DegenerateBackPressure_ReturnsInfeasibleNotNegativeWork()
    {
        // Jacket outlet P ≤ back pressure: must short-circuit to an
        // infeasible result (not produce NaN / negative specific work).
        // Drive jacket outlet P below ambient (physically impossible
        // but worth testing the guard): open-expander back-pressure
        // is 0.1 MPa, so set jacket outlet to 0.05 MPa.
        var r = ExpanderCycleSizing.Size(
            cycle:                    EngineCycle.OpenExpander,
            coolant:                  HydrogenFluid.Instance,
            coolantOutletT_K:         InletT_K,
            coolantOutletP_Pa:        0.05e6,   // below 0.1 MPa ambient
            coolantInletT_K:          CoolantInletT_K,
            coolantMassFlow_kgs:      CoolantMassFlow_kgs,
            mainChamberPressure_Pa:   MainChamberP_Pa,
            requiredPumpShaftPower_W: PumpPower_Low_W);

        Assert.NotNull(r);
        Assert.False(r!.PowerSufficient);
        Assert.Equal(0, r.AvailableShaftPower_W, precision: 6);
        Assert.Equal(0, r.ActualSpecificWork_Jkg, precision: 6);
        Assert.Contains("no forward expansion", r.Notes);
    }

    [Fact]
    public void CycleSolver_ClosedExpander_HasExpectedFlags()
    {
        var s = CycleSolvers.Get(EngineCycle.ClosedExpander);

        Assert.False(s.HasFuelRichPreburner);
        Assert.False(s.HasOxRichPreburner);
        Assert.Equal(0.0, s.PreburnerPcMultiplier,            precision: 6);
        Assert.Equal(0.0, s.FuelRichPreburnerMassFlowFraction, precision: 6);
        Assert.False(s.UsesFfscDualPreburnerSizing);
        Assert.True(s.HasTurbopump);
        Assert.False(s.HasElectricPowerConverter);
        Assert.True(s.HasTurbine);
        // Key ClosedExpander differentiator: discharge DOES feed main.
        Assert.True(s.TurbineDischargeFeedsMainChamber);
    }

    [Fact]
    public void CycleSolver_OpenVsClosedExpander_DifferOnlyInDischargeRouting()
    {
        var open   = CycleSolvers.Get(EngineCycle.OpenExpander);
        var closed = CycleSolvers.Get(EngineCycle.ClosedExpander);

        Assert.Equal(open.HasFuelRichPreburner,             closed.HasFuelRichPreburner);
        Assert.Equal(open.HasOxRichPreburner,               closed.HasOxRichPreburner);
        Assert.Equal(open.PreburnerPcMultiplier,            closed.PreburnerPcMultiplier,            precision: 6);
        Assert.Equal(open.FuelRichPreburnerMassFlowFraction, closed.FuelRichPreburnerMassFlowFraction, precision: 6);
        Assert.Equal(open.UsesFfscDualPreburnerSizing,      closed.UsesFfscDualPreburnerSizing);
        Assert.Equal(open.HasTurbopump,                     closed.HasTurbopump);
        Assert.Equal(open.HasElectricPowerConverter,        closed.HasElectricPowerConverter);
        Assert.Equal(open.HasTurbine,                       closed.HasTurbine);
        Assert.NotEqual(open.TurbineDischargeFeedsMainChamber, closed.TurbineDischargeFeedsMainChamber);
    }

    [Fact]
    public void FeasibilityGate_FiresExpanderEnthalpyDeficit_OnInfeasibleResult()
    {
        // Synthesize a RegenGenerationResult with a PowerSufficient=false
        // ExpanderTurbine attached; verify the gate fires with the
        // right ConstraintId + actual/limit.
        var expander = new ExpanderTurbineResult(
            Cycle: EngineCycle.OpenExpander,
            CoolantLabel: "H2",
            InletTemperature_K: InletT_K,
            InletPressure_Pa: InletP_Pa,
            OutletPressure_Pa: ExpanderCycleSizing.AmbientBackPressure_Pa,
            MassFlow_kgs: CoolantMassFlow_kgs,
            Cp_Jkg_K: 14_000.0,
            EffectiveGamma: 1.40,
            IsentropicSpecificWork_Jkg: 50_000.0,
            ActualSpecificWork_Jkg: 27_500.0,
            Efficiency: 0.55,
            AvailableShaftPower_W: 22_000.0,            // 22 kW
            RequiredShaftPower_W:  50_000.0,            // 50 kW
            PowerSufficient: false,
            Notes: "test-fixture deficit");

        var gen = MinimalRegenGenerationResult() with { ExpanderTurbine = expander };
        var gateResult = FeasibilityGate.Evaluate(gen);

        var violation = System.Array.Find(
            gateResult.Violations,
            v => v.ConstraintId == "EXPANDER_TURBINE_ENTHALPY_DEFICIT");
        Assert.NotNull(violation);
        Assert.Equal(22_000.0, violation!.ActualValue, precision: 3);
        Assert.Equal(50_000.0, violation.Limit,        precision: 3);
        Assert.Contains("Expander-cycle coolant enthalpy insufficient", violation.Description);
    }

    [Fact]
    public void FeasibilityGate_DoesNotFire_OnSufficientExpanderResult()
    {
        var expander = new ExpanderTurbineResult(
            Cycle: EngineCycle.ClosedExpander,
            CoolantLabel: "H2",
            InletTemperature_K: InletT_K,
            InletPressure_Pa: InletP_Pa,
            OutletPressure_Pa: MainChamberP_Pa * ExpanderCycleSizing.ChamberInjectionBackPressureRatio,
            MassFlow_kgs: CoolantMassFlow_kgs,
            Cp_Jkg_K: 14_000.0,
            EffectiveGamma: 1.40,
            IsentropicSpecificWork_Jkg: 100_000.0,
            ActualSpecificWork_Jkg: 55_000.0,
            Efficiency: 0.55,
            AvailableShaftPower_W: 44_000.0,           // 44 kW
            RequiredShaftPower_W:  30_000.0,           // 30 kW
            PowerSufficient: true,
            Notes: "test-fixture OK");

        var gen = MinimalRegenGenerationResult() with { ExpanderTurbine = expander };
        var gateResult = FeasibilityGate.Evaluate(gen);

        Assert.DoesNotContain(
            gateResult.Violations,
            v => v.ConstraintId == "EXPANDER_TURBINE_ENTHALPY_DEFICIT");
    }

    // Minimal fixture factory — the gate only reads ExpanderTurbine on
    // this branch; the rest of the result can be a no-op placeholder.
    private static RegenGenerationResult MinimalRegenGenerationResult()
    {
        var cond = new OperatingConditions();
        var design = new RegenChamberDesign();
        return RegenChamberOptimization.GenerateWith(cond, design);
    }
}
