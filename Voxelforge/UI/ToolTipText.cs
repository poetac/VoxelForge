// ToolTipText.cs — Centralised tooltip strings for every input on
// RegenChamberForm.
//
// One sentence per field + the typical range so a user staring at
// "BurnoutLength_mm" doesn't have to dig through the physics module
// to learn what value to use. Strings live here rather than buried
// in the constructor so adding / editing tooltips doesn't churn
// the form layout.
//
// Pattern:
//   var tip = new ToolTip();
//   tip.SetToolTip(nudThrustN, ToolTipText.ThrustN);
// Call WireTooltips() from the form constructor; it walks every
// public-tooltip control and binds the string in one place.

namespace Voxelforge.UI;

internal static class ToolTipText
{
    // ── Operating Point group ────────────────────────────────────
    public const string PropellantPair =
        "Propellant pair (LOX/CH4, LOX/H2, LOX/RP-1 implemented; "
        + "N2O4/MMH and H2O2/RP-1 are stubs and will hard-fail). "
        + "Selecting an unsupported pair disables Generate.";
    public const string ThrustN =
        "Vacuum thrust target [N]. Throat diameter is sized so the "
        + "design hits this thrust at the chosen Pc. Typical demo: "
        + "500-2225 N (small test article); 50-150 kN (sub-scale launch).";
    public const string ChamberPressurePsi =
        "Chamber pressure [psia]. Sets the propellant flow rate via "
        + "ṁ = Pc·A_t/c*. Typical 300-2000 psia (2-14 MPa); higher Pc = "
        + "higher Isp + higher heat flux + tougher cooling problem.";
    public const string MixtureRatio =
        "Oxidiser/fuel mass ratio (MR). Auto-snapped to the pair's "
        + "MR_Min/MR_Max band on selection. Defaults set near each pair's "
        + "peak C* (3.3 for LOX/CH4, 4.0 for LOX/H2, 2.56 for LOX/RP-1).";
    public const string CoolantInletTempK =
        "Coolant inlet temperature [K]. Cryogenic: 110-150 K (LCH4), "
        + "20-30 K (LH2). Liquid: 290-340 K (RP-1 at room T).";
    public const string CoolantInletPressureMPa =
        "Coolant inlet pressure [MPa]. Sets the supercritical regime "
        + "for cryogenic pairs (CH4: pseudocritical near 200 K @ 10 MPa). "
        + "Typical 8-15 MPa for regen jackets.";
    public const string WallMaterial =
        "Wall material from the 4-entry library: GRCop42, CuCrZr, "
        + "Inconel 625, Inconel 718. Conductivity, yield, CTE, max "
        + "service T, and printed-cost-per-cm³ all per-material in "
        + "WallMaterials.cs. Surfaces in feasibility gate WALL_TEMP.";
    public const string BartzFactor =
        "Multiplicative correction on Bartz gas-side HTC (1.0 = literature "
        + "Bartz). Set by the Hardware Validation Overlay after a hot-fire "
        + "or cold-flow comparison. Clamped to [0.2, 3.0] in the solver.";

    // ── Nozzle Geometry group ────────────────────────────────────
    public const string ContractionRatio =
        "Chamber/throat area ratio ε_c = A_c/A_t. Typical 4-8 for "
        + "small engines, 2-3 for large. Higher = bigger chamber, "
        + "lower wall heat flux, more L*. Range [3, 10].";
    public const string ExpansionRatio =
        "Exit/throat area ratio ε_e = A_e/A_t. Sea-level optimal "
        + "≈ 8; vacuum-optimal ≈ 25-200. Range [3, 25] in this tool.";
    public const string LStar =
        "Characteristic length L* [m] — chamber volume / throat area. "
        + "Sets residence time. LOX/CH4 typical 1.0-1.3 m; LOX/RP-1 "
        + "1.0-1.5 m; LOX/H2 0.7-1.0 m.";
    public const string ThetaN =
        "Bell-nozzle entrance angle θ_n [°] (Rao convention). "
        + "Typical 20-40°.";
    public const string ThetaE =
        "Bell-nozzle exit angle θ_e [°]. Typical 6-16°. "
        + "Smaller = longer bell, lower divergence loss.";
    public const string BellLengthFraction =
        "Bell length as a fraction of equivalent 15° conical nozzle. "
        + "0.8 (Rao 80% bell) is standard; 0.6 = compact, 0.9 = high-η.";

