// FilmCoolingTests.cs — Contract tests for the Stechman film-effectiveness
// model. Any future CFD-calibration of β or the burnout function should
// keep these invariants true.

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Tests;

public class FilmCoolingTests
{
    private static ChamberContour TestContour() =>
        ChamberContourGenerator.Generate(
            throatRadius_mm: 3.0,
            contractionRatio: 6.0,
            expansionRatio: 8.0,
            characteristicLength_m: 1.1,
            stationCount: 120);

    [Fact]
    public void DisabledMeansZeroEverywhere()
    {
        var contour = TestContour();
        var film = FilmCooling.Compute(contour,
            new FilmCoolingInputs { Enabled = false },
            totalFuelMassFlow_kgs: 0.04,
            gasStaticTempAtChamber_K: 3500,
            gasDensityAtChamber_kgm3: 6.0,
            gasVelocityAtChamber_ms: 50.0);

        Assert.Equal(0, film.TotalFilmMassFlow_kgs);
        Assert.All(film.Effectiveness, e => Assert.Equal(0.0, e));
    }

    [Fact]
    public void EffectivenessDecaysDownstreamOfInjection()
    {
        var contour = TestContour();
        var film = FilmCooling.Compute(contour,
            new FilmCoolingInputs
            {
                Enabled = true,
                FuelFractionAsFilm = 0.05,
                InjectionX_mm = 0,
                FilmSlotHeight_mm = 0.6,
                BurnoutLength_mm = 80,
                DecayCoefficient = 0.15,
                ThroatMixingDegradation = 0.0,
            },
            totalFuelMassFlow_kgs: 0.04,
            gasStaticTempAtChamber_K: 3500,
            gasDensityAtChamber_kgm3: 6.0,
            gasVelocityAtChamber_ms: 50.0);

        // η(0) should be ≈ 1 (at the slot exit), decaying downstream.
        double etaAtInj = film.Effectiveness[0];
        double etaMidChamber = film.Effectiveness[contour.Stations.Length / 4];
        Assert.True(etaAtInj > 0.9, $"η near x=0 should be ≈ 1 (got {etaAtInj:F2})");
        Assert.True(etaMidChamber < etaAtInj,
            $"η should monotonically decay: η(0)={etaAtInj:F2}, η(mid)={etaMidChamber:F2}");
    }

    [Fact]
    public void FilmBurnsOutPastBurnoutLength()
    {
        var contour = TestContour();
        var film = FilmCooling.Compute(contour,
            new FilmCoolingInputs
            {
                Enabled = true,
                FuelFractionAsFilm = 0.05,
                InjectionX_mm = 0,
                FilmSlotHeight_mm = 0.6,
                BurnoutLength_mm = 20.0,  // intentionally short
                DecayCoefficient = 0.15,
            },
            totalFuelMassFlow_kgs: 0.04,
            gasStaticTempAtChamber_K: 3500,
            gasDensityAtChamber_kgm3: 6.0,
            gasVelocityAtChamber_ms: 50.0);

        // Well past burnout length, η must be 0.
        for (int i = 0; i < contour.Stations.Length; i++)
            if (contour.Stations[i].X_mm > 30)
                Assert.Equal(0.0, film.Effectiveness[i]);
    }

    [Fact]
    public void EffectiveRecoveryTemp_MonotonicInEta()
    {
        // Higher η must lower T_aw_eff.
        double Taw = 4000, Tfilm = 300;
        double eff0 = FilmCooling.EffectiveRecoveryTemperature(Taw, Tfilm, 0.0);
        double eff5 = FilmCooling.EffectiveRecoveryTemperature(Taw, Tfilm, 0.5);
        double eff9 = FilmCooling.EffectiveRecoveryTemperature(Taw, Tfilm, 0.9);
        Assert.True(eff0 > eff5 && eff5 > eff9,
            $"T_aw_eff should monotonically fall with η: {eff0:F0}, {eff5:F0}, {eff9:F0}");
        Assert.Equal(Taw, eff0);
        Assert.InRange(eff9, Tfilm + 300, Tfilm + 400); // η=0.9 means close to film T
    }

    [Fact]
    public void IspPenaltyGrowsWithFilmFraction()
    {
        double mr = 3.3;
        double p0 = FilmCooling.IspPenaltyFraction(0.00, mr);
        double p5 = FilmCooling.IspPenaltyFraction(0.05, mr);
        double p15 = FilmCooling.IspPenaltyFraction(0.15, mr);
        Assert.Equal(0.0, p0);
        Assert.True(p5 < p15);
        Assert.InRange(p5, 0.005, 0.02);       // ~1 % @ 5 %
    }

