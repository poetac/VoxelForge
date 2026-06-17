// App.cs — Avalonia Application for the electric-propulsion viewer (Phase 1, ADR-027).
//
// Runs on Thread C (MTA background thread) while PicoGK owns the main thread.
// No App.axaml — the application setup is trivial; code-behind is sufficient.
//
// Static-pending-delegate pattern: AppBuilder.Configure<App>() calls new App()
// via the parameterless constructor, so the onGenerate callback must be injected
// via PendingOnGenerate before Configure<App>() is invoked.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Voxelforge.ElectricPropulsion;

namespace Voxelforge.Avalonia;

internal sealed class App : Application
{
    internal static Action<ResistojetConditions, ElectricPropulsionEngineDesign>? PendingOnGenerate;

    private readonly Action<ResistojetConditions, ElectricPropulsionEngineDesign> _onGenerate;

    public App()
    {
        _onGenerate = PendingOnGenerate
            ?? throw new InvalidOperationException(
                "App.PendingOnGenerate must be set before AppBuilder.Configure<App>().");
    }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ElectricPropulsionWindow(_onGenerate);
        base.OnFrameworkInitializationCompleted();
    }
}