    // ── Cooling Channels group ───────────────────────────────────
    public const string ChannelCount =
        "Number of axial cooling channels around the chamber. "
        + "Typical 40-180; higher = thinner ribs (lower stress) but "
        + "less channel width (lower mass flow per channel).";
    public const string ChannelHeightChamber =
        "Channel height at the chamber barrel [mm]. Typical 1.5-3.5 mm.";
    public const string ChannelHeightThroat =
        "Channel height at the throat [mm]. Typically smaller than "
        + "chamber (0.8-2.0 mm) so the coolant accelerates through the "
        + "highest-q region — boosts h_c where it's needed most.";
    public const string ChannelHeightExit =
        "Channel height at the bell exit [mm]. Typical 1.0-3.0 mm.";
    public const string RibThickness =
        "Rib thickness between channels [mm]. LPBF floor 0.5 mm; "
        + "thicker = stiffer + better fin conduction; thinner = more "
        + "channel area for the same chamber circumference.";
    public const string WallThickness =
        "Gas-side wall thickness [mm]. LPBF floor 0.5 mm; thicker = "
        + "lower hot-side T but smaller temperature gradient (less "
        + "thermal stress). Range [0.5, 2.0] in the SA optimiser.";
    public const string JacketThickness =
        "Outer jacket thickness [mm]. Carries hoop stress from coolant "
        + "pressure. Typical 1.5-3.0 mm.";
    public const string SmoothingRadius =
        "Final chamfer radius applied to the outer solid [mm]. 0 = "
        + "no smoothing. Capped at 25% of min wall thickness so it "
        + "can't destroy thin features (Smoothen safety rule).";
    public const string ManifoldLength =
        "Inlet/outlet coolant manifold axial length [mm]. Plenum that "
        + "distributes flow into the channels. Typical 8-25 mm.";
    public const string CoolantPortDiameter =
        "Coolant inlet/outlet port diameter [mm]. Typical 6-12 mm. "
        + "Threaded option drilled per CoolantPortStandard.";
    public const string CoolantPortThread =
        "Threaded port standard for coolant inlet/outlet (Plain / "
        + "G / NPT / SAE / miniature M5-M8). Drilled as helical "
        + "groove on a boss cylinder.";
    public const string ChannelManifoldFillet =
        "Fillet radius at axial channel ends where they meet manifold "
        + "plenums [mm]. Default 0.8 mm rounds the rib-termination "
        + "stress concentrator. 0 = sharp (legacy).";

    // ── Flanges group ────────────────────────────────────────────
    public const string InjectorFlangeThk =
        "Injector flange thickness [mm]. Typical 6-15 mm. Must be "
        + "thick enough for dome depth + threaded boss penetrations.";
    public const string InjectorFlangeORFactor =
        "Injector flange OR / chamber R. Typical 1.20-1.40. >= 1.05 "
        + "to give bolt-circle clearance outside the chamber.";
    public const string PropellantPortDia =
        "Axial propellant port diameter through the injector flange "
        + "[mm]. Typical 4-10 mm.";
    public const string PropellantPortThread =
        "Threaded port standard for axial propellant ports through the "
        + "injector flange. Threaded propellant ports demote "
        + "StructuralConfidence to Low (highest stress-concentration "
        + "risk on the analytical VM check).";
    public const string MountFlangeThk =
        "Nozzle-exit mount flange thickness [mm]. Typical 4-10 mm.";
    public const string MountBoltPattern =
        "Bolt-pattern preset for the mount flange (Generic 8-bolt, "
        + "MIL-STD 4-bolt, MIL-STD 6-bolt clocked, ASME B16.5).";

    // ── Film Cooling group ───────────────────────────────────────
    public const string FilmFraction =
        "Fraction of fuel mass flow diverted to wall film [0-0.30]. "
        + "5-10 % is typical for LOX/CH4 at high Pc; cuts peak wall T "
        + "by 400-1200 K but penalises Isp roughly 80% of film fraction.";
    public const string FilmSlotH =
        "Film slot height (radial thickness of injected film) [mm]. "
        + "Typical 0.3-1.0 mm.";
    public const string FilmInjectionX =
        "Axial position where film is injected [mm from injector face]. "
        + "0 = injector face (most common); larger pulls film deeper.";
    public const string FilmInletT =
        "Film injection temperature [K]. Defaults to coolant inlet T.";
    public const string FilmBurnoutL =
        "Burnout length [mm] over which the film fully mixes with the "
        + "core. Stechman correlation defaults around 100-300 mm.";
    public const string FilmDecayCoef =
        "Stechman decay coefficient β. Typical 0.05-0.30; default 0.15. "
        + "Higher = faster film burnout. Calibrate from CFD or hot-fire.";
    public const string FilmThroatMix =
        "Throat-region mixing penalty on η. 0.20-0.30 typical (degrades "
        + "film effectiveness through the throat by 20-30 %).";

