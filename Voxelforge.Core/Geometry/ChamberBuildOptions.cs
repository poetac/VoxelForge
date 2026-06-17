// ChamberBuildOptions.cs — Pure-data build-options record for the chamber
// voxel/STL pipeline.
//
// Sprint A-3 / ADR-021 (2026-04-30): extracted from
// `Voxelforge.Voxels/Geometry/ChamberVoxelBuilder.cs` into Core so the
// orchestrators (`RegenChamberOptimization` + `AerospikeOptimization`)
// can hold the options type without dragging the Voxels project (and
// its PicoGK dependency) into Core. The `ChamberVoxelBuilder` static
// class with its `Build` / `BuildAnalytical` methods stays in Voxels
// where PicoGK is available.

using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;

namespace Voxelforge.Geometry;

public sealed record ChamberBuildOptions(
    ChamberContour Contour,
    ChannelSchedule Channels,
    double OuterJacketThickness_mm = 2.0,
    double ManifoldLength_mm = 15.0,
    double ManifoldRadialDepth_mm = 6.0,
    bool IncludeManifolds = true,
    bool IncludeInletOutletPorts = true,
    double PortDiameter_mm = 10.0,
    PortStandard CoolantPortStandard = PortStandard.Plain,
    bool IncludeInjectorFlange = true,
    double InjectorFlangeThickness_mm = 8.0,
    double InjectorFlangeOuterRadiusFactor = 1.25,      // × R_c
    double PropellantPortDiameter_mm = 6.0,
    PortStandard PropellantPortStandard = PortStandard.Plain,
    bool IncludeMountingFlange = false,
    double MountingFlangeThickness_mm = 6.0,
    /// <summary>Parent audit §4: bolt-pattern preset for the mounting flange.</summary>
    MountingFlangeStandard MountingFlangeStandard = MountingFlangeStandard.Generic8Bolt,
    double SmoothingRadius_mm = 0.0,
    double ChannelManifoldFilletRadius_mm = 0.8,
    WallMaterial? MaterialForMass = null,
    InjectorFaceImportOptions? InjectorFaceSTL = null,
    InjectorPattern? InjectorElementPattern = null,
    InjectorFlowContext? InjectorFlowCtx = null,
    /// <summary>PHASE 4: pitch angle (deg) for helical channels; 0 = axial.</summary>
    double HelixPitchAngle_deg = 0.0,
    /// <summary>PHASE 5: drill an internal passage from coolant outlet to injector fuel plenum.</summary>
    bool IncludeCoolantCrossover = false,
    /// <summary>PHASE 5: diameter (mm) of the coolant-crossover passage.</summary>
    double CoolantCrossoverDiameter_mm = 3.0,
    /// <summary>TIER A.4: instrumentation-boss list drilled through outer jacket.</summary>
    System.Collections.Generic.IReadOnlyList<SensorBoss>? SensorBosses = null,
    /// <summary>Igniter preset for the injector flange.</summary>
    IgniterType IgniterType = IgniterType.None,
    /// <summary>Igniter cavity radial offset, fraction of chamber radius.</summary>
    double IgniterRadialFraction = 0.0,
    /// <summary>Fuel-side injector dome depth (mm). 0 = no dome cut.</summary>
    double FuelDomeDepth_mm = 0.0,
    /// <summary>Ox-side injector dome depth (mm). 0 = no dome cut.</summary>
    double OxDomeDepth_mm = 0.0,
    /// <summary>Anti-vortex baffle drawn as a thin radial disc inside the dome.</summary>
    bool IncludeAntiVortexBaffle = false,
    /// <summary>
    /// When true (ChannelTopology.None), the regen-channel subtraction +
    /// manifold plenums + coolant ports are skipped entirely; the
    /// chamber becomes a plain shell of wall + jacket thickness for an
    /// ablative-only build. BuildAnalytical uses the same flag to zero
    /// out the channel-volume subtrahend.
    /// </summary>
    bool SkipChannelGeneration = false,
    /// <summary>
    /// Umbilical / quick-disconnect preset. When non-`None` AND
    /// <see cref="IncludeInjectorFlange"/> is true, the
    /// propellant-flange face gets an annular seal groove and a 4-bolt
    /// pattern around each propellant port mating face. Spec is read
    /// from <see cref="UmbilicalStandards"/>.
    /// </summary>
    UmbilicalStandard UmbilicalStandard = UmbilicalStandard.None,
    /// <summary>
    /// Purge-port list. Each entry gets a bore drilled into the chamber
    /// at a default location keyed off <see cref="Coolant.PurgeLocation"/>:
    ///   • InjectorDome{Ox,Fuel} — axial bore through injector flange
    ///   • ChamberPrePurge       — radial bore through outer jacket at mid-chamber
    ///   • NozzleInertPurge      — radial bore through outer jacket at 90 % of length
    /// Empty / null list = no bores drilled. PurgePort.BoreDiameter_mm
    /// drives bore diameter; the model already gates flow against this
    /// upstream of voxel build, so no additional sizing happens here.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<Coolant.PurgePort>? PurgePorts = null,
    /// <summary>
    /// Gimbal mount configuration. When non-`FixedFlange` AND
    /// <see cref="IncludeMountingFlange"/> is true, the mounting flange
    /// gets trunnion lugs / flexure arms drawn aft of the nozzle exit
    /// per the configuration:
    ///   • PinJointGimbal  — 2 cylindrical lugs at ±Y with a pin hole
    ///   • CardanGimbal    — 4 cylindrical lugs at ±Y, ±Z with pin holes
    ///   • FlexureGimbal   — 4 thin rectangular flexure arms in cruciform
    /// Geometry sized from the constants in <see cref="Structure.GimbalMount"/>.
    /// </summary>
    Structure.MountConfiguration MountConfiguration = Structure.MountConfiguration.FixedFlange,
    /// <summary>
    /// TPMS unit-cell shape to cut into the jacket void when a TPMS
    /// topology is active. Null (default) preserves axial/helical
    /// channel behaviour. When non-null, the ordinary per-channel
    /// <see cref="AxialChannelImplicit"/> loop is replaced by a single
    /// <see cref="TpmsAnnularImplicit"/> pass.
    /// </summary>
    HeatTransfer.TpmsKind? TpmsKind = null,
    /// <summary>TPMS unit-cell edge length (mm). Ignored when <see cref="TpmsKind"/> is null.</summary>
    double TpmsCellEdge_mm = 3.0,
    /// <summary>TPMS solid-volume fraction. Ignored when <see cref="TpmsKind"/> is null.</summary>
    double TpmsSolidFraction = 0.50,
    /// <summary>
    /// Z1 hot-fix / Track B closed-loop (2026-04-28): per-station gas-side
    /// wall thickness profile, in mm, indexed by station. When non-null
    /// AND <c>Length == Contour.Stations.Length</c> the builder substitutes
    /// the per-station value for <see cref="ChannelSchedule.GasSideWallThickness_mm"/>
    /// at four sites:
    ///   • <see cref="ChamberVoxelBuilder.BuildAnalytical"/> mass / cost integral
    ///   • Outer-jacket revolve in the full PicoGK build
    ///   • TPMS-implicit <c>tWall</c> (uses <c>profile.Min()</c>, conservative
    ///     bound — TPMS sees the thinnest wall to keep wall-strut clearance)
    ///   • Smoothen safety cap (uses <c>profile.Min()</c> — keeps the
    ///     25 % feature-floor honoured everywhere on the wall)
    /// Null (default) preserves uniform behaviour bit-identically. Length
    /// mismatches are silently treated as null (defensive).
    /// </summary>
    System.Collections.Generic.IReadOnlyList<double>? GasSideWallProfile_mm = null,
    /// <summary>
    /// Hot-fire readiness Item 6 (#260, 2026-04-30): adds a structural
    /// thrust-takeout adapter body downstream of the mounting flange.
    /// Requires <see cref="IncludeMountingFlange"/> = true. Adapter sits
    /// between the chamber's mounting flange and the test-stand load cell.
    /// </summary>
    bool IncludeThrustTakeoutAdapter = false,
    /// <summary>Adapter body height (mm) along the engine axis.</summary>
    double ThrustTakeoutAdapterHeight_mm = 50.0,
    /// <summary>Adapter outer diameter (mm). 0 = match mounting-flange OD.</summary>
    double ThrustTakeoutOuterDiameter_mm = 0.0,
    /// <summary>Test-stand-side bolt-circle preset on the adapter's
    /// bottom face. Distinct from <see cref="MountingFlangeStandard"/>.</summary>
    MountingFlangeStandard ThrustTakeoutMountStandard = MountingFlangeStandard.Generic8Bolt,
    /// <summary>Count of radial umbilical / instrumentation pass-throughs
    /// drilled through the adapter sidewall. 0 = none.</summary>
    int ThrustTakeoutUmbilicalPassThroughCount = 0,
    /// <summary>Diameter (mm) of each umbilical pass-through hole.</summary>
    double ThrustTakeoutUmbilicalPassThroughDiameter_mm = 8.0,
    /// <summary>OOB-6 / Sprint B-3 (#200, 2026-04-30): acoustic-damper
    /// family. <see cref="Combustion.Stability.AcousticDamperType.None"/>
    /// suppresses the damper voxel-feature pass entirely (legacy
    /// behaviour bit-identical).</summary>
    Combustion.Stability.AcousticDamperType DamperType
        = Combustion.Stability.AcousticDamperType.None,
    /// <summary>Resonator count distributed around the chamber circumference.</summary>
    int DamperCount = 8,
    /// <summary>Helmholtz neck area (mm²).</summary>
    double HelmholtzNeckArea_mm2 = 30.0,
    /// <summary>Helmholtz neck length (mm).</summary>
    double HelmholtzNeckLength_mm = 6.0,
    /// <summary>Helmholtz cavity volume (mm³).</summary>
    double HelmholtzCavityVolume_mm3 = 1500.0,
    /// <summary>Quarter-wave cavity length (mm).</summary>
    double QuarterWaveLength_mm = 20.0,
    /// <summary>Quarter-wave cavity diameter (mm).</summary>
    double QuarterWaveDiameter_mm = 4.0,
    /// <summary>
    /// #337 / OOB-13 (2026-05-04): when true, a solid truncated-cone inner
    /// plug is fused into the bell region of the voxel assembly. Only
    /// meaningful for <see cref="Voxelforge.Optimization.ChannelTopology.ExpansionDeflection"/>;
    /// inert (false by default) on all other topologies so legacy STL output
    /// is bit-identical.
    /// </summary>
    bool IncludeExpansionDeflectionPlug = false,
    /// <summary>
    /// Plug inner/outer radius ratio at the annular throat (R_plug / R_cowl).
    /// Matches the Angelino fixed ratio used in the physics model. Only
    /// read when <see cref="IncludeExpansionDeflectionPlug"/> is true.
    /// </summary>
    double EdPlugInnerOuterRatio = 0.40,
    /// <summary>
    /// OOB-2 Sprint 2 (2026-05-04): per-station channel count from the SIMP
    /// topology optimizer (<see cref="Voxelforge.Optimization.TopologyOptimizedChannels"/>).
    /// Only consumed when the design's
    /// <see cref="Voxelforge.Optimization.ChannelTopology"/> is
    /// <see cref="Voxelforge.Optimization.ChannelTopology.TopologyOptimized"/>;
    /// other topologies ignore both this field and
    /// <see cref="TopologyOptimizedAxialPositions_mm"/>. Pair indices align
    /// with <see cref="TopologyOptimizedAxialPositions_mm"/>; both arrays
    /// must have the same length when supplied. Null on every other
    /// topology so existing voxel output is bit-identical.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<int>? TopologyOptimizedChannelsPerStation = null,
    /// <summary>
    /// OOB-2 Sprint 2 (2026-05-04): axial sample positions (mm) for the
    /// <see cref="TopologyOptimizedChannelsPerStation"/> field. Sorted
    /// ascending; the implicit linearly interpolates the channel-count
    /// field between samples. Default null = legacy uniform-N behaviour.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<double>? TopologyOptimizedAxialPositions_mm = null);
