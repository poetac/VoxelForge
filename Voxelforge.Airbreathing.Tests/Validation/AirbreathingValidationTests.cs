// AirbreathingValidationTests.cs — driver tests for the air-breathing
// validation library (Sprint A2).
//
// Each fixture in AirbreathingFixtures.All becomes a per-property
// assertion against the cycle-solver output. Sprint A2 ships with all
// tests skipped — the [Fact(Skip = ...)] markers cite the sprint that
// activates each fixture (A4 for ramjet, A7 for turbojet).
//
// Removing a Skip marker is the explicit "this physics now ships"
// signal — the ramjet sprint commit removes the ramjet skips, the
// turbojet sprint removes the turbojet skips.

using Voxelforge.Airbreathing;

namespace Voxelforge.Airbreathing.Tests.Validation;

public sealed class AirbreathingValidationTests
{
    [Fact]
    public void MattinglySyntheticRamjet_StationMap_MatchesHandDerived()
    {
        var f = AirbreathingFixtures.MattinglySyntheticRamjet;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertStationFreestream(f, actual);
        AssertStationInletExit(f, actual);
        AssertStationCombustorExit(f, actual);
        AssertStationNozzleExit(f, actual);
    }

    [Fact]
    public void MattinglySyntheticRamjet_FuelAirRatio_MatchesHandDerived()
    {
        var f = AirbreathingFixtures.MattinglySyntheticRamjet;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "FuelAirRatio",
            expected: f.Expected.FuelAirRatio,
            actual: actual.Stations.FuelMassFlow_kg_s
                  / actual.Stations.Station(1).MassFlow_kg_s,
            fraction: f.Tolerance.FuelAirRatioFraction);
    }

    [Fact]
    public void MattinglySyntheticRamjet_SpecificImpulse_MatchesHandDerived()
    {
        var f = AirbreathingFixtures.MattinglySyntheticRamjet;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J85_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.J85_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J85_SeaLevelStatic_SpecificImpulse_WithinTolerance()
    {
        var f = AirbreathingFixtures.J85_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void F404_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.F404_SeaLevelStatic_Dry;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        Assert.True(actual.IsFeasible,
            $"F404 fixture should be feasible at design point; got: "
          + $"{string.Join(", ", System.Linq.Enumerable.Select(actual.Violations, v => v.ConstraintId))}");

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void F404_SeaLevelStatic_SpecificImpulse_WithinTolerance()
    {
        var f = AirbreathingFixtures.F404_SeaLevelStatic_Dry;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void F404_SeaLevelStatic_TurbineInletT_WithinTolerance()
    {
        var f = AirbreathingFixtures.F404_SeaLevelStatic_Dry;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);
        var s4 = actual.Stations.Station(4);

        AssertWithinFraction(
            propertyName: "Station4.StagnationT_K (T_t4)",
            expected: f.Expected.CombustorExit_StagnationT_K,
            actual: s4.StagnationT_K,
            fraction: f.Tolerance.StationStateFraction);
    }

    /// <summary>
    /// Sprint A2 acceptance: confirms the catalogue exists, has both
    /// fixtures listed, and each fixture's <c>Sprint</c> tag points at
    /// a real future sprint. Active (not skipped) so CI guards against
    /// accidental fixture removal.
    /// </summary>
    [Fact]
    public void FixtureCatalogue_IsPopulated()
    {
        Assert.NotEmpty(AirbreathingFixtures.All);
        Assert.Contains(AirbreathingFixtures.All, f => f.Sprint == "A4");
        Assert.Contains(AirbreathingFixtures.All, f => f.Sprint == "A7");
        Assert.Contains(AirbreathingFixtures.All, f => f.Sprint == "A8");
        foreach (var f in AirbreathingFixtures.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Name),  $"Fixture missing name");
            Assert.False(string.IsNullOrWhiteSpace(f.Source), $"Fixture {f.Name} missing source citation");
            Assert.True(f.Tolerance.StationStateFraction > 0, $"Fixture {f.Name} has non-positive station tolerance");
            Assert.True(f.Tolerance.PerformanceFraction > 0,  $"Fixture {f.Name} has non-positive performance tolerance");
        }
    }

    [Fact]
    public void J47_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.J47_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J47_SeaLevelStatic_SpecificImpulse_WithinTolerance()
    {
        var f = AirbreathingFixtures.J47_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J57_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.J57_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J57_SeaLevelStatic_SpecificImpulse_WithinTolerance()
    {
        var f = AirbreathingFixtures.J57_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J79_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.J79_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void J79_SeaLevelStatic_SpecificImpulse_ConstantCpLimitation()
    {
        var f = AirbreathingFixtures.J79_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void RJ43_DesignPoint_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.Marquardt_RJ43_DesignPoint;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void RJ43_DesignPoint_Isp_ConstantCpLimitation()
    {
        var f = AirbreathingFixtures.Marquardt_RJ43_DesignPoint;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void R25_SeaLevelStatic_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.R25_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void R25_SeaLevelStatic_SpecificImpulse_WithinTolerance()
    {
        var f = AirbreathingFixtures.R25_SeaLevelStatic;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    // ── Argus V-1 pulsejet validation (sub-step 1a.5, Wave 1 PR-4) ─────────

    [Fact]
    public void FockeWulfV1_Pulsejet_NetThrust_WithinTolerance()
    {
        var f = AirbreathingFixtures.FockeWulfV1_Pulsejet;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "ThrustNet_N",
            expected: f.Expected.ThrustNet_N,
            actual: actual.Stations.ThrustNet_N,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void FockeWulfV1_Pulsejet_SpecificImpulse_WithinTolerance()
    {
        var f = AirbreathingFixtures.FockeWulfV1_Pulsejet;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);

        AssertWithinFraction(
            propertyName: "SpecificImpulse_s",
            expected: f.Expected.SpecificImpulse_s,
            actual: actual.Stations.SpecificImpulse_s,
            fraction: f.Tolerance.PerformanceFraction);
    }

    [Fact]
    public void FockeWulfV1_Pulsejet_AirMassFlow_InRealisticRange()
    {
        // V-1 air mass flow in literature spans 1–5 kg/s depending on
        // source. The PulsejetCycleSolver static-volumetric-efficiency
        // calibration (η_vol = 0.14) targets 1.5–2.0 kg/s to match the
        // ~3 kN static thrust target. Range check, not point assertion,
        // because the literature numbers themselves vary.
        var f = AirbreathingFixtures.FockeWulfV1_Pulsejet;
        var actual = AirbreathingOptimization.GenerateWith(f.Design, f.Conditions);
        var s0 = actual.Stations.Station(0);

        Assert.InRange(s0.MassFlow_kg_s, 1.0, 5.0);
    }

    // ---- helpers ----

    private static void AssertStationFreestream(AirbreathingFixture f, AirbreathingResult actual)
    {
        // StationState reports stagnation; freestream static is derived
        // via isentropic relations using the station's local Mach number.
        var s = actual.Stations.Station(0);
        double ratio = 1.0 + 0.2 * s.MachNumber * s.MachNumber;          // (γ−1)/2 = 0.2 for γ=1.4
        double tStatic = s.StagnationT_K / ratio;
        double pStatic = s.StagnationP_Pa / System.Math.Pow(ratio, 3.5);  // γ/(γ−1) = 3.5 for γ=1.4
        AssertWithinFraction("Station0.StaticT_K (derived)", f.Expected.FreestreamStaticT_K, tStatic, f.Tolerance.StationStateFraction);
        AssertWithinFraction("Station0.StaticP_Pa (derived)", f.Expected.FreestreamStaticP_Pa, pStatic, f.Tolerance.StationStateFraction);
    }

    private static void AssertStationInletExit(AirbreathingFixture f, AirbreathingResult actual)
    {
        // Ramjet uses station 2 for diffuser exit (inlet recovery
        // applied). Turbojet uses station 2 for compressor face;
        // station 3 is post-compressor. The fixture's
        // InletExit_StagnationT_K maps to station 2 in either case.
        var s = actual.Stations.Station(2);
        AssertWithinFraction("Station2.StagnationT_K", f.Expected.InletExit_StagnationT_K, s.StagnationT_K, f.Tolerance.StationStateFraction);
        AssertWithinFraction("Station2.StagnationP_Pa", f.Expected.InletExit_StagnationP_Pa, s.StagnationP_Pa, f.Tolerance.StationStateFraction);
    }

    private static void AssertStationCombustorExit(AirbreathingFixture f, AirbreathingResult actual)
    {
        var s = actual.Stations.Station(4);
        AssertWithinFraction("Station4.StagnationT_K", f.Expected.CombustorExit_StagnationT_K, s.StagnationT_K, f.Tolerance.StationStateFraction);
        AssertWithinFraction("Station4.StagnationP_Pa", f.Expected.CombustorExit_StagnationP_Pa, s.StagnationP_Pa, f.Tolerance.StationStateFraction);
    }

    private static void AssertStationNozzleExit(AirbreathingFixture f, AirbreathingResult actual)
    {
        var s = actual.Stations.Station(9);
        AssertWithinFraction("Station9.StagnationT_K", f.Expected.NozzleExit_StagnationT_K, s.StagnationT_K, f.Tolerance.StationStateFraction);
        AssertWithinFraction("Station9.StagnationP_Pa", f.Expected.NozzleExit_StagnationP_Pa, s.StagnationP_Pa, f.Tolerance.StationStateFraction);
        AssertWithinFraction("Station9.MachNumber", f.Expected.NozzleExit_MachNumber, s.MachNumber, f.Tolerance.StationStateFraction);
    }

    private static void AssertWithinFraction(
        string propertyName, double expected, double actual, double fraction)
    {
        if (double.IsNaN(expected))
            return;  // fixture chose not to assert this property
        var allowed = System.Math.Abs(expected) * fraction;
        var actualError = System.Math.Abs(actual - expected);
        Assert.True(
            actualError <= allowed,
            $"{propertyName}: expected {expected:G6} ± {allowed:G6} ({fraction:P1}), got {actual:G6} (error {actualError:G6})");
    }
}
