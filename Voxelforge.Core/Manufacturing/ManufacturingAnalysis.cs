// ManufacturingAnalysis.cs — Metal AM (LPBF) manufacturability assessment
// for printed regen chambers in copper alloys and superalloys.
//
// This is a pre-print sanity check only. Real qualification requires vendor
// process maps, CT inspection, and build simulation with software like
// Ansys Additive or Autodesk Netfabb.

using Voxelforge.Chamber;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Optimization;

namespace Voxelforge.Manufacturing;

public sealed record ManufacturingReport(
    WallMaterial Material,
    double BuildHeight_mm,
    double BuildDiameter_mm,
    int EstimatedLayers,
    double EstimatedBuildHours,
    double EstimatedBuildCost_USD,
    double MinFeatureSize_mm,
    bool FeatureSizeOK,
    bool RequiresInternalSupports,
    string BuildOrientationRecommendation,
    OverhangReport Overhang,
    string[] Warnings,
    string[] Recommendations);

public static class ManufacturingAnalysis
{
    // Typical LPBF process capability (scoping-grade — verify per vendor)
    private const double LpbfLayerThickness_mm = 0.030;
    private const double LpbfMinFeatureSize_mm = 0.30;
    private const double LpbfLayerTimeSec = 35.0;              // per layer, typical, 300 W 1-laser
    private const double LpbfSetupOverheadHours = 4.0;
    private const double LpbfMachineRate_USDHr = 85.0;

    public static ManufacturingReport Analyze(
        ChamberContour contour,
        ChannelSchedule channels,
        ChamberGeometryResult geom,
        WallMaterial material,
        RegenChamberDesign? design = null)
    {
        var warnings = new List<string>();
        var recs = new List<string>();

        // ── Port-to-chamber proportionality check ─────────────────
        // Real-world plumbing standards (G 1/4, NPT 1/4, SAE-4, etc.)
        // have boss diameters 13–30 mm. On a small test chamber
        // (500 N → OD ~32 mm) these protrude as ~50 % of the chamber
        // width, which is visually alarming and often a sign the user
        // should pick a miniature thread (M5/M6, G 1/16, NPT 1/16).
        if (design != null)
            CheckPortProportionality(design, contour, warnings);

        double buildHeight_mm = geom.BoundingLength_mm;
        double buildDia_mm = geom.BoundingDiameter_mm;
        int layers = (int)Math.Ceiling(buildHeight_mm / LpbfLayerThickness_mm);
        double buildHours = layers * LpbfLayerTimeSec / 3600.0 + LpbfSetupOverheadHours;
        double buildCost = buildHours * LpbfMachineRate_USDHr + geom.PrintedCost_USD;

        double minFeature = Math.Min(channels.RibThickness_mm,
                             Math.Min(channels.GasSideWallThickness_mm,
                                      Math.Min(channels.ChannelHeightAtThroat_mm * 0.5,
                                               FindMinChannelWidth(contour, channels))));
        bool featureOK = minFeature >= LpbfMinFeatureSize_mm;

        if (!featureOK)
            warnings.Add($"Minimum feature {minFeature:F2}mm below LPBF capability ({LpbfMinFeatureSize_mm:F2}mm).");

        if (channels.GasSideWallThickness_mm < 0.5)
            warnings.Add($"Gas-side wall {channels.GasSideWallThickness_mm:F2}mm is very thin; expect porosity risk.");

        if (channels.RibThickness_mm < 0.5)
            warnings.Add($"Rib thickness {channels.RibThickness_mm:F2}mm may collapse during printing.");

        if (buildHeight_mm > 400)
            warnings.Add($"Build height {buildHeight_mm:F0}mm exceeds typical 400mm LPBF envelope; requires large-format machine (EOS M400, SLM 800).");

        if (buildDia_mm > 350)
            warnings.Add($"Build diameter {buildDia_mm:F0}mm approaches large-format LPBF build-plate limits.");

        if (material.Name.Contains("Cu") || material.Name.Contains("GRCop"))
        {
            recs.Add("Copper alloys: use green-laser or IR with enhanced beam absorbers. Vendors: Elementum 3D (GRCop-42), Velo3D, Trumpf TruPrint 3000 green.");
            recs.Add("Expect 20–30 % longer print time for copper alloys vs. Inconel at same layer thickness.");
        }

        // Channels are the primary internal feature.  Axial channels with the
        // build axis aligned to the X (chamber) axis self-support walls but
        // need down-facing rib overhangs supported implicitly by channel width.
        bool needsInternalSupports = false;
        string orientation;
        if (buildHeight_mm < buildDia_mm * 1.2)
        {
            orientation = "Vertical (chamber axis = build axis). Shorter build height.";
        }
        else
        {
            orientation = "Vertical (chamber axis = build axis). Minimizes cross-section, critical for copper cooling.";
        }
        recs.Add(orientation);
        recs.Add("Powder removal: plan horizontal inlet port to vent both manifold plenums. CT-scan first article.");
        recs.Add("Post-processing: HIP at ~1000°C/100 MPa/4h (copper) or 1185°C/100 MPa/4h (Inconel), then heat treat.");

        if (channels.RibThickness_mm < 0.4)
        {
            needsInternalSupports = true;
            warnings.Add("Rib thickness below ~0.4 mm may require internal supports — discuss with print vendor.");
        }

        // Per-station 45° overhang check on both inner and outer surfaces.
        var overhang = OverhangAnalysis.Analyze(contour, channels, geom.OuterJacketThickness_mm);
        if (!overhang.AllSelfSupporting)
        {
            needsInternalSupports = true;
            if (overhang.UnprintableStationCount > 0)
                orientation += $"  (overhang @ {overhang.UnprintableStationCount} stations — consider {overhang.RecommendedBuildOrientation})";
        }
        foreach (var w in overhang.Warnings) warnings.Add(w);

        return new ManufacturingReport(
            Material: material,
            BuildHeight_mm: buildHeight_mm,
            BuildDiameter_mm: buildDia_mm,
            EstimatedLayers: layers,
            EstimatedBuildHours: buildHours,
            EstimatedBuildCost_USD: buildCost,
            MinFeatureSize_mm: minFeature,
            FeatureSizeOK: featureOK,
            RequiresInternalSupports: needsInternalSupports,
            BuildOrientationRecommendation: orientation,
            Overhang: overhang,
            Warnings: warnings.ToArray(),
            Recommendations: recs.ToArray());
    }

