// HybridRocketDesign.cs — Sprint R.W2 hybrid-rocket design record.
//
// Standalone Sprint R.W2 scaffold under Voxelforge.Hybrid. Does NOT
// integrate with the rocket-pillar EngineCycle / PropellantPair /
// RegenChamberOptimization stacks (those touch the rocket schema and
// would force a v31→v32 bump). A follow-on R.W3 sprint will wire
// HybridRocket into the rocket-family IEngine dispatcher.
//
// Concept: single-port circular grain. Liquid oxidiser (default LOX)
// flows down the central port; solid fuel (HTPB or paraffin) pyrolyses
// off the grain inner wall under the hot G_ox boundary layer.
// Regression-rate scaling: r_dot = a · G_ox^n  (Marxman 1963, fit
// constants from Karabeyoglu).
//
// References:
//   Marxman G., Wooldridge C., Muzzy R. (1963). "Fundamentals of
//     Hybrid Boundary-Layer Combustion." AIAA Progress in Astronautics
//     and Aeronautics, 15.
//   Karabeyoglu A., Cantwell B.J., Altman D. (2003). AIAA-2003-4506.
//   Sutton G.P., Biblarz O. (2017). "Rocket Propulsion Elements," 9th
//     ed., chap 16.

using System;

namespace Voxelforge.Hybrid;

/// <summary>
/// Design parameters for a single-port classical hybrid rocket motor
/// (Sprint R.W2 scaffold). Standalone — does not integrate with the
/// rocket-pillar IEngine stack yet (deferred to a future R.W3 sprint).
/// </summary>
/// <param name="Fuel">Solid-grain fuel selection.</param>
/// <param name="GrainLength_m">Axial length of the solid fuel grain [m].</param>
/// <param name="InitialPortRadius_m">Initial radius of the central oxidiser port [m].</param>
/// <param name="OuterGrainRadius_m">
/// Outer radius of the solid fuel grain (= chamber inner radius before
/// the grain is consumed) [m]. The grain is fully consumed when the
/// port reaches this radius (burn-out condition).
/// </param>
/// <param name="OxidiserMassFlow_kgs">Liquid oxidiser mass flow ṁ_ox [kg/s].</param>
/// <param name="ChamberPressure_bar">Chamber stagnation pressure P_c [bar].</param>
/// <param name="ExpansionRatio">Nozzle area ratio ε = A_e / A_t [-].</param>
internal sealed record HybridRocketDesign(
    HybridFuel Fuel,
    double GrainLength_m,
    double InitialPortRadius_m,
    double OuterGrainRadius_m,
    double OxidiserMassFlow_kgs,
    double ChamberPressure_bar,
    double ExpansionRatio)
{
    /// <summary>Validate structural self-consistency of the design record.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When any numeric field is NaN, non-positive (length, radii, mass
    /// flow, chamber pressure), or out of range (<see cref="ExpansionRatio"/>
    /// must be &gt;= 1).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// When the cross-field invariant
    /// <c>InitialPortRadius_m &lt; OuterGrainRadius_m</c> is violated — the
    /// grain has no fuel web (categorical: the geometry is malformed as a
    /// whole, not a single field out of range).
    /// </exception>
    public void ValidateSelf()
    {
        if (double.IsNaN(GrainLength_m) || GrainLength_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(GrainLength_m),
                $"GrainLength_m={GrainLength_m:F4} must be > 0.");
        if (double.IsNaN(InitialPortRadius_m) || InitialPortRadius_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(InitialPortRadius_m),
                $"InitialPortRadius_m={InitialPortRadius_m:F4} must be > 0.");
        if (double.IsNaN(OuterGrainRadius_m) || OuterGrainRadius_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(OuterGrainRadius_m),
                $"OuterGrainRadius_m={OuterGrainRadius_m:F4} must be > 0.");
        if (InitialPortRadius_m >= OuterGrainRadius_m)
            throw new ArgumentException(
                $"InitialPortRadius_m ({InitialPortRadius_m:F4}) must be < "
              + $"OuterGrainRadius_m ({OuterGrainRadius_m:F4}); otherwise the "
              + "grain has no fuel web.",
                nameof(InitialPortRadius_m));
        if (double.IsNaN(OxidiserMassFlow_kgs) || OxidiserMassFlow_kgs <= 0)
            throw new ArgumentOutOfRangeException(nameof(OxidiserMassFlow_kgs),
                $"OxidiserMassFlow_kgs={OxidiserMassFlow_kgs:F4} must be > 0.");
        if (double.IsNaN(ChamberPressure_bar) || ChamberPressure_bar <= 0)
            throw new ArgumentOutOfRangeException(nameof(ChamberPressure_bar),
                $"ChamberPressure_bar={ChamberPressure_bar:F3} must be > 0.");
        if (double.IsNaN(ExpansionRatio) || ExpansionRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(ExpansionRatio),
                $"ExpansionRatio={ExpansionRatio:F3} must be ≥ 1.0.");
    }
}
