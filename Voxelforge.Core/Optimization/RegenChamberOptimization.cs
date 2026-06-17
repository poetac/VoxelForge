// RegenChamberOptimization.cs — Geometry generation, throat sizing, scoring,
// and packing/unpacking of design variables for the optimizer.
//
// Responsibilities:
//   • Compute throat diameter from (thrust, P_c, MR, efficiencies) so the
//     user-chosen operating point is always hit.
//   • Generate(design) → builds contour, voxels, and derived values.
//   • Evaluate(design, solver, structure) → produces a RegenScoreResult.
//   • Pack/Unpack between RegenChamberDesign and double[] (for SA optimizer).
//   • Scoring profiles (Balanced, MinWallT, MinDP, MaxIsp, MinMaterial).
//
// Scoring is additive; every term is normalized so a weight of 1 gives
// roughly unit contribution at a "typical" design, allowing profiles to
// mix terms without rescaling.

using Voxelforge.Analysis;
using Voxelforge.Chamber;
using Voxelforge.Combustion;
using Voxelforge.Combustion.Stability;
using Voxelforge.Geometry;
using Voxelforge.HeatTransfer;
using Voxelforge.Injector;
using Voxelforge.Manufacturing;
using Voxelforge.Structure;

namespace Voxelforge.Optimization;

// StructuralConfidence enum + RegenGenerationResult, RegenScoreResult,
// and ScoringProfile records were extracted to dedicated files in
// Voxelforge.Core/Optimization/ as part of the Core
// extraction (A1). They live in this same namespace so consumers don't
// need any using changes.

public static class RegenChamberOptimization
{
    // OOB-13 / E-D nozzle (issue #213): Angelino-geometry fixed inner/outer
    // ratio at the annular throat (R_plug_tip / R_cowl = 0.40). The cowl
    // throat radius R_cowl satisfies π(R_cowl² − R_plug²) = A_t, yielding
    //   R_cowl = R_t / √(1 − 0.40²) where R_t = √(A_t/π).
    // The constant ~1.0911 inflates the contour throat radius for E-D designs.
    private const double EdPlugInnerOuterRatio = 0.40;
    private static readonly double EdCowlRadiusMultiplier =
        1.0 / System.Math.Sqrt(1.0 - EdPlugInnerOuterRatio * EdPlugInnerOuterRatio);

    public static readonly ScoringProfile[] Profiles =
    {
        new("Balanced",         WallTPenalty: 5.0, WallTAvg: 0.01,   DPWeight: 2.0, MassWeight: 0.002, FeatureWeight: 20, StructuralWeight: 5.0, CoolantTWeight: 0.005),
        new("Min Wall T",       WallTPenalty: 20.0, WallTAvg: 0.05,   DPWeight: 0.5, MassWeight: 0.001, FeatureWeight: 20, StructuralWeight: 2.0, CoolantTWeight: 0.002),
        new("Min Pressure Drop", WallTPenalty: 3.0, WallTAvg: 0.005, DPWeight: 10.0,MassWeight: 0.001, FeatureWeight: 20, StructuralWeight: 2.0, CoolantTWeight: 0.002),
        new("Min Material",     WallTPenalty: 3.0,  WallTAvg: 0.005, DPWeight: 1.0, MassWeight: 0.020, FeatureWeight: 20, StructuralWeight: 3.0, CoolantTWeight: 0.001),
        new("Max Isp Path",     WallTPenalty: 4.0,  WallTAvg: 0.005, DPWeight: 3.0, MassWeight: 0.001, FeatureWeight: 20, StructuralWeight: 2.0, CoolantTWeight: 0.010),
        // SPRINT 1.3: rewards injector ratios that stay in classical mixing
        // bands (v_fuel/v_ox in [0.5, 4.0], momentum ratio in [0.6, 1.5])
        // on top of normal balanced-like chamber scoring.
        new("Max Injector Uniformity",
                                 WallTPenalty: 10.0, WallTAvg: 0.01,  DPWeight: 2.0, MassWeight: 0.002, FeatureWeight: 20, StructuralWeight: 5.0, CoolantTWeight: 0.005,
                                 InjectorRatioWeight: 5.0),
    };

    // ─────────────────────────────────────────────────────────────────
    //  Optimizer variable space — SSOT lives in [SaDesignVariable]
    //  attributes on RegenChamberDesign + InjectorPattern (ADR-010).
    //  Live count + bounds are derivable via
    //  DesignVariableRegistry.DescriptorsForMany / BoundsForMany;
    //  ADR-012 documents the "add a variable" workflow.
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// SA search-vector bounds, derived from
    /// <see cref="DesignVariableRegistry.BoundsForMany"/> against the two
    /// SA-visible types (<see cref="RegenChamberDesign"/> +
    /// <see cref="Injector.InjectorPattern"/>). The registry aggregates
    /// every <see cref="SaDesignVariableAttribute"/>-tagged member into
    /// a single index-ordered array — adding a new variable is a
    /// one-line attribute annotation per ADR-010 / ADR-012.
    /// </summary>
    public static readonly (double Min, double Max)[] Bounds =
        DesignVariableRegistry.BoundsForMany(
            typeof(RegenChamberDesign),
            typeof(Injector.InjectorPattern));

    /// <summary>
    /// Sprint 7 Track C (2026-04-22) — registry-driven Pack. Iterates
    /// the DesignVariableRegistry (aggregating RegenChamberDesign +
    /// InjectorPattern attributes) and reads each dim's value from the
    /// matching source via reflection. Behaviour-equivalent to the
    /// former hand-coded path; the only observable difference is that
    /// OuterRowFilmFraction's "pattern is null" fallback now returns
    /// the record default (0.0) rather than the legacy hand-coded 0.05.
    /// The fallback is never applied by Unpack (the gate rejects it
    /// when the baseline has no pattern), so no SA-visible behaviour
    /// is affected — the drift-guard test confirms it.
    /// </summary>
    public static double[] Pack(RegenChamberDesign d)
        => DesignVariableBinder.Pack(d, d.InjectorElementPattern);

    /// <summary>
    /// Sprint 7 Track C (2026-04-22) — registry-driven Unpack.
    /// Clamps each sampled value to the descriptor's attribute bounds
    /// (same bounds that drive SA sampling) and applies it only when
    /// the descriptor's <see cref="SaGate"/> matches the baseline's
    /// categorical state. Pre-refactor, the hand-coded path applied
    /// slightly wider per-dim clamps than the attribute bounds — those
    /// wider clamps were a legacy safety valve for early SA samplers
    /// that could exceed bounds; the current sampler respects bounds,
    /// so the tighter clamp is a pure safety upgrade.
    /// </summary>
    public static RegenChamberDesign Unpack(double[] p, RegenChamberDesign baseline)
        => DesignVariableBinder.Unpack(p, baseline);

    /// <summary>
    /// Span-input overload of <see cref="Unpack(double[], RegenChamberDesign)"/>.
    /// Reads the candidate vector directly out of a
    /// <see cref="ReadOnlySpan{Double}"/> with no intermediate
    /// <c>double[]</c> allocation — the SA hot path's IObjective
    /// implementations call this from inside <c>Evaluate</c> to keep
    /// per-candidate evaluation allocation-free. Behaviour is otherwise
    /// byte-identical to the array overload.
    /// </summary>
    public static RegenChamberDesign Unpack(ReadOnlySpan<double> p, RegenChamberDesign baseline)
        => DesignVariableBinder.Unpack(p, baseline);

    // ─────────────────────────────────────────────────────────────────
    //  Generate full geometry + physics for one design
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compose a <see cref="Geometry.ChamberBuildOptions"/>
    /// from the operating conditions + design in the same way
    /// <see cref="GenerateWith"/> does internally, but exposed publicly so
    /// the tiled-build dispatcher in <c>Program.cs</c> (and the
    /// <c>Voxelforge.Benchmarks</c> console harness) can hand
    /// the same options bundle to <c>ChamberAxialTileBuilder.BuildTiled</c>.
    /// Kept deliberately minimal — same field set the tile builder
    /// actually reads. Physics-only / injector-bore / umbilical / gimbal
    /// sub-features stay at their record defaults so the tile builder
    /// produces a shell + channels + manifolds + flanges result matching
    /// monolithic <see cref="Geometry.ChamberVoxelBuilder.Build"/>'s
    /// configuration on the same inputs.
    /// </summary>
    public static Geometry.ChamberBuildOptions ComposeChamberBuildOptions(
        OperatingConditions cond, RegenChamberDesign design)
    {
        var gas     = Combustion.PropellantTables.Lookup(
            cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
        var derived = ComputeDerived(cond, gas, design);

        var contour = Chamber.ChamberContourGenerator.Generate(
            throatRadius_mm:          derived.ThroatRadius_mm,
            contractionRatio:         design.ContractionRatio,
            expansionRatio:           design.ExpansionRatio,
            characteristicLength_m:   design.CharacteristicLength_m,
            thetaN_deg:               design.BellEntranceAngle_deg,
            thetaE_deg:               design.BellExitAngle_deg,
            bellLengthFraction:       design.BellLengthFraction,
            stationCount:             design.ContourStationCount,
            dualBell:                 design.IncludeDualBell,
            seaLevelExpansionRatio:   design.SeaLevelExpansionRatio,
            inflectionAngleDeg:       design.InflectionAngle_deg);

        var channels = new HeatTransfer.ChannelSchedule(
            ChannelCount:              design.ChannelCount,
            RibThickness_mm:           design.RibThickness_mm,
            GasSideWallThickness_mm:   design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm:  design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm:    design.ChannelHeightExit_mm);

        var material = HeatTransfer.WallMaterials.All[
            Math.Clamp(cond.WallMaterialIndex, 0, HeatTransfer.WallMaterials.All.Length - 1)];

        // Z1 hot-fix / Track B closed-loop (2026-04-28): build the per-station
        // wall profile from the contour so the tile-builder + analytical mass
        // path sees the same per-station thicknesses the thermal solver uses.
        int throatIdxCompose = Structure.StructuralCheck.FindThroatStationIndex(contour);
        double[] wallProfileCompose = Structure.StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount:        contour.Stations.Length,
            throatIdx:           throatIdxCompose,
            baseline_mm:         design.GasSideWallThickness_mm,
            chamberOverride_mm:  design.ChamberWallThicknessOverride_mm,
            throatOverride_mm:   design.ThroatWallThicknessOverride_mm,
            exitOverride_mm:     design.ExitWallThicknessOverride_mm);

        return new Geometry.ChamberBuildOptions(
            Contour:                         contour,
            Channels:                        channels,
            OuterJacketThickness_mm:         design.OuterJacketThickness_mm,
            IncludeManifolds:                design.IncludeManifolds,
            ManifoldLength_mm:               design.ManifoldLength_mm,
            ChannelManifoldFilletRadius_mm:  design.ChannelManifoldFilletRadius_mm,
            IncludeInletOutletPorts:         design.IncludePorts,
            PortDiameter_mm:                 design.PortDiameter_mm,
            CoolantPortStandard:             design.CoolantPortStandard,
            IncludeInjectorFlange:           design.IncludeInjectorFlange,
            InjectorFlangeThickness_mm:      design.InjectorFlangeThickness_mm,
            InjectorFlangeOuterRadiusFactor: design.InjectorFlangeOuterRadiusFactor,
            IncludeMountingFlange:           design.IncludeMountingFlange,
            MountingFlangeThickness_mm:      design.MountingFlangeThickness_mm,
            SmoothingRadius_mm:              design.SmoothingRadius_mm,
            PropellantPortDiameter_mm:       design.PropellantPortDiameter_mm,
            PropellantPortStandard:          design.PropellantPortStandard,
            MaterialForMass:                 material,
            SkipChannelGeneration:           ChannelTopologyDispatcher.ShouldSkipChannelGeneration(design.ChannelTopology),
            GasSideWallProfile_mm:           wallProfileCompose);
    }

    /// <summary>
    /// Auto-coarsening wrapper around
    /// <see cref="GenerateWith"/> that catches
    /// <see cref="Analysis.MemoryBudgetExceededException"/> and retries
    /// at the coarser voxel size the gate suggests, up to
    /// <paramref name="maxRetries"/> levels. Used by the form's Generate
    /// button and the SA FinalizeOpt path when
    /// <see cref="UI.ResourceBudget.AutoCoarsenVoxelToFitBudget"/> is true;
    /// callers that want the strict block-on-Fail behaviour (SA
    /// per-candidate evaluation, tests, subprocess export) should keep
    /// calling <see cref="GenerateWith"/> directly.
    ///
    /// Reports the actual voxel size used via the optional
    /// <paramref name="onVoxelSubstituted"/> callback so the UI can
    /// surface "Voxel auto-coarsened 0.40 → 0.85 mm to fit budget" —
    /// the user always knows when fidelity degraded. Returns the final
    /// <see cref="RegenGenerationResult"/> with
    /// <c>Geometry.Profile.VoxelSize_mm</c> stamped at whatever voxel
    /// succeeded.
    /// </summary>
    public static RegenGenerationResult GenerateWithAutoCoarsen(
        OperatingConditions cond, RegenChamberDesign design, double voxelSize_mm,
        int maxRetries = 3,
        System.Action<double, double, string>? onVoxelSubstituted = null,
        bool skipVoxelGeometry = false,
        bool skipMfgAnalysis   = false,
        // Sprint A-3 / ADR-021 (2026-04-30): voxel-build seam. Pass
        // `Voxelforge.Geometry.ChamberVoxelBuilderAdapter` from App
        // callers when `skipVoxelGeometry` is false; null + true is the
        // headless / bench-SA / unit-test path.
        IVoxelGenerator? voxelGenerator = null,
        // Sprint A-3 Phase 2 / ADR-021: aerospike + turbopump + turbine
        // generator seams. Forward to GenerateWith.
        Geometry.IAerospikeBuilder? aerospikeBuilder = null,
        Turbopump.ITurbopumpGenerator? turbopumpGenerator = null,
        Turbopump.ITurbineGenerator? turbineGenerator = null)
    {
        double v = voxelSize_mm;
        int attempt = 0;
        while (true)
        {
            try
            {
                return GenerateWith(cond, design, voxelSize_mm: v,
                                    skipVoxelGeometry: skipVoxelGeometry,
                                    skipMfgAnalysis:   skipMfgAnalysis,
                                    voxelGenerator:    voxelGenerator,
                                    aerospikeBuilder:  aerospikeBuilder,
                                    turbopumpGenerator: turbopumpGenerator,
                                    turbineGenerator:  turbineGenerator);
            }
            catch (Analysis.MemoryBudgetExceededException mem)
            {
                attempt++;
                if (attempt > maxRetries
                    || double.IsNaN(mem.SuggestedVoxel_mm)
                    || mem.SuggestedVoxel_mm <= v)
                {
                    // No further retry helps — rethrow. The caller's
                    // MemoryBudgetExceededException catch can still
                    // surface the suggested voxel to the user; we just
                    // couldn't substitute it automatically.
                    throw;
                }
                double previous = v;
                v = mem.SuggestedVoxel_mm;
                onVoxelSubstituted?.Invoke(previous, v, mem.Message);
            }
        }
    }

