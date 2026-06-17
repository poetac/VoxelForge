// AerospikeBuilder.cs — Stand-alone aerospike engine voxel builder.
//
// Pipeline
// ────────
//   1. Derive throat geometry from the input operating-conditions +
//      design (throat area from thrust / Pc / Isp as usual, then split
//      between inner plug nose and outer throat lip).
//   2. Generate the AerospikeContour via Angelino's parametric method.
//   3. Build the combustion-chamber outer wall (pre-throat) as a
//      simple RevolvedContourImplicit sized to the design's
//      contraction ratio.
//   4. Build the annular throat ring + plug body as separate IImplicits.
//   5. Compose via AerospikeAssemblyImplicit.
//   6. Voxelise once, return a single-piece Voxels + geometry metadata.
//
// What this originally did NOT do (now mostly addressed)
// ──────────────────────────────────────────────────────
//   • Cooling channels anywhere. The plug interior was solid; the
//     combustion chamber had no regen jacket. Plug-channel cooling
//     has since been added via the opt-in regen-cooling fields below.
//   • Injector-face integration. The aerospike shell dispatches the
//     injector as a post-step against the combustion-chamber face;
//     pattern sizing + clearance gating shipped in a later sprint.
//   • SA-driven optimization. The plug-length ratio + expansion ratio
//     + chamber-pressure are now SA variables.
//   • Feasibility gate. The aerospike path has its own parallel gate
//     evaluator (AerospikeFeasibility); LPBF + plug-wall + coolant
//     cavitation gates are covered.
//
// Consumer path (CLI)
// ───────────────────
//   var spec = new AerospikeSpec(
//       Thrust_N: 20000, ChamberPressure_Pa: 7e6,
//       ExpansionRatio: 15.0, PlugLengthRatio: 0.30,
//       PropellantPair: PropellantPair.LOX_CH4);
//   var result = AerospikeBuilder.Build(spec, voxelSize_mm: 0.4);
//   // result.Voxels is ready to mesh + export to STL / 3MF.

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.HeatTransfer;

namespace Voxelforge.Geometry;

/// <summary>
/// Four-input aerospike design specification. Mirrors the regen-side
/// <see cref="Optimization.EngineSpec"/> signature so future CLI paths
/// can share parsing.
///
/// Optional regen-cooling fields. When
/// <paramref name="IncludeRegenChannels"/> is false (default) the
/// builder runs the geometry-only pipeline bit-identically. When true,
/// axial channels are cut along the plug's external surface and the
/// <see cref="HeatTransfer.AerospikePlugCooling"/> solver is invoked,
/// producing an <see cref="AerospikeThermalResult"/> on the build
/// result so the plug-wall-temp feasibility gate can fire.
/// </summary>
// AerospikeSpec record was extracted to Voxelforge.Core/Geometry/
// AerospikeSpec.cs (Sprint A-3 Phase 2 / ADR-021, 2026-04-30) so the
// orchestrator (RegenChamberOptimization, moving to Core in Phase 2)
// can reference the spec without dragging the Voxels project + PicoGK
// into Core.

/// <summary>
/// Per-station thermal result for the plug regen solver, plus summary
/// scalars consumed by the feasibility gate and CLI report. Null on
/// the geometry-only path.
/// <para>
/// Cavitation-risk telemetry (<see cref="CavitationRiskStationCount"/>,
/// <see cref="MinCoolantPressure_Pa"/>) lets
/// <see cref="AerospikeFeasibility.Evaluate"/> surface the 0.1 MPa
/// pressure clamp in <see cref="HeatTransfer.AerospikePlugCooling.Solve"/>
/// as a feasibility gate rather than a silent warning. Defaults
/// preserve legacy call-site behaviour bit-identically.
/// </para>
/// </summary>
/// <summary>
/// Sized injector pattern + face-placement geometry on the aerospike
/// pre-throat combustion chamber
/// face, produced by <see cref="AerospikeBuilder.BuildPhysicsOnly"/>
/// when <see cref="AerospikeSpec.InjectorPattern"/> is non-null.
/// Consumed by <see cref="AerospikeFeasibility.Evaluate"/> to fire the
/// <c>AEROSPIKE_ELEMENT_CLEARANCE</c> gate.
/// <para>
/// Face-placement model: a flat annular face at the chamber inlet with
/// outer radius = <see cref="AerospikeBuildResult.ChamberRadius_mm"/>.
/// Elements sit on a single pitch circle (matching the regen convention)
/// at radius <see cref="PitchCircleRadius_mm"/> = 0.60 × chamber radius
/// unless the pattern supplies a non-zero override. Arc spacing is the
/// circumferential distance between adjacent element centres:
/// <c>ArcSpacing_mm = 2π · R_pitch / ElementCount</c>. Minimum clearance
/// is the larger of the ox / fuel equivalent bore diameters multiplied
/// by a housing factor (1.8 — covers injector-body walls + manifold
/// interface) plus the LPBF 2 mm floor. If arc spacing drops below the
/// clearance threshold, <see cref="ClearanceOk"/> flips false and the
/// feasibility gate fires.
/// </para>
/// </summary>
// AerospikeInjectorSizing, AerospikeThermalResult, and AerospikeBuildResult records were extracted
// to Voxelforge.Core/Geometry/ as part of A1. AerospikeBuildResult.Voxels
// is typed `object?` in Core; cast back via `(PicoGK.Voxels?)result.Voxels`
// where the voxel body is needed.

