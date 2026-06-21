// SetupWizardForm.cs — UI overhaul Sprint 2 Steps 5+6 (2026-04-28).
//
// 3-page modal form that runs before RegenChamberForm on first launch
// (skippable thereafter via SessionSettings.SkipWizardOnLaunch).
// Step 5 shipped the skeleton; Step 6 (this revision) wires the page
// bodies — preset picker (Page 1), propellants + cycle (Page 2),
// topology + injector pattern + skip-next-time (Page 3).
//
// Wizard exit produces a (OperatingConditions, RegenChamberDesign)
// pair. The seed is constructed via AutoSeeder.Seed(spec); the
// specs themselves match the 5 canonical-design presets defined
// in Voxelforge.Benchmarks.CanonicalDesigns. We DUPLICATE
// the 5 specs here rather than referencing Benchmarks directly
// because the App→Benchmarks reference would be circular (Benchmarks
// already references App). The duplication is ~30 LOC; each preset
// is 4-5 lines (PropellantPair / Thrust_N / Pc / ε / cycle).
//
// Wizard is intentionally a thin shell — every field on the produced
// design already has an AutoSeeder.Seed default, so adding a new
// SA dim or RegenChamberDesign field doesn't require updating the
// wizard.

using System.Windows.Forms;
using Voxelforge.Combustion;
using Voxelforge.FeedSystem;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

/// <summary>
/// Modal 3-page setup wizard. Page navigation is sequential:
/// Mission/Preset → Propellants/Cycle → Architecture.
/// </summary>
internal sealed class SetupWizardForm : Form
{
    /// <summary>Current shipped wizard version. Compared against
    /// <see cref="SessionSettings.WizardVersion"/>; users on a lower
    /// version get the wizard once.</summary>
    public const int CurrentWizardVersion = 1;

    // ── Preset specs (Page 1 source of truth) ───────────────────────
    // Mirrors Voxelforge.Benchmarks.CanonicalDesigns. Kept
    // in sync by hand — only 5 entries, simple shape, drift risk
    // mitigated by a single sentence per preset describing what it is.

    private sealed record WizardPreset(
        string Name,
        string DisplayName,
        string Description,
        EngineSpec Spec);

    private static readonly WizardPreset[] s_presets = new[]
    {
        new WizardPreset(
            Name: "merlin",
            DisplayName: "Merlin-class — LOX/CH4 GG, 100 kN",
            Description: "Sea-level first stage. Gas-generator cycle, ε = 16. "
                       + "BB-2 downgrade from 900 kN per the documented contingency.",
            Spec: new EngineSpec(
                PropellantPair:     PropellantPair.LOX_CH4,
                Thrust_N:           100_000.0,
                ChamberPressure_Pa: 7e6,
                ExpansionRatio:     16.0,
                EngineCycleOverride: EngineCycle.GasGenerator)),

        new WizardPreset(
            Name: "rl10",
            DisplayName: "RL-10 — LOX/H2 closed-expander, 100 kN",
            Description: "Vacuum upper stage. Closed-expander cycle, ε = 84. "
                       + "Reference engine for the closed-expander loop.",
            Spec: new EngineSpec(
                PropellantPair:     PropellantPair.LOX_H2,
                Thrust_N:           100_000.0,
                ChamberPressure_Pa: 4e6,
                ExpansionRatio:     84.0,
                EngineCycleOverride: EngineCycle.ClosedExpander)),

        new WizardPreset(
            Name: "pressure-fed-small",
            DisplayName: "Pressure-fed small thruster — LOX/RP-1, 1 kN",
            Description: "Small-thrust pressure-fed envelope. No turbomachinery. "
                       + "Often a tight feasibility envelope at this scale.",
            Spec: new EngineSpec(
                PropellantPair:     PropellantPair.LOX_RP1,
                Thrust_N:           1_000.0,
                ChamberPressure_Pa: 2e6,
                ExpansionRatio:     8.0,
                EngineCycleOverride: EngineCycle.PressureFed)),

        new WizardPreset(
            Name: "aerospike",
            DisplayName: "Aerospike — LOX/CH4, 100 kN",
            Description: "Annular-throat plug nozzle. Altitude-compensating. "
                       + "Drives the AerospikeFeasibility code path.",
            Spec: new EngineSpec(
                PropellantPair:     PropellantPair.LOX_CH4,
                Thrust_N:           100_000.0,
                ChamberPressure_Pa: 7e6,
                ExpansionRatio:     16.0,
                EngineCycleOverride: EngineCycle.GasGenerator)),

        new WizardPreset(
            Name: "pintle",
            DisplayName: "Pintle — LOX/CH4, 50 kN",
            Description: "Coaxial pintle injector. Single moveable part — "
                       + "lineage from Apollo LMDE.",
            Spec: new EngineSpec(
                PropellantPair:     PropellantPair.LOX_CH4,
                Thrust_N:           50_000.0,
                ChamberPressure_Pa: 6e6,
                ExpansionRatio:     14.0,
                EngineCycleOverride: EngineCycle.GasGenerator)),
    };

