// AirbreathingForm.cs — WinForms UI for the air-breathing engine voxel viewer.
//
// Surfaces every cycle solver in `AirbreathingCycleSolvers`: Ramjet,
// Turbojet (with optional afterburner), Turbofan, Scramjet, RBCC,
// GasTurbine, SteamTurbine, Pulsejet, Turboprop, Turboshaft. Per-kind
// GroupBox panels light up via UpdateKindVisibility(). Voxel preview
// fires only for Ramjet + Pulsejet today (the only two voxel builders
// that exist); other kinds run physics only. Issue #441.

using System.Globalization;
using Voxelforge.Airbreathing;
using Voxelforge.Airbreathing.Cycles;
using Voxelforge.Airbreathing.Geometry;

namespace Voxelforge.UI;

internal sealed partial class AirbreathingForm : Form
{
    // ── Shared controls ───────────────────────────────────────────────────
    private readonly ComboBox      _cmbKind          = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly NumericUpDown _nudAltitude      = MakeNud(0,      50_000, 10_000, 0);
    private readonly NumericUpDown _nudMach          = MakeNud(0.5m,   6.0m,   2.0m,   2);
    private readonly NumericUpDown _nudEquivRatio    = MakeNud(0.5m,   1.5m,   1.0m,   2);
    private readonly NumericUpDown _nudWallThickness = MakeNud(0.5m,   20.0m,  2.0m,   1);

    // ── Ramjet-specific controls ──────────────────────────────────────────
    private readonly NumericUpDown _nudInletArea     = MakeNud(0.001m, 2.0m,   0.010m, 4);
    private readonly NumericUpDown _nudCombArea      = MakeNud(0.001m, 2.0m,   0.015m, 4);
    private readonly NumericUpDown _nudCombLength    = MakeNud(0.05m,  5.0m,   0.50m,  3);
    private readonly NumericUpDown _nudThroatArea    = MakeNud(0.001m, 2.0m,   0.008m, 4);
    private readonly NumericUpDown _nudExitArea      = MakeNud(0.001m, 2.0m,   0.024m, 4);

    // ── Pulsejet-specific controls ────────────────────────────────────────
    private readonly ComboBox      _cmbPjVariant      = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _nudTubeLength     = MakeNud(0.5m,  6.0m,   3.40m,  2);
    private readonly NumericUpDown _nudPjIntakeArea   = MakeNud(0.001m, 0.5m,  0.030m, 4);
    private readonly NumericUpDown _nudPjTailpipeArea = MakeNud(0.001m, 0.5m,  0.040m, 4);
    private readonly NumericUpDown _nudPjCombArea     = MakeNud(0.001m, 2.0m,  0.075m, 4);
    private readonly NumericUpDown _nudPjCombLength   = MakeNud(0.05m,  5.0m,  0.80m,  3);

    // ── Turbo-cycle shared controls (turbojet / turbofan / turboprop / turboshaft / gas turbine) ─
    private readonly NumericUpDown _nudCompressorPR    = MakeNud(2.0m,  40.0m, 8.0m,  1);
    private readonly CheckBox      _chkAfterburner     = new() { Text = "Enable afterburner (turbojet)", AutoSize = true };
    private readonly NumericUpDown _nudAfterburnerFAR  = MakeNud(0m,    0.05m, 0.025m, 3);

    // ── Turbofan-specific ─────────────────────────────────────────────────
    private readonly NumericUpDown _nudBypassRatio    = MakeNud(0.10m, 2.0m,  0.34m,  2);

    // ── Turboprop-specific ────────────────────────────────────────────────
    private readonly NumericUpDown _nudPropellerFpe   = MakeNud(0.50m, 0.95m, 0.89m,  2);

    // ── Gas-turbine-specific ──────────────────────────────────────────────
    private readonly NumericUpDown _nudRecuperatorEff = MakeNud(0m,    0.85m, 0.0m,   2);
    private readonly NumericUpDown _nudShaftPowerKw   = MakeNud(0m,    50_000m, 1000m, 0);

