// AerospikeSpec.cs — Pure-data spec for the aerospike voxel/STL
// pipeline.
//
// Sprint A-3 Phase 2 / ADR-021 (2026-04-30): extracted from
// `Voxelforge.Voxels/Geometry/AerospikeBuilder.cs` into Core so the
// orchestrator (`RegenChamberOptimization` / `AerospikeOptimization`,
// moving to Core in Phase 2) can reference the spec without dragging
// the Voxels project + PicoGK into Core. The static
// `AerospikeBuilder.{Build,BuildPhysicsOnly,BuildLinearPhysicsOnly}`
// methods stay in Voxels.

using Voxelforge.Combustion;

namespace Voxelforge.Geometry;

public sealed record AerospikeSpec(
    double          Thrust_N,
    double          ChamberPressure_Pa,
    double          ExpansionRatio,
    double          PlugLengthRatio,
    PropellantPair  PropellantPair = PropellantPair.LOX_CH4,
    double          MixtureRatio = 3.3,
    double          CStarEfficiency = 0.95,
    double          OuterShellThickness_mm = 3.0,
    double          ChamberLengthRatio = 1.2,
    // ── Optional plug cooling fields ───────────────────────────────
    /// <summary>
    /// Enable plug-interior regen-cooling channels. When false
    /// (default) the plug is solid — use the geometry-only path. When
    /// true the builder cuts N axial channels just beneath the plug
    /// external surface and invokes
    /// <see cref="HeatTransfer.AerospikePlugCooling.Solve"/> to get a
    /// peak-wall-T estimate for the plug-wall-temp feasibility gate.
    /// </summary>
    bool            IncludeRegenChannels = false,
    /// <summary>Number of axial regen channels around the plug (typ. 16–40).</summary>
    int             PlugChannelCount = 24,
    /// <summary>Channel width (mm) measured on the plug circumference.</summary>
    double          PlugChannelWidth_mm = 2.5,
    /// <summary>Channel depth (mm) measured radially from the plug surface inward.</summary>
    double          PlugChannelDepth_mm = 2.0,
    /// <summary>Plug wall thickness (mm) between combustion gas and channel.</summary>
    double          PlugWallThickness_mm = 0.8,
    /// <summary>Coolant inlet temperature (K). Default 120 K matches LCH4 saturation band.</summary>
    double          CoolantInletTemp_K = 120.0,
    /// <summary>Coolant inlet pressure (Pa). Default 12 MPa matches the default regen pair.</summary>
    double          CoolantInletPressure_Pa = 12e6,
    /// <summary>Wall material index. 0=GRCop42, 1=CuCrZr, 2=Inc625, 3=Inc718 (matches <see cref="Optimization.OperatingConditions.WallMaterialIndex"/>).</summary>
    int             WallMaterialIndex = 1,
    // ── Injector integration ──────────────────────────────────────
    /// <summary>
    /// Optional injector pattern to size against the aerospike
    /// pre-throat combustion chamber face. When null (default) the
    /// builder skips pattern sizing and leaves
    /// <c>AerospikeBuildResult.InjectorSizing</c> null — preserving
    /// legacy behaviour. When non-null, the builder derives face-
    /// placement geometry (pitch-circle radius, arc spacing, per-
    /// element OD estimate) and the <c>AEROSPIKE_ELEMENT_CLEARANCE</c>
    /// feasibility gate fires when elements don't fit without
    /// colliding.
    /// </summary>
    Injector.InjectorPattern? InjectorPattern = null,
    // ── SA-tunable chamber contraction ────────────────────────────
    /// <summary>
    /// Pre-throat chamber contraction ratio (A_chamber / A_throat).
    /// Surfaces as an <c>RegenChamberDesign.AerospikeContractionRatio</c>
    /// analogue so the SA optimiser can tune it. Default 6.0 preserves
    /// legacy sizing bit-identically. Smaller values give a more
    /// compact chamber; larger values give more residence time.
    /// </summary>
    double ChamberContractionRatio = 6.0,
    // ── Sprint 26 (2026-04-23) linear-aerospike extension ────────────
    /// <summary>
    /// Sprint 26: select the linear (extruded-rectangular) plug
    /// topology instead of the classic axisymmetric plug. The Angelino
    /// 2D expansion curve is identical; what differs is how it's
    /// interpreted downstream (revolved vs extruded) and the throat-
    /// area accounting (π(R_o² − R_i²) vs 2·h·W). When true, the
    /// builder dispatches to <c>AerospikeBuilder.BuildLinearPhysicsOnly</c>;
    /// when false (default) it uses the axisymmetric path.
    /// </summary>
    bool IsLinear = false,
    /// <summary>
    /// Sprint 26: transverse extrusion width (mm) of the linear plug.
    /// Unused on axisymmetric builds. Paired with the plug-tip half-
    /// height derived from throat area to drive the rectangular
    /// cross-section and the <c>LINEAR_AEROSPIKE_ASPECT_RATIO</c>
    /// feasibility gate. Default 60 mm matches the X-33 XRS-2200
    /// thrust-cell width scale.
    /// </summary>
    double LinearPlugWidth_mm = 60.0,
    /// <summary>
    /// PH-36 aerospike-face follow-on (2026-04-29 — closes #233): per-pair
    /// oxidizer injection T (K) for the lumped face equilibrium in
    /// <see cref="HeatTransfer.AerospikeInjectorFaceThermal.Estimate"/>.
    /// Default 0 → falls back to
    /// <see cref="HeatTransfer.InjectorFaceThermal.DefaultOxidizerInjectionT_K"/>(pair)
    /// (90.18 K for LOX-based pairs, 290-293 K for storables). Surfaces
    /// here from <c>OperatingConditions.OxidizerInletTemp_K</c> via
    /// <c>AerospikeOptimization.ToSpec</c>.
    /// </summary>
    double OxidizerInletTemp_K = 0.0,
    /// <summary>
    /// PH-35 aerospike-face follow-on (2026-04-29 — closes #234): face
    /// material max-service-T override (K). Default 0 →
    /// <see cref="HeatTransfer.InjectorFaceThermal.DefaultInjectorFaceMaxTemp_K"/>
    /// (1200 K, IN625/SS face material). Surfaces here from
    /// <c>OperatingConditions.InjectorFaceMaxTemp_K_Override</c> and
    /// feeds the <c>AEROSPIKE_INJECTOR_FACE_TEMP</c> feasibility gate
    /// via the new
    /// <see cref="HeatTransfer.AerospikeInjectorFaceResult.MaxServiceTemp_K"/>
    /// field.
    /// </summary>
    double InjectorFaceMaxTemp_K_Override = 0.0);