    private static double FindMinChannelWidth(ChamberContour c, ChannelSchedule ch)
    {
        double minW = double.MaxValue;
        foreach (var s in c.Stations)
        {
            double r_outer = s.R_mm + ch.GasSideWallThickness_mm;
            double pitch = 2.0 * Math.PI * r_outer / ch.ChannelCount;
            double w = pitch - ch.RibThickness_mm;
            if (w < minW) minW = w;
        }
        return minW;
    }

    /// <summary>
    /// Flag threaded ports whose boss OD is a large fraction of the local
    /// chamber OD. Threshold 0.40 matches the visual heuristic that a port
    /// bigger than ~½ the chamber radius looks wrong and usually means the
    /// user picked a real-plumbing size on a sub-kN test engine. Suggests a
    /// miniature preset when triggered.
    /// </summary>
    private static void CheckPortProportionality(
        RegenChamberDesign design, ChamberContour contour, List<string> warnings)
    {
        const double ratioThreshold = 0.40;
        double chamberOD_mm = 2.0 * contour.ChamberRadius_mm;

        void Check(PortStandard standard, string role, string suggestion)
        {
            if (standard == PortStandard.Plain) return;
            var spec = PortStandards.Get(standard);
            double ratio = spec.BossDiaMM / chamberOD_mm;
            if (ratio > ratioThreshold)
                warnings.Add(
                    $"{role} thread {spec.Name} has boss OD {spec.BossDiaMM:F1} mm "
                    + $"= {100 * ratio:F0}% of chamber OD {chamberOD_mm:F1} mm "
                    + $"(threshold {100 * ratioThreshold:F0}%). {suggestion}");
        }

        Check(design.CoolantPortStandard, "Coolant port",
              "Try G 1/16, NPT 1/16, or M6/M8 for sub-kN chambers.");
        Check(design.PropellantPortStandard, "Propellant port",
              "Try M5/M6 for small injectors; upsize thrust class for G/NPT fittings.");
    }
}
