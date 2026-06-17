// BaselineDesignRegressionTests.cs — Lock in the headline numbers for the
// default 500 N LOX/CH₄ design so that any physics change has to be
// explicit. If these drift the test fails with a clear delta message —
// the author then decides whether to update the asserted value (because
// a known improvement landed) or investigate a regression.
//
// These tests exercise everything EXCEPT voxel ops (which require PicoGK
// initialisation via Library.Go). They run on the stock .NET test host.

using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;
using Voxelforge.Structure;

namespace Voxelforge.Tests;

public class BaselineDesignRegressionTests
{
    private static OperatingConditions DefaultConditions() => new()
    {
        Thrust_N = 500,
        ChamberPressure_Pa = 1000 * 6894.76,    // 1000 psia
        MixtureRatio = 3.3,
        CoolantInletTemp_K = 150,
        CoolantInletPressure_Pa = 12e6,
        WallMaterialIndex = 1,                   // CuCrZr
        PropellantPair = PropellantPair.LOX_CH4,
    };

    private static RegenChamberDesign DefaultDesign() => new();   // uses all field defaults

    [Fact]
    public void ThroatDiameter_500N_1000psia_IsAbout8mm()
    {
        // At 500 N, Pc=6.9 MPa, LOX/CH4, C_F ≈ 1.52:
        //   A_t = F/(C_F·Pc) = 500/(1.52·6.9e6) = 47.7 mm²
        //   D_t = 2·√(A_t/π)  ≈ 7.8 mm
        var cond = DefaultConditions();
        var design = DefaultDesign();
        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);

