// NuclearOptimizationCatchNarrowingTests.cs — verifies that the narrowed
// catch clauses in NuclearOptimization's three opt-in solver paths
// (Brayton gas loop, fuel-pin heat conduction, regen nozzle cooling) let
// programming-error exceptions propagate while still absorbing the
// documented physics-infeasibility throws.
//
// Background: audit 10-errors.md §5.2 (medium severity, issue #587)
// flagged three bare `catch (Exception)` swallows that masked programming
// errors (NullReferenceException, IndexOutOfRangeException, etc.) as if
// they were tolerable physics failures. The refactor narrowed each to
// `catch (Exception ex) when (ex is ArgumentException or InvalidOperation
// Exception or NotSupportedException or ArithmeticException)`, matching
// the Airbreathing convention (RamjetObjective:104, RbccObjective:82,
// ScramjetObjective:73, TurbofanObjective:81, TurbojetObjective:73).
//
// These tests use reflection to invoke the private static helper methods
// directly. That is the only way to verify the catch semantics without
// either modifying production code to add test seams or constructing a
// design that happens to trigger a programming-error path through the
// public surface (which is exactly the scenario the narrowing is there
// to surface).

using System;
using System.Reflection;
using Voxelforge.Nuclear;
using Xunit;

namespace Voxelforge.Nuclear.Tests.Optimization;

public sealed class NuclearOptimizationCatchNarrowingTests
{
    // ── Test designs ─────────────────────────────────────────────────────

    /// <summary>NRX-A6 baseline used in NervaNrxA6Fixture, with the
    /// Wave-2 fuel-pin fields populated so TryRunFuelPinModel activates.</summary>
    private static NuclearThermalDesign MakeFuelPinActivatedDesign() => new(
        Kind:                    NuclearKind.NervaSolidCore,
        ReactorThermalPower_MW:  1100.0,
        ReactorCoreLength_mm:    1400.0,
        ReactorCoreDiameter_mm:  1400.0,
        FuelLoadingFraction:     0.65,
        PropellantMassFlow_kgs:  33.0,
        ChamberPressure_bar:     34.0,
        ThroatRadius_mm:         120.0,
        ExpansionRatio:          100.0,
        NozzleLength_mm:         4000.0,
        RegenChannelDepth_mm:    2.0,
        RegenChannelCount:       200,
        NozzleWallThickness_mm:  1.5,
        NozzleChannelWidth_mm:   3.0,
        NozzleManifoldDepth_mm:  5.0)
    {
        FuelPinDiameter_mm = 2.5,
        FuelPinPitch_mm    = 3.2,
        FuelPinHexRings    = 2,
        FuelElementCount   = 564,
        FuelPinLength_m    = 1.4,
    };

    private static NuclearThermalConditions MakeConditions()
        => new(PropellantInletTemp_K: 80.0, TargetDeltaV_ms: 3000.0);

    // ── Reflection helpers ───────────────────────────────────────────────

