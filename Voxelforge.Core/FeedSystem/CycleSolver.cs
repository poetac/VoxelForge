// CycleSolver.cs — Sprint 21: cycle-balance foundation.
//
// Consolidates the per-EngineCycle dispatch that was previously scattered
// across RegenChamberOptimization.SizePreburnerFor, AutoSeeder, TurbineSizing,
// and TurbopumpSizing. Adding a new cycle is now additive (one new solver
// class + one line in CycleSolvers.Get) instead of having to touch every
// dispatch site.
//
// The solver surface is intentionally tiny: it answers *categorical*
// questions about a cycle (has preburner? dual preburner? discharge
// feeds main chamber?) and supplies *defaults* (preburner Pc multiplier,
// turbine mass-flow fraction). The actual sizing math still lives in
// TurbopumpSizing, TurbineSizing, and PreburnerChamber — those just
// consume the solver's answers instead of reproducing the switch.
//
// Roadmap: unlocks the three new cycle sprints (Expander, ORSC,
// Tap-off) called out in CLAUDE.md. Each new cycle is implemented as
// one new ICycleSolver subclass + one switch arm in CycleSolvers.Get;
// adding the cycle-specific physics (preburner, turbine path) then
// sits naturally on top of the existing dispatch.

namespace Voxelforge.FeedSystem;

/// <summary>
/// Categorical dispatch for an <see cref="EngineCycle"/>. Answers the
/// questions that the sizing code used to answer with scattered
/// <c>switch</c>/<c>if</c> statements: does this cycle have a preburner?
/// a turbopump? does its turbine discharge feed the main chamber? what
/// default preburner Pc multiplier should the auto-sizer use?
/// </summary>
public interface ICycleSolver
{
    /// <summary>The cycle this solver answers for.</summary>
    EngineCycle Cycle { get; }

    // ── Preburner surface ──────────────────────────────────────────

    /// <summary>
    /// True when this cycle has a fuel-rich preburner that drives a
    /// turbine. Staged combustion and gas-generator cycles are the
    /// classic fuel-rich cases. Full-flow has BOTH fuel-rich and
    /// ox-rich preburners; see <see cref="HasOxRichPreburner"/>.
    /// </summary>
    bool HasFuelRichPreburner { get; }

    /// <summary>
    /// True when this cycle has an ox-rich preburner. Full-flow
    /// staged combustion (FFSC) today; an ORSC cycle (ox-rich only)
    /// added in the future would also return <c>true</c> here while
    /// returning <c>false</c> for <see cref="HasFuelRichPreburner"/>.
    /// </summary>
    bool HasOxRichPreburner { get; }

    /// <summary>
    /// Default preburner chamber pressure as a multiplier of the main
    /// chamber Pc, used when the user / SA has not supplied an explicit
    /// override on <see cref="Optimization.OperatingConditions.PreburnerChamberPressure_Pa"/>.
    /// 1.5 for staged-combustion / full-flow (preburner pushes mass
    /// through the main chamber so Pc_pb &gt; Pc_main), 1.2 for
    /// gas-generator (preburner operates standalone with a modest
    /// lift above main Pc). Returns 0 for cycles without a preburner
    /// (callers should guard with <see cref="HasFuelRichPreburner"/>
    /// or <see cref="HasOxRichPreburner"/> before reading).
    /// </summary>
    double PreburnerPcMultiplier { get; }

    /// <summary>
    /// Fraction of the total mass flow routed through the fuel-rich
    /// preburner's turbine. 1.0 for staged combustion (all propellant
    /// passes through the preburner then into the main chamber), 0.05
    /// for gas-generator (small side-stream tap), 1.0 for full-flow
    /// (both preburners see full flow on their respective sides).
    /// 0 when no fuel-rich preburner exists.
    /// </summary>
    double FuelRichPreburnerMassFlowFraction { get; }

    /// <summary>
    /// Sprint 24: fraction of the total mass flow routed through the
    /// ox-rich preburner's turbine. 1.0 for FullFlow (ox-rich side sees
    /// full ox flow) and for ORSC (single ox-rich preburner carries
    /// the entire ox stream). 0 when no ox-rich preburner exists.
    /// </summary>
    double OxRichPreburnerMassFlowFraction { get; }

    /// <summary>
    /// True when preburner sizing must go through
    /// <see cref="Chamber.PreburnerChamber.SizeFfscDual"/> instead of
    /// the single-preburner <see cref="Chamber.PreburnerChamber.Size"/>.
    /// Full-flow only today.
    /// </summary>
    bool UsesFfscDualPreburnerSizing { get; }

    // ── Turbopump surface ──────────────────────────────────────────

    /// <summary>
    /// True when this cycle has turbomachinery to size. PressureFed is
    /// the one cycle that short-circuits TurbopumpSizing entirely.
    /// </summary>
    bool HasTurbopump { get; }

