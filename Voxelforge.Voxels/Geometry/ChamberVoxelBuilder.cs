// ChamberVoxelBuilder.cs — Build a full regen-cooled thrust chamber as
// PicoGK voxels from a ChamberContour + ChannelSchedule.
//
// Pipeline (all cuts happen on a simple shell before any material is added,
// to stay well clear of the TPMS-style "never BoolSubtract through thin
// features" pitfall that bit the heat-exchanger project):
//
//   1. Inner-wall SDF from contour.
//   2. Outer-jacket SDF = inner + t_wall + h_channel(x) + t_jacket.
//   3. Shell = outer − inner.
//   4. Subtract N cooling-channel voids (annular/axial, following contour).
//   5. Subtract contour-following manifold plenums at each end (inlet at
//      nozzle exit for counterflow, outlet at injector end).
//   6. Subtract radial coolant ports (start at plenum inner radius of the
//      local wall, extend outward past the jacket OD — never pierce the
//      combustion cavity).
//   7. Wall-safe smoothing (capped at 25 % of the thinnest wall).
//   8. Inject + mounting flanges with axial propellant ports; BoolAdded
//      last as simple primitives, no further cuts through combined body.
//
// Coordinate convention: +X is chamber centerline (injector at x = 0,
// nozzle exit at x = L). Flanges, when enabled, live in x < 0 (injector
// end) and x > L (nozzle mounting end).

using System.Numerics;
using PicoGK;
using Voxelforge.Chamber;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;

namespace Voxelforge.Geometry;

// ChamberGeometryResult was extracted to Voxelforge.Core/Chamber/ as
// part of A1. ChamberBuildOptions was extracted to
// Voxelforge.Core/Geometry/ChamberBuildOptions.cs as part of ADR-021
// (Sprint A-3, 2026-04-30) so the headless orchestrators can reference
// the build-options record without dragging the Voxels project + PicoGK
// into Core. BuildAnalytical (pure-C# math) was extracted to
// Voxelforge.Core/Geometry/ChamberAnalyticalBuilder.cs in the same
// sprint; this file now holds only the PicoGK-using full voxel build.
//
// Sprint A-3 + B-3 merge note (2026-04-30): the OOB-6 acoustic-damper
// fields (DamperType / DamperCount / Helmholtz* / QuarterWave*) shipped
// to ChamberBuildOptions on main as PR #319 while this Sprint A-3 PR
// was in flight. Those fields landed in the Core-resident
// ChamberBuildOptions.cs (not here) during the merge.

public static class ChamberVoxelBuilder
{
    /// <inheritdoc cref="ChamberAnalyticalBuilder.BuildAnalytical(ChamberBuildOptions)"/>
    /// <remarks>
    /// Sprint A-3 / ADR-021: thin pass-through to
    /// <see cref="ChamberAnalyticalBuilder.BuildAnalytical"/> in Core
    /// for back-compat. New code should call the Core helper directly.
    /// </remarks>
    public static ChamberGeometryResult BuildAnalytical(ChamberBuildOptions opt)
        => ChamberAnalyticalBuilder.BuildAnalytical(opt);

