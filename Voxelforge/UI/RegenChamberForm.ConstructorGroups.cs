// RegenChamberForm.ConstructorGroups.cs — Sprint 15 / Track G + H-lite
// (2026-04-22):
// Partial-class sibling carrying per-section constructor-group helpers
// extracted from the inline control-construction block in
// RegenChamberForm.cs (lines 319-1450 of the constructor body).
//
// Why a partial class
// ───────────────────
// The form constructor is the remaining monolithic block in the main
// file (~1130 LOC). Rather than refactor the whole thing in a single
// PR (Sprint 17 / Track H is the planned full extraction), Sprint 15
// extracts only the two opt-in cooling groups it actively touches —
// Sprint 9's preburner-cooling group and the new aerospike-cooling
// group — into named `BuildXxxGroup()` helpers. This keeps the
// Sprint-15 diff small and gives Sprint 17 / Track H two
// pattern-match-against examples for the remaining ~15-20 helpers.
//
// What lives here
// ───────────────
//   BuildPreburnerCoolingGroup()  — Sprint 9 Track B's opt-in section,
//     extracted unchanged. Returns the `Panel` to add to the left
//     flow column. Assigns the form's chk/nud/lbl preburner field
//     members as a side-effect (they're consumed by ParameterIO).
//   BuildAerospikeCoolingGroup()  — NEW in Sprint 15. Same shape as
//     the preburner helper; controls bind to RegenChamberDesign's
//     IncludeAerospikeRegenCooling + AerospikePlugChannel{Count,
//     Width_mm,Depth_mm} + AerospikePlugWallThickness_mm fields,
//     wired through AerospikeOptimization.ToSpec.
//
// What does NOT live here
// ───────────────────────
//   The other ~15 visual groups in the constructor (operating
//   conditions, chamber geometry, channel topology, injector pattern,
//   STL/3MF actions, batch run, …) stay inline until Sprint 17 /
//   Track H pulls them out. Adding them piecemeal to this file is
//   fine when a future sprint touches the corresponding section.

using System.Windows.Forms;
using Voxelforge.Geometry;       // PortStandards
using Voxelforge.Geometry.LpbfAnalysis; // LpbfMaterial + Profiles (Sprint 27)
using Voxelforge.HeatTransfer;   // WallMaterials
using Voxelforge.Optimization;   // FieldKeys (UI overhaul Sprint 1)

namespace Voxelforge.UI;

public sealed partial class RegenChamberForm
{
    // ═══════════════════════════════════════════════════════════════
    //  Sprint 17 / Track H — constructor decomposition
    //
    //  The constructor of RegenChamberForm.cs used to run ~1130 LOC of
    //  inline control construction. Sprint 15 / Track H-lite pulled the
    //  two opt-in cooling groups (preburner + aerospike) out; Sprint 17
    //  extracts the five largest input-side groups that each just create
    //  their own fields + call Group(…). Result: each extracted section
    //  becomes a single `left.Controls.Add(BuildXxxGroup());` line in
    //  the constructor, with the control-construction detail co-located
    //  in the helper below.
    //
    //  Why not extract ALL ~15 visual groups? The remaining ones are
    //  either (a) small enough that extraction isn't a clear win
    //  (single-row groups like Proof Test), (b) tangled with event-
    //  handler wiring that's easier to read in-line (the action-buttons
    //  block, lines 755-858 of the pre-Sprint-17 constructor), or
    //  (c) cross-referenced by the resource-budget UI's adaptive
    //  refresh logic (~130 LOC with cross-control dependencies).
    //  Pick up additional extractions opportunistically when editing
    //  the corresponding section; a future sprint can do another batch.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Sprint 17 / Track H: Operating Point group — thrust / Pc / MR /
    /// coolant / wall material / propellant pair / Bartz scaling.
    /// Top of the input column; the most frequently touched group.
    /// </summary>
    private Panel BuildConditionsGroup()
    {
        nudThrustN = Num(500, 10, 100000, 10, 0);
        nudPcPsi = Num(1000, 100, 5000, 25, 0);
        nudMR = Num(3.3, 1.5, 8.5, 0.05, 2);
        nudCoolTK = Num(150, 20, 400, 5, 0);
        nudCoolPMPa = Num(12.0, 5, 25, 0.5, 1);
        cboMaterial = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var m in WallMaterials.All) cboMaterial.Items.Add(m.Name);
        cboMaterial.SelectedIndex = 1; // CuCrZr