    [Deterministic]
    public static RegenGenerationResult GenerateWith(
        OperatingConditions cond, RegenChamberDesign design, double voxelSize_mm = 0.0,
        bool skipVoxelGeometry = false,
        // When true, skip post-processing analyses that
        // do NOT drive any feasibility gate or SA score term. Conservative
        // skip set today (other analyses are required by gates):
        //   • ResidualStressAnalysis — always-on, ~5 ms, only consumed by
        //     report/UI panels. Pure inherent-strain math, no gate impact.
        //   • Gimbal-mount StructuralConfidence preview demotion — purely
        //     cosmetic (the High/Medium/Low pill shown in the UI). The
        //     mount evaluation itself still runs because reports read it.
        // The parallel SA batch in Program.StepOpt passes true to suppress
        // these on every non-best candidate. The eager `Generate(...)` /
        // single-call path defaults to false so manual / report invocations
        // still get the full picture.
        bool skipMfgAnalysis = false,
        // P7 (2026-04-29): pre-computed AerospikeBuildResult to short-
        // circuit the redundant aerospike-physics solve. When provided AND
        // `design.ChannelTopology` is an aerospike topology, the cached
        // value is stamped onto the result instead of calling
        // `AerospikeOptimization.BuildAndEvaluate` again. Saves the ~50-200 ms
        // duplicate `MonolithicEngineBuilder.BuildAerospikeCore` paid on
        // every monolithic aerospike export. Determinism preserved —
        // propellant tables + contour are pure functions of (cond, design).
        // Default null = legacy behaviour (recompute).
        Geometry.AerospikeBuildResult? cachedAerospikeResult = null,
        // Sprint A-3 / ADR-021 (2026-04-30): voxel-build seam. Pass
        // `Voxelforge.Geometry.ChamberVoxelBuilderAdapter` from App
        // callers needing the full PicoGK voxel build; null + the
        // existing `skipVoxelGeometry: true` is the headless / bench-SA
        // / unit-test path. When `skipVoxelGeometry` is false AND
        // `voxelGenerator` is null, the orchestrator falls back to
        // `AnalyticalOnlyVoxelGenerator.Instance` whose `Build` throws
        // `InvalidOperationException` — making the missing-adapter
        // mistake loud rather than silently producing wrong voxel data.
        IVoxelGenerator? voxelGenerator = null,
        // Sprint A-3 Phase 2 / ADR-021: aerospike + turbopump +
        // turbine generator seams. App callers needing aerospike or
        // turbopump geometry attached to the result pass the adapters
        // from Voxels; headless / non-aerospike / non-turbopump
        // callers pass null and the orchestrator skips that branch.
        Geometry.IAerospikeBuilder? aerospikeBuilder = null,
        Turbopump.ITurbopumpGenerator? turbopumpGenerator = null,
        Turbopump.ITurbineGenerator? turbineGenerator = null)
    {
        // Hard-fail on unsupported propellant/coolant.
        // Throws UnsupportedPropellantException with a structured Code.
        PropellantValidation.EnsureSupported(cond.PropellantPair);

        var gas = PropellantTables.Lookup(cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);

        // Throat diameter from thrust target
        var derived = ComputeDerived(cond, gas, design);

        // OOB-9 (issue #344): finite-rate Isp correction.
        // Multiplies IdealIspVacuum_s + IdealIspSeaLevel_s by the CEA-calibrated
        // dissociation factor for (pair, Pc, MR). Default false → factor = 1.0,
        // derived is unchanged, behaviour is bit-identical to pre-OOB-9.
        double frFactor = cond.UseFiniteRateCorrection
            ? Combustion.FiniteRateCorrection.DissociationCorrectionFactor(
                cond.PropellantPair, cond.ChamberPressure_Pa, cond.MixtureRatio)
            : 1.0;
        if (frFactor != 1.0)
            derived = derived with
            {
                IdealIspVacuum_s   = derived.IdealIspVacuum_s   * frFactor,
                IdealIspSeaLevel_s = derived.IdealIspSeaLevel_s * frFactor,
            };

        // OOB-7 (issue #343): rotating detonation engine Isp gain.
        // Applied after finite-rate correction so both multipliers stack
        // on the deflagration-equilibrium Isp. Default RdeTopology.None
        // → no gain (rdeFactor = 1.0), behaviour bit-identical to pre-OOB-7.
        int  rdeWaveCount    = 0;
        double rdeAnnulusFillTime_us = 0.0;
        if (design.RdeTopology != Optimization.RdeTopology.None)
        {
            double rdeFactor = Combustion.RdeCombustion.IspGain(
                cond.PropellantPair, cond.ChamberPressure_Pa);
            derived = derived with
            {
                IdealIspVacuum_s   = derived.IdealIspVacuum_s   * rdeFactor,
                IdealIspSeaLevel_s = derived.IdealIspSeaLevel_s * rdeFactor,
            };
            double circumference_m = 2.0 * Math.PI * (design.RdeAnnulusOuterRadius_mm / 1000.0);
            rdeWaveCount = Combustion.RdeCombustion.DetonationWaveCount(circumference_m);
            // Injector ΔP approximated as 20 % of chamber pressure (common design rule).
            double injDp_Pa     = 0.20 * cond.ChamberPressure_Pa;
            // Mean propellant mixture density (LOX/CH4-class mixture density ≈ 700 kg/m³ as default).
            double propRho_kgm3 = 700.0;
            rdeAnnulusFillTime_us = Combustion.RdeCombustion.AnnulusFillTime_us(
                design.RdeChannelHeight_mm / 1000.0, injDp_Pa, propRho_kgm3);
        }

        // OOB-13 / E-D nozzle (2026-04-30, issue #213): for the
        // ExpansionDeflection topology the outer bell has an annular throat
        // (inner plug at 40 % of cowl radius, Angelino fixed ratio). The
        // annular area equals the standard round-throat area, so the cowl
        // radius is inflated: R_cowl = R_t / √(1 − 0.40²) ≈ 1.0911·R_t.
        // All downstream regen-bell logic (contour, solver, scoring) sees
        // R_cowl as the throat radius — correct for the outer bell wall.
        double throatRadius_mm = ChannelTopologyDispatcher.IsExpansionDeflection(design.ChannelTopology)
            ? derived.ThroatRadius_mm * EdCowlRadiusMultiplier
            : derived.ThroatRadius_mm;

        var contour = ChamberContourGenerator.Generate(
            throatRadius_mm: throatRadius_mm,
            contractionRatio: design.ContractionRatio,
            expansionRatio: design.ExpansionRatio,
            characteristicLength_m: design.CharacteristicLength_m,
            thetaN_deg: design.BellEntranceAngle_deg,
            thetaE_deg: design.BellExitAngle_deg,
            bellLengthFraction: design.BellLengthFraction,
            stationCount: design.ContourStationCount,
            dualBell: design.IncludeDualBell,
            seaLevelExpansionRatio: design.SeaLevelExpansionRatio,
            inflectionAngleDeg: design.InflectionAngle_deg);

        var channels = new ChannelSchedule(
            ChannelCount: design.ChannelCount,
            RibThickness_mm: design.RibThickness_mm,
            GasSideWallThickness_mm: design.GasSideWallThickness_mm,
            ChannelHeightAtChamber_mm: design.ChannelHeightChamber_mm,
            ChannelHeightAtThroat_mm: design.ChannelHeightThroat_mm,
            ChannelHeightAtExit_mm: design.ChannelHeightExit_mm);

        var material = WallMaterials.All[Math.Clamp(cond.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];

        // If an injector element pattern is set with a film fraction,
        // override the design's film-cooling input with that fraction.
        // All other film parameters (decay, slot height, …) stay from design.
        var effectiveFilm = design.FilmCooling;
        if (design.InjectorElementPattern is { } pat && pat.OuterRowFilmFraction > 0)
        {
            effectiveFilm = effectiveFilm with
            {
                Enabled = true,
                FuelFractionAsFilm = Math.Clamp(pat.OuterRowFilmFraction, 0.0, 0.30),
            };
        }
        // Sprint feasibility-audit-5 (2026-04-26): SA dims 24-25 override
        // film-cooling parameters when > 0. Lets SA tune film fraction +
        // slot height directly. If both 0 the existing FilmCooling record
        // (typically the AutoSeeder default) is used unchanged.
        if (design.FilmFuelFraction > 0)
        {
            effectiveFilm = effectiveFilm with
            {
                Enabled = true,
                FuelFractionAsFilm = design.FilmFuelFraction,
            };
        }
        if (design.FilmSlotHeightOverride_mm > 0)
        {
            effectiveFilm = effectiveFilm with
            {
                FilmSlotHeight_mm = design.FilmSlotHeightOverride_mm,
            };
        }

        // Regen coolant mass flow = fuel − film bleed.
        double filmFrac = effectiveFilm.Enabled ? effectiveFilm.FuelFractionAsFilm : 0;
        double coolantMassFlow = derived.FuelMassFlow_kgs * (1.0 - filmFrac);

        // Select the coolant fluid module from the pair metadata.
        var pairMeta = PropellantPairs.GetMeta(cond.PropellantPair);
        var coolantFluid = Coolant.CoolantRegistry.Get(pairMeta.CoolantFluidKey);

        double helixAngleDeg = design.ChannelTopology == ChannelTopology.Helical
            ? Math.Clamp(design.HelixPitchAngle_deg, 0.0, 45.0)
            : 0.0;

        // ChannelTopology.None suppresses
        // regen-channel + manifold + coolant-port generation so the chamber
        // becomes a pure shell for an ablative-only build.
        bool skipChannels = ChannelTopologyDispatcher.ShouldSkipChannelGeneration(design.ChannelTopology);

        // TPMS thermal branch. design.TpmsKind is
        // null for non-TPMS topologies so the solver short-circuits to the
        // pre-B1 axial/helical path bit-identically.
        var tpmsKindForSolver = design.TpmsKind;
        double tpmsCellEdge_m = design.TpmsCellEdge_mm * 1e-3;
        double tpmsSolidFraction = design.TpmsSolidFraction;

        // Z1 hot-fix / Track B closed-loop (2026-04-28): build the per-station
        // gas-side wall thickness profile from the contour BEFORE the thermal
        // solve so RegenCoolingSolver can use the per-station value at each
        // station. Pre-Z1 the wallProfile was built AFTER the solver from
        // thermal.Stations.Length, fed only into StructuralCheck +
        // ProofTestAnalysis — leaving the thermal solver and voxel builder
        // operating on the uniform baseline regardless of override values.
        // The throat index from the contour matches the solver-output throat
        // index because the thermal march doesn't change station radii.
        int throatIdxForSolve = StructuralCheck.FindThroatStationIndex(contour);
        double[] wallProfile = StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount:        contour.Stations.Length,
            throatIdx:           throatIdxForSolve,
            baseline_mm:         design.GasSideWallThickness_mm,
            chamberOverride_mm:  design.ChamberWallThicknessOverride_mm,
            throatOverride_mm:   design.ThroatWallThicknessOverride_mm,
            exitOverride_mm:     design.ExitWallThicknessOverride_mm);

        var solverInputs = new RegenSolverInputs(
            Contour: contour,
            Gas: gas,
            Wall: material,
            Channels: channels,
            CoolantMassFlow_kgs: skipChannels ? 0.0 : coolantMassFlow,
            CoolantInletTemp_K: cond.CoolantInletTemp_K,
            CoolantInletPressure_Pa: cond.CoolantInletPressure_Pa,
            Direction: CoolantFlowDirection.Counterflow,
            CoolantCorrelation: CoolantCorrelationKind.SiederTate,
            BartzScalingFactor:           Math.Clamp(cond.BartzScalingFactor,           0.2, 3.0),
            CoolantHtcScalingFactor:      Math.Clamp(cond.CoolantHtcScalingFactor,      0.3, 3.0),
            CoolantFrictionScalingFactor: Math.Clamp(cond.CoolantFrictionScalingFactor, 0.3, 3.0),
            FilmCooling: effectiveFilm,
            AxialConductionSweeps: design.AxialConductionSweeps,
            RadialWallNodes: design.RadialWallNodes,
            EnableBartzBLCorrections: design.EnableBartzBLCorrections,
            CoolantFluid: coolantFluid,
            HelixPitchAngle_deg: helixAngleDeg,
            SkipRegenMarch: skipChannels,
            TpmsKind: tpmsKindForSolver,
            TpmsCellEdge_m: tpmsCellEdge_m,
            TpmsSolidFraction: tpmsSolidFraction,
            // Sprint 33 / PH-7: thread LPBF channel roughness into Haaland.
            LpbfRelativeRoughness: Math.Max(design.LpbfRelativeRoughness, 0.0),
            GasSideWallProfile_mm: wallProfile,
            // OOB-12: transpiration cooling.
            EnableTranspirationCooling: design.EnableTranspirationCooling,
            TranspirationBleedFraction: design.TranspirationBleedFraction,
            TranspirationEfficiency:    design.TranspirationEfficiency);

        var thermal = RegenCoolingSolver.Solve(solverInputs);

        // OOB-2 Sprint 3 (2026-05-04): run the SIMP topology optimizer when
        // the topology is TopologyOptimized and the thermal solve produced
        // station data. The solver redistributes channel count proportional to
        // the local Bartz heat-flux from `thermal`, returning a per-station
        // ChannelsPerStation[] that the voxel builder will consume.
        // Short-circuits on skipChannels (ablative-only shell) so the
        // optimizer is never invoked on a design with no channel phase.
        TopologyChannelResult? topoResult = null;
        if (!skipChannels
         && ChannelTopologyDispatcher.IsTopologyOptimized(design.ChannelTopology)
         && thermal.Stations.Length == contour.Stations.Length
         && thermal.Stations.Length >= 3)
        {
            topoResult = TopologyOptimizedChannels.Solve(new TopologyChannelInputs(
                Contour:             contour.Stations,
                ThermalStations:     thermal.Stations,
                BaseSchedule:        channels,
                MassFlowCoolant_kgs: coolantMassFlow));
        }

        // Sprint feasibility-audit-G' (2026-04-27): pass outer jacket
        // thickness + hot-gas γ_throat so StructuralCheck applies multi-
        // wall hoop credit AND per-station local gas static pressure.
        // The pre-G' single-wall + constant-Pc-floor formula was reporting
        // 12.5 GPa peak hoop at the EXIT station for RL10 — purely a
        // model artefact, not real physics (real exit gas P ≈ 0.1 MPa,
        // not Pc). Both parameters default to 0 in the back-compat
        // signature, so test fixtures using the old `Evaluate(...,
        // chamberPressure_Pa)` form still get bit-identical legacy
        // behavior.
        // Track B (2026-04-27): the per-station gas-side wall thickness
        // profile was built ABOVE before the solver call (Z1 hot-fix
        // 2026-04-28) so RegenCoolingSolver also sees it. Reuse it here for
        // StructuralCheck — the throat index from the contour matches the
        // solver-output throat index because the thermal march doesn't move
        // station radii. When all three overrides are 0 the profile is
        // uniform = GasSideWallThickness_mm and StructuralCheck behaves
        // bit-identically to the pre-Track-B path.
        // A1-follow-on (2026-04-28): pass IN625 as the jacketMaterial when
        // the wall is the GRCop42_Inconel625 composite (index 4) so
        // StructuralCheck applies bi-layer composite-yield accounting:
        // GRCop-42 liner at the gas-side T_wg + IN625 jacket at the local
        // coolant bulk T, thickness-weighted. For pure single-material
        // walls (CuCrZr, GRCop42, IN625, IN718 alone), pass null so the
        // legacy single-σ_y path runs — IN625 has a *higher* σ_y than
        // CuCrZr cold so unconditionally crediting an IN625 jacket would
        // be non-conservative for a CuCrZr design.
        WallMaterial? jacketMat = cond.WallMaterialIndex == 4
            ? HeatTransfer.WallMaterials.Inconel625
            : (WallMaterial?)null;
        var stress = StructuralCheck.Evaluate(
            thermal, material, design.GasSideWallThickness_mm, cond.ChamberPressure_Pa,
            outerJacketThickness_mm: design.OuterJacketThickness_mm,
            gasGamma:                gas.GammaThroat,
            gasSideWallProfile_mm:   wallProfile,
            jacketMaterial:          jacketMat,
            gimbalOffset_m:          cond.GimbalOffset_mm * 1e-3);  // Sprint C #350

        InjectorFaceImportOptions? stlOpt = null;
        if (design.IncludeInjectorSTL && !string.IsNullOrWhiteSpace(design.InjectorSTLPath))
        {
            stlOpt = new InjectorFaceImportOptions(
                StlPath: design.InjectorSTLPath,
                Enabled: true,
                OffsetX_mm: design.InjectorSTLOffsetX_mm,
                UniformScale: design.InjectorSTLScale,
                AutoCenterYZ: design.InjectorSTLAutoCenter);
        }

        // SPRINT 1.1: compute the effective injector ΔP fraction up front.
        // Pulled from the pattern when set (default 0.20), clamped to [0.05, 0.50].
        // The same value feeds: (a) the pattern sizing call below so orifice
        // area and flow target are self-consistent, (b) the voxel builder via
        // InjectorFlowContext so element bores match the same ΔP, and (c) the
        // combustion-stability chug check via InjectorState. Before this
        // upgrade stability was always told "20 %" regardless of what the
        // pattern asked for, so the optimizer could not see stability drift.
        double dPInjFrac = Math.Clamp(
            design.InjectorElementPattern?.DeltaPInjFraction ?? 0.20, 0.05, 0.50);
        double dPInj_Pa  = dPInjFrac * cond.ChamberPressure_Pa;

        // Size injector elements if a pattern is set and the element type is
        // implemented. Stubs (Pintle, Showerhead, Swirl) skip sizing.
        PatternSizingResult? injectorSizing = null;
        if (design.InjectorElementPattern is { } injPat && injPat.Element.IsImplemented)
        {
            // Sprint feasibility-audit-H2 (2026-04-27): override-style
            // SA dims 26 + 27 promote the pintle post diameter and
            // sleeve hole count from fixed seed-only values to SA-
            // tunable knobs. Default 0 = "use whatever the InjectorPattern
            // had" (pre-Sprint-H2 behavior); positive value = SA-supplied
            // override. Only applied when the pattern's element type is
            // Pintle — the override fields are ignored for every other
            // element type. PintleSleeveHoleCount is round-to-nearest-int
            // because the SA vector is a continuous double[] but the
            // physical knob is integer.
            var effectivePat = injPat;
            if (effectivePat.ElementType == "Pintle")
            {
                if (design.PintleDiameterOverride_mm > 0)
                    effectivePat = effectivePat with { PintleDiameter_mm = design.PintleDiameterOverride_mm };
                if (design.PintleSleeveHoleCountOverride > 0)
                    effectivePat = effectivePat with
                    {
                        PintleSleeveHoleCount = (int)Math.Round(design.PintleSleeveHoleCountOverride),
                    };
            }
            var (oxRho, fuelRho) = OrificeModel.InjectionDensities(cond.PropellantPair);
            injectorSizing = effectivePat.SizePattern(
                derived.OxidizerMassFlow_kgs,
                derived.FuelMassFlow_kgs,
                dPInj_Pa,
                oxRho,
                fuelRho);
        }

        var buildOpts = new ChamberBuildOptions(
            Contour: contour,
            Channels: channels,
            OuterJacketThickness_mm: design.OuterJacketThickness_mm,
            ManifoldLength_mm: design.ManifoldLength_mm,
            IncludeManifolds: design.IncludeManifolds,
            IncludeInletOutletPorts: design.IncludePorts,
            PortDiameter_mm: design.PortDiameter_mm,
            CoolantPortStandard: design.CoolantPortStandard,
            IncludeInjectorFlange: design.IncludeInjectorFlange,
            InjectorFlangeThickness_mm: design.InjectorFlangeThickness_mm,
            InjectorFlangeOuterRadiusFactor: design.InjectorFlangeOuterRadiusFactor,
            PropellantPortDiameter_mm: design.PropellantPortDiameter_mm,
            PropellantPortStandard: design.PropellantPortStandard,
            IncludeMountingFlange: design.IncludeMountingFlange,
            MountingFlangeThickness_mm: design.MountingFlangeThickness_mm,
            MountingFlangeStandard: design.MountingFlangeStandard,
            SmoothingRadius_mm: design.SmoothingRadius_mm,
            ChannelManifoldFilletRadius_mm: design.ChannelManifoldFilletRadius_mm,
            MaterialForMass: material,
            InjectorFaceSTL: stlOpt,
            InjectorElementPattern: design.InjectorElementPattern,
            InjectorFlowCtx: design.InjectorElementPattern != null
                ? new InjectorFlowContext(
                    TotalOxFlow_kgs:      derived.OxidizerMassFlow_kgs,
                    TotalFuelFlow_kgs:    derived.FuelMassFlow_kgs,
                    ChamberPressure_Pa:   cond.ChamberPressure_Pa,
                    PropellantPair:       cond.PropellantPair,
                    DeltaPInjFraction:    dPInjFrac)
                : null,
            HelixPitchAngle_deg: helixAngleDeg,
            IncludeCoolantCrossover:      design.IncludeCoolantCrossover,
            CoolantCrossoverDiameter_mm:  design.CoolantCrossoverDiameter_mm,
            SensorBosses:                 design.SensorBosses,
            IgniterType:                  design.IgniterType,
            IgniterRadialFraction:        design.IgniterRadialFraction,
            FuelDomeDepth_mm:             design.FuelDomeDepth_mm,
            OxDomeDepth_mm:               design.OxDomeDepth_mm,
            IncludeAntiVortexBaffle:      design.IncludeAntiVortexBaffle,
            SkipChannelGeneration:        skipChannels,
            // Propagate the purge / umbilical / gimbal fields
            // so ChamberVoxelBuilder.Build can draw the umbilical groove,
            // purge bores, and gimbal trunnions/flexures.
            UmbilicalStandard:            cond.UmbilicalStandard,
            PurgePorts:                   design.PurgePorts,
            MountConfiguration:           design.MountConfiguration,
            // Z1 hot-fix / Track B closed-loop (2026-04-28): reuse the same
            // per-station wallProfile the thermal solver consumed so
            // ChamberVoxelBuilder paints + smooths the as-designed wall.
            GasSideWallProfile_mm:        wallProfile,
            // TPMS topology. design.TpmsKind is a
            // convenience accessor on RegenChamberDesign that projects the
            // ChannelTopology enum to the HeatTransfer.TpmsKind? used by
            // ChamberVoxelBuilder's annular TPMS void; null for
            // Axial/Helical/None preserves pre-B1 behaviour bit-identically.
            TpmsKind:                     design.TpmsKind,
            TpmsCellEdge_mm:              design.TpmsCellEdge_mm,
            TpmsSolidFraction:            design.TpmsSolidFraction,
            // Hot-fire readiness Item 6 / OOB-260 (2026-04-30): test-stand
            // thrust-takeout adapter. Default false on legacy designs preserves
            // bit-identical voxel output; turning on requires both the chamber
            // mounting flange AND the adapter flag to be set on the design
            // record (ChamberVoxelBuilder.Build gates on opt.IncludeMountingFlange
            // before consulting opt.IncludeThrustTakeoutAdapter).
            IncludeThrustTakeoutAdapter:                  design.IncludeThrustTakeoutAdapter,
            ThrustTakeoutAdapterHeight_mm:                design.ThrustTakeoutAdapterHeight_mm,
            ThrustTakeoutOuterDiameter_mm:                design.ThrustTakeoutOuterDiameter_mm,
            ThrustTakeoutMountStandard:                   design.ThrustTakeoutMountStandard,
            ThrustTakeoutUmbilicalPassThroughCount:       design.ThrustTakeoutUmbilicalPassThroughCount,
            ThrustTakeoutUmbilicalPassThroughDiameter_mm: design.ThrustTakeoutUmbilicalPassThroughDiameter_mm,
            // OOB-6 / Sprint B-3 (#200, 2026-04-30): acoustic-damper voxel
            // primitive. Inert when DamperType = None (legacy default) so
            // legacy designs see zero voxel-output drift.
            DamperType:                                   design.DamperType,
            DamperCount:                                  design.DamperCount,
            HelmholtzNeckArea_mm2:                        design.HelmholtzNeckArea_mm2,
            HelmholtzNeckLength_mm:                       design.HelmholtzNeckLength_mm,
            HelmholtzCavityVolume_mm3:                    design.HelmholtzCavityVolume_mm3,
            QuarterWaveLength_mm:                         design.QuarterWaveLength_mm,
            QuarterWaveDiameter_mm:                       design.QuarterWaveDiameter_mm,
            // #337 / OOB-13 (2026-05-04): add the inner plug voxel body when
            // the topology is ExpansionDeflection. Default false on all other
            // topologies preserves bit-identical voxel output for legacy designs.
            IncludeExpansionDeflectionPlug:
                ChannelTopologyDispatcher.IsExpansionDeflection(design.ChannelTopology),
            EdPlugInnerOuterRatio: EdPlugInnerOuterRatio,
            // OOB-2 Sprint 3 (2026-05-04): pass the SIMP topology-optimizer
            // per-station channel counts to the voxel builder. Null on all
            // other topologies so the existing uniform-N path is unchanged.
            TopologyOptimizedChannelsPerStation:
                topoResult?.ChannelsPerStation,
            TopologyOptimizedAxialPositions_mm:
                topoResult is not null
                    ? System.Array.ConvertAll(contour.Stations, s => s.X_mm)
                    : null);

        // TIER A.2: parallel-SA callers take the fast analytical path. No
        // voxel build, no viewer update — physics-only scoring.
        // Thread voxelSize_mm into Build so the BuildProfile
        // can stamp dense-equivalent voxel count + session voxel size for
        // cross-run comparability. Null on the BuildAnalytical path.
        //
        // Before calling ChamberVoxelBuilder.Build
        // (which allocates `new Voxels(...)` immediately), project the
        // working-set footprint against the user's memory budget. Without
        // this gate a 50 kN design at 0.4 mm voxel (~230 M dense voxels)
        // can silently exhaust a 64 GB workstation, thrash pagefile for
        // hours, and eventually OS-kill the process. The gate throws
        // MemoryBudgetExceededException which the UI catches to surface
        // a "coarsen voxel or raise cap" message instead of crashing.
        //
        // The bbox estimate matches the voxel grid footprint computed
        // inside ChamberVoxelBuilder.Build lines 201-233: axial length is
        // contour total + flange/mount margins (~30 mm), radial is max
        // station radius + channel height + outer wall + flange lip
        // (~15 mm). A 2.5× headroom factor on the radial dimension
        // tracks the real grid allocation observed in the 2026-04-20
        // baseline capture (see baseline-0.4mm.jsonl: Lx=184.8 vs
        // contour TotalLength≈160 mm, Ly=Lz=60.5 vs ExitRadius≈25 mm).
        //
        // The gate is skipped on the analytical path (no voxels allocated)
        // and when voxelSize_mm is 0 (unit-test callers).
        if (!skipVoxelGeometry && voxelSize_mm > 0)
        {
            double maxR_mm = 0.0;
            foreach (var s in contour.Stations)
                if (s.R_mm > maxR_mm) maxR_mm = s.R_mm;
            double outerWallRadius_mm = maxR_mm
                + channels.GasSideWallThickness_mm
                + System.Math.Max(channels.ChannelHeightAtChamber_mm,
                    System.Math.Max(channels.ChannelHeightAtThroat_mm,
                                    channels.ChannelHeightAtExit_mm))
                + 2.0;    // outer structural wall (~2 mm nominal for LPBF)
            double flangeOuterRadius_mm = outerWallRadius_mm + 10.0;   // flange lip
            double bboxLx_mm = contour.TotalLength_mm + 30.0;          // inj flange + mount flange axial
            double bboxLy_mm = 2.0 * (flangeOuterRadius_mm + 5.0);     // grid padding
            double bboxLz_mm = bboxLy_mm;

            long budgetBytes = (long)UI.ResourceBudget.MemoryBudget_Bytes;
            var projection = Analysis.MemoryProjectionGate.EnsureFits(
                bboxLx_mm, bboxLy_mm, bboxLz_mm, voxelSize_mm, budgetBytes);
            // Pass / Warning fall through; Warning-level projections are
            // echoed to the diagnostic stream so a user over 70 % sees
            // the "close to the cap" hint without being blocked.
            // Sprint A-3 Phase 2 / ADR-021: switched from PicoGK.Library.Log
            // to System.Console.Error.WriteLine so this Core-resident
            // orchestrator stays PicoGK-free.
            //
            // VFD011 suppression: B.7 broadens the analyzer to flag
            // `Console.Error.WriteLine` instance calls inside [Deterministic]
            // scope. The warning here is a stderr side effect that doesn't
            // affect the deterministic return value — the projection object
            // is computed deterministically; the stderr write is a one-way
            // notification the analyzer can't see as benign. Until a
            // diagnostic-sink callback lands (deferred), suppress at the
            // specific call site rather than weakening the broader rule.
            if (projection.Level == Analysis.MemoryProjectionLevel.Warning)
#pragma warning disable VFD011
                System.Console.Error.WriteLine($"MemoryProjectionGate: {projection.Message}");
#pragma warning restore VFD011
        }

        Geometry.ChamberGeometryResult geom;
        var generator = voxelGenerator ?? AnalyticalOnlyVoxelGenerator.Instance;
        try
        {
            geom = skipVoxelGeometry
                ? generator.BuildAnalytical(buildOpts)
                : generator.Build(buildOpts, voxelSize_mm);
        }
        catch (System.OutOfMemoryException oom)
        {
            // PicoGK voxel allocations are native C++ —
            // a too-large grid typically manifests as pagefile thrash
            // rather than a clean managed exception. If the managed
            // runtime does bubble an OOM (e.g. managed-side arrays,
            // or a .NET 9 PInvoke marshalling failure), wrap it in a
            // domain-specific error so the UI catch can distinguish
            // "voxel allocation too big" from an unrelated runtime
            // failure. The rethrow keeps the stack trace intact.
            throw new Analysis.MemoryBudgetExceededException(
                new Analysis.MemoryProjection(
                    ProjectedBytes:   -1,
                    BudgetBytes:      (long)UI.ResourceBudget.MemoryBudget_Bytes,
                    FractionOfBudget: double.PositiveInfinity,
                    Level:            Analysis.MemoryProjectionLevel.Fail,
                    Message:          oom.Message),
                requestedVoxel_mm: voxelSize_mm,
                suggestedVoxel_mm: voxelSize_mm * 1.5,
                message:
                    $"Voxel allocation ran out of memory at voxel {voxelSize_mm:F2} mm. "
                    + $"Try ≥ {voxelSize_mm * 1.5:F2} mm or raise the Resource Budget cap.");
        }

        var mfg = ManufacturingAnalysis.Analyze(contour, channels, geom, material, design);

        // Combustion stability screening (chug + screech modes + composite).
        // SPRINT 1.1: pass the actual injector state rather than the hardcoded
        // 20 % nominal. Chug degrades to Marginal outside [0.15, 0.25] and to
        // Fail outside [0.13, 0.27]; FeasibilityGate catches the Fail case
        // through STABILITY_FAIL.
        // OOB-6 / Sprint B-3: route the damper geometry from the design
        // record into the stability evaluator. None / zero-count designs
        // produce a null AcousticDamperConfig and the evaluator returns
        // a null AcousticDamperResult — bit-identical for legacy designs.
        Combustion.Stability.AcousticDamperConfig? damperConfig =
            design.DamperType == Combustion.Stability.AcousticDamperType.None
                ? null
                : new Combustion.Stability.AcousticDamperConfig(
                    Type:                  design.DamperType,
                    Count:                 design.DamperCount,
                    NeckArea_mm2:          design.HelmholtzNeckArea_mm2,
                    NeckLength_mm:         design.HelmholtzNeckLength_mm,
                    CavityVolume_mm3:      design.HelmholtzCavityVolume_mm3,
                    QuarterWaveLength_mm:  design.QuarterWaveLength_mm,
                    QuarterWaveDiameter_mm: design.QuarterWaveDiameter_mm);

        var stability = StabilityScreening.Evaluate(
            contour, gas, cond.ChamberPressure_Pa,
            injector: new InjectorState(dPInj_Pa),
            propellantPair: cond.PropellantPair,    // TIER B.5: enables Crocco n-τ screening
            damperConfig: damperConfig);

        // Voxel-adequacy gate (UPGRADE 4): only when caller supplies a voxel size.
        // Safe to skip during fast unit tests (voxelSize_mm defaults to 0).
        VoxelAdequacyResult? voxelAdequacy = voxelSize_mm > 0
            ? VoxelAdequacyGate.Evaluate(channels, contour, voxelSize_mm)
            : null;

        // Z2.8 (2026-04-28): cheap burst-margin calc using the same per-station
        // wall profile already built for StructuralCheck. Reuses the helper
        // extracted from ProofTestAnalysis.Evaluate. Cost is one σ_y_cold
        // lookup + one pass over Stations — negligible vs the thermal solve.
        // Default 0 on synthetic call sites bit-identical to pre-Z2.8.
        // Z2.10 (2026-04-28): pass outerJacketThickness_mm + jacketMaterial
        // so the burst-margin calc uses composite-wall hoop credit (matches
        // StructuralCheck.Evaluate's Sprint G' multi-wall + A1-follow-on
        // composite yield). Pre-Z2.10 the burst calc was inner-liner-only
        // and fired BURST_MARGIN_INSUFFICIENT on bimetallic designs whose
        // jacket made them structurally feasible per StructuralCheck —
        // an inconsistent-physics bug between the two analyses. jacketMat
        // is the same routing as the StructuralCheck.Evaluate caller above
        // (gated to bimetallic wall index 4 only).
        double burstMargin = Structure.ProofTestAnalysis.ComputeBurstMarginFactor(
            thermal, material, design.GasSideWallThickness_mm,
            cond.ChamberPressure_Pa,
            gasSideWallProfile_mm: wallProfile,
            outerJacketThickness_mm: design.OuterJacketThickness_mm,
            jacketMaterial:          jacketMat);

        // PH-40 / issue #259 (2026-04-29): low-cycle fatigue evaluation.
        // Coffin-Manson on the through-wall thermal gradient. Cheap (one
        // log-N bisection per station, ~20 station evals) so always-on.
        // Gate LCF_LIFE_INSUFFICIENT only fires when MissionCycles ≥ 100
        // AND PredictedCyclesToFailure < 4× MissionCycles; below 100
        // cycles the result still populates but only a Notes-disclosure
        // is stamped.
        var lcf = Structure.LowCycleFatigueAnalysis.Evaluate(
            thermal, material,
            new Structure.LowCycleFatigueInputs(MissionCycles: design.MissionCycles));

        var result = new RegenGenerationResult(
            Contour: contour,
            Geometry: geom,
            Thermal: thermal,
            Stress: stress,
            Manufacturing: mfg,
            Derived: derived,
            Gas: gas,
            Conditions: cond,
            Stability: stability,
            InjectorPattern: design.InjectorElementPattern,
            InjectorSizing: injectorSizing,
            VoxelAdequacy: voxelAdequacy,
            DesignHash: DesignProvenance.Compute(cond, design),
            BurstMarginFactor: burstMargin,
            LowCycleFatigue: lcf)
        {
            ManifoldLength_mm = design.ManifoldLength_mm,
            // OOB-9 (issue #344): echo factor so gates + reporting can read it.
            FiniteRateCorrectionFactor = frFactor,
            // OOB-7 (issue #343): RDE echo fields for gates + reporting.
            RdeTopology         = design.RdeTopology,
            RdeWaveCount        = rdeWaveCount,
            RdeAnnulusFillTime_us = rdeAnnulusFillTime_us,
        };

        // PHASE 2 + PHASE 5: populate the injector-face estimate. When the
        // coolant crossover is active, the fuel arriving at the injector is
        // already regen-heated — override the D-B bulk state to the coolant
        // outlet temperature instead of the cold inlet T.
        double? fuelOverride = design.IncludeCoolantCrossover
            ? thermal.CoolantOutletT_K
            : (double?)null;

        // Structural-confidence grading from design flags.
        bool anyThreadedPort = design.CoolantPortStandard    != Geometry.PortStandard.Plain
                            || design.PropellantPortStandard != Geometry.PortStandard.Plain;
        bool anyFlange = design.IncludeInjectorFlange || design.IncludeMountingFlange;
        bool threadedPropThroughFlange =
               design.IncludeInjectorFlange
            && design.PropellantPortStandard != Geometry.PortStandard.Plain;
        StructuralConfidence confidence;
        string confidenceReason;
        if (threadedPropThroughFlange)
        {
            confidence = StructuralConfidence.Low;
            confidenceReason = "Threaded axial propellant ports pierce the injector flange — "
                             + "analytical VM check under-predicts thread-root stress. Add an FEA pass.";
        }
        else if (anyThreadedPort || anyFlange)
        {
            confidence = StructuralConfidence.Medium;
            confidenceReason = "Threaded ports or flanges introduce stress concentrations the "
                             + "thin-wall model does not capture. Inspect FEA before print.";
        }
        else
        {
            confidence = StructuralConfidence.High;
            confidenceReason = "Plain-bore ports, no flanges — analytical VM margin is representative.";
        }

        // If a gimbal mount is selected and the bearing stress is
        // uncomfortable, demote the structural confidence. Evaluation
        // is light enough (closed-form) to run up front without
        // caching. Skip on the parallel-SA fast path — confidence
        // pill is UI-only, never drives gates.
        if (!skipMfgAnalysis
         && design.MountConfiguration != Structure.MountConfiguration.FixedFlange)
        {
            var gimbalPreview = Structure.GimbalMount.Evaluate(
                design.MountConfiguration, cond.Thrust_N, material);
            if (!gimbalPreview.StressAcceptable)
            {
                // Bump to at least Medium, and to Low if already flagged.
                confidence = confidence == StructuralConfidence.Low
                    ? StructuralConfidence.Low
                    : StructuralConfidence.Medium;
                confidenceReason += $"  Gimbal bearing margin {gimbalPreview.BearingMargin:F2}×"
                                  + $" < {Structure.GimbalMount.MinBearingMargin:F1}× recommended — add FEA.";
            }
        }

        // Pre-compute the preburner pair
        // + turbopump so the turbine sizer can reach both. The with{}
        // expression evaluates field-by-field in source order, so direct
        // cross-references between `Preburner` / `Turbopump` /
        // `OxidizerPreburner` are not allowed inside the same with{}
        // block — we resolve them once here and stamp each slot from
        // the matching local.
        var cycleSolver = FeedSystem.CycleSolvers.Get(cond.EngineCycle);
        var preburnerFuel = SizePreburnerFor(cond, design, derived, oxRichSide: false);
        var preburnerOx = cycleSolver.HasOxRichPreburner
            ? SizePreburnerFor(cond, design, derived, oxRichSide: true)
            : null;
        var turbopumpSized = cycleSolver.HasTurbopump
            ? SizeTurbopumpFor(cond, design, derived, preburnerFuel, preburnerOx,
                               turbopumpGenerator, turbineGenerator)
            : null;

        // Sprint 23 (2026-04-23): expander-cycle turbine energy balance.
        FeedSystem.ExpanderTurbineResult? expanderTurbine = null;
        if ((cond.EngineCycle == FeedSystem.EngineCycle.OpenExpander
          || cond.EngineCycle == FeedSystem.EngineCycle.ClosedExpander)
            && turbopumpSized is not null)
        {
            expanderTurbine = FeedSystem.ExpanderCycleSizing.Size(
                cycle:                      cond.EngineCycle,
                coolant:                    coolantFluid,
                coolantOutletT_K:           thermal.CoolantOutletT_K,
                coolantOutletP_Pa:          thermal.CoolantOutletP_Pa,
                coolantInletT_K:            cond.CoolantInletTemp_K,
                coolantMassFlow_kgs:        coolantMassFlow,
                mainChamberPressure_Pa:     cond.ChamberPressure_Pa,
                requiredPumpShaftPower_W:   turbopumpSized.TotalShaftPower_W);
        }

        // Sprint 25 (2026-04-23): tap-off cycle turbine energy balance.
        // PH-49 (2026-04-29): look up the local static T at the tap-off
        // axial station so the boundary-layer fraction is applied to the
        // correct station temperature rather than the flat chamber T_c.
        FeedSystem.TapOffTurbineResult? tapOffTurbine = null;
        if (cond.EngineCycle == FeedSystem.EngineCycle.TapOff
            && turbopumpSized is not null)
        {
            // Interpolate the converging-section station nearest to the
            // requested axial fraction (0 = injector face, 1 = throat).
            double? localT = null;
            if (thermal.Stations.Length > 0)
            {
                int throatIdx = contour.ThroatIndex;
                int stIdx = System.Math.Clamp(
                    (int)System.Math.Round(design.TapOffAxialStation_frac * throatIdx),
                    0, throatIdx);
                localT = thermal.Stations[stIdx].StaticTemp_K;
            }

            tapOffTurbine = FeedSystem.TapOffCycleSizing.Size(
                cycle:                       cond.EngineCycle,
                chamberTemperature_K:        gas.ChamberTemp_K,
                chamberPressure_Pa:          cond.ChamberPressure_Pa,
                totalMassFlow_kgs:           derived.TotalMassFlow_kgs,
                warmGasGamma:                gas.Gamma,
                warmGasMolecularWeight_gmol: gas.MolecularWeight,
                requiredPumpShaftPower_W:    turbopumpSized.TotalShaftPower_W,
                localGasTemperature_K:       localT);
        }

        return result with
        {
            InjectorFace = result.ToInjectorFaceGeometry() is { } injGeom
                ? HeatTransfer.InjectorFaceThermal.Estimate(injGeom, fuelOverride)
                : null,
            // TIER B.6: residual-stress / warp prediction. Pure analytical
            // pass — no voxel ops or physics solver involvement, safe to run
            // in the fast SA path. Suppress on the
            // parallel-SA candidate path — Residual never drives a gate or
            // SA score term, only report / UI panels read it.
            Residual = skipMfgAnalysis
                ? null
                : Manufacturing.ResidualStressAnalysis.Analyze(contour, material),
            StructuralConfidence = confidence,
            StructuralConfidenceReason = confidenceReason,
            // Run the feed-system stackup when the user
            // supplied a tank ullage pressure. Opt-in by default value.
            FeedStackup = FeedSystem.PressureStackup.Compute(
                cond, design, thermal,
                injectorDeltaPInj_Pa: dPInj_Pa,
                fuelMassFlow_kgs:     derived.FuelMassFlow_kgs,
                oxMassFlow_kgs:       derived.OxidizerMassFlow_kgs,
                chamberRadius_mm:     contour.ChamberRadius_mm),
            IgniterType = design.IgniterType,
            // Evaluate the selected mount's stiffness +
            // bearing-stress margin. FixedFlange is a near-no-op that
            // stamps "infinite stiffness, no bearing load".
            GimbalMount = Structure.GimbalMount.Evaluate(
                design.MountConfiguration, cond.Thrust_N, material),
            // Run the purge-port flow model against every
            // port in the list. Empty list → empty result.
            PurgeResults = Coolant.PurgeFlowModel.EvaluateAll(
                design.PurgePorts, cond.ChamberPressure_Pa),
            // Ablative-liner recession integral. Reuses
            // the per-station heat-flux profile from `thermal`; safe to
            // run on the fast skipVoxelGeometry path (no PicoGK calls).
            Ablative = Manufacturing.AblativeAnalysis.Run(
                material:           design.AblativeMaterial,
                thermal:            thermal,
                initialThickness_mm:design.AblativeThickness_mm,
                burnDuration_s:     design.AblativeBurnDuration_s,
                safetyFactor:       design.AblativeSafetyFactor),
            // Pre-fire chilldown lumped-jacket transient.
            // Opt-in + cryogenic-pair only — RP-1 short-circuits to
            // null because the propellant is at room T already.
            Chilldown = (cond.IncludeChilldownTransient
                         && HeatTransfer.ChilldownTransient.IsCryogenic(pairMeta.CoolantFluidKey))
                ? HeatTransfer.ChilldownTransient.Run(BuildChilldownInputs(
                    cond, geom, thermal, material, pairMeta, derived))
                : null,
            // Start-transient simulation. Opt-in via
            // cond.IncludeStartTransient.
            StartTransient = cond.IncludeStartTransient
                ? Combustion.StartTransientSim.Run(BuildStartTransientInputs(
                    cond, design, contour, derived))
                : null,
            // Shutdown / blowdown transient. Shares the IncludeStartTransient
            // opt-in flag — the safety-review cycle that wants startup also
            // wants shutdown for the symmetric cutoff analysis. Provides
            // residual-propellant burn/vent breakdown + time-to-subcritical
            // for the SafetyReport "Startup / Shutdown sequence" section.
            ShutdownBlowdown = cond.IncludeStartTransient
                ? Combustion.ShutdownBlowdownSim.Run(BuildShutdownBlowdownInputs(
                    cond, contour, derived))
                : null,
            // Turbopump sizing. PressureFed short-circuits
            // to a no-op result; other cycles run per-pump sizing math.
            // When design.IncludeTurbopumpGeometry is
            // true, the sized TurbopumpResult gets parametric pump
            // geometry attached for both pumps.
            // The same result additionally carries the
            // sized turbine stage + optional turbine-wheel geometry for
            // each shaft (see TurbineSizing.Size).
            Turbopump = turbopumpSized,
            // TPMS topology echo so the
            // feasibility gate + report can branch without a separate
            // design reference. Non-TPMS designs still stamp the topology
            // (Axial / Helical / None) — cheap, no overhead.
            ChannelTopology   = design.ChannelTopology,
            TpmsCellEdge_mm   = design.TpmsCellEdge_mm,
            TpmsSolidFraction = design.TpmsSolidFraction,
            // Preburner sizing. Null
            // for PressureFed / ElectricPump / OpenExpander (no
            // preburner). For GasGenerator / StagedCombustion / FullFlow
            // the helper auto-resolves MR + Pc and reuses the propellant
            // table at the preburner's off-nominal MR. FullFlow
            // additionally populates OxidizerPreburner with the ox-rich
            // sibling via PreburnerChamber.SizeFfscDual.
            Preburner         = preburnerFuel,
            OxidizerPreburner = preburnerOx,
            // Sprint 2b (2026-04-22): populate the aerospike sidecar
            // build result when the baseline is on the aerospike
            // topology. Uses the xUnit-safe physics-only path, so no
            // PicoGK dependency is added to this hot code path.
            // Callers that want the voxelised aerospike body (STL
            // export, viewer) must still go through
            // AerospikeBuilder.Build on the task thread.
            // Sprint 26 (2026-04-23): both the axisymmetric Aerospike
            // and the extruded-rectangular LinearAerospike topologies
            // populate the Aerospike sidecar via AerospikeOptimization.
            // Downstream scoring + feasibility code branches on
            // Aerospike.Contour.IsLinear where (and only where) the
            // distinction matters.
            // P7 (2026-04-29): use the caller-supplied
            // `cachedAerospikeResult` when provided AND topology matches —
            // saves a redundant AerospikeOptimization.BuildAndEvaluate
            // pass on the monolithic-aerospike export path.
            // Sprint A-3 Phase 2 / ADR-021 (post-orchestrator-move):
            // aerospike topologies need an IAerospikeBuilder. App
            // callers (Program.cs, MonolithicEngineBuilder,
            // KioskPipeline, StlExporter, OOB-3 fixtures) pass
            // `new AerospikeBuilderAdapter()` explicitly when running
            // aerospike presets. Headless / non-aerospike callers leave
            // aerospikeBuilder null and the result's Aerospike sidecar
            // stays null (downstream scoring + feasibility code already
            // handles the null case bit-identically to non-aerospike
            // designs).
            Aerospike = ChannelTopologyDispatcher.IsAerospike(design.ChannelTopology)
                ? (cachedAerospikeResult
                    ?? aerospikeBuilder?.BuildPhysicsOnly(AerospikeOptimization.ToSpec(cond, design)))
                : null,
            // Sprint 23 (2026-04-23): expander-cycle turbine energy
            // balance. Null on non-expander cycles.
            ExpanderTurbine = expanderTurbine,
            // Sprint 25 (2026-04-23): tap-off cycle turbine energy
            // balance (null on non-TapOff cycles).
            TapOffTurbine = tapOffTurbine,
            // Sprint 27 (2026-04-23): LPBF printability analysis. Opt-in
            // via design.IncludeLpbfPrintabilityAnalysis; synthesises
            // surface samples from the contour + channels and runs the
            // overhang / drain-path checks plus the orientation advisor.
            // Trapped-powder voxel flood-fill is deferred to the voxel-
            // building path (STL export) — the fast SA path only runs
            // the voxel-free checks. Safe on skipMfgAnalysis because the
            // analysis itself is pure math.
            Printability = design.IncludeLpbfPrintabilityAnalysis
                ? RunPrintabilityAnalysis(design, contour, channels)
                : null,
            // Sprint 28 (2026-04-24): surface the two pieces the
            // INSTRUMENTATION_TAP_INTERFERENCE gate needs — the regen
            // channel count (for boss-vs-channel azimuth overlap) and
            // the raw SensorBosses list. Both are design-side fields
            // that the feasibility evaluator otherwise has no handle
            // on (gate signature takes a RegenGenerationResult, not
            // the Design). Costs nothing on non-boss paths (empty list).
            ChannelCount = design.ChannelCount,
            SensorBosses = design.SensorBosses,
            // OOB-2 Sprint 3 (2026-05-04): surface the topology result so
            // TOPOLOGY_CHANNEL_NOT_PRINTABLE gate can check per-station widths.
            TopologyChannels = topoResult,
            // OOB-12 (2026-05-04): transpiration echo for gate + BuildSheet.
            EnableTranspirationCooling = design.EnableTranspirationCooling,
            TranspirationBleedFraction = design.TranspirationBleedFraction,
        };
    }