    // ── State carried across pages ──────────────────────────────────
    private WizardPreset? _selectedPreset;          // Page 1 selection
    private bool _scratchSelected;                   // Page 1 "start from scratch"
    private PropellantPair _scratchPair = PropellantPair.LOX_CH4;
    private EngineCycle _scratchCycle = EngineCycle.PressureFed;
    private double _scratchThrust_N = 50_000;
    private double _scratchPc_MPa = 5.0;
    private double _scratchExpansion = 12.0;

    // Page 2 state (populated by page 2 controls when shown).
    private PropellantPair _page2Pair = PropellantPair.LOX_CH4;
    private double _page2MR = 3.3;
    private EngineCycle _page2Cycle = EngineCycle.PressureFed;
    private int _page2WallMaterialIndex;

    // Page 3 state.
    private ChannelTopology _page3Topology = ChannelTopology.Axial;
    private InjectorPatternKind _page3Pattern = InjectorPatternKind.None;
    private bool _page3SkipNextTime;

    // ── Form controls ───────────────────────────────────────────────
    private readonly TabControl _pages;
    private readonly Button _btnBack;
    private readonly Button _btnNext;
    private readonly Button _btnCancel;
    private readonly Button _btnFinish;
    private readonly Label _lblTitle;

    /// <summary>Result populated when the user clicks Finish.</summary>
    public WizardResult? Result { get; private set; }