    // ── Optimisation group ───────────────────────────────────────
    public const string MaxIterations =
        "Maximum SA iterations. 100-500 typical for a ~1-3 min run on "
        + "an 8-core box (with Fast SA on). 300 is a good demo default.";
    public const string Seed =
        "Random seed for the SA. Same seed + same conditions produces "
        + "the same Pareto front + best — useful for regression checks.";
    public const string ScoringProfile =
        "Pre-defined weight profile over (peak T, ΔP, mass, feature, "
        + "structural, coolant ΔT). 'Balanced' is the default; "
        + "'Min Wall T' / 'Min Pressure Drop' / 'Min Material' / "
        + "'Max Isp Path' / 'Max Injector Uniformity' for asymmetric goals.";
    public const string WarmStart =
        "Start SA from the current design rather than the centre of "
        + "the parameter bounds. Keep ON unless you want a fresh search.";
    public const string ParallelSa =
        "Fast SA: parallelises 8 candidate evaluations on the physics-"
        + "only path (skipVoxelGeometry: true) per StepOpt. ~6-8× wall-"
        + "clock speedup on an 8-core box.";
    public const string MultiChainSa =
        "Multi-chain SA: spawns N independent SA chains (auto-scales to "
        + "ProcessorCount−2, max 16) running in parallel. Each chain has "
        + "its own RNG, cooling schedule, and restart history; periodic "
        + "elite migration cross-pollinates the global best every 100 "
        + "iters. Sobol-sequence seeds the first 64 candidates per chain "
        + "for better-than-uniform initial coverage. Strict determinism: "
        + "same seed + chain count → identical result. Supersedes the Fast "
        + "SA batch path; greys it out when checked.";

    public const string NsgaIi =
        "NSGA-II multi-objective: replaces SA with a Pareto-front "
        + "optimizer that simultaneously minimises peak wall temperature, "
        + "coolant pressure drop, and chamber mass (Deb et al. 2002). "
        + "The three-axis scatter in the Pareto panel is populated as "
        + "the run completes. Population size must be even (step 4). "
        + "Mutually exclusive with Multi-chain SA and Fast SA.";

    // ── Mesh resolution group ────────────────────────────────────
    public const string StlExportVoxel =
        "Export voxel size [mm]. Smaller = higher-fidelity STL but "
        + "longer export. Different from the session voxel: any "
        + "non-session size routes through a headless subprocess "
        + "asynchronously. 0.30 ≈ seconds, 0.20 ≈ 30 s, 0.10 ≈ minutes.";

    // ── Proof / tolerance ────────────────────────────────────────
    public const string ProofFactor =
        "Hydrostatic proof-test pressure factor. 1.5 × MEOP (chamber P) "
        + "is standard; flight hardware typically 1.5-2.0×.";
    public const string TolSamples =
        "Monte-Carlo sample count for the LPBF tolerance sweep. "
        + "400 = ~3 s on the parallel path; bump to 1000+ for tail "
        + "quantile (P99) accuracy.";
    public const string TolWall =
        "Wall + jacket tolerance ± [mm], 3σ band. LPBF default 0.10 mm.";
    public const string TolChannel =
        "Channel + rib tolerance ± [mm], 3σ band. LPBF default 0.10 mm.";

    // ── Feed system / chilldown / start transient / engine cycle ──
    public const string TankUllageMPa =
        "Tank ullage pressure [MPa]. 0 = feed-system stackup is "
        + "skipped (default). Set > 0 to opt the stackup in. Typical "
        + "pressure-fed: 1.3-1.6 × Pc.";
    public const string FilterPreset =
        "Inline propellant filter preset. Custom (default) reads the "
        + "legacy FilterDeltaP_Pa scalar; named presets supersede with "
        + "a tabulated clean ΔP + dirty multiplier.";
    public const string FilterContamination =
        "Filter contamination state on a 0-1 scale. 0 = clean / fresh; "
        + "1 = end-of-life. Linearly interps clean → clean × DirtyMultiplier.";
    public const string UmbilicalStandard =
        "Ground-side umbilical / quick-disconnect preset. Feeds K·½ρv² "
        + "loss into the feed stackup AND drills a seal-groove + 4-bolt "
        + "pattern around each propellant port.";

