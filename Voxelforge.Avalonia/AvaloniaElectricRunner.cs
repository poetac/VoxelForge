// AvaloniaElectricRunner.cs — static launcher for the Avalonia electric-
// propulsion viewer (Phase 1, ADR-027).
//
// Called once from Program.UiThreadMain when --avalonia-electric is active.
// Starts Avalonia on a dedicated MTA background thread (Thread C per ADR-027)
// and blocks until the window's Opened event fires, guaranteeing the returned
// reference has a live HWND before the PicoGK task thread starts using it.

using System.Threading;
using Avalonia;
using Voxelforge.ElectricPropulsion;

namespace Voxelforge.Avalonia;

/// <summary>
/// Manages starting the Avalonia electric-propulsion viewer on a dedicated
/// MTA background thread per the ADR-027 threading model.
/// </summary>
public static class AvaloniaElectricRunner
{
    /// <summary>
    /// Launches the Avalonia electric-propulsion window on a new MTA thread.
    /// Blocks until the window's <c>Opened</c> event fires, then returns.
    /// </summary>
    /// <param name="onGenerate">
    /// Callback invoked on the Avalonia UI thread when the user clicks
    /// Generate. Implementations post to SharedState and return immediately.
    /// </param>
    /// <param name="onClosed">
    /// Invoked when the Avalonia window closes. Used to signal
    /// <c>Library.bContinueTask(false)</c>. Called on the Avalonia UI thread.
    /// </param>
    /// <returns>The opened <see cref="ElectricPropulsionWindow"/>.</returns>
    public static ElectricPropulsionWindow Launch(
        Action<ResistojetConditions, ElectricPropulsionEngineDesign> onGenerate,
        Action onClosed)
    {
        ArgumentNullException.ThrowIfNull(onGenerate);
        ArgumentNullException.ThrowIfNull(onClosed);

        ElectricPropulsionWindow? window    = null;
        Exception?                threadEx  = null;
        using var                 ready     = new ManualResetEventSlim(false);

        // Inject the callback before AppBuilder.Configure<App>() calls new App().
        App.PendingOnGenerate = onGenerate;

        var t = new Thread(() =>
        {
            try
            {
                AppBuilder
                    .Configure<App>()
                    .UsePlatformDetect()
                    .UseSkia()
                    .AfterSetup(b =>
                    {
                        // AfterSetup fires after OnFrameworkInitializationCompleted,
                        // so desktop.MainWindow is already set by App.
                        if (b.Instance?.ApplicationLifetime
                            is global::Avalonia.Controls.ApplicationLifetimes
                                   .IClassicDesktopStyleApplicationLifetime desktop
                            && desktop.MainWindow is ElectricPropulsionWindow w)
                        {
                            window    = w;
                            w.Opened += (_, _) => ready.Set();
                            w.Closed += (_, _) => onClosed();
                        }
                    })
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                threadEx = ex;
                ready.Set();   // unblock caller on failure
            }
        })
        {
            Name         = "AvaloniaEPUI",
            IsBackground = false,   // keep process alive while window is open (ADR-027)
        };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();

        ready.Wait();   // block until Opened fires (or thread throws)

        if (threadEx is not null)
            throw new InvalidOperationException(
                "Avalonia electric-propulsion window failed to start.", threadEx);

        return window
            ?? throw new InvalidOperationException(
                "Avalonia window was not assigned during startup — lifecycle mismatch.");
    }
}