/// <summary>
/// Aerospike engine voxel builder. All methods are static; thread-safe
/// up to PicoGK's per-process Library constraint (voxel ops must run
/// on the task thread, same contract as <see cref="ChamberVoxelBuilder"/>).
/// </summary>
public static class AerospikeBuilder
{
    /// <summary>
    /// Sprint 2a (2026-04-22): physics-only build — everything
    /// <see cref="Build"/> does except the PicoGK voxelization. Returns an
    /// <see cref="AerospikeBuildResult"/> with <see cref="AerospikeBuildResult.Voxels"/>
    /// set to null; all other fields (contour, derived radii, volume,
    /// mass, thermal when <see cref="AerospikeSpec.IncludeRegenChannels"/>
    /// is true) are populated identically.
    /// <para>
    /// Why this exists: <c>new PicoGK.Library(vox)</c> + voxelize crashes
    /// the xUnit test host on dispose (ADR-005). The physics-only entry
    /// point unblocks the SA optimizer + feasibility-gate evaluation +
    /// scoring from running on aerospike designs inside xUnit — prereq
    /// for Sprint 2b (GenerateWith dispatch + aerospike scoring).
    /// </para>
    /// <para>
    /// <see cref="Build"/> calls this method first, then voxelizes on top
    /// of the returned result so the two entry points can never drift.
    /// </para>
    /// </summary>
    public static AerospikeBuildResult BuildPhysicsOnly(AerospikeSpec spec)
    {
        // ── 1. Propellant lookup → throat-area derivation ──────────
        var gas = PropellantTables.Lookup(
            spec.PropellantPair, spec.MixtureRatio, spec.ChamberPressure_Pa);
        // A_t from F = C_F × P_c × A_t, C_F ≈ 1.4 for aerospike-ish design
        // (sea-level equivalent). Approximate — the full C_F(altitude)
        // sweep is downstream of geometry.
        const double cf_approx = 1.4;
        double throatArea_m2 = spec.Thrust_N / (cf_approx * spec.ChamberPressure_Pa);
        double throatArea_mm2 = throatArea_m2 * 1e6;
        // Annular throat: A_t = π(R_o² − R_i²). Take R_i = 0.40·R_o
        // (matches AerospikeContourGenerator convention).
        double rOuter_mm = System.Math.Sqrt(throatArea_mm2 / (System.Math.PI * (1 - 0.16)));
        double rInner_mm = 0.40 * rOuter_mm;

        // ── 2. Aerospike contour ───────────────────────────────────
        var contour = AerospikeContourGenerator.Generate(
            throatOuterRadius_mm: rOuter_mm,
            expansionRatio:       spec.ExpansionRatio,
            plugLengthRatio:      spec.PlugLengthRatio,
            gamma:                gas.Gamma,
            stationCount:         80,
            includeCowl:          true);

        // ── 3. Combustion chamber sizing ───────────────────────────
        // Simple cylindrical barrel: R_chamber = √(ε_c) × R_o with
        // ε_c from the spec (Sprint 9 Track C, default 6.0); length =
        // ChamberLengthRatio × R_chamber.
        double contractionRatio = spec.ChamberContractionRatio;
        double rChamber_mm = System.Math.Sqrt(contractionRatio) * rOuter_mm;
        double chamberLength_mm = spec.ChamberLengthRatio * rChamber_mm;

        // ── 4. Analytical volume + mass (GRCop-42 density default) ─
        // Chamber shell volume (thin cylindrical annulus of thickness t)
        // + plug volume (conical frustum with flat base).
        double shellVol_mm3 = 2.0 * System.Math.PI * rChamber_mm
                            * spec.OuterShellThickness_mm * chamberLength_mm;
        double plugVol_mm3 = ConeFrustumVolume_mm3(
            rOuter_mm, contour.PlugBaseRadius_mm, contour.PlugTruncatedLength_mm);
        double solidVol_mm3 = shellVol_mm3 + plugVol_mm3;
        const double density_gPerCm3 = 8.9;   // GRCop-42 nominal
        double mass_g = solidVol_mm3 * 1e-3 * density_gPerCm3;

        // ── 5. Phase 2 thermal solve (optional) ────────────────────
        AerospikeThermalResult? thermal = null;
        if (spec.IncludeRegenChannels)
        {
            var pairMeta = Combustion.PropellantPairs.GetMeta(spec.PropellantPair);
            var fluid = Coolant.CoolantRegistry.Get(pairMeta.CoolantFluidKey);
            var material = HeatTransfer.WallMaterials.All[System.Math.Clamp(
                spec.WallMaterialIndex, 0, HeatTransfer.WallMaterials.All.Length - 1)];

            // Coolant mass flow: approximate as fuel-side mass flow from
            // C*-derived total mass flow × (1 / (1 + MR)).
            double totalMdot = spec.Thrust_N / System.Math.Max(gas.CStar_ms * spec.CStarEfficiency * cf_approx, 1e-6);
            double fuelMdot = totalMdot / (1.0 + spec.MixtureRatio);

            var coolingInputs = new AerospikePlugCoolingInputs(
                Contour:                contour,
                Gas:                    gas,
                Wall:                   material,
                ChannelCount:           spec.PlugChannelCount,
                ChannelWidth_mm:        spec.PlugChannelWidth_mm,
                ChannelDepth_mm:        spec.PlugChannelDepth_mm,
                PlugWallThickness_mm:   spec.PlugWallThickness_mm,
                CoolantMassFlow_kgs:    fuelMdot,
                CoolantInletTemp_K:     spec.CoolantInletTemp_K,
                CoolantInletPressure_Pa: spec.CoolantInletPressure_Pa,
                CoolantFluid:           fluid);
            thermal = HeatTransfer.AerospikePlugCooling.Solve(coolingInputs);
        }

        // ── 6. Sprint 7 Track A: injector sizing + face-placement ──
        // When the spec carries an InjectorPattern, size it against the
        // derived mass flows and place elements on the pre-throat
        // combustion-chamber face. The pre-throat chamber is a flat
        // annular face at chamber inlet; elements sit on a pitch circle
        // at 60 % of the chamber radius (matches the regen convention;
        // falls back to the pattern's own override when non-zero). The
        // AEROSPIKE_ELEMENT_CLEARANCE gate fires in
        // AerospikeFeasibility.Evaluate when arc spacing drops below
        // the LPBF minimum.
        AerospikeInjectorSizing? injectorSizing = null;
        if (spec.InjectorPattern is { } pattern)
        {
            // Derive mass flows from the thrust + C* + MR triple. For
            // propellant-pair densities we use the injector-side
            // injection densities (tank-T cryogens + stored propellants).
            var (oxDensity, fuelDensity) =
                Injector.OrificeModel.InjectionDensities(spec.PropellantPair);
            double totalMdot = spec.Thrust_N
                             / System.Math.Max(gas.CStar_ms * spec.CStarEfficiency * cf_approx, 1e-6);
            double fuelFlow = totalMdot / (1.0 + spec.MixtureRatio);
            double oxFlow   = totalMdot - fuelFlow;

            double deltaPInj_Pa = pattern.DeltaPInjFraction * spec.ChamberPressure_Pa;

            var patternSizing = pattern.SizePattern(
                totalOxFlow_kgs:   oxFlow,
                totalFuelFlow_kgs: fuelFlow,
                deltaPInj_Pa:      deltaPInj_Pa,
                oxDensity_kgm3:    oxDensity,
                fuelDensity_kgm3:  fuelDensity);

            // Pitch circle: pattern override wins when non-zero; else
            // default to 0.60 × R_chamber (mirrors Chamber.
            // ChamberVoxelBuilder's default so aerospike + regen agree).
            double pitchR_mm = pattern.PitchCircleRadius_mm > 0
                ? pattern.PitchCircleRadius_mm
                : 0.60 * rChamber_mm;

            // Arc spacing between adjacent element centres, in mm.
            int elementCount = System.Math.Max(1, patternSizing.ElementCount);
            double arcSpacing_mm = 2.0 * System.Math.PI * pitchR_mm / elementCount;

            // Element OD estimate. Each element houses one ox + one fuel
            // orifice (or the coaxial equivalent); the body wraps both.
            // 1.8× scales the larger equivalent bore to a plausible
            // housing OD (SpaceX / NASA merlin-class drawings run ~1.6–
            // 2.0× for the injector-face side of the element).
            const double HousingFactor = 1.8;
            double oxOD = patternSizing.PerElementResult.OxEquivDiameter_mm;
            double fuelOD = patternSizing.PerElementResult.FuelEquivDiameter_mm;
            double elementOD_mm = HousingFactor * System.Math.Max(oxOD, fuelOD);

            // Clearance floor = element OD + 2 mm LPBF feature floor.
            const double LpbfFloor_mm = 2.0;
            double minClearance_mm = elementOD_mm + LpbfFloor_mm;
            bool clearanceOk = arcSpacing_mm >= minClearance_mm;

            injectorSizing = new AerospikeInjectorSizing(
                PatternSizing:           patternSizing,
                PitchCircleRadius_mm:    pitchR_mm,
                ArcSpacing_mm:           arcSpacing_mm,
                ElementOuterDiameter_mm: elementOD_mm,
                MinClearance_mm:         minClearance_mm,
                ClearanceOk:             clearanceOk);
        }

        // ── 7. Sprint 8 Track A: injector-face thermal ────────────
        // Runs only when the sizing produced a pattern-bore-area to
        // back-cool the face. The estimator returns null when there's
        // no InjectorSizing — so this chains cleanly.
        HeatTransfer.AerospikeInjectorFaceResult? injectorFace = null;
        if (injectorSizing is not null)
        {
            // Temporarily stitch a preliminary AerospikeBuildResult
            // together so the estimator has access to contour + sizing
            // fields. Only the fields the estimator reads matter here.
            var preliminary = new AerospikeBuildResult(
                Voxels:                null,
                Contour:               contour,
                ThroatOuterRadius_mm:  rOuter_mm,
                ThroatInnerRadius_mm:  rInner_mm,
                PlugTruncatedLength_mm: contour.PlugTruncatedLength_mm,
                ChamberRadius_mm:      rChamber_mm,
                ChamberLength_mm:      chamberLength_mm,
                TotalLength_mm:        chamberLength_mm + contour.PlugTruncatedLength_mm,
                TotalDiameter_mm:      2.0 * (rChamber_mm + spec.OuterShellThickness_mm),
                SolidVolume_mm3:       solidVol_mm3,
                EstimatedMass_g:       mass_g,
                Description:           "",
                Thermal:               thermal,
                InjectorSizing:        injectorSizing);
            injectorFace = HeatTransfer.AerospikeInjectorFaceThermal.Estimate(
                build:                   preliminary,
                gas:                     gas,
                coolantInletTemp_K:      spec.CoolantInletTemp_K,
                coolantInletPressure_Pa: spec.CoolantInletPressure_Pa,
                propellantPair:          spec.PropellantPair,
                mixtureRatio:            spec.MixtureRatio,
                // PH-36 + PH-35 aerospike-face follow-ons (2026-04-29 —
                // closes #233 + #234). Forward the new spec fields for
                // per-pair oxidizer T + face material T-limit override.
                oxidizerInletTemp_K:             spec.OxidizerInletTemp_K,
                injectorFaceMaxTemp_K_Override:  spec.InjectorFaceMaxTemp_K_Override);
        }

        string desc = $"Aerospike @ {spec.Thrust_N / 1000:F1} kN, Pc={spec.ChamberPressure_Pa / 1e6:F1} MPa, "
                    + $"ε={spec.ExpansionRatio:F0}, plug={spec.PlugLengthRatio:F2} "
                    + $"({spec.PropellantPair}, γ={gas.Gamma:F3})"
                    + (spec.IncludeRegenChannels
                        ? $", regen {spec.PlugChannelCount}×({spec.PlugChannelWidth_mm:F1}×{spec.PlugChannelDepth_mm:F1} mm)"
                        : "")
                    + (injectorSizing is { } inj
                        ? $", injector {inj.PatternSizing.ElementCount}×({inj.ElementOuterDiameter_mm:F1} mm) on R={inj.PitchCircleRadius_mm:F1} mm"
                        : "");

        return new AerospikeBuildResult(
            Voxels:                  null,
            Contour:                 contour,
            ThroatOuterRadius_mm:    rOuter_mm,
            ThroatInnerRadius_mm:    rInner_mm,
            PlugTruncatedLength_mm:  contour.PlugTruncatedLength_mm,
            ChamberRadius_mm:        rChamber_mm,
            ChamberLength_mm:        chamberLength_mm,
            TotalLength_mm:          chamberLength_mm + contour.PlugTruncatedLength_mm,
            TotalDiameter_mm:        2.0 * (rChamber_mm + spec.OuterShellThickness_mm),
            SolidVolume_mm3:         solidVol_mm3,
            EstimatedMass_g:         mass_g,
            Description:             desc,
            Thermal:                 thermal,
            InjectorSizing:          injectorSizing,
            InjectorFace:            injectorFace);
    }

