// MonolithicEngineBuilder.cs — Composes chamber + turbopump +
// preburner + feed manifold into a single voxel body + single STL.
// The headline "functionally-integrated part that doesn't require
// assembly". Reads design.FlangeRadialProjection_mm — the
// SA-promoted flange-projection knob — when sizing pump mount flanges.
//
// Pipeline
// ────────
//   1. Run AutoSeeder on the 4-input spec (propellant / thrust / Pc /
//      ε) with an explicit EngineCycleOverride so the feed-system
//      has a non-null turbopump + (optionally) a preburner.
//   2. Call `ChamberVoxelBuilder.Build` for the main regen chamber.
//   3. Call `TurbopumpGeometryGenerator` + `BuildImplicit` for fuel
//      and ox pumps. Pump voxels live in separate bounding boxes
//      from the chamber (different coordinate frames).
//   4. Route the feed tubes via `FeedManifoldRouter.Route`. Tube
//      voxels share a bounding box spanning all body positions.
//   5. Voxelise each non-chamber body in a unified bounding box +
//      `BoolAdd` them onto the chamber body.
//   6. Return one monolithic `Voxels` ready to mesh + export.
//
// Scope caveats
// ─────────────
//   • Chamber dispatch: regen-chamber path originally; aerospike
//     monolithic composition was added later as a same-pattern bundle.
//   • Pump placement: fuel pump at `(+50, −80, 0)` relative to
//     chamber origin with shaft +Z, ox pump at `(−50, −80, 0)`. Both
//     sit outside the chamber envelope. Production layouts would
//     route the shafts along a gearbox; Phase 1 models them as
//     independent pumps for geometry purposes.
//   • No flange / bearing / seal detail. The monolithic voxel is a
//     first-cut representation that a real user / shop inspects in
//     Slicer before committing to metal.
//   • No FEA / mass / CoG / thermal integration check — the regen
//     feasibility gate + preburner warnings + aerospike gate remain
//     the engineering-audit layer; this builder ships geometry only.

using System.Linq;
using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;
using Voxelforge.Turbopump;

namespace Voxelforge.Geometry;

/// <summary>
/// Result bundle from <see cref="MonolithicEngineBuilder.Build"/>.
/// Includes optional <see cref="PreburnerVoxelGeometry"/> and
/// <see cref="PumpMountFlangeGeometry"/> fields so callers can surface
/// the fused bodies without re-deriving them.
/// </summary>
public sealed record MonolithicEngineResult(
    Voxels EngineVoxels,
    RegenGenerationResult ChamberResult,
    TurbopumpGeometry? FuelPumpGeometry,
    TurbopumpGeometry? OxPumpGeometry,
    FeedManifoldLayout ManifoldLayout,
    int ComponentBodyCount,
    double EstimatedEngineMass_g,
    string Description,
    PreburnerVoxelGeometry? PreburnerGeometry = null,
    PumpMountFlangeGeometry? FuelPumpFlangeGeometry = null,
    PumpMountFlangeGeometry? OxPumpFlangeGeometry = null,
    // Body-intersection feasibility gate result. IsFeasible=true
    // means no routed tube passes through a non-endpoint body.
    FeasibilityGateResult? BodyIntersectionGate = null,
    // Sprint 9 Track A (2026-04-22) — optional aerospike-chamber build
    // result, populated only by the Aerospike monolithic pipeline
    // (MonolithicEngineBuilder.BuildAerospike). Null on the regen
    // monolithic path. When present, <see cref="ChamberResult"/> still
    // carries the physics-only regen-shape sidecar that the turbopump /
    // preburner sizing ran against; Aerospike carries the actual
    // voxelised aerospike body that was BoolAdded into
    // <see cref="EngineVoxels"/>.
    AerospikeBuildResult? AerospikeChamber = null);

/// <summary>
/// Single-shot monolithic-engine composer. Task-thread-only (PicoGK
/// Library convention).
/// </summary>
public static class MonolithicEngineBuilder
{
    /// <summary>
    /// Compose the full engine from an <see cref="EngineSpec"/>. The
    /// spec should specify <c>EngineCycleOverride</c> (GasGenerator /
    /// StagedCombustion / FullFlow) — PressureFed produces an engine
    /// with feed lines but no pumps (still valid; monolithic output
    /// is correctly smaller).
    ///
    /// Cycle + AutoSeeder set <c>cond.IncludeTurbopumpGeometry = true</c>
    /// automatically so downstream turbopump geometry is always
    /// populated on non-PressureFed engines.
    /// </summary>
    /// <summary>
    /// Default toroidal bend fillet radius (mm). Non-zero enables the
    /// fillet on all cornered discharge + preburner-exhaust tubes. Set
    /// to 0 to reproduce mitred behaviour.
    /// </summary>
    public const double DefaultBendFilletRadius_mm = FeedManifoldRouter.DefaultBendFilletRadius_mm;

