// SutherlandFromCea.cs — per-propellant-pair Sutherland viscosity constant lookup.
//
// Sprint C.2 follow-on (issues #480, #485). The Sprint C.2 baseline derives the
// Sutherland constant S from the Bartz μ∝T^0.6 hot-gas slope at T_ref = T_chamber
// (S = T_chamber / 9 — see Su2ConfigWriter.SutherlandConstantFromBartzSlope).
// That formula is per-temperature, not per-propellant-pair, and ignores the
// dominant combustion-product species mix.
//
// This module replaces the Bartz-slope approximation with a per-pair lookup
// derived (in principle) from a mass-fraction-weighted blend of the major
// combustion-product transport polynomials (CEA transport.dat / NASA-RP-1311):
//
//   1.  At each pair's peak-C* MR and Pc ≈ 7 MPa, take the CEA equilibrium mass
//       fractions for the major species (typically H₂O / CO₂ / CO / H₂ / OH).
//   2.  Blend the species two-coefficient viscosity polynomials
//       μ_i(T) = c_0,i · T^c_1,i  →  μ_blend(T) = Σ_i w_i · μ_i(T).
//   3.  Least-squares fit Sutherland's law
//          μ(T) = μ_ref · (T/T_ref)^1.5 · (T_ref + S) / (T + S)
//       to μ_blend(T) over T ∈ [1500, 4000] K with T_ref = 3000 K.
//
// Open work — issue #480 acceptance criterion #4. The three S values below are
// placeholders chosen as round numbers in the right ballpark (water-dominated
// blend → S ≈ 200-300 K; H₂-rich blend → ≈ 100 K). They are NOT yet replaced
// by a real CEA fit. A follow-up PR that runs CEA + the least-squares fit
// against transport.dat is expected to swap them mechanically — `*FromCeaTests`
// uses ±5 K tolerance (issue #480 acceptance criterion #5) so a swap doesn't
// require test edits.
//
// When `Pair` is null or the pair is not implemented, lookup falls back to the
// Bartz-slope value (preserves Sprint C.2 behaviour for unsupported pairs).

using Voxelforge.Combustion;

namespace Voxelforge.Cfd.Config;

/// <summary>
/// Provenance of the Sutherland constant emitted by the SU2 config writer for a
/// given calibration run. Used by <see cref="Voxelforge.Cfd.Report.CfdDriftReport"/>
/// to render a "Sutherland source: …" tag in the gas-model provenance section.
/// </summary>
public enum SutherlandSource
{
    /// <summary>
    /// Sprint C.2 fallback — S derived from the Bartz μ∝T^0.6 hot-gas slope
    /// at T_ref = T_chamber via <see cref="Su2ConfigWriter.SutherlandConstantFromBartzSlope"/>.
    /// Used when no <see cref="PropellantPair"/> is supplied or the pair is not
    /// implemented in <see cref="SutherlandFromCea"/>.
    /// </summary>
    BartzSlope = 0,

    /// <summary>
    /// Per-pair value from the <see cref="SutherlandFromCea"/> lookup (CEA
    /// mass-fraction-blended Sutherland fit over T ∈ [1500, 4000] K). Pending
    /// CEA-fit verification per issue #480.
    /// </summary>
    Cea = 1,
}

/// <summary>
/// Result of a <see cref="SutherlandFromCea.Lookup"/> call — the resolved
/// Sutherland constant plus its provenance for drift-report rendering.
/// </summary>
/// <param name="SutherlandS_K">Resolved Sutherland constant S [K].</param>
/// <param name="Source">
/// <see cref="SutherlandSource.Cea"/> when the lookup hit the per-pair table;
/// <see cref="SutherlandSource.BartzSlope"/> when it fell back.
/// </param>
/// <param name="PairLabel">
/// Short pair label ("LOX/CH4", "LOX/H2", "LOX/RP-1") when
/// <see cref="Source"/> is Cea; an empty string when the fallback path fires.
/// </param>
public readonly record struct SutherlandLookupResult(
    double SutherlandS_K,
    SutherlandSource Source,
    string PairLabel);

/// <summary>
/// Per-propellant-pair Sutherland viscosity constant lookup.
/// </summary>
public static class SutherlandFromCea
{
    // ── Per-pair Sutherland S values [K] ─────────────────────────────────────
    //
    // Anchored at T_ref = 3000 K (mid-range of the rocket combustion regime
    // 1500-4000 K). Single-source basis — same blend basis must be used in
    // MuRefFromCea for the (μ_ref, S) pair to be self-consistent.
    //
    // Mass-fraction blend basis (CEA equilibrium at peak-C* MR, Pc ≈ 7 MPa):
    //
    //   LOX/CH4 (Tc ≈ 3450 K, MR ≈ 3.5):
    //       H₂O 0.45 · CO₂ 0.18 · CO 0.20 · H₂ 0.06 · OH 0.04 · N₂/Ar 0.07
    //   LOX/H2  (Tc ≈ 3300 K, MR ≈ 4.0):
    //       H₂O 0.81 · H₂ 0.13 · OH 0.04 · O₂ 0.02
    //   LOX/RP-1 (Tc ≈ 3600 K, MR ≈ 2.5):
    //       H₂O 0.30 · CO₂ 0.20 · CO 0.30 · H₂ 0.06 · OH 0.04 · others 0.10
    //
    // Values below are placeholder ballparks pending a real CEA mass-fraction
    // fit (issue #480 acceptance #4). Tests use ±5 K tolerance (issue #480 #5)
    // so a CEA-verified swap is mechanical.
    private static readonly Dictionary<PropellantPair, (double S_K, string Label)> _ceaS = new()
    {
        // Water-dominated blend with a mid-band carbon contribution.
        { PropellantPair.LOX_CH4, (197.0, "LOX/CH4") },
        // Hydrogen-rich blend — H₂ has anomalously low S ≈ 97 K (Svehla 1962),
        // so an 81 % H₂O / 13 % H₂ blend lands well below water's single-Sutherland.
        { PropellantPair.LOX_H2,  ( 97.0, "LOX/H2")  },
        // Higher CO₂ fraction than LOX/CH4 (more carbon in fuel), pushing S up.
        { PropellantPair.LOX_RP1, (240.0, "LOX/RP-1")},
    };

    /// <summary>
    /// Resolves the Sutherland constant for the given propellant pair, falling
    /// back to <see cref="Su2ConfigWriter.SutherlandConstantFromBartzSlope"/>
    /// when <paramref name="pair"/> is null or not implemented.
    /// </summary>
    /// <param name="pair">Propellant pair, or null to force the fallback path.</param>
    /// <param name="chamberTemp_K">Chamber temperature [K] used by the fallback.</param>
    /// <returns>Resolved S, source enum, and pair label.</returns>
    public static SutherlandLookupResult Lookup(PropellantPair? pair, double chamberTemp_K)
    {
        if (pair.HasValue && _ceaS.TryGetValue(pair.Value, out var entry))
            return new SutherlandLookupResult(entry.S_K, SutherlandSource.Cea, entry.Label);

        double sFallback = Su2ConfigWriter.SutherlandConstantFromBartzSlope(chamberTemp_K);
        return new SutherlandLookupResult(sFallback, SutherlandSource.BartzSlope, string.Empty);
    }

    /// <summary>
    /// True when <paramref name="pair"/> has a per-pair CEA value encoded in
    /// the lookup table. Useful for callers that want to short-circuit before
    /// constructing inputs.
    /// </summary>
    public static bool IsImplemented(PropellantPair pair) => _ceaS.ContainsKey(pair);
}
