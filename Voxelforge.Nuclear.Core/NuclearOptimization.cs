// NuclearOptimization.cs — top-level orchestrator for the nuclear thermal pillar.
//
// Analogous to MarineOptimization on the marine side. Calls each physics
// solver in sequence: cycle → regen cooling → gate evaluation.
//
// Regen pass uses LH2 as both propellant and nozzle coolant (the reactor-
// preheated H2 flows through regen channels before entering the reactor,
// exactly as in NERVA ground-test runs). HydrogenFluid.Instance covers
// the 30–1500 K range needed for the nozzle coolant pass (H2 enters at
// ~80 K and exits at ~300–600 K before the reactor inlet).

using System;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Coolant;
using Voxelforge.Engines;
using Voxelforge.HeatTransfer;
using Voxelforge.Nuclear.Brayton;
using Voxelforge.Nuclear.FuelPin;
using Voxelforge.Nuclear.Optimization;
using Voxelforge.Optimization;

namespace Voxelforge.Nuclear;

/// <summary>
/// Top-level orchestrator for the NERVA-class NTR pillar. Coordinates the
/// lumped thermal cycle, regen nozzle cooling pass, and gate evaluation.
/// </summary>
public static class NuclearOptimization
{
    /// <summary>
    /// Evaluate a nuclear thermal design against operating conditions.
    /// Returns a <see cref="NtrGenerationResult"/> with cycle outputs,
    /// regen nozzle summary, and gate violations.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="design"/> or <paramref name="conditions"/>
    /// is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the family of <paramref name="design"/> or
    /// <paramref name="conditions"/> is not
    /// <see cref="EngineFamilies.Nuclear"/>.
    /// </exception>
    public static NtrGenerationResult GenerateWith(
        NuclearThermalDesign design,
        NuclearThermalConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(conditions);
        // Reject structurally-invalid designs up front. Without this, a
        // degenerate-but-constructible design (e.g. mDot = 0 from a CLI /
        // deserialized caller) reaches the cycle solver, divides by zero, and
        // propagates NaN through core-exit-T — which then slips past every hard
        // gate (NaN > limit is false) so the result reports IsFeasible = true
        // with NaN performance. ValidateSelf was defined but never invoked;
        // wiring it here matches the Marine / EP pillars. SA-optimizer designs
        // are always in-bounds, so this only fires for invalid direct inputs.
        design.ValidateSelf();
        if (design.Family != EngineFamilies.Nuclear)
            throw new ArgumentException(
                $"Design family '{design.Family}' is not '{EngineFamilies.Nuclear}'.",
                nameof(design));
        if (conditions.Family != EngineFamilies.Nuclear)
            throw new ArgumentException(
                $"Conditions family '{conditions.Family}' is not '{EngineFamilies.Nuclear}'.",
                nameof(conditions));

        // ── 1. Thermal cycle ──────────────────────────────────────────────────
        var cycle = NtrCycleSolver.Solve(design, conditions);

        // ── 2. Regen nozzle cooling pass ──────────────────────────────────────
        bool regenWallExceeds = RunRegenCooling(design, cycle, conditions);

        // ── 3. Per-pin heat-conduction model (Sprint NU.W2, when activated). ─
        // The model runs only when the four required fuel-pin fields are
        // populated — Wave-1 designs with all NaN/zero fuel-pin fields skip
        // the entire path and leave the per-pin result fields at NaN.
        FuelPinHeatResult? pinHeat = TryRunFuelPinModel(design, conditions);

        // ── 4. Bimodal Brayton gas loop (Sprint NU.W3, when activated). ──
        // Runs only when Kind = BimodalNtr and BimodalMode != Thrust. The
        // thrust-mode pipeline is bit-identical to NervaSolidCore (cycle +
        // regen + fuel-pin + gates); the Brayton loop adds an electric-
        // power output alongside.
        BraytonGasLoopResult? brayton = TryRunBraytonModel(design);

        // ── 5. Gate evaluation ────────────────────────────────────────────────
        var (violations, advisories) =
            NuclearGates.Evaluate(design, cycle, regenWallExceeds, pinHeat, brayton);

        // Pure-electric mode: the LH₂ thrust is shut off, so override the
        // thrust/Isp result fields to NaN. Hybrid/Thrust modes leave them as
        // computed by NtrCycleSolver.
        bool electricOnly = design.Kind == NuclearKind.BimodalNtr
                         && design.BimodalMode == BimodalMode.Electric;
        double thrust  = electricOnly ? double.NaN : cycle.ThrustVacuum_N;
        double isp     = electricOnly ? double.NaN : cycle.IspVacuum_s;
        double cstar   = electricOnly ? double.NaN : cycle.CStar_ms;

        return new NtrGenerationResult(
            Design:                          design,
            Conditions:                      conditions,
            CoreExitTemp_K:                  cycle.CoreExitTemp_K,
            GammaEff:                        cycle.GammaEff,
            CStar_ms:                        cstar,
            IspVacuum_s:                     isp,
            ThrustVacuum_N:                  thrust,
            VolumetricHeatFlux_MWm3:         cycle.VolumetricHeatFlux_MWm3,
            KEff:                            cycle.KEff,
            RegenNozzleWallTempExceedsLimit: regenWallExceeds,
            Violations:                      violations,
            Advisories:                      advisories,
            IsFeasible:                      violations.Count == 0)
        {
            PeakFuelCenterlineTemp_K = pinHeat?.PeakFuelCenterlineTemp_K ?? double.NaN,
            PinSurfaceTemp_K         = pinHeat?.PinSurfaceTemp_K         ?? double.NaN,
            FuelPinHotChannelFactor  = pinHeat?.HotChannelFactor         ?? double.NaN,
            FuelPinCoolantExitTemp_K = pinHeat?.CoolantExitTemp_K        ?? double.NaN,
            ElectricPowerOutput_kWe  = brayton?.ElectricPowerOutput_kWe  ?? double.NaN,
            BraytonThermalEfficiency = brayton?.ThermalEfficiency        ?? double.NaN,
            BraytonCarnotEfficiency  = brayton?.CarnotEfficiency         ?? double.NaN,
            ReactorPowerToBrayton_MW = brayton?.ReactorPowerToBrayton_MW ?? double.NaN,
            BraytonHeMassFlow_kgs    = brayton?.HeMassFlow_kgs           ?? double.NaN,
        };
    }

