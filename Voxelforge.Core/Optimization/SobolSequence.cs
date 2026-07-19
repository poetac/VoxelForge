// Sprint T1.2 (2026-04-25): Sobol low-discrepancy sequence generator.
//
// Replaces the Optimizer's `Random`-based initial-candidate generation
// with a quasi-Monte-Carlo low-discrepancy sequence. Sobol points cover
// the unit hypercube more uniformly than uniform random samples — the
// star-discrepancy goes as O((log N)^d / N) instead of uniform's
// O(1/√N), so for D=24 dimensions and N=64 initial candidates the
// Sobol coverage is materially better than uniform sampling.
//
// Why this matters for SA: the first ~64 SA candidates set the basin
// of attraction. Better initial coverage → faster time-to-feasible
// → fewer SA iterations wasted in pre-convergence flailing. Audit
// (CLAUDE.md "T1.2") estimates 1.5-3× faster time-to-first-feasible.
//
// Implementation: classical Sobol with Joe-Kuo (2008) direction numbers.
// Direction numbers for the first 8 dimensions (MaxBakedDimensions) are
// baked in below, transcribed from the Joe-Kuo reference table
// "new-joe-kuo-6.21201". Voxelforge's SA registry is ~24-26 dims, so
// dims 8+ DO exercise the fallback below in normal use — it is not a
// dead code path. That fallback is a pseudo-stratified hash-based
// sequence (see ComputeDirectionNumbers), not genuine Sobol or Halton;
// it gives "better than uniform" coverage on the mostly-categorical/
// gated tail dims but should not be described as either by name (issue
// #80 — the previous header claimed "Halton-style", which the fallback
// code itself never actually implements).
//
// Slicing: each chain in T1.1's multi-chain SA gets a non-overlapping
// stride of the Sobol sequence so chains explore distinct regions.
// Slice i of S sources at i, i+S, i+2S, … positions in the global
// Sobol stream. This preserves the low-discrepancy property within
// each slice and ensures chains don't redundantly cover the same area.
//
// References:
//   Sobol, I.M. (1967). "On the distribution of points in a cube and
//     the approximate evaluation of integrals."
//   Joe, S. & Kuo, F. (2008). "Constructing Sobol sequences with better
//     two-dimensional projections." SIAM J. Sci. Comput. 30(5).
//   Bratley, P. & Fox, B. (1988). "Algorithm 659: Implementing Sobol's
//     quasirandom sequence generator." ACM TOMS 14(1).

using System;

namespace Voxelforge.Optimization;

/// <summary>
/// Sobol low-discrepancy sequence generator. Returns points in [0,1)^D
/// distributed more uniformly than uniform random samples for the same
/// N. Used by <see cref="MultiChainOptimizer"/> to seed each chain's
/// initial-candidate population with a non-overlapping stride.
/// </summary>
public sealed class SobolSequence
{
    private const int MaxBits = 30;          // 2^30 ≈ 1e9 points before exhaustion
    private const int MaxBakedDimensions = 8; // first 8 dims have Joe-Kuo direction numbers baked in
    private readonly int _dimensions;
    private readonly uint[][] _v;            // direction numbers: _v[d][bit]
    private uint[] _x;                       // running XOR state per dim
    private long _index;                     // points generated so far

    /// <summary>
    /// Constructs a Sobol generator for the given dimension count. The
    /// first point returned (after Reset) is at index 0 = origin (all
    /// zeros); typical usage skips that by calling Next() once and
    /// discarding, OR calls SkipTo(1) before consuming.
    /// </summary>
    public SobolSequence(int dimensions)
    {
        if (dimensions < 1) throw new ArgumentOutOfRangeException(nameof(dimensions), "dimensions must be ≥ 1");
        _dimensions = dimensions;
        _v = new uint[dimensions][];
        _x = new uint[dimensions];
        for (int d = 0; d < dimensions; d++)
        {
            _v[d] = ComputeDirectionNumbers(d);
        }
        _index = 0;
    }

    /// <summary>Total Sobol points generated so far (i.e. the index of the next point).</summary>
    public long Index => _index;

    /// <summary>
    /// Advance the generator to global index <paramref name="targetIndex"/>
    /// without producing intermediate points. Used to position a chain's
    /// slice start.
    /// </summary>
    public void SkipTo(long targetIndex)
    {
        if (targetIndex < 0) throw new ArgumentOutOfRangeException(nameof(targetIndex));
        // Reset and re-apply Gray-code XOR up to targetIndex.
        Array.Clear(_x, 0, _x.Length);
        _index = 0;
        for (long i = 0; i < targetIndex; i++)
        {
            AdvanceOne();
        }
    }

