// KioskLog.cs — append-only diagnostic log for the trade-show kiosk.
//
// Writes one timestamped line per print + one line per error to
// `<watch-folder>/kiosk.log`. Lets the operator (or a post-show
// analysis pass) reconstruct what happened without console access —
// the WinExe build has no console window, so Console.WriteLine
// vanishes into a null stdout in interactive mode.
//
// Best-effort: a file-system failure here is logged via a sentinel
// internal flag but never throws into the caller. The kiosk should
// keep working even if `kiosk.log` becomes unwritable.

using System.IO;

namespace Voxelforge.Kiosk;

internal static class KioskLog
{
    private static readonly object s_lock = new();

    public static void Write(string watchFolder, string line)
    {
        try
        {
            Directory.CreateDirectory(watchFolder);
            var path = Path.Combine(watchFolder, "kiosk.log");
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            lock (s_lock)
            {
                File.AppendAllText(path, $"{stamp}  {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging is best-effort; never fail a build because the
            // log file is locked / disk full / etc.
        }
    }
}