    public static ChamberGeometryResult Build(ChamberBuildOptions opt, double voxelSize_mm = 0.0)
    {
        // Per-stage wall-clock tally. Zero-allocation on the hot path;
        // stamps the resulting BuildProfile onto the returned
        // ChamberGeometryResult so CUDA-gate baselines can be read off
        // a single Build() call without running the subprocess exporter.
        var profiler = new BuildProfiler();

        var contour = opt.Contour;
        var ch = opt.Channels;

        // ── Inner-wall contour (defines the gas-side surface) ─────
        var innerPts = contour.Stations.Select(s => ((double)s.X_mm, (double)s.R_mm)).ToArray();
        var innerImpl = new RevolvedContourImplicit(innerPts);

        // ── Outer-jacket contour ──────────────────────────────────
        double xThroat = contour.Stations[contour.ThroatIndex].X_mm;
        double xEnd = contour.TotalLength_mm;
        // Z1 hot-fix / Track B closed-loop (2026-04-28): per-station wall
        // profile feeds the outer-jacket revolve. Null OR length mismatch
        // ⇒ uniform fallback (bit-identical).
        bool useWallProfileBuild = opt.GasSideWallProfile_mm is not null
                                && opt.GasSideWallProfile_mm.Count == contour.Stations.Length;
        var outerPts = contour.Stations.Select((s, i) =>
        {
            // Ablative-only (SkipChannelGeneration) builds collapse the
            // channel-height term — the shell becomes plain wall + jacket.
            double h = opt.SkipChannelGeneration
                ? 0.0
                : InterpChannelHeight(s.X_mm, 0, xThroat, xEnd, ch);
            double t_wall_i = useWallProfileBuild
                ? opt.GasSideWallProfile_mm![i]
                : ch.GasSideWallThickness_mm;
            double r = s.R_mm + t_wall_i + h + opt.OuterJacketThickness_mm;
            return ((double)s.X_mm, r);
        }).ToArray();
        var outerImpl = new RevolvedContourImplicit(outerPts);

        // ── Bounds (must include any flange extensions + threaded boss protrusions) ──
        double maxOuterContour_mm = outerPts.Max(p => p.Item2);
        double flangeOuterRadius_mm = opt.IncludeInjectorFlange
            ? Math.Max(maxOuterContour_mm, contour.ChamberRadius_mm * opt.InjectorFlangeOuterRadiusFactor)
            : maxOuterContour_mm;
        double mountOuterRadius_mm = opt.IncludeMountingFlange
            ? Math.Max(maxOuterContour_mm, contour.ExitRadius_mm + opt.OuterJacketThickness_mm + 8.0)
            : maxOuterContour_mm;

        // Threaded coolant port bosses protrude radially from the jacket.
        var coolantPortSpec = PortStandards.Get(opt.CoolantPortStandard);
        float coolantBossOut = (opt.IncludeInletOutletPorts && opt.IncludeManifolds && !opt.SkipChannelGeneration)
            ? PortGeometry.RequiredClearanceMM(coolantPortSpec, (float)opt.OuterJacketThickness_mm + 1f)
            : 0f;
        double radialBossOuter_mm = maxOuterContour_mm + coolantBossOut;

        // Propellant port bosses protrude axially past the injector flange.
        var propPortSpec = PortStandards.Get(opt.PropellantPortStandard);
        float propBossOut = opt.IncludeInjectorFlange
            ? PortGeometry.RequiredClearanceMM(propPortSpec, (float)opt.InjectorFlangeThickness_mm + 1f)
            : 0f;

        double maxOuterAll_mm = Math.Max(Math.Max(maxOuterContour_mm, flangeOuterRadius_mm),
                                         Math.Max(mountOuterRadius_mm, radialBossOuter_mm));

        float xMinBound = opt.IncludeInjectorFlange
            ? -(float)opt.InjectorFlangeThickness_mm - propBossOut - 2f
            : -2f;
        // Gimbal trunnions / flexures project AFT of the mount flange.
        // PinJoint / Cardan use Roark `LugLength_mm` beam-cantilever as
        // their visible aft extension; flexures use
        // `FlexureLength_mm`. Extend xMaxBound so the voxel sweep includes
        // them, otherwise the BoolAdd silently clips at the bounds plane.
        float gimbalAftExt = 0f;
        if (opt.IncludeMountingFlange
         && opt.MountConfiguration != Structure.MountConfiguration.FixedFlange)
        {
            gimbalAftExt = opt.MountConfiguration switch
            {
                Structure.MountConfiguration.FlexureGimbal => (float)Structure.GimbalMount.FlexureLength_mm + 2f,
                _                                          => (float)Structure.GimbalMount.LugLength_mm   + 2f,
            };
        }
        float xMaxBound = (float)contour.TotalLength_mm
                        + (opt.IncludeMountingFlange ? (float)opt.MountingFlangeThickness_mm : 0f)
                        + gimbalAftExt + 2f;

        float pad = 2f;
        var bounds = new BBox3(
            new Vector3(xMinBound, -(float)maxOuterAll_mm - pad, -(float)maxOuterAll_mm - pad),
            new Vector3(xMaxBound, (float)maxOuterAll_mm + pad, (float)maxOuterAll_mm + pad));

        // ── 1-3. Shell: outer − inner ─────────────────────────────
        Voxels outerSolid;
        using (profiler.Begin(BuildProfiler.Stage.Shell))
        {
            // ── 1. Outer solid ────────────────────────────────────
            outerSolid = LibraryScope.MakeVoxels(outerImpl, bounds);

            // ── 2. Inner void (combustion cavity) ─────────────────
            var innerSolid = LibraryScope.MakeVoxels(innerImpl, bounds);

            // ── 3. Shell = outerSolid − innerSolid ───────────────
            outerSolid.BoolSubtract(innerSolid);
        }

        // ── 4–6. Cooling channels + manifold plenums + radial ports ───────
        // Skipped entirely when ChannelTopology == None (SkipChannelGeneration):
        // the chamber stays a plain wall + jacket shell for an ablative-only
        // build.
        int N = ch.ChannelCount;
        if (!opt.SkipChannelGeneration)
        {
            // ── 4. Subtract cooling-channel voids ─────────────────────
            float xChStart = (float)(opt.IncludeManifolds ? opt.ManifoldLength_mm : 0.5);
            float xChEnd = (float)(contour.TotalLength_mm - (opt.IncludeManifolds ? opt.ManifoldLength_mm : 0.5));
            if (xChEnd <= xChStart) { xChEnd = xChStart + 1f; }

            using (profiler.Begin(BuildProfiler.Stage.ChannelsOuter))
            {
                if (opt.TpmsKind is { } tpmsKind)
                {
                    // Single-pass TPMS void instead of N discrete
                    // axial/helical channels. One voxelise + one
                    // BoolSubtract — scales better than the O(N) loop on
                    // large-N designs but samples a denser implicit function.
                    // Z1 hot-fix: TPMS uses a single tWall_mm value. When
                    // a per-station profile is present, take the MIN —
                    // that's the conservative bound that keeps strut-to-
                    // gas-wall clearance honoured everywhere along the
                    // chamber. Bench baselines refresh will catch any
                    // net effect.
                    double tpmsWall_mm = useWallProfileBuild
                        ? System.Linq.Enumerable.Min(opt.GasSideWallProfile_mm!)
                        : ch.GasSideWallThickness_mm;
                    var tpmsImpl = new TpmsAnnularImplicit(
                        innerContour:    innerImpl,
                        kind:            tpmsKind,
                        cellEdge_mm:     (float)opt.TpmsCellEdge_mm,
                        solidFraction:   (float)opt.TpmsSolidFraction,
                        tWall_mm:        (float)tpmsWall_mm,
                        hChamber_mm:     (float)ch.ChannelHeightAtChamber_mm,
                        hThroat_mm:      (float)ch.ChannelHeightAtThroat_mm,
                        hExit_mm:        (float)ch.ChannelHeightAtExit_mm,
                        xStart_mm:       xChStart,
                        xThroat_mm:      (float)xThroat,
                        xEnd_mm:         xChEnd);

                    long tVoxStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var tpmsVox = LibraryScope.MakeVoxels(tpmsImpl, bounds);
                    long tBoolStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    outerSolid.BoolSubtract(tpmsVox);
                    long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                    profiler.AddTicks(BuildProfiler.Stage.ChannelVoxelise,    tBoolStart - tVoxStart);
                    profiler.AddTicks(BuildProfiler.Stage.ChannelBoolSubtract, tEnd       - tBoolStart);
                    (tpmsVox as System.IDisposable)?.Dispose();
                    // NoteChannelCount still reports N so downstream
                    // diagnostics / baseline JSONL stay comparable.
                    profiler.NoteChannelCount(N);
                }
                else if (opt.TopologyOptimizedChannelsPerStation is { Count: >= 2 } nPerStation
                      && opt.TopologyOptimizedAxialPositions_mm is { Count: >= 2 } xPerStation
                      && nPerStation.Count == xPerStation.Count)
                {
                    // OOB-2 Sprint 2 (2026-05-04): variable-pitch channels.
                    // The SIMP solver in TopologyOptimizedChannels.Solve writes
                    // per-station integer counts (8 ≤ N ≤ N_base) that this
                    // implicit linearly interpolates along x. Modular-θ math
                    // generalises cleanly: thetaStep = 2π / N_local(x). No
                    // helix support yet (Sprint 3 if demanded); manifold
                    // fillets reuse the axial-only path.
                    var topoImpl = new TopologyOptimizedChannelImplicit(
                        innerImpl,
                        (float)ch.GasSideWallThickness_mm,
                        (float)ch.ChannelHeightAtChamber_mm,
                        (float)ch.ChannelHeightAtThroat_mm,
                        (float)ch.ChannelHeightAtExit_mm,
                        xChStart, (float)xThroat, xChEnd,
                        xPerStation,
                        nPerStation,
                        (float)ch.RibThickness_mm,
                        phaseOffsetRad: 0f,
                        manifoldFilletRadius_mm: (float)opt.ChannelManifoldFilletRadius_mm);

                    long tVoxStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var topoVox = LibraryScope.MakeVoxels(topoImpl, bounds);
                    long tBoolStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    outerSolid.BoolSubtract(topoVox);
                    long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                    profiler.AddTicks(BuildProfiler.Stage.ChannelVoxelise,    tBoolStart - tVoxStart);
                    profiler.AddTicks(BuildProfiler.Stage.ChannelBoolSubtract, tEnd       - tBoolStart);
                    (topoVox as System.IDisposable)?.Dispose();
                    profiler.NoteChannelCount(N);
                }
                else
                {
                    // Pattern-mode: one voxelise + one BoolSubtract for all N
                    // channels, replacing the prior O(N) per-channel loop.
                    // AxialChannelPatternImplicit's modular-θ math returns the
                    // same SDF as min(per-channel) at any (x, y, z) — pinned by
                    // AxialChannelPatternEquivalenceTests. At N=179 this drops
                    // chamber-build time on a 20 kN class engine from ~30+ min
                    // to roughly minutes.
                    var patImpl = new AxialChannelPatternImplicit(
                        innerImpl,
                        (float)ch.GasSideWallThickness_mm,
                        (float)ch.ChannelHeightAtChamber_mm,
                        (float)ch.ChannelHeightAtThroat_mm,
                        (float)ch.ChannelHeightAtExit_mm,
                        xChStart, (float)xThroat, xChEnd,
                        N,
                        (float)ch.RibThickness_mm,
                        phaseOffsetRad: 0f,
                        manifoldFilletRadius_mm: (float)opt.ChannelManifoldFilletRadius_mm,
                        helixPitchAngle_deg: (float)opt.HelixPitchAngle_deg);

                    long tVoxStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var patVox = LibraryScope.MakeVoxels(patImpl, bounds);
                    long tBoolStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    outerSolid.BoolSubtract(patVox);
                    long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                    profiler.AddTicks(BuildProfiler.Stage.ChannelVoxelise,    tBoolStart - tVoxStart);
                    profiler.AddTicks(BuildProfiler.Stage.ChannelBoolSubtract, tEnd       - tBoolStart);
                    (patVox as System.IDisposable)?.Dispose();
                    profiler.NoteChannelCount(N);
                }
            }

            // ── 5. Manifold plenums that track the contour ────────────
            if (opt.IncludeManifolds)
            {
                using (profiler.Begin(BuildProfiler.Stage.Manifolds))
                {
                    // Inlet manifold at nozzle-exit end (counterflow coolant inlet)
                    double xManInletStart = contour.TotalLength_mm - opt.ManifoldLength_mm;
                    double xManInletEnd = contour.TotalLength_mm;
                    // Outlet manifold at injector end (warm coolant exit, feeds fuel line)
                    double xManOutletStart = 0.0;
                    double xManOutletEnd = opt.ManifoldLength_mm;

                    // Effective plenum depth = local channel height (contour-following);
                    // use per-end channel heights to keep the jacket thickness uniform.
                    float hInlet = (float)ch.ChannelHeightAtExit_mm;
                    float hOutlet = (float)ch.ChannelHeightAtChamber_mm;
                    float clearance = 0.4f;

                    var inletMan = new RevolvedPlenumImplicit(
                        innerImpl,
                        (float)xManInletStart, (float)xManInletEnd,
                        (float)ch.GasSideWallThickness_mm,
                        hInlet,
                        clearance);
                    var outletMan = new RevolvedPlenumImplicit(
                        innerImpl,
                        (float)xManOutletStart, (float)xManOutletEnd,
                        (float)ch.GasSideWallThickness_mm,
                        hOutlet,
                        clearance);

                    // P13 pattern: union inlet+outlet plenum implicits into
                    // one voxelize + BoolSubtract (was 2 separate voxels).
                    outerSolid.BoolSubtract(
                        LibraryScope.MakeVoxels(new UnionImplicit(inletMan, outletMan), bounds));
                }
            }

            // ── 6. Radial coolant ports (bore through jacket; optional threaded boss) ──
            if (opt.IncludeInletOutletPorts && opt.IncludeManifolds)
            {
                using (profiler.Begin(BuildProfiler.Stage.RadialPorts))
                {
                    double xInletPort = contour.TotalLength_mm - 0.5 * opt.ManifoldLength_mm;
                    double xOutletPort = 0.5 * opt.ManifoldLength_mm;

                    AddRadialPort(outerSolid, bounds, innerImpl, outerImpl,
                        (float)xInletPort,
                        (float)(opt.PortDiameter_mm * 0.5),
                        (float)ch.GasSideWallThickness_mm,
                        (float)opt.OuterJacketThickness_mm,
                        coolantPortSpec);
                    AddRadialPort(outerSolid, bounds, innerImpl, outerImpl,
                        (float)xOutletPort,
                        (float)(opt.PortDiameter_mm * 0.5),
                        (float)ch.GasSideWallThickness_mm,
                        (float)opt.OuterJacketThickness_mm,
                        coolantPortSpec);
                }
            }
        }

        // ── 7. Wall-safe smoothing ────────────────────────────────
        if (opt.SmoothingRadius_mm > 0)
        {
            using (profiler.Begin(BuildProfiler.Stage.Smoothen))
            {
                // Z1 hot-fix: respect the THINNEST wall in the profile to
                // keep the 25 % feature-floor cap honoured everywhere on
                // the wall, not just the uniform baseline.
                double minWallProfile_mm = useWallProfileBuild
                    ? System.Linq.Enumerable.Min(opt.GasSideWallProfile_mm!)
                    : ch.GasSideWallThickness_mm;
                double minWall = Math.Min(minWallProfile_mm, ch.RibThickness_mm);
                double safeRadius = Math.Min(opt.SmoothingRadius_mm, 0.25 * minWall);
                if (safeRadius > 0.02) outerSolid.Smoothen((float)safeRadius);
            }
        }

        // ── 7.5 E-D inner plug ───────────────────────────────────────
        // For ExpansionDeflection topology: fuse a solid truncated-cone plug
        // into the bell after channels are cut but before flange additions.
        // Inert (false by default) on all other topologies.
        if (opt.IncludeExpansionDeflectionPlug)
        {
            using (profiler.Begin(BuildProfiler.Stage.LateFeatures))
            {
                ExpansionDeflectionPlugGeometry.AddPlug(
                    outerSolid, bounds, contour, opt.EdPlugInnerOuterRatio);
            }
        }

        // ── 8. Flanges (added after smoothing; simple primitives) ─
        // The per-flange geometry blocks live in `AddInjectorFlangeFull`
        // / `AddMountingFlangeFull` so the tiled builder
        // (`ChamberAxialTileBuilder.BuildTile`) can call the same
        // helpers for full-fidelity flanges on its first / last tiles.
        // Semantics preserved bit-identically — only the callsite
        // structure changed.
        if (opt.IncludeInjectorFlange)
        {
            using (profiler.Begin(BuildProfiler.Stage.InjectorFlange))
            {
                AddInjectorFlangeFull(outerSolid, bounds, opt, contour,
                                      propPortSpec, maxOuterContour_mm);
            }
        }

        if (opt.IncludeMountingFlange)
        {
            using (profiler.Begin(BuildProfiler.Stage.MountingFlange))
            {
                AddMountingFlangeFull(outerSolid, bounds, opt, contour, maxOuterContour_mm);
            }

            // Hot-fire readiness Item 6 (#260, 2026-04-30): structural
            // thrust-takeout adapter. Sits flush against the mounting
            // flange aft face. Off by default; turning it on requires
            // both the chamber mounting flange AND the adapter flag —
            // the adapter has no meaning without the flange to bolt to.
            if (opt.IncludeThrustTakeoutAdapter)
            {
                using (profiler.Begin(BuildProfiler.Stage.MountingFlange))
                {
                    AddThrustTakeoutAdapterFull(outerSolid, bounds, opt, contour, maxOuterContour_mm);
                }
            }
        }

        // ── 8.5 Injector element orifice bores (optional pattern) ────
        // This step switches on ElementType so each type draws its own
        // recognisable geometry. Supports Pintle + Swirl + Showerhead
        // branches alongside Coax + ImpingingDoublet; also supports
        // layout selection via `InjectorPattern.FaceLayout` so patterns
        // can use hex-grid / annular-rows / central layouts instead of
        // the single pitch circle.
        //   • ImpingingDoublet — two separate bores per element, offset
        //     tangentially along the pitch circle. Ox and fuel bores sized
        //     independently from OxOrificeArea / FuelOrificeArea.
        //   • Coax             — single combined bore with a wider "recess
        //     pocket" at the face face-side (first ~40 % of flange depth),
        //     narrowing to the combined-area bore for the remainder.
        //   • Pintle           — central boss with annular gap + ring of
        //     sleeve holes around it; ignores ElementCount > 1 (pintle is
        //     a single-element injector).
        //   • Swirl            — combined bore + four tangential inlet
        //     slots at 90° intervals simulating the swirl chamber's
        //     tangential feed.
        //   • Showerhead       — two separate bores per element (ox + fuel)
        //     placed in a tight axial pair; no impingement.
        //   • default          — legacy combined-area single bore.
        if (opt.IncludeInjectorFlange
            && opt.InjectorElementPattern is { } elemPat
            && elemPat.Element.IsImplemented
            && opt.InjectorFlowCtx is { } flowCtx)
        {
            using var _injBoresScope = profiler.Begin(BuildProfiler.Stage.InjectorBores);
            double deltaPInj = flowCtx.DeltaPInjFraction * flowCtx.ChamberPressure_Pa;
            var (oxRho, fuelRho) = OrificeModel.InjectionDensities(flowCtx.PropellantPair);

            double filmFrac = System.Math.Clamp(elemPat.OuterRowFilmFraction, 0.0, 0.30);
            double netFuel  = flowCtx.TotalFuelFlow_kgs * (1.0 - filmFrac);

            var sizing = elemPat.SizePattern(
                flowCtx.TotalOxFlow_kgs, netFuel,
                deltaPInj, oxRho, fuelRho);

            double pitchR = elemPat.PitchCircleRadius_mm > 0
                ? elemPat.PitchCircleRadius_mm
                : contour.ChamberRadius_mm * 0.60;

            float xFace = -(float)opt.InjectorFlangeThickness_mm;
            float flangeLen = (float)(opt.InjectorFlangeThickness_mm + 2.0);
            int n = System.Math.Max(elemPat.ElementCount, 1);

            float boreOxR = System.Math.Max(
                (float)System.Math.Sqrt(sizing.PerElementResult.OxOrificeArea_mm2 / System.Math.PI), 0.3f);
            float boreFuelR = System.Math.Max(
                (float)System.Math.Sqrt(sizing.PerElementResult.FuelOrificeArea_mm2 / System.Math.PI), 0.3f);
            float boreCombinedR = System.Math.Max(
                (float)System.Math.Sqrt(
                    (sizing.PerElementResult.OxOrificeArea_mm2 + sizing.PerElementResult.FuelOrificeArea_mm2)
                    / System.Math.PI), 0.3f);

            // Element placement comes from the pattern's FaceLayout
            // (Circular / Hex / AnnularRows / Central) so the voxel
            // shape matches the architectural intent. Circular
            // reproduces the legacy pitch-circle pattern bit-identically.
            var positions = InjectorFaceLayoutGenerator.PlaceElements(
                layout:             elemPat.FaceLayout,
                elementCount:       n,
                pitchRadius_mm:     pitchR,
                chamberRadius_mm:   contour.ChamberRadius_mm);

            switch (elemPat.ElementType)
            {
                case "ImpingingDoublet":
                {
                    // Tangential spacing: half the pitch-circle arc allocated
                    // to this element, capped so the two bores fit without
                    // overlapping. The small safety margin prevents adjacent
                    // elements from sharing a wall. Falls back to radial
                    // offset when positions aren't on a uniform ring.
                    double slotArc = 2.0 * System.Math.PI * pitchR / System.Math.Max(n, 1);
                    double spacing = System.Math.Min(slotArc * 0.25,
                                                     boreOxR + boreFuelR + 0.3);
                    // P13 pattern: union all bores into one voxelize + one
                    // BoolSubtract instead of 2·N separate voxel allocations.
                    if (positions.Length > 0)
                    {
                        var bores = new IImplicit[positions.Length * 2];
                        for (int pi = 0; pi < positions.Length; pi++)
                        {
                            var (py, pz) = positions[pi];
                            double rLocal = System.Math.Sqrt(py * py + pz * pz);
                            // Tangent is perpendicular to the radial direction.
                            double tx = rLocal > 1e-6 ? -pz / rLocal : 1.0;
                            double ty = rLocal > 1e-6 ? +py / rLocal : 0.0;
                            float oxY = (float)(py + spacing * tx);
                            float oxZ = (float)(pz + spacing * ty);
                            float fuY = (float)(py - spacing * tx);
                            float fuZ = (float)(pz - spacing * ty);

                            bores[pi * 2] = new CylinderImplicit(
                                new Vector3(xFace - 1f, oxY, oxZ),
                                new Vector3(1, 0, 0),
                                boreOxR, flangeLen);
                            bores[pi * 2 + 1] = new CylinderImplicit(
                                new Vector3(xFace - 1f, fuY, fuZ),
                                new Vector3(1, 0, 0),
                                boreFuelR, flangeLen);
                        }
                        outerSolid.BoolSubtractTemp(new UnionImplicit(bores), bounds);
                    }
                    break;
                }

                case "Coax":
                {
                    // Two-stage bore per element: a wider recess pocket on
                    // the face side (simulates the LOX-post recess cavity),
                    // narrowing to the combined-area bore through the rest
                    // of the flange and into the chamber cavity.
                    float recessR   = boreCombinedR * 1.40f;   // ~40 % wider
                    float recessLen = System.Math.Max(
                        (float)(opt.InjectorFlangeThickness_mm * 0.40), 1.0f);
                    // P13 pattern: union pocket + bore for all elements into
                    // one voxelize + BoolSubtract (was 2·N separate voxels).
                    if (positions.Length > 0)
                    {
                        var cuts = new IImplicit[positions.Length * 2];
                        for (int pi = 0; pi < positions.Length; pi++)
                        {
                            var (py, pz) = positions[pi];
                            cuts[pi * 2] = new CylinderImplicit(
                                new Vector3(xFace - 1f, (float)py, (float)pz),
                                new Vector3(1, 0, 0),
                                recessR, recessLen + 1f);
                            cuts[pi * 2 + 1] = new CylinderImplicit(
                                new Vector3(xFace - 1f, (float)py, (float)pz),
                                new Vector3(1, 0, 0),
                                boreCombinedR, flangeLen);
                        }
                        outerSolid.BoolSubtractTemp(new UnionImplicit(cuts), bounds);
                    }
                    break;
                }

                case "Pintle":
                {
                    // Single pintle per chamber: central annular gap + ring
                    // of axial sleeve holes. Geometry scales with the
                    // PintleElement's canonical dimensions (12 mm post by
                    // default) rather than per-element areas. Uses the
                    // first (and only) position from InjectorFaceLayout
                    // (which is (0,0) under the Central layout).
                    if (positions.Length == 0) break;
                    var (py, pz) = positions[0];
                    float pinDia = 12.0f;   // matches PintleElement default
                    float postR  = pinDia * 0.5f;
                    float gapR   = System.Math.Max(
                        (float)System.Math.Sqrt(
                            sizing.PerElementResult.OxOrificeArea_mm2
                            / System.Math.PI + postR * postR),
                        postR + 0.3f);

                    // Fuel sleeve holes: drill 18 small axial bores around
                    // a larger circle (pintle diameter + 2 × gap + buffer).
                    int sleeveN = 18;
                    double sleeveR = (gapR + 1.5) * 1.2;
                    double sleeveArea = sizing.PerElementResult.FuelOrificeArea_mm2
                                        / System.Math.Max(sleeveN, 1);
                    float sleeveHoleR = System.Math.Max(
                        (float)System.Math.Sqrt(sleeveArea / System.Math.PI), 0.3f);

                    // P13 pattern: union the outer annular disc + all 18
                    // sleeve holes into one voxelize + BoolSubtract (was
                    // 1 + 18 = 19 separate voxel allocations). The inner
                    // post BoolAdd stays separate — can't merge subtracts
                    // and adds into the same implicit.
                    var cuts = new IImplicit[1 + sleeveN];
                    cuts[0] = new CylinderImplicit(
                        new Vector3(xFace - 1f, (float)py, (float)pz),
                        new Vector3(1, 0, 0),
                        gapR, flangeLen);
                    for (int k = 0; k < sleeveN; k++)
                    {
                        double theta = 2.0 * System.Math.PI * k / sleeveN;
                        float sy = (float)(py + sleeveR * System.Math.Cos(theta));
                        float sz = (float)(pz + sleeveR * System.Math.Sin(theta));
                        cuts[1 + k] = new CylinderImplicit(
                            new Vector3(xFace - 1f, sy, sz),
                            new Vector3(1, 0, 0),
                            sleeveHoleR, flangeLen);
                    }
                    outerSolid.BoolSubtractTemp(new UnionImplicit(cuts), bounds);

                    // Add the inner post back (BoolAdd — can't merge with subtracts).
                    var postCyl = new CylinderImplicit(
                        new Vector3(xFace - 1f, (float)py, (float)pz),
                        new Vector3(1, 0, 0),
                        postR, flangeLen);
                    outerSolid.BoolAddTemp(postCyl, bounds);
                    break;
                }

                case "Swirl":
                {
                    // Combined bore + 4 tangential feed slots at the face
                    // side simulating the swirl chamber's tangential inlets.
                    // Slots are short rectangular bores perpendicular to
                    // the radial direction, inset slightly from the bore.
                    float slotDepth = System.Math.Max(
                        (float)(opt.InjectorFlangeThickness_mm * 0.35), 1.0f);
                    float slotR = boreCombinedR * 0.35f;
                    // P13 pattern: 5 cuts per element (1 bore + 4 slots) all
                    // unioned into one voxelize + BoolSubtract (was 5·N).
                    if (positions.Length > 0)
                    {
                        var cuts = new IImplicit[positions.Length * 5];
                        for (int pi = 0; pi < positions.Length; pi++)
                        {
                            var (py, pz) = positions[pi];
                            cuts[pi * 5] = new CylinderImplicit(
                                new Vector3(xFace - 1f, (float)py, (float)pz),
                                new Vector3(1, 0, 0),
                                boreCombinedR, flangeLen);

                            // Tangential slot cylinders: 4 slots at 90° rotations.
                            // Each slot is a short radial cylinder intersecting
                            // the main bore at its upstream end.
                            for (int s = 0; s < 4; s++)
                            {
                                double slotTheta = System.Math.PI * 0.5 * s;
                                double cosT = System.Math.Cos(slotTheta);
                                double sinT = System.Math.Sin(slotTheta);
                                float sy = (float)(py + boreCombinedR * 0.6 * cosT);
                                float sz = (float)(pz + boreCombinedR * 0.6 * sinT);
                                cuts[pi * 5 + 1 + s] = new CylinderImplicit(
                                    new Vector3(xFace - 0.5f, sy, sz),
                                    new Vector3(1, 0, 0),
                                    slotR, slotDepth);
                            }
                        }
                        outerSolid.BoolSubtractTemp(new UnionImplicit(cuts), bounds);
                    }
                    break;
                }

                case "Showerhead":
                {
                    // Pair of non-impinging bores per element: ox and fuel
                    // drilled side-by-side along the radial direction so
                    // their axial jets never cross.
                    double paddingR = boreOxR + boreFuelR + 0.3;
                    // P13 pattern: union all 2·N bores into one voxelize +
                    // BoolSubtract instead of 2·N separate voxel allocations.
                    if (positions.Length > 0)
                    {
                        var bores = new IImplicit[positions.Length * 2];
                        for (int pi = 0; pi < positions.Length; pi++)
                        {
                            var (py, pz) = positions[pi];
                            double rLocal = System.Math.Sqrt(py * py + pz * pz);
                            double rx = rLocal > 1e-6 ? py / rLocal : 1.0;
                            double ry = rLocal > 1e-6 ? pz / rLocal : 0.0;
                            float oxY = (float)(py + paddingR * 0.5 * rx);
                            float oxZ = (float)(pz + paddingR * 0.5 * ry);
                            float fuY = (float)(py - paddingR * 0.5 * rx);
                            float fuZ = (float)(pz - paddingR * 0.5 * ry);

                            bores[pi * 2] = new CylinderImplicit(
                                new Vector3(xFace - 1f, oxY, oxZ),
                                new Vector3(1, 0, 0),
                                boreOxR, flangeLen);
                            bores[pi * 2 + 1] = new CylinderImplicit(
                                new Vector3(xFace - 1f, fuY, fuZ),
                                new Vector3(1, 0, 0),
                                boreFuelR, flangeLen);
                        }
                        outerSolid.BoolSubtractTemp(new UnionImplicit(bores), bounds);
                    }
                    break;
                }

                default:   // legacy single combined bore
                {
                    // P13 pattern: union all N bores into one voxelize +
                    // BoolSubtract instead of N separate voxel allocations.
                    if (positions.Length > 0)
                    {
                        var bores = new IImplicit[positions.Length];
                        for (int pi = 0; pi < positions.Length; pi++)
                        {
                            var (py, pz) = positions[pi];
                            bores[pi] = new CylinderImplicit(
                                new Vector3(xFace - 1f, (float)py, (float)pz),
                                new Vector3(1, 0, 0),
                                boreCombinedR, flangeLen);
                        }
                        outerSolid.BoolSubtractTemp(new UnionImplicit(bores), bounds);
                    }
                    break;
                }
            }
        }

        // ── 8.6 Internal coolant-to-fuel-plenum crossover (PHASE 5) ──
        // Short axial passage through the injector flange from the upstream
        // manifold region into the chamber-face side. Cut at pitch radius
        // just inside the outer jacket so it stays fully embedded in material
        // on both ends. This is the "closed expander cycle" enabler — the
        // coolant outlet can feed the injector fuel plenum without external
        // plumbing. Geometry is representational at the 0.4 mm session voxel;
        // a future iteration should route it around the channels.
        if (opt.IncludeInjectorFlange && opt.IncludeCoolantCrossover)
        {
            using var _crossScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            double crossR_mm = 0.5 * System.Math.Max(opt.CoolantCrossoverDiameter_mm, 0.8);
            double pitchR_mm = contour.ChamberRadius_mm
                             + ch.GasSideWallThickness_mm
                             + ch.ChannelHeightAtChamber_mm
                             + 0.5 * opt.OuterJacketThickness_mm;
            float startX = -(float)opt.InjectorFlangeThickness_mm - 0.5f;
            float endX   = (float)(opt.ManifoldLength_mm * 0.5);   // half-way into upstream manifold
            float length = endX - startX;
            // Place at θ = π/2 so it doesn't collide with the axial propellant
            // ports (which live at ±Y per step 9 flange drill pattern).
            float y = 0f;
            float z = (float)pitchR_mm;
            var cross = new CylinderImplicit(
                new Vector3(startX, y, z),
                new Vector3(1, 0, 0),
                (float)crossR_mm,
                length);
            outerSolid.BoolSubtractTemp(cross, bounds);
        }

        // ── 8.7 Instrumentation bosses (TIER A.4) ─────────────────
        // Radial holes through the outer jacket at user-specified
        // (axialFraction, azimuth) positions. Each preset carries its own
        // bore diameter + boss OD. We cut the bore only (not the raised
        // boss collar) — keeping the exterior flush avoids clashes with
        // the existing threaded-port bosses. Users who need a raised
        // boss can choose a threaded-port preset instead.
        if (opt.SensorBosses is { Count: > 0 } bosses)
        {
            using var _sensorScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            double L_total_mm = contour.TotalLength_mm;
            foreach (var b in bosses)
            {
                if (!SensorBossPresets.All.TryGetValue(b.Type, out var spec)) continue;
                double ax = System.Math.Clamp(b.AxialFraction, 0.0, 1.0);
                float xHole = (float)(ax * L_total_mm);
                // Bore starts 1 mm outside the jacket OD for the given station;
                // use the local outer radius estimate as the chamber radius
                // plus wall + channel + jacket thickness.
                double rOuter_mm = contour.ChamberRadius_mm
                                 + ch.GasSideWallThickness_mm
                                 + ch.ChannelHeightAtChamber_mm
                                 + opt.OuterJacketThickness_mm + 1.0;
                double theta = b.AzimuthDeg * System.Math.PI / 180.0;
                float dirY = (float) System.Math.Cos(theta);
                float dirZ = (float) System.Math.Sin(theta);
                // Cylinder axis runs from outside the jacket inward.
                // Start point sits outside; drill length = jacket + wall + 0.5 mm margin.
                float startY = dirY * (float)rOuter_mm;
                float startZ = dirZ * (float)rOuter_mm;
                float length = (float)(ch.GasSideWallThickness_mm
                                      + opt.OuterJacketThickness_mm
                                      + 0.5 /* margin */ + 2.0);
                var bore = new CylinderImplicit(
                    new Vector3(xHole, startY, startZ),
                    new Vector3(-dirY, -dirZ, 0f),   // inward toward axis
                    (float)(spec.BoreDiameter_mm * 0.5),
                    length);
                outerSolid.BoolSubtractTemp(bore, bounds);
            }
        }

        // ── 8.8 Igniter cavity ─────────────────────
        // Drilled axially through the injector flange; one cavity + an
        // optional perpendicular feed bore. Placed at (radial offset,
        // azimuth = 0) so it clears the main injector pattern. No-op
        // when IgniterType = None or the injector flange is disabled.
        if (opt.IncludeInjectorFlange && opt.IgniterType != IgniterType.None)
        {
            using var _igniterScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            var iSpec = IgniterPresets.SpecFor(opt.IgniterType);
            if (iSpec.CavityDiameter_mm > 0 && iSpec.CavityDepth_mm > 0)
            {
                double radialOffset_mm = System.Math.Clamp(opt.IgniterRadialFraction, 0.0, 0.8)
                                       * contour.ChamberRadius_mm;
                float xFace = -(float)opt.InjectorFlangeThickness_mm;
                float cavityLen = (float)(iSpec.CavityDepth_mm + 2.0);

                // Main axial cavity — from outside the face, into the chamber.
                var cavity = new CylinderImplicit(
                    new Vector3(xFace - 2f, (float)radialOffset_mm, 0f),
                    new Vector3(1, 0, 0),
                    (float)(iSpec.CavityDiameter_mm * 0.5),
                    cavityLen);
                outerSolid.BoolSubtractTemp(cavity, bounds);

                // Perpendicular feed bore (torch styles only).
                if (iSpec.FeedBoreDiameter_mm > 0)
                {
                    float feedLen = (float)(contour.ChamberRadius_mm + 20.0);
                    float feedStartY = (float)(radialOffset_mm - feedLen * 0.5);
                    var feed = new CylinderImplicit(
                        new Vector3(xFace - 0.5f * (float)iSpec.CavityDepth_mm, feedStartY, 0f),
                        new Vector3(0, 1, 0),
                        (float)(iSpec.FeedBoreDiameter_mm * 0.5),
                        feedLen);
                    outerSolid.BoolSubtractTemp(feed, bounds);
                }
            }
        }

        // ── 8.9 Inlet domes ────────────────────────
        // Hollow out a cylindrical plenum behind the injector face for
        // the fuel (and optionally ox) side. Requires a flange thick
        // enough to contain the dome depth + at least 2 mm of face
        // material; caller is responsible for raising InjectorFlange
        // Thickness_mm when using deeper domes.
        if (opt.IncludeInjectorFlange && opt.FuelDomeDepth_mm > 0)
        {
            using var _domeScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            float domeR = (float)(0.90 * contour.ChamberRadius_mm);   // 10 % smaller than chamber
            float domeStartX = -(float)opt.InjectorFlangeThickness_mm + 2.0f;
            float domeDepth = (float)System.Math.Min(
                opt.FuelDomeDepth_mm,
                opt.InjectorFlangeThickness_mm - 3.0);
            if (domeDepth > 0.5f)
            {
                var dome = new RevolvedDomeImplicit(domeStartX, domeDepth, domeR);
                outerSolid.BoolSubtractTemp(dome, bounds);

                // Optional anti-vortex baffle: a thin radial disc across
                // the dome interior, 0.5 × domeDepth from the back. Cuts
                // swirl without closing the flow area.
                if (opt.IncludeAntiVortexBaffle)
                {
                    float baffleX = domeStartX + 0.5f * domeDepth;
                    // Inner disc sized smaller than the dome so the
                    // outer rim still passes flow around it.
                    var baffle = new DiscImplicit(
                        baffleX, thickness_mm: 1.5f, radius_mm: domeR * 0.6f);
                    outerSolid.BoolAddTemp(baffle, bounds);
                }
            }
        }

        // ── 8.91 Umbilical seal-groove + bolt circle ──
        // Cuts an annular seal groove + 4-bolt clearance pattern around
        // each axial propellant port on the upstream face of the injector
        // flange. Spec carries face OD, bore ID, and seal-groove depth;
        // bolt circle pitched 1.5 mm outside the groove. No-op when
        // UmbilicalStandard == None or flange is disabled.
        if (opt.IncludeInjectorFlange
         && opt.UmbilicalStandard != UmbilicalStandard.None)
        {
            using var _umbilicalScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            var uSpec = UmbilicalStandards.SpecFor(opt.UmbilicalStandard);
            float xFlangeBack = -(float)opt.InjectorFlangeThickness_mm;
            float yPortOffset = (float)(contour.ChamberRadius_mm * 0.5);   // matches AddAxialPropellantPort placement above
            float boreR_mm    = (float)(uSpec.BoreInnerDiameter_mm * 0.5);
            float grooveDepth = (float)System.Math.Min(uSpec.SealGrooveDepth_mm,
                                                       opt.InjectorFlangeThickness_mm * 0.5);
            float grooveWidth = 1.0f;                       // 1 mm trough width (typical O-ring cross-section)
            float boltR       = 1.75f;                      // M3.5 clearance — small fasteners on a low-load umbilical face
            float boltCircleR = boreR_mm + 1.5f + grooveWidth + 2.0f;

            AddUmbilicalSealAndBolts(outerSolid, bounds,
                portCenter:   new Vector3(xFlangeBack, +yPortOffset, 0),
                boreR_mm:     boreR_mm,
                grooveDepth:  grooveDepth,
                grooveWidth:  grooveWidth,
                boltR_mm:     boltR,
                boltCircleR:  boltCircleR,
                flangeThickness_mm: (float)opt.InjectorFlangeThickness_mm);
            AddUmbilicalSealAndBolts(outerSolid, bounds,
                portCenter:   new Vector3(xFlangeBack, -yPortOffset, 0),
                boreR_mm:     boreR_mm,
                grooveDepth:  grooveDepth,
                grooveWidth:  grooveWidth,
                boltR_mm:     boltR,
                boltCircleR:  boltCircleR,
                flangeThickness_mm: (float)opt.InjectorFlangeThickness_mm);
        }

        // ── 8.92 Purge-port bores ──
        // Drill a bore for each configured purge port. Direction +
        // location keyed off PurgeLocation:
        //   • InjectorDomeOx    — axial through injector flange at +Y
        //   • InjectorDomeFuel  — axial through injector flange at -Y
        //   • ChamberPrePurge   — radial through outer jacket at +Z, mid-chamber
        //   • NozzleInertPurge  — radial through outer jacket at +Z, near exit
        // Empty list = no bores drilled. Bore diameter taken straight from
        // PurgePort.BoreDiameter_mm — the flow model has already gated
        // against this upstream of voxel build.
        if (opt.PurgePorts is { Count: > 0 } purgePorts)
        {
            using var _purgeScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            double L_total_mm     = contour.TotalLength_mm;
            double rOuterMid_mm   = contour.ChamberRadius_mm
                                  + ch.GasSideWallThickness_mm
                                  + (opt.SkipChannelGeneration ? 0.0 : ch.ChannelHeightAtChamber_mm)
                                  + opt.OuterJacketThickness_mm + 1.0;
            double rOuterExit_mm  = contour.ExitRadius_mm
                                  + ch.GasSideWallThickness_mm
                                  + (opt.SkipChannelGeneration ? 0.0 : ch.ChannelHeightAtExit_mm)
                                  + opt.OuterJacketThickness_mm + 1.0;
            float yPortAxial      = (float)(contour.ChamberRadius_mm * 0.7);   // off-axis on flange face

            foreach (var port in purgePorts)
            {
                if (port.Fluid == Coolant.PurgeFluid.None || port.BoreDiameter_mm <= 0) continue;
                float boreR = (float)(port.BoreDiameter_mm * 0.5);

                switch (port.Location)
                {
                    case Coolant.PurgeLocation.InjectorDomeOx:
                    case Coolant.PurgeLocation.InjectorDomeFuel:
                        if (!opt.IncludeInjectorFlange) continue;
                        {
                            float ySign = port.Location == Coolant.PurgeLocation.InjectorDomeOx ? +1f : -1f;
                            float xBack = -(float)opt.InjectorFlangeThickness_mm;
                            var axial = new CylinderImplicit(
                                new Vector3(xBack - 1f, ySign * yPortAxial, 0f),
                                new Vector3(1, 0, 0),
                                boreR,
                                (float)opt.InjectorFlangeThickness_mm + 2f);
                            outerSolid.BoolSubtractTemp(axial, bounds);
                        }
                        break;

                    case Coolant.PurgeLocation.ChamberPrePurge:
                    case Coolant.PurgeLocation.NozzleInertPurge:
                        {
                            bool nearExit = port.Location == Coolant.PurgeLocation.NozzleInertPurge;
                            float xHole   = (float)(nearExit ? 0.90 * L_total_mm : 0.50 * L_total_mm);
                            float rOuter  = (float)(nearExit ? rOuterExit_mm : rOuterMid_mm);
                            // Drill +Z inward — keeps it visually clear of the
                            // sensor-boss bores that default to +Y.
                            float length = (float)(ch.GasSideWallThickness_mm
                                                  + opt.OuterJacketThickness_mm
                                                  + 0.5 + 2.0);
                            var radial = new CylinderImplicit(
                                new Vector3(xHole, 0f, rOuter),
                                new Vector3(0, 0, -1),
                                boreR,
                                length);
                            outerSolid.BoolSubtractTemp(radial, bounds);
                        }
                        break;
                }
            }
        }

        // ── 8.92 OOB-6 acoustic-damper cavities (#200) ──
        // Drill an array of Helmholtz cavities (neck + buried cavity) or
        // quarter-wave radial cavities into the chamber outer jacket at
        // mid-barrel. Reuses the sensor-boss CylinderImplicit pattern;
        // implementation lives in AcousticDamperGeometry. No-op when
        // DamperType = None (legacy designs see zero voxel-output drift).
        if (opt.DamperType != Combustion.Stability.AcousticDamperType.None
            && opt.DamperCount > 0)
        {
            using var _damperScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);

            // Place dampers at the chamber barrel mid-station — typical
            // production placement (Saturn V F-1 baffle ring lived ~mid-
            // barrel; SSME LOX preburner dampers similar). Future SA
            // dim could move this axially; v1 ships fixed at 0.50.
            const double damperAxialFraction = 0.50;
            double L_total_mm = contour.TotalLength_mm;

            // Inner radius at mid-station = chamber barrel radius (the
            // acoustic-mode region); outer = chamber radius + jacket.
            double innerR_mm = contour.ChamberRadius_mm;
            double outerR_mm = innerR_mm + ch.GasSideWallThickness_mm
                             + ch.ChannelHeightAtChamber_mm
                             + opt.OuterJacketThickness_mm;

            // Convert physics-side area / volume into voxel-side
            // diameter / length. Cavity proportions: cube-ish
            // (length = diameter) so the buried cavity is a stable
            // single-cylinder primitive that the BoolSubtract path can
            // resolve cleanly at 0.4–1.0 mm voxel.
            double neckDia_mm = 2.0 * Math.Sqrt(opt.HelmholtzNeckArea_mm2 / Math.PI);
            double cavityDia_mm = Math.Cbrt(4.0 * opt.HelmholtzCavityVolume_mm3 / Math.PI);
            double cavityLen_mm = cavityDia_mm;

            var damperGeometry = new AcousticDamperGeometrySpec(
                Type:                 opt.DamperType,
                Count:                opt.DamperCount,
                NeckDiameter_mm:      neckDia_mm,
                NeckLength_mm:        opt.HelmholtzNeckLength_mm,
                CavityDiameter_mm:    cavityDia_mm,
                CavityLength_mm:      cavityLen_mm,
                QuarterWaveLength_mm: opt.QuarterWaveLength_mm,
                QuarterWaveDiameter_mm: opt.QuarterWaveDiameter_mm,
                AxialFraction:        damperAxialFraction,
                InnerRadius_mm:       innerR_mm,
                OuterJacketRadius_mm: outerR_mm);

            AcousticDamperGeometry.AddDamperArray(
                shell:              outerSolid,
                bounds:             bounds,
                spec:               damperGeometry,
                chamberLength_mm:   L_total_mm);
        }

