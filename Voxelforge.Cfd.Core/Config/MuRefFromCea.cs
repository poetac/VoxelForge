// MuRefFromCea.cs — per-propellant-pair Sutherland reference viscosity lookup.
//
// Sister module to <see cref="SutherlandFromCea"/> (issues #480, #485). Same
// scope: replace the per-temperature `μ = 1.0e-4 · (Tc/3500)^0.7` formula in
// `Voxelforge.Core.Combustion.CeaTable2DBase` (which does NOT distinguish
// LOX/CH4 from LOX/H2 from LOX/RP-1) with a per-pair lookup derived from the
// same CEA mass-fraction blend basis used by SutherlandFromCea.
//
// Why CFD-local override instead of editing CeaTable2DBase. The rocket pillar
// consumes `gas.Viscosity_PaS` from `PropellantState` for Bartz HTC, regen
// jacket sizing, and structural margins. Changing it across the pillar moves
// every regen-cooling result. The SU2 config write site (issue #485) is the
// only place where a per-pair μ_ref materially affects outputs (it sets
// SU2's MU_REF Sutherland anchor). A future trigger for promoting per-pair
// μ_ref into PropellantState itself: when a second consumer (regen-side
// viscosity refinement, air-breathing CFD) also needs per-pair values.
//
// A single CEA mass-fraction-blended viscosity polynomial fit produces
// (μ_ref, S) jointly — this file and SutherlandFromCea.cs share the same
// blend basis (peak-C* MR, Pc ≈ 7 MPa, T_ref = 3000 K).

using Voxelforge.Combustion;

namespace Voxelforge.Cfd.Config;

/// <summary>
/// Provenance of the reference viscosity emitted by the SU2 config writer for a
/// given calibration run. Used by <see cref="Voxelforge.Cfd.Report.CfdDriftReport"/>
/// to render a "Viscosity reference: …" tag in the gas-model provenance section.
/// </summary>
public enum MuRefSource
{
    /// <summary>
    /// Fallback — μ_ref pulled from <see cref="PropellantState.Viscosity_PaS"/>,
    /// which today is computed by <c>Voxelforge.Core/Combustion/CeaTable2DBase</c>
    /// via the per-temperature formula <c>μ = 1.0e-4 · (Tc/3500)^0.7</c>. Used
    /// when no <see cref="PropellantPair"/> is supplied or the pair is not
    /// implemented in <see cref="MuRefFromCea"/>.
    /// </summary>
    CeaTableFormula = 0,

    /// <summary>
    /// Per-pair value from the <see cref="MuRefFromCea"/> lookup (CEA
    /// mass-fraction-blended Sutherland fit over T ∈ [1500, 4000] K). Pending
    /// CEA-fit verification per issue #485.
    /// </summary>
    Cea = 1,
}

/// <summary>
/// Result of a <see cref="MuRefFromCea.Lookup"/> call — the resolved μ_ref
/// plus its provenance for drift-report rendering.
/// </summary>
/// <param name="MuRef_PaS">Resolved reference viscosity μ_ref [Pa·s].</param>
/// <param name="Source">
/// <see cref="MuRefSource.Cea"/> when the lookup hit the per-pair table;
/// <see cref="MuRefSource.CeaTableFormula"/> when it fell back.
/// </param>
/// <param name="PairLabel">
/// Short pair label ("LOX/CH4", "LOX/H2", "LOX/RP-1") when
/// <see cref="Source"/> is Cea; an empty string when the fallback path fires.
/// </param>
public readonly record struct MuRefLookupResult(
    double MuRef_PaS,
    MuRefSource Source,
    string PairLabel);

/// <summary>
/// Per-propellant-pair Sutherland reference viscosity lookup.
/// </summary>
public static class MuRefFromCea
{
    // ── Per-pair μ_ref values [Pa·s] at T_ref = T_chamber ────────────────────
    //
    // Anchored at T_ref = T_chamber (Sutherland's law as written in SU2 v8 sets
    // MU_T_REF to the same temperature). Same mass-fraction blend basis as
    // SutherlandFromCea.cs — see that file for blend documentation.
    //
    // Values below are placeholder ballparks (within ~10 % of the existing
    // CeaTable2DBase per-temperature formula) pending a real CEA mass-fraction
    // fit (issue #485). Directionally:
    //   • LOX/CH4 — close to the formula baseline (water + carbon mix, ~9.5e-5).
    //   • LOX/H2 — lower because the H₂-rich blend has lighter species that
    //              drag μ down (H₂ μ is ~3× lower than H₂O μ at T = 3000 K).
    //   • LOX/RP-1 — slightly higher because the carbon-rich CO/CO₂ blend
    //                shifts μ up vs LOX/CH4 at the same T_ref.
    //
    // Tests in MuRefFromCeaTests.cs use ±10 % tolerance so a CEA-verified swap
    // is mechanical (mirrors issue #480's ±5 K tolerance for S).
    private static readonly Dictionary<PropellantPair, (double MuRef_PaS, string Label)> _ceaMuRef = new()
    {
        { PropellantPair.LOX_CH4, (9.50e-5, "LOX/CH4") },
        { PropellantPair.LOX_H2,  (8.50e-5, "LOX/H2")  },
        { PropellantPair.LOX_RP1, (1.05e-4, "LOX/RP-1")},
    };

    /// <summary>
    /// Resolves the Sutherland reference viscosity μ_ref for the given pair,
    /// falling back to <paramref name="fallback_PaS"/> (typically
    /// <see cref="PropellantState.Viscosity_PaS"/>) when <paramref name="pair"/>
    /// is null or not implemented.
    /// </summary>
    /// <param name="pair">Propellant pair, or null to force the fallback.</param>
    /// <param name="fallback_PaS">
    /// Fallback μ_ref [Pa·s] — typically <c>gas.Viscosity_PaS</c>. Used when
    /// the pair lookup misses.
    /// </param>
    /// <returns>Resolved μ_ref, source enum, and pair label.</returns>
    public static MuRefLookupResult Lookup(PropellantPair? pair, double fallback_PaS)
    {
        if (pair.HasValue && _ceaMuRef.TryGetValue(pair.Value, out var entry))
            return new MuRefLookupResult(entry.MuRef_PaS, MuRefSource.Cea, entry.Label);

        return new MuRefLookupResult(fallback_PaS, MuRefSource.CeaTableFormula, string.Empty);
    }

    /// <summary>
    /// True when <paramref name="pair"/> has a per-pair CEA value encoded in
    /// the lookup table. Mirrors <see cref="SutherlandFromCea.IsImplemented"/>.
    /// </summary>
    public static bool IsImplemented(PropellantPair pair) => _ceaMuRef.ContainsKey(pair);
}