    // ══════════════════════ PH-37 (2026-04-29) ══════════════════════
    // C* efficiency derate from film-cooling boundary-layer blockage.

    [Fact]
    public void PH37_CStarEfficiencyFactor_NoFilm_IsUnity()
    {
        Assert.Equal(1.0, FilmCooling.CStarEfficiencyFactor(0.0));
    }

    [Fact]
    public void PH37_CStarEfficiencyFactor_DecreasesLinearlyWithFilmFraction()
    {
        // Stechman / Ewen scaling: η_C*_film = 1 − 0.30 · filmFraction.
        // Linear monotone decrease with film fraction up to the 0.7 floor.
        double f0 = FilmCooling.CStarEfficiencyFactor(0.00);
        double f5 = FilmCooling.CStarEfficiencyFactor(0.05);
        double f15 = FilmCooling.CStarEfficiencyFactor(0.15);
        Assert.Equal(1.000, f0,  precision: 4);
        Assert.Equal(0.985, f5,  precision: 4);  // 1 − 0.3·0.05
        Assert.Equal(0.955, f15, precision: 4);  // 1 − 0.3·0.15
        Assert.True(f0 > f5);
        Assert.True(f5 > f15);
    }

    [Fact]
    public void PH37_CStarEfficiencyFactor_ClampedAtFloor()
    {
        // The clamp prevents pathological inputs from producing < 0.7
        // (a 30 % C* drop is the documented maximum credibility band).
        double huge = FilmCooling.CStarEfficiencyFactor(2.0);
        Assert.Equal(0.7, huge);
    }

    // ══════════════════════ Z3-F1 (2026-04-29) ══════════════════════
    // Per-station G_g support in FilmCooling.Compute.

    [Fact]
    public void Z3F1_PerStationGasMassFlux_ShiftsEffectivenessVsScalar()
    {
        // The chamber-side scalar G_g under-predicts G_g at the throat by
        // the contraction ratio (mass conservation: G·A = const). Threading
        // a per-station G_g array that grows toward the throat makes the
        // Stechman momentum-ratio (G_g/G_f)^0.25 larger past mid-chamber,
        // which strengthens decay and lowers η downstream of the injector.
        var contour = TestContour();
        var fci = new FilmCoolingInputs
        {
            Enabled = true,
            FuelFractionAsFilm = 0.05,
            InjectionX_mm = 0,
            FilmSlotHeight_mm = 0.6,
            BurnoutLength_mm = 200,
            DecayCoefficient = 0.15,
            ThroatMixingDegradation = 0.0,
        };
        const double rhoG = 6.0;
        const double uG = 50.0;

        // Scalar (chamber-only) path.
        var scalar = FilmCooling.Compute(contour, fci,
            totalFuelMassFlow_kgs: 0.04,
            gasStaticTempAtChamber_K: 3500,
            gasDensityAtChamber_kgm3: rhoG,
            gasVelocityAtChamber_ms: uG);

        // Per-station path: G(x) = G_chamber · (A_chamber / A(x)) by
        // mass conservation. Same chamber state, but per-station array
        // injects the area-shrinkage effect.
        int N = contour.Stations.Length;
        double G_chamber = rhoG * uG;
        double A_chamber = contour.Stations[0].Area_mm2;
        var perStation = new double[N];
        for (int i = 0; i < N; i++)
            perStation[i] = G_chamber * (A_chamber / System.Math.Max(contour.Stations[i].Area_mm2, 1e-9));

        var perStationResult = FilmCooling.Compute(contour, fci,
            totalFuelMassFlow_kgs: 0.04,
            gasStaticTempAtChamber_K: 3500,
            gasDensityAtChamber_kgm3: rhoG,
            gasVelocityAtChamber_ms: uG,
            gasMassFluxPerStation_kg_m2_s: perStation);

        // At the injector face (i ≈ 0) the two paths agree (A ≈ A_chamber).
        Assert.Equal(scalar.Effectiveness[0], perStationResult.Effectiveness[0], precision: 3);

        // Past mid-chamber (toward the throat, where G_g rises with the
        // contraction ratio) the per-station path gives lower η — Stechman
        // momentum factor is larger, decay is stronger.
        int throatIdx = contour.ThroatIndex;
        double scalarAtThroat = scalar.Effectiveness[throatIdx];
        double perStationAtThroat = perStationResult.Effectiveness[throatIdx];
        Assert.True(perStationAtThroat <= scalarAtThroat + 1e-9,
            $"per-station η at throat ({perStationAtThroat:F3}) should be ≤ "
          + $"scalar η ({scalarAtThroat:F3}) — per-station G_g is larger past mid-chamber.");
    }
}
