// StatefulHydrogenStorageComponent.cs — Sprint SI.W6 stateful H₂ tank
// adapter. Tracks cumulative stored mass over time given a per-tick
// HydrogenInflowRate_kgs input (positive = filling, negative = drain).
//
// The classical SI.W2 HydrogenStorageComponent computes a STATIC
// snapshot of stored mass given the tank's design pressure / volume /
// temperature. This stateful variant adds a single state variable
// (currentStoredMass_kg) that evolves via:
//
//   dm/dt = inflow_kgs − boilOff_kgs
//
// For compressed-gas + metal-hydride tanks, boilOff_kgs = 0; for
// cryogenic LH₂, the design's HeatLeakRate_W drives a continuous
// boil-off.

using System;
using System.Collections.Generic;
using Voxelforge.HydrogenStorage;

namespace Voxelforge.Integration.Components;

/// <summary>
/// Stateful adapter for the Hydrogen Storage pillar. Carries the
/// instantaneous stored-mass over time given an inflow rate.
/// </summary>
internal sealed class StatefulHydrogenStorageComponent
    : SystemComponent, IStatefulComponent
{
    private readonly HydrogenStorageDesign _design;
    private readonly double _initialMass_kg;
    private double _currentMass_kg;

    public StatefulHydrogenStorageComponent(
        string name,
        HydrogenStorageDesign design,
        double initialStoredMass_kg)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (initialStoredMass_kg < 0)
            throw new ArgumentOutOfRangeException(nameof(initialStoredMass_kg),
                "initialStoredMass_kg must be ≥ 0.");
        _design          = design;
        _initialMass_kg  = initialStoredMass_kg;
        _currentMass_kg  = initialStoredMass_kg;
    }

    // ── SystemComponent surface ────────────────────────────────────────

    public override IReadOnlyList<string> InputPorts { get; }
        = new[] { "HydrogenInflowRate_kgs" };

    public override IReadOnlyList<string> OutputPorts { get; }
        = new[]
        {
            "StoredHydrogenMass_kg", "BoilOffRate_kgs",
            "NetMassFlowRate_kgs",
        };

    public override void Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        IDictionary<string, double> outputs)
    {
        double inflow = inputs["HydrogenInflowRate_kgs"];
        var staticResult = HydrogenStorageSolver.Solve(_design);
        double boilOff = staticResult.BoilOffRate_kgs;
        outputs["StoredHydrogenMass_kg"] = _currentMass_kg;
        outputs["BoilOffRate_kgs"]       = boilOff;
        outputs["NetMassFlowRate_kgs"]   = inflow - boilOff;
    }

    // ── IStatefulComponent surface ─────────────────────────────────────

    public IReadOnlyList<string> StateVariables { get; }
        = new[] { "StoredHydrogenMass_kg" };

    public void ComputeDerivatives(
        ReadOnlySpan<double> state,
        IReadOnlyDictionary<string, double> portInputs,
        IReadOnlyDictionary<string, double> portOutputs,
        Span<double> derivatives)
    {
        // dm/dt = inflow − boilOff. The Evaluate above writes both
        // pieces to the output dictionary; we can read them here.
        derivatives[0] = portOutputs["NetMassFlowRate_kgs"];
    }

    public void GetInitialState(Span<double> destination)
        => destination[0] = _initialMass_kg;

    public void SetState(ReadOnlySpan<double> state)
        => _currentMass_kg = state[0];

    public void GetCurrentState(Span<double> destination)
        => destination[0] = _currentMass_kg;
}