        // ── 8.93 Gimbal trunnions / flexures ──
        // Add aft-projecting hardware on the mount flange. Pin joints and
        // Cardan use cylindrical lugs with through-hole pin bores; flexure
        // uses thin (Width × Thickness) rectangular arms approximated as
        // narrow cylinders. Geometry is sized from the constants in
        // Structure.GimbalMount so a future design change there propagates.
        if (opt.IncludeMountingFlange
         && opt.MountConfiguration != Structure.MountConfiguration.FixedFlange)
        {
            using var _gimbalScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            float xFlangeAft = (float)contour.TotalLength_mm + (float)opt.MountingFlangeThickness_mm;
            float lugR       = (float)(Structure.GimbalMount.PinDiameter_mm * 0.5 + 4f);  // lug = pin + 4 mm wall
            float pinR       = (float)(Structure.GimbalMount.PinDiameter_mm * 0.5);
            float lugLen     = (float)Structure.GimbalMount.LugLength_mm;
            float flexLen    = (float)Structure.GimbalMount.FlexureLength_mm;
            float flexR      = (float)(Structure.GimbalMount.FlexureThickness_mm * 0.5);
            float lugRingR   = (float)(contour.ExitRadius_mm
                                     + opt.OuterJacketThickness_mm + 6f);  // outboard of jacket OD

            switch (opt.MountConfiguration)
            {
                case Structure.MountConfiguration.PinJointGimbal:
                    AddGimbalLugPair(outerSolid, bounds, xFlangeAft,
                        lugCenterR: lugRingR, lugR: lugR, lugLen: lugLen, pinR: pinR, fourLugs: false);
                    break;
                case Structure.MountConfiguration.CardanGimbal:
                    AddGimbalLugPair(outerSolid, bounds, xFlangeAft,
                        lugCenterR: lugRingR, lugR: lugR, lugLen: lugLen, pinR: pinR, fourLugs: true);
                    break;
                case Structure.MountConfiguration.FlexureGimbal:
                    AddGimbalFlexureCross(outerSolid, bounds, xFlangeAft,
                        armOffsetR: lugRingR - flexR, armR: flexR, armLen: flexLen);
                    break;
            }
        }