    public static MonolithicEngineResult Build(
        EngineSpec spec,
        double voxelSize_mm = Voxelforge.Constants.VoxelConstants.DefaultBuilderVoxelSize_mm,
        double bendFilletRadius_mm = DefaultBendFilletRadius_mm,
        bool   includePumpMountFlanges = true,
        bool   includePreburnerBody = true)
    {
        if (spec is null) throw new System.ArgumentNullException(nameof(spec));
        if (voxelSize_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(voxelSize_mm),
                "voxel size must be positive");

        // ── 1. AutoSeed ────────────────────────────────────────────
        var seed = AutoSeeder.Seed(spec);
        return BuildRegenCore(
            seed.Conditions, seed.Design,
            voxelSize_mm, bendFilletRadius_mm,
            includePumpMountFlanges, includePreburnerBody,
            descriptionSpec: spec);
    }

    /// <summary>
    /// Sprint 28 (2026-04-23): full-design-fidelity monolithic composition.
    /// Takes the same <see cref="OperatingConditions"/> + <see cref="RegenChamberDesign"/>
    /// the user tuned in the UI / saved to disk and fuses a monolithic
    /// engine around them, honoring every knob on the saved design
    /// (channel schedule, film fraction, injector pattern, flange specs,
    /// port standards, …). The <see cref="Build(EngineSpec, double, double, bool, bool)"/>
    /// overload retains the auto-seeded behaviour for CLI / benchmark use
    /// where only the scalar inputs are available.
    /// <para>
    /// Dispatches on <see cref="RegenChamberDesign.ChannelTopology"/>:
    /// <see cref="ChannelTopology.Aerospike"/> routes to the aerospike
    /// composition path; everything else routes to the regen-bell
    /// composition path.
    /// </para>
    /// </summary>
    public static MonolithicEngineResult BuildFromDesign(
        OperatingConditions cond,
        RegenChamberDesign design,
        double voxelSize_mm = Voxelforge.Constants.VoxelConstants.DefaultBuilderVoxelSize_mm,
        double bendFilletRadius_mm = DefaultBendFilletRadius_mm,
        bool   includePumpMountFlanges = true,
        bool   includePreburnerBody = true)
    {
        if (cond is null) throw new System.ArgumentNullException(nameof(cond));
        if (design is null) throw new System.ArgumentNullException(nameof(design));
        if (voxelSize_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(voxelSize_mm),
                "voxel size must be positive");

        // Monolithic composition implies turbopump voxels in the output;
        // force the flag on so the downstream pump geometry is populated
        // even if the saved design left it off (the UI default pre-Sprint 28).
        var effectiveCond = cond with { IncludeTurbopumpGeometry = true };

        return ChannelTopologyDispatcher.IsAerospikeAxisymmetric(design.ChannelTopology)
            ? BuildAerospikeCore(
                effectiveCond, design,
                voxelSize_mm, bendFilletRadius_mm,
                includePumpMountFlanges, includePreburnerBody,
                descriptionSpec: null)
            : BuildRegenCore(
                effectiveCond, design,
                voxelSize_mm, bendFilletRadius_mm,
                includePumpMountFlanges, includePreburnerBody,
                descriptionSpec: null);
    }

