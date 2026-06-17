// ModulationScheme.cs — Sprint ANT.W3 modulation + FEC discriminator.
//
// First-class enumeration of the (modulation, coding) combinations the
// link-budget solver supports as a categorical design variable.
//
// ── Why a combined enum (modulation × coding) ──────────────────────
// The CCSDS TM Blue Book (131.0-B-3) defines a discrete enumerated set
// of valid (modulation, code) pairs — not a free Cartesian product.
// LDPC R-1/2 at k=1024 information bits is a real CCSDS table entry;
// "BPSK + Ldpc-R-7/8 + k=4096" is NOT in the blue book and would
// produce a spurious SA candidate if we kept the dimensions split.
// The combined enum lets a single [SaDesignVariable] integer-indexed
// dim sample valid configurations only.
//
// ── Selection ──────────────────────────────────────────────────────
// Wave-1 set (sprint ANT.W3) — 20 named combinations:
//
//   Uncoded modulation (Proakis 5e Table 8.1 at BER = 1e-5):
//     BpskUncoded, QpskUncoded, EightPskUncoded,
//     SixteenQamUncoded, SixtyFourQamUncoded, Qam256Uncoded
//
//   Convolutional coding rate 1/2 (Proakis 5e §8.2.5, K=7):
//     BpskConvolutionalR12, QpskConvolutionalR12
//
//   Turbo coding (CCSDS 131.0-B-3 §7, k=1784):
//     BpskTurboR13, QpskTurboR13, BpskTurboR12, QpskTurboR12
//
//   CCSDS AR4JA LDPC codes (131.0-B-3 §7.4, k=1024):
//     BpskLdpcR12,  QpskLdpcR12,
//     BpskLdpcR23,  QpskLdpcR23,
//     BpskLdpcR45,  QpskLdpcR45,
//     BpskLdpcR78,  QpskLdpcR78
//
// Required Eb/N₀ values are looked up via ModulationSchemeTable, NOT
// stored on the enum (keeps the enum a pure tag; sources + citations
// concentrate in the table file).
//
// References:
//   CCSDS 131.0-B-3 (2022). TM Synchronization and Channel Coding.
//   Proakis J. (2007). Digital Communications, 5th ed., Table 8.1.

namespace Voxelforge.Antenna;

/// <summary>
/// Digital modulation + forward-error-correction (FEC) combination
/// used by the RF-link budget solver (Sprint ANT.W3).
/// <para>
/// Each named value identifies a specific (modulation, coding-rate)
/// pair drawn from either CCSDS TM Blue Book 131.0-B-3 or Proakis
/// 5e Table 8.1. Required Eb/N₀ values live in
/// <see cref="ModulationSchemeTable"/>.
/// </para>
/// </summary>
internal enum ModulationScheme
{
    // ── Uncoded modulation (Proakis 5e Table 8.1 at BER = 1e-5) ────────

    /// <summary>BPSK, uncoded. Required Eb/N₀ ≈ 9.6 dB at BER 1e-5.</summary>
    BpskUncoded            = 0,

    /// <summary>QPSK, uncoded. Same Eb/N₀ as BPSK (same constellation distance).</summary>
    QpskUncoded            = 1,

    /// <summary>8-PSK, uncoded. Required Eb/N₀ ≈ 13.0 dB at BER 1e-5.</summary>
    EightPskUncoded        = 2,

    /// <summary>16-QAM, uncoded. Required Eb/N₀ ≈ 13.4 dB at BER 1e-5.</summary>
    SixteenQamUncoded      = 3,

    /// <summary>64-QAM, uncoded. Required Eb/N₀ ≈ 17.8 dB at BER 1e-5.</summary>
    SixtyFourQamUncoded    = 4,

    /// <summary>256-QAM, uncoded. Required Eb/N₀ ≈ 24.0 dB at BER 1e-5.</summary>
    Qam256Uncoded          = 5,

    // ── Convolutional R-1/2 (Proakis 5e §8.2.5, K=7) ────────────────────

    /// <summary>BPSK + convolutional R-1/2, K=7 Viterbi. Eb/N₀ ≈ 4.5 dB.</summary>
    BpskConvolutionalR12   = 6,

    /// <summary>QPSK + convolutional R-1/2, K=7 Viterbi. Eb/N₀ ≈ 4.5 dB.</summary>
    QpskConvolutionalR12   = 7,

    // ── Turbo codes (CCSDS 131.0-B-3 §7, k=1784, 10 iterations) ─────────

    /// <summary>BPSK + CCSDS turbo R-1/3, k=1784. Eb/N₀ ≈ 0.8 dB.</summary>
    BpskTurboR13           = 8,

    /// <summary>QPSK + CCSDS turbo R-1/3, k=1784. Eb/N₀ ≈ 0.8 dB.</summary>
    QpskTurboR13           = 9,

    /// <summary>BPSK + CCSDS turbo R-1/2, k=1784. Eb/N₀ ≈ 1.2 dB.</summary>
    BpskTurboR12           = 10,

    /// <summary>QPSK + CCSDS turbo R-1/2, k=1784. Eb/N₀ ≈ 1.2 dB.</summary>
    QpskTurboR12           = 11,

    // ── CCSDS AR4JA LDPC (131.0-B-3 §7.4, k=1024) ──────────────────────

    /// <summary>BPSK + CCSDS AR4JA LDPC R-1/2, k=1024. Eb/N₀ ≈ 1.0 dB.</summary>
    BpskLdpcR12            = 12,

    /// <summary>QPSK + CCSDS AR4JA LDPC R-1/2, k=1024. Eb/N₀ ≈ 1.0 dB.</summary>
    QpskLdpcR12            = 13,

    /// <summary>BPSK + CCSDS AR4JA LDPC R-2/3, k=1024. Eb/N₀ ≈ 1.6 dB.</summary>
    BpskLdpcR23            = 14,

    /// <summary>QPSK + CCSDS AR4JA LDPC R-2/3, k=1024. Eb/N₀ ≈ 1.6 dB.</summary>
    QpskLdpcR23            = 15,

    /// <summary>BPSK + CCSDS AR4JA LDPC R-4/5, k=1024. Eb/N₀ ≈ 2.0 dB.</summary>
    BpskLdpcR45            = 16,

    /// <summary>QPSK + CCSDS AR4JA LDPC R-4/5, k=1024. Eb/N₀ ≈ 2.0 dB.</summary>
    QpskLdpcR45            = 17,

    /// <summary>BPSK + CCSDS AR4JA LDPC R-7/8, k=1024. Eb/N₀ ≈ 2.5 dB.</summary>
    BpskLdpcR78            = 18,

    /// <summary>QPSK + CCSDS AR4JA LDPC R-7/8, k=1024. Eb/N₀ ≈ 2.5 dB.</summary>
    QpskLdpcR78            = 19,
}
