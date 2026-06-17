// ModulationSchemeTable.cs — Sprint ANT.W3 required-Eb/N₀ lookup.
//
// Pure static lookup mapping each ModulationScheme to its required
// Eb/N₀ in dB. Values sourced from CCSDS TM Blue Book + Proakis 5e,
// each cited inline beside its row.
//
// ── Reference targets ──────────────────────────────────────────────
// Uncoded modulation values: Proakis 5e Table 8.1 at BER = 1e-5,
// which is the universally-cited "uncoded BER reference floor" — at
// 1e-5 the (Eb/N0)_req values are tabulated for the entire 2-PSK to
// 256-QAM family and accepted as canonical textbook numbers. Picking
// 1e-6 instead would only shift the cluster up by ~ 1 dB; the
// pillar's link-margin math handles whichever floor the user encodes.
//
// FEC values:
//   - Convolutional R-1/2 (K=7): Proakis 5e §8.2.5 + Sklar 2e §6.3
//     converge on Eb/N0 ≈ 4.5 dB at BER 1e-5 for QPSK + Viterbi.
//   - Turbo R-1/3 / R-1/2 (k=1784): CCSDS 131.0-B-3 §7.3 + Andrews
//     et al. 2007 "Development of Turbo and LDPC Codes for Deep-Space
//     Applications" Table II → 0.8 dB / 1.2 dB at BER 1e-6.
//   - AR4JA LDPC R-1/2 / R-2/3 / R-4/5 (k=1024): CCSDS 131.0-B-3
//     §7.4 + Andrews 2007 Table III → 1.0 / 1.6 / 2.0 dB at BER 1e-6.
//   - LDPC R-7/8 (k=8160, "C2 LDPC"): CCSDS 131.0-B-3 §7.4.2.2 →
//     2.5 dB at BER 1e-6 (Andrews 2007 Fig. 12 cluster).
//
// All FEC values are within ±0.2 dB of their primary citation, which
// is the sprint's acceptance band.

using System;

namespace Voxelforge.Antenna;