    private static BraytonGasLoopResult? TryRunBraytonModel(NuclearThermalDesign design)
    {
        // Activation guard: Kind must be BimodalNtr and mode must request
        // electric power (Electric or Hybrid).
        if (design.Kind != NuclearKind.BimodalNtr) return null;
        if (design.BimodalMode == BimodalMode.Thrust) return null;

        // Required Brayton fields: ElectricPowerTarget, T_hot, P, RPM all > 0.
        if (design.ElectricPowerTarget_kWe   <= 0) return null;
        if (design.BraytonTurbineInletTemp_K <= 0) return null;
        if (design.BraytonHePressure_bar     <= 0) return null;
        if (design.AlternatorRpm             <= 0) return null;

        try
        {
            return BraytonGasLoopSolver.Solve(
                reactorThermalPower_MW:    design.ReactorThermalPower_MW,
                electricPowerTarget_kWe:   design.ElectricPowerTarget_kWe,
                turbineInletTemp_K:        design.BraytonTurbineInletTemp_K,
                hePressure_bar:            design.BraytonHePressure_bar,
                alternatorRpm:             design.AlternatorRpm,
                recuperatorEffectiveness:  design.BraytonRecuperatorEffectiveness);
        }
        catch (Exception ex) when (ex is ArgumentException
                                      or InvalidOperationException
                                      or NotSupportedException
                                      or ArithmeticException)
        {
            // Brayton is opt-in; surface physics-infeasibility failure as
            // no-result rather than crashing the full evaluation. Narrowed
            // (audit 10-errors.md §5.2) so programming-error exceptions
            // (NullReferenceException, IndexOutOfRangeException,
            // OutOfMemoryException, etc.) propagate naturally instead of
            // being masked as tolerable physics failures.
            //   ArgumentException covers ArgumentNullException +
            //     ArgumentOutOfRangeException — the validation throw-sites
            //     in BraytonGasLoopSolver.Solve.
            //   InvalidOperationException covers cycle-failure paths
            //     (CycleNotConvergedException is parented here per audit
            //     §1.1 row 9 / B.8a pattern).
            //   NotSupportedException covers reserved/unimplemented enum
            //     variants per the Marine/EP convention (audit §1.1 row 8).
            //   ArithmeticException (DivideByZeroException, OverflowException)
            //     covers numerical-collapse failures inside the lumped model.
            return null;
        }
    }