    /// <summary>
    /// True when this cycle uses an electric motor + battery stack to
    /// drive the pumps (Rocket Lab Rutherford lineage). Adds a power-
    /// converter mass estimate on top of the mechanical pump mass.
    /// </summary>
    bool HasElectricPowerConverter { get; }

    // ── Turbine surface ────────────────────────────────────────────

    /// <summary>
    /// True when this cycle has a gas turbine to size. Covers every
    /// cycle except PressureFed and ElectricPump.
    /// </summary>
    bool HasTurbine { get; }

    /// <summary>
    /// True when the turbine discharge feeds back into the main
    /// chamber (staged combustion and full-flow); false when it
    /// dumps to ambient (gas-generator, open-expander). Drives the
    /// turbine back-pressure calculation in
    /// <see cref="TurbineSizing.Size"/>.
    /// </summary>
    bool TurbineDischargeFeedsMainChamber { get; }
}

/// <summary>
/// Registry and factory for <see cref="ICycleSolver"/> implementations.
/// Call <see cref="Get(EngineCycle)"/> to retrieve the solver for a
/// given cycle. The registry is compile-time-exhaustive — a missing
/// switch arm surfaces as a <c>throw</c> at the first caller rather
/// than silently defaulting.
/// </summary>
public static class CycleSolvers
{
    /// <summary>
    /// Sprint 32 (2026-04-24, PH-25) — single source of truth for the
    /// back-pressure ratio applied to a turbine exhaust that feeds the
    /// main chamber injector (closed-expander + staged-combustion +
    /// full-flow). Pre-Sprint-32 there were two inconsistent values:
    /// 1.10 in <see cref="TurbineSizing.ChamberInjectionBackPressureRatio"/>
    /// and 1.30 in <see cref="ExpanderCycleSizing.ChamberInjectionBackPressureRatio"/>.
    /// Unified to 1.18 — midway between them, biased slightly toward
    /// the more conservative (closed-expander) value to keep injector-
    /// ΔP margin healthy. Both subsystems now reference this constant.
    /// </summary>
    public const double ChamberInjectionBackPressureRatio = 1.18;

    private static readonly ICycleSolver PressureFed      = new PressureFedSolver();
    private static readonly ICycleSolver GasGenerator     = new GasGeneratorSolver();
    private static readonly ICycleSolver ElectricPump     = new ElectricPumpSolver();
    private static readonly ICycleSolver OpenExpander     = new OpenExpanderSolver();
    private static readonly ICycleSolver ClosedExpander   = new ClosedExpanderSolver();
    private static readonly ICycleSolver StagedCombustion = new StagedCombustionSolver();
    private static readonly ICycleSolver FullFlow         = new FullFlowSolver();
    private static readonly ICycleSolver ORSC             = new ORSCSolver();
    private static readonly ICycleSolver TapOff           = new TapOffSolver();

