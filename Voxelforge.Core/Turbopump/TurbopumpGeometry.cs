namespace Voxelforge.Turbopump;

/// <summary>
/// Pure-data turbopump geometry record. Extracted to Core in A1 from
/// TurbopumpGeometryGenerator.cs (which stays in App because its
/// BuildImplicit path uses PicoGK).
/// </summary>
public sealed record TurbopumpGeometry(
    double ImpellerHubRadius_mm,
    double ImpellerTipRadius_mm,
    double ImpellerThickness_mm,     // per-stage axial thickness
    int    ImpellerBladeCount,
    double InducerHubRadius_mm,
    double InducerTipRadius_mm,
    double InducerLength_mm,
    int    InducerBladeCount,
    double VoluteMinorRadiusStart_mm,
    double VoluteMinorRadiusEnd_mm,
    double CasingOuterRadius_mm,
    double CasingLength_mm,
    double TotalLength_mm,           // inducer-inlet → volute-discharge face (incl. all stages)
    double EstimatedMass_g,           // rotor + casing material estimate
    string Notes,
    // Sprint 3 polish (2026-04-22) — N-stage fields.
    int    StageCount = 1,            // number of serial centrifugal stages
    double InterstageGap_mm = 0.0);   // axial gap between stacked impellers; 0 when StageCount = 1