    /// <summary>
    /// Returns the next Sobol point in [0,1)^D. Each call advances the
    /// generator's internal index by 1.
    /// </summary>
    public double[] Next()
    {
        AdvanceOne();
        var pt = new double[_dimensions];
        for (int d = 0; d < _dimensions; d++)
        {
            pt[d] = _x[d] / (double)(1u << MaxBits);
        }
        return pt;
    }

    private void AdvanceOne()
    {
        // Gray-code Sobol increment: find lowest zero bit of _index,
        // XOR each dim's running state with the corresponding direction
        // number for that bit position. See Bratley-Fox 1988 §3.
        long c = 0;
        long n = _index;
        while ((n & 1) != 0)
        {
            n >>= 1;
            c++;
        }
        if (c >= MaxBits) throw new InvalidOperationException(
            $"Sobol sequence exhausted at index {_index} (max ~{1L << MaxBits} points).");
        for (int d = 0; d < _dimensions; d++)
        {
            _x[d] ^= _v[d][c];
        }
        _index++;
    }

    // ─── Joe-Kuo direction numbers ─────────────────────────────────────
    //
    // Issue #80: this table previously mis-transcribed the genuine
    // Joe-Kuo (2008) "new-joe-kuo-6.21201" reference table — dim 3's
    // slot paired one row's `a` with the NEXT row's `m`-list (a=2 fused
    // with m={1,1,3,3}), decoding to x⁴+x²+1 = (x²+x+1)² over GF(2),
    // which is reducible and therefore never a valid Sobol polynomial.
    // Dims 4-7 didn't match any genuine Joe-Kuo row at all. Retranscribed
    // 2026-07-19 directly from the reference table (cross-checked against
    // two independent mirrors of the original web.maths.unsw.edu.au/
    // ~fkuo/sobol/ source: github.com/diku-dk/sobol-futhark and
    // github.com/joe-kuo/sobol_data, byte-identical for d=2..9) and
    // verified primitive by hand (also see SobolSequencePropertyTests,
    // which checks this by brute-force GF(2) multiplicative order).
    //
    // Source table's dimension d is 1-indexed with d=1 the trivial van
    // der Corput sequence (this class's dim=0); this class's dim=k for
    // k≥1 is therefore source dimension d=k+1.
    //
    // Direction-number format: m_i ∈ {1, 3, 5, ...} (odd integers <
    // 2^i). Direction number v_i = m_i × 2^(MaxBits - i).
    //
    // Polynomials (a) and m-values (m) per dim (source d = dim+1):
    //   dim=0 (d=1): trivial (van der Corput on base 2), all m_i = 1
    //   dim=1 (d=2): a=0, m = {1}
    //   dim=2 (d=3): a=1, m = {1, 3}
    //   dim=3 (d=4): a=1, m = {1, 3, 1}
    //   dim=4 (d=5): a=2, m = {1, 1, 1}
    //   dim=5 (d=6): a=1, m = {1, 1, 3, 3}
    //   dim=6 (d=7): a=4, m = {1, 3, 5, 13}
    //   dim=7 (d=8): a=2, m = {1, 1, 5, 5, 17}

    // internal (not private): SobolSequencePropertyTests (Core.Tests, a
    // friend assembly via InternalsVisibleTo) verifies these directly
    // against the source table rather than a copy that could drift.
    internal static readonly uint[] s_polynomials =
    {
        0,  // dim 0 — trivial
        0, 1, 1, 2, 1, 4, 2,
    };

    internal static readonly uint[][] s_mValues =
    {
        Array.Empty<uint>(),                  // dim 0 — trivial
        new uint[] { 1 },                     // dim 1 (source d=2)
        new uint[] { 1, 3 },                  // dim 2 (source d=3)
        new uint[] { 1, 3, 1 },               // dim 3 (source d=4)
        new uint[] { 1, 1, 1 },               // dim 4 (source d=5)
        new uint[] { 1, 1, 3, 3 },            // dim 5 (source d=6)
        new uint[] { 1, 3, 5, 13 },           // dim 6 (source d=7)
        new uint[] { 1, 1, 5, 5, 17 },        // dim 7 (source d=8)
    };

