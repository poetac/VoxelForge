// Shared state between PicoGK task thread and Avalonia thread.
static class SpikeState
{
    public static volatile bool    AvaloniaLaunched  = false;
    public static volatile bool    AvaloniaExited    = false;
    public static           Exception? AvaloniaException = null;
    public static volatile bool    PicoGKTaskReached = false;
}
