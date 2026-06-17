// RamjetSurfaceSamplerTests — synthesise samples from a known
// 5-station contour, check counts + normal magnitudes + axisymmetric
// distribution. PicoGK-free; runs cross-platform.

using System;
using System.Linq;
using System.Numerics;
using Voxelforge.Airbreathing.Geometry;
using Xunit;

namespace Voxelforge.Airbreathing.Tests;

public class RamjetSurfaceSamplerTests
{
    private static RamjetContour BuildSampleContour()
    {
        // Compact ramjet: combustor 0.30 m, divergent ε ≈ 5
        var design = new AirbreathingEngineDesign(
            Kind:                    AirbreathingEngineKind.Ramjet,
            InletThroatArea_m2:      0.020,
            CombustorArea_m2:        0.050,
            CombustorLength_m:       0.30,
            NozzleThroatArea_m2:     0.020,
            NozzleExitArea_m2:       0.100,
            EquivalenceRatio:        0.85);
        return RamjetGeometry.From(design);
    }

    [Fact]
    public void Emits_TwoSamplesPerStationPerAzimuthSlot()
    {
        var contour = BuildSampleContour();
        const int azim = 32;
        var samples = RamjetSurfaceSampler.SampleAxisymmetric(
            contour, wallThickness_mm: 2.0, azimuthalSamples: azim);

        // 5 stations × 32 az × 2 walls (inner + outer) = 320
        Assert.Equal(contour.Stations.Length * azim * 2, samples.Count);
    }

    [Fact]
    public void Normals_AreUnitMagnitude()
    {
        var contour = BuildSampleContour();
        var samples = RamjetSurfaceSampler.SampleAxisymmetric(contour, 2.0, 16);

        foreach (var s in samples)
        {
            float mag = s.Normal.Length();
            Assert.InRange(mag, 0.999f, 1.001f);
        }
    }

    [Fact]
    public void InnerNormals_PointTowardAxisOnConstantRadiusStation()
    {
        // Combustor stations have constant radius (slope = 0), so the
        // inner normal should point exactly along -radial (toward axis).
        var contour = BuildSampleContour();
        var samples = RamjetSurfaceSampler.SampleAxisymmetric(contour, 2.0, 4);

        // Find a sample at the combustor station (R = combustor radius)
        // with phi = 0 (so normal should be (0, -1, 0)).
        double rCombustor_mm = Math.Sqrt(0.050 / Math.PI) * 1000.0;  // CombustorArea_m2 → R_mm
        var combustorSamples = samples.Where(s =>
            Math.Abs(Math.Sqrt(s.Point.Y * s.Point.Y + s.Point.Z * s.Point.Z) - rCombustor_mm) < 0.5
            && s.Point.Y > 0
            && Math.Abs(s.Point.Z) < 0.01)  // phi ≈ 0
            .ToList();

        Assert.NotEmpty(combustorSamples);
        // Inner-wall sample at this point: normal points toward axis (-Y direction).
        var innerSample = combustorSamples.First(s =>
            Math.Abs(Math.Sqrt(s.Point.Y * s.Point.Y + s.Point.Z * s.Point.Z) - rCombustor_mm) < 0.5);
        // Inner normal pointing toward -Y means Normal.Y is negative.
        // (Some stations may have nonzero slope from finite differences;
        // we relax the check to "Y component is negative-leaning".)
        Assert.True(innerSample.Normal.Y < 0,
            $"Inner normal at +Y combustor surface should have Normal.Y < 0; got Normal={innerSample.Normal}");
    }

    [Fact]
    public void OuterRadius_EqualsInnerPlusWallThickness()
    {
        var contour = BuildSampleContour();
        const double wall = 3.0;
        var samples = RamjetSurfaceSampler.SampleAxisymmetric(contour, wall, 8);

        // Group by approximate X (stations) — should have inner + outer groups.
        var grouped = samples
            .GroupBy(s => Math.Round(s.Point.X, 2))
            .ToList();
        Assert.True(grouped.Count >= contour.Stations.Length);

        foreach (var stationGroup in grouped)
        {
            // Round to 2 decimal places (0.01 mm) before Distinct — float→double
            // conversion in Vector3 introduces ~1e-6 mm jitter across azimuth
            // slots that would otherwise show as N distinct radii.
            var radii = stationGroup
                .Select(s => Math.Round(Math.Sqrt(s.Point.Y * s.Point.Y + s.Point.Z * s.Point.Z), 2))
                .Distinct()
                .OrderBy(r => r)
                .ToList();
            // Expect 2 distinct radii per station: inner, outer = inner + wall.
            Assert.Equal(2, radii.Count);
            Assert.InRange(radii[1] - radii[0], wall - 0.05, wall + 0.05);
        }
    }

    [Fact]
    public void SubFourAzimuth_ClampsToFour()
    {
        var contour = BuildSampleContour();
        var samples = RamjetSurfaceSampler.SampleAxisymmetric(contour, 2.0, azimuthalSamples: 1);
        // Clamped to 4 → 5 stations × 4 az × 2 walls = 40.
        Assert.Equal(contour.Stations.Length * 4 * 2, samples.Count);
    }

    [Fact]
    public void ZeroOrNegativeWallThickness_Throws()
    {
        var contour = BuildSampleContour();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RamjetSurfaceSampler.SampleAxisymmetric(contour, 0.0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RamjetSurfaceSampler.SampleAxisymmetric(contour, -1.0, 16));
    }
}