    /// <summary>
    /// End-to-end build from an <see cref="AerospikeSpec"/> to a
    /// voxelised engine body + metadata. Runs <see cref="BuildPhysicsOnly"/>
    /// for the physics / contour / thermal pass, then voxelizes the
    /// assembly against the physics-derived bounding box. Use
    /// <see cref="BuildPhysicsOnly"/> directly if you do not need the
    /// <see cref="Voxels"/> body (SA scoring, feasibility-gate checks,
    /// xUnit tests) — that path is safe to call without an active
    /// PicoGK <see cref="Library"/>.
    /// </summary>
    public static AerospikeBuildResult Build(AerospikeSpec spec, double voxelSize_mm = Voxelforge.Constants.VoxelConstants.DefaultBuilderVoxelSize_mm)
    {
        if (voxelSize_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(voxelSize_mm),
                "voxel size must be positive");

        // Physics / contour / thermal — identical to BuildPhysicsOnly.
        var physics = BuildPhysicsOnly(spec);
        var contour = physics.Contour;
        double rOuter_mm       = physics.ThroatOuterRadius_mm;
        double rChamber_mm     = physics.ChamberRadius_mm;
        double chamberLength_mm = physics.ChamberLength_mm;

        // ── Compose IImplicits ─────────────────────────────────────
        // Sprint fix (2026-04-25 layer 6): chamber is now a UNIFORM
        // CYLINDER from x = −L_c to x = 0. Pre-fix layers 1-4 had the
        // chamber tapering down to the throat radius via a converging-
        // section funnel — that funnel rendered as a deep recess in the
        // chamber's downstream face, making the throat ring + plug
        // appear "disconnected" at the bottom of the recess. Real
        // aerospikes have no converging section: the chamber is a
        // uniform-radius tube, and the annular throat lives in the gap
        // between the chamber wall (at rChamber) and the plug (at the
        // plug surface). The throat ring spans the FULL bottom of the
        // chamber from rOuter (annular-throat outer edge) to
        // rChamber+shell (chamber outer), forming the throat plate.
        var chamberContour = new RevolvedContourImplicit(new[]
        {
            (-chamberLength_mm, rChamber_mm + spec.OuterShellThickness_mm),
            (0.0,               rChamber_mm + spec.OuterShellThickness_mm),
        });
        // Sprint fix (2026-04-25 layer 5): start the chamber inner contour
        // at x = (-chamberLength + injectorCapThickness) instead of -chamberLength.
        // Effect: for x ∈ [-chamberLength, -chamberLength + injectorCapThickness]
        // the chamber-inner SDF returns +ve (axial gap, outside its x range)
        // so the chamber-shell SDF is `max(d_outer, -d_inner)` = d_outer
        // (since -d_inner < 0). That region of the chamber body is SOLID
        // (the injector face cap closes the upstream end). For x in
        // [(-chamberLength + injectorCapThickness), 0] the inner contour
        // returns -ve inside the cavity, so the chamber is HOLLOW with
        // OuterShellThickness wall.
        //
        // Pre-fix the chamber's upstream end was open (we don't model an
        // injector face) so when the user looked into the open mouth they
        // saw the throat ring + plug "floating" inside an open tube.
        // Adding the cap closes the upstream end so the chamber is a
        // proper printable combustion shell.
        double injectorCapThickness_mm = System.Math.Max(
            spec.OuterShellThickness_mm * 2.0,
            chamberLength_mm * 0.05);
        var chamberInnerContour = new RevolvedContourImplicit(new[]
        {
            (-chamberLength_mm + injectorCapThickness_mm, rChamber_mm),
            (0.0,                                          rChamber_mm),
        });
        float chamberXMin_mm = (float)(-chamberLength_mm);
        float chamberXMax_mm = 0f;
        // Sprint fix (2026-04-25 layer 6+7+10): throat ring spans the
        // FULL chamber-bottom annular plate from rOuter (annular-throat
        // outer edge / cowl inner) to rChamber+shell (chamber outer
        // wall). The ring axially thickens to max(cowl, 2 × shell, 2 mm)
        // so the bottom plate reads as a substantial structure but is
        // always at most half the truncated plug length so the plug
        // sticks out visibly past the plate. Pre-layer-10 the plate
        // could be thicker than the truncated plug for small engines
        // with low PlugLengthRatio, hiding the plug entirely inside
        // the plate band. The cap keeps the plug visible at every
        // PlugLengthRatio ∈ [0.15, 1.0].
        double throatPlateMaxFromPlug_mm = System.Math.Max(
            contour.PlugTruncatedLength_mm * 0.5,
            spec.OuterShellThickness_mm * 1.5);
        double throatPlateThickness_mm = System.Math.Min(
            System.Math.Max(
                System.Math.Max(contour.CowlLength_mm, spec.OuterShellThickness_mm * 2.0),
                2.0),
            throatPlateMaxFromPlug_mm);
        var throatRing = new AnnularThroatImplicit(
            xMin_mm:  -(float)throatPlateThickness_mm * 0.5f,
            xMax_mm:   (float)throatPlateThickness_mm * 0.5f,
            rInner_mm: (float)rOuter_mm,
            rOuter_mm: (float)(rChamber_mm + spec.OuterShellThickness_mm));
        // Plug body rooted at the throat plane (x = 0) and growing +X.
        var plug = new RevolvedPlugImplicit(contour, offsetX_mm: 0f);

        // Sprint fix (2026-04-25 layer 8+9): N spider struts bridging the
        // throat plate to the axis. An STL-connectivity audit found the
        // plug body was a separate connected component, plus a tiny
        // floating cap at the plug tip (the plug tapers to r≈0 quickly
        // due to area-Mach back-solve hitting the chamber outer
        // boundary). Spiders extend from r=0 to rOuter so they capture
        // both the plug body AND the tip cap, making the whole assembly
        // a single printable piece. Axial extent matches the throat
        // plate band; thickness ≈ 1.5 × shell.
        var spider = new SpiderStrutsImplicit(
            xMin_mm:    -(float)throatPlateThickness_mm * 0.5f,
            xMax_mm:     (float)throatPlateThickness_mm * 0.5f,
            rInner_mm:   0f,
            rOuter_mm:   (float)rOuter_mm,
            thickness_mm: (float)(spec.OuterShellThickness_mm * 1.5),
            count:        4);

        var assembly = new AerospikeAssemblyImplicit(
            chamberOuter: chamberContour,
            chamberXMin_mm: chamberXMin_mm,
            chamberXMax_mm: chamberXMax_mm,
            throatRing: throatRing,
            plug: plug,
            chamberInner: chamberInnerContour,
            spider: spider);

        // ── Voxelise ───────────────────────────────────────────────
        // Bounding box sized for full coverage with some margin.
        const float bboxPad = 10f;
        float minX = -(float)chamberLength_mm - bboxPad;
        float maxX =  (float)contour.PlugTruncatedLength_mm + bboxPad;
        float bboxRmax = (float)System.Math.Max(
            rChamber_mm + spec.OuterShellThickness_mm + 2.0,
            rOuter_mm + spec.OuterShellThickness_mm + 2.0) + bboxPad;
        var bounds = new BBox3(
            new Vector3(minX,       -bboxRmax, -bboxRmax),
            new Vector3(maxX,        bboxRmax,  bboxRmax));
        var vox = LibraryScope.MakeVoxels(assembly, bounds);

        // ── Optional regen channels ─────────────────────────
        // When `IncludeRegenChannels` is true, cut N axial cooling
        // channels into the plug body. One voxelise of the channel
        // array + one BoolSubtract — constant-cost in N.
        if (spec.IncludeRegenChannels)
        {
            var channelArray = new AerospikePlugChannelArray(
                contour: contour,
                count:   spec.PlugChannelCount,
                tWall_mm: (float)spec.PlugWallThickness_mm,
                depth_mm: (float)spec.PlugChannelDepth_mm,
                width_mm: (float)spec.PlugChannelWidth_mm);
            var channelVox = LibraryScope.MakeVoxels(channelArray, bounds);
            vox.BoolSubtract(channelVox);
            (channelVox as System.IDisposable)?.Dispose();
        }

        return physics with { Voxels = new PicoGKVoxelHandle(vox) };
    }

