namespace Voxelforge.Turbopump;

/// <summary>
/// Parametric turbine-stage geometry summary. Extracted to Core in A1
/// from TurbineGeometryGenerator.cs (which stays in App because the
/// BuildImplicit path uses PicoGK).
/// </summary>
public sealed record TurbineGeometry(
    double WheelHubRadius_mm,
    double WheelTipRadius_mm,
    double WheelThickness_mm,
    int    WheelBladeCount,
    double StatorInnerRadius_mm,
    double StatorOuterRadius_mm,
    double StatorAxialHeight_mm,
    int    StatorVaneCount,
    double NozzleThroatArea_mm2,
    double HousingOuterRadius_mm,
    double TotalLength_mm,
    double EstimatedMass_g,
    string Notes);
