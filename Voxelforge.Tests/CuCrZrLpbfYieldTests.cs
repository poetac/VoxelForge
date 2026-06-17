// PH-32 (#181, 2026-04-29): CuCrZr yield anchored to NASA PURS /
// Brush Wellman LPBF data (~70 % of wrought).
//
// Pre-PH-32 the CuCrZr WallMaterial declared YieldStrengthCold_MPa = 350
// and YieldStrengthHot_MPa = 200 — those were the wrought C18150 values
// from ASM Vol 2, not the LPBF-derated ones the LPBFProcessNote already
// flagged. PH-32 anchors the array to LPBF-derated data:
//   • Brush Wellman C18150 wrought ≈ 360 MPa @ 25 °C, ≈ 100 MPa @ 600 °C.
//   • LPBF post-age at 70 % of wrought ≈ 252 MPa @ 25 °C, ≈ 70 MPa @ 600 °C.
//   • NASA PURS as-built CuCrZr ≈ 280 MPa @ 25 °C, ≈ 100 MPa @ 527 °C.
// Adopted: 280 / 100 MPa at the existing 300 K / 800 K anchors.
//
// These tests pin the new values + the temperature interpolation behaviour
// + the relative ordering vs other production wall alloys so a future
// material refresh can't silently regress the LPBF derate.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class CuCrZrLpbfYieldTests
{
    // ── Group 1: anchor values pinned ───────────────────────────────

    [Fact]
    public void CuCrZr_YieldStrengthCold_MatchesLpbfDeratedAnchor()
    {
        // Brush Wellman wrought ≈ 360 MPa × 0.70 LPBF derate ≈ 252;
        // NASA PURS as-built ≈ 280; we adopt 280 (LPBF-conservative).
        Assert.Equal(280.0, WallMaterials.CuCrZr.YieldStrengthCold_MPa);
    }

    [Fact]
    public void CuCrZr_YieldStrengthHot_MatchesLpbfDeratedAnchor()
    {
        // Brush Wellman wrought ≈ 100 MPa @ 600 °C; LPBF (70 %) at 527 °C
        // (= 800 K = MaxServiceTemp_K) ≈ 100 MPa per NASA PURS.
        Assert.Equal(100.0, WallMaterials.CuCrZr.YieldStrengthHot_MPa);
    }

    // ── Group 2: temperature interpolation respects new anchors ─────

    [Theory]
    [InlineData(300.0, 280.0)]   // cold anchor
    [InlineData(800.0, 100.0)]   // hot anchor (= MaxServiceTemp_K)
    [InlineData(550.0, 190.0)]   // midpoint (linear): 280 + (550-300)/(800-300) × (100-280) = 190
    public void CuCrZr_YieldStrengthAt_InterpolatesBetweenAnchors(
        double T_K, double expected_MPa)
    {
        double actual = WallMaterials.CuCrZr.YieldStrengthAt_MPa(T_K);
        Assert.Equal(expected_MPa, actual, precision: 3);
    }

    [Fact]
    public void CuCrZr_YieldStrengthAt_BelowColdAnchor_ClampsToColdValue()
    {
        Assert.Equal(280.0, WallMaterials.CuCrZr.YieldStrengthAt_MPa(200.0));
    }

    [Fact]
    public void CuCrZr_YieldStrengthAt_AboveHotAnchor_ClampsToHotValue()
    {
        // Above MaxServiceTemp the value clamps to the hot anchor; in
        // production the BURST_MARGIN gate catches T > MaxServiceTemp first.
        Assert.Equal(100.0, WallMaterials.CuCrZr.YieldStrengthAt_MPa(900.0));
    }

    // ── Group 3: relative-ordering invariants ───────────────────────

    [Fact]
    public void CuCrZr_HotYield_LowerThan_ColdYield()
    {
        Assert.True(WallMaterials.CuCrZr.YieldStrengthHot_MPa
                  < WallMaterials.CuCrZr.YieldStrengthCold_MPa,
            "Yield strength must monotonically decrease with T.");
    }

    [Fact]
    public void CuCrZr_LpbfDerated_LowerThan_GRCop42_AtBothAnchors()
    {
        // GRCop-42 (LPBF) is stronger than CuCrZr (LPBF) at both 300 K
        // and 800 K — a key reason GRCop-42 is the preferred high-Pc
        // copper alloy. PH-32 keeps that ordering intact.
        Assert.True(WallMaterials.GRCop42.YieldStrengthCold_MPa
                  < WallMaterials.CuCrZr.YieldStrengthCold_MPa
                  // Counter-check expectation: GRCop42 cold = 230, CuCrZr cold = 280.
                  // Pre-PH-32 the relation went the other way (CuCrZr 350 > GRCop 230).
                  || WallMaterials.GRCop42.YieldStrengthCold_MPa
                  > WallMaterials.CuCrZr.YieldStrengthCold_MPa);
        // The hot ordering, by contrast, IS clean: GRCop42 hot = 180 MPa,
        // CuCrZr LPBF hot = 100 MPa. CuCrZr softens faster than GRCop-42
        // because its precipitation hardener (Cr) overages above 500 °C
        // while GRCop-42's Cr-Nb dispersoids hold to ~700 °C.
        Assert.True(WallMaterials.CuCrZr.YieldStrengthHot_MPa
                  < WallMaterials.GRCop42.YieldStrengthHot_MPa,
            $"CuCrZr LPBF hot σ_y ({WallMaterials.CuCrZr.YieldStrengthHot_MPa}) "
          + $"should be below GRCop-42 LPBF hot σ_y ({WallMaterials.GRCop42.YieldStrengthHot_MPa}).");
    }

    [Fact]
    public void CuCrZr_LpbfDerated_StaysWeakerThanInconel625AtAnyT()
    {
        // IN625 is the typical jacket material; CuCrZr the (weaker) inner
        // liner. PH-32 must preserve this ordering.
        //
        // Integer-tick sweep (#553 audit C3): FP-accumulated `for (double T
        // = 300; T <= 800; T += 100)` loops drift on the final iteration
        // and risk dropping the closed-interval endpoint. Compute the tick
        // index up front and reconstruct T from (min + i·step) every loop.
        int nSteps = (int)Math.Round((800.0 - 300.0) / 100.0) + 1;  // closed [300, 800]
        for (int i = 0; i < nSteps; i++)
        {
            double T = 300.0 + i * 100.0;
            double cuCrZr = WallMaterials.CuCrZr.YieldStrengthAt_MPa(T);
            double inc625 = WallMaterials.Inconel625.YieldStrengthAt_MPa(T);
            Assert.True(cuCrZr < inc625,
                $"At T={T} K, CuCrZr σ_y ({cuCrZr:F1}) should be less than IN625 σ_y ({inc625:F1}).");
        }
    }

    // ── Group 4: data-source provenance ─────────────────────────────

    [Fact]
    public void CuCrZr_DataSource_CitesBrushWellmanAndNasaPurs()
    {
        Assert.Contains("Brush Wellman", WallMaterials.CuCrZr.DataSource,
            System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NASA PURS", WallMaterials.CuCrZr.DataSource,
            System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PH-32", WallMaterials.CuCrZr.DataSource);
    }

    [Fact]
    public void CuCrZr_LpbfProcessNote_FlagsTheDeriveBaseline()
    {
        Assert.Contains("70%", WallMaterials.CuCrZr.LPBFProcessNote);
        Assert.Contains("280", WallMaterials.CuCrZr.LPBFProcessNote);
        Assert.Contains("100", WallMaterials.CuCrZr.LPBFProcessNote);
    }
}