    /// <summary>
    /// Sprint 27 (2026-04-23): run the LPBF printability analysis for
    /// the regen-chamber pipeline. Uses the chamber contour + channel
    /// schedule to synthesise surface samples (axisymmetric unwrap at
    /// 24 azimuthal slices) and the design's coolant + purge + igniter
    /// topology to build a minimal routing graph. Skips the voxel-flood
    /// trapped-powder check on the fast SA path — too expensive to run
    /// per-candidate — so the TRAPPED_POWDER_REGION gate only fires on
    /// opted-in voxel builds that explicitly attach a snapshot via the
    /// overloaded entry point.
    /// </summary>
    private static Geometry.LpbfAnalysis.LpbfPrintabilityResult RunPrintabilityAnalysis(
        RegenChamberDesign design,
        Chamber.ChamberContour contour,
        HeatTransfer.ChannelSchedule channels)
    {
        var material = Geometry.LpbfAnalysis.LpbfMaterialProfiles.For(design.LpbfMaterial);

        // Build axis: by default +X (chamber axis = build axis — the
        // axisymmetric chamber's natural build orientation). User can
        // override via LpbfPrintOrientationAxis_deg as a rotation about
        // +Z from +X; -1 means auto (+X baseline, advisor recommends).
        var buildAxis = design.LpbfPrintOrientationAxis_deg < 0
            ? new System.Numerics.Vector3(1, 0, 0)
            : new System.Numerics.Vector3(
                (float)System.Math.Cos(design.LpbfPrintOrientationAxis_deg * System.Math.PI / 180.0),
                (float)System.Math.Sin(design.LpbfPrintOrientationAxis_deg * System.Math.PI / 180.0),
                0f);

        // Routing graph: chamber coolant path has manifold inlet +
        // manifold outlet as external ports, plus one node per purge
        // port. Igniter bore adds an external port when configured.
        var nodes = new System.Collections.Generic.List<Geometry.LpbfAnalysis.LpbfRoutingNode>
        {
            new("coolant-in",  "Coolant manifold inlet",  IsExternalPort: true),
            new("coolant-out", "Coolant manifold outlet", IsExternalPort: true),
            new("injector-face", "Injector face bore",    IsExternalPort: true),
            new("nozzle-exit",   "Nozzle exit",           IsExternalPort: true),
        };
        var edges = new System.Collections.Generic.List<Geometry.LpbfAnalysis.LpbfRoutingEdge>
        {
            new("coolant-in",    "coolant-out",   "regen jacket"),
            new("injector-face", "nozzle-exit",   "combustion cavity"),
        };
        // Purge ports: each adds an external-port node + edge to the
        // coolant path (the purge line injects upstream of the manifold).
        if (design.PurgePorts is { Count: > 0 } ports)
        {
            int idx = 0;
            foreach (var p in ports)
            {
                string id = $"purge-{idx++}";
                nodes.Add(new(id, $"Purge {p.Location} ({p.Fluid})", IsExternalPort: true));
                edges.Add(new(id, "coolant-in", "purge line"));
            }
        }

        var graph = new Geometry.LpbfAnalysis.LpbfRoutingGraph(nodes, edges);

        return Geometry.LpbfAnalysis.LpbfPrintabilityAnalysis.ForChamber(
            contour:          contour,
            channels:         channels,
            material:         material,
            buildAxis:        buildAxis,
            routingGraph:     graph,
            voxelField:       null,
            openings:         null,
            azimuthalSamples: 24);
    }