        cboPropellantPair = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in Combustion.PropellantPairs.All)
            cboPropellantPair.Items.Add(p.Name + (p.Implemented ? "" : "  (unavailable)"));
        cboPropellantPair.SelectedIndex = 0;
        lblPairNote = new Label
        {
            Text = Combustion.PropellantPairs.All[0].Note,
            AutoSize = false, Width = 620, Height = 34,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic)
        };

        // Bartz scaling factor input. 1.0 = literature Bartz; the
        // measured-data overlay may recommend a value that the user
        // can dial in here or apply via the "Apply calibrated Bartz
        // factor" button on the Hardware validation overlay group.
        nudBartzFactor = Num(1.0, 0.3, 3.0, 0.01, 2);

        return Group("Operating Point",
            Row("Propellant pair",       cboPropellantPair, FieldKeys.PropellantPair),
            lblPairNote,
            Row("Thrust (N)",            nudThrustN,        FieldKeys.Thrust_N),
            Row("Chamber P (psia)",      nudPcPsi,          FieldKeys.ChamberPressure_MPa),
            Row("Mixture ratio O/F",     nudMR,             FieldKeys.MixtureRatio),
            Row("Coolant inlet T (K)",   nudCoolTK),
            Row("Coolant inlet P (MPa)", nudCoolPMPa),
            Row("Wall material",         cboMaterial,       FieldKeys.WallMaterial),
            Row("Bartz factor (cal.)",   nudBartzFactor));
    }

    /// <summary>
    /// Sprint 17 / Track H: Nozzle Geometry group — bell/contour shape
    /// parameters (contraction ratio, expansion ratio, L*, Rao angles).
    /// </summary>
    private Panel BuildNozzleGeometryGroup()
    {
        nudContraction = Num(6, 3, 10, 0.25, 2);
        nudExpansion = Num(8, 3, 25, 0.5, 1);
        nudLStar = Num(1.1, 0.7, 1.6, 0.05, 2);
        nudThetaN = Num(30, 20, 38, 1, 0);
        nudThetaE = Num(10, 6, 16, 0.5, 1);
        nudBellFrac = Num(0.8, 0.6, 0.9, 0.02, 2);

        return Group("Nozzle Geometry",
            Row("Contraction ratio \u03b5_c",   nudContraction, FieldKeys.ContractionRatio),
            Row("Expansion ratio \u03b5_e",     nudExpansion,   FieldKeys.ExpansionRatio),
            Row("L* (m)",                       nudLStar,       FieldKeys.CharacteristicLength_m),
            Row("Bell entry \u03b8_n (\u00b0)", nudThetaN,      FieldKeys.BellEntranceAngle_deg),
            Row("Bell exit \u03b8_e (\u00b0)",  nudThetaE,      FieldKeys.BellExitAngle_deg),
            Row("Bell length fraction",         nudBellFrac,    FieldKeys.BellLengthFraction));
    }

    /// <summary>
    /// Sprint 17 / Track H: Cooling Channels group — channel count,
    /// geometry per station, rib + wall + jacket thicknesses,
    /// smoothing, manifold + port geometry.
    /// </summary>
    private Panel BuildCoolingChannelsGroup()
    {
        nudChannelCount = Num(80, 40, 120, 4, 0);
        nudHChamber = Num(2.5, 1.0, 5.0, 0.1, 2);
        nudHThroat = Num(1.5, 0.8, 3.0, 0.1, 2);
        nudHExit = Num(2.0, 1.0, 5.0, 0.1, 2);
        nudRib = Num(0.8, 0.5, 2.0, 0.05, 2);
        nudWall = Num(0.8, 0.5, 2.0, 0.05, 2);
        nudJacket = Num(2.0, 1.0, 4.0, 0.1, 1);
        nudSmoothing = Num(0.0, 0.0, 0.5, 0.05, 2);
        nudManifoldL = Num(15, 8, 40, 1, 0);
        nudPortD = Num(10, 4, 20, 0.5, 1);
        nudChannelFillet = Num(0.8, 0.0, 3.0, 0.1, 2);
        chkManifolds = new CheckBox { Text = "Include manifolds", Checked = true, AutoSize = true };
        chkPorts = new CheckBox { Text = "Include radial coolant ports", Checked = true, AutoSize = true };
        cboCoolantPortStd = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var name in PortStandards.Names) cboCoolantPortStd.Items.Add(name);
        cboCoolantPortStd.SelectedIndex = 0;

        return Group("Cooling Channels",
            Row("# channels",                nudChannelCount,   FieldKeys.ChannelCount),
            Row("h @ chamber (mm)",          nudHChamber,       FieldKeys.ChannelHeightChamber_mm),
            Row("h @ throat (mm)",           nudHThroat,        FieldKeys.ChannelHeightThroat_mm),
            Row("h @ exit (mm)",             nudHExit,          FieldKeys.ChannelHeightExit_mm),
            Row("rib thickness (mm)",        nudRib,            FieldKeys.RibThickness_mm),
            Row("wall thickness (mm)",       nudWall,           FieldKeys.GasSideWallThickness_mm),
            Row("jacket thickness (mm)",     nudJacket,         FieldKeys.OuterJacketThickness_mm),
            Row("smoothing (mm)",            nudSmoothing),
            Row("manifold length (mm)",      nudManifoldL),
            Row("coolant port D (mm)",       nudPortD),
            Row("coolant port thread",       cboCoolantPortStd),
            Row("channel manifold fillet (mm)", nudChannelFillet),
            chkManifolds, chkPorts);
    }

    /// <summary>
    /// Sprint 17 / Track H: Flanges & Propellant Ports group —
    /// injector + mount flange toggles and geometry, propellant port
    /// diameter / thread standard, mount bolt-pattern preset.
    /// </summary>
    private Panel BuildFlangesGroup()
    {
        chkInjectorFlange = new CheckBox { Text = "Include injector flange", Checked = true, AutoSize = true };
        nudFlangeThk = Num(8, 3, 25, 0.5, 1);
        nudFlangeORFactor = Num(1.25, 1.05, 1.8, 0.05, 2);
        nudPropPortD = Num(6, 2, 20, 0.5, 1);
        cboPropPortStd = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var name in PortStandards.Names) cboPropPortStd.Items.Add(name);
        cboPropPortStd.SelectedIndex = 0;
        chkMountFlange = new CheckBox { Text = "Include nozzle mount flange", Checked = false, AutoSize = true };
        nudMountThk = Num(6, 3, 25, 0.5, 1);
        cboMountFlangeStd = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in Geometry.MountingFlangePresets.All.Values)
            cboMountFlangeStd.Items.Add(s.DisplayName);
        cboMountFlangeStd.SelectedIndex = 0;

        return Group("Flanges & Propellant Ports",
            chkInjectorFlange,
            Row("injector flange thk (mm)", nudFlangeThk,       FieldKeys.FlangeRadialProjection_mm),
            Row("flange OR / R_c",          nudFlangeORFactor),
            Row("propellant port D (mm)",   nudPropPortD),
            Row("propellant port thread",   cboPropPortStd),
            chkMountFlange,
            Row("mount flange thk (mm)",    nudMountThk),
            Row("mount bolt pattern",       cboMountFlangeStd,  FieldKeys.MountingFlangeStandard));
    }

    /// <summary>
    /// Injector internals — closes the silent-skip gaps tracked by
    /// issues #306 (igniter cavity), #307 (dome + anti-vortex baffle),
    /// and #308 (closed-expander coolant crossover). Each field was
    /// already consumed by ChamberVoxelBuilder but had no UI control
    /// so the only path to a non-default value was a JSON load, an
    /// AutoSeeder seed (igniter only), or programmatic test code —
    /// manual previews silently rendered the default-off geometry.
    /// Group is collapsed by default since these are advanced choices
    /// the typical user doesn't need to touch.
    /// </summary>
    private Panel BuildInjectorInternalsGroup()
    {
        cboIgniterType = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        cboIgniterType.Items.AddRange(new object[]
        {
            "None (hypergolic / external torch)",
            "Spark torch",
            "Augmented spark",
            "Pyrotechnic cartridge",
        });
        cboIgniterType.SelectedIndex = (int)Geometry.IgniterType.None;

        nudIgniterRadialFrac      = Num(0.0,  0.0, 0.8,  0.05, 2);

        nudFuelDomeDepth_mm       = Num(0.0,  0.0, 30.0, 0.5,  1);
        nudOxDomeDepth_mm         = Num(0.0,  0.0, 30.0, 0.5,  1);
        nudDomeInletDia_mm        = Num(8.0,  2.0, 30.0, 0.5,  1);
        chkAntiVortexBaffle       = new CheckBox { Text = "Anti-vortex baffle in dome", Checked = false, AutoSize = true };

        chkCoolantCrossover       = new CheckBox { Text = "Coolant crossover (closed expander cycle)", Checked = false, AutoSize = true };
        nudCoolantCrossoverDia_mm = Num(3.0,  0.5, 10.0, 0.5,  1);

        return Group("Injector internals (igniter / dome / crossover)", startCollapsed: true,
            Row("igniter type",              cboIgniterType),
            Row("igniter radial frac",       nudIgniterRadialFrac),
            Row("fuel dome depth (mm)",      nudFuelDomeDepth_mm),
            Row("ox dome depth (mm)",        nudOxDomeDepth_mm),
            Row("dome inlet dia (mm)",       nudDomeInletDia_mm),
            chkAntiVortexBaffle,
            chkCoolantCrossover,
            Row("crossover dia (mm)",        nudCoolantCrossoverDia_mm),
            MakeHelp("Igniter cavity drills a torch / pyrotechnic cavity through the injector flange "
                  + "(IgniterType=None = no cavity, hypergolic pairs need none). Inlet domes "
                  + "(depth > 0) hollow a plenum behind the injector face — only fires when the "
                  + "flange is thicker than depth+3 mm; the anti-vortex baffle adds a thin radial disc "
                  + "in the dome. The coolant crossover (closed expander cycle) drills a short axial "
                  + "passage from the upstream manifold into the chamber-face zone so regen-heated "
                  + "fuel feeds the injector without external plumbing — only meaningful on expander cycles."));
    }

    /// <summary>
    /// Sprint 17 / Track H: Film Cooling group — Stechman-η fuel-film
    /// opt-in, slot geometry, inlet T, burnout length, decay, throat
    /// mixing loss.
    /// </summary>
    private Panel BuildFilmCoolingGroup()
    {
        chkFilmEnable = new CheckBox { Text = "Enable fuel-film cooling", Checked = false, AutoSize = true };
        nudFilmFrac = Num(0.05, 0.00, 0.30, 0.01, 2);
        nudFilmSlotH = Num(0.6, 0.2, 3.0, 0.1, 2);
        nudFilmInjX = Num(0.0, 0.0, 200.0, 1.0, 1);
        nudFilmInletT = Num(150.0, 100.0, 300.0, 5.0, 0);
        nudFilmBurnL = Num(200.0, 20.0, 500.0, 10.0, 0);
        nudFilmDecay = Num(0.15, 0.02, 0.50, 0.02, 2);
        nudFilmThroatMix = Num(0.25, 0.00, 0.60, 0.05, 2);

        return Group("Film Cooling (Stechman \u03b7)",
            chkFilmEnable,
            Row("fuel fraction to film",   nudFilmFrac,         FieldKeys.FilmFuelFraction),
            Row("film slot h (mm)",        nudFilmSlotH,        FieldKeys.FilmSlotHeightOverride_mm),
            Row("injection x (mm)",        nudFilmInjX,         FieldKeys.FilmInjectionAxialFraction),
            Row("film inlet T (K)",        nudFilmInletT),
            Row("burnout length (mm)",     nudFilmBurnL),
            Row("decay coef \u03b2",      nudFilmDecay),
            Row("throat mixing loss",      nudFilmThroatMix));
    }

    /// <summary>
    /// Sprint 9 Track B preburner regen-cooling opt-in group. Extracted
    /// from the inline constructor block in Sprint 15 / Track H-lite —
    /// behaviour identical to the pre-Sprint-15 inline path. Assigns
    /// the form's <see cref="chkPreburnerCooling"/> +
    /// <see cref="nudPreburnerChCount"/> /
    /// <see cref="nudPreburnerChWidth_mm"/> /
    /// <see cref="nudPreburnerChDepth_mm"/> /
    /// <see cref="nudPreburnerWallT_mm"/> +
    /// <see cref="lblPreburnerWallT"/> /
    /// <see cref="lblPreburnerCoolantOut"/> /
    /// <see cref="lblPreburnerHeatLoad"/> instance fields, which
    /// <see cref="ParameterIO"/> + <see cref="ResultsDisplay"/> consume.
    /// </summary>
    private Panel BuildPreburnerCoolingGroup()
    {
        chkPreburnerCooling     = new CheckBox { Text = "Enable preburner regen cooling", Checked = false, AutoSize = true };
        nudPreburnerChCount     = Num(24,   4,  60,   2,    0);
        nudPreburnerChWidth_mm  = Num(2.5,  0.5, 6.0,  0.1,  1);
        nudPreburnerChDepth_mm  = Num(2.0,  0.5, 6.0,  0.1,  1);
        nudPreburnerWallT_mm    = Num(0.8,  0.3, 3.0,  0.1,  1);
        lblPreburnerWallT       = Out("Preburner peak wall T: —");
        lblPreburnerCoolantOut  = Out("Preburner coolant out: —");
        lblPreburnerHeatLoad    = Out("Preburner heat load: —");

        return Group("Preburner regen cooling (opt-in)", startCollapsed: true,
            chkPreburnerCooling,
            Row("Channel count",            nudPreburnerChCount,     FieldKeys.PreburnerCoolingChannelCount),
            Row("Channel width (mm)",       nudPreburnerChWidth_mm),
            Row("Channel depth (mm)",       nudPreburnerChDepth_mm,  FieldKeys.PreburnerCoolingChannelDepth_mm),
            Row("Hot-wall thickness (mm)",  nudPreburnerWallT_mm),
            lblPreburnerWallT, lblPreburnerCoolantOut, lblPreburnerHeatLoad,
            MakeHelp("Only active when cycle is StagedCombustion / GasGenerator / FullFlow. " +
                     "Runs a lumped-parameter Bartz ↔ Dittus-Boelter balance on the preburner " +
                     "chamber wall. PREBURNER_WALL_TEMP gate fires when the predicted peak wall T " +
                     "exceeds the selected wall material's service limit. " +
                     "Numbers populate after Generate (blank when opt-in is off)."));
    }

    /// <summary>
    /// Sprint 15 / Track G aerospike plug-channel regen-cooling opt-in
    /// group. Closes the feature loop Sprint 11 Track F opened: the SA
    /// scoring path already reads from <c>gen.Aerospike.Thermal</c> when
    /// populated; before this UI section, the only way to populate it
    /// was via the Benchmarks <c>--channels</c> CLI flag. Now any
    /// aerospike-topology design can opt in via the UI and the
    /// <c>AEROSPIKE_PLUG_WALL_TEMP</c> gate has data to fire on.
    /// Defaults match the AerospikeSpec defaults (24 channels, 2.5 mm
    /// width, 2.0 mm depth, 0.8 mm wall).
    /// </summary>
    private Panel BuildAerospikeCoolingGroup()
    {
        // Sprint fix (2026-04-25): expose PlugLengthRatio +
        // AerospikeContractionRatio in the UI. Both were SA-tunable
        // but had no UI surface; the user couldn't change them in
        // manual mode, so aerospike previews stuck at PlugLengthRatio
        // = 0.30 (a 70 % truncation that hides the iconic plug shape).
        nudPlugLengthRatio              = Num(0.30, 0.15, 1.00, 0.05, 2);
        nudAerospikeContractionRatio    = Num(6.0,  3.0,  10.0, 0.5,  1);
        chkAerospikeCooling     = new CheckBox { Text = "Enable aerospike plug-channel regen cooling", Checked = false, AutoSize = true };
        nudAerospikeChCount     = Num(24,   4,  60,   2,    0);
        nudAerospikeChWidth_mm  = Num(2.5,  0.5, 6.0,  0.1,  1);
        nudAerospikeChDepth_mm  = Num(2.0,  0.5, 6.0,  0.1,  1);
        nudAerospikeWallT_mm    = Num(0.8,  0.3, 3.0,  0.1,  1);

        return Group("Aerospike geometry + cooling (opt-in)", startCollapsed: true,
            MakeHelp("PlugLengthRatio = 1.0 shows the full Angelino plug. " +
                     "Default 0.30 truncates 70 % for compactness; iconic " +
                     "long-plug renders need ≥ 0.70."),
            Row("Plug length ratio",            nudPlugLengthRatio,             FieldKeys.PlugLengthRatio),
            Row("Aerospike contraction ratio",  nudAerospikeContractionRatio,   FieldKeys.AerospikeContractionRatio),
            chkAerospikeCooling,
            Row("Channel count",                nudAerospikeChCount,            FieldKeys.AerospikePlugCooling),
            Row("Channel width (mm)",           nudAerospikeChWidth_mm),
            Row("Channel depth (mm)",           nudAerospikeChDepth_mm),
            Row("Plug wall thickness (mm)",     nudAerospikeWallT_mm),
            MakeHelp("Cooling block: only active when ChannelTopology is Aerospike. Runs the " +
                     "AerospikePlugCooling solver (per-station Bartz ↔ Dittus-Boelter " +
                     "balance on the plug surface) and populates AerospikeBuildResult.Thermal " +
                     "so the AEROSPIKE_PLUG_WALL_TEMP and AEROSPIKE_COOLANT_CAVITATION_RISK " +
                     "gates fire. Off by default — the geometry-only aerospike pipeline still " +
                     "works without any cooling solve."));
    }

    /// <summary>
    /// Sprint 27 (2026-04-23) LPBF printability-analysis opt-in group.
    /// Binds to RegenChamberDesign.IncludeLpbfPrintabilityAnalysis +
    /// LpbfMaterial + LpbfPrintOrientationAxis_deg. When the checkbox is
    /// on, the three feasibility gates OVERHANG_ANGLE_EXCEEDED,
    /// TRAPPED_POWDER_REGION, and DRAIN_PATH_MISSING become live.
    /// </summary>
    private Panel BuildLpbfPrintabilityGroup()
    {
        chkLpbfPrintability = new CheckBox
        {
            Text     = "Enable LPBF printability analysis",
            Checked  = false,
            AutoSize = true,
        };
        cboLpbfMaterial = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in LpbfMaterialProfiles.All)
            cboLpbfMaterial.Items.Add(p.DisplayName);
        cboLpbfMaterial.SelectedIndex = (int)LpbfMaterial.CuCrZr;

        // -1 sentinel sits outside the visible NumericUpDown band; users
        // drag it to a non-negative value to force the build axis, or
        // clear it (set to -1) to hand the choice back to the advisor.
        nudLpbfOrientationAxis = Num(-1, -1, 360, 5, 0);

        lblLpbfPrintability = Out("Printability: —");

        return Group("LPBF printability (Sprint 27, opt-in)", startCollapsed: true,
            chkLpbfPrintability,
            Row("Alloy profile", cboLpbfMaterial),
            Row("Build-axis ° (-1 = auto)", nudLpbfOrientationAxis),
            lblLpbfPrintability,
            MakeHelp("Runs the Geometry.LpbfAnalysis overhang / drain-path / orientation "
                   + "advisor on the chamber's axisymmetric surface. Trapped-powder flood-fill "
                   + "is skipped on the fast path (voxel snapshot required — run it at STL "
                   + "export time). Alloy profile drives the overhang-angle floor: IN718 35°, "
                   + "GRCop / IN625 40°, CuCrZr / 316L 45°. Build-axis override -1 means the "
                   + "advisor picks the best axis automatically."));
    }

    /// <summary>
    /// Sprint 26 (2026-04-23): linear-aerospike geometry group.
    /// Plug transverse width + design-intent aspect ratio. Sits next
    /// to the axisymmetric plug-cooling group so a user switching the
    /// topology dropdown from Aerospike → LinearAerospike finds the
    /// linear-specific knobs immediately. Only consumed when
    /// ChannelTopology = LinearAerospike; carried silently on every
    /// other topology per the §7 categorical-silent-revert convention.
    /// </summary>
    private Panel BuildLinearAerospikeGroup()
    {
        nudLinearPlugWidth_mm  = Num(60.0, 10.0, 400.0, 1.0, 1);
        nudLinearAspectRatio   = Num(1.0,  0.2,   5.0,  0.1, 2);

        return Group("Linear aerospike (extruded, opt-in)", startCollapsed: true,
            Row("Plug transverse width (mm)", nudLinearPlugWidth_mm, FieldKeys.LinearAerospikePlugWidth_mm),
            Row("Design aspect ratio target",  nudLinearAspectRatio,  FieldKeys.LinearAerospikePlugDepth_mm),
            MakeHelp("Only active when ChannelTopology is LinearAerospike. The plug is " +
                     "extruded (X-33 / XRS-2200 lineage) rather than revolved; the Angelino " +
                     "2D expansion curve is shared with the axisymmetric path. Plug transverse " +
                     "width + thrust/Pc derive the plug half-height at the throat. The " +
                     "LINEAR_AEROSPIKE_ASPECT_RATIO feasibility gate fires outside [0.30, 5.00] " +
                     "— below 0.30 the side-wall recirculation bubble dominates; above 5.00 " +
                     "the plug becomes a long-span cantilever unmanageable at LPBF scale. The " +
                     "Sprint 15 plug-channel cooling opt-in is reused on the linear path; " +
                     "voxelisation of the rectangular plug is a Sprint-27+ follow-on (physics / " +
                     "SA scoring / feasibility work end-to-end today)."));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Sprint 22 / Track H completion — extract the 8 remaining inline
    //  Group(...) blocks from the constructor. Pattern matches Sprint 15
    //  and Sprint 17 helpers: assign field members as side-effect, return
    //  Panel. Each helper replaces a ~10-30 LOC inline block with a
    //  single `left.Controls.Add(BuildXxxGroup());` call.
    //
    //  Remaining-inline after this sprint: Action Buttons + Mesh
    //  resolution + Export & save — they have cross-control dependencies
    //  (status-bar refresh, resource-budget adaptive UI) that make
    //  mechanical extraction risky. Documented in ROADMAP.md.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Sprint 22: Injector-face STL import group. Collapsed by default
    /// (low-frequency section). Controls bind to the injector-face
    /// import path + offset + scale + auto-center toggle.
    /// </summary>
    private Panel BuildInjectorStlGroup()
    {
        chkInjectorSTL = new CheckBox { Text = "Import injector face STL", Checked = false, AutoSize = true };
        txtInjectorSTLPath = new TextBox { Width = 380, ReadOnly = false };
        btnBrowseInjectorSTL = new Button { Text = "Browse\u2026", Width = 90, Height = 26 };
        btnBrowseInjectorSTL.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "STL|*.stl" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                txtInjectorSTLPath.Text = dlg.FileName;
                MaybePush();
            }
        };
        var stlPathRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Width = 580 };
        stlPathRow.Controls.AddRange(new Control[] { txtInjectorSTLPath, btnBrowseInjectorSTL });

        nudInjectorSTLOffsetX = Num(-8.0, -100.0, 100.0, 0.5, 1);
        nudInjectorSTLScale = Num(1.0, 0.01, 100.0, 0.1, 2);
        chkInjectorSTLAutoCenter = new CheckBox { Text = "Auto-center on chamber axis (YZ)", Checked = true, AutoSize = true };

        return Group("Injector Face STL", startCollapsed: true,
            chkInjectorSTL,
            stlPathRow,
            Row("place STL min-X at (mm)",  nudInjectorSTLOffsetX, FieldKeys.InjectorStlPath),
            Row("uniform scale",            nudInjectorSTLScale),
            chkInjectorSTLAutoCenter);
    }

    /// <summary>
    /// Sprint 22: Proof-test (cold hydro) group. Proof factor + run-button.
    /// Collapsed by default.
    /// </summary>
    private Panel BuildProofTestGroup()
    {
        nudProofFactor = Num(1.5, 1.1, 3.0, 0.05, 2);
        btnRunProofTest = new Button { Text = "Run Proof Test", Width = 170, Height = 30 };
        btnRunProofTest.Click += (_, _) => RunProofTest();
        return Group("Proof Test (cold hydro)", startCollapsed: true,
            Row("proof factor \u00d7 MEOP", nudProofFactor),
            btnRunProofTest);
    }

    /// <summary>
    /// Sprint 22: LPBF Monte-Carlo tolerance-sweep group. Sample count +
    /// per-surface ± tolerance + run-button. Collapsed by default.
    /// </summary>
    private Panel BuildToleranceGroup()
    {
        nudTolSamples = Num(400, 50, 2000, 50, 0);
        nudTolWall = Num(0.10, 0.02, 0.30, 0.01, 2);
        nudTolChannel = Num(0.10, 0.02, 0.30, 0.01, 2);
        btnRunTolerance = new Button { Text = "Run Tolerance Sweep", Width = 190, Height = 30 };
        btnRunTolerance.Click += (_, _) => RunToleranceSweep();
        return Group("LPBF Tolerance (Monte-Carlo)", startCollapsed: true,
            Row("sample count", nudTolSamples),
            Row("wall + jacket \u00b1 tol (mm)", nudTolWall),
            Row("channel + rib \u00b1 tol (mm)", nudTolChannel),
            btnRunTolerance);
    }

    /// <summary>
    /// Channel-topology selector. Lets the user pick Axial / Helical /
    /// None / TPMS family / Aerospike without editing JSON. Collapsed
    /// by default.
    /// </summary>
    private Panel BuildChannelTopologyGroup()
    {
        cboTopology = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (Optimization.ChannelTopology t in System.Enum.GetValues<Optimization.ChannelTopology>())
            cboTopology.Items.Add(t.ToString());
        cboTopology.SelectedIndex = 0;       // Axial default

        return Group("Channel topology", startCollapsed: true,
            Row("topology", cboTopology, FieldKeys.ChannelTopology),
            MakeHelp("Axial = straight channels (default). Helical = spiral around chamber. " +
                     "None = no regen channels (ablative-only shell — runs the ablative recession " +
                     "analysis so you can model an ablative chamber without editing JSON). " +
                     "TpmsGyroid/TpmsSchwarzP/TpmsSchwarzD = triply-periodic-minimal-surface cooling " +
                     "(TpmsCellEdge_mm + TpmsSolidFraction drive the unit-cell geometry). " +
                     "Aerospike = annular-throat plug nozzle (the regen-bell display readouts " +
                     "show fallback values — the aerospike plug body itself lives on result.Aerospike and " +
                     "is exposed via the --aerospike CLI path in Voxelforge.Benchmarks)."));
    }

    /// <summary>
    /// Feed-system stackup opt-in group. Tank ullage (0 = off),
    /// filter preset + contamination fraction, umbilical standard.
    /// Collapsed by default.
    /// </summary>
    private Panel BuildFeedSystemGroup()
    {
        nudTankUllage_MPa = Num(0.0, 0.0, 50.0, 0.5, 2);          // 0 = stackup off
        cboFilterStd = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (FeedSystem.FilterStandard fs in System.Enum.GetValues<FeedSystem.FilterStandard>())
            cboFilterStd.Items.Add(FeedSystem.FilterPresets.SpecFor(fs).DisplayName);
        cboFilterStd.SelectedIndex = (int)FeedSystem.FilterStandard.Custom;
        nudFilterContamination = Num(0.0, 0.0, 1.0, 0.05, 2);

        cboUmbilicalStd = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (Geometry.UmbilicalStandard us in System.Enum.GetValues<Geometry.UmbilicalStandard>())
            cboUmbilicalStd.Items.Add(Geometry.UmbilicalStandards.SpecFor(us).DisplayName);
        cboUmbilicalStd.SelectedIndex = 0;     // None

        return Group("Feed system (stackup, opt-in)", startCollapsed: true,
            Row("Tank ullage (MPa) — 0 = off", nudTankUllage_MPa),
            Row("Filter preset", cboFilterStd),
            Row("Filter contamination (0-1)", nudFilterContamination),
            Row("Umbilical / QD", cboUmbilicalStd),
            MakeHelp("Setting tank ullage > 0 opts the feed-system stackup in. " +
                     "Filter contamination linearly interpolates between clean and end-of-life ΔP " +
                     "per the preset's DirtyMultiplier. Umbilical also draws a seal-groove + 4-bolt " +
                     "pattern around each propellant port."));
    }

    /// <summary>
    /// Pre-fire chilldown-transient opt-in group. Integrator-enable +
    /// jacket T / HTC / ΔT-done / max-time knobs + time / propellant
    /// / shock / regime readouts. Collapsed by default.
    /// </summary>
    private Panel BuildChilldownGroup()
    {
        chkChilldownEnable = new CheckBox { Text = "Enable pre-fire chilldown integrator", Checked = false, AutoSize = true };
        nudChilldownInitT = Num(298, 200, 350, 5, 0);
        nudChilldownHTC   = Num(5000, 500, 30000, 500, 0);
        nudChilldownDoneDT= Num(50, 5, 200, 5, 0);
        nudChilldownMaxT  = Num(60, 5, 600, 5, 0);
        lblChilldownTime    = Out("Chilldown time: —");
        lblChilldownProp    = Out("Propellant boiled off: —");
        lblChilldownShock   = Out("Thermal-shock σ peak: —");
        lblChilldownRegime  = Out("Regime: —");

        return Group("Chilldown transient (opt-in)", startCollapsed: true,
            chkChilldownEnable,
            Row("Initial jacket T (K)", nudChilldownInitT),
            Row("Two-phase HTC (W/m²·K)", nudChilldownHTC),
            Row("Done ΔT (K)", nudChilldownDoneDT),
            Row("Max acceptable time (s)", nudChilldownMaxT),
            lblChilldownTime, lblChilldownProp, lblChilldownShock, lblChilldownRegime,
            MakeHelp("Lumped-jacket first-order integrator. Skipped on RP-1 (non-cryogenic). " +
                     "Time = -τ·ln(ΔT_done/ΔT₀); τ = m·cp/(h·A). Numbers populate after Generate."));
    }

    /// <summary>
    /// Start-transient simulator opt-in group. Valve-ramp +
    /// igniter-delay + sim-duration + hard-start-threshold knobs +
    /// time-to-90% / peak-overshoot / unburned readouts + Pc(t) chart.
    /// Collapsed by default.
    /// </summary>
    private Panel BuildStartTransientGroup()
    {
        chkStartTransient = new CheckBox { Text = "Enable start-transient simulator", Checked = false, AutoSize = true };
        nudValveOpen_ms     = Num(100, 10, 1000, 10, 0);
        nudIgniterDelay_ms  = Num(50,   0, 2000, 10, 0);
        nudStartSimDur_ms   = Num(1000, 100, 5000, 50, 0);
        nudStartSimDt_ms    = Num(1.0, 0.1, 10.0, 0.1, 1);
        nudHardStartFactor  = Num(0.50, 0.10, 2.00, 0.05, 2);
        // Per-side valve ramps; 0 = use shared `Valve open ramp` above.
        nudOxValveOpen_ms   = Num(0, 0, 1000, 10, 0);
        nudFuelValveOpen_ms = Num(0, 0, 1000, 10, 0);
        lblStartTimeTo90       = Out("Time to 90 % Pc: —");
        lblStartPeakOvershoot  = Out("Peak Pc overshoot: —");
        lblStartUnburned       = Out("Unburned at ignition: —");
        pcChartPanel = new StartTransientChartPanel
        { Width = 700, Height = 200, BorderStyle = BorderStyle.FixedSingle };

        return Group("Start transient (opt-in)", startCollapsed: true,
            chkStartTransient,
            Row("Valve open ramp (ms, shared)", nudValveOpen_ms),
            Row("Ox valve override (ms, 0 = shared)",   nudOxValveOpen_ms),
            Row("Fuel valve override (ms, 0 = shared)", nudFuelValveOpen_ms),
            Row("Igniter delay (ms)", nudIgniterDelay_ms),
            Row("Sim duration (ms)", nudStartSimDur_ms),
            Row("Sim time-step (ms)", nudStartSimDt_ms),
            Row("Hard-start overshoot threshold", nudHardStartFactor),
            lblStartTimeTo90, lblStartPeakOvershoot, lblStartUnburned,
            pcChartPanel,
            MakeHelp("Lumped 0-D: linear valve ramp → dome fill → first-order Pc lag with τ_c = V_c/(c*·A_t). " +
                     "Hard-start fires when peak Pc overshoots target by ≥ threshold. " +
                     "Per-side ramp overrides enable staged starts (set fuel < ox to lead the fuel " +
                     "dome — classic hard-start mitigation). 0 = use the shared ramp. " +
                     "Chart: blue = Pc/Pc_target, gray = valve position, orange = dome fill."));
    }

    /// <summary>
    /// Engine-cycle / turbopump-sizing opt-in group. Cycle selector +
    /// pump inlet/discharge P (0 = auto) + pump efficiency + fuel /
    /// ox / total readouts. Collapsed by default.
    /// </summary>
    private Panel BuildEngineCycleGroup()
    {
        cboEngineCycle = new ComboBox { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (FeedSystem.EngineCycle ec in System.Enum.GetValues<FeedSystem.EngineCycle>())
            cboEngineCycle.Items.Add(ec.ToString());
        cboEngineCycle.SelectedIndex = 0;     // PressureFed
        nudPumpInletP_MPa     = Num(0.0,  0.0, 20.0, 0.1, 2);     // 0 = auto-size from ullage / 0.3 MPa default
        nudPumpDischargeP_MPa = Num(0.0,  0.0, 50.0, 0.5, 2);     // 0 = auto-size to Pc × 1.5
        nudPumpEff            = Num(0.65, 0.30, 0.90, 0.01, 2);
        lblPumpFuel           = Out("Fuel pump: —");
        lblPumpOx             = Out("Ox pump: —");
        lblPumpTotal          = Out("Total shaft / dry mass: —");

        return Group("Engine cycle / turbopump (opt-in)", startCollapsed: true,
            Row("Cycle",                             cboEngineCycle,        FieldKeys.EngineCycle),
            Row("Pump inlet P (MPa) — 0 = auto",     nudPumpInletP_MPa,     FieldKeys.PumpInletPressure_MPa),
            Row("Pump discharge P (MPa) — 0 = auto", nudPumpDischargeP_MPa),
            Row("Pump efficiency η",                 nudPumpEff),
            lblPumpFuel, lblPumpOx, lblPumpTotal,
            MakeHelp("PressureFed = no pump (default). Other cycles run per-pump head/power/RPM " +
                     "via centrifugal specific-speed and check NPSHA vs NPSHR. " +
                     "ElectricPump reports converter mass at 1.5 kg/kW shaft."));
    }
}
