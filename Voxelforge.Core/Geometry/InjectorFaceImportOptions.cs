// InjectorFaceImportOptions.cs — Pure-data record for STL injector face
// import. Sprint A-3 / ADR-021 (2026-04-30): extracted from
// `Voxelforge.Voxels/Geometry/InjectorFaceImport.cs` so the headless
// orchestrators can reference it without dragging the Voxels project +
// PicoGK into Core. The static `InjectorFaceImport.Apply` and the
// PicoGK-shaped `InjectorFaceImportResult` stay in Voxels.

namespace Voxelforge.Geometry;

public sealed record InjectorFaceImportOptions(
    string StlPath,                     // absolute path to .stl
    bool   Enabled,                     // feature toggle
    double OffsetX_mm,                  // where to place STL min-X in chamber coords
    double UniformScale,                // 1.0 = no scale
    bool   AutoCenterYZ);               // translate so STL centres on chamber axis