    /// <summary>
    /// Size a preburner for the given cycle.
    /// Null result for PressureFed / ElectricPump / OpenExpander
    /// (which have no preburner). Auto-resolves MR + Pc from
    /// <see cref="RegenChamberDesign.PreburnerMrRatio"/> /
    /// <see cref="RegenChamberDesign.PreburnerChamberPressure_Pa"/>
    /// with sensible fallbacks (propellant-specific fuel-rich MR +
    /// 1.5× main Pc for staged, 1.2× for gas-generator).
    ///
    /// Turbine mass-flow estimate: fuel-rich preburner passes ~30 %
    /// of total mass flow in GG (gas-generator literature estimate);
    /// staged-combustion passes the full flow through the preburner
    /// (all propellants go through preburner → main chamber). MVP
    /// picks one conservatively-sized value.
    /// </summary>
    private static Chamber.PreburnerResult? SizePreburnerFor(
        OperatingConditions cond, RegenChamberDesign design, DerivedValues derived,
        bool oxRichSide = false)
    {
        // Sprint 21: per-cycle dispatch centralised in CycleSolvers.
        // Adding a new cycle is additive — the solver registers what
        // it has (preburner / ox preburner / dual sizing) and this
        // method reads those flags + the Pc multiplier + the mass-
        // flow fraction.
        var solver = FeedSystem.CycleSolvers.Get(cond.EngineCycle);

        // Early return: no preburner at all (PressureFed / ElectricPump /
        // OpenExpander today; future Expander / Tap-off cycles too).
        if (!solver.HasFuelRichPreburner && !solver.HasOxRichPreburner)
            return null;

        // Resolve the fuel-rich preburner
        // MR with SA-design override > cond override > pair-specific
        // Suggest. `design.PreburnerMrRatio` is the SA-promoted knob
        // (dim 20 of the packed vector); non-zero wins over the static
        // cond field and the Suggest fallback.
        double resolvedFuelRichMr = design.PreburnerMrRatio > 0
            ? design.PreburnerMrRatio
            : cond.PreburnerMrRatio > 0
                ? cond.PreburnerMrRatio
                : Chamber.PreburnerChamber.SuggestPreburnerMr(cond.EngineCycle, cond.PropellantPair);

        // Resolve Pc: explicit override on OperatingConditions >
        // solver-supplied multiplier of the main chamber Pc.
        double prePc = cond.PreburnerChamberPressure_Pa > 0
            ? cond.PreburnerChamberPressure_Pa
            : cond.ChamberPressure_Pa * solver.PreburnerPcMultiplier;

        // When caller asks for the ox-rich side, dispatch on the
        // sizing mode the solver advertises:
        //   • UsesFfscDualPreburnerSizing = true  → SizeFfscDual (pair
        //     of preburners sized consistently). Today: FullFlow only.
        //   • UsesFfscDualPreburnerSizing = false + HasOxRichPreburner
        //     → Sprint 24 (ORSC) single-ox-rich path via the scalar
        //     PreburnerChamber.Size. MR defaults to SuggestOxRichPreburnerMr.
        if (oxRichSide)
        {
            if (!solver.HasOxRichPreburner) return null;

            if (solver.UsesFfscDualPreburnerSizing)
            {
                var (_, oxRich) = Chamber.PreburnerChamber.SizeFfscDual(
                    pair:                   cond.PropellantPair,
                    fuelRichMr:             resolvedFuelRichMr,
                    oxRichMr:               0,    // use SuggestOxRichPreburnerMr
                    preburnerPc_Pa:         prePc,
                    totalFuelMassFlow_kgs:  derived.FuelMassFlow_kgs,
                    totalOxMassFlow_kgs:    derived.OxidizerMassFlow_kgs);
                return oxRich;
            }

            // Sprint 24 (2026-04-23): ORSC single-ox-rich path. Fuel
            // goes straight to main injection; only the ox-rich
            // preburner is sized. MR defaults from pair-specific
            // heuristic (SuggestOxRichPreburnerMr) unless overridden.
            double orMr = cond.PreburnerMrRatio > 0
                ? cond.PreburnerMrRatio
                : Chamber.PreburnerChamber.SuggestOxRichPreburnerMr(cond.PropellantPair);
            double orTurbineMassFlow = derived.TotalMassFlow_kgs
                                     * solver.OxRichPreburnerMassFlowFraction;
            return Chamber.PreburnerChamber.Size(
                cycle:                   cond.EngineCycle,
                pair:                    cond.PropellantPair,
                preburnerMr:             orMr,
                preburnerPc_Pa:          prePc,
                turbineMassFlow_kgs:     orTurbineMassFlow);
        }

        if (!solver.HasFuelRichPreburner)
            return null;    // ox-rich-only cycles (future ORSC) fall through

        double preMr = resolvedFuelRichMr;

        // FFSC: route the fuel-rich side through SizeFfscDual so
        // the pair is sized consistently with the ox-rich call below.
        if (solver.UsesFfscDualPreburnerSizing)
        {
            var (fuelRich, _) = Chamber.PreburnerChamber.SizeFfscDual(
                pair:                   cond.PropellantPair,
                fuelRichMr:             resolvedFuelRichMr,
                oxRichMr:               0,
                preburnerPc_Pa:         prePc,
                totalFuelMassFlow_kgs:  derived.FuelMassFlow_kgs,
                totalOxMassFlow_kgs:    derived.OxidizerMassFlow_kgs);
            return fuelRich;
        }

        // Non-FFSC path: solver supplies the mass-flow fraction
        // (GasGenerator 0.05 side-stream, StagedCombustion 1.00 full flow).
        double turbineMassFlow = derived.TotalMassFlow_kgs
                               * solver.FuelRichPreburnerMassFlowFraction;

        var sized = Chamber.PreburnerChamber.Size(
            cycle:                   cond.EngineCycle,
            pair:                    cond.PropellantPair,
            preburnerMr:             preMr,
            preburnerPc_Pa:          prePc,
            turbineMassFlow_kgs:     turbineMassFlow);

        // Sprint 9 Track B (2026-04-22): optional preburner regen cooling.
        // Opt-in via design.IncludePreburnerRegenCooling. Reuses the
        // coolant fluid from the main propellant pair (fuel-side) and
        // the material already selected for the chamber. Fuel mass flow
        // through the cooling jacket is taken at the design's fuel
        // stream rate (conservative — real staged-combustion cycles
        // route all fuel through the preburner jacket anyway).
        if (sized is not null && design.IncludePreburnerRegenCooling)
        {
            var pairMeta = Combustion.PropellantPairs.GetMeta(cond.PropellantPair);
            var coolantFluid = Coolant.CoolantRegistry.Get(pairMeta.CoolantFluidKey);
            var wallMaterial = HeatTransfer.WallMaterials.All[
                System.Math.Clamp(cond.WallMaterialIndex, 0,
                    HeatTransfer.WallMaterials.All.Length - 1)];
            // Z2.9 follow-on (2026-04-28): pass the main-chamber gas
            // Prandtl as a proxy for the preburner gas. The preburner runs
            // ~1500-2300 K (vs main chamber 3000-3700 K) but Prandtl is
            // weak-T at constant composition, so this is a good first-order
            // hand-off. A future PreburnerWarmGasState record could carry
            // its own per-station Prandtl.
            var preburnerGasProxy = Combustion.PropellantTables.Lookup(
                cond.PropellantPair, cond.MixtureRatio, cond.ChamberPressure_Pa);
            var thermal = HeatTransfer.PreburnerCooling.Solve(
                preburner:           sized,
                channelCount:        design.PreburnerChannelCount,
                channelWidth_mm:     design.PreburnerChannelWidth_mm,
                channelDepth_mm:     design.PreburnerChannelDepth_mm,
                wallThickness_mm:    design.PreburnerWallThickness_mm,
                coolantMassFlow_kgs: derived.FuelMassFlow_kgs,
                coolantInletT_K:     cond.CoolantInletTemp_K,
                coolantInletP_Pa:    cond.CoolantInletPressure_Pa,
                coolantFluid:        coolantFluid,
                wall:                wallMaterial,
                gasPrandtl:          preburnerGasProxy.Prandtl);
            sized = sized with { Thermal = thermal };
        }

        return sized;
    }