    private static FuelPinHeatResult? TryRunFuelPinModel(
        NuclearThermalDesign design,
        NuclearThermalConditions conditions)
    {
        // Activation guard: all required fields must be finite + positive.
        if (double.IsNaN(design.FuelPinDiameter_mm) || design.FuelPinDiameter_mm <= 0) return null;
        if (double.IsNaN(design.FuelPinPitch_mm)    || design.FuelPinPitch_mm    <= 0) return null;
        if (design.FuelPinHexRings  < 1) return null;
        if (design.FuelElementCount < 1) return null;
        if (double.IsNaN(design.FuelPinLength_m) || design.FuelPinLength_m <= 0) return null;

        try
        {
            var hex = HexArrayGeometry.Resolve(
                hexRings:       design.FuelPinHexRings,
                pinDiameter_mm: design.FuelPinDiameter_mm,
                pinPitch_mm:    design.FuelPinPitch_mm);

            return FuelPinHeatModel.Solve(
                reactorThermalPower_W:    design.ReactorThermalPower_MW * 1e6,
                fuelElementCount:         design.FuelElementCount,
                hexGeometry:              hex,
                fuelPinLength_m:          design.FuelPinLength_m,
                coolantMassFlow_kgs:      design.PropellantMassFlow_kgs,
                coolantInletTemp_K:       conditions.PropellantInletTemp_K,
                coolantInletPressure_Pa:  design.ChamberPressure_bar * 1e5,
                hotChannelFactor:         design.FuelPinHotChannelFactor,
                fuelMaterial:             design.FuelMaterial);
        }
        catch (Exception ex) when (ex is ArgumentException
                                      or InvalidOperationException
                                      or NotSupportedException
                                      or ArithmeticException)
        {
            // Fuel-pin model is opt-in; surface physics-infeasibility failure
            // as no-result rather than crashing the full evaluation. The
            // activation guard above already screens out malformed inputs,
            // so an exception here implies a deeper numerical issue.
            //
            // Narrowed (audit 10-errors.md §5.2) so programming-error
            // exceptions (NullReferenceException, IndexOutOfRangeException,
            // OutOfMemoryException, etc.) propagate naturally instead of
            // being masked as tolerable physics failures. The narrow set
            // covers the documented throw-sites in HexArrayGeometry.Resolve
            // and FuelPinHeatModel.Solve (ArgumentNullException +
            // ArgumentOutOfRangeException via ArgumentException base, plus
            // InvalidOperationException for total-pin-resolution failure).
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static bool RunRegenCooling(
        NuclearThermalDesign design,
        NtrCycleResult       cycle,
        NuclearThermalConditions conditions)
    {
        try
        {
            // Build a synthetic contour representing just the nozzle (no combustion
            // chamber barrel); characteristic length 0.5 m is representative of
            // NTR convergent-section volume. Contraction ratio 3.0 is a conservative
            // NTR design point — the reactor face is ~3× the throat area.
            var contour = ChamberContourGenerator.Generate(
                throatRadius_mm:      design.ThroatRadius_mm,
                contractionRatio:     3.0,
                expansionRatio:       design.ExpansionRatio,
                characteristicLength_m: 0.5);

            // Hot H2 gas state at the nozzle stagnation plane (= reactor exit).
            double Pc_Pa = design.ChamberPressure_bar * 1e5;
            var gas = new PropellantState(
                MixtureRatio:      0.0,
                ChamberPressure_Pa: Pc_Pa,
                ChamberTemp_K:     cycle.CoreExitTemp_K,
                GammaChamber:      cycle.GammaEff,
                GammaThroat:       cycle.GammaEff,
                MolecularWeight:   LH2ThermalProperties.MolecularWeight_gmol,
                SpecificGasConst:  LH2ThermalProperties.GasConstant_J_kgK,
                Cp_Jkg:            LH2ThermalProperties.Cp_J_kgK(cycle.CoreExitTemp_K),
                Viscosity_PaS:     LH2ThermalProperties.Viscosity_PaS(cycle.CoreExitTemp_K),
                Prandtl:           LH2ThermalProperties.Prandtl(cycle.CoreExitTemp_K),
                CStar_ms:          cycle.CStar_ms,
                IspVacuum_s:       cycle.IspVacuum_s,
                PropellantName:    "H2 (NTR propellant)");

            // Channel schedule — channel height varies with design parameter;
            // throat height reduced to 60 % of nominal to maintain adequate
            // wall thickness through the high-flux throat region.
            var channels = new ChannelSchedule(
                ChannelCount:              (int)Math.Round(design.RegenChannelCount),
                RibThickness_mm:           1.0,
                GasSideWallThickness_mm:   design.NozzleWallThickness_mm,
                ChannelHeightAtChamber_mm: design.RegenChannelDepth_mm,
                ChannelHeightAtThroat_mm:  design.RegenChannelDepth_mm * 0.60,
                ChannelHeightAtExit_mm:    design.RegenChannelDepth_mm);

            var inputs = new RegenSolverInputs(
                Contour:                contour,
                Gas:                    gas,
                Wall:                   WallMaterials.Inconel718,
                Channels:               channels,
                CoolantMassFlow_kgs:    design.PropellantMassFlow_kgs,
                CoolantInletTemp_K:     conditions.PropellantInletTemp_K,
                CoolantInletPressure_Pa: Pc_Pa,
                CoolantFluid:           CoolantRegistry.Get("H2"));

            var result = RegenCoolingSolver.Solve(inputs);
            return result.WallTempExceedsLimit;
        }
        catch (Exception ex) when (ex is ArgumentException
                                      or InvalidOperationException
                                      or NotSupportedException
                                      or ArithmeticException)
        {
            // Regen is advisory only — surface physics-infeasibility failure
            // as no violation rather than crashing the entire evaluation.
            //
            // Narrowed (audit 10-errors.md §5.2) so programming-error
            // exceptions (NullReferenceException, IndexOutOfRangeException,
            // OutOfMemoryException, etc.) propagate naturally instead of
            // being masked as tolerable physics failures. The narrow set
            // covers the documented throw-sites in
            // ChamberContourGenerator.Generate (ArgumentException),
            // RegenCoolingSolver.Solve (ArgumentException), and
            // CoolantRegistry.Get (InvalidOperationException for an
            // unregistered fluid key).
            return false;
        }
    }
}
