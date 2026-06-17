// KioskForm.cs — interactive trade-show kiosk UI.
//
// UX flow:
//   1. Visitor lands at idle screen with one big button: "Try a design".
//   2. Press → kiosk picks a preset (rotated by sequence number),
//      builds a perturbed variant, displays it in the PicoGK GLFW
//      viewer alongside this form, enables two buttons:
//        • "🔄 Try Another" — fresh perturbation, viewer updates
//        • "✓ Save This One" — commit STL to watch folder, fire
//          production render asynchronously
//   3. After commit, visitor sees confirmation and the cycle resets
//      to step 1 for the next visitor.
//
// Operator shortcuts (Ctrl+Shift+key):
//   D — toggle hidden debug panel (preset combo + last error)
//   R — reset session counter
//   Q — quit kiosk

using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Voxelforge.Kiosk;

internal sealed class KioskForm : Form
{
    private readonly KioskSettings _settings;
    private readonly Button _btnPrimary;       // Try a design / Try Another
    private readonly Button _btnSave;          // Save This One (hidden until preview ready)
    private readonly Label  _lblStatus;
    private readonly Label  _lblCounter;
    private readonly Label  _lblTitle;
    private readonly Panel  _debugPanel;
    private readonly ComboBox _cboPreset;
    private readonly Label  _lblDebug;
    private readonly System.Windows.Forms.Timer _buildTickTimer;

    private int  _printsThisSession = 0;
    private bool _inFlight = false;
    private bool _hasPreview = false;
    private DateTime _buildStartedUtc;
    private string   _inFlightPreset = "";

    private readonly Action<KioskPreviewReady>  _onPreviewHandler;
    private readonly Action<KioskCommitResult>  _onCommitHandler;
    private readonly Action<string>             _onRenderHandler;
    private readonly Action<Exception, string>  _onErrorHandler;