    // ── Steam-turbine-specific ────────────────────────────────────────────
    private readonly NumericUpDown _nudSteamBoilerBar = MakeNud(10m,   200m,   60m,   1);
    private readonly NumericUpDown _nudSteamCondBar   = MakeNud(0.02m, 1.0m,   0.05m, 3);
    private readonly NumericUpDown _nudSteamSuperheatK = MakeNud(0m,   500m,   150m,  0);

    // ── Scramjet-specific ─────────────────────────────────────────────────
    private readonly NumericUpDown _nudIsolatorLength = MakeNud(0.05m, 5.0m,   0.50m, 3);

    // ── RBCC-specific ─────────────────────────────────────────────────────
    private readonly ComboBox      _cmbRbccMode       = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly NumericUpDown _nudRbccEntrainER  = MakeNud(0.1m,  10m,    1.0m,  2);

    // ── Output controls ───────────────────────────────────────────────────
    private readonly Button  _btnGenerate  = new() { Text = "Generate Preview", Height = 36 };
    private readonly TextBox _txtResults   = new() { Multiline = true, ReadOnly = true, Height = 170, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };
    private readonly Label   _lblStatus    = new() { Text = "Ready.", AutoSize = true };

    // ── Layout panels (shown/hidden by kind) ──────────────────────────────
    private GroupBox? _ramjetGeometryGroup;
    private GroupBox? _pulsejetGeometryGroup;
    private GroupBox? _turbojetGeometryGroup;
    private GroupBox? _turbofanGeometryGroup;
    private GroupBox? _turbopropGeometryGroup;
    private GroupBox? _turboshaftGeometryGroup;
    private GroupBox? _gasTurbineGeometryGroup;
    private GroupBox? _steamTurbineGeometryGroup;
    private GroupBox? _scramjetGeometryGroup;
    private GroupBox? _rbccGeometryGroup;

    // Callbacks wired by the caller (Program.cs UiThreadMain)
    private readonly Action<FlightConditions, AirbreathingEngineDesign, RamjetBuildOptions> _onGenerate;

