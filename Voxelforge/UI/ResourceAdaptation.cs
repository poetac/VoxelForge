// ResourceAdaptation.cs — Adaptive resource scaling based on the
// machine's live state.
//
// Battery / AC awareness: `SystemInformation.PowerStatus` tells us
// whether the laptop is running on battery. When the user has
// BatteryAwareQuiet enabled, we flip to Quiet mode the moment the
// cord comes out and restore the prior mode when it goes back in.
//
// Foreground-window throttle: when the form loses focus
// (user switches to another app), scale the resource budget down
// to the Quiet preset so the foreground app isn't competing with
// a background solve. Restore on regain.
//
// Both hooks use the existing `ResourceBudgetSettings.ApplySettings`
// machinery — we mutate a shadow copy of `SessionSettings`
// (so user's saved preset is preserved) and re-apply. When the
// trigger condition clears, we re-apply the user's original
// SessionSettings to restore.

using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace Voxelforge.UI;

public sealed class ResourceAdaptation : IDisposable
{
    private readonly SessionSettings _userSettings;
    private readonly Form _hostForm;
    private readonly System.Windows.Forms.Timer _powerPoll;
    private bool _lastOnBattery;
    private bool _lastFormFocused = true;
    private bool _batteryThrottled;
    private bool _foregroundThrottled;
    private bool _disposed;

    public ResourceAdaptation(SessionSettings userSettings, Form hostForm)
    {
        _userSettings = userSettings;
        _hostForm = hostForm;

        _hostForm.Activated   += OnFormActivated;
        _hostForm.Deactivate  += OnFormDeactivated;

        // Power status isn't event-driven in WinForms; poll every
        // 2 s. Cheap enough (reads a cached OS value).
        _powerPoll = new System.Windows.Forms.Timer { Interval = 2000 };
        _powerPoll.Tick += (_, _) => CheckPowerAndApply();
        _powerPoll.Start();

        // Prime state without triggering a spurious flip.
        _lastOnBattery = IsOnBattery();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _powerPoll.Stop();
        _powerPoll.Dispose();
        _hostForm.Activated   -= OnFormActivated;
        _hostForm.Deactivate  -= OnFormDeactivated;
        GC.SuppressFinalize(this);
    }

    private static bool IsOnBattery()
    {
        try
        {
            var ps = SystemInformation.PowerStatus;
            // BatteryChargeStatus.NoSystemBattery means it's a desktop:
            // treat as "plugged in" (never throttle).
            if (ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)) return false;
            return ps.PowerLineStatus == PowerLineStatus.Offline;
        }
        catch { return false; }
    }

    private void OnFormActivated(object? sender, EventArgs e) { _lastFormFocused = true; ApplyCurrent(); }
    private void OnFormDeactivated(object? sender, EventArgs e) { _lastFormFocused = false; ApplyCurrent(); }

    private void CheckPowerAndApply()
    {
        bool onBat = IsOnBattery();
        if (onBat == _lastOnBattery) return;
        _lastOnBattery = onBat;
        ApplyCurrent();
    }

    /// <summary>
    /// Compose the active resource budget from the user's saved
    /// settings plus the two live triggers. A throttle trigger
    /// temporarily forces Quiet; when both triggers clear we restore
    /// the user's saved preset. The original _userSettings object
    /// is never mutated — we operate on a local shadow.
    /// </summary>
    private void ApplyCurrent()
    {
        bool battery    = _userSettings.BatteryAwareQuiet          && _lastOnBattery;
        bool foreground = _userSettings.AdaptiveForegroundThrottle && !_lastFormFocused;

        if (!battery && !foreground)
        {
            // No throttle active — restore user's saved budget.
            if (_batteryThrottled || _foregroundThrottled)
            {
                ResourceBudgetSettings.ApplySettings(_userSettings);
                _batteryThrottled = false;
                _foregroundThrottled = false;
            }
            return;
        }

        // Build a shadow copy with ResourceMode = Quiet and zero'd
        // explicit caps so the preset defaults take effect.
        var shadow = new SessionSettings
        {
            ResourceMode                   = ResourceMode.Quiet,
            MaxParallelism                 = 0,
            MemoryBudget_MB                = 0,
            DemotePriorityDuringSolves     = true,
            BatteryAwareQuiet              = _userSettings.BatteryAwareQuiet,
            AdaptiveForegroundThrottle     = _userSettings.AdaptiveForegroundThrottle,
            GcLatencyTuning                = _userSettings.GcLatencyTuning,
        };
        ResourceBudgetSettings.ApplySettings(shadow);
        _batteryThrottled    = battery;
        _foregroundThrottled = foreground;
    }
}
