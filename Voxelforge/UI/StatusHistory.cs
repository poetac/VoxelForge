// StatusHistory.cs — Rolling buffer + expandable panel for the
// status bar. Catches the
// "wait, what happened 30 s ago" failure mode where transient messages
// flash past and get overwritten before the user reads them.
//
// Design:
//   • `StatusHistoryBuffer` is pure-data — a thread-safe ring of
//     (UTC timestamp, severity, message) records. Owned by the form,
//     fed from every SetStatus(...) call.
//   • `StatusHistoryPanel` is the matching UserControl: a collapsed-
//     by-default expander below `lblStatus`, revealed by clicking
//     the "▾" toggle. Lists the last 20 entries most-recent-first,
//     colour-coded by severity, each prefixed with relative age
//     ("5 s ago", "1 m 20 s ago", "just now").
//
// Severity heuristic: messages starting with "Error" / "Warning" /
// "Failed" / "⚠" get Error; trailing-ellipsis progress messages get
// Progress; everything else is Info. The heuristic lives in the
// buffer's `Classify` helper so unit tests can exercise it directly.

using System.Drawing;
using System.Windows.Forms;

namespace Voxelforge.UI;

public enum StatusSeverity
{
    Info = 0,
    Progress = 1,
    Error = 2,
}

public sealed record StatusEntry(DateTime UtcTimestamp, StatusSeverity Severity, string Message);

/// <summary>
/// Thread-safe rolling buffer of status messages. New entries evict
/// the oldest once <see cref="Capacity"/> is reached. All public
/// methods lock on a single internal mutex — the buffer is written
/// from the UI thread (via SetStatus on form) and read from both
/// the UI thread (panel paint) and tests (snapshot).
/// </summary>
public sealed class StatusHistoryBuffer
{
    private readonly object _sync = new();
    private readonly Queue<StatusEntry> _entries;

    public int Capacity { get; }

    public StatusHistoryBuffer(int capacity = 20)
    {
        if (capacity < 1) capacity = 1;
        Capacity = capacity;
        _entries = new Queue<StatusEntry>(capacity);
    }

    public void Add(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var entry = new StatusEntry(DateTime.UtcNow, Classify(message), message);
        lock (_sync)
        {
            if (_entries.Count >= Capacity) _entries.Dequeue();
            _entries.Enqueue(entry);
        }
        OnChanged?.Invoke();
    }

    /// <summary>Snapshot most-recent-first. Returned array is a copy safe to enumerate across threads.</summary>
    public StatusEntry[] Snapshot()
    {
        lock (_sync)
        {
            var arr = _entries.ToArray();
            Array.Reverse(arr);
            return arr;
        }
    }

    public int Count
    {
        get { lock (_sync) { return _entries.Count; } }
    }

    public void Clear()
    {
        lock (_sync) { _entries.Clear(); }
        OnChanged?.Invoke();
    }

    public event Action? OnChanged;

    /// <summary>
    /// Heuristic severity classifier. Exposed as internal so
    /// Phase8UiInfraTests can assert on edge cases (empty, error
    /// prefixes, progress ellipsis) directly without spinning up
    /// the WinForms panel.
    /// </summary>
    internal static StatusSeverity Classify(string message)
    {
        if (string.IsNullOrEmpty(message)) return StatusSeverity.Info;
        string m = message.TrimStart();
        // Error patterns — check before progress because an "Error …"
        // message with a trailing ellipsis should still read as Error.
        if (m.StartsWith("Error",   System.StringComparison.OrdinalIgnoreCase)
         || m.StartsWith("Warning", System.StringComparison.OrdinalIgnoreCase)
         || m.StartsWith("Failed",  System.StringComparison.OrdinalIgnoreCase)
         || m.StartsWith('\u26a0')
         || m.Contains("error:",    System.StringComparison.OrdinalIgnoreCase)
         || m.Contains("failed",    System.StringComparison.OrdinalIgnoreCase))
            return StatusSeverity.Error;
        string trimmed = message.TrimEnd();
        if (trimmed.EndsWith('\u2026')
         || trimmed.EndsWith("...",    System.StringComparison.Ordinal))
            return StatusSeverity.Progress;
        return StatusSeverity.Info;
    }

    /// <summary>
    /// Render a single entry's age relative to `now` as "just now" /
    /// "Xs ago" / "Xm Ys ago". Exposed internal for tests. Clamps
    /// negative / zero ages (shouldn't happen in practice, but belt-
    /// and-suspenders against clock drift on multi-machine sessions).
    /// </summary>
    internal static string FormatRelativeAge(DateTime timestampUtc, DateTime nowUtc)
    {
        var delta = nowUtc - timestampUtc;
        if (delta.TotalSeconds < 2) return "just now";
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalMinutes < 60)
        {
            int m = (int)delta.TotalMinutes;
            int s = (int)(delta.TotalSeconds - m * 60);
            return s > 0 ? $"{m}m {s}s ago" : $"{m}m ago";
        }
        int h = (int)delta.TotalHours;
        return $"{h}h ago";
    }
}

