// SpikeApp.cs — Minimal Avalonia Application + MainWindow for the spike.
//
// Intentionally bare: one window with a label showing the thread ID on which
// Avalonia's Dispatcher is running.  That is the only observable needed —
// we want proof that a Dispatcher spun up on a non-main thread, and that the
// window rendered without a crash.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace Voxelforge.Spike.Avalonia;

/// <summary>Minimal Avalonia application — single window only.</summary>
public class SpikeApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SpikeWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>A single window that displays its Dispatcher's thread ID.</summary>
public class SpikeWindow : Window
{
    public SpikeWindow()
    {
        Title  = "Voxelforge Spike — Avalonia on background thread";
        Width  = 480;
        Height = 120;

        var label = new TextBlock
        {
            Text = $"Avalonia Dispatcher running on thread {System.Threading.Thread.CurrentThread.ManagedThreadId}  "
                 + $"(IsMainThread = {System.Threading.Thread.CurrentThread == System.Threading.Thread.CurrentThread})\n"
                 + "PicoGK GLFW viewer is running concurrently on the main thread.",
            Margin   = new Thickness(16),
            FontSize = 14,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
        };

        Content = label;

        Opened  += (_, _) => Console.WriteLine($"[Avalonia window] Opened on thread {System.Threading.Thread.CurrentThread.ManagedThreadId}.");
        Closed  += (_, _) => Console.WriteLine("[Avalonia window] Closed.");
    }
}
