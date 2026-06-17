// AntennaConstraintIds.cs — Sprints ANT.W6 + ANT.W7 feasibility-gate
// string constants for the antenna module. Pattern mirrors existing
// pillar constraint-ID registries (e.g. FeasibilityViolation constants
// in rocket / EP evaluators). These IDs are referenced in both the
// voxel builders (geometry-based printability gates) and the RF solver
// (geometry → RF coupling advisory, ANT.W7).

namespace Voxelforge.Antenna;

/// <summary>
/// Canonical string IDs for antenna feasibility gates introduced in
/// Sprints ANT.W6 and ANT.W7.
/// </summary>
internal static class AntennaConstraintIds
{
    // ── ANT.W6 — printability gates ─────────────────────────────────────

    /// <summary>
    /// Wire cross-section diameter is below the minimum feature size for
    /// the selected <see cref="PrintMaterial"/>. Hard gate: the element
    /// cannot be reliably manufactured at the requested wire diameter.
    /// The builder clamps the wire to the material minimum and sets
    /// <see cref="HelicalGeometryResult.WireTooThinForMaterial"/>.
    /// </summary>
    internal const string WireTooThin = "ANTENNA_WIRE_TOO_THIN";

    /// <summary>
    /// One or more antenna elements have an overhang angle that exceeds
    /// the maximum supportable angle for the selected print material
    /// without support structures. Hard gate for FDM/LPBF processes.
    /// </summary>
    internal const string ElementOverhangUnsupported = "ANTENNA_ELEMENT_OVERHANG_UNSUPPORTED";

    /// <summary>
    /// Microstrip patch substrate thickness is below the minimum
    /// printable feature for the selected <see cref="PrintMaterial"/>.
    /// Hard gate: the substrate cannot be reliably printed.
    /// </summary>
    internal const string SubstrateTooThin = "ANTENNA_SUBSTRATE_TOO_THIN";

    // ── ANT.W7 — geometry → RF coupling advisory ─────────────────────────

    /// <summary>
    /// The physical geometry dimensions (coil circumference C/λ, element
    /// spacing, or patch resonant length) do not match the RF design
    /// parameters by more than 5 %. Advisory gate — the antenna will
    /// function, but gain or bandwidth may deviate from the modelled value.
    /// </summary>
    internal const string GeometryRfMismatch = "ANTENNA_GEOMETRY_RF_MISMATCH";
}
