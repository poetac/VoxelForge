// SobolSequencePropertyTests.cs — Issue #80: verifies the baked Joe-Kuo
// direction-number table (SobolSequence.s_polynomials / s_mValues,
// internal specifically so this test can check the real production data
// rather than a copy that could drift) is mathematically valid: every
// m_i is odd, every m_i < 2^i (1-indexed literature convention: array
// index j, 0-indexed, corresponds to m_(j+1), so the bound is 2^(j+1)),
// and the polynomial each (s, a) pair encodes is primitive over GF(2).
//
// Fail-on-old proof: reverting the #80 fix restores the pre-fix table,
// under which 5 of the 7 real baked dimensions (dims 3-7, not just the
// dim-3 row-fusion the original issue diagnosed by inspection) decode to
// NON-primitive polynomials -- computationally verified (Python
// transliteration of this exact algorithm) during authoring:
//   dim 3 (a=2, m={1,1,3,3}, s=4): x^4+x^2+1 = (x^2+x+1)^2 -- reducible.
//   dim 4 (a=1, m={1,1,5,11,7}, s=5): x^5+x+1 -- reducible (divisible by x^2+x+1).
//   dim 5, 6, 7: also non-primitive under the same check.
// Only dims 1-2 (0-indexed) happened to be accidentally valid pre-fix,
// because they coincided with what should have been source d=3/d=4's
// values (shifted by one row) rather than the correct d=2/d=3 values.

using Voxelforge.Optimization;
using Xunit;

namespace Voxelforge.Core.Tests;

public sealed class SobolSequencePropertyTests
{
    /// <summary>
    /// Brute-force multiplicative order of x in GF(2)[x]/(poly), where
    /// poly = x^s + (bits 1..s-1 taken from a) + 1, encoded as bit s
    /// down to bit 0 of a uint. Primitive over GF(2) iff x's order is
    /// exactly 2^s - 1 (the full multiplicative group of GF(2^s)) --
    /// not "never returns to 1" (reducible poly) and not a proper
    /// divisor of 2^s - 1 (irreducible but non-primitive).
    /// </summary>
    private static bool IsPrimitive(int s, uint a)
    {
        if (s <= 1) return true;   // x+1: no free coefficients, always valid
        uint poly = (1u << s) | (a << 1) | 1u;
        uint order = (1u << s) - 1;
        uint current = 1;
        for (uint step = 1; step <= order; step++)
        {
            current <<= 1;
            if ((current & (1u << s)) != 0) current ^= poly;
            if (current == 1) return step == order;
        }
        return false;
    }

    [Fact]
    public void BakedDimensions_EveryMValueIsOdd()
    {
        for (int dim = 1; dim < SobolSequence.s_mValues.Length; dim++)
        {
            var m = SobolSequence.s_mValues[dim];
            for (int i = 0; i < m.Length; i++)
            {
                Assert.True(m[i] % 2 == 1, $"dim {dim}, m[{i}] = {m[i]} is even");
            }
        }
    }

    [Fact]
    public void BakedDimensions_EveryMValueBelowTwoToThePowerOfIndexPlusOne()
    {
        for (int dim = 1; dim < SobolSequence.s_mValues.Length; dim++)
        {
            var m = SobolSequence.s_mValues[dim];
            for (int i = 0; i < m.Length; i++)
            {
                uint bound = 1u << (i + 1);
                Assert.True(m[i] < bound, $"dim {dim}, m[{i}] = {m[i]} is not < 2^{i + 1}");
            }
        }
    }

    [Fact]
    public void BakedDimensions_PolynomialIsPrimitiveOverGF2()
    {
        Assert.Equal(SobolSequence.s_polynomials.Length, SobolSequence.s_mValues.Length);
        for (int dim = 1; dim < SobolSequence.s_polynomials.Length; dim++)
        {
            int s = SobolSequence.s_mValues[dim].Length;
            uint a = SobolSequence.s_polynomials[dim];
            Assert.True(IsPrimitive(s, a),
                $"dim {dim}: (s={s}, a={a}) decodes to a non-primitive polynomial over GF(2)");
        }
    }

    [Theory]
    [InlineData(1, 0u, true)]                 // x+1 (degree 1, trivially valid)
    [InlineData(2, 1u, true)]                 // x^2+x+1 (the unique primitive quadratic)
    [InlineData(4, 2u, false)]                // x^4+x^2+1 = (x^2+x+1)^2 -- reducible (the pre-fix dim-3 bug)
    [InlineData(5, 1u, false)]                // x^5+x+1 -- reducible (divisible by x^2+x+1)
    [InlineData(5, 2u, true)]                 // x^5+x^2+1 -- the corrected dim-7 (source d=8) entry
    public void IsPrimitive_KnownCases(int s, uint a, bool expected)
    {
        Assert.Equal(expected, IsPrimitive(s, a));
    }
}