    private static uint[] ComputeDirectionNumbers(int dim)
    {
        var v = new uint[MaxBits];
        if (dim < MaxBakedDimensions)
        {
            // Bake direction numbers for the first 8 dims using Joe-Kuo m-values.
            if (dim == 0)
            {
                // Van der Corput on base 2: v[i] = 2^(MaxBits-1-i).
                for (int i = 0; i < MaxBits; i++)
                {
                    v[i] = 1u << (MaxBits - 1 - i);
                }
                return v;
            }
            uint[] m = s_mValues[dim];
            uint a  = s_polynomials[dim];
            int s   = m.Length;  // polynomial degree

            // Initial direction numbers from m_values.
            for (int i = 0; i < s; i++)
            {
                v[i] = m[i] << (MaxBits - 1 - i);
            }
            // Recurrence for i ≥ s: v_i = (a_1·v_{i-1}) ⊕ (a_2·v_{i-2}) ⊕ … ⊕ v_{i-s} ⊕ (v_{i-s} >> s).
            for (int i = s; i < MaxBits; i++)
            {
                v[i] = v[i - s] ^ (v[i - s] >> s);
                for (int k = 1; k < s; k++)
                {
                    if (((a >> (s - 1 - k)) & 1u) != 0)
                    {
                        v[i] ^= v[i - k];
                    }
                }
            }
            return v;
        }

        // Dim ≥ 8: fall back to a per-dimension prime-seeded pseudo-
        // stratified stream (formula below) for safe coverage. Issue #80:
        // this is neither pure Sobol nor genuine Halton — see the formula
        // comment below for what it actually computes — but for
        // voxelforge's registry where dims 8+ are mostly categorical /
        // gated, it gives adequate uniformity without needing the full
        // Joe-Kuo D=21201 table (which is ~150 KB of direction numbers).
        int prime = NthOddPrime(dim - MaxBakedDimensions + 1);
        // Encode "Halton on prime" in the same v[] layout by mapping
        // bit positions to prime-base reflections. We approximate this
        // by deriving v[i] = (i+1) × prime mod 2^MaxBits, which gives a
        // pseudo-stratified low-discrepancy stream. Not a true Halton
        // sequence but adequate for "better than uniform" coverage on
        // these tail dims.
        for (int i = 0; i < MaxBits; i++)
        {
            uint shifted = (uint)((((long)(i + 1)) * prime * 2654435761L) & ((1L << MaxBits) - 1));
            v[i] = shifted == 0 ? 1u : shifted;
        }
        return v;
    }

    private static int NthOddPrime(int n)
    {
        // Returns the n-th odd prime (3, 5, 7, 11, 13, …). n=1 → 3.
        // Sieve up to 200 — covers n ≤ 45, ample for 24-dim registry.
        int found = 0;
        for (int candidate = 3; candidate < 200; candidate += 2)
        {
            bool isPrime = true;
            for (int d = 3; d * d <= candidate; d += 2)
            {
                if (candidate % d == 0) { isPrime = false; break; }
            }
            if (isPrime)
            {
                found++;
                if (found == n) return candidate;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(n), "n exceeds prime table");
    }

    /// <summary>
    /// Static helper: generate <paramref name="count"/> Sobol points for
    /// a chain with the given slice index (0-based) of <paramref name="totalSlices"/>.
    /// Each chain's points are non-overlapping in the global Sobol stream.
    /// </summary>
    public static double[][] ChainSlice(int dimensions, int count, int sliceIndex, int totalSlices)
    {
        if (sliceIndex < 0 || sliceIndex >= totalSlices)
            throw new ArgumentOutOfRangeException(nameof(sliceIndex));
        var seq = new SobolSequence(dimensions);
        // Skip to this chain's offset; each chain takes Sobol positions
        // sliceIndex+1, sliceIndex+1+totalSlices, sliceIndex+1+2·totalSlices, ...
        // The +1 is implicit: SkipTo(N) leaves the internal index at N, then
        // the first Next() advances to N+1 and returns that point. Slice 0
        // therefore starts at index 1 (the first non-origin Sobol point);
        // higher slices start at sliceIndex+1, preserving disjoint modular
        // classes across chains.
        // L3 (post-Phase-6 logical-error audit): pre-fix the SkipTo argument
        // was sliceIndex + 1, giving a +1 shift on every chain's documented
        // start index. Determinism + disjointness were preserved (the bug
        // was purely a documentation mismatch), but slice 0 missed Sobol
        // index 1 entirely; slice 0's intended index-1 was unused.
        var points = new double[count][];
        seq.SkipTo(sliceIndex);
        for (int i = 0; i < count; i++)
        {
            points[i] = seq.Next();
            // Skip ahead by totalSlices-1 to leave room for other chains.
            for (int k = 0; k < totalSlices - 1; k++) seq.Next();
        }
        return points;
    }
}
