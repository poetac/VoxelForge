// StatefulElectrolyserComponent.cs — Sprint SI.W13 stateful PEM
// electrolyser adapter. Tracks cumulative hydrogen mass produced over
// time given the instantaneous HydrogenProductionRate_kgs.
//
// The SI.W2 ElectrolyserComponent is a static-snapshot wrapper around
// PemElectrolyserSolver. For closed-loop sizing studies ("how big does
// the tank need to be for a 24-hour solar→H₂ duty cycle?"), we need to
// know the time-integral of production rate, not just the
// instantaneous rate. This adapter exposes:
//
//   dM_cumulative/dt = HydrogenProductionRate_kgs(t)
//
// as a single state variable, evolved by TimeStepIntegrator.

using System;
using System.Collections.Generic;
using Voxelforge.Electrolyser;

namespace Voxelforge.Integration.Components;

/// <summary>
/// Stateful PEM electrolyser adapter (Sprint SI.W13). Accumulates the
/// time-integral of HydrogenProductionRate_kgs into a
/// CumulativeHydrogenMass_kg state variable.
/// </summary>
internal sealed class StatefulElectrolyserComponent
    : SystemComponent, IStatefulComponent
{
    private readonly PemElectrolyserDesign _design;
    private readonly double _initialCumulative_kg;
    private double _currentCumulative_kg;

    public StatefulElectrolyserComponent(
        string name,
        PemElectrolyserDesign design,
        double initialCumulativeMass_kg = 0.0)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (initialCumulativeMass_kg < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCumulativeMass_kg),
                "initialCumulativeMass_kg must be ≥ 0.");
        _design               = design;
        _initialCumulative_kg = initialCumulativeMass_kg;
        _currentCumulative_kg = initialCumulativeMass_kg;
    }

    // ── SystemComponent surface ────────────────────────────────────────

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "OperatingCurrentDensity_A_cm2" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StackElectricPower_W", "StackVoltage_V",
            "HydrogenProductionRate_kgs", "HydrogenProductionRate_Nm3_h",
            "HhvEfficiency", "CumulativeHydrogenMass_kg",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        double j = inputs["OperatingCurrentDensity_A_cm2"];
        // Idle short-circuit: the underlying PemElectrolyserDesign validation
        // rejects j ≤ 0, but a stateful run legitimately holds idle (j = 0)
        // periods where the stack draws no current and produces no hydrogen.
        // Skip the solve and emit zeros so the integrator can carry mass
        // forward unchanged.
        if (j <= 0.0)
        {
            outputs["StackElectricPower_W"]         = 0.0;
            outputs["StackVoltage_V"]               = 0.0;
            outputs["HydrogenProductionRate_kgs"]   = 0.0;
            outputs["HydrogenProductionRate_Nm3_h"] = 0.0;
            outputs["HhvEfficiency"]                = 0.0;
            outputs["CumulativeHydrogenMass_kg"]    = _currentCumulative_kg;
            return;
        }
        var d = _design with { OperatingCurrentDensity_A_cm2 = j };
        var r = PemElectrolyserSolver.Solve(d);
        outputs["StackElectricPower_W"]         = r.StackElectricPower_W;
        outputs["StackVoltage_V"]               = r.StackVoltage_V;
        outputs["HydrogenProductionRate_kgs"]   = r.HydrogenProductionRate_kgs;
        outputs["HydrogenProductionRate_Nm3_h"] = r.HydrogenProductionRate_Nm3_h;
        outputs["HhvEfficiency"]                = r.HhvEfficiency;
        outputs["CumulativeHydrogenMass_kg"]    = _currentCumulative_kg;
    }

    // ── IStatefulComponent surface ─────────────────────────────────────

    public IReadOnlyList<string> StateVariables { get; }
        = new[] { "CumulativeHydrogenMass_kg" };

    public void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives)
    {
        // dM/dt = HydrogenProductionRate_kgs.
        derivatives[0] = portOutputs["HydrogenProductionRate_kgs"];
    }

    public void GetInitialState(Span<double> destination)
        => destination[0] = _initialCumulative_kg;

    public void SetState(ReadOnlySpan<double> state)
        => _currentCumulative_kg = state[0];

    public void GetCurrentState(Span<double> destination)
        => destination[0] = _currentCumulative_kg;
}