    public KioskForm(KioskSettings settings)
    {
        _settings = settings;

        Text = "Voxelforge Kiosk";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(20, 24, 32);
        ForeColor = Color.White;
        KeyPreview = true;

        _lblTitle = new Label
        {
            Text = "VOXELFORGE",
            Font = new Font("Segoe UI", 28f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 230, 240),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(900, 60),
        };

        _btnPrimary = new Button
        {
            Text = "Try a Design",
            Font = new Font("Segoe UI", 32f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(60, 160, 90),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(440, 200),
            TabStop = false,
            Cursor = Cursors.Hand,
        };
        _btnPrimary.FlatAppearance.BorderSize = 0;
        _btnPrimary.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 180, 110);
        _btnPrimary.Click += OnPrimaryClicked;

        _btnSave = new Button
        {
            Text = "✓ Save This One",
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(72, 130, 200),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(440, 140),
            TabStop = false,
            Cursor = Cursors.Hand,
            Visible = false,
        };
        _btnSave.FlatAppearance.BorderSize = 0;
        _btnSave.FlatAppearance.MouseOverBackColor = Color.FromArgb(92, 150, 220);
        _btnSave.Click += OnSaveClicked;

        _lblStatus = new Label
        {
            Text = "Press to generate a unique rocket-engine design.",
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            ForeColor = Color.FromArgb(180, 200, 220),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(900, 60),
        };

        _lblCounter = new Label
        {
            Text = "Saved today: 0",
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            ForeColor = Color.FromArgb(140, 160, 180),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(280, 40),
        };

        _debugPanel = new Panel
        {
            BackColor = Color.FromArgb(40, 44, 52),
            Size = new Size(380, 240),
            Visible = false,
        };
        _cboPreset = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11f),
            Location = new Point(12, 36),
            Size = new Size(200, 28),
        };
        _cboPreset.Items.Add("(rotate)");
        foreach (var name in KioskPipeline.FdmCanonicals) _cboPreset.Items.Add(name);
        _cboPreset.SelectedIndex = 0;
        var lblDebugTitle = new Label
        {
            Text = "Debug — Ctrl+Shift+R reset, +Q quit",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(12, 8),
            AutoSize = true,
        };
        _lblDebug = new Label
        {
            Font = new Font("Consolas", 9f),
            ForeColor = Color.FromArgb(180, 200, 200),
            Location = new Point(12, 70),
            Size = new Size(356, 160),
            AutoSize = false,
            Text = $"Output:\n  {_settings.WatchFolder}\nNextSeq: {_settings.NextSequence}",
        };
        _debugPanel.Controls.Add(lblDebugTitle);
        _debugPanel.Controls.Add(_cboPreset);
        _debugPanel.Controls.Add(_lblDebug);

        Controls.Add(_lblTitle);
        Controls.Add(_btnPrimary);
        Controls.Add(_btnSave);
        Controls.Add(_lblStatus);
        Controls.Add(_lblCounter);
        Controls.Add(_debugPanel);

        _buildTickTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _buildTickTimer.Tick += OnBuildTick;

        Resize += (_, _) => LayoutChildren();
        Shown  += (_, _) => LayoutChildren();
        KeyDown += OnKeyDown;
        FormClosed += OnFormClosed;

        _onPreviewHandler = MarshalToUi<KioskPreviewReady>(HandlePreviewReady);
        _onCommitHandler  = MarshalToUi<KioskCommitResult>(HandleCommitReady);
        _onRenderHandler  = MarshalToUi<string>(HandleRenderReady);
        _onErrorHandler   = MarshalToUi<Exception, string>(HandleError);

        KioskShared.OnPreviewReady += _onPreviewHandler;
        KioskShared.OnCommitReady  += _onCommitHandler;
        KioskShared.OnRenderReady  += _onRenderHandler;
        KioskShared.OnError        += _onErrorHandler;
    }

    private Action<T> MarshalToUi<T>(Action<T> handler) => arg =>
    {
        if (IsDisposed) return;
        try { BeginInvoke((Action)(() => handler(arg))); }
        catch (ObjectDisposedException)   { /* race */ }
        catch (InvalidOperationException) { /* form closing */ }
    };

    private Action<T1, T2> MarshalToUi<T1, T2>(Action<T1, T2> handler) => (a, b) =>
    {
        if (IsDisposed) return;
        try { BeginInvoke((Action)(() => handler(a, b))); }
        catch (ObjectDisposedException)   { /* race */ }
        catch (InvalidOperationException) { /* form closing */ }
    };

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        KioskShared.OnPreviewReady -= _onPreviewHandler;
        KioskShared.OnCommitReady  -= _onCommitHandler;
        KioskShared.OnRenderReady  -= _onRenderHandler;
        KioskShared.OnError        -= _onErrorHandler;
        _buildTickTimer.Stop();
        _buildTickTimer.Dispose();
    }

    private void LayoutChildren()
    {
        int cx = ClientSize.Width  / 2;
        int cy = ClientSize.Height / 2;

        _lblTitle.Size     = new Size(Math.Min(900, ClientSize.Width - 40), 60);
        _lblTitle.Location = new Point(cx - _lblTitle.Width / 2, 40);

        // Two-button layout: stack primary + save vertically when both
        // are visible; centre primary alone otherwise.
        if (_btnSave.Visible)
        {
            _btnPrimary.Location = new Point(cx - _btnPrimary.Width / 2, cy - _btnPrimary.Height - 20);
            _btnSave.Location    = new Point(cx - _btnSave.Width    / 2, cy + 20);
        }
        else
        {
            _btnPrimary.Location = new Point(cx - _btnPrimary.Width / 2, cy - _btnPrimary.Height / 2);
        }

        _lblStatus.Size     = new Size(Math.Min(900, ClientSize.Width - 80), 60);
        _lblStatus.Location = new Point(
            cx - _lblStatus.Width / 2,
            (_btnSave.Visible ? _btnSave.Bottom : _btnPrimary.Bottom) + 30);

        _lblCounter.Location = new Point(ClientSize.Width - _lblCounter.Width - 24, 24);

        _debugPanel.Location = new Point(
            ClientSize.Width  - _debugPanel.Width  - 24,
            ClientSize.Height - _debugPanel.Height - 24);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc / Alt+F4 / etc. ignored — visitor lockout.
        if (!e.Control || !e.Shift) return;
        switch (e.KeyCode)
        {
            case Keys.D:
                _debugPanel.Visible = !_debugPanel.Visible;
                e.Handled = true;
                break;
            case Keys.R:
                _printsThisSession = 0;
                _lblCounter.Text = "Saved today: 0";
                _lblStatus.Text = "Counter reset.";
                e.Handled = true;
                break;
            case Keys.Q:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void OnPrimaryClicked(object? sender, EventArgs e)
    {
        if (_inFlight) return;
        _inFlight = true;
        SetButtonsDisabled();

        string preset;
        if (_cboPreset.SelectedIndex > 0)
        {
            preset = (string)_cboPreset.Items[_cboPreset.SelectedIndex]!;
        }
        else
        {
            int seqForRotation = _settings.NextSequence;
            preset = KioskPipeline.FdmCanonicals[
                Math.Abs(seqForRotation) % KioskPipeline.FdmCanonicals.Length];
        }

        int seq = _settings.NextSequence;
        _inFlightPreset = preset;
        _buildStartedUtc = DateTime.UtcNow;
        _lblStatus.Text = $"Generating {preset} #{seq:D4}…";
        _btnPrimary.Text = "Generating…";
        _buildTickTimer.Start();

        KioskShared.Enqueue(new KioskTryAnotherRequest(preset, seq));
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_inFlight || !_hasPreview) return;
        _inFlight = true;
        SetButtonsDisabled();
        _btnSave.Text = "Saving…";
        _lblStatus.Text = "Writing STL + firing render…";
        _buildTickTimer.Start();
        _buildStartedUtc = DateTime.UtcNow;

        KioskShared.Enqueue(new KioskCommitRequest(_settings.WatchFolder));
    }

    private void OnBuildTick(object? sender, EventArgs e)
    {
        if (!_inFlight) return;
        double elapsed = (DateTime.UtcNow - _buildStartedUtc).TotalSeconds;
        int dots = ((int)(elapsed * 2.0) % 4) + 1;
        string ellipsis = new string('.', dots);
        if (!_hasPreview)
        {
            _btnPrimary.Text = $"Generating{ellipsis}";
            _lblStatus.Text  = $"Generating {_inFlightPreset}{ellipsis}  ({elapsed:F0}s)";
        }
        else
        {
            _btnSave.Text   = $"Saving{ellipsis}";
            _lblStatus.Text = $"Writing STL{ellipsis}  ({elapsed:F0}s)";
        }
    }

    private void HandlePreviewReady(KioskPreviewReady r)
    {
        _buildTickTimer.Stop();
        _hasPreview = true;
        _inFlight = false;

        _btnPrimary.Text = "🔄 Try Another";
        _btnPrimary.BackColor = Color.FromArgb(60, 160, 90);
        _btnPrimary.Enabled = true;

        _btnSave.Visible = true;
        _btnSave.Enabled = true;
        _btnSave.Text    = "✓ Save This One";

        _lblStatus.Text = $"{r.PresetName} #{r.SequenceNumber:D4}  •  " +
                          $"L={r.BoundingLength_mm:F0}mm  OD={r.BoundingDiameter_mm:F0}mm  •  " +
                          "see preview window";
        _lblDebug.Text  =
            $"Output:\n  {_settings.WatchFolder}\nNextSeq: {_settings.NextSequence}\n" +
            $"Preview: {r.PresetName} #{r.SequenceNumber:D4}\n{r.Description}";
        LayoutChildren();
    }

    private void HandleCommitReady(KioskCommitResult r)
    {
        _buildTickTimer.Stop();
        _printsThisSession++;
        _settings.NextSequence = r.SequenceNumber + 1;
        _hasPreview = false;
        _inFlight = false;

        _lblCounter.Text = $"Saved today: {_printsThisSession}";
        _btnPrimary.Text = "Try a Design";
        _btnPrimary.BackColor = Color.FromArgb(60, 160, 90);
        _btnPrimary.Enabled = true;
        _btnSave.Visible = false;

        string renderHint = r.RenderPending
            ? "  (render in progress…)"
            : "  (Blender absent — STL only)";
        _lblStatus.Text =
            $"✓ Saved {Path.GetFileName(r.StlPath)}  ({r.TriangleCount:N0} tri, " +
            $"{r.StlBytes / 1024:N0} KB){renderHint}";
        LayoutChildren();
    }

    private void HandleRenderReady(string pngPath)
    {
        _lblStatus.Text = $"✓ Saved STL + render  →  {Path.GetFileName(pngPath)}";
        _lblDebug.Text  = _lblDebug.Text + $"\nRender: {pngPath}";
    }

    private void HandleError(Exception ex, string ctx)
    {
        _buildTickTimer.Stop();
        _inFlight = false;

        _lblStatus.Text = $"Build failed for {ctx}. Press to try again.";
        _lblDebug.Text  = $"ERROR ({ctx}):\n{ex.Message}\n\n{ex.StackTrace}";

        _btnPrimary.Text = _hasPreview ? "🔄 Try Another" : "Try a Design";
        _btnPrimary.BackColor = Color.FromArgb(60, 160, 90);
        _btnPrimary.Enabled = true;
        if (_hasPreview)
        {
            _btnSave.Enabled = true;
            _btnSave.Text = "✓ Save This One";
        }
        LayoutChildren();
    }

    private void SetButtonsDisabled()
    {
        _btnPrimary.Enabled = false;
        _btnPrimary.BackColor = Color.FromArgb(80, 100, 130);
        _btnSave.Enabled = false;
        _btnSave.BackColor = Color.FromArgb(80, 100, 130);
    }
}