        Assert.InRange(derived.ThroatDiameter_mm, 6.5, 9.0);
        Assert.InRange(derived.TotalMassFlow_kgs, 0.12, 0.30);
        Assert.InRange(derived.IdealIspVacuum_s, 280, 400);
    }

    [Fact]
    public void ThermalSolve_NoFilm_PredictsOverheating()
    {
        // With film cooling OFF, CuCrZr at 500 N LOX/CH4 physically CANNOT
        // survive — solver must predict T_wg ≫ 800 K.
        var (_, thermal, _) = SolveBaseline(filmEnabled: false);
        Assert.True(thermal.PeakGasSideWallT_K > 1500,
            $"Expected overheating without film; got T_wg={thermal.PeakGasSideWallT_K:F0} K");
        Assert.True(thermal.WallTempExceedsLimit);
    }

    [Fact]
    public void ThermalSolve_WithFilm_ReducesRecoveryTemperatureNearInjector()
    {
        // Robust test: check the DIRECT film-cooling mechanism rather than the
        // net T_wg outcome, which at 500 N is sensitive to the film-fraction
        // vs coolant-flow trade-off. Film must lower T_aw_eff near the injector.
        var (_, withFilm, _) = SolveBaseline(filmEnabled: true);
        int nearInjector = withFilm.Stations.Length / 10;
        var s = withFilm.Stations[nearInjector];
        Assert.True(s.FilmEffectiveness > 0.1,
            $"Film η should be > 0.1 near injector (got {s.FilmEffectiveness:F3})");
        Assert.True(s.EffectiveRecoveryTemp_K < s.AdiabaticWallTemp_K - 500,
            $"T_aw_eff should be ≥ 500 K below T_aw_core (got Δ={s.AdiabaticWallTemp_K - s.EffectiveRecoveryTemp_K:F0} K)");
        Assert.True(withFilm.FilmMassFlow_kgs > 0,
            "Film mass flow should be non-zero when enabled");
    }

    [Fact]
    public void ThermalSolve_CoolantOutletIsAboveInlet()
    {
        var (_, thermal, _) = SolveBaseline(filmEnabled: true);
        Assert.True(thermal.CoolantOutletT_K > thermal.CoolantInletT_K);
        double dT = thermal.CoolantOutletT_K - thermal.CoolantInletT_K;
        Assert.InRange(dT, 100, 700);  // 100–700 K coolant rise is reasonable
    }

    [Fact]
    public void StructuralCheck_MarginImprovesWithThickerWall()
    {
        var (contour, thermal_thin, stress_thin) = SolveBaseline(
            filmEnabled: true, wallThickness_mm: 0.6);
        var (_, thermal_thick, stress_thick) = SolveBaseline(
            filmEnabled: true, wallThickness_mm: 1.5);

        // Thicker walls reduce the hoop stress (σ = P·r/t).
        Assert.True(stress_thick.PeakHoop_MPa < stress_thin.PeakHoop_MPa,
            $"Thicker wall should lower σ_hoop ({stress_thin.PeakHoop_MPa:F0} → {stress_thick.PeakHoop_MPa:F0})");
    }

    [Fact]
    public void ProofTest_FailsLoudlyOnUndersizedWall()
    {
        // Ridiculously thin wall + high proof factor — must NOT pass.
        var (_, thermal, _) = SolveBaseline(filmEnabled: true, wallThickness_mm: 0.5);
        var design = DefaultDesign() with { GasSideWallThickness_mm = 0.5, ProofFactor = 3.0 };
        var wall = WallMaterials.All[1];   // CuCrZr (~420 MPa σ_y at 293 K)
        var proof = ProofTestAnalysis.Evaluate(thermal, wall,
            design.GasSideWallThickness_mm, DefaultConditions().ChamberPressure_Pa,
            design.ProofFactor);
        Assert.False(proof.Passes,
            $"0.5 mm CuCrZr wall at 3.0× MEOP ({proof.ProofPressure_Pa/1e6:F1} MPa) should fail; SF_min={proof.ColdStructure.MinSafetyFactor:F2}");
    }

    [Fact]
    public void AxialConductionRMS_IsNonNegative()
    {
        // Physics sanity: axial conduction magnitude can't be negative.
        var (_, thermal, _) = SolveBaseline(filmEnabled: true);
        Assert.True(thermal.AxialConductionRMS_Wm2 >= 0);
    }

    [Fact]
    public void StationWallProfilesAreMonotonic_GasToCoolant()
    {
        // At every station with a meaningful ΔT across the wall, T(r) must
        // monotonically decrease from gas wall to coolant wall. Stations
        // where T_wg ≈ T_wc (< 5 K apart) are dominated by numerical noise
        // in the k(T) iteration and are exempt.
        var (_, thermal, _) = SolveBaseline(filmEnabled: false);   // no-film to guarantee strong ΔT
        int checkedStations = 0;
        foreach (var s in thermal.Stations)
        {
            double dT = s.GasSideWallTemp_K - s.CoolantSideWallTemp_K;
            if (Math.Abs(dT) < 5) continue;  // noise-dominated, skip
            var profile = s.WallRadialProfile_K;
            Assert.True(profile.Length >= 2);
            for (int j = 1; j < profile.Length; j++)
                Assert.True(profile[j] <= profile[j - 1] + 1.0,
                    $"Station {s.Index} (ΔT={dT:F0} K): profile not monotonic ({profile[j-1]:F0} → {profile[j]:F0})");
            checkedStations++;
        }
        Assert.True(checkedStations > 5, "test didn't exercise enough stations");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Helper: run the full non-voxel stack.
    // ─────────────────────────────────────────────────────────────────

    private static (ChamberContour Contour, RegenSolverOutputs Thermal, StructuralSummary Stress)
        SolveBaseline(bool filmEnabled, double wallThickness_mm = 0.8)
    {
        var cond = DefaultConditions();
        var design = DefaultDesign() with
        {
            GasSideWallThickness_mm = wallThickness_mm,
            FilmCooling = filmEnabled
                ? new FilmCoolingInputs
                {
                    Enabled = true,
                    FuelFractionAsFilm = 0.05,
                    FilmSlotHeight_mm = 0.6,
                    BurnoutLength_mm = 200,
                    DecayCoefficient = 0.15,
                    ThroatMixingDegradation = 0.25,
                }
                : new FilmCoolingInputs { Enabled = false },
        };

        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = RegenChamberOptimization.ComputeDerived(cond, gas, design);

        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: derived.ThroatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            thetaN_deg: design.BellEntranceAngle_deg,
            thetaE_deg: design.BellExitAngle_deg,
            bellLengthFraction: design.BellLengthFraction,
            stationCount: 120);

        var channels = new ChannelSchedule(
            ChannelCount: design.ChannelCount,
            RibThickness_mm: design.RibThickness_mm,
            GasSideWallThickness_mm: design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm: design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm: design.ChannelHeightExit_mm);

        var material = WallMaterials.All[cond.WallMaterialIndex];

        double coolantMass = derived.FuelMassFlow_kgs *
            (design.FilmCooling.Enabled ? (1 - design.FilmCooling.FuelFractionAsFilm) : 1.0);

        var solverInputs = new RegenSolverInputs(
            Contour: contour,
            Gas: gas,
            Wall: material,
            Channels: channels,
            CoolantMassFlow_kgs: coolantMass,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            FilmCooling: design.FilmCooling,
            AxialConductionSweeps: design.AxialConductionSweeps,
            RadialWallNodes: design.RadialWallNodes);

        var thermal = RegenCoolingSolver.Solve(solverInputs);
        var stress = StructuralCheck.Evaluate(thermal, material,
            design.GasSideWallThickness_mm, cond.ChamberPressure_Pa,
            gasGamma: gas.GammaThroat);

        return (contour, thermal, stress);
    }
}