    internal AirbreathingForm(
        Action<FlightConditions, AirbreathingEngineDesign, RamjetBuildOptions> onGenerate,
        Airbreathing.AirbreathingEngineKind initialKind = Airbreathing.AirbreathingEngineKind.Ramjet)
    {
        _onGenerate = onGenerate ?? throw new ArgumentNullException(nameof(onGenerate));

        // Populate kind ComboBox — display-name strings keyed to AirbreathingEngineKind enum.
        // Order matches the enum (Ramjet=1 first; sentinel None=0 omitted).
        _cmbKind.Items.AddRange(new object[]
        {
            "Ramjet",
            "Turbojet",
            "Turbofan",
            "Scramjet",
            "RBCC",
            "GasTurbine",
            "SteamTurbine",
            "Pulsejet",
            "Turboprop",
            "Turboshaft",
        });
        _cmbKind.SelectedIndex = KindToIndex(initialKind);
        _cmbKind.SelectedIndexChanged += (_, _) => UpdateKindVisibility();

        // Populate pulsejet variant ComboBox.
        _cmbPjVariant.Items.AddRange(new object[]
        {
            "Standard (V-1 / reed-valve, η_vol = 0.14)",
            "Valveless (Lockwood-Hiller U-tube, η_vol = 0.10)",
        });
        _cmbPjVariant.SelectedIndex = 0;

        // Populate RBCC mode ComboBox (matches RbccOperatingMode enum).
        _cmbRbccMode.Items.AddRange(new object[] { "DuctedRocket", "Ramjet", "Scramjet" });
        _cmbRbccMode.SelectedIndex = 1; // Ramjet — solver default

        Text            = "Voxelforge — Air-Breathing Viewer  [ramjet]";
        Width           = 440;
        AutoSize        = true;
        AutoSizeMode    = AutoSizeMode.GrowOnly;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        Padding         = new Padding(10);
        StartPosition   = FormStartPosition.Manual;
        Location        = new Point(10, 10);

        var layout = new TableLayoutPanel
        {
            Dock         = DockStyle.Fill,
            ColumnCount  = 1,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding      = new Padding(0),
        };

        layout.Controls.Add(BuildKindGroup());
        layout.Controls.Add(BuildFlightGroup());

        _ramjetGeometryGroup       = BuildRamjetGeometryGroup();
        _pulsejetGeometryGroup     = BuildPulsejetGeometryGroup();
        _turbojetGeometryGroup     = BuildTurbojetGeometryGroup();
        _turbofanGeometryGroup     = BuildTurbofanGeometryGroup();
        _turbopropGeometryGroup    = BuildTurbopropGeometryGroup();
        _turboshaftGeometryGroup   = BuildTurboshaftGeometryGroup();
        _gasTurbineGeometryGroup   = BuildGasTurbineGeometryGroup();
        _steamTurbineGeometryGroup = BuildSteamTurbineGeometryGroup();
        _scramjetGeometryGroup     = BuildScramjetGeometryGroup();
        _rbccGeometryGroup         = BuildRbccGeometryGroup();
        layout.Controls.Add(_ramjetGeometryGroup);
        layout.Controls.Add(_pulsejetGeometryGroup);
        layout.Controls.Add(_turbojetGeometryGroup);
        layout.Controls.Add(_turbofanGeometryGroup);
        layout.Controls.Add(_turbopropGeometryGroup);
        layout.Controls.Add(_turboshaftGeometryGroup);
        layout.Controls.Add(_gasTurbineGeometryGroup);
        layout.Controls.Add(_steamTurbineGeometryGroup);
        layout.Controls.Add(_scramjetGeometryGroup);
        layout.Controls.Add(_rbccGeometryGroup);

        layout.Controls.Add(BuildBuildOptionsGroup());
        layout.Controls.Add(_btnGenerate);
        layout.Controls.Add(_txtResults);
        layout.Controls.Add(_lblStatus);

        Controls.Add(layout);

        _btnGenerate.Dock   = DockStyle.Fill;
        _txtResults.Dock    = DockStyle.Fill;
        _txtResults.BackColor = SystemColors.Window;

        _btnGenerate.Click += (_, _) => PostGenerate();

        UpdateKindVisibility();
        Load += (_, _) => PostGenerate();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static NumericUpDown MakeNud(decimal min, decimal max, decimal @default, int decimals)
    {
        var nud = new NumericUpDown
        {
            Minimum       = min,
            Maximum       = max,
            Value         = @default,
            DecimalPlaces = decimals,
            Increment     = decimals == 0 ? 100 : (decimal)Math.Pow(10, -decimals),
            Width         = 110,
        };
        return nud;
    }

    private static GroupBox BuildLabeledGroup(string title, params (string label, Control ctrl)[] rows)
    {
        var gb = new GroupBox
        {
            Text     = title,
            AutoSize = true,
            Dock     = DockStyle.Fill,
            Padding  = new Padding(6, 14, 6, 6),
        };

        var inner = new TableLayoutPanel
        {
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 2,
            Dock         = DockStyle.Fill,
            Padding      = new Padding(0),
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        foreach (var (label, ctrl) in rows)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, TextAlign = ContentAlignment.MiddleLeft };
            inner.Controls.Add(lbl);
            ctrl.Anchor = AnchorStyles.Left;
            inner.Controls.Add(ctrl);
        }

        gb.Controls.Add(inner);
        return gb;
    }

    private GroupBox BuildKindGroup() =>
        BuildLabeledGroup("Engine kind",
            ("Kind:", _cmbKind));

