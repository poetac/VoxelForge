// RenderImageDialog.cs — modal dialog for Sprint render's "Render Image…" button.
//
// Sprint render (2026-04-25) — Visual elegance / Noyron-parity track.
//
// Lets the user pick material / mode / resolution / frames before launching
// the voxelforge-render subprocess. Returned values are read off the
// public properties after ShowDialog returns DialogResult.OK.

using System;
using System.Windows.Forms;

namespace Voxelforge.UI;

internal sealed class RenderImageDialog : Form
{
    public string Material      { get; private set; } = "copper";
    public string Mode          { get; private set; } = "still";
    public string Resolution    { get; private set; } = "high";
    public int    Frames        { get; private set; } = 16;
    public string OutputPath    { get; private set; } = "";

    public RenderImageDialog(string defaultOutputPath)
    {
        Text             = "Render image";
        StartPosition    = FormStartPosition.CenterParent;
        FormBorderStyle  = FormBorderStyle.FixedDialog;
        MinimizeBox      = false;
        MaximizeBox      = false;
        AutoSize         = true;
        AutoSizeMode     = AutoSizeMode.GrowAndShrink;
        Padding          = new Padding(12);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize    = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock         = DockStyle.Fill,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Output path with browse button.
        var lblOut = new Label { Text = "Output file:", AutoSize = true, Anchor = AnchorStyles.Left };
        var pnlOut = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var txtOut = new TextBox { Width = 300, Text = defaultOutputPath };
        var btnBrowse = new Button { Text = "Browse…", AutoSize = true };
        btnBrowse.Click += (_, _) =>
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "PNG image|*.png",
                FileName = System.IO.Path.GetFileName(txtOut.Text),
                InitialDirectory = System.IO.Path.GetDirectoryName(txtOut.Text) ?? "",
            };
            if (sfd.ShowDialog(this) == DialogResult.OK) txtOut.Text = sfd.FileName;
        };
        pnlOut.Controls.AddRange(new Control[] { txtOut, btnBrowse });
        layout.Controls.Add(lblOut, 0, 0);
        layout.Controls.Add(pnlOut, 1, 0);

        // Material.
        var cboMaterial = MakeDropdown(new[] { "copper", "inconel", "titanium" }, "copper");
        layout.Controls.Add(new Label { Text = "Material:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(cboMaterial, 1, 1);

        // Mode.
        var cboMode = MakeDropdown(new[] { "still", "turntable" }, "still");
        layout.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(cboMode, 1, 2);

        // Resolution.
        var cboRes = MakeDropdown(new[] { "low", "high", "maximum" }, "high");
        layout.Controls.Add(new Label { Text = "Resolution:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(cboRes, 1, 3);

        // Frames (turntable only — disabled when mode != turntable).
        var nudFrames = new NumericUpDown { Minimum = 4, Maximum = 60, Value = 16, Width = 80 };
        nudFrames.Enabled = false;
        cboMode.SelectedIndexChanged += (_, _) =>
            nudFrames.Enabled = (string)cboMode.SelectedItem! == "turntable";
        layout.Controls.Add(new Label { Text = "Frames (turntable):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(nudFrames, 1, 4);

        // Render-time hint.
        var lblHint = new Label
        {
            Text = "Hint: low ≈ 5 s/frame (Eevee). high ≈ 30 s/frame (Cycles). maximum ≈ 3-5 min/frame (Cycles 4K).",
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
            MaximumSize = new System.Drawing.Size(450, 0),
        };
        layout.Controls.Add(lblHint, 0, 5);
        layout.SetColumnSpan(lblHint, 2);

        // OK / Cancel.
        var pnlButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };
        var btnRender = new Button { Text = "Render", DialogResult = DialogResult.OK, AutoSize = true };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        pnlButtons.Controls.AddRange(new Control[] { btnRender, btnCancel });
        layout.Controls.Add(pnlButtons, 0, 6);
        layout.SetColumnSpan(pnlButtons, 2);

        AcceptButton = btnRender;
        CancelButton = btnCancel;
        Controls.Add(layout);

        // Capture values on accept.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(txtOut.Text))
            {
                MessageBox.Show(this, "Output path is required.", "Render image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            OutputPath = txtOut.Text;
            Material   = (string)cboMaterial.SelectedItem!;
            Mode       = (string)cboMode.SelectedItem!;
            Resolution = (string)cboRes.SelectedItem!;
            Frames     = (int)nudFrames.Value;
        };
    }

    private static ComboBox MakeDropdown(string[] items, string defaultItem)
    {
        var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        cbo.Items.AddRange(items);
        cbo.SelectedItem = defaultItem;
        return cbo;
    }
}