    /// <summary>
    /// Post-AutoSeed regen-bell monolithic composition. Extracted from
    /// <see cref="Build(EngineSpec, double, double, bool, bool)"/> so
    /// <see cref="BuildFromDesign"/> can reuse the full composition
    /// pipeline without re-seeding the design from scalars.
    /// <paramref name="descriptionSpec"/> is non-null only on the
    /// EngineSpec path; the from-design path passes null and the
    /// description is composed from (cond, design) directly.
    /// </summary>
    private static MonolithicEngineResult BuildRegenCore(
        OperatingConditions cond,
        RegenChamberDesign design,
        double voxelSize_mm,
        double bendFilletRadius_mm,
        bool   includePumpMountFlanges,
        bool   includePreburnerBody,
        EngineSpec? descriptionSpec)
    {
        cond = cond with { IncludeTurbopumpGeometry = true };

        // ── 2. Chamber voxel build + regen sizing ──────────────────
        var chamberResult = RegenChamberOptimization.GenerateWith(
            cond, design, voxelSize_mm: voxelSize_mm,
            voxelGenerator: new ChamberVoxelBuilderAdapter());
        var engineVoxels = chamberResult.Geometry.Voxels.AsPicoGK();

        // ── 3. Turbopump geometries (pumps stay null on PressureFed) ─
        var fuelGeom = chamberResult.Turbopump?.FuelPumpGeometry;
        var oxGeom = chamberResult.Turbopump?.OxPumpGeometry;

        // ── 4. Compose pump + manifold voxels and BoolAdd onto chamber ─
        // Pump mount positions — the fuel pump sits at +Y off the
        // chamber axis, ox at −Y. Both shafts aligned with +Z.
        // Chamber runs along +X with injector at origin.
        var fuelPumpOrigin = new Vector3(
            (float)(chamberResult.Contour.TotalLength_mm * 0.15),
             80f, -40f);
        var oxPumpOrigin = new Vector3(
            (float)(chamberResult.Contour.TotalLength_mm * 0.15),
            -80f, -40f);

        var bundleBounds = ComputeEngineBBox(chamberResult, fuelGeom, oxGeom);

        // Pump mount flanges sit at the pump's inlet side (z=0 in
        // local frame, i.e. the same point as OffsetImplicit's origin).
        // The flange hugs the casing OD and projects outward by
        // DefaultRadialProjection so BoolAdd fuses it with the existing
        // pump + chamber bodies.
        PumpMountFlangeGeometry? fuelFlangeGeom = null;
        PumpMountFlangeGeometry? oxFlangeGeom = null;

        if (fuelGeom is not null)
        {
            var assembly = TurbopumpGeometryGenerator.BuildImplicit(fuelGeom);
            var offset = new OffsetImplicit(assembly, fuelPumpOrigin);
            using var vox = LibraryScope.MakeVoxels(offset, bundleBounds);
            engineVoxels.BoolAdd(vox);

            if (includePumpMountFlanges)
            {
                fuelFlangeGeom = PumpMountFlange.Size(
                    fuelGeom.CasingOuterRadius_mm,
                    radialProjection_mm: design.FlangeRadialProjection_mm);
                var flange = PumpMountFlange.BuildImplicit(fuelFlangeGeom);
                var flangeOffset = new OffsetImplicit(flange, fuelPumpOrigin);
                using var fvox = LibraryScope.MakeVoxels(flangeOffset, bundleBounds);
                engineVoxels.BoolAdd(fvox);
            }
        }
        if (oxGeom is not null)
        {
            var assembly = TurbopumpGeometryGenerator.BuildImplicit(oxGeom);
            var offset = new OffsetImplicit(assembly, oxPumpOrigin);
            using var vox = LibraryScope.MakeVoxels(offset, bundleBounds);
            engineVoxels.BoolAdd(vox);

            if (includePumpMountFlanges)
            {
                oxFlangeGeom = PumpMountFlange.Size(
                    oxGeom.CasingOuterRadius_mm,
                    radialProjection_mm: design.FlangeRadialProjection_mm);
                var flange = PumpMountFlange.BuildImplicit(oxFlangeGeom);
                var flangeOffset = new OffsetImplicit(flange, oxPumpOrigin);
                using var fvox = LibraryScope.MakeVoxels(flangeOffset, bundleBounds);
                engineVoxels.BoolAdd(fvox);
            }
        }

        // Preburner voxel body (capsule) sits between the fuel-pump
        // turbine side and the chamber injector dome. Placement is
        // a soft choice — the intent is to produce a visually plausible
        // body that BoolAdds without clashing with the chamber envelope.
        PreburnerVoxelGeometry? preburnerGeom = null;
        if (includePreburnerBody && chamberResult.Preburner is { } pre)
        {
            preburnerGeom = PreburnerVoxel.Size(pre);
            var preImplicit = PreburnerVoxel.BuildImplicit(preburnerGeom);
            // Place the preburner near the fuel pump: above the pump in
            // +Y with its long axis running along +X just like the main
            // chamber. Origin shifted so the capsule clears the pump
            // casing by ≥ 15 mm.
            var preOrigin = new Vector3(
                (float)(chamberResult.Contour.TotalLength_mm * 0.30),
                (float)(fuelPumpOrigin.Y + (fuelGeom?.CasingOuterRadius_mm ?? 40) + 25),
                (float)(fuelPumpOrigin.Z + 10));
            var preOffset = new OffsetImplicit(preImplicit, preOrigin);
            using var pvox = LibraryScope.MakeVoxels(preOffset, bundleBounds);
            engineVoxels.BoolAdd(pvox);
        }

        // ── 5. Feed manifold routing ───────────────────────────────
        var injectorDome = new Vector3(0, 0, 0);
        var fuelInletPoint = fuelPumpOrigin + new Vector3(0, 0, 0);   // inducer inlet at Z=0 local
        var fuelDischarge = fuelPumpOrigin + new Vector3(
            (float)(fuelGeom?.CasingOuterRadius_mm ?? 30), 0f,
            (float)((fuelGeom?.TotalLength_mm ?? 40) * 0.6));
        var oxInletPoint = oxPumpOrigin + new Vector3(0, 0, 0);
        var oxDischarge = oxPumpOrigin + new Vector3(
            -(float)(oxGeom?.CasingOuterRadius_mm ?? 30), 0f,
            (float)((oxGeom?.TotalLength_mm ?? 40) * 0.6));
        Vector3? preburnerExhaust = chamberResult.Preburner is not null
            ? fuelPumpOrigin + new Vector3(0, 0, 120f)
            : null;
        var layout = FeedManifoldRouter.Route(
            cycle:                 cond.EngineCycle,
            injectorDomeCenter:    injectorDome,
            turbopumpFuelInlet:    fuelInletPoint,
            turbopumpFuelDischarge: fuelDischarge,
            turbopumpOxInlet:      oxInletPoint,
            turbopumpOxDischarge:  oxDischarge,
            preburnerExhaust:      preburnerExhaust,
            bendFilletRadius_mm:   bendFilletRadius_mm);

        var tubeImplicits = FeedManifoldRouter.BuildImplicits(layout);
        foreach (var tube in tubeImplicits)
        {
            using var tubeVox = LibraryScope.MakeVoxels(tube, bundleBounds);
            engineVoxels.BoolAdd(tubeVox);
        }

        // ── 5b. Body-intersection feasibility gate ─────────
        // Pure-geometry check: does any routed tube midpoint pass
        // through a non-endpoint body envelope? Emits
        // MONOLITHIC_BODY_INTERSECTION warnings but does NOT fail the
        // build — the voxel BoolAdd has already absorbed any collision
        // and the gate is advisory, consistent with the regen
        // FeasibilityGate warnings surface.
        double chamberEnvR = chamberResult.Contour.Stations
            .Max(st => st.R_mm);
        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm:  chamberEnvR,
            ChamberLength_mm:       chamberResult.Contour.TotalLength_mm,
            FuelPumpGeometry:       fuelGeom,
            FuelPumpOrigin:         fuelPumpOrigin,
            OxPumpGeometry:         oxGeom,
            OxPumpOrigin:           oxPumpOrigin,
            PreburnerGeometry:      preburnerGeom,
            PreburnerOrigin:        chamberResult.Preburner is not null
                                    ? new Vector3(
                                          (float)(chamberResult.Contour.TotalLength_mm * 0.30),
                                          (float)(fuelPumpOrigin.Y + (fuelGeom?.CasingOuterRadius_mm ?? 40) + 25),
                                          (float)(fuelPumpOrigin.Z + 10))
                                    : Vector3.Zero,
            // Include the single-stage turbine wheels (common shaft
            // with the pumps, axial extent below z=0 in the pump local
            // frame). The evaluator back-solves the world-frame z-base
            // from the pump origin.
            FuelTurbineGeometry:    chamberResult.Turbopump?.FuelTurbineGeometry,
            OxTurbineGeometry:      chamberResult.Turbopump?.OxTurbineGeometry);
        var bodyGate = MonolithicFeasibility.Evaluate(layout, envelopes);