    public const string ChilldownEnable =
        "Enable the pre-fire chilldown integrator. Skipped on RP-1 "
        + "(non-cryogenic) regardless of this flag.";
    public const string ChilldownInitT =
        "Initial regen-jacket wall temperature [K]. 298 = sea-level ambient.";
    public const string ChilldownHTC =
        "Effective two-phase HTC [W/m²K]. 5000 sits in the Chen / Shah "
        + "transition-boiling envelope for LCH4 / LH2 against warm metal.";
    public const string ChilldownDoneDT =
        "Done threshold [K] — chilldown complete when T_jacket - T_sat "
        + "drops below this. Default 50 K.";
    public const string ChilldownMaxT =
        "Max acceptable chilldown time [s]. Soft gate fires when integrated "
        + "time exceeds this.";

    public const string StartTransientEnable =
        "Enable the lumped 0-D start-transient simulator.";
    public const string ValveOpenMs =
        "Shared valve open ramp [ms]. Used for both ox + fuel unless "
        + "the per-side overrides below are set.";
    public const string OxValveOpenMs =
        "Ox-side valve open override [ms]. 0 = use the shared ramp. "
        + "Lead one side to mitigate hard-start (fuel-lead = small, ox-lag = larger).";
    public const string FuelValveOpenMs =
        "Fuel-side valve open override [ms]. 0 = use the shared ramp.";
    public const string IgniterDelayMs =
        "Igniter delay [ms] from valve-open command. Pre-ignition pooled "
        + "propellant feeds the hard-start spike estimate.";
    public const string StartSimDurMs =
        "Total simulation duration [ms]. 1000 typical for a small engine.";
    public const string StartSimDtMs =
        "Time step [ms] for the explicit Euler integrator. 1.0 typical.";
    public const string HardStartFactor =
        "Hard-start risk threshold — predicted Pc overshoot above which "
        + "HARD_START_RISK fires. 0.5 (50 %) per Sutton §10.6.";

    public const string EngineCycle =
        "Engine cycle. PressureFed (default) skips turbopump sizing; "
        + "GasGenerator / ElectricPump / OpenExpander run per-pump head "
        + "+ shaft power + RPM + NPSH check.";
    public const string PumpInletPMPa =
        "Pump inlet pressure [MPa]. 0 = auto-resolve from TankUllage or "
        + "0.3 MPa default.";
    public const string PumpDischargePMPa =
        "Pump discharge pressure [MPa]. 0 = auto-size to Pc × 1.5.";
    public const string PumpEfficiency =
        "Centrifugal-pump efficiency η. 0.65 typical for LRE turbopump impellers.";

    // ── Channel topology ─────────────────────────────────────────
    public const string ChannelTopology =
        "Cooling-channel topology: Axial (default), Helical (ChannelTopology.Helical "
        + "spirals at HelixPitchAngle_deg), or None (ablative-only — wall + jacket "
        + "shell, no regen channels).";