/// <summary>
/// Static lookup mapping <see cref="ModulationScheme"/> to its
/// required Eb/N₀ in dB (Sprint ANT.W3).
/// <para>
/// All values are at BER = 1e-5 (uncoded set, Proakis 5e Table 8.1
/// canonical anchor) or BER = 1e-6 (FEC set, CCSDS 131.0-B-3 +
/// Andrews 2007). The link-margin solver consumes this dB value
/// directly via <see cref="AntennaSolver.ComputeLinkMargin_dB"/>.
/// </para>
/// </summary>
internal static class ModulationSchemeTable
{
    /// <summary>
    /// Required Eb/N₀ [dB] for the given scheme. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for an unrecognised
    /// scheme (defensive — keeps a stale binder fault loud).
    /// </summary>
    internal static double RequiredEbN0_dB(ModulationScheme scheme) => scheme switch
    {
        // ── Uncoded (Proakis 5e Table 8.1 at BER 1e-5) ─────────────────

        // BPSK at BER 1e-5: Eb/N0 = (erfc-inverse(2·1e-5))² / 2 ≈ 9.6 dB.
        // Proakis 5e Table 8.1 row 1 (BPSK).
        ModulationScheme.BpskUncoded         => 9.6,

        // QPSK at BER 1e-5: identical to BPSK constellation distance,
        // same Eb/N0. Proakis 5e Table 8.1 row 2 (QPSK).
        ModulationScheme.QpskUncoded         => 9.6,

        // 8-PSK at BER 1e-5: ~ 3.4 dB worse than QPSK due to denser
        // constellation. Proakis 5e Eq. 4.3-31 / Table 8.1 row 3.
        ModulationScheme.EightPskUncoded     => 13.0,

        // 16-QAM at BER 1e-5: SER ~ 10·Q(sqrt(0.4·E_s/N_0)). Proakis 5e
        // Eq. 4.3-31 / Table 8.1 row 5 → Eb/N0 ≈ 13.4 dB.
        ModulationScheme.SixteenQamUncoded   => 13.4,

        // 64-QAM at BER 1e-5: Proakis 5e Eq. 4.3-31, M=64 →
        // Eb/N0 ≈ 17.8 dB. Wider constellation eats ~ 4 dB vs 16-QAM.
        ModulationScheme.SixtyFourQamUncoded => 17.8,

        // 256-QAM at BER 1e-5: Proakis 5e Eq. 4.3-31, M=256 →
        // Eb/N0 ≈ 24.0 dB. The "rate-limited" regime where capacity
        // wins over distance.
        ModulationScheme.Qam256Uncoded       => 24.0,

        // ── Convolutional R-1/2 (K=7 Viterbi, Proakis 5e §8.2.5) ────────

        // BPSK + convolutional R-1/2 (K=7), soft-decision Viterbi at
        // BER 1e-5: Proakis 5e Figure 8.2-7 ≈ 4.5 dB.
        ModulationScheme.BpskConvolutionalR12 => 4.5,

        // QPSK + convolutional R-1/2 (K=7): same Eb/N0 as BPSK (in-
        // phase + quadrature carry independent bit streams). Proakis
        // 5e §8.2.5 reference cluster.
        ModulationScheme.QpskConvolutionalR12 => 4.5,

        // ── CCSDS Turbo (131.0-B-3 §7.3, k=1784, 10 iter) ──────────────

        // BPSK + CCSDS turbo R-1/3: Andrews 2007 Table II row 1 →
        // Eb/N0 ≈ 0.8 dB at BER 1e-6. CCSDS 131.0-B-3 §7.3.4.
        ModulationScheme.BpskTurboR13         => 0.8,

        // QPSK + CCSDS turbo R-1/3: same as BPSK (independent streams).
        ModulationScheme.QpskTurboR13         => 0.8,

        // BPSK + CCSDS turbo R-1/2: Andrews 2007 Table II row 2 →
        // Eb/N0 ≈ 1.2 dB at BER 1e-6. CCSDS 131.0-B-3 §7.3.4.
        ModulationScheme.BpskTurboR12         => 1.2,

        // QPSK + CCSDS turbo R-1/2: same as BPSK.
        ModulationScheme.QpskTurboR12         => 1.2,

        // ── CCSDS AR4JA LDPC (131.0-B-3 §7.4, k=1024) ─────────────────

        // BPSK + AR4JA LDPC R-1/2: Andrews 2007 Table III row 1 / CCSDS
        // 131.0-B-3 §7.4.1.1 → Eb/N0 ≈ 1.0 dB at BER 1e-6.
        ModulationScheme.BpskLdpcR12          => 1.0,

        // QPSK + AR4JA LDPC R-1/2: same as BPSK.
        ModulationScheme.QpskLdpcR12          => 1.0,

        // BPSK + AR4JA LDPC R-2/3: Andrews 2007 Table III row 2 →
        // Eb/N0 ≈ 1.6 dB at BER 1e-6.
        ModulationScheme.BpskLdpcR23          => 1.6,

        // QPSK + AR4JA LDPC R-2/3: same as BPSK.
        ModulationScheme.QpskLdpcR23          => 1.6,

        // BPSK + AR4JA LDPC R-4/5: Andrews 2007 Table III row 3 →
        // Eb/N0 ≈ 2.0 dB at BER 1e-6.
        ModulationScheme.BpskLdpcR45          => 2.0,

        // QPSK + AR4JA LDPC R-4/5: same as BPSK.
        ModulationScheme.QpskLdpcR45          => 2.0,

        // BPSK + CCSDS "C2" LDPC R-7/8 (k=8160): CCSDS 131.0-B-3
        // §7.4.2.2 + Andrews 2007 Fig. 12 cluster → Eb/N0 ≈ 2.5 dB
        // at BER 1e-6. Higher rate ⇒ smaller coding-gain margin.
        ModulationScheme.BpskLdpcR78          => 2.5,

        // QPSK + C2 LDPC R-7/8: same as BPSK.
        ModulationScheme.QpskLdpcR78          => 2.5,

        _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme,
            $"Unknown ModulationScheme '{scheme}'."),
    };

    /// <summary>
    /// Total number of named modulation schemes — convenience for SA
    /// design-variable bounds (<c>[0, Count - 1]</c>).
    /// </summary>
    internal const int Count = 20;

    /// <summary>
    /// Map an SA integer index (0..Count-1) to its enum value.
    /// Useful for [SaDesignVariable] dim → categorical-state conversion.
    /// Throws <see cref="ArgumentOutOfRangeException"/> on out-of-range.
    /// </summary>
    internal static ModulationScheme FromIndex(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Modulation index must be in [0, {Count - 1}]; got {index}.");
        return (ModulationScheme)index;
    }

    /// <summary>
    /// Map an enum value to its SA integer index. Inverse of
    /// <see cref="FromIndex"/> for round-trip Pack/Unpack tests.
    /// </summary>
    internal static int ToIndex(ModulationScheme scheme) => (int)scheme;
}