        // ── 6. Summary + mass tally ────────────────────────────────
        int bodies = 1                                      // chamber
                   + (fuelGeom is not null ? 1 : 0)
                   + (oxGeom is not null ? 1 : 0)
                   + (fuelFlangeGeom is not null ? 1 : 0)
                   + (oxFlangeGeom is not null ? 1 : 0)
                   + (preburnerGeom is not null ? 1 : 0)
                   + layout.Tubes.Count;
        double totalMass_g = chamberResult.Geometry.TotalMass_g
                           + (fuelGeom?.EstimatedMass_g ?? 0)
                           + (oxGeom?.EstimatedMass_g ?? 0)
                           + (fuelFlangeGeom?.EstimatedMass_g ?? 0)
                           + (oxFlangeGeom?.EstimatedMass_g ?? 0)
                           + (preburnerGeom?.EstimatedMass_g ?? 0)
                           + layout.EstimatedTubeMass_g;
        double descThrust_N = descriptionSpec?.Thrust_N ?? cond.Thrust_N;
        double descPc_Pa    = descriptionSpec?.ChamberPressure_Pa ?? cond.ChamberPressure_Pa;
        double descEps      = descriptionSpec?.ExpansionRatio ?? design.ExpansionRatio;
        var descPair = descriptionSpec?.PropellantPair ?? cond.PropellantPair;
        string desc = $"Monolithic {descPair} @ {descThrust_N/1000:F1} kN, "
                    + $"Pc={descPc_Pa/1e6:F1} MPa, ε={descEps:F0}, "
                    + $"cycle={cond.EngineCycle}, {bodies} bodies fused, "
                    + $"total mass ≈ {totalMass_g/1000:F1} kg.";