        // ── 9. Optional injector-face STL import ──────────────────
        string stlMessage = "";
        if (opt.InjectorFaceSTL is { } stlOpt && stlOpt.Enabled)
        {
            using var _injStlScope = profiler.Begin(BuildProfiler.Stage.LateFeatures);
            var loaded = InjectorFaceImport.Load(stlOpt, bounds);
            stlMessage = loaded.Message;
            if (loaded.Voxels != null)
                outerSolid.BoolAdd(loaded.Voxels);
        }

        // ── Measurements ─────────────────────────────────────────
        float vol_mm3;
        BBox3 finalBbox;
        using (profiler.Begin(BuildProfiler.Stage.FinalMeasurements))
        {
            outerSolid.CalculateProperties(out vol_mm3, out finalBbox);
        }

        var material = opt.MaterialForMass ?? WallMaterials.CuCrZr;
        double vol_cm3 = vol_mm3 / 1000.0;
        double mass_g = vol_cm3 * (material.Density_kgm3 / 1000.0);
        double cost = vol_cm3 * material.PrintCostPerCm3_USD;

        // Inner surface area by ring sum along contour
        double innerSurf_mm2 = 0;
        for (int i = 0; i < contour.Stations.Length - 1; i++)
        {
            var a = contour.Stations[i];
            var b = contour.Stations[i + 1];
            double dx = b.X_mm - a.X_mm;
            double r_avg = 0.5 * (a.R_mm + b.R_mm);
            double slant = Math.Sqrt(dx * dx + (b.R_mm - a.R_mm) * (b.R_mm - a.R_mm));
            innerSurf_mm2 += 2.0 * Math.PI * r_avg * slant;
        }