    private static MethodInfo PrivateMethod(string name)
    {
        var method = typeof(NuclearOptimization).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private static object? Invoke(MethodInfo method, params object?[] args)
    {
        // TargetInvocationException unwrap: when the invoked method throws,
        // reflection wraps the exception. We rethrow the inner so xUnit can
        // assert the real type.
        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }

    // ── 1. Programming-error propagation (the core regression) ──────────
    //
    // Each of the three wrapped private methods MUST propagate exception
    // types outside the narrow {ArgumentException, InvalidOperation
    // Exception, NotSupportedException, ArithmeticException} set. The
    // canonical programming-error type is NullReferenceException, which
    // arises whenever a field/argument that the catch block author
    // assumed non-null turns out to be null.

    [Fact]
    public void TryRunBraytonModel_NullDesign_PropagatesNullReferenceException()
    {
        // Before the catch was narrowed, the bare `catch (Exception)` would
        // have absorbed the NRE from `design.Kind` and silently returned
        // null. After narrowing, the NRE propagates.
        var method = PrivateMethod("TryRunBraytonModel");
        Assert.Throws<NullReferenceException>(
            () => Invoke(method, new object?[] { null }));
    }

    [Fact]
    public void TryRunFuelPinModel_NullDesign_PropagatesNullReferenceException()
    {
        // Same rationale. `design.FuelPinDiameter_mm` is the first deref;
        // an NRE there used to be swallowed as "no-result".
        var method = PrivateMethod("TryRunFuelPinModel");
        Assert.Throws<NullReferenceException>(
            () => Invoke(method, new object?[] { null, MakeConditions() }));
    }

    [Fact]
    public void TryRunFuelPinModel_NullConditions_PropagatesNullReferenceException()
    {
        // The activation guard above the catch reads only design-side
        // fields, so a null conditions argument flows through to
        // `conditions.PropellantInletTemp_K` inside the try block. Before
        // the refactor that NRE was masked as a no-result; after, it
        // propagates so the caller learns about the missing argument.
        var method = PrivateMethod("TryRunFuelPinModel");
        Assert.Throws<NullReferenceException>(
            () => Invoke(method, new object?[] { MakeFuelPinActivatedDesign(), null }));
    }

    // ── 2. Physics-infeasibility absorption (regression: still graceful) ─
    //
    // The narrowing must preserve the legitimate +∞ penalty path for
    // physics validation failures. ArgumentOutOfRangeException is the
    // canonical physics-infeasibility throw across the pillar Cores
    // (audit §1.2: "must be positive; got {x}." form). A design that
    // pushes a Brayton parameter outside its physical envelope must still
    // produce a no-result return rather than crashing.

    [Fact]
    public void TryRunBraytonModel_RecuperatorOutOfRange_StillReturnsNull()
    {
        // BraytonGasLoopSolver.Solve throws ArgumentOutOfRangeException
        // when recuperatorEffectiveness is finite and outside [0, 1].
        // The NuclearOptimization activation guard does NOT pre-screen
        // this field (it only checks the four mandatory ones), so the
        // throw escapes BraytonGasLoopSolver and lands in the narrowed
        // catch. ArgumentOutOfRangeException : ArgumentException, so the
        // narrow `when (ex is ArgumentException ...)` filter still
        // absorbs it and the method returns null.
        var design = MakeFuelPinActivatedDesign() with
        {
            // Activate the Brayton path (Kind + Mode + 4 required fields > 0).
            Kind                            = NuclearKind.BimodalNtr,
            BimodalMode                     = BimodalMode.Electric,
            ElectricPowerTarget_kWe         = 25.0,
            BraytonTurbineInletTemp_K       = 1500.0,
            BraytonHePressure_bar           = 50.0,
            AlternatorRpm                   = 36000.0,
            // Push recuperator effectiveness above 1.0 — physically
            // impossible, triggers ArgumentOutOfRangeException inside
            // BraytonGasLoopSolver.Solve.
            BraytonRecuperatorEffectiveness = 2.0,
        };
        var method = PrivateMethod("TryRunBraytonModel");
        var result = Invoke(method, new object?[] { design });
        Assert.Null(result);
    }

    // ── 3. GenerateWith end-to-end: regression on the baseline path ─────
    //
    // Sanity: the NRX-A6 baseline that NervaNrxA6Fixture covers must
    // still complete cleanly. The catch narrowing is a defensive
    // refactor; it must not change behaviour for any design that hits
    // only the documented physics-infeasibility paths.

    [Fact]
    public void GenerateWith_NrxA6Baseline_StillFeasible()
    {
        var design = MakeFuelPinActivatedDesign();
        var cond   = MakeConditions();
        var result = NuclearOptimization.GenerateWith(design, cond);
        Assert.True(result.IsFeasible,
            $"Expected feasible NRX-A6 baseline after catch narrowing. "
          + $"Violations: {string.Join(", ", result.Violations)}");
    }
}