    /// <summary>
    /// Return the solver that answers for <paramref name="cycle"/>.
    /// Throws if the enum value has no solver registered — that's the
    /// compile-time forcing function that prompts a new solver class
    /// when a new cycle is added to the enum.
    /// </summary>
    public static ICycleSolver Get(EngineCycle cycle) => cycle switch
    {
        EngineCycle.PressureFed      => PressureFed,
        EngineCycle.GasGenerator     => GasGenerator,
        EngineCycle.ElectricPump     => ElectricPump,
        EngineCycle.OpenExpander     => OpenExpander,
        EngineCycle.ClosedExpander   => ClosedExpander,
        EngineCycle.StagedCombustion => StagedCombustion,
        EngineCycle.FullFlow         => FullFlow,
        EngineCycle.ORSC             => ORSC,
        EngineCycle.TapOff           => TapOff,
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(cycle), cycle,
            $"No CycleSolver registered for {cycle}. When you add a new EngineCycle value, "
          + "add a matching ICycleSolver subclass in CycleSolver.cs and a case here."),
    };

    // ── Solver implementations ─────────────────────────────────────
    //
    // Kept as stateless singletons inside the registry — the interface
    // is all read-only property getters, so one instance per cycle
    // suffices. Nested private types so nobody outside the registry
    // can instantiate a stray solver and feed it back through Get().

    private sealed class PressureFedSolver : ICycleSolver
    {
        public EngineCycle Cycle                            => EngineCycle.PressureFed;
        public bool        HasFuelRichPreburner             => false;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 0.0;
        public double      FuelRichPreburnerMassFlowFraction => 0.0;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => false;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => false;
        public bool        TurbineDischargeFeedsMainChamber => false;
    }

    private sealed class GasGeneratorSolver : ICycleSolver
    {
        public EngineCycle Cycle                            => EngineCycle.GasGenerator;
        public bool        HasFuelRichPreburner             => true;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 1.20;
        public double      FuelRichPreburnerMassFlowFraction => 0.05;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => false;
    }

    private sealed class ElectricPumpSolver : ICycleSolver
    {
        public EngineCycle Cycle                            => EngineCycle.ElectricPump;
        public bool        HasFuelRichPreburner             => false;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 0.0;
        public double      FuelRichPreburnerMassFlowFraction => 0.0;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => true;
        public bool        HasTurbine                       => false;
        public bool        TurbineDischargeFeedsMainChamber => false;
    }

    private sealed class OpenExpanderSolver : ICycleSolver
    {
        // OpenExpander has no preburner — the regen-heated coolant
        // drives the turbine directly. Classic expander-cycle
        // behaviour; open variant dumps turbine exhaust overboard.
        public EngineCycle Cycle                            => EngineCycle.OpenExpander;
        public bool        HasFuelRichPreburner             => false;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 0.0;
        public double      FuelRichPreburnerMassFlowFraction => 0.0;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => false;   // dumps overboard
    }

    private sealed class ClosedExpanderSolver : ICycleSolver
    {
        // Sprint 23: ClosedExpander mirrors OpenExpander's
        // no-preburner + coolant-driven-turbine topology, with the
        // turbine discharge ducted into the main chamber instead of
        // dumped overboard. Classic for hydrogen engines (RL10, Vinci,
        // BE-3U — all H2/O2). The higher back-pressure reduces
        // specific work vs the open variant at a given inlet state,
        // but no thrust or propellant is lost.
        public EngineCycle Cycle                            => EngineCycle.ClosedExpander;
        public bool        HasFuelRichPreburner             => false;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 0.0;
        public double      FuelRichPreburnerMassFlowFraction => 0.0;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => true;   // into main chamber
    }

    private sealed class StagedCombustionSolver : ICycleSolver
    {
        public EngineCycle Cycle                            => EngineCycle.StagedCombustion;
        public bool        HasFuelRichPreburner             => true;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 1.50;
        public double      FuelRichPreburnerMassFlowFraction => 1.00;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => true;
    }

    private sealed class FullFlowSolver : ICycleSolver
    {
        public EngineCycle Cycle                            => EngineCycle.FullFlow;
        public bool        HasFuelRichPreburner             => true;
        public bool        HasOxRichPreburner               => true;
        public double      PreburnerPcMultiplier            => 1.50;
        // Each preburner sees the full flow on its respective side in
        // FFSC — fuel-rich preburner consumes all the fuel, ox-rich
        // consumes all the ox. The fraction is "1.00 of the fuel-rich
        // side's mass flow", which equals the total fuel mass flow.
        public double      FuelRichPreburnerMassFlowFraction => 1.00;
        public double      OxRichPreburnerMassFlowFraction   => 1.00;
        public bool        UsesFfscDualPreburnerSizing      => true;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => true;
    }

    private sealed class ORSCSolver : ICycleSolver
    {
        // Sprint 24: ox-rich staged combustion. Single ox-rich preburner
        // drives the turbine; fuel goes straight to main injection
        // (not through a preburner). Russian heritage (RD-180, RD-191,
        // RD-253). Sized via PreburnerChamber.Size (single preburner,
        // not SizeFfscDual). TurbineDischargeFeedsMainChamber = true
        // (staged cycle — exhaust goes into the chamber).
        public EngineCycle Cycle                            => EngineCycle.ORSC;
        public bool        HasFuelRichPreburner             => false;
        public bool        HasOxRichPreburner               => true;
        public double      PreburnerPcMultiplier            => 1.50;
        public double      FuelRichPreburnerMassFlowFraction => 0.0;
        public double      OxRichPreburnerMassFlowFraction   => 1.00;
        public bool        UsesFfscDualPreburnerSizing      => false;    // single ox-rich
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => true;
    }

    private sealed class TapOffSolver : ICycleSolver
    {
        // Sprint 25: Tap-off cycle. No preburner — hot gas tapped
        // directly from the main chamber (fuel-film-cooled boundary
        // region) drives the turbine. Heritage: J-2S, BE-4 fuel-rich
        // tap. Open cycle — turbine exhaust dumps overboard (similar
        // routing to GasGenerator + OpenExpander). No preburner flags,
        // no FFSC sizing; the TapOffCycleSizing module computes the
        // turbine energy balance directly from chamber state.
        public EngineCycle Cycle                            => EngineCycle.TapOff;
        public bool        HasFuelRichPreburner             => false;
        public bool        HasOxRichPreburner               => false;
        public double      PreburnerPcMultiplier            => 0.0;
        public double      FuelRichPreburnerMassFlowFraction => 0.0;
        public double      OxRichPreburnerMassFlowFraction   => 0.0;
        public bool        UsesFfscDualPreburnerSizing      => false;
        public bool        HasTurbopump                     => true;
        public bool        HasElectricPowerConverter        => false;
        public bool        HasTurbine                       => true;
        public bool        TurbineDischargeFeedsMainChamber => false;   // dumps overboard
    }
}
