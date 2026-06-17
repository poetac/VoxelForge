// IInjectorElement.cs — Interface contract for injector element models.
//
// Preliminary-design fidelity: each element type is sized by the classical
// orifice equation Q = Cd·A·√(2·ΔP/ρ), not a breakup/combustion model.
// The interface exists so the rest of the codebase (voxel builder,
// thermal solver film-coupling, report export) can consume any element
// type without knowing its internals.
//
// Adding a new element type:
//   1. Create a file in this folder implementing IInjectorElement.
//   2. Add a case to InjectorElementFactory.Create().
//   3. Add any new geometry to ChamberVoxelBuilder's element step.
//
// STOP short of breakup-length / SMD correlation — those belong in a
// future Injector.Combustion namespace, not here.

namespace Voxelforge.Injector.Elements;

/// <summary>
/// Inputs for one-element orifice sizing: pressure drop, propellant
/// densities at injection conditions, and per-element mass flow targets.
/// </summary>
public readonly record struct SizingInputs(
    double DeltaPInj_Pa,           // injector pressure drop
    double OxDensity_kgm3,         // oxidiser density at injector face
    double FuelDensity_kgm3,       // fuel density at injector face
    double OxFlowPerElement_kgs,   // target ox mass flow per element
    double FuelFlowPerElement_kgs, // target fuel mass flow per element
    double CdOx = 0.70,            // discharge coefficient, ox orifice
    double CdFuel = 0.70,          // discharge coefficient, fuel orifice
    // Unlike-doublet impingement half-angle [degrees]. Consumed only
    // by ImpingingDoubletElement; other element types ignore it.
    // Default 20° matches the existing element-class default. Range
    // [10, 45] is checked at consumption.
    double ImpingementHalfAngle_deg = 20.0,
    // Pintle-specific knobs threaded through from the InjectorPattern
    // so they can be SA-tuned / UI-set / JSON-persisted without
    // PintleElement having to carry mutable state. Other element
    // types ignore these; defaults match PintleElement's legacy
    // instance-field defaults bit-identically.
    double PintleDiameter_mm = 12.0,           // central-post diameter, scales with thrust
    int    PintleSleeveHoleCount = 18,         // number of axial sleeve holes around pintle
    double PintleBlockageFractionTarget = 0.60); // Dressler stability centre (band 0.40–0.85)

/// <summary>
/// Orifice geometry and flow characterisation for one element after sizing.
/// </summary>
public readonly record struct OrificeResult(
    double OxOrificeArea_mm2,      // total ox orifice area for this element
    double FuelOrificeArea_mm2,    // total fuel orifice area for this element
    double OxVelocity_ms,          // actual ox jet velocity (Cd · √(2·ΔP/ρ))
    double FuelVelocity_ms,        // actual fuel jet velocity
    double VelocityRatio,          // v_fuel / v_ox
    double MomentumRatio,          // (ṁ_f·v_f) / (ṁ_ox·v_ox) — also "TMR" in Pintle literature
    string[] Notes,
    // Sprint 18 (2026-04-23): blockage factor BL = N · d_sleeve / (π · D_pintle).
    // Populated by PintleElement; 0 for every other element type (not
    // applicable — pintle-specific Dressler stability metric). Consumed
    // by the PINTLE_BLOCKAGE_OUT_OF_BAND feasibility gate.
    double PintleBlockageFraction = 0.0)
{
    /// <summary>Equivalent-circle bore diameter for the oxidiser orifice(s).</summary>
    public double OxEquivDiameter_mm
        => 2.0 * System.Math.Sqrt(OxOrificeArea_mm2 / System.Math.PI);

    /// <summary>Equivalent-circle bore diameter for the fuel orifice(s).</summary>
    public double FuelEquivDiameter_mm
        => 2.0 * System.Math.Sqrt(FuelOrificeArea_mm2 / System.Math.PI);
}

/// <summary>
/// Contract for a single injector element model. Implemented element types
/// provide sizing and geometry metadata; stub types set
/// <see cref="IsImplemented"/> = false and throw on <see cref="Size"/>.
/// </summary>
public interface IInjectorElement
{
    /// <summary>Short identifier used by InjectorElementFactory.</summary>
    string ElementType { get; }

    /// <summary>
    /// True if this element has a working sizing model.
    /// False for stubs — they will throw on <see cref="Size"/>.
    /// </summary>
    bool IsImplemented { get; }

    /// <summary>
    /// Compute orifice areas and flow characterisation for one element.
    /// </summary>
    /// <exception cref="System.NotImplementedException">
    /// Thrown if <see cref="IsImplemented"/> is false.
    /// </exception>
    OrificeResult Size(SizingInputs inputs);
}