    /// <summary>
    /// Helper that resolves the propellant densities + inlet/discharge
    /// pressures and calls <see cref="FeedSystem.TurbopumpSizing.Size"/>.
    /// Additionally runs
    /// <see cref="FeedSystem.TurbineSizing.Size"/> when a preburner is
    /// available + attaches the sized turbine + optional turbine-wheel
    /// geometry to the returned result.
    /// </summary>
    private static FeedSystem.TurbopumpResult SizeTurbopumpFor(
        OperatingConditions cond, RegenChamberDesign design, DerivedValues derived,
        Chamber.PreburnerResult? fuelPreburner,
        Chamber.PreburnerResult? oxPreburner,
        // Sprint A-3 Phase 2 / ADR-021: forward the generator seams from
        // the GenerateWith caller. Null falls back to the static methods
        // (back-compat path for App callers).
        Turbopump.ITurbopumpGenerator? turbopumpGenerator = null,
        Turbopump.ITurbineGenerator? turbineGenerator = null)
    {
        var (oxRho, fuelRho) = Injector.OrificeModel.InjectionDensities(cond.PropellantPair);
        // Sprint feasibility-audit-integrity-bundle-2 (2026-04-27, ID-3):
        // OX pump only needs to push past the injector ΔP — chamber
        // pressure plus ~20 % typical injector pressure drop = 1.2 × Pc.
        // Pre-bundle-2 the OX pump shared the fuel pump's discharge (which
        // for expander cycles has been pushed to 4-5 × Pc by Sprint F1).
        // That over-spec'd the OX pump shaft power on expanders by 4-5×;
        // post-bundle-2 the OX pump is sized correctly. The 1.2 × Pc
        // multiplier matches Huzel & Huang §3.2 typical injector ΔP/Pc
        // recommendation (15-25 % range; 20 % midpoint) and aligns with
        // the existing `dPInjFraction` clamp [0.05, 0.50] used at line
        // ~395 of this file. Floor at 0.5 MPa for low-Pc small thrusters
        // where the injector ΔP is bounded below by tank-feed pressure
        // requirements.
        const double InjectorDischargeFactor = 1.2;
        const double InjectorDischargeFloor_Pa = 0.5e6;
        double oxDischarge = System.Math.Max(
            cond.ChamberPressure_Pa * InjectorDischargeFactor,
            InjectorDischargeFloor_Pa);
        var sized = FeedSystem.TurbopumpSizing.Size(
            cycle:                cond.EngineCycle,
            cond:                 cond,
            fuelFlow_kgs:         derived.FuelMassFlow_kgs,
            oxFlow_kgs:           derived.OxidizerMassFlow_kgs,
            fuelDensity_kgm3:     fuelRho,
            oxDensity_kgm3:       oxRho,
            fuelInletPressure_Pa: ResolvePumpInlet(cond),
            oxInletPressure_Pa:   ResolvePumpInlet(cond),
            dischargePressure_Pa: ResolvePumpDischarge(cond),
            pumpEfficiency:       cond.PumpEfficiency,
            // Sprint 3 (2026-04-22) — multi-stage centrifugal pump
            // support. design.PumpStageCount = 1 is the pre-Sprint-3
            // default and round-trips the prior sizing exactly.
            stageCount:           design.PumpStageCount,
            // Sprint 30 (2026-04-24, PH-2) — inducer flag drives the
            // Thoma-NPSHR formula's S_s (8 500 vs 20 000).
            hasInducer:           design.HasInducer,
            // Sprint 34b (2026-04-25, PH-8) — user-overrideable RPM.
            // 0 = auto-derive from N_s = 2500 (back-compat).
            pumpRpm_rpm:          design.PumpRpm_rpm,
            // Sprint feasibility-audit-integrity-bundle-2 (ID-3, 2026-04-27).
            oxDischargePressure_Pa: oxDischarge);

        // Attach parametric pump geometry
        // when the conditions flag is set. Geometry-generation is
        // analytical (pure math, no PicoGK) — safe on any thread; the
        // eventual voxelisation (via `TurbopumpGeometryGenerator.BuildImplicit`)
        // is task-thread only.
        // Sprint A-3 Phase 2 / ADR-021 (post-orchestrator-move): pump
        // geometry attachment requires the caller to supply an
        // ITurbopumpGenerator — Core can't reach the Voxels-side
        // TurbopumpGeometryGenerator.Generate. Headless / non-turbopump
        // callers leave turbopumpGenerator null and the geometry stays
        // null on the result (same effect as
        // cond.IncludeTurbopumpGeometry = false).
        if (cond.IncludeTurbopumpGeometry && turbopumpGenerator is not null)
        {
            sized = sized with
            {
                FuelPumpGeometry = sized.FuelPump is null ? null : turbopumpGenerator.Generate(sized.FuelPump),
                OxPumpGeometry   = sized.OxPump   is null ? null : turbopumpGenerator.Generate(sized.OxPump),
            };
        }

        // Turbine sizing + optional
        // turbine-wheel geometry. The turbine closes the shaft-power
        // loop against the preburner enthalpy rise; PowerBalanceOK
        // seeds the TURBINE_POWER_DEFICIT feasibility gate.
        var turbine = FeedSystem.TurbineSizing.Size(
            cycle:                  cond.EngineCycle,
            mainChamberPressure_Pa: cond.ChamberPressure_Pa,
            fuelPump:               sized.FuelPump,
            oxPump:                 sized.OxPump,
            fuelPreburner:          fuelPreburner,
            oxPreburner:            oxPreburner,
            efficiency:             FeedSystem.TurbineSizing.DefaultEfficiency);

        Turbopump.TurbineGeometry? fuelTurbGeom = null;
        Turbopump.TurbineGeometry? oxTurbGeom = null;
        // Sprint A-3 Phase 2 / ADR-021 (post-orchestrator-move): turbine
        // geometry attachment requires the caller to supply an
        // ITurbineGenerator — Core can't reach the Voxels-side
        // TurbineGeometryGenerator.Generate.
        if (cond.IncludeTurbopumpGeometry && turbine is not null && turbineGenerator is not null)
        {
            fuelTurbGeom = turbine.FuelTurbine is null ? null : turbineGenerator.Generate(turbine.FuelTurbine);
            oxTurbGeom   = turbine.OxTurbine   is null ? null : turbineGenerator.Generate(turbine.OxTurbine);
        }

        sized = sized with
        {
            Turbine             = turbine,
            FuelTurbineGeometry = fuelTurbGeom,
            OxTurbineGeometry   = oxTurbGeom,
        };

        // Shaft bending critical speed
        // advisory. Only runs when both the pump and turbine geometries
        // were generated for a side (so IncludeTurbopumpGeometry must be
        // true AND the matching pump/turbine must have sized). Advisory
        // only — whirl-band hits do NOT seed a feasibility gate, matching
        // the rim-stress convention. Warnings merge into the
        // existing TurbopumpResult.Warnings array so the UI surfaces them
        // alongside NPSH issues.
        FeedSystem.ShaftCriticalSpeedResult? fuelShaft = null, oxShaft = null;
        if (cond.IncludeTurbopumpGeometry && turbine is not null)
        {
            fuelShaft = FeedSystem.ShaftCriticalSpeed.Estimate(
                label:        "fuel",
                pump:         sized.FuelPumpGeometry,
                turbine:      sized.FuelTurbineGeometry,
                operatingRpm: sized.FuelPump?.Rpm ?? 0,
                layout:       design.ShaftLayout);
            oxShaft = FeedSystem.ShaftCriticalSpeed.Estimate(
                label:        "ox",
                pump:         sized.OxPumpGeometry,
                turbine:      sized.OxTurbineGeometry,
                operatingRpm: sized.OxPump?.Rpm ?? 0,
                layout:       design.ShaftLayout);
        }

        if (fuelShaft is not null || oxShaft is not null)
        {
            var shaftWarnings = new System.Collections.Generic.List<string>(sized.Warnings);
            if (fuelShaft is { WhirlOk: false }
                && FeedSystem.ShaftCriticalSpeed.FormatWarning(fuelShaft) is { } fw)
                shaftWarnings.Add(fw);
            if (oxShaft is { WhirlOk: false }
                && FeedSystem.ShaftCriticalSpeed.FormatWarning(oxShaft) is { } ow)
                shaftWarnings.Add(ow);
            sized = sized with
            {
                FuelShaft = fuelShaft,
                OxShaft   = oxShaft,
                Warnings  = shaftWarnings.ToArray(),
            };
        }

        return sized;
    }

