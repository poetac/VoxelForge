// PulsejetContourTests.cs — Wave 1 PR-5 (sub-step 1a.5).
// Cross-platform contour-derivation tests for PulsejetGeometry.From.
// Stays net9.0 (no PicoGK dep) so the airbreathing test project keeps
// its cross-platform-clean architectural property.

using System;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Geometry;
using Xunit;

namespace Voxelforge.Airbreathing.Tests.Geometry;

public sealed class PulsejetContourTests
{
    private static AirbreathingEngineDesign V1ReferenceDesign() =>
        new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:      0.030,
            CombustorArea_m2:        0.075,
            CombustorLength_m:       0.80,
            NozzleThroatArea_m2:     0.025,
            NozzleExitArea_m2:       0.040,
            EquivalenceRatio:        0.95,
            CompressorPressureRatio: 1.0)
        with
        {
            PulsejetTubeLength_m    = 3.40,
            PulsejetIntakeArea_m2   = 0.030,
            PulsejetTailpipeArea_m2 = 0.040,
        };

    [Fact]
    public void From_V1Reference_ProducesFiveStations()
    {
        var contour = PulsejetGeometry.From(V1ReferenceDesign());
        Assert.Equal(5, contour.Stations.Length);
    }

    [Fact]
    public void From_V1Reference_TotalLengthMatchesDesign()
    {
        var contour = PulsejetGeometry.From(V1ReferenceDesign());
        Assert.Equal(3.40, contour.TotalLength_m, precision: 6);
    }

    [Fact]
    public void From_V1Reference_StationsMonotoneInX()
    {
        var contour = PulsejetGeometry.From(V1ReferenceDesign());
        for (int i = 1; i < contour.Stations.Length; i++)
        {
            Assert.True(contour.Stations[i].X_m >= contour.Stations[i - 1].X_m,
                $"Stations not monotone: x[{i-1}]={contour.Stations[i-1].X_m}, x[{i}]={contour.Stations[i].X_m}");
        }
    }

    [Fact]
    public void From_V1Reference_CombustorIndexPointsToCombustorSection()
    {
        var contour = PulsejetGeometry.From(V1ReferenceDesign());
        Assert.Equal(PulsejetSection.Combustor, contour.CombustorStation.Section);
    }

    [Fact]
    public void From_V1Reference_IntakeAndExitRadiiMatchDesign()
    {
        var contour = PulsejetGeometry.From(V1ReferenceDesign());
        // r = √(A/π); intake A=0.030 → r ≈ 0.0977 m; exit A=0.040 → r ≈ 0.1128 m.
        Assert.Equal(Math.Sqrt(0.030 / Math.PI), contour.Stations[0].R_m, precision: 4);
        Assert.Equal(Math.Sqrt(0.040 / Math.PI), contour.ExitStation.R_m, precision: 4);
    }

    [Fact]
    public void From_NonPulsejetKind_ThrowsArgumentException()
    {
        var ramjet = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Ramjet,
            0.030, 0.075, 0.80, 0.025, 0.040, 0.95, 1.0);
        Assert.Throws<ArgumentException>(() => PulsejetGeometry.From(ramjet));
    }

    [Fact]
    public void From_NullDesign_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PulsejetGeometry.From(null!));
    }

    [Fact]
    public void From_FallsBackToLegacyFieldsWhenPulsejetSpecificAreZero()
    {
        // v5 design without pulsejet-specific fields populated. The
        // factory should fall back to InletThroatArea_m2, NozzleExitArea_m2,
        // and CombustorLength_m / CombustorFractionOfTotal.
        var legacy = new AirbreathingEngineDesign(
            AirbreathingEngineKind.Pulsejet,
            InletThroatArea_m2:      0.025,
            CombustorArea_m2:        0.060,
            CombustorLength_m:       0.50,
            NozzleThroatArea_m2:     0.020,
            NozzleExitArea_m2:       0.030,
            EquivalenceRatio:        0.90,
            CompressorPressureRatio: 1.0);
        // Pulsejet-specific fields default 0.0; factory should still build.
        var contour = PulsejetGeometry.From(legacy);
        Assert.Equal(5, contour.Stations.Length);
        Assert.True(contour.TotalLength_m > 0);
    }
}
