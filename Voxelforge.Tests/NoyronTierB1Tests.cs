// NoyronTierB1Tests.cs — Tier B1 scaffold forcing-function suite.
// Validates the TPMS correlation library without the full
// ChannelTopology / voxel / SA integration (those ship in B1 proper).
//
// Covers:
//   • Properties registry: all three TpmsKind values resolve; dimensionless
//     values respect published-literature ordering
//     (Schwarz-P < Gyroid < Schwarz-D on σ_SAV; same on Nu coefficient).
//   • SurfaceAreaDensity scales inversely with cell edge.
//   • Porosity linearisation stays bounded over ψ ∈ [0.30, 0.70].
//   • FrictionFactor crosses over from f·Re/Re laminar to Forchheimer
//     plateau around Re ≈ 1000.
//   • NusseltNumber: Attarzadeh form at Re ≥ 2000; laminar asymptote
//     at Re < 2000; Gyroid > Schwarz-P, Schwarz-D > Gyroid at matched Re.
//   • HeatTransferCoefficient returns a positive finite value on the
//     nominal envelope + zero on degenerate inputs.
//   • Solid-fraction out-of-range inputs throw.
//   • Recommend heuristic respects thermalWeight ordering.
//
// All tests are pure math — no PicoGK Library required.

using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class NoyronTierB1Tests
{
    // ══════════════════════ Properties registry ══════════════════════

    [Theory]
    [InlineData(TpmsKind.Gyroid)]
    [InlineData(TpmsKind.SchwarzP)]
    [InlineData(TpmsKind.SchwarzD)]
    public void Properties_AllKindsResolve(TpmsKind kind)
    {
        var p = TpmsCorrelations.Properties(kind);
        Assert.Equal(kind, p.Kind);
        Assert.True(p.SurfaceAreaDensityDimensionless > 0);
        Assert.True(p.FrictionReProduct > 0);
        Assert.True(p.NusseltCoefficient > 0);
    }

    [Fact]
    public void Properties_SurfaceAreaOrdering_MatchesLiterature()
    {
        // Kapfer et al. 2011: SchwarzP < Gyroid < SchwarzD on σ_SAV.
        var sp = TpmsCorrelations.Properties(TpmsKind.SchwarzP);
        var g  = TpmsCorrelations.Properties(TpmsKind.Gyroid);
        var sd = TpmsCorrelations.Properties(TpmsKind.SchwarzD);

        Assert.True(sp.SurfaceAreaDensityDimensionless < g.SurfaceAreaDensityDimensionless);
        Assert.True(g.SurfaceAreaDensityDimensionless < sd.SurfaceAreaDensityDimensionless);
    }

    [Fact]
    public void Properties_NusseltOrdering_MatchesSurfaceArea()
    {
        // Attarzadeh 2020: Nu coefficient tracks σ_SAV — more surface,
        // more convection at matched (Re, Pr).
        var sp = TpmsCorrelations.Properties(TpmsKind.SchwarzP);
        var g  = TpmsCorrelations.Properties(TpmsKind.Gyroid);
        var sd = TpmsCorrelations.Properties(TpmsKind.SchwarzD);

        Assert.True(sp.NusseltCoefficient < g.NusseltCoefficient);
        Assert.True(g.NusseltCoefficient < sd.NusseltCoefficient);
    }

    // ══════════════════════ SurfaceAreaDensity ══════════════════════

    [Fact]
    public void SurfaceAreaDensity_InverselyScalesWithCellEdge()
    {
        // σ_SAV = dimensionless / L_cell — halving the cell edge doubles σ_SAV.
        double sav_1mm = TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.001);
        double sav_2mm = TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.002);
        Assert.Equal(sav_1mm / 2.0, sav_2mm, 3);
    }

    [Fact]
    public void SurfaceAreaDensity_ZeroAtDegenerateCellEdge()
    {
        Assert.Equal(0.0, TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.0));
        Assert.Equal(0.0, TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, -0.001));
    }

    [Fact]
    public void SurfaceAreaDensity_PeaksAtPsiHalf()
    {
        // Linear porosity correction: σ peaks at ψ = 0.50 (solidFraction = 0.50).
        double center = TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.001, 0.50);
        double offA   = TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.001, 0.35);
        double offB   = TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.001, 0.65);

        Assert.True(center > offA);
        Assert.True(center > offB);
        // Symmetric correction ⇒ equal departures.
        Assert.Equal(offA, offB, 6);
    }

    // ══════════════════════ FrictionFactor ══════════════════════

    [Fact]
    public void FrictionFactor_LaminarLimit_MatchesFReProduct()
    {
        // At Re = 100, laminar regime dominates ⇒ f ≈ f·Re / Re.
        double f = TpmsCorrelations.FrictionFactor(TpmsKind.Gyroid, 100);
        Assert.Equal(96.0 / 100.0, f, 3);
    }

    [Fact]
    public void FrictionFactor_TurbulentPlateau_ApproachesConstant()
    {
        // At Re >> 1000 the laminar term drops below the Forchheimer plateau.
        double f5k  = TpmsCorrelations.FrictionFactor(TpmsKind.Gyroid, 5_000);
        double f50k = TpmsCorrelations.FrictionFactor(TpmsKind.Gyroid, 50_000);
        // Same plateau ⇒ within numerical precision.
        Assert.Equal(f5k, f50k, 6);
        // And the plateau is the known 0.080 constant.
        Assert.Equal(0.080, f5k, 3);
    }

    [Fact]
    public void FrictionFactor_OrderingAtTurbulentPlateau_MatchesLiterature()
    {
        // SchwarzP has the smoothest flow paths ⇒ lowest f; SchwarzD the roughest.
        double fp  = TpmsCorrelations.FrictionFactor(TpmsKind.SchwarzP, 10_000);
        double fg  = TpmsCorrelations.FrictionFactor(TpmsKind.Gyroid,   10_000);
        double fd  = TpmsCorrelations.FrictionFactor(TpmsKind.SchwarzD, 10_000);
        Assert.True(fp < fg);
        Assert.True(fg < fd);
    }

    [Fact]
    public void FrictionFactor_ZeroAtDegenerateRe()
    {
        Assert.Equal(0.0, TpmsCorrelations.FrictionFactor(TpmsKind.Gyroid, 0.0));
        Assert.Equal(0.0, TpmsCorrelations.FrictionFactor(TpmsKind.SchwarzP, -5.0));
    }

    // ══════════════════════ NusseltNumber ══════════════════════

    [Fact]
    public void NusseltNumber_Turbulent_ExceedsLaminar()
    {
        // Turbulent Re range produces Nu orders of magnitude above the
        // laminar asymptote.
        double nuLam = TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid, 500, 4.0);
        double nuTur = TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid, 20_000, 4.0);
        Assert.True(nuTur > nuLam);
        Assert.True(nuTur > 50);  // Attarzadeh form at this Re,Pr range is O(100).
    }

    [Fact]
    public void NusseltNumber_OrderingAtMatchedRePr_MatchesSurfaceArea()
    {
        double nuSP = TpmsCorrelations.NusseltNumber(TpmsKind.SchwarzP, 10_000, 4.0);
        double nuG  = TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid,   10_000, 4.0);
        double nuSD = TpmsCorrelations.NusseltNumber(TpmsKind.SchwarzD, 10_000, 4.0);
        Assert.True(nuSP < nuG);
        Assert.True(nuG < nuSD);
    }

    [Fact]
    public void NusseltNumber_ZeroAtDegenerateInputs()
    {
        Assert.Equal(0.0, TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid, 0, 4));
        Assert.Equal(0.0, TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid, 100, 0));
    }

    [Fact]
    public void NusseltNumber_RespectsDittusBoelterExponents()
    {
        // In the turbulent regime, Nu ∝ Re^0.8 ⇒ Re × 10 → Nu × 10^0.8 ≈ 6.31.
        double nu1 = TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid, 2_000, 4.0);
        double nu2 = TpmsCorrelations.NusseltNumber(TpmsKind.Gyroid, 20_000, 4.0);
        Assert.Equal(nu2 / nu1, System.Math.Pow(10, 0.8), 2);
    }

    // ══════════════════════ HeatTransferCoefficient ══════════════════════

    [Fact]
    public void HeatTransferCoefficient_PositiveOnNominalInputs()
    {
        // Nominal methane coolant at ~100 K: k ≈ 0.20 W/(m·K).
        double h = TpmsCorrelations.HeatTransferCoefficient(
            TpmsKind.Gyroid, reynolds: 15_000, prandtl: 3.5,
            conductivity_WmK: 0.20, cellEdge_m: 0.002);
        Assert.True(h > 0);
        Assert.True(h < 1_000_000);  // sanity: not astronomically large
    }

    [Fact]
    public void HeatTransferCoefficient_ZeroOnDegenerateInputs()
    {
        Assert.Equal(0.0,
            TpmsCorrelations.HeatTransferCoefficient(TpmsKind.Gyroid, 15_000, 3.5, 0.0, 0.002));
        Assert.Equal(0.0,
            TpmsCorrelations.HeatTransferCoefficient(TpmsKind.Gyroid, 15_000, 3.5, 0.20, 0.0));
    }

    // ══════════════════════ Validation ══════════════════════

    [Theory]
    [InlineData(0.29)]
    [InlineData(0.71)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void SurfaceAreaDensity_RejectsOutOfRangeSolidFraction(double solidFraction)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            TpmsCorrelations.SurfaceAreaDensity(TpmsKind.Gyroid, 0.001, solidFraction));
    }

    [Fact]
    public void Properties_UnknownKind_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            TpmsCorrelations.Properties((TpmsKind)999));
    }

    // ══════════════════════ Recommend ══════════════════════

    [Theory]
    [InlineData(0.00, TpmsKind.SchwarzP)]
    [InlineData(0.30, TpmsKind.SchwarzP)]
    [InlineData(0.33, TpmsKind.SchwarzP)]
    [InlineData(0.50, TpmsKind.Gyroid)]
    [InlineData(0.67, TpmsKind.SchwarzD)]
    [InlineData(1.00, TpmsKind.SchwarzD)]
    public void Recommend_RespectsThermalWeightOrdering(double weight, TpmsKind expected)
    {
        Assert.Equal(expected, TpmsCorrelations.Recommend(weight));
    }
}