    /// <summary>
    /// Pump inlet pressure with sensible auto-sizing fallback. When
    /// the user supplies a non-zero <see cref="OperatingConditions.PumpInletPressure_Pa"/>
    /// it wins; otherwise fall back to the existing
    /// <see cref="OperatingConditions.TankUllagePressure_Pa"/> (or
    /// 0.3 MPa default if neither is set).
    /// </summary>
    private static double ResolvePumpInlet(OperatingConditions cond)
    {
        if (cond.PumpInletPressure_Pa > 1.0) return cond.PumpInletPressure_Pa;
        if (cond.TankUllagePressure_Pa > 1.0) return cond.TankUllagePressure_Pa;
        return 0.3e6;     // 300 kPa = typical cryogenic NPSH-margin ullage
    }

    /// <summary>Pump discharge with auto-sizing fallback (Pc × 1.5).</summary>
    private static double ResolvePumpDischarge(OperatingConditions cond)
        => cond.PumpDischargePressure_Pa > 1.0
            ? cond.PumpDischargePressure_Pa
            : cond.ChamberPressure_Pa * 1.5;

    /// <summary>
    /// Bundle the chamber volume + steady-state mass-flow + dome
    /// volume into a <see cref="Combustion.StartTransientInputs"/>
    /// for the lumped start simulator.
    /// </summary>
    private static Combustion.StartTransientInputs BuildStartTransientInputs(
        OperatingConditions cond,
        RegenChamberDesign design,
        Chamber.ChamberContour contour,
        DerivedValues derived)
    {
        // Chamber volume = barrel length × πR² (rough — ignores the
        // converging-section integral). Adequate for a fill-time τ.
        double R_m = contour.ChamberRadius_mm * 1e-3;
        double L_m = contour.ChamberLength_mm * 1e-3;
        double V_chamber = System.Math.PI * R_m * R_m * L_m;

        // Dome volume = (fuel + ox) dome depth × dome cross-section.
        // Cross-section approximated as a cylinder at 95 % chamber R.
        double R_dome_m = 0.95 * R_m;
        double V_dome_ox   = System.Math.PI * R_dome_m * R_dome_m * design.OxDomeDepth_mm   * 1e-3;
        double V_dome_fuel = System.Math.PI * R_dome_m * R_dome_m * design.FuelDomeDepth_mm * 1e-3;
        double V_dome      = V_dome_ox + V_dome_fuel;
        if (V_dome <= 0) V_dome = 0.05 * V_chamber;   // 5 % of V_c fallback (no domes sized)

        // Density: weighted average of injection densities.
        var (oxRho, fuelRho) = Injector.OrificeModel.InjectionDensities(cond.PropellantPair);
        double rhoAvg = 0.5 * (oxRho + fuelRho);

        return new Combustion.StartTransientInputs(
            ValveOpenTime_s:            cond.StartValveOpenTime_s,
            IgniterDelay_s:             cond.StartIgniterDelay_s,
            DomeVolume_m3:              V_dome,
            DomePropellantDensity_kgm3: rhoAvg,
            SteadyMassFlow_kgs:         derived.TotalMassFlow_kgs,
            ChamberVolume_m3:           V_chamber,
            CStar_ms:                   derived.CStarActual_ms,
            // Math.Pow(x, 2) → x*x — audit 12-perf.md micro-fix (#557).
            ThroatArea_m2:              System.Math.PI * (derived.ThroatRadius_mm * 1e-3) * (derived.ThroatRadius_mm * 1e-3),
            ChamberPressure_Pa:         cond.ChamberPressure_Pa,
            SimulationDuration_s:       cond.StartSimulationDuration_s,
            TimeStep_s:                 cond.StartSimulationTimeStep_s,
            HardStartFactor:            cond.StartHardStartFactor,
            // Per-side ramps + dome volumes + steady flows. When the
            // user sets `OxStartValveOpenTime_s` / `FuelStartValveOpenTime_s`
            // to 0 (default), the simulator falls back to the shared ramp.
            // Per-side dome volumes default to a 50/50 split when both are 0.
            OxValveOpenTime_s:      cond.OxStartValveOpenTime_s,
            FuelValveOpenTime_s:    cond.FuelStartValveOpenTime_s,
            OxDomeVolume_m3:        V_dome_ox,
            FuelDomeVolume_m3:      V_dome_fuel,
            OxSteadyMassFlow_kgs:   derived.OxidizerMassFlow_kgs,
            FuelSteadyMassFlow_kgs: derived.FuelMassFlow_kgs);
    }