    private static double ConeFrustumVolume_mm3(double rBase1, double rBase2, double length)
        => System.Math.PI / 3.0 * length
         * (rBase1 * rBase1 + rBase1 * rBase2 + rBase2 * rBase2);

    /// <summary>
    /// Sprint 26 (2026-04-23): linear-aerospike physics-only entry
    /// point. Mirrors <see cref="BuildPhysicsOnly"/> for the extruded-
    /// rectangular plug topology (X-33 XRS-2200 lineage). Returns an
    /// <see cref="AerospikeBuildResult"/> with
    /// <see cref="AerospikeBuildResult.Voxels"/> null — voxelisation
    /// of the rectangular plug body is a Sprint-27+ follow-on (needs a
    /// new <c>RectangularPlugImplicit</c> + chamber transition
    /// manifold SDF). The physics / contour / thermal / feasibility
    /// path is complete, so SA scoring + the
    /// <c>LINEAR_AEROSPIKE_ASPECT_RATIO</c> feasibility gate + the
    /// Sprint 15 plug-cooling opt-in all work end-to-end.
    /// <para>
    /// Every non-geometry code path (thermal solver, feasibility
    /// evaluator, injector sizing, scoring dispatch) is blind to the
    /// linear / axisymmetric distinction except for a single branch on
    /// <see cref="AerospikeContour.IsLinear"/>. That's the design's
    /// key forcing function: adding a new aerospike topology is an
    /// additive edit, not a switch hunt.
    /// </para>
    /// </summary>
    public static AerospikeBuildResult BuildLinearPhysicsOnly(AerospikeSpec spec)
    {
        if (!spec.IsLinear)
            throw new System.ArgumentException(
                "BuildLinearPhysicsOnly requires spec.IsLinear = true. "
              + "Use BuildPhysicsOnly for the axisymmetric topology.",
                nameof(spec));
        if (spec.LinearPlugWidth_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(spec),
                "LinearPlugWidth_mm must be positive on a linear spec.");

        // ── 1. Propellant lookup → throat-area derivation ──────────
        var gas = PropellantTables.Lookup(
            spec.PropellantPair, spec.MixtureRatio, spec.ChamberPressure_Pa);
        const double cf_approx = 1.4;
        double throatArea_m2 = spec.Thrust_N / (cf_approx * spec.ChamberPressure_Pa);
        double throatArea_mm2 = throatArea_m2 * 1e6;
        // Linear throat: A_t = 2 · h_throat · W → h_throat = A_t / (2 W).
        // The factor of 2 captures the XRS-2200 symmetric top + bottom
        // slots; single-sided variants are out of scope for Sprint 26.
        double W_mm = spec.LinearPlugWidth_mm;
        double hThroat_mm = throatArea_mm2 / (2.0 * W_mm);
        // No inner plug nose on the linear path (the plug itself occupies
        // the flow-split axis), so expose a zero for downstream code that
        // reads ThroatInnerRadius_mm as a bookkeeping placeholder.
        const double rInnerPlaceholder_mm = 0.0;

        // For wide-plug linear aerospikes (XRS-2200: W ≈ 2 300 mm vs
        // h_throat ≈ 34 mm), the correct Angelino R_o that reproduces the
        // real plug length is the exit-equivalent radius √(ε·A_t/π), not
        // h_throat. Using h_throat gives L_trunc ≈ 29 mm / AR ≈ 0.01;
        // √(ε·A_t/π) ≈ 1 691 mm gives L_trunc ≈ 1 430 mm / AR ≈ 0.62,
        // matching Plum Brook 1999 test-article dimensions (Wallerstedt 1998
        // AIAA-98-3522). h_throat still drives the physical slot area.
        double contourRadius_mm = System.Math.Sqrt(
            spec.ExpansionRatio * throatArea_mm2 / System.Math.PI);

        // ── 2. Linear aerospike contour ────────────────────────────
        var contour = LinearAerospikeContourGenerator.Generate(
            throatHeight_mm:   hThroat_mm,
            plugWidth_mm:      W_mm,
            expansionRatio:    spec.ExpansionRatio,
            plugLengthRatio:   spec.PlugLengthRatio,
            gamma:             gas.Gamma,
            stationCount:      80,
            includeCowl:       true,
            contourRadius_mm:  contourRadius_mm);

        // ── 3. Combustion chamber sizing ───────────────────────────
        // Keep the pre-throat chamber geometry circular for Sprint 26
        // scope containment — the rectangular-manifold chamber that
        // XRS-2200 actually uses is a follow-on. Total chamber area
        // = contraction × throat area; circular ⇒ radius = √(A / π).
        double contractionRatio = spec.ChamberContractionRatio;
        double aChamber_mm2 = contractionRatio * throatArea_mm2;
        double rChamber_mm = System.Math.Sqrt(aChamber_mm2 / System.Math.PI);
        double chamberLength_mm = spec.ChamberLengthRatio * rChamber_mm;

        // ── 4. Analytical volume + mass ────────────────────────────
        // Chamber shell: cylindrical annulus (unchanged — circular chamber).
        double shellVol_mm3 = 2.0 * System.Math.PI * rChamber_mm
                            * spec.OuterShellThickness_mm * chamberLength_mm;
        // Plug body: rectangular frustum from
        // (2·h_throat × W) at x = 0 down to (2·h_base × W) at x = L_trunc.
        // Volume = average cross-section × length.
        // h_base uses a linear-taper approximation from h_throat at the
        // throat to 0 at the full-spike tip (matching the r_linear_mm
        // formula in AerospikeContourGenerator). contour.PlugBaseRadius_mm
        // is on the contourRadius scale and is not the physical slot height.
        double hBase_mm = hThroat_mm * (1.0 - spec.PlugLengthRatio);
        double aPlugIn_mm2  = 2.0 * hThroat_mm * W_mm;
        double aPlugOut_mm2 = 2.0 * hBase_mm   * W_mm;
        double plugVol_mm3 = 0.5 * (aPlugIn_mm2 + aPlugOut_mm2)
                           * contour.PlugTruncatedLength_mm;
        double solidVol_mm3 = shellVol_mm3 + plugVol_mm3;
        const double density_gPerCm3 = 8.9;   // GRCop-42 nominal
        double mass_g = solidVol_mm3 * 1e-3 * density_gPerCm3;

        // ── 5. Phase 2 thermal solve (optional) ────────────────────
        // The solver branches on contour.IsLinear internally; Sprint 15
        // opt-in fields still gate whether the thermal solve runs.
        AerospikeThermalResult? thermal = null;
        if (spec.IncludeRegenChannels)
        {
            var pairMeta = Combustion.PropellantPairs.GetMeta(spec.PropellantPair);
            var fluid = Coolant.CoolantRegistry.Get(pairMeta.CoolantFluidKey);
            var material = HeatTransfer.WallMaterials.All[System.Math.Clamp(
                spec.WallMaterialIndex, 0, HeatTransfer.WallMaterials.All.Length - 1)];

            double totalMdot = spec.Thrust_N / System.Math.Max(gas.CStar_ms * spec.CStarEfficiency * cf_approx, 1e-6);
            double fuelMdot = totalMdot / (1.0 + spec.MixtureRatio);

            var coolingInputs = new AerospikePlugCoolingInputs(
                Contour:                contour,
                Gas:                    gas,
                Wall:                   material,
                ChannelCount:           spec.PlugChannelCount,
                ChannelWidth_mm:        spec.PlugChannelWidth_mm,
                ChannelDepth_mm:        spec.PlugChannelDepth_mm,
                PlugWallThickness_mm:   spec.PlugWallThickness_mm,
                CoolantMassFlow_kgs:    fuelMdot,
                CoolantInletTemp_K:     spec.CoolantInletTemp_K,
                CoolantInletPressure_Pa: spec.CoolantInletPressure_Pa,
                CoolantFluid:           fluid);
            thermal = HeatTransfer.AerospikePlugCooling.Solve(coolingInputs);
        }

        // Injector sizing + face thermal: mostly chamber-frame, not
        // plug-topology-specific — the existing axisymmetric logic is
        // re-applied with the circular pre-chamber assumption above.
        AerospikeInjectorSizing? injectorSizing = null;
        HeatTransfer.AerospikeInjectorFaceResult? injectorFace = null;
        if (spec.InjectorPattern is { } pattern)
        {
            var (oxDensity, fuelDensity) =
                Injector.OrificeModel.InjectionDensities(spec.PropellantPair);
            double totalMdot = spec.Thrust_N
                             / System.Math.Max(gas.CStar_ms * spec.CStarEfficiency * cf_approx, 1e-6);
            double fuelFlow = totalMdot / (1.0 + spec.MixtureRatio);
            double oxFlow   = totalMdot - fuelFlow;
            double deltaPInj_Pa = pattern.DeltaPInjFraction * spec.ChamberPressure_Pa;

            var patternSizing = pattern.SizePattern(
                totalOxFlow_kgs:   oxFlow,
                totalFuelFlow_kgs: fuelFlow,
                deltaPInj_Pa:      deltaPInj_Pa,
                oxDensity_kgm3:    oxDensity,
                fuelDensity_kgm3:  fuelDensity);

            double pitchR_mm = pattern.PitchCircleRadius_mm > 0
                ? pattern.PitchCircleRadius_mm
                : 0.60 * rChamber_mm;
            int elementCount = System.Math.Max(1, patternSizing.ElementCount);
            double arcSpacing_mm = 2.0 * System.Math.PI * pitchR_mm / elementCount;
            const double HousingFactor = 1.8;
            double oxOD = patternSizing.PerElementResult.OxEquivDiameter_mm;
            double fuelOD = patternSizing.PerElementResult.FuelEquivDiameter_mm;
            double elementOD_mm = HousingFactor * System.Math.Max(oxOD, fuelOD);
            const double LpbfFloor_mm = 2.0;
            double minClearance_mm = elementOD_mm + LpbfFloor_mm;
            bool clearanceOk = arcSpacing_mm >= minClearance_mm;

            injectorSizing = new AerospikeInjectorSizing(
                PatternSizing:           patternSizing,
                PitchCircleRadius_mm:    pitchR_mm,
                ArcSpacing_mm:           arcSpacing_mm,
                ElementOuterDiameter_mm: elementOD_mm,
                MinClearance_mm:         minClearance_mm,
                ClearanceOk:             clearanceOk);

            var preliminary = new AerospikeBuildResult(
                Voxels:                null,
                Contour:               contour,
                ThroatOuterRadius_mm:  hThroat_mm,
                ThroatInnerRadius_mm:  rInnerPlaceholder_mm,
                PlugTruncatedLength_mm: contour.PlugTruncatedLength_mm,
                ChamberRadius_mm:      rChamber_mm,
                ChamberLength_mm:      chamberLength_mm,
                TotalLength_mm:        chamberLength_mm + contour.PlugTruncatedLength_mm,
                TotalDiameter_mm:      2.0 * (rChamber_mm + spec.OuterShellThickness_mm),
                SolidVolume_mm3:       solidVol_mm3,
                EstimatedMass_g:       mass_g,
                Description:           "",
                Thermal:               thermal,
                InjectorSizing:        injectorSizing);
            injectorFace = HeatTransfer.AerospikeInjectorFaceThermal.Estimate(
                build:                   preliminary,
                gas:                     gas,
                coolantInletTemp_K:      spec.CoolantInletTemp_K,
                coolantInletPressure_Pa: spec.CoolantInletPressure_Pa,
                propellantPair:          spec.PropellantPair,
                mixtureRatio:            spec.MixtureRatio,
                // PH-36 + PH-35 aerospike-face follow-ons (2026-04-29 —
                // closes #233 + #234). Forward the new spec fields for
                // per-pair oxidizer T + face material T-limit override.
                oxidizerInletTemp_K:             spec.OxidizerInletTemp_K,
                injectorFaceMaxTemp_K_Override:  spec.InjectorFaceMaxTemp_K_Override);
        }

        string desc = $"Linear aerospike @ {spec.Thrust_N / 1000:F1} kN, Pc={spec.ChamberPressure_Pa / 1e6:F1} MPa, "
                    + $"ε={spec.ExpansionRatio:F0}, plug={spec.PlugLengthRatio:F2}, "
                    + $"W={W_mm:F1} mm, AR={contour.LinearAspectRatio:F2} "
                    + $"({spec.PropellantPair}, γ={gas.Gamma:F3})"
                    + (spec.IncludeRegenChannels
                        ? $", regen {spec.PlugChannelCount}×({spec.PlugChannelWidth_mm:F1}×{spec.PlugChannelDepth_mm:F1} mm)"
                        : "");

        return new AerospikeBuildResult(
            Voxels:                  null,
            Contour:                 contour,
            ThroatOuterRadius_mm:    hThroat_mm,
            ThroatInnerRadius_mm:    rInnerPlaceholder_mm,
            PlugTruncatedLength_mm:  contour.PlugTruncatedLength_mm,
            ChamberRadius_mm:        rChamber_mm,
            ChamberLength_mm:        chamberLength_mm,
            TotalLength_mm:          chamberLength_mm + contour.PlugTruncatedLength_mm,
            TotalDiameter_mm:        2.0 * (rChamber_mm + spec.OuterShellThickness_mm),
            SolidVolume_mm3:         solidVol_mm3,
            EstimatedMass_g:         mass_g,
            Description:             desc,
            Thermal:                 thermal,
            InjectorSizing:          injectorSizing,
            InjectorFace:            injectorFace);
    }

