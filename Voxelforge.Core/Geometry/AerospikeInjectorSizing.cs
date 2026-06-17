namespace Voxelforge.Geometry;

/// <summary>
/// Result of laying out an aerospike injector — places the chosen
/// element pattern on a single circular ring above the throat plane,
/// reports the resulting pitch geometry and whether the ring fits
/// inside the available throat-station bore with adequate
/// face-to-face clearance.
/// <para>
/// Produced by the aerospike-builder pipeline and surfaced on
/// <see cref="AerospikeBuildResult.InjectorSizing"/>; consumed by the
/// face-thermal solver (<see cref="HeatTransfer.AerospikeInjectorFaceResult"/>)
/// and by the feasibility-gate engine (the
/// <c>AEROSPIKE_ELEMENT_CLEARANCE</c> gate fires when
/// <see cref="ClearanceOk"/> is false).
/// </para>
/// </summary>
/// <param name="PatternSizing">
/// Underlying single-element pattern sizing (orifice diameter, count,
/// per-element flow). Driven by the propellant pair's selected
/// <see cref="Injector.IInjectorPattern"/>.
/// </param>
/// <param name="PitchCircleRadius_mm">
/// Radius of the ring on which element centres are placed, measured
/// from the chamber centreline.
/// </param>
/// <param name="ArcSpacing_mm">
/// Centre-to-centre arc length between adjacent elements on the pitch
/// circle. Used to score density and to spot ring crowding.
/// </param>
/// <param name="ElementOuterDiameter_mm">
/// Outermost diameter of one element (orifice + boss + manifold
/// envelope) — the dimension the clearance check is keyed off.
/// </param>
/// <param name="MinClearance_mm">
/// Minimum face-to-face gap between adjacent element envelopes. Negative
/// values indicate overlap. The clearance check passes when this exceeds
/// the manufacturing-margin floor.
/// </param>
/// <param name="ClearanceOk">
/// True when <see cref="MinClearance_mm"/> meets the manufacturing-margin
/// floor; false otherwise. Drives the
/// <c>AEROSPIKE_ELEMENT_CLEARANCE</c> feasibility gate.
/// </param>
public sealed record AerospikeInjectorSizing(
    Injector.PatternSizingResult PatternSizing,
    double PitchCircleRadius_mm,
    double ArcSpacing_mm,
    double ElementOuterDiameter_mm,
    double MinClearance_mm,
    bool   ClearanceOk);