    private GroupBox BuildFlightGroup()
    {
        var fuelLabel = new Label
        {
            Text      = "H₂ (ramjet) / JP-8 (pulsejet)",
            AutoSize  = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        return BuildLabeledGroup("Flight conditions",
            ("Altitude (m):", _nudAltitude),
            ("Mach number:", _nudMach),
            ("Fuel:", fuelLabel));
    }

    private GroupBox BuildRamjetGeometryGroup() =>
        BuildLabeledGroup("Ramjet geometry",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildPulsejetGeometryGroup() =>
        BuildLabeledGroup("Pulsejet geometry",
            ("Variant:", _cmbPjVariant),
            ("Tube length (m):", _nudTubeLength),
            ("Intake area (m²):", _nudPjIntakeArea),
            ("Tailpipe area (m²):", _nudPjTailpipeArea),
            ("Combustor area (m²):", _nudPjCombArea),
            ("Combustor length (m):", _nudPjCombLength),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildTurbojetGeometryGroup() =>
        BuildLabeledGroup("Turbojet (J79-class)",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Compressor π_c:", _nudCompressorPR),
            ("Equivalence ratio:", _nudEquivRatio),
            ("(reheat):", _chkAfterburner),
            ("Afterburner f/a:", _nudAfterburnerFAR));

    private GroupBox BuildTurbofanGeometryGroup() =>
        BuildLabeledGroup("Turbofan (F404-class)",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Compressor π_c:", _nudCompressorPR),
            ("Bypass ratio:", _nudBypassRatio),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildTurbopropGeometryGroup() =>
        BuildLabeledGroup("Turboprop (T56-A-15-class)",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Compressor π_c:", _nudCompressorPR),
            ("Propeller f_pe:", _nudPropellerFpe),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildTurboshaftGeometryGroup() =>
        BuildLabeledGroup("Turboshaft (T700-GE-701C-class)",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Compressor π_c:", _nudCompressorPR),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildGasTurbineGeometryGroup() =>
        BuildLabeledGroup("Gas turbine (Brayton)",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Compressor π_c:", _nudCompressorPR),
            ("Recuperator ε:", _nudRecuperatorEff),
            ("Shaft power target (kW):", _nudShaftPowerKw),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildSteamTurbineGeometryGroup() =>
        BuildLabeledGroup("Steam turbine (Rankine)",
            ("Boiler P (bar):", _nudSteamBoilerBar),
            ("Condenser P (bar):", _nudSteamCondBar),
            ("Superheat ΔT (K):", _nudSteamSuperheatK));

    private GroupBox BuildScramjetGeometryGroup() =>
        BuildLabeledGroup("Scramjet",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("Isolator length (m):", _nudIsolatorLength),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildRbccGeometryGroup() =>
        BuildLabeledGroup("RBCC (rocket-based combined cycle)",
            ("Inlet area (m²):", _nudInletArea),
            ("Combustor area (m²):", _nudCombArea),
            ("Combustor length (m):", _nudCombLength),
            ("Throat area (m²):", _nudThroatArea),
            ("Exit area (m²):", _nudExitArea),
            ("RBCC mode:", _cmbRbccMode),
            ("Ejector ER:", _nudRbccEntrainER),
            ("Equivalence ratio:", _nudEquivRatio));

    private GroupBox BuildBuildOptionsGroup() =>
        BuildLabeledGroup("Build options",
            ("Wall thickness (mm):", _nudWallThickness));

    private static int KindToIndex(AirbreathingEngineKind kind) => kind switch
    {
        AirbreathingEngineKind.Ramjet       => 0,
        AirbreathingEngineKind.Turbojet     => 1,
        AirbreathingEngineKind.Turbofan     => 2,
        AirbreathingEngineKind.Scramjet     => 3,
        AirbreathingEngineKind.Rbcc         => 4,
        AirbreathingEngineKind.GasTurbine   => 5,
        AirbreathingEngineKind.SteamTurbine => 6,
        AirbreathingEngineKind.Pulsejet     => 7,
        AirbreathingEngineKind.Turboprop    => 8,
        AirbreathingEngineKind.Turboshaft   => 9,
        _                                   => 0, // None / unknown → Ramjet
    };

    private AirbreathingEngineKind SelectedKind() => (_cmbKind.SelectedItem as string) switch
    {
        "Ramjet"       => AirbreathingEngineKind.Ramjet,
        "Turbojet"     => AirbreathingEngineKind.Turbojet,
        "Turbofan"     => AirbreathingEngineKind.Turbofan,
        "Scramjet"     => AirbreathingEngineKind.Scramjet,
        "RBCC"         => AirbreathingEngineKind.Rbcc,
        "GasTurbine"   => AirbreathingEngineKind.GasTurbine,
        "SteamTurbine" => AirbreathingEngineKind.SteamTurbine,
        "Pulsejet"     => AirbreathingEngineKind.Pulsejet,
        "Turboprop"    => AirbreathingEngineKind.Turboprop,
        "Turboshaft"   => AirbreathingEngineKind.Turboshaft,
        _              => AirbreathingEngineKind.Ramjet,
    };

    private void UpdateKindVisibility()
    {
        var kind = SelectedKind();

        if (_ramjetGeometryGroup        != null) _ramjetGeometryGroup.Visible       = kind == AirbreathingEngineKind.Ramjet;
        if (_pulsejetGeometryGroup      != null) _pulsejetGeometryGroup.Visible     = kind == AirbreathingEngineKind.Pulsejet;
        if (_turbojetGeometryGroup      != null) _turbojetGeometryGroup.Visible     = kind == AirbreathingEngineKind.Turbojet;
        if (_turbofanGeometryGroup      != null) _turbofanGeometryGroup.Visible     = kind == AirbreathingEngineKind.Turbofan;
        if (_turbopropGeometryGroup     != null) _turbopropGeometryGroup.Visible    = kind == AirbreathingEngineKind.Turboprop;
        if (_turboshaftGeometryGroup    != null) _turboshaftGeometryGroup.Visible   = kind == AirbreathingEngineKind.Turboshaft;
        if (_gasTurbineGeometryGroup    != null) _gasTurbineGeometryGroup.Visible   = kind == AirbreathingEngineKind.GasTurbine;
        if (_steamTurbineGeometryGroup  != null) _steamTurbineGeometryGroup.Visible = kind == AirbreathingEngineKind.SteamTurbine;
        if (_scramjetGeometryGroup      != null) _scramjetGeometryGroup.Visible     = kind == AirbreathingEngineKind.Scramjet;
        if (_rbccGeometryGroup          != null) _rbccGeometryGroup.Visible         = kind == AirbreathingEngineKind.Rbcc;

        Text = $"Voxelforge — Air-Breathing Viewer  [{kind.ToString().ToLowerInvariant()}]";
    }

    private static AirbreathingFuel FuelForKind(AirbreathingEngineKind kind) => kind switch
    {
        AirbreathingEngineKind.Pulsejet  => AirbreathingFuel.Jp8,  // V-1 reference
        // Ramjet / scramjet / RBCC sit in the H₂ regime; turbo-cycle
        // engines (turbojet/fan/prop/shaft + power-gen Brayton/Rankine)
        // use JP-8 / kerosene by convention. The cycle solvers carry
        // their own fuel-specific properties, so this label is purely
        // a UI hint until per-kind fuel selectors land.
        AirbreathingEngineKind.Ramjet      => AirbreathingFuel.H2,
        AirbreathingEngineKind.Scramjet    => AirbreathingFuel.H2,
        AirbreathingEngineKind.Rbcc        => AirbreathingFuel.H2,
        _                                  => AirbreathingFuel.Jp8,
    };

    private void PostGenerate()
    {
        SetStatus("Generating…");

        var kind = SelectedKind();

        var cond = new FlightConditions(
            Altitude_m: (double)_nudAltitude.Value,
            MachNumber: (double)_nudMach.Value,
            Fuel:       FuelForKind(kind));

        AirbreathingEngineDesign design;

        if (kind == AirbreathingEngineKind.Pulsejet)
        {
            bool valveless = _cmbPjVariant.SelectedIndex == 1;
            design = new AirbreathingEngineDesign(
                Kind:                AirbreathingEngineKind.Pulsejet,
                InletThroatArea_m2:  (double)_nudPjIntakeArea.Value,
                CombustorArea_m2:    (double)_nudPjCombArea.Value,
                CombustorLength_m:   (double)_nudPjCombLength.Value,
                NozzleThroatArea_m2: (double)_nudPjIntakeArea.Value,  // no CD throat on pulsejet
                NozzleExitArea_m2:   (double)_nudPjTailpipeArea.Value,
                EquivalenceRatio:    (double)_nudEquivRatio.Value)
            {
                PulsejetTubeLength_m    = (double)_nudTubeLength.Value,
                PulsejetIntakeArea_m2   = (double)_nudPjIntakeArea.Value,
                PulsejetTailpipeArea_m2 = (double)_nudPjTailpipeArea.Value,
                PulsejetVariant         = valveless ? PulsejetVariant.Valveless : PulsejetVariant.Standard,
            };
        }
        else if (kind == AirbreathingEngineKind.SteamTurbine)
        {
            // Steam turbine has no airflow geometry; pass placeholder areas.
            design = new AirbreathingEngineDesign(
                Kind:                AirbreathingEngineKind.SteamTurbine,
                InletThroatArea_m2:  0.01,
                CombustorArea_m2:    0.01,
                CombustorLength_m:   0.1,
                NozzleThroatArea_m2: 0.01,
                NozzleExitArea_m2:   0.01,
                EquivalenceRatio:    1.0)
            {
                SteamBoilerPressure_bar    = (double)_nudSteamBoilerBar.Value,
                SteamCondensePressure_bar  = (double)_nudSteamCondBar.Value,
                SteamSuperheatDeltaT_K     = (double)_nudSteamSuperheatK.Value,
            };
        }
        else
        {
            // Common airflow-cycle geometry — ramjet / turbojet / turbofan /
            // scramjet / RBCC / gas turbine / turboprop / turboshaft.
            design = new AirbreathingEngineDesign(
                Kind:                kind,
                InletThroatArea_m2:  (double)_nudInletArea.Value,
                CombustorArea_m2:    (double)_nudCombArea.Value,
                CombustorLength_m:   (double)_nudCombLength.Value,
                NozzleThroatArea_m2: (double)_nudThroatArea.Value,
                NozzleExitArea_m2:   (double)_nudExitArea.Value,
                EquivalenceRatio:    (double)_nudEquivRatio.Value,
                CompressorPressureRatio: KindUsesCompressor(kind) ? (double)_nudCompressorPR.Value : 1.0,
                BypassRatio:         kind == AirbreathingEngineKind.Turbofan ? (double)_nudBypassRatio.Value : 0.0,
                IsolatorLength_m:    kind == AirbreathingEngineKind.Scramjet ? (double)_nudIsolatorLength.Value : 0.5,
                RbccMode:            kind == AirbreathingEngineKind.Rbcc ? RbccModeFromCombo() : RbccOperatingMode.Ramjet,
                EjectorEntrainmentRatio: kind == AirbreathingEngineKind.Rbcc ? (double)_nudRbccEntrainER.Value : 1.0)
            {
                PropellerPowerExtraction_frac = kind == AirbreathingEngineKind.Turboprop
                    ? (double)_nudPropellerFpe.Value
                    : 0.0,
                RecuperatorEffectiveness = kind == AirbreathingEngineKind.GasTurbine
                    ? (double)_nudRecuperatorEff.Value
                    : 0.0,
                ShaftPowerTarget_W = kind == AirbreathingEngineKind.GasTurbine
                    ? (double)_nudShaftPowerKw.Value * 1000.0
                    : 0.0,
                EnableAfterburner = kind == AirbreathingEngineKind.Turbojet && _chkAfterburner.Checked,
                AfterburnerFuelAirRatio = kind == AirbreathingEngineKind.Turbojet && _chkAfterburner.Checked
                    ? (double)_nudAfterburnerFAR.Value
                    : 0.0,
            };
        }

        var buildOpts = new RamjetBuildOptions(
            WallThickness_mm: (double)_nudWallThickness.Value,
            RunLpbfAnalysis:  false);  // task thread ignores buildOpts for non-Ramjet kinds

        _onGenerate(cond, design, buildOpts);
    }

    private static bool KindUsesCompressor(AirbreathingEngineKind kind) => kind switch
    {
        AirbreathingEngineKind.Turbojet     => true,
        AirbreathingEngineKind.Turbofan     => true,
        AirbreathingEngineKind.Turboprop    => true,
        AirbreathingEngineKind.Turboshaft   => true,
        AirbreathingEngineKind.GasTurbine   => true,
        _                                   => false,
    };

    private RbccOperatingMode RbccModeFromCombo() => (_cmbRbccMode.SelectedItem as string) switch
    {
        "DuctedRocket" => RbccOperatingMode.DuctedRocket,
        "Ramjet"       => RbccOperatingMode.Ramjet,
        "Scramjet"     => RbccOperatingMode.Scramjet,
        _              => RbccOperatingMode.Ramjet,
    };

    // ── Called from task thread via BeginInvoke ───────────────────────────

    internal void SetStatus(string msg)
    {
        if (IsDisposed) return;
        _lblStatus.Text = msg;
    }

    internal void UpdateResults(AirbreathingResult result, RamjetGeometryResult? geo)
    {
        if (IsDisposed) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();

        // Physics
        var st = result.Stations;
        sb.AppendLine(string.Format(ci, "Thrust:       {0:F1} N", st.ThrustNet_N));
        sb.AppendLine(string.Format(ci, "Isp (fuel):   {0:F0} s", st.SpecificImpulse_s));
        sb.AppendLine(string.Format(ci, "Fuel flow:    {0:F4} kg/s", st.FuelMassFlow_kg_s));
        if (result.ThermalEfficiency > 0)
            sb.AppendLine(string.Format(ci, "η_th:          {0:P1}", result.ThermalEfficiency));
        if (result.SpecificWork_Jkg > 0)
            sb.AppendLine(string.Format(ci, "Specific work: {0:F0} J/kg", result.SpecificWork_Jkg));
        if (result.ShaftPower_W > 0)
            sb.AppendLine(string.Format(ci, "Shaft power:   {0:F1} kW", result.ShaftPower_W / 1000.0));

        var kind = SelectedKind();

        // Pulsejet: show Helmholtz frequency estimate.
        if (kind == AirbreathingEngineKind.Pulsejet)
        {
            double tubeLen = (double)_nudTubeLength.Value;
            double intakeA = (double)_nudPjIntakeArea.Value;
            double combVol = (double)_nudPjCombArea.Value * (double)_nudPjCombLength.Value;
            double f_Hz    = Voxelforge.Airbreathing.Cycles.HelmholtzFrequencyCalculator
                                 .Frequency_Hz(tubeLen, intakeA, combVol, 340.0);
            sb.AppendLine(string.Format(ci,
                "Helmholtz f:  {0:F1} Hz  (note: ~2× under-predicts measured)", f_Hz));
        }

        // Geometry (ramjet + pulsejet voxel preview only — other kinds run physics-only).
        if (geo is not null)
        {
            sb.AppendLine(string.Format(ci, "Length:       {0:F1} mm", geo.BoundingLength_mm));
            sb.AppendLine(string.Format(ci, "OD:           {0:F1} mm", geo.BoundingDiameter_mm));
            sb.AppendLine(string.Format(ci, "Wall:         {0:F2} mm", geo.WallThickness_mm));
            sb.AppendLine(string.Format(ci, "Mass est.:    {0:F1} g",  geo.TotalMass_g));
            sb.AppendLine(string.Format(ci, "ε (nozzle):   {0:F2}",   geo.ExpansionRatio));
        }
        else if (kind != AirbreathingEngineKind.Ramjet)
        {
            sb.AppendLine($"(physics-only — no voxel preview for {kind} yet)");
        }

        // Feasibility
        if (!result.IsFeasible)
        {
            sb.AppendLine();
            sb.AppendLine("⚠ INFEASIBLE:");
            foreach (var v in result.Violations)
                sb.AppendLine($"  • {v.ConstraintId}: {v.Description}");
        }
        else if (result.Advisories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ℹ Advisories:");
            foreach (var a in result.Advisories)
                sb.AppendLine($"  • {a.ConstraintId}: {a.Description}");
        }

        _txtResults.Text = sb.ToString();
        SetStatus(result.IsFeasible
            ? $"Feasible ✓  Thrust {st.ThrustNet_N:F1} N,  Isp {st.SpecificImpulse_s:F0} s"
            : $"Infeasible — {result.Violations.Count} gate(s) failed");
    }
}