/// <summary>
/// Collapsed-by-default expander listing the most recent status
/// messages. Toggleable by the "▾" label click. Auto-collapses after
/// `AutoCollapseSeconds` of no new messages when the user hasn't
/// manually pinned it open.
/// </summary>
public sealed class StatusHistoryPanel : UserControl
{
    private readonly StatusHistoryBuffer _buffer;
    private readonly Label _toggle;
    private readonly ListBox _list;
    private readonly System.Windows.Forms.Timer _autoCollapseTimer;
    private bool _userPinned;
    private DateTime _lastEntryAt = DateTime.MinValue;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int AutoCollapseSeconds { get; set; } = 5;

    public StatusHistoryPanel(StatusHistoryBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

        Height = 24;           // collapsed height = just the toggle row
        BackColor = SystemColors.Control;

        _toggle = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "\u25b8 Status history",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.25f),
            Padding = new Padding(6, 0, 0, 0),
        };
        _toggle.Click += (_, _) => TogglePinned();

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = new Font("Consolas", 8.25f),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 16,
            Visible = false,
        };
        _list.DrawItem += OnDrawItem;

        Controls.Add(_list);
        Controls.Add(_toggle);

        _buffer.OnChanged += OnBufferChanged;

        _autoCollapseTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _autoCollapseTimer.Tick += (_, _) => TickAutoCollapse();
        _autoCollapseTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.OnChanged -= OnBufferChanged;
            _autoCollapseTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnBufferChanged()
    {
        _lastEntryAt = DateTime.UtcNow;
        if (InvokeRequired) { BeginInvoke(new Action(RebuildListSafely)); return; }
        RebuildListSafely();
    }

    private void RebuildListSafely()
    {
        if (IsDisposed) return;
        var snap = _buffer.Snapshot();
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            var now = DateTime.UtcNow;
            foreach (var e in snap)
            {
                string age = StatusHistoryBuffer.FormatRelativeAge(e.UtcTimestamp, now);
                _list.Items.Add(new StatusListItem(e, age));
            }
        }
        finally { _list.EndUpdate(); }

        // Auto-expand on new entry (unless user has collapsed it manually
        // and hasn't repinned — the user-pinned flag sticks through one
        // round trip so a Collapse action stays sticky even if a new
        // message arrives immediately after).
        if (!_userPinned && _list.Items.Count > 0) SetExpanded(true, userAction: false);
    }

    private void TogglePinned()
    {
        _userPinned = !(_list.Visible);    // about to flip visibility — user "pins" to that state
        SetExpanded(!_list.Visible, userAction: true);
    }

    private void SetExpanded(bool expanded, bool userAction)
    {
        if (_list.Visible == expanded) return;
        _list.Visible = expanded;
        Height = expanded ? 220 : 24;
        _toggle.Text = (expanded ? "\u25be" : "\u25b8") + " Status history"
                     + (expanded ? $"  ({_buffer.Count})" : "");
        if (userAction) _userPinned = expanded;
    }

    private void TickAutoCollapse()
    {
        if (!_list.Visible || _userPinned || _lastEntryAt == DateTime.MinValue) return;
        var idle = (DateTime.UtcNow - _lastEntryAt).TotalSeconds;
        if (idle >= AutoCollapseSeconds) SetExpanded(false, userAction: false);
    }

    private static void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ListBox lb) return;
        if (lb.Items[e.Index] is not StatusListItem item) return;
        e.DrawBackground();

        Color fg = item.Entry.Severity switch
        {
            StatusSeverity.Error    => Color.Firebrick,
            StatusSeverity.Progress => Color.DarkBlue,
            _                       => Color.Black,
        };

        using var ageBrush = new SolidBrush(Color.DimGray);
        using var msgBrush = new SolidBrush(fg);
        string age = item.AgeLabel;
        e.Graphics.DrawString(age, e.Font ?? lb.Font, ageBrush,
                              e.Bounds.Left + 4, e.Bounds.Top + 1);
        const int ageColWidth = 72;
        e.Graphics.DrawString(item.Entry.Message, e.Font ?? lb.Font, msgBrush,
                              e.Bounds.Left + ageColWidth, e.Bounds.Top + 1);
        e.DrawFocusRectangle();
    }

    private sealed record StatusListItem(StatusEntry Entry, string AgeLabel);
}