    // ── Geometry feature toggles (checkboxes) ────────────────────
    // Tooltips for the 14 checkbox controls. Same centralised-string
    // pattern as above; same WireTooltips() call site.
    public const string IncludeManifolds =
        "Build inlet + outlet coolant manifold plenums around the chamber. "
        + "OFF strips the manifolds (pure channel-only slice — useful for "
        + "cross-section renders). Leave ON for any real design.";
    public const string IncludeCoolantPorts =
        "Drill the radial coolant inlet + outlet ports through the jacket. "
        + "OFF leaves the manifolds blind (no external connection). Turn OFF "
        + "only when a custom port layout will be added downstream.";
    public const string IncludeInjectorFlange =
        "Extrude the injector flange solid off the chamber head-end. Required "
        + "for any propellant-port / injector-STL workflow. OFF is the cut-away "
        + "mode (chamber cavity exposed).";
    public const string IncludeMountFlange =
        "Extrude the nozzle-exit mount flange. ON draws the selected "
        + "MountingFlangePresets standard (MIL-STD, ASME B16.5, etc.) with "
        + "its bolt-circle. OFF leaves the nozzle exit plane un-flanged.";
    public const string EnableFilmCooling =
        "Enable the Stechman η fuel-film model. When OFF the coolant jacket "
        + "carries the whole heat load; when ON the FilmFraction is diverted "
        + "to the wall and the regen solver couples through η(x).";
    public const string ImportInjectorSTL =
        "Voxel-boolean an external injector face STL onto the chamber head-end. "
        + "Mesh must be closed + watertight; scaling + axial offset below "
        + "position it. Leave OFF to use the built-in parametric injector.";
    public const string AutoCenterInjectorSTL =
        "Auto-center the imported STL on the chamber axis in Y + Z. OFF uses "
        + "the raw STL coordinates (turn OFF only if the source STL is "
        + "intentionally off-axis).";
    public const string LivePreview =
        "Regenerate on every parameter edit. Each rebuild is 3-10 s at the "
        + "default 0.4 mm voxel — OFF stages all edits and you click "
        + "Generate once when done. Persists across restarts.";
    public const string RunAllAnalyses =
        "Master switch: flips chilldown + start-transient + ullage stackup + "
        + "GasGenerator cycle on in one click, so a new user sees the full "
        + "analysis stack. Unchecking restores the previous per-toggle values.";
    public const string DemotePriority =
        "Drop the process to BelowNormal priority during SA + tolerance "
        + "sweeps so foreground apps (IDE, browser) stay responsive. Restored "
        + "to Normal on op completion. Recommended ON for dev boxes.";
    public const string BatteryAwareQuiet =
        "On battery power, auto-flip to the Quiet preset (halved cores, "
        + "lower memory cap). Keeps a laptop cool + extends runtime when "
        + "unplugged. Reverts on AC reconnect.";
    public const string AdaptiveForegroundThrottle =
        "When the form loses focus (user Alt+Tabs away), scale parallelism "
        + "down to half the budget so the machine stays snappy for whatever "
        + "they switched to. Restores full budget when focus returns.";
    public const string GcLatencyTuning =
        "Flip GC to SustainedLowLatency during heavy ops so long collection "
        + "pauses don't punctuate a solve. Small memory overhead; restored "
        + "on op completion.";
    public const string AbortOpOnInputEdit =
        "When ON, editing a design input during a running SA / tolerance "
        + "sweep cancels the in-flight run (its outputs would be stale "
        + "anyway). OFF preserves legacy behaviour — edits don't touch the "
        + "current run.";
    public const string AutoCoarsenVoxel =
        "When ON, if a Generate / finalize voxel build would exceed the "
        + "memory budget, automatically retry at a coarser voxel size "
        + "instead of blocking. Lets large-thrust designs render at lower "
        + "fidelity. The status bar announces the substitution (\"Voxel "
        + "auto-coarsened 0.40 → 0.85 mm to fit budget\"). OFF (default) "
        + "preserves the strict behaviour — the build is refused and the "
        + "suggested voxel is surfaced so the user can retype it.";
    public const string FastPreview =
        "When ON, manual Generate clicks skip channel voxelisation (~84 % "
        + "of build time at 0.4 mm voxel) and render a bare shell so you "
        + "can iterate on thrust / material / contour rapidly. Full physics "
        + "+ feasibility + mass + cost still compute. Your saved design is "
        + "never modified — SA / Save / Export always use the real topology. "
        + "OFF (default) renders the full flow path.";
    public const string TileLargeBuilds =
        "When ON, Generate splits the voxel build into Tile-Count axial "
        + "slices, meshes each, and welds them. Peak memory ≈ 1/N of the "
        + "monolithic build — lets large-thrust (>10 kN) designs render at "
        + "full voxel fidelity on a capped memory budget. OFF (default) "
        + "uses the monolithic build. SA / Save / Export ignore this flag.";
    public const string TileCount =
        "Target axial tile count for Tile-large-builds mode. Planner may "
        + "collapse to fewer tiles when per-tile core length would fall "
        + "below the minimum safe length. 4 is a good default for a "
        + "~180 mm chamber; bump higher for longer chambers where peak "
        + "memory needs to drop further.";
    // Sprint 28 (2026-04-23): export-side monolithic toggle.
    public const string ExportMonolithic =
        "When ON, the STL export fuses chamber + turbopump + feed manifold "
        + "+ preburner into a single voxel body via "
        + "MonolithicEngineBuilder.BuildFromDesign in the headless subprocess. "
        + "Honors the full saved design (channel schedule, injector pattern, "
        + "flange specs all carry through). OFF (default) produces a single-"
        + "body STL — bell chamber for regen designs, aerospike plug + "
        + "chamber for aerospike topology.";
    public const string IsolateLargeBuildsAtFailProjection =
        "EXPERIMENTAL / scaffold toggle: round-trips through session.json "
        + "and surfaces a status-bar hint when the pre-flight projection is "
        + "Fail. Full dispatch through a Job-Object-bound subprocess is "
        + "scheduled for a follow-on sprint — today the Generate path still "
        + "builds in-process. Leave OFF unless you are hand-editing the "
        + "session file or testing the hint. Auto-coarsen + Tile-large-"
        + "builds cover the common large-thrust cases without losing the "
        + "live viewer.";
}
