// ControlVisibilityRegistry.cs — UI overhaul Sprint 1 Step 2 (2026-04-28).
//
// App-side wiring layer that binds the Core-side UiVisibilityRules to
// concrete WinForms Control instances. The form constructor calls
// Register(fieldKey, rowPanel, group) for each control as it builds
// the layout; later, RecomputeAll(state) walks the registry and sets
// .Visible on each rowPanel + group.
//
// Key invariants:
//
//   1. We register the OUTER row Panel (label + input together), not
//      the input alone. Hiding only the input would orphan the label.
//
//   2. When EVERY registered child of a group is hidden, the group
//      container itself is hidden too. Cuts visual clutter when a
//      whole subsystem (preburner, aerospike, film cooling) goes
//      away.
//
//   3. SuspendLayout / ResumeLayout brackets the recompute so the
//      layout engine doesn't repaint per-control. Recompute is < 1 ms
//      (~60 dict lookups + writes); fires only on combo / checkbox
//      change, not on numeric ValueChanged.
//
//   4. Hidden controls keep their .Value — visibility ≠ reset. The
//      App's ReadDesign() continues reading the underlying value;
//      reverting the discriminator brings the row back with its
//      previous value.

using System.Collections.Generic;
using System.Windows.Forms;
using Voxelforge.Optimization;

namespace Voxelforge.UI;

/// <summary>
/// Maps <see cref="FieldKeys"/> string identifiers to live WinForms
/// row panels (and their parent group container, if any). The form
/// constructor populates the registry as controls are built; layout
/// updates happen via <see cref="RecomputeAll"/>.
/// </summary>
internal sealed class ControlVisibilityRegistry
{
    /// <summary>One registry entry per field — the row panel + an
    /// optional parent group whose visibility cascades down when all
    /// children are hidden.</summary>
    private readonly struct Entry
    {
        public Entry(Control rowPanel, Control? group)
        {
            RowPanel = rowPanel;
            Group    = group;
        }

        public Control RowPanel { get; }
        public Control? Group { get; }
    }

    private readonly Dictionary<string, Entry> _entries =
        new(System.StringComparer.Ordinal);

    // Reverse index group → rows; lets RecomputeAll cascade
    // group-level visibility (hide group when every row is hidden).
    private readonly Dictionary<Control, List<string>> _byGroup = new();

    /// <summary>
    /// Total number of registered fields. Useful as a smoke-check
    /// from the form constructor (e.g., "we expected ~50 fields,
    /// got <c>RegisteredCount</c>").
    /// </summary>
    public int RegisteredCount => _entries.Count;

    /// <summary>
    /// Bind a field key to a row panel. Optionally pass the parent
    /// group container so the registry can cascade group-level
    /// visibility (hide the whole group when every child is hidden).
    /// </summary>
    public void Register(string fieldKey, Control rowPanel, Control? group = null)
    {
        if (string.IsNullOrEmpty(fieldKey))
            throw new System.ArgumentException(
                "Field key must be non-empty", nameof(fieldKey));
        if (rowPanel is null)
            throw new System.ArgumentNullException(nameof(rowPanel));

        // Idempotent: re-registering the same key updates the entry
        // rather than throwing. Callers that legitimately need to
        // re-bind (e.g., during a re-layout pass) get clean semantics.
        _entries[fieldKey] = new Entry(rowPanel, group);

        if (group is not null)
        {
            if (!_byGroup.TryGetValue(group, out var keys))
            {
                keys = new List<string>();
                _byGroup[group] = keys;
            }
            // Avoid duplicate entries on idempotent re-register.
            if (!keys.Contains(fieldKey))
                keys.Add(fieldKey);
        }
    }

    /// <summary>
    /// Recompute visibility for every registered field given the
    /// current discriminator state. Caller is expected to bracket
    /// this with SuspendLayout / ResumeLayout on the parent panel
    /// to prevent layout-engine churn.
    /// </summary>
    public void RecomputeAll(UiVisibilityState state)
    {
        if (state is null)
            throw new System.ArgumentNullException(nameof(state));

        // Pass 1: per-row visibility.
        foreach (var (key, entry) in _entries)
        {
            bool show = UiVisibilityRules.ShouldShow(key, state);
            if (entry.RowPanel.Visible != show)
                entry.RowPanel.Visible = show;
        }

        // Pass 2: group-level cascade — hide the group container if
        // and only if every registered child of the group is now
        // hidden. Empty groups (no entries registered) stay visible
        // so manually-built decorative groups aren't accidentally
        // hidden.
        foreach (var (group, keys) in _byGroup)
        {
            if (keys.Count == 0)
                continue;

            bool anyVisible = false;
            foreach (var key in keys)
            {
                if (_entries.TryGetValue(key, out var e) && e.RowPanel.Visible)
                {
                    anyVisible = true;
                    break;
                }
            }

            if (group.Visible != anyVisible)
                group.Visible = anyVisible;
        }
    }
}