    /// <summary>
    /// Bundle the chamber + throat geometry + steady-state operating
    /// point into a <see cref="Combustion.ShutdownBlowdownInputs"/>
    /// for the lumped shutdown integrator. Reuses the start-transient
    /// valve-ramp fields on <see cref="OperatingConditions"/> as a
    /// proxy for the close-time (typical valves close in roughly the
    /// same time they open). Simulation duration is set generously
    /// (5 s) so the engine is reliably below subcritical Pc by the
    /// end of the run.
    /// </summary>
    private static Combustion.ShutdownBlowdownInputs BuildShutdownBlowdownInputs(
        OperatingConditions cond,
        Chamber.ChamberContour contour,
        DerivedValues derived)
    {
        double R_m = contour.ChamberRadius_mm * 1e-3;
        double L_m = contour.ChamberLength_mm * 1e-3;
        double V_chamber = System.Math.PI * R_m * R_m * L_m;

        // Conservative shutdown-window: 5 s × the start-transient
        // sim duration ceiling, capped at 5 s. Enough headroom for
        // typical decay (~50 ms exponential out of typical Pc) plus
        // residual propellant tail.
        double simDuration = System.Math.Max(cond.StartSimulationDuration_s, 5.0);

        return new Combustion.ShutdownBlowdownInputs(
            SteadyMassFlow_kgs:    derived.TotalMassFlow_kgs,
            ChamberPressure_Pa:    cond.ChamberPressure_Pa,
            ChamberVolume_m3:      V_chamber,
            CStar_ms:              derived.CStarActual_ms,
            // Math.Pow(x, 2) → x*x — audit 12-perf.md micro-fix (#557).
            ThroatArea_m2:         System.Math.PI * (derived.ThroatRadius_mm * 1e-3) * (derived.ThroatRadius_mm * 1e-3),
            ValveCloseTime_s:      cond.StartValveOpenTime_s,
            AmbientPressure_Pa:    cond.AmbientPressure_Pa,
            SimulationDuration_s:  simDuration,
            TimeStep_s:            cond.StartSimulationTimeStep_s,
            // Per-side close ramps mirror the per-side open ramps.
            OxValveCloseTime_s:    cond.OxStartValveOpenTime_s,
            FuelValveCloseTime_s:  cond.FuelStartValveOpenTime_s);
    }

    /// <summary>
    /// Bundle the regen-jacket wall mass + wetted area + material
    /// properties into a <see cref="HeatTransfer.ChilldownInputs"/>
    /// for the lumped chilldown integrator.
    /// </summary>
    private static HeatTransfer.ChilldownInputs BuildChilldownInputs(
        OperatingConditions cond,
        ChamberGeometryResult geom,
        HeatTransfer.RegenSolverOutputs thermal,
        HeatTransfer.WallMaterial material,
        Combustion.PropellantPairMetadata pairMeta,
        DerivedValues derived)
    {
        double tSat = HeatTransfer.ChilldownTransient.SaturationTemperature_K(pairMeta.CoolantFluidKey);
        return new HeatTransfer.ChilldownInputs(
            WallMass_kg:              geom.TotalMass_g * 1e-3,
            WallArea_m2:              thermal.TotalWettedArea_mm2 * 1e-6,
            WallSpecificHeat_Jkg:     material.SpecificHeat_Jkg,
            InitialWallTemp_K:        cond.ChilldownInitialJacketTemp_K,
            CoolantSaturationTemp_K:  tSat,
            CoolantMassFlow_kgs:      derived.FuelMassFlow_kgs,
            TwoPhaseHTC_Wm2K:         cond.ChilldownTwoPhaseHTC_Wm2K,
            DoneDeltaT_K:             cond.ChilldownDoneDeltaT_K,
            WallElasticModulus_Pa:    material.ElasticModulusCold_GPa * 1e9,
            WallCTE_perK:             material.CTE_perK,
            MaxTime_s:                cond.ChilldownMaxTime_s);
    }

    /// <summary>
    /// Run a hydrostatic proof-test analysis on a previously-generated design.
    /// Uses the existing thermal result as the station template but with all
    /// wall temperatures synthetically set to <paramref name="testTemp_K"/>.
    /// Returns elastic-burst margin + pass/fail at (proofFactor × MEOP).
    /// </summary>
    public static Structure.ProofTestResult EvaluateProofTest(
        RegenGenerationResult gen, RegenChamberDesign design)
    {
        var material = WallMaterials.All[
            Math.Clamp(gen.Conditions.WallMaterialIndex, 0, WallMaterials.All.Length - 1)];
        // Track B: forward the same per-station profile the steady-state
        // structural check uses, so proof testing scores the as-designed
        // wall (not the uniform baseline).
        int throatIdx = Structure.StructuralCheck.FindThroatStationIndex(gen.Thermal);
        double[] wallProfile = Structure.StructuralCheck.BuildGasSideWallProfile_mm(
            stationCount:        gen.Thermal.Stations.Length,
            throatIdx:           throatIdx,
            baseline_mm:         design.GasSideWallThickness_mm,
            chamberOverride_mm:  design.ChamberWallThicknessOverride_mm,
            throatOverride_mm:   design.ThroatWallThicknessOverride_mm,
            exitOverride_mm:     design.ExitWallThicknessOverride_mm);
        var result = Structure.ProofTestAnalysis.Evaluate(
            gen.Thermal, material,
            design.GasSideWallThickness_mm,
            gen.Conditions.ChamberPressure_Pa,
            design.ProofFactor,
            outerJacketThickness_mm: design.OuterJacketThickness_mm,
            gasSideWallProfile_mm:   wallProfile);
        // SPRINT 3: stamp the hash of the inputs this proof was run against.
        return result with { DesignHash = DesignProvenance.Compute(gen.Conditions, design) };
    }

    /// <summary>
    /// Run a Monte-Carlo tolerance sweep over manufacturing variability.
    /// Uses the existing contour + operating conditions; perturbs channel
    /// dimensions and wall thicknesses per LPBF typical tolerances.
    /// Pure math — no voxel regeneration; safe to call from the UI thread.
    /// </summary>
    public static Analysis.ToleranceResult EvaluateTolerance(
        RegenGenerationResult gen, RegenChamberDesign design,
        Analysis.ToleranceInputs inputs,
        System.Threading.CancellationToken cancellationToken = default)
    {
        // Thread cancellation through so the Stop
        // button / input-change can abort a stale sweep before its
        // 400-sample run completes.
        var result = Analysis.ToleranceAnalysis.Run(
            gen.Contour, gen.Conditions, design, inputs, cancellationToken);
        // SPRINT 3: stamp the hash of the inputs this sweep was run against.
        return result with { DesignHash = DesignProvenance.Compute(gen.Conditions, design) };
    }

    // ─────────────────────────────────────────────────────────────────
    //  Throat sizing and derived values
    // ─────────────────────────────────────────────────────────────────

