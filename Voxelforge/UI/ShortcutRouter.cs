// ShortcutRouter.cs — Form-level keyboard-shortcut resolver.
//
// A pure Keys → Action mapper so the unit tests can cover the
// bindings without spinning up a live Form. The form owns a
// single `KeyDown` handler (KeyPreview = true) that dispatches
// each Action to the matching named method.
//
// Bindings (standard Windows conventions):
//   F5                  → Generate
//   Ctrl+G              → Generate
//   Ctrl+S              → Save Design
//   Ctrl+O              → Load Design
//   Ctrl+E              → Export STL
//   Esc                 → Stop Optimization
//   F1                  → About
//
// `Bindings` + `FormatShortcutsList` surface the mapping as
// user-visible text so the About dialog can render it without
// re-encoding the key → action → label triplets. Tests in
// Phase7FollowOnTests.cs cover the round-trip between `Bindings`
// and `Resolve`.

using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Voxelforge.UI;

public static class ShortcutRouter
{
    public enum Action
    {
        None,
        Generate,
        SaveDesign,
        LoadDesign,
        ExportStl,
        StopOpt,
        About,
    }

    public static Action Resolve(Keys keys) => keys switch
    {
        Keys.F5                 => Action.Generate,
        Keys.Control | Keys.G   => Action.Generate,
        Keys.Control | Keys.S   => Action.SaveDesign,
        Keys.Control | Keys.O   => Action.LoadDesign,
        Keys.Control | Keys.E   => Action.ExportStl,
        Keys.Escape             => Action.StopOpt,
        Keys.F1                 => Action.About,
        _                       => Action.None,
    };

    /// <summary>
    /// User-visible label for a bound action, used by the About dialog's
    /// keyboard-shortcuts section.
    /// </summary>
    public readonly record struct Binding(string KeyLabel, Action Action, string ActionLabel);

    /// <summary>
    /// Documented shortcut bindings in presentation order. The list is the
    /// single source of truth for both <see cref="Resolve"/> regression
    /// tests and the About dialog's shortcuts section — every entry here
    /// must map back to the matching <see cref="Action"/> via
    /// <see cref="Resolve"/>.
    /// </summary>
    public static IReadOnlyList<Binding> Bindings { get; } = new[]
    {
        new Binding("F5",      Action.Generate,   "Generate"),
        new Binding("Ctrl+G",  Action.Generate,   "Generate"),
        new Binding("Ctrl+S",  Action.SaveDesign, "Save Design"),
        new Binding("Ctrl+O",  Action.LoadDesign, "Load Design"),
        new Binding("Ctrl+E",  Action.ExportStl,  "Export STL"),
        new Binding("Esc",     Action.StopOpt,    "Stop Optimization"),
        new Binding("F1",      Action.About,      "About"),
    };

    /// <summary>
    /// Multi-line "KeyLabel — ActionLabel" listing for the About dialog.
    /// Columns are padded so the action labels line up in a fixed-width
    /// font-neutral way. The key column width is computed from the
    /// longest <see cref="Binding.KeyLabel"/> so adding a longer chord
    /// (e.g. "Ctrl+Shift+X") in <see cref="Bindings"/> stays aligned
    /// without manually re-tuning a constant.
    /// </summary>
    public static string FormatShortcutsList()
    {
        int keyColumn = 2;
        for (int i = 0; i < Bindings.Count; i++)
        {
            int len = Bindings[i].KeyLabel.Length;
            if (len + 2 > keyColumn) keyColumn = len + 2;
        }
        var sb = new StringBuilder();
        for (int i = 0; i < Bindings.Count; i++)
        {
            var b = Bindings[i];
            sb.Append(b.KeyLabel.PadRight(keyColumn));
            sb.Append("— ");
            sb.Append(b.ActionLabel);
            if (i < Bindings.Count - 1) sb.Append('\n');
        }
        return sb.ToString();
    }
}
