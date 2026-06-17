// AboutDialog.cs — Lightweight modal About dialog for the
// regen-chamber app.
//
// Opened from the Help → About menu item (or the F1 shortcut).
// Shows the product name / version, build date, a one-line
// "what this is" summary, and clickable text pointing at the
// two source docs (README.md + DEMO_SCRIPT.md) that live next
// to the executable. (HANDOFF.md was retired 2026-04-22; see
// ADR/README.md "Removed ADRs" for the migration story.)
//
// Deliberately hand-drawn WinForms (no XAML, no resource file)
// so it matches the rest of RegenChamberForm's style.
//
// A "Keyboard shortcuts" section sourced from
// ShortcutRouter.FormatShortcutsList() documents the bindings. The
// dialog's ClientSize absorbs the block; all Y-offsets below the
// shortcuts block shift down in lockstep.
//
// A "Copy diagnostic info" button next to OK copies
// `AboutInfo.FormatDiagnosticInfo()` (product + version + tests + OS
// + .NET framework + processor count + keyboard shortcuts) to the
// clipboard in one click — bug-report UX so a user doesn't have to
// dig through Settings for the environment snapshot.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Voxelforge.UI;

internal static class AboutDialog
{
    /// <summary>Show the About dialog modally.</summary>
    public static void Show(IWin32Window? owner)
    {
        using var dlg = Build();
        dlg.ShowDialog(owner);
    }

    private static Form Build()
    {
        var dlg = new Form
        {
            Text               = $"About {AboutInfo.ProductName}",
            FormBorderStyle    = FormBorderStyle.FixedDialog,
            StartPosition      = FormStartPosition.CenterParent,
            MinimizeBox        = false,
            MaximizeBox        = false,
            ClientSize         = new Size(480, 402),
            Font               = new Font("Segoe UI", 9.5f),
        };

        var title = new Label
        {
            Text = $"{AboutInfo.ProductName} {AboutInfo.Version}",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Location = new Point(16, 14),
            AutoSize = true,
        };

        string dateText = AboutInfo.BuildDate == DateTime.MinValue
            ? "unknown"
            : AboutInfo.BuildDate.ToString("yyyy-MM-dd HH:mm");

        var sub = new Label
        {
            Text = $"Assembly version: {AboutInfo.AssemblyVersion}\n"
                 + $"Build date: {dateText}\n"
                 + $"Tests passing: {AboutInfo.TestCount}",
            Location = new Point(18, 48),
            AutoSize = true,
            ForeColor = SystemColors.ControlDarkDark,
        };

        var body = new Label
        {
            Text =
                "Regenerative-cooled rocket thrust-chamber generator:\n"
              + "per-station thermal solver, 38 feasibility gates, SA-based optimiser with\n"
              + "Pareto tracking, 14-stackup feed-system pressure budget, start + chilldown\n"
              + "transient simulators, turbopump sizing, ablative / film-only variants,\n"
              + "voxel-geometry export (STL / 3MF), and CFD-field VTI export — via PicoGK.",
            Location = new Point(18, 112),
            Size = new Size(444, 76),
        };

        var shortcutsHeader = new Label
        {
            Text = "Keyboard shortcuts",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(18, 196),
            AutoSize = true,
        };

        var shortcutsList = new Label
        {
            Text = ShortcutRouter.FormatShortcutsList(),
            Font = new Font("Consolas", 9f),
            Location = new Point(22, 218),
            Size = new Size(440, 124),
            ForeColor = SystemColors.ControlDarkDark,
        };

        var readmeLink = new LinkLabel
        {
            Text = "Open README.md",
            Location = new Point(18, 348),
            AutoSize = true,
        };
        readmeLink.LinkClicked += (_, _) => TryOpenDoc(AboutInfo.ReadmeFile);

        var demoLink = new LinkLabel
        {
            Text = "Open DEMO_SCRIPT.md",
            Location = new Point(170, 348),
            AutoSize = true,
        };
        demoLink.LinkClicked += (_, _) => TryOpenDoc(AboutInfo.DemoFile);

        var copyInfo = new Button
        {
            Text = "Copy diagnostic info",
            Location = new Point(224, 362),
            Size = new Size(152, 28),
        };
        copyInfo.Click += (_, _) => TryCopyDiagnosticInfo(copyInfo);

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(384, 362),
            Size = new Size(80, 28),
        };
        dlg.AcceptButton = ok;
        dlg.CancelButton = ok;

        dlg.Controls.AddRange(new Control[]
        {
            title, sub, body,
            shortcutsHeader, shortcutsList,
            readmeLink, demoLink, copyInfo, ok,
        });
        return dlg;
    }

    private static void TryCopyDiagnosticInfo(Button btn)
    {
        try
        {
            Clipboard.SetText(AboutInfo.FormatDiagnosticInfo());
            string original = btn.Text;
            btn.Text = "Copied \u2713";
            // Restore the original label after a short delay so the user
            // gets a visible confirmation without a MessageBox interruption.
            var timer = new System.Windows.Forms.Timer { Interval = 1200 };
            timer.Tick += (_, _) =>
            {
                btn.Text = original;
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy diagnostic info:\n{ex.Message}",
                            "Clipboard error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void TryOpenDoc(string fileName)
    {
        try
        {
            // Walk up from the assembly directory until we find the doc —
            // lets the link work both from a dev-layout (bin/Debug/net9.0…)
            // and a published-layout where the exe sits next to the docs.
            string? dir = Path.GetDirectoryName(typeof(AboutInfo).Assembly.Location);
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
                    return;
                }
                dir = Path.GetDirectoryName(dir);
            }
            MessageBox.Show($"Could not locate {fileName} near the running executable.",
                            "File not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open {fileName}:\n{ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