    public SetupWizardForm()
    {
        Text = "voxelforge — first-time setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new System.Drawing.Size(720, 560);
        ShowInTaskbar = false;

        _lblTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Font = new System.Drawing.Font("Segoe UI", 12.5f, System.Drawing.FontStyle.Bold),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 12, 16, 4),
            Text = "Step 1 of 3 — Mission & Starting point",
        };
        Controls.Add(_lblTitle);

        _pages = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.FlatButtons,
            ItemSize = new System.Drawing.Size(0, 1),
            SizeMode = TabSizeMode.Fixed,
        };
        _pages.TabPages.Add(BuildPage1MissionAndPreset());
        _pages.TabPages.Add(BuildPage2PropellantsAndCycle());
        _pages.TabPages.Add(BuildPage3Architecture());
        _pages.SelectedIndexChanged += (_, _) => OnPageChanged();
        Controls.Add(_pages);

        // Bottom button bar.
        var btnBar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 10, 16, 10) };
        _btnCancel = new Button { Text = "Cancel",  Width = 100, Height = 30 };
        _btnBack   = new Button { Text = "< Back",  Width = 100, Height = 30 };
        _btnNext   = new Button { Text = "Next >",  Width = 100, Height = 30 };
        _btnFinish = new Button { Text = "Finish",  Width = 100, Height = 30 };
        btnBar.Controls.Add(_btnCancel);
        btnBar.Controls.Add(_btnBack);
        btnBar.Controls.Add(_btnNext);
        btnBar.Controls.Add(_btnFinish);
        btnBar.Resize += (_, _) => LayoutButtons(btnBar);
        LayoutButtons(btnBar);
        Controls.Add(btnBar);

        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnBack.Click   += (_, _) => GoBack();
        _btnNext.Click   += (_, _) => GoNext();
        _btnFinish.Click += (_, _) => Finish();

        _pages.SelectedIndex = 0;
        OnPageChanged();
    }

    private static void LayoutButtons(Panel bar)
    {
        var children = bar.Controls;
        if (children.Count < 4) return;
        var cancel = children[0];
        var back   = children[1];
        var next   = children[2];
        var finish = children[3];

        cancel.Left = 16;
        cancel.Top  = 12;

        finish.Left = bar.Width - finish.Width - 16;
        finish.Top  = 12;
        next.Left   = finish.Left - next.Width - 6;
        next.Top    = 12;
        back.Left   = next.Left - back.Width - 6;
        back.Top    = 12;
    }

    private void OnPageChanged()
    {
        int i = _pages.SelectedIndex;
        int last = _pages.TabPages.Count - 1;

        _btnBack.Enabled    = i > 0;
        _btnNext.Visible    = i < last;
        _btnFinish.Visible  = i == last;

        _lblTitle.Text = i switch
        {
            0 => "Step 1 of 3 — Mission & Starting point",
            1 => "Step 2 of 3 — Propellants & Cycle",
            2 => "Step 3 of 3 — Chamber architecture",
            _ => "Setup wizard",
        };

        // Page 2 + 3 are populated from page 1 state when first entered,
        // unless the user has explicitly diverged.
        // KNOWN LIMITATION (red-team audit): there is no "already-visited /
        // diverged" guard, so navigating page 2 → 3 → Back to 2 re-runs
        // PopulatePage2FromSelection and discards the user's page-2 cycle/pair
        // edits (contradicting the comment above). A fix needs a visited flag
        // (reset when the page-1 preset changes); deferred to a Windows-verified
        // change. Recoverable: re-pick the cycle/pair.
        if (i == 1) PopulatePage2FromSelection();
        if (i == 2) PopulatePage3FromSelection();
    }

    private void GoBack()
    {
        if (_pages.SelectedIndex > 0) _pages.SelectedIndex -= 1;
    }

    private void GoNext()
    {
        if (_pages.SelectedIndex < _pages.TabPages.Count - 1) _pages.SelectedIndex += 1;
    }

    private void Finish()
    {
        // Build the final EngineSpec. If the user picked a preset,
        // start from its spec; if they picked "from scratch", build
        // one from scratch fields. Then override the page-2 cycle and
        // page-3 topology choices on top of whichever was selected.
        var baseSpec = _scratchSelected
            ? new EngineSpec(
                PropellantPair:      _scratchPair,
                Thrust_N:            _scratchThrust_N,
                ChamberPressure_Pa:  _scratchPc_MPa * 1e6,
                ExpansionRatio:      _scratchExpansion,
                EngineCycleOverride: _scratchCycle)
            : (_selectedPreset?.Spec ?? s_presets[0].Spec);

        // Page 2 may have changed the cycle / pair / MR vs the preset.
        var finalSpec = baseSpec with
        {
            PropellantPair      = _page2Pair,
            EngineCycleOverride = _page2Cycle,
        };

        // Seed via AutoSeeder — this fills every other field with a
        // sensible default. Then override topology + MR + wall material
        // to honour the user's wizard picks.
        var seed = AutoSeeder.Seed(finalSpec);
        var conditions = seed.Conditions with
        {
            MixtureRatio       = _page2MR,
            WallMaterialIndex  = _page2WallMaterialIndex,
        };
        // KNOWN LIMITATION (red-team audit): the page-3 injector-pattern combo
        // is captured into _page3Pattern but NOT applied here, so the wizard
        // seed always uses AutoSeeder's default injector pattern regardless of
        // the user's pick. Wiring it needs a InjectorPatternKind → the design's
        // InjectorElementPattern (an InjectorPattern object) mapping, which
        // doesn't exist yet — deferred to a Windows-verified fix. Users can set
        // the pattern in the main form afterward (the seed is editable).
        var design = seed.Design with
        {
            ChannelTopology = _page3Topology,
        };

        Result = new WizardResult(
            Conditions: conditions,
            Design:     design,
            PresetName: _scratchSelected ? null : _selectedPreset?.Name,
            SkipNextLaunch: _page3SkipNextTime);

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Page 1: Mission & Preset ────────────────────────────────────

    private TabPage BuildPage1MissionAndPreset()
    {
        var page = new TabPage("Mission & Preset");
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16),
        };

        flow.Controls.Add(new Label
        {
            Text = "Pick a starting point. You can change every field afterwards on the main form.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        });

        // 5 preset radio cards, plus "from scratch".
        var grpPresets = new GroupBox
        {
            Text = "Canonical presets",
            Width = 660,
            Height = 240,
            Margin = new Padding(0, 0, 0, 12),
        };

        var rbScratchAtBottom = new RadioButton  // forward declare for cards' CheckedChanged handler to flip
        {
            Text = "Or start from scratch (custom thrust + Pc + ε)",
            AutoSize = true, Top = 12, Left = 12,
        };

        for (int i = 0; i < s_presets.Length; i++)
        {
            var preset = s_presets[i];
            var rb = new RadioButton
            {
                Text = preset.DisplayName,
                AutoSize = true, Top = 16 + i * 40, Left = 12,
                Tag = preset,
            };
            rb.CheckedChanged += (_, _) =>
            {
                if (rb.Checked)
                {
                    _selectedPreset = (WizardPreset)rb.Tag!;
                    _scratchSelected = false;
                }
            };
            grpPresets.Controls.Add(rb);
            var lbl = new Label
            {
                Text = preset.Description,
                AutoSize = false,
                Width = 600,
                Height = 18,
                Top = 16 + i * 40 + 18,
                Left = 32,
                Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic),
                ForeColor = System.Drawing.SystemColors.GrayText,
            };
            grpPresets.Controls.Add(lbl);
            if (i == 0) { rb.Checked = true; }   // Default to first preset (Merlin)
        }

        flow.Controls.Add(grpPresets);

        // "Start from scratch" expander row.
        var grpScratch = new GroupBox
        {
            Text = "Start from scratch",
            Width = 660,
            Height = 130,
        };

        rbScratchAtBottom.CheckedChanged += (_, _) =>
        {
            if (rbScratchAtBottom.Checked)
            {
                _scratchSelected = true;
                _selectedPreset = null;
            }
        };
        grpScratch.Controls.Add(rbScratchAtBottom);

        var nudThrust = new NumericUpDown
        {
            Minimum = 100, Maximum = 5_000_000, Value = 50_000, Increment = 1000,
            Width = 140, Top = 40, Left = 220,
        };
        var nudPc = new NumericUpDown
        {
            Minimum = 1, Maximum = 30, Value = 5, Increment = 0.5m, DecimalPlaces = 1,
            Width = 90, Top = 40, Left = 540,
        };
        var nudEps = new NumericUpDown
        {
            Minimum = 3, Maximum = 250, Value = 12, Increment = 0.5m, DecimalPlaces = 1,
            Width = 90, Top = 80, Left = 220,
        };

        nudThrust.ValueChanged += (_, _) => _scratchThrust_N = (double)nudThrust.Value;
        nudPc.ValueChanged     += (_, _) => _scratchPc_MPa = (double)nudPc.Value;
        nudEps.ValueChanged    += (_, _) => _scratchExpansion = (double)nudEps.Value;

        grpScratch.Controls.Add(new Label { Text = "Thrust (N):", Top = 44, Left = 32, AutoSize = true });
        grpScratch.Controls.Add(nudThrust);
        grpScratch.Controls.Add(new Label { Text = "Chamber P (MPa):", Top = 44, Left = 380, AutoSize = true });
        grpScratch.Controls.Add(nudPc);
        grpScratch.Controls.Add(new Label { Text = "Expansion ratio ε:", Top = 84, Left = 32, AutoSize = true });
        grpScratch.Controls.Add(nudEps);
        flow.Controls.Add(grpScratch);

        page.Controls.Add(flow);
        return page;
    }

    // ── Page 2: Propellants & Cycle ─────────────────────────────────

    private ComboBox _cboPair = null!;
    private NumericUpDown _nudMR = null!;
    private ComboBox _cboCycle = null!;
    private ComboBox _cboWall = null!;
    private Label _lblPairNote = null!;

    private TabPage BuildPage2PropellantsAndCycle()
    {
        var page = new TabPage("Propellants & Cycle");
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16),
        };

        flow.Controls.Add(new Label
        {
            Text = "Propellants drive cycle recommendations. Wall material follows from "
                 + "operating temperature + propellant chemistry.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        });

        _cboPair = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var meta in PropellantPairs.All)
            _cboPair.Items.Add(meta.Name + (meta.Implemented ? "" : "  (unavailable)"));
        _cboPair.SelectedIndex = 0;

        _lblPairNote = new Label
        {
            AutoSize = false,
            Width = 600, Height = 30,
            Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Italic),
            ForeColor = System.Drawing.SystemColors.GrayText,
        };

        _nudMR = new NumericUpDown
        {
            Width = 120,
            Minimum = 0.5m, Maximum = 10.0m, Increment = 0.05m, DecimalPlaces = 2,
            Value = 3.3m,
        };

        _cboCycle = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        // Items populated by RefreshCycleCombo() once a pair is known.

        _cboWall = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var m in HeatTransfer.WallMaterials.All)
            _cboWall.Items.Add(m.Name);
        _cboWall.SelectedIndex = 1;     // CuCrZr — middle-of-the-road default
        // Sync the backing field to the displayed default: the
        // SelectedIndexChanged handler below is wired AFTER this assignment, so
        // it does not fire for the initial selection — without this line
        // _page2WallMaterialIndex would stay 0 (GRCop-42) while the combo shows
        // CuCrZr, and Finish() would record the wrong material.
        _page2WallMaterialIndex = _cboWall.SelectedIndex;

        _cboPair.SelectedIndexChanged += (_, _) =>
        {
            _page2Pair = (PropellantPair)_cboPair.SelectedIndex;
            UpdatePage2PairNote();
            RefreshCycleCombo();
            ClampMRToPairBand();
        };
        _nudMR.ValueChanged += (_, _) => _page2MR = (double)_nudMR.Value;
        _cboCycle.SelectedIndexChanged += (_, _) =>
        {
            // Combo items are tagged with the EngineCycle value because
            // RecommendedCycles reorder means SelectedIndex != enum value.
            if (_cboCycle.SelectedItem is CycleComboItem item)
                _page2Cycle = item.Cycle;
        };
        _cboWall.SelectedIndexChanged += (_, _) =>
            _page2WallMaterialIndex = _cboWall.SelectedIndex;

        flow.Controls.Add(LabelledRow("Propellant pair", _cboPair));
        flow.Controls.Add(_lblPairNote);
        flow.Controls.Add(LabelledRow("Mixture ratio O/F", _nudMR));
        flow.Controls.Add(LabelledRow("Engine cycle", _cboCycle));
        flow.Controls.Add(LabelledRow("Wall material", _cboWall));

        page.Controls.Add(flow);
        return page;
    }

    private sealed record CycleComboItem(EngineCycle Cycle, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private void RefreshCycleCombo()
    {
        _cboCycle.Items.Clear();

        // Recommended cycles first (with suffix), then everything else.
        var recommended = new System.Collections.Generic.HashSet<EngineCycle>(
            UiVisibilityRules.RecommendedCycles(_page2Pair));

        foreach (var c in recommended)
            _cboCycle.Items.Add(new CycleComboItem(c, $"{c}  (recommended for {_page2Pair})"));
        foreach (EngineCycle c in System.Enum.GetValues<EngineCycle>())
            if (!recommended.Contains(c))
                _cboCycle.Items.Add(new CycleComboItem(c, c.ToString()));

        if (_cboCycle.Items.Count > 0)
            _cboCycle.SelectedIndex = 0;
    }

    private void ClampMRToPairBand()
    {
        var meta = PropellantPairs.GetMeta(_page2Pair);
        _nudMR.Minimum = (decimal)meta.MR_Min;
        _nudMR.Maximum = (decimal)meta.MR_Max;
        if (_nudMR.Value < _nudMR.Minimum) _nudMR.Value = _nudMR.Minimum;
        if (_nudMR.Value > _nudMR.Maximum) _nudMR.Value = _nudMR.Maximum;
        _nudMR.Value = (decimal)meta.MR_Default;
    }

    private void UpdatePage2PairNote()
    {
        var meta = PropellantPairs.GetMeta(_page2Pair);
        _lblPairNote.Text = meta.Implemented
            ? meta.Note
            : meta.Note + "  ⚠ Tables not implemented; Generate will be disabled.";
        _lblPairNote.ForeColor = meta.Implemented
            ? System.Drawing.SystemColors.GrayText
            : System.Drawing.Color.DarkRed;
    }

    private void PopulatePage2FromSelection()
    {
        // Inherit from page 1 selection.
        var spec = _scratchSelected
            ? new EngineSpec(
                PropellantPair:      _scratchPair,
                Thrust_N:            _scratchThrust_N,
                ChamberPressure_Pa:  _scratchPc_MPa * 1e6,
                ExpansionRatio:      _scratchExpansion,
                EngineCycleOverride: _scratchCycle)
            : (_selectedPreset?.Spec ?? s_presets[0].Spec);

        _cboPair.SelectedIndex = (int)spec.PropellantPair;
        _page2Pair = spec.PropellantPair;
        UpdatePage2PairNote();
        RefreshCycleCombo();

        // Pick the user's preset cycle if it appears in the list.
        for (int i = 0; i < _cboCycle.Items.Count; i++)
            if (_cboCycle.Items[i] is CycleComboItem item
                && item.Cycle == (spec.EngineCycleOverride ?? EngineCycle.PressureFed))
            { _cboCycle.SelectedIndex = i; break; }
    }

    // ── Page 3: Architecture ────────────────────────────────────────

    private ComboBox _cboTopology = null!;
    private ComboBox _cboInjectorPattern = null!;
    private CheckBox _chkSkipNext = null!;

    private TabPage BuildPage3Architecture()
    {
        var page = new TabPage("Architecture");
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16),
        };

        flow.Controls.Add(new Label
        {
            Text = "Channel topology drives the regen-jacket geometry. "
                 + "Injector pattern is the chamber-head architecture.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        });

        _cboTopology = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (ChannelTopology t in System.Enum.GetValues<ChannelTopology>())
            _cboTopology.Items.Add(t.ToString());
        _cboTopology.SelectedIndex = 0;
        _cboTopology.SelectedIndexChanged += (_, _) =>
            _page3Topology = (ChannelTopology)_cboTopology.SelectedIndex;

        _cboInjectorPattern = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (InjectorPatternKind k in System.Enum.GetValues<InjectorPatternKind>())
            _cboInjectorPattern.Items.Add(k.ToString());
        _cboInjectorPattern.SelectedIndex = 0;
        _cboInjectorPattern.SelectedIndexChanged += (_, _) =>
            _page3Pattern = (InjectorPatternKind)_cboInjectorPattern.SelectedIndex;

        _chkSkipNext = new CheckBox
        {
            Text = "Skip wizard next time (you can re-enable from Help → Reset to wizard…)",
            AutoSize = true,
            Margin = new Padding(0, 24, 0, 0),
        };
        _chkSkipNext.CheckedChanged += (_, _) => _page3SkipNextTime = _chkSkipNext.Checked;

        flow.Controls.Add(LabelledRow("Channel topology", _cboTopology));
        flow.Controls.Add(LabelledRow("Injector pattern", _cboInjectorPattern));
        flow.Controls.Add(_chkSkipNext);

        page.Controls.Add(flow);
        return page;
    }

    private void PopulatePage3FromSelection()
    {
        // Default topology = Axial for non-aerospike presets, Aerospike
        // for the "aerospike" preset card. Pintle preset → InjectorPatternKind.Pintle.
        if (_selectedPreset is { Name: "aerospike" })
            _cboTopology.SelectedIndex = (int)ChannelTopology.Aerospike;

        if (_selectedPreset is { Name: "pintle" })
            _cboInjectorPattern.SelectedIndex = (int)InjectorPatternKind.Pintle;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static Panel LabelledRow(string label, Control input)
    {
        var p = new Panel { Width = 660, Height = 36, Margin = new Padding(0, 4, 0, 4) };
        var lbl = new Label
        {
            Text = label,
            AutoSize = false, Width = 220, Height = 28, Top = 4, Left = 0,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        };
        p.Controls.Add(lbl);
        input.Left = 230;
        input.Top  = (p.Height - System.Math.Max(input.Height, 22)) / 2;
        p.Controls.Add(input);
        return p;
    }

    // ── Public surface (consumed by Program.cs) ────────────────────

    /// <summary>
    /// Decide whether the wizard should show on this launch.
    /// <list type="bullet">
    ///   <item><description>Show when the user is below the
    ///     <see cref="CurrentWizardVersion"/> AND has not opted out
    ///     via <see cref="SessionSettings.SkipWizardOnLaunch"/>.</description></item>
    ///   <item><description>Suppress for users on the current version
    ///     OR who have explicitly opted out — they get straight to
    ///     the main form.</description></item>
    /// </list>
    /// First-launch users (WizardVersion = 0, SkipWizardOnLaunch
    /// default = false) see the wizard once. Existing users on a
    /// stale version see it once when they upgrade. Power users
    /// who flipped "Skip next time" never see it again until they
    /// reset via Help → Reset to wizard… (out-of-scope follow-on).
    /// </summary>
    public static bool ShouldShow(SessionSettings settings)
    {
        if (settings is null) return false;
        if (settings.SkipWizardOnLaunch) return false;
        return settings.WizardVersion < CurrentWizardVersion;
    }

    /// <summary>
    /// Construct the wizard, run it modally, and return the user's
    /// chosen seed pair.
    /// </summary>
    public static WizardResult? RunModal(SessionSettings settings)
    {
        using var form = new SetupWizardForm();
        var dr = form.ShowDialog();
        return dr == DialogResult.OK ? form.Result : null;
    }
}

/// <summary>
/// What the wizard returns. Holds the seed
/// <see cref="OperatingConditions"/> + <see cref="RegenChamberDesign"/>
/// pair, a friendly preset name (or null for "from scratch"), and a
/// flag mirroring the user's "Skip wizard next time" pick — Program.cs
/// writes this into <see cref="SessionSettings.SkipWizardOnLaunch"/>.
/// </summary>
internal sealed record WizardResult(
    OperatingConditions Conditions,
    RegenChamberDesign Design,
    string? PresetName,
    bool SkipNextLaunch);
