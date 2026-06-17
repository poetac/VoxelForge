// HydroTurbineSolver.cs — Sprint HE.W1 closed-form hydroelectric
// turbine performance snapshot.
//
// Stateless, allocation-free, deterministic. Computes P_hydraulic,
// hydraulic + overall efficiency, shaft + electrical power for a hydro
// unit at a specified (kind, H, Q) operating point.
//
//   P_hydraulic = ρ · g · Q · H                  [W]
//   η_turbine   = η_peak · in-envelope-correction
//   η_overall   = η_turbine · η_generator
//   P_shaft     = η_turbine · P_hydraulic
//   P_elec      = η_overall · P_hydraulic
//
// Out-of-envelope penalty: if the design head H sits outside the
// per-kind validity band, η_turbine is linearly de-rated (cluster
// approximation; real off-design operation is more complex but a
// quadratic / Gaussian fit isn't justified at scaffold fidelity).
//
// References:
//   USBR (2005). "Engineering Monograph 39: Selecting Hydraulic
//     Reaction Turbines."
//   ASME PTC 18 (2011). "Hydraulic Turbines and Pump-Turbines."
//   Three Gorges Project — 26 × 700 MW Francis turbine units.

using System;

namespace Voxelforge.Hydroelectric;

/// <summary>
/// Closed-form hydroelectric turbine performance snapshot solver
/// (Sprint HE.W1).
/// </summary>
internal static class HydroTurbineSolver
{
    /// <summary>Standard gravity [m/s²].</summary>
    internal const double G0_ms2 = 9.80665;

    /// <summary>
    /// Maximum off-envelope head-derating factor [-]. A design at exactly
    /// the envelope edge runs at peak η; a design twice as far out as
    /// the envelope width runs at η · (1 - this fraction).
    /// </summary>
    internal const double OffEnvelopeMaxDerating = 0.30;

    /// <summary>
    /// Solve the hydroelectric turbine performance snapshot at the
    /// design (kind, H, Q) operating point.
    /// </summary>
    /// <param name="design">Validated hydroelectric design.</param>
    /// <returns>Solved performance snapshot.</returns>
    internal static HydroTurbineResult Solve(HydroTurbineDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.ValidateSelf();
        var props = HydroTurbineRegistry.For(design.Kind);

        // 1. Hydraulic power = ρ·g·Q·H.
        double P_hydraulic = design.WaterDensity_kgm3
                           * G0_ms2
                           * design.VolumetricFlowRate_m3s
                           * design.Head_m;

        // 2. Hydraulic efficiency from cluster anchor + off-envelope
        //    de-rating.
        bool inEnvelope = design.Head_m >= props.MinimumHead_m
                       && design.Head_m <= props.MaximumHead_m;
        double eta_turbine = props.PeakHydraulicEfficiency
                           * (inEnvelope ? 1.0 : ComputeOffEnvelopePenalty(design.Head_m, props));

        // 3. Power roll-up.
        double P_shaft  = eta_turbine * P_hydraulic;
        double P_elec   = design.GeneratorEfficiency * P_shaft;
        double eta_overall = eta_turbine * design.GeneratorEfficiency;

        return new HydroTurbineResult(
            HydraulicPower_W:      P_hydraulic,
            HydraulicEfficiency:   eta_turbine,
            GeneratorEfficiency:   design.GeneratorEfficiency,
            OverallEfficiency:     eta_overall,
            ShaftPower_W:          P_shaft,
            ElectricalPower_W:     P_elec,
            HeadInValidEnvelope:   inEnvelope);
    }

    /// <summary>
    /// Sprint HE.W2. Auto-select the best-fit hydroelectric turbine
    /// kind for a given hydraulic head. Returns Pelton for high-head
    /// installations, Francis for medium-head (the dominant utility
    /// range), Kaplan for low-head run-of-river.
    /// </summary>
    /// <param name="head_m">Net hydraulic head H [m].</param>
    /// <returns>Best-fit <see cref="HydroTurbineKind"/>.</returns>
    internal static HydroTurbineKind SelectKindForHead(double head_m)
    {
        if (head_m <= 0)
            throw new ArgumentOutOfRangeException(nameof(head_m),
                "head_m must be > 0.");
        if (head_m >= HydroTurbineRegistry.Pelton.MinimumHead_m)
            return HydroTurbineKind.Pelton;
        if (head_m >= HydroTurbineRegistry.Francis.MinimumHead_m)
            return HydroTurbineKind.Francis;
        return HydroTurbineKind.Kaplan;
    }

    /// <summary>
    /// Compute the off-envelope de-rating factor for a head outside the
    /// kind's validity band. Linear in fractional distance past the
    /// envelope edge; clamped at <see cref="OffEnvelopeMaxDerating"/>.
    /// Public-static for tests.
    /// </summary>
    /// <param name="head_m">Design head [m].</param>
    /// <param name="props">Per-kind property registry entry.</param>
    /// <returns>Multiplicative factor on η_peak ∈
    /// [1 − OffEnvelopeMaxDerating, 1.0].</returns>
    internal static double ComputeOffEnvelopePenalty(
        double head_m,
        HydroTurbineProperties props)
    {
        ArgumentNullException.ThrowIfNull(props);
        double envelopeWidth = props.MaximumHead_m - props.MinimumHead_m;
        double distanceOut = head_m < props.MinimumHead_m
            ? props.MinimumHead_m - head_m
            : head_m - props.MaximumHead_m;
        if (distanceOut <= 0) return 1.0;   // in-envelope
        double fraction = distanceOut / envelopeWidth;
        if (fraction > 1.0) fraction = 1.0;
        return 1.0 - OffEnvelopeMaxDerating * fraction;
    }
}