        double totalLen = (finalBbox.vecMax.X - finalBbox.vecMin.X);
        double totalDia = 2 * maxOuterAll_mm;

        var desc = $"L={totalLen:F0}mm, OD={totalDia:F0}mm, N={N} ch, "
                 + $"mass={mass_g:F0}g, cost={cost:F0}USD ({material.Name})";

        // Stamp the per-stage profile onto the result so CUDA-baseline
        // capture is a single Build() away.
        var profile = profiler.Finalize(bounds, voxelSize_mm);

        return new ChamberGeometryResult(
            Voxels: new PicoGKVoxelHandle(outerSolid),
            SolidVolume_mm3: vol_mm3,
            InnerSurfaceArea_mm2: innerSurf_mm2,
            OuterJacketThickness_mm: opt.OuterJacketThickness_mm,
            TotalMass_g: mass_g,
            PrintedCost_USD: cost,
            BoundingLength_mm: totalLen,
            BoundingDiameter_mm: totalDia,
            Description: desc,
            InjectorSTLMessage: stlMessage,
            Profile: profile);
    }

    /// <summary>
    /// Add a radial coolant port at axial station x_port: either a plain
    /// drilled bore (spec.IsThreaded == false), or a threaded nipple boss
    /// with through-bore. Cuts only through the jacket into the plenum —
    /// never reaches the combustion cavity.
    /// </summary>
    private static void AddRadialPort(
        Voxels shell,
        BBox3 bounds,
        RevolvedContourImplicit innerWall,
        RevolvedContourImplicit outerJacket,
        float xPort_mm,
        float plainPortRadius_mm,
        float tWall_mm,
        float tJacket_mm,
        PortSpec spec)
    {
        float rJacketOuter = outerJacket.RadiusAt(xPort_mm);
        var faceCenter = new Vector3(xPort_mm, rJacketOuter, 0);
        var outward = new Vector3(0, 1, 0);

        if (!spec.IsThreaded)
        {
            // Plain drilled port: start INSIDE the plenum, exit past jacket OD.
            float rInnerWall = innerWall.RadiusAt(xPort_mm);
            float rStart = rInnerWall + tWall_mm + 0.5f;
            float rEnd = rJacketOuter + 15f;
            float length = MathF.Max(rEnd - rStart, tJacket_mm + 12f);

            var cyl = new CylinderImplicit(
                new Vector3(xPort_mm, rStart, 0),
                outward, plainPortRadius_mm, length);
            shell.BoolSubtractTemp(cyl, bounds);
            return;
        }

        // Threaded port: shoulder collar penetrates jacket by (t_jacket + 1),
        // front extension = thread length + pitch for a clean lead-in nose.
        float backPen = tJacket_mm + 1f;
        float frontExt = spec.ThreadLengthMM + spec.PitchMM;
        var built = PortGeometry.Build(bounds, spec, faceCenter, outward, backPen, frontExt);
        shell.BoolSubtract(built.BoreSubtract);
        shell.BoolAdd(built.BossAdd);
    }

    /// <summary>
    /// Add an axial propellant port through the injector flange at a given
    /// offset. Threaded nipple faces upstream (-X) into the feed line.
    /// </summary>
    private static void AddAxialPropellantPort(
        Voxels flange,
        BBox3 bounds,
        Vector3 faceOnBackSide,          // (xStart, yOffset, 0) — back face of flange
        float plainPortRadius_mm,
        float flangeThickness_mm,
        PortSpec spec)
    {
        if (!spec.IsThreaded)
        {
            // Plain drilled bore through the flange — subtract a cylinder that
            // spans the full flange thickness with small overhang.
            var cyl = new CylinderImplicit(
                new Vector3(faceOnBackSide.X - 1f, faceOnBackSide.Y, faceOnBackSide.Z),
                new Vector3(1, 0, 0),
                plainPortRadius_mm,
                flangeThickness_mm + 2f);
            flange.BoolSubtractTemp(cyl, bounds);
            return;
        }

        // Threaded boss protruding from the back (upstream, -X) face of the
        // flange. backPen slightly less than flange thickness so neither the
        // collar (BossDia) nor its 0.3mm overhang reaches the chamber-facing
        // face; the bore still punches through the remaining ~0.5mm wall.
        var axis = new Vector3(-1, 0, 0);           // outward = upstream
        var faceCenter = faceOnBackSide;            // back face of flange
        float backPen = MathF.Max(flangeThickness_mm - 0.5f, 1f);
        float frontExt = spec.ThreadLengthMM + spec.PitchMM;
        var built = PortGeometry.Build(bounds, spec, faceCenter, axis, backPen, frontExt);
        flange.BoolSubtract(built.BoreSubtract);
        flange.BoolAdd(built.BossAdd);
    }

    public static string ExportStl(Voxels vox, string path)
        => ExportStlProfiled(vox, path).Message;

    /// <summary>
    /// Same as ExportStl but returns the meshing / write wall-clock
    /// split and the triangle count. The StlExporter subprocess and
    /// the Benchmarks console app consume these numbers to emit BENCH
    /// lines; the interactive Program.cs fast-path exporter keeps
    /// using the string-returning overload because it only needs the
    /// status message.
    /// </summary>
    public static ExportStlResult ExportStlProfiled(Voxels vox, string path)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var mesh = new Mesh(vox);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        mesh.SaveToStlFile(path);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

        int triCount = (int)mesh.nTriangleCount();
        long bytes = new System.IO.FileInfo(path).Length;
        string msg = $"Exported STL ({triCount:N0} triangles) → {path}";
        return new ExportStlResult(
            Message:       msg,
            Meshing_ms:    BuildProfiler.TicksToMs(t1 - t0),
            StlWrite_ms:   BuildProfiler.TicksToMs(t2 - t1),
            TriangleCount: triCount,
            StlBytes:      bytes);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Full-fidelity flange helpers shared between monolithic Build()
    //  and ChamberAxialTileBuilder.BuildTile(). Semantics preserved
    //  from the pre-refactor inline code verbatim; the lifts here are
    //  purely call-site consolidation so tiled mode produces the same
    //  flange geometry as monolithic.
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the injector flange onto <paramref name="shell"/>. Includes:
    /// (a) solid flange disc sized to the greater of
    ///     <c>ChamberRadius × InjectorFlangeOuterRadiusFactor</c> and
    ///     <c>maxOuterContour + 2 mm</c>,
    /// (b) two axial propellant ports (LOX +Y, fuel -Y) at ±0.5 × chamber
    ///     radius, threaded when <paramref name="propPortSpec"/> is
    ///     threaded (plain drilled otherwise),
    /// (c) 6-bolt circle clearance holes at <c>flangeR - 5 mm</c>,
    ///     batched via an N→1 BoolSubtract optimisation.
    /// </summary>
    internal static void AddInjectorFlangeFull(
        Voxels                  shell,
        BBox3                   bounds,
        ChamberBuildOptions     opt,
        Chamber.ChamberContour  contour,
        PortSpec                propPortSpec,
        double                  maxOuterContour_mm)
    {
        float xStart    = -(float)opt.InjectorFlangeThickness_mm;
        float thickness =  (float)opt.InjectorFlangeThickness_mm;
        float flangeR   = (float)Math.Max(
            contour.ChamberRadius_mm * opt.InjectorFlangeOuterRadiusFactor,
            maxOuterContour_mm + 2.0);   // >= jacket OD at injector end

        // Solid disc
        var flangeDisc = new DiscImplicit(xStart, thickness, flangeR);
        var flangeVox  = LibraryScope.MakeVoxels(flangeDisc, bounds);

        // Two axial propellant ports (LOX +Y, fuel -Y) — threaded when spec
        // selected. Propellant ports face UPSTREAM (-X) into feed lines.
        float yOffset = (float)(contour.ChamberRadius_mm * 0.5);
        AddAxialPropellantPort(flangeVox, bounds,
            new Vector3(xStart, yOffset, 0),
            (float)(opt.PropellantPortDiameter_mm * 0.5),
            thickness,
            propPortSpec);
        AddAxialPropellantPort(flangeVox, bounds,
            new Vector3(xStart, -yOffset, 0),
            (float)(opt.PropellantPortDiameter_mm * 0.5),
            thickness,
            propPortSpec);

        // Bolt circle — 6 clearance holes around perimeter. Union the
        // implicit SDFs (not the voxels) so we voxelize once instead
        // of 6×, then a single BoolSubtract. Same A − (B ∪ C ∪ …)
        // result, ~6× fewer voxel-grid allocations per flange.
        float boltRad     = 2.0f;
        float boltCircleR = flangeR - 5.0f;
        if (boltCircleR > contour.ChamberRadius_mm + 3f)
        {
            var bolts = new IImplicit[6];
            for (int i = 0; i < 6; i++)
            {
                double theta = 2.0 * Math.PI * i / 6.0;
                float yB = boltCircleR * MathF.Cos((float)theta);
                float zB = boltCircleR * MathF.Sin((float)theta);
                bolts[i] = new CylinderImplicit(
                    new Vector3(xStart - 1f, yB, zB),
                    new Vector3(1, 0, 0),
                    boltRad,
                    thickness + 2f);
            }
            flangeVox.BoolSubtractTemp(new UnionImplicit(bolts), bounds);
        }

        shell.BoolAdd(flangeVox);
    }

    /// <summary>
    /// Build the mounting flange onto <paramref name="shell"/>. Includes:
    /// (a) solid flange disc sized to the greater of
    ///     <c>ExitRadius + OuterJacketThickness + FlangeMarginRadius</c>
    ///     and <c>maxOuterContour + 2 mm</c>,
    /// (b) exit bore subtracted to keep the nozzle exit open,
    /// (c) bolt circle from the preset (<see cref="MountingFlangePresets"/>)
    ///     batched via an N→1 BoolSubtract.
    /// </summary>
    internal static void AddMountingFlangeFull(
        Voxels                  shell,
        BBox3                   bounds,
        ChamberBuildOptions     opt,
        Chamber.ChamberContour  contour,
        double                  maxOuterContour_mm)
    {
        float thickness = (float)opt.MountingFlangeThickness_mm;
        float xStart    = (float)contour.TotalLength_mm;
        var   spec      = MountingFlangePresets.SpecFor(opt.MountingFlangeStandard);
        float flangeR   = (float)Math.Max(
            contour.ExitRadius_mm + opt.OuterJacketThickness_mm + spec.FlangeMarginRadius_mm,
            maxOuterContour_mm + 2.0);

        var mountDisc = new DiscImplicit(xStart, thickness, flangeR);
        var mountVox  = LibraryScope.MakeVoxels(mountDisc, bounds);

        // Subtract the exit throat (open exit)
        var exitBore = new CylinderImplicit(
            new Vector3(xStart - 1f, 0, 0),
            new Vector3(1, 0, 0),
            (float)(contour.ExitRadius_mm + 0.5),
            thickness + 2f);
        mountVox.BoolSubtractTemp(exitBore, bounds);

        // Bolt pattern from preset. Union implicit SDFs and voxelize once.
        float boltRad     = (float)(spec.BoltDiameter_mm * 0.5);
        float boltCircleR = flangeR - (float)spec.BoltCircleInset_mm;
        if (boltCircleR > contour.ExitRadius_mm + opt.OuterJacketThickness_mm + 3f)
        {
            int n = Math.Max(spec.BoltCount, 2);
            var bolts = new IImplicit[n];
            for (int i = 0; i < n; i++)
            {
                double theta = spec.StartAngle_rad + 2.0 * Math.PI * i / n;
                float yB = boltCircleR * MathF.Cos((float)theta);
                float zB = boltCircleR * MathF.Sin((float)theta);
                bolts[i] = new CylinderImplicit(
                    new Vector3(xStart - 1f, yB, zB),
                    new Vector3(1, 0, 0),
                    boltRad,
                    thickness + 2f);
            }
            mountVox.BoolSubtractTemp(new UnionImplicit(bolts), bounds);
        }

        shell.BoolAdd(mountVox);
    }

    /// <summary>
    /// Hot-fire readiness Item 6 (#260, 2026-04-30): build the test-
    /// stand thrust-takeout adapter onto <paramref name="shell"/>.
    /// Mirrors <see cref="AddMountingFlangeFull"/>'s shape so both
    /// helpers compose the same way under tiled + monolithic builds.
    ///
    /// Adapter geometry is delegated to
    /// <see cref="ThrustTakeoutAdapterGeometry.AddAdapterFull"/>; this
    /// helper resolves the chamber-aware xStart and outer-diameter
    /// defaults, then hands the spec record off.
    /// </summary>
    internal static void AddThrustTakeoutAdapterFull(
        Voxels                  shell,
        BBox3                   bounds,
        ChamberBuildOptions     opt,
        Chamber.ChamberContour  contour,
        double                  maxOuterContour_mm)
    {
        // Adapter top face sits flush against the mounting-flange
        // aft face. Mounting flange spans
        //   x ∈ [TotalLength, TotalLength + flangeThickness];
        // adapter top sits at TotalLength + flangeThickness.
        float xStart = (float)(contour.TotalLength_mm + opt.MountingFlangeThickness_mm);

        // Mounting-flange OD (recomputed identically to AddMountingFlangeFull).
        var mountSpec = MountingFlangePresets.SpecFor(opt.MountingFlangeStandard);
        double flangeOD_mm = 2.0 * Math.Max(
            contour.ExitRadius_mm + opt.OuterJacketThickness_mm + mountSpec.FlangeMarginRadius_mm,
            maxOuterContour_mm + 2.0);

        double resolvedOD = ThrustTakeoutAdapterGeometry.ResolveOuterDiameter(
            opt.ThrustTakeoutOuterDiameter_mm, flangeOD_mm);

        var spec = new ThrustTakeoutAdapterSpec(
            OuterDiameter_mm:                resolvedOD,
            Height_mm:                       opt.ThrustTakeoutAdapterHeight_mm,
            MountStandard:                   opt.ThrustTakeoutMountStandard,
            UmbilicalPassThroughCount:       opt.ThrustTakeoutUmbilicalPassThroughCount,
            UmbilicalPassThroughDiameter_mm: opt.ThrustTakeoutUmbilicalPassThroughDiameter_mm);

        ThrustTakeoutAdapterGeometry.AddAdapterFull(
            shell, bounds, xStart, (float)contour.ExitRadius_mm, spec);
    }

    // ───────────────────────────────────────────────────────────────────
    //  Follow-on helpers (umbilical / purge / gimbal)
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cut an annular seal groove + 4-bolt clearance pattern around
    /// one propellant port on the back face of the injector flange.
    /// The groove is formed by subtracting a wider cylinder, then
    /// BoolAdd-ing back the inner core to leave a flat annular trough
    /// at <paramref name="grooveDepth"/>. Bolt holes are unioned then
    /// subtracted in one kernel dispatch.
    /// </summary>
    private static void AddUmbilicalSealAndBolts(
        Voxels flange, BBox3 bounds,
        Vector3 portCenter,
        float boreR_mm,
        float grooveDepth,
        float grooveWidth,
        float boltR_mm,
        float boltCircleR,
        float flangeThickness_mm)
    {
        if (grooveDepth < 0.2f || grooveWidth < 0.2f) return;
        float xBack = portCenter.X;                             // back (upstream) face of flange
        float innerR = boreR_mm + 1.5f;                         // 1.5 mm meat between bore and groove
        float outerR = innerR + grooveWidth;
        float depthSafe = MathF.Min(grooveDepth, flangeThickness_mm * 0.5f);

        // Annular trough: subtract big disc, add inner disc back.
        var bigDisc = new CylinderImplicit(
            new Vector3(xBack - 0.1f, portCenter.Y, portCenter.Z),
            new Vector3(1, 0, 0),
            outerR,
            depthSafe + 0.2f);
        flange.BoolSubtractTemp(bigDisc, bounds);

        var innerDisc = new CylinderImplicit(
            new Vector3(xBack - 0.1f, portCenter.Y, portCenter.Z),
            new Vector3(1, 0, 0),
            innerR,
            depthSafe + 0.2f);
        flange.BoolAddTemp(innerDisc, bounds);

        // 4 bolt holes at 45° / 135° / 225° / 315° on the bolt circle.
        // Union implicit SDFs, voxelize once.
        var bolts = new IImplicit[4];
        for (int i = 0; i < 4; i++)
        {
            double th = System.Math.PI * 0.25 + i * System.Math.PI * 0.5;
            float yB = portCenter.Y + boltCircleR * MathF.Cos((float)th);
            float zB = portCenter.Z + boltCircleR * MathF.Sin((float)th);
            bolts[i] = new CylinderImplicit(
                new Vector3(xBack - 1f, yB, zB),
                new Vector3(1, 0, 0),
                boltR_mm,
                flangeThickness_mm + 2f);
        }
        flange.BoolSubtractTemp(new UnionImplicit(bolts), bounds);
    }

    /// <summary>
    /// Add a pair (or pair-of-pairs) of cylindrical trunnion lugs aft
    /// of the mount flange, each with a pin hole drilled radially
    /// through it. Two-lug variant gives a pin-joint gimbal (single
    /// axis); four-lug variant gives a Cardan yoke (two axes). Pin-hole
    /// bores use the batched N→1 BoolSubtract pattern.
    /// </summary>
    private static void AddGimbalLugPair(
        Voxels outer, BBox3 bounds,
        float xFlangeAft, float lugCenterR, float lugR, float lugLen, float pinR,
        bool fourLugs)
    {
        // Lug centerlines along ±Y (and optionally ±Z for Cardan).
        int nLugs = fourLugs ? 4 : 2;
        Voxels? lugUnion = null;
        Voxels? pinUnion = null;
        for (int i = 0; i < nLugs; i++)
        {
            // 0°, 180° (pin joint); plus 90°, 270° (Cardan).
            double th = i * (fourLugs ? System.Math.PI * 0.5 : System.Math.PI);
            float yC = lugCenterR * MathF.Cos((float)th);
            float zC = lugCenterR * MathF.Sin((float)th);

            // Lug body — solid cylinder projecting +X (aft) from the flange.
            var lug = new CylinderImplicit(
                new Vector3(xFlangeAft - 0.1f, yC, zC),
                new Vector3(1, 0, 0),
                lugR,
                lugLen + 0.2f);
            var lv = LibraryScope.MakeVoxels(lug, bounds);
            if (lugUnion is null) lugUnion = lv;
            else lugUnion.BoolAdd(lv);

            // Pin hole — through-hole perpendicular to the lug centerline,
            // along the direction normal to the lug-axis radius vector.
            // For a lug at (yC, zC), the pin axis is (-zC, yC) normalised.
            float r = MathF.Sqrt(yC * yC + zC * zC);
            float pinAxisY = r > 1e-3f ? -zC / r : 0f;
            float pinAxisZ = r > 1e-3f ?  yC / r : 1f;
            float pinLen = lugR * 4f;     // generous — guarantees through
            float xPinCenter = xFlangeAft + lugLen * 0.5f;
            // Centre the pin axis on the lug's mid-length.
            float startY = yC - 0.5f * pinLen * pinAxisY;
            float startZ = zC - 0.5f * pinLen * pinAxisZ;
            var pin = new CylinderImplicit(
                new Vector3(xPinCenter, startY, startZ),
                new Vector3(0, pinAxisY, pinAxisZ),
                pinR,
                pinLen);
            var pv = LibraryScope.MakeVoxels(pin, bounds);
            if (pinUnion is null) pinUnion = pv;
            else pinUnion.BoolAdd(pv);
        }
        if (lugUnion is not null) outer.BoolAdd(lugUnion);
        if (pinUnion is not null) outer.BoolSubtract(pinUnion);
    }

    /// <summary>
    /// Four thin cylindrical "flexure arms" in cruciform (+Y, −Y, +Z,
    /// −Z), aft-projecting from the mount flange. Approximates a
    /// Merlin-style flat-bar flexure with round bars — visual MVP;
    /// exact rectangular cross-section needs a `BoxImplicit` primitive
    /// PicoGK doesn't currently expose.
    /// </summary>
    private static void AddGimbalFlexureCross(
        Voxels outer, BBox3 bounds,
        float xFlangeAft, float armOffsetR, float armR, float armLen)
    {
        Voxels? armUnion = null;
        for (int i = 0; i < 4; i++)
        {
            double th = i * System.Math.PI * 0.5;
            float yC = armOffsetR * MathF.Cos((float)th);
            float zC = armOffsetR * MathF.Sin((float)th);
            var arm = new CylinderImplicit(
                new Vector3(xFlangeAft - 0.1f, yC, zC),
                new Vector3(1, 0, 0),
                armR,
                armLen + 0.2f);
            var av = LibraryScope.MakeVoxels(arm, bounds);
            if (armUnion is null) armUnion = av;
            else armUnion.BoolAdd(av);
        }
        if (armUnion is not null) outer.BoolAdd(armUnion);
    }

    private static double InterpChannelHeight(
        double x_mm, double x_cham, double x_throat, double x_exit, ChannelSchedule c)
    {
        if (x_mm <= x_throat)
        {
            double t = Math.Clamp((x_mm - x_cham) / Math.Max(x_throat - x_cham, 1e-6), 0, 1);
            return c.ChannelHeightAtChamber_mm + t * (c.ChannelHeightAtThroat_mm - c.ChannelHeightAtChamber_mm);
        }
        double t2 = Math.Clamp((x_mm - x_throat) / Math.Max(x_exit - x_throat, 1e-6), 0, 1);
        return c.ChannelHeightAtThroat_mm + t2 * (c.ChannelHeightAtExit_mm - c.ChannelHeightAtThroat_mm);
    }
}