        return new MonolithicEngineResult(
            EngineVoxels:             engineVoxels,
            ChamberResult:            chamberResult,
            FuelPumpGeometry:         fuelGeom,
            OxPumpGeometry:           oxGeom,
            ManifoldLayout:           layout,
            ComponentBodyCount:       bodies,
            EstimatedEngineMass_g:    totalMass_g,
            Description:              desc,
            PreburnerGeometry:        preburnerGeom,
            FuelPumpFlangeGeometry:   fuelFlangeGeom,
            OxPumpFlangeGeometry:     oxFlangeGeom,
            BodyIntersectionGate:     bodyGate);
    }

    /// <summary>
    /// Sprint 9 Track A (2026-04-22) — aerospike monolithic composition.
    /// Mirror of <see cref="Build"/> but routes the chamber body through
    /// <see cref="AerospikeBuilder.Build"/> instead of the regen voxel
    /// builder. Turbopump + preburner sizing still ride on
    /// <see cref="RegenChamberOptimization.GenerateWith"/> because those
    /// solvers are chamber-shape-agnostic — they consume thrust / Pc /
    /// MR / propellant + pump settings from <see cref="OperatingConditions"/>
    /// and the design record's pump knobs. The resulting
    /// <see cref="MonolithicEngineResult.AerospikeChamber"/> field
    /// carries the full aerospike body (contour + voxels + injector
    /// sizing + face thermal), and
    /// <see cref="MonolithicEngineResult.BodyIntersectionGate"/> runs
    /// with the plug envelope populated so routed feed tubes that clip
    /// the plug are flagged.
    /// <para>
    /// Coordinate convention: the aerospike body occupies its NATIVE
    /// frame — throat plane at world x = 0, pre-throat chamber
    /// extending to x = −ChamberLength_mm (injector face), plug
    /// extending to x = +PlugTruncatedLength_mm. Pumps + feed manifold
    /// are placed the same way as the regen path (pumps at (±80, ±80)
    /// off-axis, feed tubes routed to the injector face at
    /// x = −ChamberLength_mm).
    /// </para>
    /// </summary>
    public static MonolithicEngineResult BuildAerospike(
        Optimization.EngineSpec spec,
        double voxelSize_mm = Voxelforge.Constants.VoxelConstants.DefaultBuilderVoxelSize_mm,
        double bendFilletRadius_mm = DefaultBendFilletRadius_mm,
        bool   includePumpMountFlanges = true,
        bool   includePreburnerBody = true)
    {
        if (spec is null) throw new System.ArgumentNullException(nameof(spec));
        if (voxelSize_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(voxelSize_mm),
                "voxel size must be positive");

        // ── 1. AutoSeed, forcing aerospike topology ────────────────
        // We route through AutoSeeder rather than constructing
        // (cond, design) by hand so the same propellant / material /
        // cycle defaults apply as on the regen monolithic path.
        var forcedSpec = spec with
        {
            ChannelTopologyOverride = Optimization.ChannelTopology.Aerospike,
        };
        var seed = AutoSeeder.Seed(forcedSpec);
        return BuildAerospikeCore(
            seed.Conditions, seed.Design,
            voxelSize_mm, bendFilletRadius_mm,
            includePumpMountFlanges, includePreburnerBody,
            descriptionSpec: spec);
    }

    /// <summary>
    /// Post-AutoSeed aerospike monolithic composition. Extracted from
    /// <see cref="BuildAerospike(EngineSpec, double, double, bool, bool)"/>
    /// so <see cref="BuildFromDesign"/> can reuse the full composition
    /// pipeline without re-seeding from scalars when the user's saved
    /// design already carries the full aerospike tuning.
    /// Requires <c>design.ChannelTopology == ChannelTopology.Aerospike</c>
    /// (enforced by <see cref="AerospikeOptimization.ToSpec"/> downstream).
    /// </summary>
    private static MonolithicEngineResult BuildAerospikeCore(
        OperatingConditions cond,
        RegenChamberDesign design,
        double voxelSize_mm,
        double bendFilletRadius_mm,
        bool   includePumpMountFlanges,
        bool   includePreburnerBody,
        Optimization.EngineSpec? descriptionSpec)
    {
        cond = cond with { IncludeTurbopumpGeometry = true };

        // ── 2. Aerospike body (voxelised) ──────────────────────────
        // AerospikeOptimization.ToSpec maps (cond, design) to the
        // AerospikeSpec shape; Build produces the full voxel body
        // (chamber + annular throat ring + plug). InjectorSizing +
        // InjectorFace are populated if the seeded design carries a
        // pattern; else null (no element-clearance gate fires).
        var aeroSpec = Optimization.AerospikeOptimization.ToSpec(cond, design);
        var aeroResult = AerospikeBuilder.Build(aeroSpec, voxelSize_mm);
        if (aeroResult.Voxels is null)
            throw new System.InvalidOperationException(
                "AerospikeBuilder.Build returned a null Voxels — the monolithic "
              + "aerospike composer needs the voxel path, not BuildPhysicsOnly.");
        var engineVoxels = aeroResult.Voxels!.AsPicoGK();
        double chamberLength_mm = aeroResult.ChamberLength_mm;

        // ── 3. Turbopump + preburner sizing (physics-only sidecar) ─
        // GenerateWith runs every physics solver but skips voxel
        // generation for the regen chamber. Turbopump + preburner
        // sizing are shape-agnostic (they consume cond + design) so
        // their results are valid on the aerospike path; the regen
        // thermal / geometry fields on the returned struct are
        // ignored.
        //
        // P7 (2026-04-29): pass the already-computed `aeroResult` as
        // `cachedAerospikeResult` so GenerateWith short-circuits its
        // own AerospikeOptimization.BuildAndEvaluate pass — saves
        // ~50-200 ms duplicate aerospike-physics work on every
        // monolithic export.
        var physicsSidecar = RegenChamberOptimization.GenerateWith(
            cond, design, skipVoxelGeometry: true,
            cachedAerospikeResult: aeroResult);
        var fuelGeom = physicsSidecar.Turbopump?.FuelPumpGeometry;
        var oxGeom   = physicsSidecar.Turbopump?.OxPumpGeometry;

        // ── 4. Pump / flange voxels ────────────────────────────────
        // Same layout rule as the regen monolithic path so a reader
        // comparing the two STLs sees the same turbopump positioning.
        // The chamber "reference length" used for pump-X placement
        // is the aerospike's pre-throat chamber length (injector face
        // at x = −chamberLength_mm; pumps sit 15% along the chamber
        // from the injector face, matching the regen convention's
        // 0.15 × TotalLength factor).
        double chamberRefLen_mm = chamberLength_mm;
        var fuelPumpOrigin = new Vector3(
            (float)(-chamberRefLen_mm + chamberRefLen_mm * 0.15),
             80f, -40f);
        var oxPumpOrigin = new Vector3(
            (float)(-chamberRefLen_mm + chamberRefLen_mm * 0.15),
            -80f, -40f);

        // Bounding box covers the aerospike body (which extends
        // negative X back to the injector face and positive X past
        // the plug base) plus the pump envelopes.
        var bundleBounds = ComputeEngineBBoxAerospike(aeroResult, fuelGeom, oxGeom);

        PumpMountFlangeGeometry? fuelFlangeGeom = null;
        PumpMountFlangeGeometry? oxFlangeGeom = null;

        if (fuelGeom is not null)
        {
            var assembly = Turbopump.TurbopumpGeometryGenerator.BuildImplicit(fuelGeom);
            var offset = new OffsetImplicit(assembly, fuelPumpOrigin);
            using var vox = LibraryScope.MakeVoxels(offset, bundleBounds);
            engineVoxels.BoolAdd(vox);

            if (includePumpMountFlanges)
            {
                fuelFlangeGeom = Turbopump.PumpMountFlange.Size(
                    fuelGeom.CasingOuterRadius_mm,
                    radialProjection_mm: design.FlangeRadialProjection_mm);
                var flange = Turbopump.PumpMountFlange.BuildImplicit(fuelFlangeGeom);
                var flangeOffset = new OffsetImplicit(flange, fuelPumpOrigin);
                using var fvox = LibraryScope.MakeVoxels(flangeOffset, bundleBounds);
                engineVoxels.BoolAdd(fvox);
            }
        }
        if (oxGeom is not null)
        {
            var assembly = Turbopump.TurbopumpGeometryGenerator.BuildImplicit(oxGeom);
            var offset = new OffsetImplicit(assembly, oxPumpOrigin);
            using var vox = LibraryScope.MakeVoxels(offset, bundleBounds);
            engineVoxels.BoolAdd(vox);

            if (includePumpMountFlanges)
            {
                oxFlangeGeom = Turbopump.PumpMountFlange.Size(
                    oxGeom.CasingOuterRadius_mm,
                    radialProjection_mm: design.FlangeRadialProjection_mm);
                var flange = Turbopump.PumpMountFlange.BuildImplicit(oxFlangeGeom);
                var flangeOffset = new OffsetImplicit(flange, oxPumpOrigin);
                using var fvox = LibraryScope.MakeVoxels(flangeOffset, bundleBounds);
                engineVoxels.BoolAdd(fvox);
            }
        }

        // ── 5. Preburner body (same placement rule as regen) ───────
        Chamber.PreburnerVoxelGeometry? preburnerGeom = null;
        if (includePreburnerBody && physicsSidecar.Preburner is { } pre)
        {
            preburnerGeom = Chamber.PreburnerVoxel.Size(pre);
            var preImplicit = Chamber.PreburnerVoxel.BuildImplicit(preburnerGeom);
            var preOrigin = new Vector3(
                (float)(-chamberRefLen_mm + chamberRefLen_mm * 0.30),
                (float)(fuelPumpOrigin.Y + (fuelGeom?.CasingOuterRadius_mm ?? 40) + 25),
                (float)(fuelPumpOrigin.Z + 10));
            var preOffset = new OffsetImplicit(preImplicit, preOrigin);
            using var pvox = LibraryScope.MakeVoxels(preOffset, bundleBounds);
            engineVoxels.BoolAdd(pvox);
        }

        // ── 6. Feed manifold routing ───────────────────────────────
        // Injector dome sits at the injector face (world x = −chamberLength_mm).
        var injectorDome = new Vector3(-(float)chamberLength_mm, 0, 0);
        var fuelInletPoint  = fuelPumpOrigin + new Vector3(0, 0, 0);
        var fuelDischarge   = fuelPumpOrigin + new Vector3(
            (float)(fuelGeom?.CasingOuterRadius_mm ?? 30), 0f,
            (float)((fuelGeom?.TotalLength_mm ?? 40) * 0.6));
        var oxInletPoint    = oxPumpOrigin + new Vector3(0, 0, 0);
        var oxDischarge     = oxPumpOrigin + new Vector3(
            -(float)(oxGeom?.CasingOuterRadius_mm ?? 30), 0f,
            (float)((oxGeom?.TotalLength_mm ?? 40) * 0.6));
        Vector3? preburnerExhaust = physicsSidecar.Preburner is not null
            ? fuelPumpOrigin + new Vector3(0, 0, 120f)
            : null;

        var layout = FeedManifoldRouter.Route(
            cycle:                 cond.EngineCycle,
            injectorDomeCenter:    injectorDome,
            turbopumpFuelInlet:    fuelInletPoint,
            turbopumpFuelDischarge: fuelDischarge,
            turbopumpOxInlet:      oxInletPoint,
            turbopumpOxDischarge:  oxDischarge,
            preburnerExhaust:      preburnerExhaust,
            bendFilletRadius_mm:   bendFilletRadius_mm);

        var tubeImplicits = FeedManifoldRouter.BuildImplicits(layout);
        foreach (var tube in tubeImplicits)
        {
            using var tubeVox = LibraryScope.MakeVoxels(tube, bundleBounds);
            engineVoxels.BoolAdd(tubeVox);
        }

        // ── 7. Body-intersection feasibility with plug envelope ────
        // Sprint 7 Track B's AerospikePlug envelope + station-
        // interpolated radius flags any routed tube that clips the
        // plug body. The chamber-outer-radius field here reflects the
        // pre-throat barrel radius + shell; aerospike doesn't have a
        // bell nozzle so no regen-style chamber envelope, but the
        // plug-envelope check handles the downstream side.
        var envelopes = new MonolithicBodyEnvelopes(
            ChamberOuterRadius_mm:  aeroResult.ChamberRadius_mm + aeroSpec.OuterShellThickness_mm,
            ChamberLength_mm:       chamberLength_mm,
            FuelPumpGeometry:       fuelGeom,
            FuelPumpOrigin:         fuelPumpOrigin,
            OxPumpGeometry:         oxGeom,
            OxPumpOrigin:           oxPumpOrigin,
            PreburnerGeometry:      preburnerGeom,
            PreburnerOrigin:        physicsSidecar.Preburner is not null
                                    ? new Vector3(
                                          (float)(-chamberRefLen_mm + chamberRefLen_mm * 0.30),
                                          (float)(fuelPumpOrigin.Y + (fuelGeom?.CasingOuterRadius_mm ?? 40) + 25),
                                          (float)(fuelPumpOrigin.Z + 10))
                                    : Vector3.Zero,
            FuelTurbineGeometry:    physicsSidecar.Turbopump?.FuelTurbineGeometry,
            OxTurbineGeometry:      physicsSidecar.Turbopump?.OxTurbineGeometry,
            AerospikePlug:          aeroResult,
            AerospikePlugOrigin:    Vector3.Zero);
        var bodyGate = MonolithicFeasibility.Evaluate(layout, envelopes);

        // ── 8. Summary + mass tally ────────────────────────────────
        int bodies = 1   // aerospike body (chamber + throat ring + plug as single voxel body)
                   + (fuelGeom is not null ? 1 : 0)
                   + (oxGeom is not null ? 1 : 0)
                   + (fuelFlangeGeom is not null ? 1 : 0)
                   + (oxFlangeGeom is not null ? 1 : 0)
                   + (preburnerGeom is not null ? 1 : 0)
                   + layout.Tubes.Count;
        double totalMass_g = aeroResult.EstimatedMass_g
                           + (fuelGeom?.EstimatedMass_g ?? 0)
                           + (oxGeom?.EstimatedMass_g ?? 0)
                           + (fuelFlangeGeom?.EstimatedMass_g ?? 0)
                           + (oxFlangeGeom?.EstimatedMass_g ?? 0)
                           + (preburnerGeom?.EstimatedMass_g ?? 0)
                           + layout.EstimatedTubeMass_g;
        double descThrust_N = descriptionSpec?.Thrust_N ?? cond.Thrust_N;
        double descPc_Pa    = descriptionSpec?.ChamberPressure_Pa ?? cond.ChamberPressure_Pa;
        double descEps      = descriptionSpec?.ExpansionRatio ?? design.ExpansionRatio;
        var descPair        = descriptionSpec?.PropellantPair ?? cond.PropellantPair;
        string desc = $"Monolithic aerospike {descPair} @ {descThrust_N/1000:F1} kN, "
                    + $"Pc={descPc_Pa/1e6:F1} MPa, ε={descEps:F0}, "
                    + $"plug={aeroResult.Contour.PlugLengthRatio:F2}, "
                    + $"cycle={cond.EngineCycle}, {bodies} bodies fused, "
                    + $"total mass ≈ {totalMass_g/1000:F1} kg.";

        return new MonolithicEngineResult(
            EngineVoxels:           engineVoxels,
            ChamberResult:          physicsSidecar,
            FuelPumpGeometry:       fuelGeom,
            OxPumpGeometry:         oxGeom,
            ManifoldLayout:         layout,
            ComponentBodyCount:     bodies,
            EstimatedEngineMass_g:  totalMass_g,
            Description:            desc,
            PreburnerGeometry:      preburnerGeom,
            FuelPumpFlangeGeometry: fuelFlangeGeom,
            OxPumpFlangeGeometry:   oxFlangeGeom,
            BodyIntersectionGate:   bodyGate,
            AerospikeChamber:       aeroResult);
    }

    /// <summary>
    /// Sprint 9 Track A bbox — mirrors <see cref="ComputeEngineBBox"/>
    /// but takes an <see cref="AerospikeBuildResult"/> instead of a
    /// <see cref="RegenGenerationResult"/>. Covers −ChamberLength ≤ x
    /// ≤ PlugTruncatedLength for the aerospike body; pump extents are
    /// identical to the regen path.
    /// </summary>
    private static BBox3 ComputeEngineBBoxAerospike(
        AerospikeBuildResult aero,
        TurbopumpGeometry? fuelGeom,
        TurbopumpGeometry? oxGeom)
    {
        float xMin = -(float)aero.ChamberLength_mm - 50f;
        float xMax =  (float)aero.PlugTruncatedLength_mm + 30f;
        float yMin = -200, yMax = 200;
        float zMin = -300, zMax = 300;

        if (fuelGeom is not null)
        {
            yMax = MathF.Max(yMax, 80f + (float)fuelGeom.CasingOuterRadius_mm + 10);
            zMax = MathF.Max(zMax, -40f + (float)fuelGeom.TotalLength_mm + 10);
        }
        if (oxGeom is not null)
        {
            yMin = MathF.Min(yMin, -80f - (float)oxGeom.CasingOuterRadius_mm - 10);
            zMax = MathF.Max(zMax, -40f + (float)oxGeom.TotalLength_mm + 10);
        }
        return new BBox3(
            new Vector3(xMin, yMin, zMin),
            new Vector3(xMax, yMax, zMax));
    }

    /// <summary>
    /// Compute an axis-aligned bounding box that encloses the chamber
    /// + both turbopump envelopes + feed-line extents + tank-interface
    /// stubs (which sit 200 mm upstream of pump inlets).
    /// </summary>
    private static BBox3 ComputeEngineBBox(
        RegenGenerationResult chamber,
        TurbopumpGeometry? fuelGeom,
        TurbopumpGeometry? oxGeom)
    {
        float xMin = -50, xMax = (float)(chamber.Contour.TotalLength_mm + 30);
        float yMin = -200, yMax = 200;
        float zMin = -300, zMax = 300;

        if (fuelGeom is not null)
        {
            xMax = MathF.Max(xMax, (float)(chamber.Contour.TotalLength_mm * 0.15 + fuelGeom.CasingOuterRadius_mm + 50));
            yMax = MathF.Max(yMax, 80f + (float)fuelGeom.CasingOuterRadius_mm + 10);
            zMax = MathF.Max(zMax, -40f + (float)fuelGeom.TotalLength_mm + 10);
        }
        if (oxGeom is not null)
        {
            yMin = MathF.Min(yMin, -80f - (float)oxGeom.CasingOuterRadius_mm - 10);
            zMax = MathF.Max(zMax, -40f + (float)oxGeom.TotalLength_mm + 10);
        }
        return new BBox3(
            new Vector3(xMin, yMin, zMin),
            new Vector3(xMax, yMax, zMax));
    }
}

/// <summary>
/// Helper that translates an underlying <see cref="IImplicit"/> by a
/// fixed offset. Used to mount pump assemblies away from the
/// world-origin (chamber injector dome).
/// </summary>
internal sealed class OffsetImplicit : IImplicit
{
    private readonly IImplicit _inner;
    private readonly Vector3 _offset;

    public OffsetImplicit(IImplicit inner, Vector3 offset)
    {
        _inner = inner;
        _offset = offset;
    }

    public float fSignedDistance(in Vector3 p)
    {
        var translated = p - _offset;
        return _inner.fSignedDistance(in translated);
    }
}