    /// <summary>
    /// Sprint 26 follow-on (2026-04-24): end-to-end linear-aerospike
    /// voxel build. Mirrors <see cref="Build"/> for the extruded-
    /// rectangular topology — runs <see cref="BuildLinearPhysicsOnly"/>
    /// first for physics + contour + thermal, then composes the
    /// rectangular plug (<see cref="RectangularPlugImplicit"/>) with
    /// the circular pre-throat chamber via
    /// <see cref="LinearAerospikeAssemblyImplicit"/> and voxelises
    /// against a box sized to the linear extent.
    /// <para>
    /// Regen-channel cutting on the linear path is not yet wired — the
    /// Sprint 15 <see cref="AerospikePlugChannelArray"/> assumes an
    /// axisymmetric plug (channels equi-spaced in θ). A rectangular
    /// analogue is the next follow-on; for now the
    /// <see cref="AerospikeSpec.IncludeRegenChannels"/> flag still
    /// populates the thermal result on the linear path (the solver
    /// branches on <see cref="AerospikeContour.IsLinear"/>) but the
    /// voxel body is solid. That's the same trade-off Sprint 2a made
    /// for BuildPhysicsOnly before Sprint 15 cut the axisymmetric
    /// channels.
    /// </para>
    /// </summary>
    public static AerospikeBuildResult BuildLinear(AerospikeSpec spec, double voxelSize_mm = Voxelforge.Constants.VoxelConstants.DefaultBuilderVoxelSize_mm)
    {
        if (voxelSize_mm <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(voxelSize_mm),
                "voxel size must be positive");

        var physics = BuildLinearPhysicsOnly(spec);
        var contour = physics.Contour;
        double rChamber_mm      = physics.ChamberRadius_mm;
        double chamberLength_mm = physics.ChamberLength_mm;
        double hThroat_mm       = physics.ThroatOuterRadius_mm;   // plug half-height on linear path
        double plugWidth_mm     = contour.PlugWidth_mm;

        // ── Compose IImplicits ─────────────────────────────────────
        // Circular pre-throat chamber (matches BuildLinearPhysicsOnly's
        // geometry assumption). A rectangular-manifold chamber is a
        // Sprint-28+ follow-on once a real XRS-2200-class design is on
        // the roadmap.
        var chamberContour = new RevolvedContourImplicit(new[]
        {
            (-chamberLength_mm,        rChamber_mm + spec.OuterShellThickness_mm),
            (-chamberLength_mm * 0.3,  rChamber_mm + spec.OuterShellThickness_mm),
            (0.0,                       hThroat_mm + spec.OuterShellThickness_mm * 0.5),
        });
        float chamberXMin_mm = (float)(-chamberLength_mm);
        float chamberXMax_mm = 0f;

        // Rectangular plug rooted at the throat plane (x = 0) and
        // growing +X. The RectangularPlugImplicit ctor gates on
        // contour.IsLinear.
        var plug = new RectangularPlugImplicit(contour, offsetX_mm: 0f);

        var assembly = new LinearAerospikeAssemblyImplicit(
            chamberOuter:   chamberContour,
            chamberXMin_mm: chamberXMin_mm,
            chamberXMax_mm: chamberXMax_mm,
            plug:           plug);

        // ── Voxelise ───────────────────────────────────────────────
        // Bounding box sized for full coverage with margin. Transverse
        // extent in Y is max(plug half-height, chamber radius +
        // shell); extent in Z is max(plug half-width, chamber radius +
        // shell) — the circular chamber bounds the Z extent near the
        // throat, the plug width bounds it downstream.
        const float bboxPad = 10f;
        float minX = -(float)chamberLength_mm - bboxPad;
        float maxX =  (float)contour.PlugTruncatedLength_mm + bboxPad;
        float bboxY = (float)System.Math.Max(
            hThroat_mm,
            rChamber_mm + spec.OuterShellThickness_mm + 2.0) + bboxPad;
        float bboxZ = (float)System.Math.Max(
            0.5 * plugWidth_mm,
            rChamber_mm + spec.OuterShellThickness_mm + 2.0) + bboxPad;
        var bounds = new BBox3(
            new Vector3(minX, -bboxY, -bboxZ),
            new Vector3(maxX,  bboxY,  bboxZ));
        var vox = LibraryScope.MakeVoxels(assembly, bounds);

        // Note: rectangular plug-channel cutting is deferred. When
        // spec.IncludeRegenChannels is true the thermal solve still
        // runs (see BuildLinearPhysicsOnly) so the
        // AEROSPIKE_PLUG_WALL_TEMP / AEROSPIKE_COOLANT_CAVITATION_RISK
        // gates fire correctly; only the voxel body lacks the
        // channel cutouts today.

        return physics with { Voxels = new PicoGKVoxelHandle(vox) };
    }
}