    public static DerivedValues ComputeDerived(
        OperatingConditions cond, PropellantState gas, RegenChamberDesign design)
    {
        // Ideal thrust coefficient for chosen expansion ratio
        double eps = design.ExpansionRatio;
        double Me = PropellantTables.MachFromAreaRatio(eps, gas.Gamma, supersonic: true);
        double Pe = PropellantTables.StaticPressure(cond.ChamberPressure_Pa, Me, gas.Gamma);

        // Sprint 37 / PH-20 (2026-04-25): dual-bell sea-level / altitude
        // mode switch. Pre-Sprint-37, ComputeDerived used design.ExpansionRatio
        // (full outer bell ε) regardless of ambient pressure — at sea
        // level this double-counts the altitude-compensation benefit
        // because the flow actually separates at the contour inflection
        // and runs at the inner-bell ε_sea. Standard rule of thumb
        // (Hagemann 1998; Östlund 2005): flow separates when Pe / P_amb
        // < 0.4 (Summerfield criterion). When the design is dual-bell
        // and the full-ε exit pressure satisfies that criterion, swap
        // to the sea-level expansion ratio for C_F + Isp evaluation.
        // At altitude P_amb is small so Pe/P_amb is large → falls through
        // to full ε naturally. Bell-only designs are unaffected.
        const double SeparationCriterion = 0.4;
        if (design.IncludeDualBell
            && design.SeaLevelExpansionRatio > 0
            && cond.AmbientPressure_Pa > 0
            && Pe < SeparationCriterion * cond.AmbientPressure_Pa)
        {
            eps = design.SeaLevelExpansionRatio;
            Me = PropellantTables.MachFromAreaRatio(eps, gas.Gamma, supersonic: true);
            Pe = PropellantTables.StaticPressure(cond.ChamberPressure_Pa, Me, gas.Gamma);
        }
        double g = gas.Gamma;

        double term = 2.0 * g * g / (g - 1.0)
                    * Math.Pow(2.0 / (g + 1.0), (g + 1.0) / (g - 1.0))
                    * (1.0 - Math.Pow(Pe / cond.ChamberPressure_Pa, (g - 1.0) / g));
        double C_F_ideal = Math.Sqrt(Math.Max(term, 0))
                         + (Pe - cond.AmbientPressure_Pa) / cond.ChamberPressure_Pa * eps;

        // PH-19 (#176, 2026-04-29): decompose divergence loss from the
        // lumped NozzleCfEfficiency knob.
        //
        // Pre-PH-19, NozzleCfEfficiency = 0.94 lumped three distinct
        // losses — divergence λ_div(θ_e), boundary-layer η_BL, and
        // two-phase η_2Φ — into a single scalar. Result: SA saw no
        // Isp incentive to minimise bell exit angle θ_e because θ_e
        // affected only the bell's physical length, not C_F. Heavy
        // bells (low ε / low L% → large θ_e) and shallow bells (high
        // ε / high L% → small θ_e) scored identically on Isp.
        //
        // Post-PH-19:
        //     C_F = C_F_ideal · λ_div(ε, L%) · NozzleCfEfficiency_BL+2Φ
        // where λ_div = (1 + cos θ_e) / 2 from RaoBellTable. SA can now
        // trade bell length vs Isp: longer bells (higher L%) → smaller
        // θ_e → higher λ_div → higher C_F → smaller throat → higher Isp,
        // paid for in mass.
        //
        // Aerospike topologies (axisymmetric + linear) have axial exit
        // flow at the design point, so λ_div ≡ 1.0 — bypasses the table.
        // Dual-bell falls through naturally: the eps-swap block above
        // already swaps to SeaLevelExpansionRatio at separation, so the
        // λ_div lookup runs on the active expansion ratio (sea-level ε
        // when separated, full ε otherwise — both physically correct).
        double divergenceLoss;
        if (ChannelTopologyDispatcher.IsAerospike(design.ChannelTopology))
        {
            divergenceLoss = 1.0;
        }
        else
        {
            divergenceLoss = Chamber.RaoBellTable.DivergenceLossFactor(
                eps, design.BellLengthFraction);
        }
        double C_F = C_F_ideal * divergenceLoss * cond.NozzleCfEfficiency;
        if (C_F <= 0) C_F = 1.0;

        // Sprint 37b / PH-18 (2026-04-25): truncated-plug base-drag
        // correction for aerospike topology. Pre-Sprint-37b, ComputeDerived
        // used the bell C_F formula unconditionally — but a truncated
        // plug has a flat base at P_base ≈ 0.5 × P_ambient (Rao 1961;
        // Hagemann 1998), giving a base-drag term
        //     ΔC_F = (P_amb − P_base) · A_base / (P_c · A_t)
        //          = 0.5 · P_amb · A_base / (P_c · A_t)
        // pre-Sprint-37b absent. Empirically calibrated A_base/A_t ≈
        // 4 × (1 − PlugLengthRatio) for typical Angelino contours at
        // ε ∈ [25, 80] (matches Hagemann 1998 fig. 4 at PlugLengthRatio
        // = 0.30 within ~20 %). Bell-only and full-plug (pLR = 1.0)
        // designs are unaffected. SA was biased toward over-truncation
        // by the missing drag; post-fix lower pLR → lower C_F → bigger
        // throat → lower Isp → SA pushes back.
        if (ChannelTopologyDispatcher.IsAerospikeAxisymmetric(design.ChannelTopology)
            && design.PlugLengthRatio < 1.0
            && cond.AmbientPressure_Pa > 0)
        {
            double pLR = Math.Clamp(design.PlugLengthRatio, 0.1, 1.0);
            double pAmbRatio = cond.AmbientPressure_Pa
                             / Math.Max(cond.ChamberPressure_Pa, 1);
            double baseDragLoss = 2.0 * pAmbRatio * (1.0 - pLR);
            C_F = Math.Max(C_F - baseDragLoss, 0.1);
        }

        // Throat area from F = C_F · P_c · A_t
        double A_t_m2 = cond.Thrust_N / (C_F * cond.ChamberPressure_Pa);
        double R_t_mm = Math.Sqrt(A_t_m2 / Math.PI) * 1000.0;

        // PH-37 (2026-04-29): C* efficiency derate from film-cooling
        // boundary-layer blockage. η_C*_film = 1 − 0.30·filmFraction
        // applied alongside cond.CStarEfficiency. Closes a scoring loophole
        // where the SA optimizer could drive FilmFuelFraction high without
        // paying any C* penalty. Formula at FilmCooling.CStarEfficiencyFactor.
        // Applied to BOTH Cstar_eff (so mDot rises) AND IspVac (so the
        // thermodynamic relation Isp = C*·C_F/g₀ stays consistent and the
        // SA scoring sees a real Isp drop on film-heavy designs).
        double cstarFilmBlockage = HeatTransfer.FilmCooling.CStarEfficiencyFactor(
            design.FilmFuelFraction);

        // Mass flow from ṁ = P_c · A_t / C*_effective
        double Cstar_eff = gas.CStar_ms * cond.CStarEfficiency * cstarFilmBlockage;
        double mDot = cond.ChamberPressure_Pa * A_t_m2 / Math.Max(Cstar_eff, 1);
        double mDotFuel = mDot / (1.0 + cond.MixtureRatio);
        double mDotOx = mDot - mDotFuel;

        double IspVac = gas.IspVacuum_s * cstarFilmBlockage;
        double IspSl = IspVac * (C_F / (C_F + (cond.AmbientPressure_Pa / cond.ChamberPressure_Pa) * eps));

        return new DerivedValues
        {
            ThroatRadius_mm = R_t_mm,
            ThroatDiameter_mm = 2 * R_t_mm,
            ChamberRadius_mm = R_t_mm * Math.Sqrt(design.ContractionRatio),
            ExitRadius_mm = R_t_mm * Math.Sqrt(design.ExpansionRatio),
            TotalMassFlow_kgs = mDot,
            FuelMassFlow_kgs = mDotFuel,
            OxidizerMassFlow_kgs = mDotOx,
            IdealIspVacuum_s = IspVac,
            IdealIspSeaLevel_s = IspSl,
            ThrustCoefficient = C_F,
            CStarActual_ms = Cstar_eff,
            ChamberTemp_K = gas.ChamberTemp_K,
            DivergenceLoss = divergenceLoss,
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  Scoring
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Score a regen-chamber design generation result against an explicit
    /// scoring profile. Replaces the parameterless Evaluate(gen) which read
    /// the static _profileIndex — see issue #551 / ADR-042. No static-state
    /// read: the profile is supplied by the caller, so two concurrent
    /// invocations cannot interfere via shared mutable state.
    /// </summary>
    /// <param name="gen">Generation result to be scored.</param>
    /// <param name="profile">Scoring profile (weights for each cost term).
    /// Use one of the entries in <see cref="Profiles"/> for behaviour
    /// equivalent to the legacy static-state callsite.</param>
    public static RegenScoreResult Evaluate(RegenGenerationResult gen, ScoringProfile profile)
    {
        // ── Hard-constraint gates (UPGRADE 3 + 4) ───────────────────────────
        // Any violation → TotalScore = +∞ so the SA optimizer unconditionally
        // rejects the candidate. All violations are collected and returned
        // in the result for diagnostic display.
        var gate = FeasibilityGate.Evaluate(gen);

        // Voxel-adequacy gate: if the result carries a voxel check and it failed,
        // add a synthetic violation so the same +∞ path is taken.
        var allViolations = new List<FeasibilityViolation>(gate.Violations);
        if (gen.VoxelAdequacy?.Overall == VoxelAdequacyLevel.Fail)
        {
            // Report the single worst (lowest-ratio) failing feature
            FeatureAdequacy? worst = null;
            foreach (var f in gen.VoxelAdequacy.Features)
                if (f.Level == VoxelAdequacyLevel.Fail && (worst == null || f.VoxelRatio < worst.VoxelRatio))
                    worst = f;

            if (worst != null)
                allViolations.Add(new FeasibilityViolation(
                    ConstraintId: "VOXEL_RESOLUTION",
                    Description:  $"Feature '{worst.FeatureName}' ({worst.FeatureSize_mm:F3} mm) = {worst.VoxelRatio:F2}× voxel — minimum 2× required.",
                    ActualValue:  worst.VoxelRatio,
                    Limit:        VoxelAdequacyGate.FailRatioThreshold));
        }

        var p = profile;
        var t = gen.Thermal;
        var s = gen.Stress;
        var m = gen.Manufacturing;
        var mat = WallMaterials.All[gen.Conditions.WallMaterialIndex];

        // ── Sprint 11 Track F (2026-04-23) — aerospike-aware scoring ─────
        // Sprint 2b step 3 introduced `effectiveMass_g` (aerospike mass
        // when sidecar is present). Track F extends that pattern to
        // ALL thermal scoring inputs so SA actually optimises the
        // aerospike's plug thermal on aerospike baselines instead of
        // the fallback bell-chamber compute that `gen.Thermal` carries.
        //
        // Surface mapping (aerospike branch → scoring variable):
        //   Aerospike.Thermal.PeakGasSideWallT_K      → peakT
        //   Aerospike.Thermal.CoolantPressureDrop_Pa  → coolantDP_Pa
        //   Aerospike.Thermal.CoolantOutletT_K        → coolantOutletT_K
        //   Aerospike.Thermal.TotalHeatLoad_W         → totalHeatLoad_W
        //   max(Aerospike.Thermal.HeatFlux_Wm2[])     → peakHeatFlux_Wm2
        //     (aerospike has no single "throat" flux — report the peak
        //      station flux as the closest analogue for score + UI)
        // Manufacturing (feature size, structural SF) still reads the
        // regen path — aerospike-specific manufacturing analysis is a
        // later-sprint refinement; regen-fallback MinFeatureSize_mm is
        // still representative for LPBF of a similar-scale plug.
        bool isAerospike = gen.Aerospike is not null;
        var aeroThermal = gen.Aerospike?.Thermal;
        bool hasAeroThermal = isAerospike && aeroThermal is not null;

        double peakT = hasAeroThermal
            ? aeroThermal!.PeakGasSideWallT_K
            : t.PeakGasSideWallT_K;
        double limitT = mat.MaxServiceTemp_K;
        double excessT = Math.Max(0, peakT - limitT);
        double penaltyWallT = p.WallTPenalty * excessT * excessT;

        double wallTAvgScore = p.WallTAvg * Math.Max(0, peakT - 500);

        double coolantDP_Pa = hasAeroThermal
            ? aeroThermal!.CoolantPressureDrop_Pa
            : t.CoolantPressureDrop_Pa;
        double dPFrac = coolantDP_Pa / Math.Max(gen.Conditions.ChamberPressure_Pa, 1);
        double dPScore = p.DPWeight * dPFrac * 100;   // scale to O(1) for typical 5–15 % ΔP

        // Sprint 2b step 3 (2026-04-22) + Sprint 11 Track F: effective mass
        // comes from the aerospike sidecar on aerospike baselines. The regen
        // fallback `gen.Geometry.TotalMass_g` is the bell-chamber compute
        // path that always runs — not the aerospike body the user asked for.
        double effectiveMass_g = isAerospike
            ? gen.Aerospike!.EstimatedMass_g
            : gen.Geometry.TotalMass_g;
        double massScore = p.MassWeight * effectiveMass_g;

        double featureScore = 0;
        if (!m.FeatureSizeOK)
            featureScore = p.FeatureWeight * Math.Max(0, 0.40 - m.MinFeatureSize_mm) * 10;

        double structScore = 0;
        if (s.MinSafetyFactor < 1.2)
        {
            // Math.Pow(x, 2) → x*x — audit 12-perf.md micro-fix (#557).
            double slack = 1.2 - s.MinSafetyFactor;
            structScore = p.StructuralWeight * slack * slack * 20;
        }

        // Coolant outlet T — reward higher coolant ΔT (better regen capability)
        double coolantOutletT_K = hasAeroThermal
            ? aeroThermal!.CoolantOutletT_K
            : t.CoolantOutletT_K;
        double dT_cool = Math.Max(0, coolantOutletT_K - gen.Conditions.CoolantInletTemp_K);
        double coolantScore = -p.CoolantTWeight * dT_cool;  // negative: bigger ΔT lowers cost

        // Large ΔP/Pc (>25 %) is operationally infeasible — hard penalty
        if (dPFrac > 0.25) dPScore += 100 * (dPFrac - 0.25);

        // SPRINT 1.3: soft injector-ratio penalties. Velocity ratio target band
        // [0.5, 4.0] (Sutton §9.5); momentum ratio target [0.6, 1.5] (Huzel §8.2).
        // Penalty is quadratic in out-of-band distance. Zero for profiles that
        // don't care (InjectorRatioWeight == 0). Requires a sized pattern.
        double injectorRatioScore = 0;
        if (p.InjectorRatioWeight > 0 && gen.InjectorSizing is { } sizing)
        {
            double vr = sizing.PerElementResult.VelocityRatio;
            double mr = sizing.PerElementResult.MomentumRatio;
            double vrExcess = vr < 0.5 ? (0.5 - vr) : vr > 4.0 ? (vr - 4.0) : 0.0;
            double mrExcess = mr < 0.6 ? (0.6 - mr) : mr > 1.5 ? (mr - 1.5) : 0.0;
            injectorRatioScore = p.InjectorRatioWeight * (vrExcess * vrExcess + mrExcess * mrExcess);
        }

        // Feasibility flags
        bool infeasFeature = !m.FeatureSizeOK;
        bool yieldExceeded = s.MinSafetyFactor < 1.0;

        double total = penaltyWallT + wallTAvgScore + dPScore + massScore
                     + featureScore + structScore + coolantScore
                     + injectorRatioScore;

        // Override: infeasible designs get +∞ score regardless of soft penalties.
        if (allViolations.Count > 0)
            total = double.PositiveInfinity;

        // ── Sprint 11 Track F: surface aerospike-branch scalars on the
        //    RegenScoreResult so the UI + report writer + Pareto panel
        //    read the aerospike values (not the fallback bell-chamber
        //    compute) when scoring went through the aerospike path.
        bool wallTExceeded = hasAeroThermal
            ? (peakT > limitT)
            : t.WallTempExceedsLimit;
        double wallTMargin_K = hasAeroThermal
            ? (limitT - peakT)
            : t.WallMarginK;
        double reportedTotalHeatLoad_W = hasAeroThermal
            ? aeroThermal!.TotalHeatLoad_W
            : t.TotalHeatLoad_W;
        // Aerospike has no single "throat heat flux" — report the peak
        // per-station flux from the plug thermal sweep as its analogue.
        double reportedThroatHeatFlux_Wm2;
        if (hasAeroThermal && aeroThermal!.HeatFlux_Wm2.Length > 0)
        {
            double maxQ = 0;
            foreach (double q in aeroThermal.HeatFlux_Wm2)
                if (q > maxQ) maxQ = q;
            reportedThroatHeatFlux_Wm2 = maxQ;
        }
        else
        {
            reportedThroatHeatFlux_Wm2 = t.ThroatHeatFlux_Wm2;
        }

        // Sprint 14 / Track I / P9: pre-size at 8 — typical Evaluate
        // collects 0–6 warnings (thermal + manufacturing + 1–2 gate
        // descriptions); 8 covers the common case without growing.
        var warnings = new List<string>(8);
        // Warnings from the fallback regen path only apply to a non-aerospike
        // design — forwarding them on aerospike baselines pollutes the
        // warnings panel with bell-chamber diagnostics that describe a
        // compute path the user isn't using.
        if (!isAerospike) warnings.AddRange(t.Warnings);
        else if (aeroThermal is not null) warnings.AddRange(aeroThermal.Warnings);
        warnings.AddRange(m.Warnings);
        if (yieldExceeded) warnings.Add($"Yield exceeded: SF={s.MinSafetyFactor:F2}");
        if (wallTExceeded) warnings.Add($"Wall T exceeds {limitT:F0}K: peak={peakT:F0}K");
        // #557 item 5 — skip the per-violation INFEASIBLE string-build on the
        // infeasibility-trip path: SA rejects +∞ scores outright and never
        // reads the resulting strings, so building ~3-5 interpolated strings
        // per infeasible candidate burns ~5 µs/call for no consumer benefit.
        // The raw violations are preserved verbatim in FeasibilityViolations
        // below for callers that need the structured data.
        if (!double.IsPositiveInfinity(total))
        {
            foreach (var v in allViolations)
                warnings.Add($"[INFEASIBLE] {v.ConstraintId}: {v.Description}");
        }

        var preliminaryScore = new RegenScoreResult(
            TotalScore: total,
            PeakWallT_K: peakT,
            WallTMargin_K: wallTMargin_K,
            CoolantDP_Pa: coolantDP_Pa,
            CoolantDP_Fraction: dPFrac,
            CoolantTOut_K: coolantOutletT_K,
            TotalHeatLoad_W: reportedTotalHeatLoad_W,
            ThroatHeatFlux_Wm2: reportedThroatHeatFlux_Wm2,
            Mass_g: effectiveMass_g,
            Cost_USD: gen.Geometry.PrintedCost_USD,
            MinFeatureSize_mm: m.MinFeatureSize_mm,
            MinSafetyFactor: s.MinSafetyFactor,
            WallTExceeded: wallTExceeded,
            YieldExceeded: yieldExceeded,
            InfeasibleFeature: infeasFeature,
            Warnings: warnings.ToArray(),
            FeasibilityViolations: allViolations.ToArray());
        // TIER A.3: build the structured severity list from all sources.
        return preliminaryScore with
        {
            StructuredWarnings = WarningAggregator.BuildFor(gen, preliminaryScore),
        };
    }
}
